using System.Diagnostics;
using CodeAtlas.Api.Models;

namespace CodeAtlas.Api.Services;

public interface IGitService
{
    Task<string> CloneRepositoryAsync(string repoUrl, string? branch, CancellationToken cancellationToken);
    void CleanupTempDirectory(string tempPath);
    int CountCSharpFiles(string repoPath);
    SolutionDiscoveryResult DiscoverSolutionOrProjects(string repoPath);
    string? GetCommitHash(string repoPath);
    (List<string> detectedLanguages, List<string> foundFiles) DetectRepositoryDetails(string repoPath);
}

public class SolutionDiscoveryResult
{
    public bool Success { get; set; }
    public string? SolutionPath { get; set; }
    public List<string> ProjectPaths { get; set; } = new();
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;
    private readonly string _tempRoot;
    private readonly string _gitPath;

    public GitService(ILogger<GitService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _tempRoot = configuration["APP__TEMP_ROOT"] ?? Path.GetTempPath();
        _gitPath = configuration["APP__GIT_PATH"] ?? "git";
    }

    public async Task<string> CloneRepositoryAsync(string repoUrl, string? branch, CancellationToken cancellationToken)
    {
        // Normalize the repository URL to ensure it has the correct protocol
        var normalizedUrl = NormalizeRepositoryUrl(repoUrl);
        _logger.LogInformation("Original URL: {OriginalUrl}, Normalized URL: {NormalizedUrl}", repoUrl, normalizedUrl);
        
        // Create unique temp folder for this request
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var tempDir = Path.Combine(_tempRoot, $"codeatlas-{uniqueId}");
        var repoPath = Path.Combine(tempDir, "repo");

        _logger.LogInformation("Created temp directory: {TempDir}", tempDir);

        try
        {
            // Create the temp directory but not the final repo directory (git clone will create it)
            Directory.CreateDirectory(tempDir);

            // Build git clone command with shallow clone flags and long path support
            var arguments = new List<string>
            {
                "clone",
                "--depth=1",
                "--single-branch",
                "--no-tags",
                "--config", "core.longpaths=true"
            };

            if (!string.IsNullOrEmpty(branch))
            {
                arguments.AddRange(new[] { "--branch", branch });
            }

            arguments.Add(normalizedUrl);
            arguments.Add(repoPath);

            var finalArgs = string.Join(" ", arguments.Select(a => a.Contains(" ") ? $"\"{a}\"" : a));
            _logger.LogInformation("Running git clone with args: {Args}", string.Join(" ", arguments));
            _logger.LogInformation("Final command: {GitPath} {FinalArgs}", _gitPath, finalArgs);
            _logger.LogInformation("Working directory exists: {WorkingDirExists}", Directory.Exists(tempDir));
            _logger.LogInformation("Target repo path: {RepoPath}", repoPath);
            _logger.LogInformation("Target repo path exists before clone: {RepoPathExists}", Directory.Exists(repoPath));

            using var process = new Process();
            process.StartInfo.FileName = _gitPath;
            process.StartInfo.Arguments = finalArgs;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WorkingDirectory = tempDir; // Set working directory

            var output = new List<string>();
            var error = new List<string>();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.Add(e.Data);
                    _logger.LogInformation("Git stdout: {Output}", e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    error.Add(e.Data);
                    _logger.LogWarning("Git stderr: {Error}", e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for process to complete with 120s timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(combinedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts.Token.IsCancellationRequested)
                {
                    _logger.LogError("Git clone timed out after 120 seconds");
                    throw new TimeoutException("Git clone operation timed out after 120 seconds");
                }
                throw;
            }

            _logger.LogInformation("Git process completed with exit code: {ExitCode}", process.ExitCode);
            _logger.LogInformation("Git stdout output count: {OutputCount}", output.Count);
            _logger.LogInformation("Git stderr output count: {ErrorCount}", error.Count);
            
            if (output.Any())
            {
                _logger.LogInformation("Git stdout: {Output}", string.Join(Environment.NewLine, output));
            }
            
            if (error.Any())
            {
                _logger.LogWarning("Git stderr: {Error}", string.Join(Environment.NewLine, error));
            }

            if (process.ExitCode != 0)
            {
                var errorMessage = string.Join(Environment.NewLine, error);
                
                // Check if this is a partial success (clone succeeded but checkout failed due to long paths)
                var isPartialSuccess = process.ExitCode == 128 && 
                                     error.Any(e => e.Contains("Clone succeeded, but checkout failed")) &&
                                     error.Any(e => e.Contains("Filename too long"));
                
                if (isPartialSuccess)
                {
                    _logger.LogWarning("Git clone had partial success - some files couldn't be checked out due to long paths, but most files are available");
                    _logger.LogWarning("Exit code 128 with checkout failure: {Error}", errorMessage.Substring(0, Math.Min(500, errorMessage.Length)));
                    // Continue processing - the repository is mostly cloned
                }
                else
                {
                    _logger.LogError("Git clone failed with exit code {ExitCode}: {Error}", process.ExitCode, errorMessage);
                    _logger.LogError("Full command that failed: {GitPath} {FinalArgs}", _gitPath, finalArgs);
                    throw new InvalidOperationException($"Could not clone the repository. Ensure the URL is public and reachable. Details: {errorMessage}");
                }
            }

            _logger.LogInformation("Checking if repo was cloned successfully...");
            _logger.LogInformation("Target repo path exists after clone: {RepoPathExists}", Directory.Exists(repoPath));
            
            if (Directory.Exists(repoPath))
            {
                var filesInRepo = Directory.GetFiles(repoPath, "*", SearchOption.AllDirectories).Length;
                _logger.LogInformation("Files found in cloned repo: {FileCount}", filesInRepo);
            }

            _logger.LogInformation("Successfully cloned repository to {RepoPath}", repoPath);
            return repoPath;
        }
        catch
        {
            // Clean up on failure
            CleanupTempDirectory(tempDir);
            throw;
        }
    }

    public void CleanupTempDirectory(string tempPath)
    {
        try
        {
            if (Directory.Exists(tempPath))
            {
                _logger.LogInformation("Cleaning up temp directory: {TempPath}", tempPath);
                
                // First try to remove read-only attributes from git files
                try
                {
                    var gitDir = Path.Combine(tempPath, ".git");
                    if (Directory.Exists(gitDir))
                    {
                        RemoveReadOnlyAttributes(gitDir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to remove read-only attributes from git files");
                }
                
                Directory.Delete(tempPath, recursive: true);
                _logger.LogDebug("Successfully deleted temp directory: {TempPath}", tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp directory: {TempPath}. This is not critical and files will be cleaned up by OS temp cleanup.", tempPath);
        }
    }

    private void RemoveReadOnlyAttributes(string directoryPath)
    {
        try
        {
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                    }
                }
                catch
                {
                    // Ignore individual file errors
                }
            }

            var directories = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories);
            foreach (var dir in directories)
            {
                try
                {
                    var attributes = File.GetAttributes(dir);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(dir, attributes & ~FileAttributes.ReadOnly);
                    }
                }
                catch
                {
                    // Ignore individual directory errors
                }
            }
        }
        catch
        {
            // Ignore any errors in cleanup
        }
    }

    public int CountCSharpFiles(string repoPath)
    {
        _logger.LogInformation("Counting C# files in path: {RepoPath}", repoPath);
        _logger.LogInformation("Directory exists: {DirectoryExists}", Directory.Exists(repoPath));
        
        if (!Directory.Exists(repoPath))
        {
            _logger.LogWarning("Repository path does not exist: {RepoPath}", repoPath);
            return 0;
        }

        try
        {
            var allFiles = Directory.GetFiles(repoPath, "*", SearchOption.AllDirectories);
            _logger.LogInformation("Total files found: {TotalFiles}", allFiles.Length);
            
            var csFiles = Directory.GetFiles(repoPath, "*.cs", SearchOption.AllDirectories);
            var count = csFiles.Length;
            _logger.LogInformation("Found {Count} C# files in repository", count);
            
            if (count > 0)
            {
                _logger.LogInformation("Sample C# files: {SampleFiles}", string.Join(", ", csFiles.Take(5).Select(Path.GetFileName)));
            }
            
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count C# files in {RepoPath}", repoPath);
            return 0;
        }
    }

    private string NormalizeRepositoryUrl(string repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            return repoUrl;

        var trimmedUrl = repoUrl.Trim();

        // If URL already has a protocol, return as-is
        if (trimmedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmedUrl.StartsWith("git://", StringComparison.OrdinalIgnoreCase) ||
            trimmedUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmedUrl;
        }

        // If URL starts with git@, it's SSH format, return as-is
        if (trimmedUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            return trimmedUrl;
        }

        // If URL looks like github.com/user/repo or github.com/user/repo.git
        // Add https:// prefix
        if (trimmedUrl.Contains("github.com/") || 
            trimmedUrl.Contains("gitlab.com/") || 
            trimmedUrl.Contains("bitbucket.org/") ||
            (trimmedUrl.Contains("/") && trimmedUrl.Contains(".")))
        {
            return $"https://{trimmedUrl}";
        }

        // Default: assume it needs https:// prefix
        return $"https://{trimmedUrl}";
    }

    public SolutionDiscoveryResult DiscoverSolutionOrProjects(string repoPath)
    {
        _logger.LogInformation("Starting solution/project discovery in path: {RepoPath}", repoPath);
        
        if (!Directory.Exists(repoPath))
        {
            _logger.LogError("Repository path does not exist: {RepoPath}", repoPath);
            return new SolutionDiscoveryResult
            {
                Success = false,
                ErrorCode = "NoSolutionOrProject",
                ErrorMessage = "Repository path does not exist after cloning."
            };
        }

        try
        {
            // Step 1: Look for solution at repo root
            var rootSolutionFiles = Directory.GetFiles(repoPath, "*.sln", SearchOption.TopDirectoryOnly);
            if (rootSolutionFiles.Length > 0)
            {
                var chosenSolution = rootSolutionFiles[0];
                _logger.LogInformation("Found solution at repo root: {SolutionPath}", chosenSolution);
                
                if (rootSolutionFiles.Length > 1)
                {
                    _logger.LogInformation("Multiple solutions found at root, chose first: {ChosenSolution}. Available: {AllSolutions}", 
                        chosenSolution, string.Join(", ", rootSolutionFiles.Select(Path.GetFileName)));
                }
                
                return new SolutionDiscoveryResult
                {
                    Success = true,
                    SolutionPath = chosenSolution
                };
            }

            // Step 2: Perform DFS to find first .sln file in subdirectories
            var allSolutionFiles = Directory.GetFiles(repoPath, "*.sln", SearchOption.AllDirectories);
            if (allSolutionFiles.Length > 0)
            {
                // Sort to get consistent DFS ordering
                Array.Sort(allSolutionFiles);
                var chosenSolution = allSolutionFiles[0];
                
                _logger.LogInformation("Found solution via DFS: {SolutionPath}", chosenSolution);
                
                if (allSolutionFiles.Length > 1)
                {
                    _logger.LogInformation("Multiple solutions found, chose first by DFS: {ChosenSolution}. Available: {AllSolutions}",
                        chosenSolution, string.Join(", ", allSolutionFiles.Select(s => Path.GetRelativePath(repoPath, s))));
                }
                
                return new SolutionDiscoveryResult
                {
                    Success = true,
                    SolutionPath = chosenSolution
                };
            }

            // Step 3: Enumerate all .csproj files
            var projectFiles = Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories);
            if (projectFiles.Length > 0)
            {
                _logger.LogInformation("No solution files found, using {ProjectCount} project files: {ProjectFiles}",
                    projectFiles.Length, string.Join(", ", projectFiles.Take(5).Select(p => Path.GetRelativePath(repoPath, p))));
                
                if (projectFiles.Length > 5)
                {
                    _logger.LogInformation("... and {AdditionalCount} more project files", projectFiles.Length - 5);
                }
                
                return new SolutionDiscoveryResult
                {
                    Success = true,
                    ProjectPaths = projectFiles.ToList()
                };
            }

            // Step 4: No .sln or .csproj found - detect what type of repository this actually is
            _logger.LogWarning("No .sln or .csproj files found in repository: {RepoPath}", repoPath);
            
            // Detect what type of repository this might be
            var detectedType = DetectRepositoryType(repoPath);
            var specificMessage = detectedType switch
            {
                "TypeScript/JavaScript" => $"This appears to be a TypeScript/JavaScript repository (found package.json, tsconfig.json, or .js/.ts files). CodeAtlas only analyzes C# repositories with .sln or .csproj files.",
                "Python" => $"This appears to be a Python repository (found .py files or requirements.txt). CodeAtlas only analyzes C# repositories with .sln or .csproj files.",
                "Java" => $"This appears to be a Java repository (found .java files or pom.xml). CodeAtlas only analyzes C# repositories with .sln or .csproj files.",
                "Go" => $"This appears to be a Go repository (found .go files or go.mod). CodeAtlas only analyzes C# repositories with .sln or .csproj files.",
                "Rust" => $"This appears to be a Rust repository (found .rs files or Cargo.toml). CodeAtlas only analyzes C# repositories with .sln or .csproj files.",
                "C++" => $"This appears to be a C++ repository (found .cpp/.h files or CMakeLists.txt). CodeAtlas only analyzes C# repositories with .sln or .csproj files.",
                "Documentation" => $"This appears to be a documentation repository (found only .md, .txt, or config files). CodeAtlas requires C# repositories with .sln or .csproj files.",
                _ => "No C# solution (.sln) or project (.csproj) files found. CodeAtlas requires a C# repository with compilable projects."
            };
            
            return new SolutionDiscoveryResult
            {
                Success = false,
                ErrorCode = "NoSolutionOrProject",
                ErrorMessage = specificMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during solution/project discovery in {RepoPath}", repoPath);
            return new SolutionDiscoveryResult
            {
                Success = false,
                ErrorCode = "NoSolutionOrProject",
                ErrorMessage = $"Error scanning repository for C# projects: {ex.Message}"
            };
        }
    }

    public string? GetCommitHash(string repoPath)
    {
        try
        {
            _logger.LogDebug("Getting commit hash for repository: {RepoPath}", repoPath);
            
            var processInfo = new ProcessStartInfo
            {
                FileName = _gitPath,
                Arguments = "rev-parse HEAD",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process for commit hash");
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                _logger.LogDebug("Retrieved commit hash: {CommitHash}", output);
                return output;
            }
            else
            {
                _logger.LogWarning("Failed to get commit hash. Exit code: {ExitCode}, Error: {Error}", process.ExitCode, error);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting commit hash for repository: {RepoPath}", repoPath);
            return null;
        }
    }

    public (List<string> detectedLanguages, List<string> foundFiles) DetectRepositoryDetails(string repoPath)
    {
        var detectedLanguages = new List<string>();
        var foundFiles = new List<string>();

        try
        {
            // Check for various project types by looking for characteristic files
            var allFiles = Directory.GetFiles(repoPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetFileName(f).ToLowerInvariant())
                .ToList();
            
            var extensions = Directory.GetFiles(repoPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetExtension(f).ToLowerInvariant())
                .Where(ext => !string.IsNullOrEmpty(ext))
                .ToHashSet();

            // TypeScript/JavaScript detection
            if (allFiles.Contains("package.json") || allFiles.Contains("tsconfig.json") || 
                extensions.Contains(".ts") || extensions.Contains(".js") || extensions.Contains(".tsx") || extensions.Contains(".jsx"))
            {
                detectedLanguages.Add("TypeScript/JavaScript");
                var tsFiles = new List<string>();
                if (allFiles.Contains("package.json")) tsFiles.Add("package.json");
                if (allFiles.Contains("tsconfig.json")) tsFiles.Add("tsconfig.json");
                if (extensions.Contains(".ts") || extensions.Contains(".tsx")) tsFiles.Add(".ts files");
                if (extensions.Contains(".js") || extensions.Contains(".jsx")) tsFiles.Add(".js files");
                if (tsFiles.Any()) foundFiles.Add(string.Join(", ", tsFiles.Take(1)));
            }

            // Python detection
            if (allFiles.Contains("requirements.txt") || allFiles.Contains("setup.py") || allFiles.Contains("pyproject.toml") ||
                extensions.Contains(".py"))
            {
                detectedLanguages.Add("Python");
                if (allFiles.Contains("requirements.txt")) foundFiles.Add("requirements.txt");
                else if (allFiles.Contains("setup.py")) foundFiles.Add("setup.py");
                else if (allFiles.Contains("pyproject.toml")) foundFiles.Add("pyproject.toml");
                else if (extensions.Contains(".py")) foundFiles.Add(".py files");
            }

            // Java detection
            if (allFiles.Contains("pom.xml") || allFiles.Contains("build.gradle") || extensions.Contains(".java"))
            {
                detectedLanguages.Add("Java");
                if (allFiles.Contains("pom.xml")) foundFiles.Add("pom.xml");
                else if (allFiles.Contains("build.gradle")) foundFiles.Add("build.gradle");
                else if (extensions.Contains(".java")) foundFiles.Add(".java files");
            }

            // Go detection
            if (allFiles.Contains("go.mod") || allFiles.Contains("go.sum") || extensions.Contains(".go"))
            {
                detectedLanguages.Add("Go");
                if (allFiles.Contains("go.mod")) foundFiles.Add("go.mod");
                else if (allFiles.Contains("go.sum")) foundFiles.Add("go.sum");
                else if (extensions.Contains(".go")) foundFiles.Add(".go files");
            }

            // Rust detection
            if (allFiles.Contains("cargo.toml") || extensions.Contains(".rs"))
            {
                detectedLanguages.Add("Rust");
                if (allFiles.Contains("cargo.toml")) foundFiles.Add("Cargo.toml");
                else if (extensions.Contains(".rs")) foundFiles.Add(".rs files");
            }

            // C++ detection
            if (allFiles.Contains("cmakelists.txt") || allFiles.Contains("makefile") || 
                extensions.Contains(".cpp") || extensions.Contains(".cc") || extensions.Contains(".cxx") || extensions.Contains(".h") || extensions.Contains(".hpp"))
            {
                detectedLanguages.Add("C++");
                if (allFiles.Contains("cmakelists.txt")) foundFiles.Add("CMakeLists.txt");
                else if (allFiles.Contains("makefile")) foundFiles.Add("Makefile");
                else if (extensions.Contains(".cpp") || extensions.Contains(".cc") || extensions.Contains(".cxx")) foundFiles.Add(".cpp files");
                else if (extensions.Contains(".h") || extensions.Contains(".hpp")) foundFiles.Add(".h files");
            }

            // C# detection (for completeness)
            if (allFiles.Any(f => f.EndsWith(".sln")) || allFiles.Any(f => f.EndsWith(".csproj")) || extensions.Contains(".cs"))
            {
                detectedLanguages.Add("C#");
                if (allFiles.Any(f => f.EndsWith(".sln"))) foundFiles.Add(".sln files");
                else if (allFiles.Any(f => f.EndsWith(".csproj"))) foundFiles.Add(".csproj files");
                else if (extensions.Contains(".cs")) foundFiles.Add(".cs files");
            }

            // Additional popular languages
            if (extensions.Contains(".php"))
            {
                detectedLanguages.Add("PHP");
                foundFiles.Add(".php files");
            }

            if (extensions.Contains(".rb") || allFiles.Contains("gemfile"))
            {
                detectedLanguages.Add("Ruby");
                if (allFiles.Contains("gemfile")) foundFiles.Add("Gemfile");
                else foundFiles.Add(".rb files");
            }

            if (extensions.Contains(".swift"))
            {
                detectedLanguages.Add("Swift");
                foundFiles.Add(".swift files");
            }

            if (extensions.Contains(".kt") || extensions.Contains(".kts"))
            {
                detectedLanguages.Add("Kotlin");
                foundFiles.Add(".kt files");
            }

            if (extensions.Contains(".scala"))
            {
                detectedLanguages.Add("Scala");
                foundFiles.Add(".scala files");
            }

            if (extensions.Contains(".dart") || allFiles.Contains("pubspec.yaml"))
            {
                detectedLanguages.Add("Dart");
                if (allFiles.Contains("pubspec.yaml")) foundFiles.Add("pubspec.yaml");
                else foundFiles.Add(".dart files");
            }

            if (extensions.Contains(".r"))
            {
                detectedLanguages.Add("R");
                foundFiles.Add(".r files");
            }

            if (extensions.Contains(".m") || extensions.Contains(".mm"))
            {
                detectedLanguages.Add("Objective-C");
                foundFiles.Add(".m files");
            }

            if (extensions.Contains(".pl") || extensions.Contains(".pm"))
            {
                detectedLanguages.Add("Perl");
                foundFiles.Add(".pl files");
            }

            if (extensions.Contains(".lua"))
            {
                detectedLanguages.Add("Lua");
                foundFiles.Add(".lua files");
            }

            if (extensions.Contains(".sh") || extensions.Contains(".bash"))
            {
                detectedLanguages.Add("Shell");
                foundFiles.Add("shell scripts");
            }

            if (extensions.Contains(".ps1"))
            {
                detectedLanguages.Add("PowerShell");
                foundFiles.Add(".ps1 files");
            }

            if (allFiles.Contains("dockerfile") || allFiles.Any(f => f.StartsWith("dockerfile")))
            {
                detectedLanguages.Add("Docker");
                foundFiles.Add("Dockerfile");
            }

            // Documentation repository detection
            var codeExtensions = new[] { ".cs", ".ts", ".js", ".py", ".java", ".go", ".rs", ".cpp", ".c", ".h", ".php", ".rb", ".swift", ".kt", ".scala", ".dart" };
            var hasCodeFiles = extensions.Any(ext => codeExtensions.Contains(ext));
            
            if (!hasCodeFiles && (extensions.Contains(".md") || extensions.Contains(".txt") || extensions.Contains(".rst")))
            {
                detectedLanguages.Add("Documentation");
                if (extensions.Contains(".md")) foundFiles.Add(".md files");
                else if (extensions.Contains(".txt")) foundFiles.Add(".txt files");
                else if (extensions.Contains(".rst")) foundFiles.Add(".rst files");
            }

            return (detectedLanguages, foundFiles);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error detecting repository details for {RepoPath}", repoPath);
            return (new List<string> { "Unknown" }, new List<string> { "Unable to scan files" });
        }
    }

    private string DetectRepositoryType(string repoPath)
    {
        var (detectedLanguages, _) = DetectRepositoryDetails(repoPath);
        return detectedLanguages.FirstOrDefault() ?? "Unknown";
    }
}
