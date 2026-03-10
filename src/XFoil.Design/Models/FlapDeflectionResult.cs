using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class FlapDeflectionResult
{
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
