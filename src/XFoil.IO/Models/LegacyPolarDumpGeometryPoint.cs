// Legacy audit:
// Primary legacy source: none
// Role in port: Managed DTO for one geometry sample stored in a legacy polar-dump export.
// Differences: No direct Fortran analogue exists because the legacy code emitted geometry coordinates directly during file writing.
// Decision: Keep the managed DTO because it simplifies dump import/export handling.
namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpGeometryPoint
{
    // Legacy mapping: none; managed-only value-object constructor for one dump geometry point.
    // Difference from legacy: The original workflow had raw coordinate lines, not named objects.
    // Decision: Keep the managed constructor because it is the appropriate DTO boundary.
    public LegacyPolarDumpGeometryPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}
