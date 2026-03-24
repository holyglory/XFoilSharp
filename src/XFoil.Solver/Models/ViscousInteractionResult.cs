// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: none
// Role in port: Managed result container for the older outer viscous-interaction loop.
// Differences: The object packages a managed surrogate workflow rather than mirroring a direct legacy runtime result type.
// Decision: Keep the DTO because it captures historical managed behavior explicitly.
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
