using System.Text.Json;
using CodeAtlas.Api.Models;

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

// POST /analyze endpoint with schema-correct empty response
app.MapPost("/analyze", (AnalyzeRequest request) =>
{
    // For now, return empty but schema-correct payload
    var response = new AnalyzeResponse
    {
        Meta = new Meta
        {
            Repo = request.RepoUrl,
            Branch = request.Branch,
            Commit = null,
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
    
    return Results.Ok(response);
})
.WithName("AnalyzeRepository")
.WithOpenApi();

app.Run();
