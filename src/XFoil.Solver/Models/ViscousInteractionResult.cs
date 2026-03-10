namespace XFoil.Solver.Models;

public sealed class ViscousInteractionResult
{
    public ViscousInteractionResult(
        ViscousStateSeed initialSeed,
        ViscousStateSeed finalSeed,
        ViscousSolveResult solveResult,
        int interactionIterations,
        bool converged,
        double averageRelativeEdgeVelocityChange,
        double finalIterationRelativeEdgeVelocityChange)
    {
        InitialSeed = initialSeed;
        FinalSeed = finalSeed;
        SolveResult = solveResult;
        InteractionIterations = interactionIterations;
        Converged = converged;
        AverageRelativeEdgeVelocityChange = averageRelativeEdgeVelocityChange;
        FinalIterationRelativeEdgeVelocityChange = finalIterationRelativeEdgeVelocityChange;
    }

    public ViscousStateSeed InitialSeed { get; }

    public ViscousStateSeed FinalSeed { get; }

    public ViscousSolveResult SolveResult { get; }

    public int InteractionIterations { get; }

    public bool Converged { get; }

    public double AverageRelativeEdgeVelocityChange { get; }

    public double FinalIterationRelativeEdgeVelocityChange { get; }
}
