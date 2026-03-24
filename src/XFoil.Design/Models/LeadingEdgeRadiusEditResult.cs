using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xgdes.f :: LERAD
// Role in port: Managed result object for leading-edge-radius edits.
// Differences: Legacy XFoil mutates the active geometry and reports the effect procedurally instead of returning a typed summary.
// Decision: Keep the managed result object because it captures the edit outcome cleanly.
namespace XFoil.Design.Models;

public sealed class LeadingEdgeRadiusEditResult
{
    // Legacy mapping: none; this constructor packages the outcome of a LERAD-derived geometry edit.
    // Difference from legacy: The managed port returns original/final radius values explicitly instead of leaving them implicit in updated geometry state.
    // Decision: Keep the result object because it improves observability for callers and tests.
    public LeadingEdgeRadiusEditResult(
        AirfoilGeometry geometry,
        double originalRadius,
        double finalRadius,
        double radiusScaleFactor,
        double blendDistanceChordFraction)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        OriginalRadius = originalRadius;
        FinalRadius = finalRadius;
        RadiusScaleFactor = radiusScaleFactor;
        BlendDistanceChordFraction = blendDistanceChordFraction;
    }

    public AirfoilGeometry Geometry { get; }

    public double OriginalRadius { get; }

    public double FinalRadius { get; }

    public double RadiusScaleFactor { get; }

    public double BlendDistanceChordFraction { get; }
}
