using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class TrailingEdgeGapEditResult
{
    public TrailingEdgeGapEditResult(
        AirfoilGeometry geometry,
        double originalGap,
        double finalGap,
        double targetGap,
        double blendDistanceChordFraction)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        OriginalGap = originalGap;
        FinalGap = finalGap;
        TargetGap = targetGap;
        BlendDistanceChordFraction = blendDistanceChordFraction;
    }

    public AirfoilGeometry Geometry { get; }

    public double OriginalGap { get; }

    public double FinalGap { get; }

    public double TargetGap { get; }

    public double BlendDistanceChordFraction { get; }
}
