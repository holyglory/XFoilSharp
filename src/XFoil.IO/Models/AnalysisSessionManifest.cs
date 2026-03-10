namespace XFoil.IO.Models;

public sealed class AnalysisSessionManifest
{
    public string? Name { get; init; }

    public SessionAirfoilDefinition? Airfoil { get; init; }

    public IReadOnlyList<SessionSweepDefinition> Sweeps { get; init; } = Array.Empty<SessionSweepDefinition>();
}
