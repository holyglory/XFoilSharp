// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xoper.f :: saved-polar operating-point reporting lineage
// Role in port: Managed DTO for one viscous polar point reported by the older displacement-coupled workflow.
// Differences: The managed port packages the same headline quantities into an immutable object instead of relying on print buffers and temporary scalars.
// Decision: Keep the managed DTO because it simplifies exports and tests.
namespace XFoil.Solver.Models;

public sealed class ViscousPolarPoint
{
    public ViscousPolarPoint(
        double angleOfAttackDegrees,
        double liftCoefficient,
        double estimatedProfileDragCoefficient,
        double momentCoefficientQuarterChord,
        double finalSurfaceResidual,
        double finalTransitionResidual,
        double finalWakeResidual,
        bool outerConverged,
        bool innerInteractionConverged,
        double finalDisplacementRelaxation,
        double finalSeedEdgeVelocityChange)
    {
        AngleOfAttackDegrees = angleOfAttackDegrees;
        LiftCoefficient = liftCoefficient;
        EstimatedProfileDragCoefficient = estimatedProfileDragCoefficient;
        MomentCoefficientQuarterChord = momentCoefficientQuarterChord;
        FinalSurfaceResidual = finalSurfaceResidual;
        FinalTransitionResidual = finalTransitionResidual;
        FinalWakeResidual = finalWakeResidual;
        OuterConverged = outerConverged;
        InnerInteractionConverged = innerInteractionConverged;
        FinalDisplacementRelaxation = finalDisplacementRelaxation;
        FinalSeedEdgeVelocityChange = finalSeedEdgeVelocityChange;
    }

    public double AngleOfAttackDegrees { get; }

    public double LiftCoefficient { get; }

    public double EstimatedProfileDragCoefficient { get; }

    public double MomentCoefficientQuarterChord { get; }

    public double FinalSurfaceResidual { get; }

    public double FinalTransitionResidual { get; }

    public double FinalWakeResidual { get; }

    public bool OuterConverged { get; }

    public bool InnerInteractionConverged { get; }

    public double FinalDisplacementRelaxation { get; }

    public double FinalSeedEdgeVelocityChange { get; }
}
