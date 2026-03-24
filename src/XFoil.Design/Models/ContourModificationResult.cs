using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xgdes.f :: GDES contour-edit workflow
// Role in port: Managed result object for contour modifications driven by external control points.
// Differences: Legacy XFoil does not return a dedicated object for this workflow; geometry edits happen through shared state and plotting commands.
// Decision: Keep the managed result object because it gives the service layer a clear, testable output contract.
namespace XFoil.Design.Models;

public sealed class ContourModificationResult
{
    // Legacy mapping: none; this constructor packages the metadata for a managed contour-modification workflow.
    // Difference from legacy: The managed port returns endpoint and control-point information explicitly instead of only mutating geometry buffers.
    // Decision: Keep the result object because it makes the operation auditable.
    public ContourModificationResult(
        AirfoilGeometry geometry,
        int modifiedStartIndex,
        int modifiedEndIndex,
        int controlPointCount,
        bool matchedEndpointSlope)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        ModifiedStartIndex = modifiedStartIndex;
        ModifiedEndIndex = modifiedEndIndex;
        ControlPointCount = controlPointCount;
        MatchedEndpointSlope = matchedEndpointSlope;
    }

    public AirfoilGeometry Geometry { get; }

    public int ModifiedStartIndex { get; }

    public int ModifiedEndIndex { get; }

    public int ControlPointCount { get; }

    public bool MatchedEndpointSlope { get; }
}
