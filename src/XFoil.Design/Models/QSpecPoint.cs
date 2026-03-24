using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xqdes.f :: QSPEC/SSPEC arrays
// Role in port: Managed representation of one sampled QSpec point.
// Differences: Legacy XFoil stores the same information across several parallel arrays rather than one per-point object.
// Decision: Keep the managed DTO because it makes QSpec profiles explicit and easy to export.
namespace XFoil.Design.Models;

public sealed class QSpecPoint
{
    // Legacy mapping: none; this constructor packages one QSpec sample that legacy XFoil would keep in parallel arrays.
    // Difference from legacy: The managed port groups geometry and aerodynamic scalars into one point object.
    // Decision: Keep the DTO because it simplifies profile transport and CSV export.
    public QSpecPoint(
        int index,
        double surfaceCoordinate,
        double plotCoordinate,
        AirfoilPoint location,
        double speedRatio,
        double pressureCoefficient,
        double correctedPressureCoefficient)
    {
        Index = index;
        SurfaceCoordinate = surfaceCoordinate;
        PlotCoordinate = plotCoordinate;
        Location = location;
        SpeedRatio = speedRatio;
        PressureCoefficient = pressureCoefficient;
        CorrectedPressureCoefficient = correctedPressureCoefficient;
    }

    public int Index { get; }

    public double SurfaceCoordinate { get; }

    public double PlotCoordinate { get; }

    public AirfoilPoint Location { get; }

    public double SpeedRatio { get; }

    public double PressureCoefficient { get; }

    public double CorrectedPressureCoefficient { get; }
}
