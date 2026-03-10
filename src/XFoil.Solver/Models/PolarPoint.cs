namespace XFoil.Solver.Models;

public sealed class PolarPoint
{
    public PolarPoint(
        double angleOfAttackDegrees,
        double liftCoefficient,
        double dragCoefficient,
        double correctedPressureIntegratedLiftCoefficient,
        double correctedPressureIntegratedDragCoefficient,
        double momentCoefficientQuarterChord,
        double circulation,
        double pressureIntegratedLiftCoefficient,
        double pressureIntegratedDragCoefficient)
    {
        AngleOfAttackDegrees = angleOfAttackDegrees;
        LiftCoefficient = liftCoefficient;
        DragCoefficient = dragCoefficient;
        CorrectedPressureIntegratedLiftCoefficient = correctedPressureIntegratedLiftCoefficient;
        CorrectedPressureIntegratedDragCoefficient = correctedPressureIntegratedDragCoefficient;
        MomentCoefficientQuarterChord = momentCoefficientQuarterChord;
        Circulation = circulation;
        PressureIntegratedLiftCoefficient = pressureIntegratedLiftCoefficient;
        PressureIntegratedDragCoefficient = pressureIntegratedDragCoefficient;
    }

    public double AngleOfAttackDegrees { get; }

    public double LiftCoefficient { get; }

    public double DragCoefficient { get; }

    public double CorrectedPressureIntegratedLiftCoefficient { get; }

    public double CorrectedPressureIntegratedDragCoefficient { get; }

    public double MomentCoefficientQuarterChord { get; }

    public double Circulation { get; }

    public double PressureIntegratedLiftCoefficient { get; }

    public double PressureIntegratedDragCoefficient { get; }
}
