// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO for one operating point inside a legacy polar-dump file.
// Differences: No direct Fortran analogue exists because the legacy dump writer streamed operating-point values directly to file rather than constructing nested objects.
// Decision: Keep the managed DTO because it makes dump import/export explicit and strongly typed.
namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpOperatingPoint
{
    // Legacy mapping: none; managed-only constructor for one dump operating-point record.
    // Difference from legacy: The Fortran workflow had formatted output statements instead of an immutable object with validated children.
    // Decision: Keep the managed constructor because it is the right import/export boundary.
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
