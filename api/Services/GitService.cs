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
                ErrorMessage = "No `.sln` or `.csproj` found in the repository. Provide a C# solution/project repo or specify a path to the `.sln`."
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

            // Step 4: No .sln or .csproj found
            _logger.LogWarning("No .sln or .csproj files found in repository: {RepoPath}", repoPath);
            return new SolutionDiscoveryResult
            {
                Success = false,
                ErrorCode = "NoSolutionOrProject",
                ErrorMessage = "No `.sln` or `.csproj` found in the repository. Provide a C# solution/project repo or specify a path to the `.sln`."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during solution/project discovery in {RepoPath}", repoPath);
            return new SolutionDiscoveryResult
            {
                Success = false,
                ErrorCode = "NoSolutionOrProject",
                ErrorMessage = "No `.sln` or `.csproj` found in the repository. Provide a C# solution/project repo or specify a path to the `.sln`."
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
}
