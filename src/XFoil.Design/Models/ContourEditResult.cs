using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class ContourEditResult
{
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
