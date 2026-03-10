namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpOperatingPoint
{
    public LegacyPolarDumpOperatingPoint(
        double angleOfAttackDegrees,
        double liftCoefficient,
        double dragCoefficient,
        double storedDragComponentCoefficient,
        double momentCoefficientQuarterChord,
        double topTransition,
        double bottomTransition,
        double machNumber,
        IReadOnlyList<LegacyPolarDumpSide> sides)
    {
        AngleOfAttackDegrees = angleOfAttackDegrees;
        LiftCoefficient = liftCoefficient;
        DragCoefficient = dragCoefficient;
        StoredDragComponentCoefficient = storedDragComponentCoefficient;
        MomentCoefficientQuarterChord = momentCoefficientQuarterChord;
        TopTransition = topTransition;
        BottomTransition = bottomTransition;
        MachNumber = machNumber;
        Sides = sides ?? throw new ArgumentNullException(nameof(sides));
    }

    public double AngleOfAttackDegrees { get; }

    public double LiftCoefficient { get; }

    public double DragCoefficient { get; }

    public double StoredDragComponentCoefficient { get; }

    public double MomentCoefficientQuarterChord { get; }

    public double TopTransition { get; }

    public double BottomTransition { get; }

    public double MachNumber { get; }

    public IReadOnlyList<LegacyPolarDumpSide> Sides { get; }
}
