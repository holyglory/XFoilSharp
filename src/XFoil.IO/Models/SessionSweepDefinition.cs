// Legacy audit:
// Primary legacy source: none
// Role in port: Managed-only DTO describing one requested sweep in an analysis session manifest.
// Differences: No direct Fortran analogue exists because this batch/session abstraction is part of the .NET automation layer rather than the legacy interactive workflow.
// Decision: Keep the managed DTO because it is the right way to represent scripted sweep inputs.
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
