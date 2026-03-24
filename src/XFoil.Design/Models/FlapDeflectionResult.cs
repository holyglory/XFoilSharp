using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xgdes.f :: FLAP
// Role in port: Managed result object for flap-deflection geometry edits.
// Differences: Legacy XFoil applies flap edits directly to geometry arrays and state flags rather than returning a dedicated summary object.
// Decision: Keep the managed result object because it makes flap-edit outcomes explicit for callers.
namespace XFoil.Design.Models;

public sealed class FlapDeflectionResult
{
    // Legacy mapping: none; this constructor packages the outcome of a FLAP-derived geometry edit into one immutable object.
    // Difference from legacy: The managed port surfaces hinge and point-count metadata explicitly instead of leaving it implicit in state changes.
    // Decision: Keep the result object because it is a clearer service contract.
    public FlapDeflectionResult(
        AirfoilGeometry geometry,
        AirfoilPoint hingePoint,
        double deflectionDegrees,
        int affectedPointCount,
        int insertedPointCount,
        int removedPointCount)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        HingePoint = hingePoint;
        DeflectionDegrees = deflectionDegrees;
        AffectedPointCount = affectedPointCount;
        InsertedPointCount = insertedPointCount;
        RemovedPointCount = removedPointCount;
    }

    public AirfoilGeometry Geometry { get; }

    public AirfoilPoint HingePoint { get; }

    public double DeflectionDegrees { get; }

    public int AffectedPointCount { get; }

    public int InsertedPointCount { get; }

    public int RemovedPointCount { get; }
}
