using System.Text.Json;
using CodeAtlas.Api.Models;
using CodeAtlas.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization to camelCase with UTC timestamps
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.WriteIndented = true;
});

// Configure CORS to allow localhost:4200
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IGitService, GitService>();
builder.Services.AddScoped<IRoslynAnalysisService, RoslynAnalysisService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable CORS
app.UseCors();

// Health endpoint that returns JSON with status and CodeAtlas text
app.MapGet("/health", () =>
{
    var response = new
    {
        Status = "Healthy",
        Service = "CodeAtlas",
        Timestamp = DateTime.UtcNow,
        Message = "CodeAtlas API is running successfully"
    };
    return Results.Ok(response);
})
.WithName("GetHealth")
.WithOpenApi();

// POST /analyze endpoint with temp workspace and shallow clone
// Helper function to generate top N metrics for nodes
static List<Node> GenerateTopMetrics(IEnumerable<Node> nodes, Func<Node, int> selector, int topN)
{
    return nodes
        .Where(n => selector(n) > 0) // Only include nodes with positive values
        .OrderByDescending(selector)
        .Take(topN)
        .ToList();
}

app.MapPost("/analyze", async (AnalyzeRequest request, IGitService gitService, IRoslynAnalysisService roslynService, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    string? tempPath = null;
    
    try
    {
        logger.LogInformation("Starting analysis for repository: {RepoUrl}", request.RepoUrl);
        
        // Step 1: Clone repository to temp workspace
        tempPath = await gitService.CloneRepositoryAsync(request.RepoUrl, request.Branch, cancellationToken);
        
        // Step 2: Count C# files and check limits
        var csFileCount = gitService.CountCSharpFiles(tempPath);
        if (csFileCount > 100000)
        {
            logger.LogWarning("Repository has {Count} C# files, exceeding limit of 100,000", csFileCount);
            return Results.Json(
                new ErrorResponse 
                { 
                    Code = "LimitsExceeded", 
                    Message = $"Repository contains {csFileCount} C# files, exceeding the limit of 100,000 files" 
                }, 
                statusCode: 413);
        }
        
        logger.LogInformation("Repository contains {Count} C# files, within limits", csFileCount);
        
        // Step 3: Discover solution or project files
        var discoveryResult = gitService.DiscoverSolutionOrProjects(tempPath);
        if (!discoveryResult.Success)
        {
            logger.LogWarning("No solution or project files found in repository: {RepoUrl}", request.RepoUrl);
            
            // Get repository details for better error reporting
            var (detectedLanguages, foundFiles) = gitService.DetectRepositoryDetails(tempPath);
            
            logger.LogInformation("Detected languages: {Languages}, Found files: {Files}", 
                string.Join(", ", detectedLanguages), string.Join(", ", foundFiles));
            
            var errorResponse = new ErrorResponse 
            { 
                Code = discoveryResult.ErrorCode!, 
                Message = discoveryResult.ErrorMessage!,
                DetectedLanguages = detectedLanguages,
                FoundFiles = foundFiles
            };
            
            logger.LogInformation("Returning error response: {ErrorResponse}", System.Text.Json.JsonSerializer.Serialize(errorResponse));
            
            return Results.Json(errorResponse, statusCode: 400);
        }
        
        if (!string.IsNullOrEmpty(discoveryResult.SolutionPath))
        {
            logger.LogInformation("Using solution file: {SolutionPath}", discoveryResult.SolutionPath);
        }
        else if (discoveryResult.ProjectPaths.Count > 0)
        {
            logger.LogInformation("Using {ProjectCount} project files", discoveryResult.ProjectPaths.Count);
        }
        
        // Step 4: Perform Roslyn analysis (Build + TFM + Exclude Tests)
        var analysisResult = await roslynService.AnalyzeAsync(tempPath, discoveryResult, cancellationToken);
        if (!analysisResult.Success)
        {
            logger.LogError("Roslyn analysis failed: {ErrorCode} - {ErrorMessage}", analysisResult.ErrorCode, analysisResult.ErrorMessage);
            
            var statusCode = analysisResult.ErrorCode switch
            {
                "MissingSdk" => 412,
                "BuildFailed" => 424,
                "LimitsExceeded" => 413,
                "NoSuitableProjects" => 422, // Unprocessable Entity
                _ => 500
            };
            
            return Results.Json(
                new ErrorResponse 
                { 
                    Code = analysisResult.ErrorCode!, 
                    Message = analysisResult.ErrorMessage! 
                }, 
                statusCode: statusCode);
        }
        
        logger.LogInformation("Roslyn analysis completed successfully with {CompilationCount} compilations and {DependencyCount} dependencies", 
            analysisResult.Compilations.Count, analysisResult.Dependencies.Count);
        
        // Step 10: Detect strongly connected components (cycles) in the file graph
        var sccs = roslynService.DetectStronglyConnectedComponents(analysisResult.Dependencies);
        var cycles = sccs
            .Where(scc => scc.Size >= 2) // Only include actual cycles (size >= 2)
            .Select(scc => new Cycle
            {
                Id = scc.Id,
                Size = scc.Size,
                Sample = scc.GetSample()
            })
            .ToList();
        
        logger.LogInformation("Detected {CycleCount} cycles from {SccCount} strongly connected components", cycles.Count, sccs.Count);
        
        // Get commit hash for the repository
        var commitHash = gitService.GetCommitHash(tempPath);
        
        // Step 7: Build file graph from extracted dependencies
        var fileNodes = new List<Node>();
        var fileEdges = new List<Edge>();
        
        // Create file nodes with canonical IDs (File:<RELATIVE_PATH_FROM_REPO_ROOT>)
        var allFiles = analysisResult.Dependencies
            .SelectMany(d => new[] { d.FromFile, d.ToFile })
            .Distinct()
            .OrderBy(f => f)
            .ToList();
            
        // Step 9: Calculate fan-in and fan-out for file nodes
        var fileFanIn = new Dictionary<string, int>();
        var fileFanOut = new Dictionary<string, int>();
        
        foreach (var file in allFiles)
        {
            fileFanIn[file] = analysisResult.Dependencies.Count(d => d.ToFile == file);
            fileFanOut[file] = analysisResult.Dependencies.Count(d => d.FromFile == file);
        }
            
        foreach (var file in allFiles)
        {
            var nodeId = $"File:{file}";
            var fileName = Path.GetFileName(file);
            var loc = analysisResult.FileLinesOfCode.GetValueOrDefault(file, 0);
            
            fileNodes.Add(new Node
            {
                Id = nodeId,
                Label = fileName,
                Loc = loc,
                FanIn = fileFanIn.GetValueOrDefault(file, 0),
                FanOut = fileFanOut.GetValueOrDefault(file, 0)
            });
        }
        
        // Create file edges from dependencies
        foreach (var dependency in analysisResult.Dependencies)
        {
            fileEdges.Add(new Edge
            {
                From = $"File:{dependency.FromFile}",
                To = $"File:{dependency.ToFile}"
            });
        }
        
        // Step 8: Build namespace graph with canonical IDs (Namespace:<Fully.Qualified.Name>)
        var namespaceNodes = new List<Node>();
        var namespaceEdges = new List<Edge>();
        
        // Create namespace nodes from unique namespaces
        var allNamespaces = analysisResult.NamespaceDependencies
            .SelectMany(d => new[] { d.FromNamespace, d.ToNamespace })
            .Distinct()
            .OrderBy(ns => ns)
            .ToList();
            
        // Step 9: Calculate fan-in and fan-out for namespace nodes, and aggregate LOC
        var namespaceFanIn = new Dictionary<string, int>();
        var namespaceFanOut = new Dictionary<string, int>();
        var namespaceLoc = new Dictionary<string, int>();
        
        foreach (var namespaceName in allNamespaces)
        {
            namespaceFanIn[namespaceName] = analysisResult.NamespaceDependencies.Count(d => d.ToNamespace == namespaceName);
            namespaceFanOut[namespaceName] = analysisResult.NamespaceDependencies.Count(d => d.FromNamespace == namespaceName);
            
            // Aggregate LOC from all files in this namespace
            var filesInNamespace = analysisResult.FileNamespaces
                .Where(kvp => kvp.Value == namespaceName)
                .Select(kvp => kvp.Key);
            namespaceLoc[namespaceName] = filesInNamespace.Sum(file => analysisResult.FileLinesOfCode.GetValueOrDefault(file, 0));
        }
            
        foreach (var namespaceName in allNamespaces)
        {
            var nodeId = $"Namespace:{namespaceName}";
            var displayName = namespaceName == "<global>" ? "(global)" : namespaceName.Split('.').Last();
            
            namespaceNodes.Add(new Node
            {
                Id = nodeId,
                Label = displayName,
                Loc = namespaceLoc.GetValueOrDefault(namespaceName, 0),
                FanIn = namespaceFanIn.GetValueOrDefault(namespaceName, 0),
                FanOut = namespaceFanOut.GetValueOrDefault(namespaceName, 0)
            });
        }
        
        // Create namespace edges from dependencies
        foreach (var dependency in analysisResult.NamespaceDependencies)
        {
            namespaceEdges.Add(new Edge
            {
                From = $"Namespace:{dependency.FromNamespace}",
                To = $"Namespace:{dependency.ToNamespace}"
            });
        }
        
        var response = new AnalyzeResponse
        {
            Meta = new Meta
            {
                Repo = request.RepoUrl,
                Branch = request.Branch,
                Commit = commitHash,
                GeneratedAt = DateTime.UtcNow
            },
            Graphs = new Graphs
            {
                Namespace = new Graph
                {
                    Nodes = namespaceNodes,
                    Edges = namespaceEdges
                },
                File = new Graph
                {
                    Nodes = fileNodes,
                    Edges = fileEdges
                }
            },
            Metrics = new Metrics
            {
                Counts = new Counts
                {
                    NamespaceNodes = namespaceNodes.Count,
                    FileNodes = fileNodes.Count,
                    Edges = fileEdges.Count + namespaceEdges.Count
                },
                FanInTop = GenerateTopMetrics(fileNodes.Concat(namespaceNodes), n => n.FanIn, 5),
                FanOutTop = GenerateTopMetrics(fileNodes.Concat(namespaceNodes), n => n.FanOut, 5)
            },
            Cycles = cycles
        };
        
        logger.LogInformation("Analysis completed successfully for repository: {RepoUrl}", request.RepoUrl);
        return Results.Ok(response);
    }
    catch (TimeoutException)
    {
        logger.LogError("Analysis timed out for repository: {RepoUrl}", request.RepoUrl);
        return Results.Json(
            new ErrorResponse 
            { 
                Code = "Timeout", 
                Message = "Repository analysis timed out after 120 seconds" 
            }, 
            statusCode: 504);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("Could not clone"))
    {
        logger.LogError(ex, "Failed to clone repository: {RepoUrl}", request.RepoUrl);
        
        // Extract the actual git error from the exception message
        var gitError = ex.Message.Replace("Could not clone the repository. Ensure the URL is public and reachable. Details: ", "");
        
        // Provide specific error messages based on common git clone failures
        string specificMessage;
        if (gitError.Contains("Repository not found") || gitError.Contains("not found"))
        {
            specificMessage = $"Repository '{request.RepoUrl}' does not exist or is not publicly accessible.";
        }
        else if (gitError.Contains("Permission denied") || gitError.Contains("authentication"))
        {
            specificMessage = $"Access denied to repository '{request.RepoUrl}'. Private repositories are not supported.";
        }
        else if (gitError.Contains("timeout") || gitError.Contains("timed out"))
        {
            specificMessage = $"Connection timeout while cloning '{request.RepoUrl}'. Please try again later.";
        }
        else if (gitError.Contains("network") || gitError.Contains("connection"))
        {
            specificMessage = $"Network error while accessing '{request.RepoUrl}'. Check your internet connection.";
        }
        else
        {
            specificMessage = $"Failed to clone '{request.RepoUrl}': {gitError.Trim()}";
        }
        
        return Results.Json(
            new ErrorResponse 
            { 
                Code = "CloneFailed", 
                Message = specificMessage
            }, 
            statusCode: 502);
    }
    catch (InvalidOperationException ex) when (ex.Message.StartsWith("MissingSdk"))
    {
        logger.LogError(ex, "Missing SDK for repository: {RepoUrl}", request.RepoUrl);
        return Results.Json(
            new ErrorResponse 
            { 
                Code = "MissingSdk", 
                Message = ex.Message.Substring("MissingSdk: ".Length) 
            }, 
            statusCode: 412);
    }
    catch (InvalidOperationException ex) when (ex.Message.StartsWith("BuildFailed"))
    {
        logger.LogError(ex, "Build failed for repository: {RepoUrl}", request.RepoUrl);
        return Results.Json(
            new ErrorResponse 
            { 
                Code = "BuildFailed", 
                Message = ex.Message.Substring("BuildFailed: ".Length) 
            }, 
            statusCode: 424);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error during analysis of repository: {RepoUrl}", request.RepoUrl);
        return Results.Json(
            new ErrorResponse 
            { 
                Code = "InternalError", 
                Message = "An unexpected error occurred during analysis" 
            }, 
            statusCode: 500);
    }
    finally
    {
        // Always cleanup temp directory
        if (!string.IsNullOrEmpty(tempPath))
        {
            gitService.CleanupTempDirectory(tempPath);
        }
    }
})
.WithName("AnalyzeRepository")
.WithOpenApi();

app.Run();
