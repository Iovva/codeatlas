namespace CodeAtlas.Api.Models;

public class AnalyzeResponse
{
    public required Meta Meta { get; set; }
    public required Graphs Graphs { get; set; }
    public required Metrics Metrics { get; set; }
    public required List<Cycle> Cycles { get; set; }
}

public class Meta
{
    public required string Repo { get; set; }
    public string? Branch { get; set; }
    public string? Commit { get; set; }
    public required DateTime GeneratedAt { get; set; }
}

public class Graphs
{
    public required Graph Namespace { get; set; }
    public required Graph File { get; set; }
}

public class Graph
{
    public required List<Node> Nodes { get; set; }
    public required List<Edge> Edges { get; set; }
}

public class Node
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public int Loc { get; set; }
    public int FanIn { get; set; }
    public int FanOut { get; set; }
}

public class Edge
{
    public required string From { get; set; }
    public required string To { get; set; }
}

public class Metrics
{
    public required Counts Counts { get; set; }
    public required List<Node> FanInTop { get; set; }
    public required List<Node> FanOutTop { get; set; }
}

public class Counts
{
    public int NamespaceNodes { get; set; }
    public int FileNodes { get; set; }
    public int Edges { get; set; }
}

public class Cycle
{
    public int Id { get; set; }
    public int Size { get; set; }
    public required List<string> Sample { get; set; }
}
