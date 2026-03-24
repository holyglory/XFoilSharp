// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO for one sample line within a legacy polar-dump side section.
// Differences: No direct Fortran analogue exists because the legacy dump writer produced raw table rows rather than typed sample objects.
// Decision: Keep the managed DTO because it is the natural in-memory form for imported dump samples.
namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpSideSample
{
    // Legacy mapping: none; managed-only value-object constructor for one dump sample row.
    // Difference from legacy: The original output was raw numeric text rather than a structured object.
    // Decision: Keep the managed constructor because it matches the importer/exporter boundary.
    public LegacyPolarDumpSideSample(double x, double pressureCoefficient, double momentumThickness, double displacementThickness, double skinFrictionCoefficient, double shearLagCoefficient)
    {
        X = x;
        PressureCoefficient = pressureCoefficient;
        MomentumThickness = momentumThickness;
        DisplacementThickness = displacementThickness;
        SkinFrictionCoefficient = skinFrictionCoefficient;
        ShearLagCoefficient = shearLagCoefficient;
    }

    public double X { get; }

    public double PressureCoefficient { get; }

    public double MomentumThickness { get; }

    public double DisplacementThickness { get; }

    public double SkinFrictionCoefficient { get; }

    public double ShearLagCoefficient { get; }
}
