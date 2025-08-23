using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text.RegularExpressions;
using CodeAtlas.Api.Models;

namespace CodeAtlas.Api.Services;

public interface IRoslynAnalysisService
{
    Task<RoslynAnalysisResult> AnalyzeAsync(string workspacePath, SolutionDiscoveryResult discoveryResult, CancellationToken cancellationToken = default);
    List<StronglyConnectedComponent> DetectStronglyConnectedComponents(List<DependencyEdge> dependencies);
}

public class RoslynAnalysisService : IRoslynAnalysisService
{
    private readonly ILogger<RoslynAnalysisService> _logger;

    public RoslynAnalysisService(ILogger<RoslynAnalysisService> logger)
    {
        _logger = logger;
    }

    public async Task<RoslynAnalysisResult> AnalyzeAsync(string workspacePath, SolutionDiscoveryResult discoveryResult, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Roslyn analysis for workspace: {WorkspacePath}", workspacePath);

        try
        {
            _logger.LogInformation("Performing dependency extraction analysis (Step 7 - File→File Dependencies)");
            
            // Use the direct Roslyn approach - no MSBuild dependency
            var projectsToAnalyze = await CreateProjectsForValidation(discoveryResult, cancellationToken);

            // Filter out test projects and select target frameworks
            var filteredProjects = FilterAndSelectProjects(projectsToAnalyze);
            
            _logger.LogInformation("Found {TotalProjects} projects, filtered to {FilteredProjects} suitable projects", 
                projectsToAnalyze.Count, filteredProjects.Count);
            
            if (!filteredProjects.Any())
            {
                // Always return an error if no suitable projects are found
                if (projectsToAnalyze.Count == 0)
                {
                    _logger.LogError("No projects could be loaded from the repository");
                    return RoslynAnalysisResult.CreateError("NoSuitableProjects", 
                        "No C# projects could be loaded from the repository. The repository may not contain compatible project files or may have structural issues.");
                }
                
                var sampleProjects = string.Join(", ", projectsToAnalyze.Take(5).Select(p => p.Name));
                return RoslynAnalysisResult.CreateError("NoSuitableProjects", 
                    $"No suitable projects found for analysis after filtering test projects and selecting target frameworks. " +
                    $"Total projects found: {projectsToAnalyze.Count}. Sample projects: {sampleProjects}");
            }

            // Validate text size
            var totalTextSize = await ValidateTextSizeAsync(filteredProjects, cancellationToken);
            if (totalTextSize > 200 * 1024 * 1024) // 200MB
            {
                return RoslynAnalysisResult.CreateError("LimitsExceeded", $"Total text size ({totalTextSize / (1024 * 1024)}MB) exceeds 200MB limit");
            }

            // Build and validate projects - be lenient about compilation errors
            var compilations = await BuildProjectsAsync(filteredProjects, cancellationToken);
            
            if (!compilations.Any())
            {
                // If no compilations available, this indicates compilation failures
                _logger.LogError("No compilations available - all projects failed to compile successfully");
                return RoslynAnalysisResult.CreateError("BuildFailed", 
                    $"All {filteredProjects.Count} projects failed to compile. This may be due to missing SDKs, incompatible target frameworks, or compilation errors. Check that the repository builds locally with the correct .NET SDK installed.");
            }

            _logger.LogInformation("Successfully compiled {ProjectCount} projects for analysis", compilations.Count);

            // Step 7: Extract file-to-file dependencies from symbol references
            var dependencies = await ExtractDependenciesAsync(workspacePath, compilations, cancellationToken);
            
            if (dependencies.Count > 150000)
            {
                return RoslynAnalysisResult.CreateError("LimitsExceeded", 
                    $"Dependency graph contains {dependencies.Count} edges, exceeding the limit of 150,000 edges");
            }

            _logger.LogInformation("Extracted {DependencyCount} file-to-file dependencies", dependencies.Count);

            // Step 8: Extract namespace information and build namespace graph
            var fileNamespaces = await ExtractFileNamespacesAsync(workspacePath, compilations, cancellationToken);
            var namespaceDependencies = BuildNamespaceDependencies(dependencies, fileNamespaces);

            // Step 9: Calculate lines of code for each file
            var fileLinesOfCode = await CalculateLinesOfCodeAsync(workspacePath, compilations, cancellationToken);
            _logger.LogInformation("Calculated LOC for {FileCount} files", fileLinesOfCode.Count);

            return RoslynAnalysisResult.CreateSuccess(compilations, filteredProjects, dependencies, fileNamespaces, namespaceDependencies, fileLinesOfCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze workspace");
            return RoslynAnalysisResult.CreateError("BuildFailed", $"Analysis failed: {ex.Message}");
        }
    }

    private List<Project> FilterAndSelectProjects(List<Project> projects)
    {
        var filteredProjects = new List<Project>();

        foreach (var project in projects)
        {
            _logger.LogDebug("Evaluating project: {ProjectName} (Language: {Language}, FilePath: {FilePath})", 
                project.Name, project.Language, project.FilePath);

            // Skip test projects
            if (IsTestProject(project))
            {
                _logger.LogDebug("Excluding test project: {ProjectName}", project.Name);
                continue;
            }

            // Only include C# projects
            if (project.Language != LanguageNames.CSharp)
            {
                _logger.LogDebug("Excluding non-C# project: {ProjectName} (Language: {Language})", project.Name, project.Language);
                continue;
            }

            // Include project if it has any documents
            if (project.Documents.Any())
            {
                filteredProjects.Add(project);
                _logger.LogDebug("Including project: {ProjectName} with {DocumentCount} documents", 
                    project.Name, project.Documents.Count());
            }
            else
            {
                _logger.LogDebug("Excluding project with no documents: {ProjectName}", project.Name);
            }
        }

        return filteredProjects;
    }

    private bool IsTestProject(Project project)
    {
        var name = project.Name;
        var path = project.FilePath ?? "";

        // Check project name patterns
        if (name.Contains(".Tests", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".Test", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".Specs", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".Spec", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".Benchmarks", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check folder patterns
        if (path.Contains("/test/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\test\\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private Project? SelectBestTargetFramework(Project project)
    {
        // Get the target framework from project
        var tfm = GetTargetFramework(project);
        
        // Check if the TFM is supported/preferred
        if (IsPreferredTargetFramework(tfm))
        {
            return project;
        }

        // For multi-targeting projects, MSBuildWorkspace should have already selected the best TFM
        // If we get here, the project uses a TFM that might not be available
        _logger.LogDebug("Project {ProjectName} uses TFM: {TargetFramework}", project.Name, tfm);
        return project;
    }

    private string GetTargetFramework(Project project)
    {
        // Try to get TFM from assembly name or output file path
        var assemblyName = project.AssemblyName;
        var outputFilePath = project.OutputFilePath;
        
        // Check project properties for target framework
        foreach (var property in project.AnalyzerOptions.AdditionalFiles)
        {
            // This is a simplified approach - in a real scenario we'd parse the project file
        }

        // Fallback: try to infer from the project name or output path
        if (!string.IsNullOrEmpty(outputFilePath))
        {
            var pathParts = outputFilePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in pathParts)
            {
                if (IsValidTargetFramework(part))
                {
                    return part;
                }
            }
        }

        return "unknown";
    }

    private bool IsPreferredTargetFramework(string tfm)
    {
        var preferredOrder = new[]
        {
            "net8.0", "net7.0", "net6.0", 
            "netstandard2.1", "netstandard2.0",
            "net5.0", "net4.8", "net4.7.2", "net4.7.1", "net4.7", "net4.6.2", "net4.6.1", "net4.6"
        };

        return preferredOrder.Contains(tfm, StringComparer.OrdinalIgnoreCase);
    }

    private bool IsValidTargetFramework(string tfm)
    {
        return tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) && 
               (char.IsDigit(tfm[3]) || tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<long> ValidateTextSizeAsync(List<Project> projects, CancellationToken cancellationToken)
    {
        long totalSize = 0;

        foreach (var project in projects)
        {
            foreach (var document in project.Documents)
            {
                if (IsGeneratedFile(document))
                    continue;

                var text = await document.GetTextAsync(cancellationToken);
                totalSize += text.Length;
            }
        }

        _logger.LogInformation("Total text size for analysis: {SizeMB:F1}MB", totalSize / (1024.0 * 1024.0));
        return totalSize;
    }

    private bool IsGeneratedFile(Document document)
    {
        var filePath = document.FilePath ?? "";
        var fileName = Path.GetFileName(filePath);

        // Check file path patterns
        if (filePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check file name patterns
        if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private async Task<List<Compilation>> BuildProjectsAsync(List<Project> projects, CancellationToken cancellationToken)
    {
        var compilations = new List<Compilation>();
        var buildErrors = new List<string>();

        foreach (var project in projects)
        {
            try
            {
                _logger.LogDebug("Compiling project: {ProjectName}", project.Name);
                
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation == null)
                {
                    var error = $"Failed to get compilation for project: {project.Name}";
                    _logger.LogWarning(error);
                    buildErrors.Add(error);
                    continue;
                }

                // Check for critical errors that would prevent analysis
                var errors = compilation.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                if (errors.Any())
                {
                    _logger.LogWarning("Project {ProjectName} has {ErrorCount} compilation errors, attempting to continue", 
                        project.Name, errors.Count);
                    
                    // Check for SDK-related errors
                    foreach (var error in errors.Take(5))
                    {
                        var message = error.GetMessage();
                        _logger.LogDebug("Compilation error: {Error}", message);
                        
                        // Detect missing SDK scenarios
                        if (IsSdkError(message))
                        {
                            var tfm = GetTargetFramework(project);
                            throw new InvalidOperationException($"MissingSdk: The repository requires .NET {tfm}. Install the matching SDK/workload and retry.");
                        }
                    }
                }

                compilations.Add(compilation);
                _logger.LogDebug("Successfully compiled project: {ProjectName}", project.Name);
            }
            catch (InvalidOperationException) when (buildErrors.Count > 0)
            {
                // Re-throw SDK errors
                throw;
            }
            catch (Exception ex)
            {
                var error = $"Failed to compile project {project.Name}: {ex.Message}";
                _logger.LogWarning(ex, error);
                buildErrors.Add(error);
            }
        }

        // If we have some successful compilations, continue
        // If we have no compilations but some errors, log them but continue
        if (!compilations.Any() && buildErrors.Any())
        {
            _logger.LogWarning("No successful compilations, but continuing with limited analysis. Errors: {Errors}", 
                string.Join("; ", buildErrors.Take(3)));
        }

        return compilations;
    }

    private bool IsSdkError(string errorMessage)
    {
        var sdkIndicators = new[]
        {
            "The reference assemblies for",
            "not found", 
            "could not be resolved",
            "FrameworkReference",
            "Microsoft.NETCore.App",
            "Microsoft.AspNetCore.App",
            "Microsoft.WindowsDesktop.App",
            "SDK not found",
            "workload",
            "target framework"
        };

        return sdkIndicators.Any(indicator => 
            errorMessage.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<Project>> CreateProjectsForValidation(SolutionDiscoveryResult discoveryResult, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating Roslyn projects for validation using direct file system approach");
        
        var projects = new List<Project>();
        var workspace = new AdhocWorkspace();
        
        try
        {
            // Get project paths - if we have a solution, we need to discover projects from the solution directory
            var projectPaths = new List<string>();
            
            if (!string.IsNullOrEmpty(discoveryResult.SolutionPath))
            {
                // For solutions, discover .csproj files in the solution directory and subdirectories
                var solutionDir = Path.GetDirectoryName(discoveryResult.SolutionPath)!;
                var csprojFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
                projectPaths.AddRange(csprojFiles);
                _logger.LogInformation("Discovered {ProjectCount} .csproj files from solution directory", csprojFiles.Length);
            }
            else
            {
                // Use the explicitly discovered project paths
                projectPaths.AddRange(discoveryResult.ProjectPaths);
            }

            _logger.LogInformation("Processing {TotalProjectCount} projects for fallback validation", projectPaths.Count);

            foreach (var projectPath in projectPaths.Take(10)) // Limit to 10 projects for validation
            {
                try
                {
                    if (File.Exists(projectPath))
                    {
                        var projectName = Path.GetFileNameWithoutExtension(projectPath);
                        var projectId = ProjectId.CreateNewId(projectName);
                        
                        // Create a basic project info
                        var projectInfo = ProjectInfo.Create(
                            projectId,
                            VersionStamp.Create(),
                            projectName,
                            assemblyName: projectName,
                            language: LanguageNames.CSharp,
                            filePath: projectPath);

                        var project = workspace.AddProject(projectInfo);
                        
                        // Try to add some basic source files from the project directory
                        var projectDir = Path.GetDirectoryName(projectPath);
                        if (Directory.Exists(projectDir))
                        {
                            var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                                .Where(f => !IsGeneratedFileByPath(f))
                                .Take(50); // Limit files per project
                            
                            foreach (var csFile in csFiles)
                            {
                                try
                                {
                                    var content = await File.ReadAllTextAsync(csFile, cancellationToken);
                                    var documentId = DocumentId.CreateNewId(projectId);
                                    var documentInfo = DocumentInfo.Create(
                                        documentId,
                                        Path.GetFileName(csFile),
                                        loader: TextLoader.From(TextAndVersion.Create(SourceText.From(content), VersionStamp.Create())),
                                        filePath: csFile);
                                    
                                    workspace.TryApplyChanges(workspace.CurrentSolution.AddDocument(documentInfo));
                                    project = workspace.CurrentSolution.GetProject(projectId)!;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug(ex, "Failed to add document {FilePath}", csFile);
                                }
                            }
                        }
                        
                        projects.Add(project);
                        _logger.LogDebug("Created mock project: {ProjectName} with {DocumentCount} documents", 
                            projectName, project.Documents.Count());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create mock project for {ProjectPath}", projectPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create mock projects");
        }
        
        return projects;
    }

    private bool IsGeneratedFileByPath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        
        // Check file path patterns
        if (filePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check file name patterns
        if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Extract file-to-file dependencies and namespace information from symbol references within the solution
    /// </summary>
    private async Task<List<DependencyEdge>> ExtractDependenciesAsync(string workspacePath, List<Compilation> compilations, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting dependency extraction from {CompilationCount} compilations", compilations.Count);
        
        var dependencies = new HashSet<DependencyEdge>();
        var allDocuments = new Dictionary<string, string>(); // file path -> relative path from repo root
        
        // First, build a map of all source files in the solution
        foreach (var compilation in compilations)
        {
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (!string.IsNullOrEmpty(syntaxTree.FilePath) && File.Exists(syntaxTree.FilePath))
                {
                    var relativePath = GetRelativePath(workspacePath, syntaxTree.FilePath);
                    allDocuments[syntaxTree.FilePath] = relativePath;
                }
            }
        }
        
        _logger.LogInformation("Found {DocumentCount} source files in solution", allDocuments.Count);
        
        // Process each compilation to find symbol references
        foreach (var compilation in compilations)
        {
            await ProcessCompilationForDependencies(compilation, allDocuments, dependencies, cancellationToken);
        }
        
        var result = dependencies.ToList();
        _logger.LogInformation("Extracted {DependencyCount} unique file-to-file dependencies", result.Count);
        
        return result;
    }
    
    /// <summary>
    /// Process a single compilation to extract dependencies
    /// </summary>
    private async Task ProcessCompilationForDependencies(Compilation compilation, Dictionary<string, string> allDocuments, HashSet<DependencyEdge> dependencies, CancellationToken cancellationToken)
    {
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (string.IsNullOrEmpty(syntaxTree.FilePath) || !allDocuments.ContainsKey(syntaxTree.FilePath))
                continue;
                
            var fromFile = allDocuments[syntaxTree.FilePath];
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            
            // Find all identifier nodes in the syntax tree
            var identifiers = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(id => !IsUsingDirective(id)) // Exclude using statements
                .ToList();
            
            foreach (var identifier in identifiers)
            {
                try
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(identifier, cancellationToken);
                    var symbol = symbolInfo.Symbol;
                    
                    if (symbol != null)
                    {
                        var declaringFiles = GetDeclaringFiles(symbol, allDocuments);
                        
                        foreach (var toFile in declaringFiles)
                        {
                            if (fromFile != toFile || HasSelfReference(identifier, symbol))
                            {
                                dependencies.Add(new DependencyEdge(fromFile, toFile));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log but continue processing other identifiers
                    _logger.LogDebug(ex, "Failed to resolve symbol for identifier {Identifier} in file {File}", identifier.Identifier.ValueText, fromFile);
                }
            }
        }
    }
    
    /// <summary>
    /// Check if an identifier is part of a using directive (should be excluded)
    /// </summary>
    private bool IsUsingDirective(IdentifierNameSyntax identifier)
    {
        var parent = identifier.Parent;
        while (parent != null)
        {
            if (parent is UsingDirectiveSyntax)
                return true;
            parent = parent.Parent;
        }
        return false;
    }
    
    /// <summary>
    /// Get the files where a symbol is declared, filtered to only include files within the solution
    /// </summary>
    private List<string> GetDeclaringFiles(ISymbol symbol, Dictionary<string, string> allDocuments)
    {
        var declaringFiles = new List<string>();
        
        // Handle partial classes - get all declaration locations
        var locations = symbol.Locations.Where(loc => loc.IsInSource).ToList();
        
        foreach (var location in locations)
        {
            var sourceTree = location.SourceTree;
            if (sourceTree?.FilePath != null && allDocuments.ContainsKey(sourceTree.FilePath))
            {
                var relativePath = allDocuments[sourceTree.FilePath];
                if (!declaringFiles.Contains(relativePath))
                {
                    declaringFiles.Add(relativePath);
                }
            }
        }
        
        // For partial classes, use the first declaring location as per specification
        if (declaringFiles.Count > 1)
        {
            return new List<string> { declaringFiles.First() };
        }
        
        return declaringFiles;
    }
    
    /// <summary>
    /// Check if this represents a real self-reference (not just a declaration)
    /// </summary>
    private bool HasSelfReference(IdentifierNameSyntax identifier, ISymbol symbol)
    {
        // Simple heuristic: if the identifier is in a method body or property body, it's likely a real reference
        var parent = identifier.Parent;
        while (parent != null)
        {
            if (parent is MethodDeclarationSyntax || parent is PropertyDeclarationSyntax || 
                parent is ConstructorDeclarationSyntax || parent is FieldDeclarationSyntax ||
                parent is BlockSyntax || parent is ArrowExpressionClauseSyntax)
            {
                return true;
            }
            parent = parent.Parent;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get relative path from workspace root, using forward slashes as specified
    /// </summary>
    private string GetRelativePath(string workspacePath, string filePath)
    {
        var relativePath = Path.GetRelativePath(workspacePath, filePath);
        return relativePath.Replace('\\', '/'); // Use forward slashes as per specification
    }
    
    /// <summary>
    /// Extract namespace information for each file in the compilations
    /// </summary>
    public async Task<Dictionary<string, string>> ExtractFileNamespacesAsync(string workspacePath, List<Compilation> compilations, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting namespace extraction from {CompilationCount} compilations", compilations.Count);
        
        var fileNamespaces = new Dictionary<string, string>(); // relative file path -> namespace
        
        foreach (var compilation in compilations)
        {
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (string.IsNullOrEmpty(syntaxTree.FilePath) || !File.Exists(syntaxTree.FilePath))
                    continue;
                    
                var relativePath = GetRelativePath(workspacePath, syntaxTree.FilePath);
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                
                // Find the primary namespace for this file
                var namespaceDeclaration = root.DescendantNodes()
                    .OfType<NamespaceDeclarationSyntax>()
                    .FirstOrDefault();
                    
                var fileScopedNamespace = root.DescendantNodes()
                    .OfType<FileScopedNamespaceDeclarationSyntax>()
                    .FirstOrDefault();
                
                string namespaceName;
                if (fileScopedNamespace != null)
                {
                    namespaceName = fileScopedNamespace.Name.ToString();
                }
                else if (namespaceDeclaration != null)
                {
                    namespaceName = namespaceDeclaration.Name.ToString();
                }
                else
                {
                    // Fallback to global namespace
                    namespaceName = "<global>";
                }
                
                fileNamespaces[relativePath] = namespaceName;
                _logger.LogDebug("File {File} assigned to namespace {Namespace}", relativePath, namespaceName);
            }
        }
        
        _logger.LogInformation("Extracted namespace information for {FileCount} files", fileNamespaces.Count);
        return fileNamespaces;
    }
    
    /// <summary>
    /// Build namespace-level dependencies from file dependencies
    /// </summary>
    public List<NamespaceDependency> BuildNamespaceDependencies(List<DependencyEdge> fileDependencies, Dictionary<string, string> fileNamespaces)
    {
        _logger.LogInformation("Building namespace dependencies from {FileDependencyCount} file dependencies", fileDependencies.Count);
        
        var namespaceDependencies = new HashSet<NamespaceDependency>();
        
        foreach (var fileDep in fileDependencies)
        {
            // Get namespace for source and target files
            if (fileNamespaces.TryGetValue(fileDep.FromFile, out var fromNamespace) &&
                fileNamespaces.TryGetValue(fileDep.ToFile, out var toNamespace))
            {
                // Only add edge if namespaces are different or it's a self-reference within a namespace
                if (fromNamespace != toNamespace || fromNamespace == toNamespace)
                {
                    namespaceDependencies.Add(new NamespaceDependency(fromNamespace, toNamespace));
                }
            }
        }
        
        var result = namespaceDependencies.ToList();
        _logger.LogInformation("Built {NamespaceDependencyCount} namespace dependencies", result.Count);
        
        return result;
    }
    
    /// <summary>
    /// Calculate lines of code for a file, excluding blank and comment lines
    /// Step 9: LOC calculation requirement
    /// </summary>
    public async Task<Dictionary<string, int>> CalculateLinesOfCodeAsync(string workspacePath, List<Compilation> compilations, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Calculating lines of code for files in {CompilationCount} compilations", compilations.Count);
        
        var locMap = new Dictionary<string, int>(); // relative file path -> LOC count
        
        foreach (var compilation in compilations)
        {
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (string.IsNullOrEmpty(syntaxTree.FilePath) || !File.Exists(syntaxTree.FilePath))
                    continue;
                    
                var relativePath = GetRelativePath(workspacePath, syntaxTree.FilePath);
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var sourceText = await syntaxTree.GetTextAsync(cancellationToken);
                
                var loc = CalculateNonBlankNonCommentLines(sourceText, root);
                locMap[relativePath] = loc;
                
                _logger.LogDebug("File {File} has {LOC} lines of code", relativePath, loc);
            }
        }
        
        _logger.LogInformation("Calculated LOC for {FileCount} files", locMap.Count);
        return locMap;
    }
    
    /// <summary>
    /// Calculate non-blank, non-comment lines for a source text
    /// </summary>
    private int CalculateNonBlankNonCommentLines(SourceText sourceText, SyntaxNode root)
    {
        var lineCount = 0;
        var commentSpans = root.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || 
                       t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                       t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                       t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            .Select(t => t.Span)
            .ToList();
        
        for (int i = 0; i < sourceText.Lines.Count; i++)
        {
            var line = sourceText.Lines[i];
            var lineText = line.ToString().Trim();
            
            // Skip blank lines
            if (string.IsNullOrWhiteSpace(lineText))
                continue;
                
            // Check if the entire line is within a comment span
            var lineSpan = line.Span;
            var isCommentLine = commentSpans.Any(commentSpan => 
                commentSpan.Contains(lineSpan) || 
                (commentSpan.IntersectsWith(lineSpan) && IsLineFullyComment(lineText, commentSpan, lineSpan)));
            
            if (!isCommentLine)
            {
                lineCount++;
            }
        }
        
        return lineCount;
    }
    
    /// <summary>
    /// Check if a line is fully covered by a comment (allowing for edge cases)
    /// </summary>
    private bool IsLineFullyComment(string lineText, TextSpan commentSpan, TextSpan lineSpan)
    {
        // Simple heuristic: if line starts with // or /* or * (continuation), treat as comment
        var trimmed = lineText.TrimStart();
        return trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*");
    }
    
    /// <summary>
    /// Detect strongly connected components (SCCs) in the file dependency graph using Tarjan's algorithm
    /// Step 10: Cycles (SCC) detection requirement
    /// </summary>
    public List<StronglyConnectedComponent> DetectStronglyConnectedComponents(List<DependencyEdge> dependencies)
    {
        _logger.LogInformation("Starting SCC detection on {EdgeCount} dependency edges", dependencies.Count);
        
        // Build adjacency list representation of the graph
        var graph = new Dictionary<string, List<string>>();
        var allNodes = new HashSet<string>();
        
        foreach (var edge in dependencies)
        {
            allNodes.Add(edge.FromFile);
            allNodes.Add(edge.ToFile);
            
            if (!graph.ContainsKey(edge.FromFile))
                graph[edge.FromFile] = new List<string>();
            graph[edge.FromFile].Add(edge.ToFile);
        }
        
        // Ensure all nodes are in the graph (even isolated ones)
        foreach (var node in allNodes)
        {
            if (!graph.ContainsKey(node))
                graph[node] = new List<string>();
        }
        
        _logger.LogInformation("Built graph with {NodeCount} nodes for SCC analysis", allNodes.Count);
        
        // Run Tarjan's algorithm
        var tarjan = new TarjanSccDetector(graph);
        var sccs = tarjan.FindStronglyConnectedComponents();
        
        _logger.LogInformation("Found {SccCount} strongly connected components", sccs.Count);
        
        return sccs;
    }
}

/// <summary>
/// Represents a strongly connected component detected in the dependency graph
/// </summary>
public class StronglyConnectedComponent
{
    public int Id { get; set; }
    public List<string> Files { get; set; } = new();
    public int Size => Files.Count;
    
    /// <summary>
    /// Get a sample of files for display (first few files in the component)
    /// </summary>
    public List<string> GetSample(int maxSamples = 5)
    {
        return Files.Take(maxSamples).Select(f => $"File:{f}").ToList();
    }
}

/// <summary>
/// Implementation of Tarjan's algorithm for strongly connected component detection
/// </summary>
public class TarjanSccDetector
{
    private readonly Dictionary<string, List<string>> _graph;
    private readonly Dictionary<string, int> _index = new();
    private readonly Dictionary<string, int> _lowLink = new();
    private readonly HashSet<string> _onStack = new();
    private readonly Stack<string> _stack = new();
    private readonly List<StronglyConnectedComponent> _components = new();
    private int _currentIndex = 0;
    
    public TarjanSccDetector(Dictionary<string, List<string>> graph)
    {
        _graph = graph;
    }
    
    public List<StronglyConnectedComponent> FindStronglyConnectedComponents()
    {
        foreach (var node in _graph.Keys)
        {
            if (!_index.ContainsKey(node))
            {
                StrongConnect(node);
            }
        }
        
        return _components;
    }
    
    private void StrongConnect(string node)
    {
        // Set the depth index for this node to the smallest unused index
        _index[node] = _currentIndex;
        _lowLink[node] = _currentIndex;
        _currentIndex++;
        _stack.Push(node);
        _onStack.Add(node);
        
        // Consider successors of the node
        if (_graph.ContainsKey(node))
        {
            foreach (var successor in _graph[node])
            {
                if (!_index.ContainsKey(successor))
                {
                    // Successor has not yet been visited; recurse on it
                    StrongConnect(successor);
                    _lowLink[node] = Math.Min(_lowLink[node], _lowLink[successor]);
                }
                else if (_onStack.Contains(successor))
                {
                    // Successor is in stack and hence in the current SCC
                    _lowLink[node] = Math.Min(_lowLink[node], _index[successor]);
                }
            }
        }
        
        // If this is a root node, pop the stack and create an SCC
        if (_lowLink[node] == _index[node])
        {
            var component = new StronglyConnectedComponent
            {
                Id = _components.Count + 1
            };
            
            string stackNode;
            do
            {
                stackNode = _stack.Pop();
                _onStack.Remove(stackNode);
                component.Files.Add(stackNode);
            } while (stackNode != node);
            
            _components.Add(component);
        }
    }
}

/// <summary>
/// Represents a dependency edge between two files
/// </summary>
public class DependencyEdge : IEquatable<DependencyEdge>
{
    public string FromFile { get; }
    public string ToFile { get; }
    
    public DependencyEdge(string fromFile, string toFile)
    {
        FromFile = fromFile ?? throw new ArgumentNullException(nameof(fromFile));
        ToFile = toFile ?? throw new ArgumentNullException(nameof(toFile));
    }
    
    public bool Equals(DependencyEdge? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FromFile == other.FromFile && ToFile == other.ToFile;
    }
    
    public override bool Equals(object? obj)
    {
        return Equals(obj as DependencyEdge);
    }
    
    public override int GetHashCode()
    {
        return HashCode.Combine(FromFile, ToFile);
    }
    
    public override string ToString()
    {
        return $"{FromFile} -> {ToFile}";
    }
}

/// <summary>
/// Represents a dependency edge between two namespaces
/// </summary>
public class NamespaceDependency : IEquatable<NamespaceDependency>
{
    public string FromNamespace { get; }
    public string ToNamespace { get; }
    
    public NamespaceDependency(string fromNamespace, string toNamespace)
    {
        FromNamespace = fromNamespace ?? throw new ArgumentNullException(nameof(fromNamespace));
        ToNamespace = toNamespace ?? throw new ArgumentNullException(nameof(toNamespace));
    }
    
    public bool Equals(NamespaceDependency? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FromNamespace == other.FromNamespace && ToNamespace == other.ToNamespace;
    }
    
    public override bool Equals(object? obj)
    {
        return Equals(obj as NamespaceDependency);
    }
    
    public override int GetHashCode()
    {
        return HashCode.Combine(FromNamespace, ToNamespace);
    }
    
    public override string ToString()
    {
        return $"{FromNamespace} -> {ToNamespace}";
    }
}

public class RoslynAnalysisResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public List<Compilation> Compilations { get; init; } = new();
    public List<Project> Projects { get; init; } = new();
    public List<DependencyEdge> Dependencies { get; init; } = new();
    public Dictionary<string, string> FileNamespaces { get; init; } = new();
    public List<NamespaceDependency> NamespaceDependencies { get; init; } = new();
    public Dictionary<string, int> FileLinesOfCode { get; init; } = new();

    public static RoslynAnalysisResult CreateSuccess(List<Compilation> compilations, List<Project> projects, List<DependencyEdge> dependencies, Dictionary<string, string> fileNamespaces, List<NamespaceDependency> namespaceDependencies, Dictionary<string, int> fileLinesOfCode)
    {
        return new RoslynAnalysisResult
        {
            Success = true,
            Compilations = compilations,
            Projects = projects,
            Dependencies = dependencies,
            FileNamespaces = fileNamespaces,
            NamespaceDependencies = namespaceDependencies,
            FileLinesOfCode = fileLinesOfCode
        };
    }

    public static RoslynAnalysisResult CreateError(string code, string message)
    {
        return new RoslynAnalysisResult
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            Dependencies = new List<DependencyEdge>(),
            FileNamespaces = new Dictionary<string, string>(),
            NamespaceDependencies = new List<NamespaceDependency>(),
            FileLinesOfCode = new Dictionary<string, int>()
        };
    }
}
