// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: none
// Role in port: Managed result container for the older displacement-coupled surrogate workflow retained for compatibility/documentation.
// Differences: The legacy runtime had no matching immutable result object, and the original surrogate workflow was a managed experiment rather than a direct port.
// Decision: Keep the DTO because it documents and transports the historical managed workflow cleanly.
using XFoil.Core.Models;

namespace XFoil.Solver.Models;

public sealed class DisplacementCoupledResult
{
    public DisplacementCoupledResult(
        InviscidAnalysisResult initialAnalysis,
        InviscidAnalysisResult finalAnalysis,
        ViscousSolveResult finalSolveResult,
        AirfoilGeometry displacedGeometry,
        int iterations,
        double maxSurfaceDisplacement,
        double estimatedProfileDragCoefficient,
        bool converged,
        int innerInteractionIterations,
        bool innerInteractionConverged,
        double finalSeedEdgeVelocityChange,
        double finalDisplacementRelaxation,
        double finalLiftDelta,
        double finalMomentDelta)
    {
        InitialAnalysis = initialAnalysis;
        FinalAnalysis = finalAnalysis;
        FinalSolveResult = finalSolveResult;
        DisplacedGeometry = displacedGeometry;
        Iterations = iterations;
        MaxSurfaceDisplacement = maxSurfaceDisplacement;
        EstimatedProfileDragCoefficient = estimatedProfileDragCoefficient;
        Converged = converged;
        InnerInteractionIterations = innerInteractionIterations;
        InnerInteractionConverged = innerInteractionConverged;
        FinalSeedEdgeVelocityChange = finalSeedEdgeVelocityChange;
        FinalDisplacementRelaxation = finalDisplacementRelaxation;
        FinalLiftDelta = finalLiftDelta;
        FinalMomentDelta = finalMomentDelta;
    }

    public InviscidAnalysisResult InitialAnalysis { get; }

    public InviscidAnalysisResult FinalAnalysis { get; }

    public ViscousSolveResult FinalSolveResult { get; }

    public AirfoilGeometry DisplacedGeometry { get; }

    public int Iterations { get; }

    public double MaxSurfaceDisplacement { get; }

    public double EstimatedProfileDragCoefficient { get; }

    public bool Converged { get; }

    public int InnerInteractionIterations { get; }

    public bool InnerInteractionConverged { get; }

    public double FinalSeedEdgeVelocityChange { get; }

    public double FinalDisplacementRelaxation { get; }

    public double FinalLiftDelta { get; }

    public double FinalMomentDelta { get; }
}
