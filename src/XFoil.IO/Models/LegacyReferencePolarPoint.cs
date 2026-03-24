// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO for one point inside a reference-polar comparison block.
// Differences: No direct Fortran analogue exists because this comparison-file structure belongs to the managed tooling layer.
// Decision: Keep the managed DTO because it is the natural import representation.
namespace XFoil.IO.Models;

public sealed class LegacyReferencePolarPoint
{
    // Legacy mapping: none; managed-only value-object constructor for one reference-polar point.
    // Difference from legacy: The original runtime did not represent comparison points as named objects.
    // Decision: Keep the managed constructor because it is the appropriate DTO boundary.
    public LegacyReferencePolarPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}
