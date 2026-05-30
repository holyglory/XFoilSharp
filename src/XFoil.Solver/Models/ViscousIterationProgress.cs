// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: VISCAL iteration progress lineage
// Role in port: Lightweight per-iteration progress notification for watchdogs and UI status.
// Differences: Legacy XFoil printed/progressed through COMMON state; the managed port can expose typed progress without changing result parity.
// Decision: Keep this callback payload diagnostics-only so solver math and stored results remain unchanged.
namespace XFoil.Solver.Models;

/// <summary>
/// Progress notification emitted after a viscous Newton iteration advances.
/// </summary>
public readonly record struct ViscousIterationProgress
{
    public int Iteration { get; init; }

    public int MaxIterations { get; init; }

    public double AlphaRadians { get; init; }

    public double RmsResidual { get; init; }

    public double RelaxationFactor { get; init; }

    public double CL { get; init; }

    public double CD { get; init; }

    public double CM { get; init; }
}
