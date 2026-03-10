namespace XFoil.IO.Models;

public sealed class AnalysisSessionRunResult
{
    public string SessionName { get; init; } = string.Empty;

    public string GeometryName { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public IReadOnlyList<AnalysisSessionArtifact> Artifacts { get; init; } = Array.Empty<AnalysisSessionArtifact>();

    public string SummaryPath { get; init; } = string.Empty;
}
