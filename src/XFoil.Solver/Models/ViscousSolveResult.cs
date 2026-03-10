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
