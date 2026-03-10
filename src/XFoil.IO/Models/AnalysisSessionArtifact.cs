namespace XFoil.IO.Models;

public sealed class AnalysisSessionArtifact
{
    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public int PointCount { get; init; }
}
