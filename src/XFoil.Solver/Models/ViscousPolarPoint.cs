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
