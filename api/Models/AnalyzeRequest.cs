namespace CodeAtlas.Api.Models;

public class AnalyzeRequest
{
    public required string RepoUrl { get; set; }
    public string? Branch { get; set; }
}

public class ErrorResponse
{
    public required string Code { get; set; }
    public required string Message { get; set; }
}