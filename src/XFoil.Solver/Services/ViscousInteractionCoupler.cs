using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class ViscousInteractionCoupler
{
    private const double InteractionConvergenceThreshold = 0.01d;
    private readonly EdgeVelocityFeedbackBuilder edgeVelocityFeedbackBuilder = new();
    private readonly ViscousStateEstimator viscousStateEstimator = new();
    private readonly ViscousIntervalSystemBuilder viscousIntervalSystemBuilder = new();
    private readonly ViscousLaminarSolver viscousLaminarSolver = new();

    public ViscousInteractionResult Couple(
        ViscousStateSeed initialSeed,
        AnalysisSettings settings,
        int interactionIterations = 3,
        double couplingFactor = 0.12d,
        int viscousIterations = 8,
        double residualTolerance = 0.3d)
    {
        if (initialSeed is null)
        {
            throw new ArgumentNullException(nameof(initialSeed));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (interactionIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(interactionIterations), "Interaction iteration count must be positive.");
        }

        if (couplingFactor <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(couplingFactor), "Coupling factor must be positive.");
        }

        var seed = initialSeed;
        ViscousSolveResult? solveResult = null;
        var lastIterationRelativeEdgeVelocityChange = double.PositiveInfinity;
        var executedIterations = 0;
        var converged = false;

        for (var iteration = 0; iteration < interactionIterations; iteration++)
        {
            var state = viscousStateEstimator.Estimate(seed, settings);
            var system = viscousIntervalSystemBuilder.Build(state, settings);
            solveResult = viscousLaminarSolver.Solve(system, settings, viscousIterations, residualTolerance);
            executedIterations = iteration + 1;

            if (iteration > 0
                && solveResult.Converged
                && solveResult.FinalTransitionResidual <= residualTolerance
                && lastIterationRelativeEdgeVelocityChange <= InteractionConvergenceThreshold)
            {
                converged = true;
                break;
            }

            if (iteration < interactionIterations - 1)
            {
                var updatedSeed = edgeVelocityFeedbackBuilder.ApplyDisplacementFeedback(seed, solveResult.SolvedSystem.State, couplingFactor);
                lastIterationRelativeEdgeVelocityChange = edgeVelocityFeedbackBuilder.ComputeAverageRelativeEdgeVelocityChange(seed, updatedSeed);
                seed = updatedSeed;
            }
        }

        if (!converged
            && solveResult is not null
            && solveResult.Converged
            && solveResult.FinalTransitionResidual <= residualTolerance
            && (executedIterations == 1 || lastIterationRelativeEdgeVelocityChange <= InteractionConvergenceThreshold))
        {
            converged = true;
        }

        return new ViscousInteractionResult(
            initialSeed,
            seed,
            solveResult ?? throw new InvalidOperationException("Viscous interaction solve did not execute."),
            executedIterations,
            converged,
            edgeVelocityFeedbackBuilder.ComputeAverageRelativeEdgeVelocityChange(initialSeed, seed),
            double.IsFinite(lastIterationRelativeEdgeVelocityChange) ? lastIterationRelativeEdgeVelocityChange : 0d);
    }
}
