// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xbl.f :: viscous Newton solve convergence lineage
// Role in port: Managed result container for one solve of the older interval-based viscous system.
// Differences: The managed port packages initial/final systems and residual summaries explicitly instead of leaving them transient in solver locals.
// Decision: Keep the managed DTO because it preserves useful diagnostics for tests and tooling.
namespace XFoil.Solver.Models;

public sealed class ViscousSolveResult
{
    public ViscousSolveResult(
        ViscousIntervalSystem initialSystem,
        ViscousIntervalSystem solvedSystem,
        int iterations,
        bool converged,
        double initialSurfaceResidual,
        double finalSurfaceResidual,
        double initialTransitionResidual,
        double finalTransitionResidual,
        double initialWakeResidual,
        double finalWakeResidual)
    {
        InitialSystem = initialSystem;
        SolvedSystem = solvedSystem;
        Iterations = iterations;
        Converged = converged;
        InitialSurfaceResidual = initialSurfaceResidual;
        FinalSurfaceResidual = finalSurfaceResidual;
        InitialTransitionResidual = initialTransitionResidual;
        FinalTransitionResidual = finalTransitionResidual;
        InitialWakeResidual = initialWakeResidual;
        FinalWakeResidual = finalWakeResidual;
    }

    public ViscousIntervalSystem InitialSystem { get; }

    public ViscousIntervalSystem SolvedSystem { get; }

    public int Iterations { get; }

    public bool Converged { get; }

    public double InitialSurfaceResidual { get; }

    public double FinalSurfaceResidual { get; }

    public double InitialTransitionResidual { get; }

    public double FinalTransitionResidual { get; }

    public double InitialWakeResidual { get; }

    public double FinalWakeResidual { get; }
}
