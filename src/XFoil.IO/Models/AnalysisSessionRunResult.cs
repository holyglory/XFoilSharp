// Legacy audit:
// Primary legacy source: none
// Role in port: Managed-only DTO summarizing one completed analysis-session run.
// Differences: No direct Fortran analogue exists because the legacy workflow reported output files procedurally instead of returning a structured run-result object.
// Decision: Keep the managed DTO because it is a better API surface for automation and tests.
namespace XFoil.IO.Models;

public sealed class AnalysisSessionRunResult
{
    public string SessionName { get; init; } = string.Empty;

    public string GeometryName { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public IReadOnlyList<AnalysisSessionArtifact> Artifacts { get; init; } = Array.Empty<AnalysisSessionArtifact>();

    public string SummaryPath { get; init; } = string.Empty;
}
