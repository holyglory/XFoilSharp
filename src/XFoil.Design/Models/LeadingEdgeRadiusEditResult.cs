using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class LeadingEdgeRadiusEditResult
{
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
