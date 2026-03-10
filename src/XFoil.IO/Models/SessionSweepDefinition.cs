namespace XFoil.IO.Models;

public sealed class SessionSweepDefinition
{
    public string? Name { get; init; }

    public string? Kind { get; init; }

    public string? InputPath { get; init; }

    public string? Output { get; init; }

    public double Start { get; init; }

    public double End { get; init; }

    public double Step { get; init; }

    public int? PanelCount { get; init; }

    public double? MachNumber { get; init; }

    public double? ReynoldsNumber { get; init; }

    public int? CouplingIterations { get; init; }

    public int? ViscousIterations { get; init; }

    public double? ResidualTolerance { get; init; }

    public double? DisplacementRelaxation { get; init; }

    public double? TransitionReynoldsTheta { get; init; }

    public double? CriticalAmplificationFactor { get; init; }
}
