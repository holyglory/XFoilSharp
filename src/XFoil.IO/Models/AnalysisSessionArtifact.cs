// Legacy audit:
// Primary legacy source: none
// Role in port: Managed-only DTO describing one artifact emitted by the .NET analysis-session runner.
// Differences: No direct Fortran analogue exists because the legacy workflow wrote files procedurally and did not package them into named result objects.
// Decision: Keep the managed DTO because it is the right boundary for session output metadata.
namespace XFoil.IO.Models;

public sealed class AnalysisSessionArtifact
{
    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public int PointCount { get; init; }
}
