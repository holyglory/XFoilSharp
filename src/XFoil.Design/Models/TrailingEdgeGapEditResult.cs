using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xgdes.f :: TGAP
// Role in port: Managed result object for trailing-edge-gap edits.
// Differences: Legacy XFoil updates the active geometry and prints progress rather than returning an explicit edit summary.
// Decision: Keep the managed result object because it records the edit outcome clearly for services, CLI, and tests.
namespace XFoil.Design.Models;

public sealed class TrailingEdgeGapEditResult
{
    // Legacy mapping: none; this constructor packages the outcome of a TGAP-derived geometry edit.
    // Difference from legacy: The managed port returns original, final, and target gap values explicitly instead of burying them in mutable state.
    // Decision: Keep the result object because it improves observability and testing.
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
