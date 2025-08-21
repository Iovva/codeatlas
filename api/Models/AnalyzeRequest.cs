namespace CodeAtlas.Api.Models;

public class AnalyzeRequest
{
    public required string RepoUrl { get; set; }
    public string? Branch { get; set; }
}
