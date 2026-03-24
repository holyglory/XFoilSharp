using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xgdes.f :: GDES edit command family
// Role in port: Managed result object for pointwise contour-edit operations.
// Differences: Legacy XFoil edits geometry in place and reports progress procedurally instead of returning a structured edit summary.
// Decision: Keep the managed result object because it captures edit metadata cleanly for CLI and tests.
namespace XFoil.Design.Models;

public sealed class ContourEditResult
{
    // Legacy mapping: none; this constructor aggregates the outcome of a GDES-inspired contour edit into one immutable result.
    // Difference from legacy: The managed port surfaces edit counts and angle metadata explicitly instead of leaving them implicit in the mutated geometry.
    // Decision: Keep the structured result because it is more useful to callers.
    public ContourEditResult(
        AirfoilGeometry geometry,
        string operation,
        int primaryIndex,
        int insertedPointCount,
        int removedPointCount,
        int refinedCornerCount,
        double maxCornerAngleDegrees,
        int maxCornerAngleIndex)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        PrimaryIndex = primaryIndex;
        InsertedPointCount = insertedPointCount;
        RemovedPointCount = removedPointCount;
        RefinedCornerCount = refinedCornerCount;
        MaxCornerAngleDegrees = maxCornerAngleDegrees;
        MaxCornerAngleIndex = maxCornerAngleIndex;
    }

    public AirfoilGeometry Geometry { get; }

    public string Operation { get; }

    public int PrimaryIndex { get; }

    public int InsertedPointCount { get; }

    public int RemovedPointCount { get; }

    public int RefinedCornerCount { get; }

    public double MaxCornerAngleDegrees { get; }

    public int MaxCornerAngleIndex { get; }
}
