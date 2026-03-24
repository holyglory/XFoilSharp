// Legacy audit:
// Primary legacy source: none
// Role in port: Managed-only DTO describing an analysis session manifest consumed by the .NET session runner.
// Differences: No direct Fortran analogue exists because batch session configuration is a managed API addition rather than a legacy XFoil data structure.
// Decision: Keep the managed DTO because it cleanly represents session inputs.
namespace XFoil.IO.Models;

public sealed class AnalysisSessionManifest
{
    public string? Name { get; init; }

    public SessionAirfoilDefinition? Airfoil { get; init; }

    public IReadOnlyList<SessionSweepDefinition> Sweeps { get; init; } = Array.Empty<SessionSweepDefinition>();
}
