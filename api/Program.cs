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
app.MapPost("/analyze", async (AnalyzeRequest request, IGitService gitService, ILogger<Program> logger, CancellationToken cancellationToken) =>
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
            return Results.Json(
                new ErrorResponse 
                { 
                    Code = discoveryResult.ErrorCode!, 
                    Message = discoveryResult.ErrorMessage! 
                }, 
                statusCode: 400);
        }
        
        if (!string.IsNullOrEmpty(discoveryResult.SolutionPath))
        {
            logger.LogInformation("Using solution file: {SolutionPath}", discoveryResult.SolutionPath);
        }
        else if (discoveryResult.ProjectPaths.Count > 0)
        {
            logger.LogInformation("Using {ProjectCount} project files", discoveryResult.ProjectPaths.Count);
        }
        
        // For now, return empty but schema-correct payload (actual analysis will be implemented in later steps)
        var response = new AnalyzeResponse
        {
            Meta = new Meta
            {
                Repo = request.RepoUrl,
                Branch = request.Branch,
                Commit = null, // Will be populated when we implement git commit detection
                GeneratedAt = DateTime.UtcNow
            },
            Graphs = new Graphs
            {
                Namespace = new Graph
                {
                    Nodes = new List<Node>(),
                    Edges = new List<Edge>()
                },
                File = new Graph
                {
                    Nodes = new List<Node>(),
                    Edges = new List<Edge>()
                }
            },
            Metrics = new Metrics
            {
                Counts = new Counts
                {
                    NamespaceNodes = 0,
                    FileNodes = 0,
                    Edges = 0
                },
                FanInTop = new List<Node>(),
                FanOutTop = new List<Node>()
            },
            Cycles = new List<Cycle>()
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
        return Results.Json(
            new ErrorResponse 
            { 
                Code = "CloneFailed", 
                Message = "Could not clone the repository. Ensure the URL is public and reachable." 
            }, 
            statusCode: 502);
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
