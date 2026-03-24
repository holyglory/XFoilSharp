// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xgeom.f :: LEFIND/GEOPAR, f_xfoil/src/spline.f :: SCALC
// Role in port: Managed summary object for basic airfoil metrics consumed by tests and preprocessing utilities.
// Differences: Legacy XFoil computes related quantities transiently into scalars and COMMON members instead of returning one metrics object.
// Decision: Keep the managed summary object because it makes geometry inspection explicit and does not participate in parity-sensitive solver state.
namespace XFoil.Core.Models;

public sealed class AirfoilMetrics
{
    // Legacy mapping: none; this constructor aggregates geometry properties that legacy XFoil leaves distributed across local scalars and COMMON state.
    // Difference from legacy: The managed port returns an immutable metrics bundle instead of relying on side effects from geometry-analysis routines.
    // Decision: Keep the managed bundle because it is clearer for callers and has no direct parity obligation.
    public AirfoilMetrics(
        AirfoilPoint leadingEdge,
        AirfoilPoint trailingEdgeMidpoint,
        double chord,
        double totalArcLength,
        double maxThickness,
        double maxCamber)
    {
        LeadingEdge = leadingEdge;
        TrailingEdgeMidpoint = trailingEdgeMidpoint;
        Chord = chord;
        TotalArcLength = totalArcLength;
        MaxThickness = maxThickness;
        MaxCamber = maxCamber;
    }

    public AirfoilPoint LeadingEdge { get; }

    public AirfoilPoint TrailingEdgeMidpoint { get; }

    public double Chord { get; }

    public double TotalArcLength { get; }

    public double MaxThickness { get; }

    public double MaxCamber { get; }
}
