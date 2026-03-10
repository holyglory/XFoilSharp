using XFoil.Core.Models;
using XFoil.Design.Models;

namespace XFoil.Design.Services;

public sealed class ContourEditService
{
    private const double CornerInsertionFraction = 0.3333d;
    private const double DuplicateTolerance = 1e-12d;

    public ContourEditResult AddPoint(
        AirfoilGeometry geometry,
        int insertIndex,
        AirfoilPoint point)
    {
        ValidateGeometry(geometry);
        ValidateInsertIndex(geometry, insertIndex);

        var editedPoints = geometry.Points.ToList();
        editedPoints.Insert(insertIndex, point);

        return BuildResult(geometry, $"{geometry.Name} addp", "ADDP", editedPoints, insertIndex, 1, 0, 0);
    }

    public ContourEditResult MovePoint(
        AirfoilGeometry geometry,
        int pointIndex,
        AirfoilPoint point)
    {
        ValidateGeometry(geometry);
        ValidatePointIndex(geometry, pointIndex);

        var editedPoints = geometry.Points.ToArray();
        editedPoints[pointIndex] = point;

        return BuildResult(geometry, $"{geometry.Name} movp", "MOVP", editedPoints, pointIndex, 0, 0, 0);
    }

    public ContourEditResult DeletePoint(
        AirfoilGeometry geometry,
        int pointIndex)
    {
        ValidateGeometry(geometry);
        ValidatePointIndex(geometry, pointIndex);

        if (geometry.Points.Count <= 3)
        {
            throw new InvalidOperationException("Deleting a point would leave fewer than three geometry points.");
        }

        var editedPoints = geometry.Points.ToList();
        editedPoints.RemoveAt(pointIndex);

        return BuildResult(geometry, $"{geometry.Name} delp", "DELP", editedPoints, pointIndex, 0, 1, 0);
    }

    public ContourEditResult DoublePoint(
        AirfoilGeometry geometry,
        int pointIndex)
    {
        ValidateGeometry(geometry);
        ValidatePointIndex(geometry, pointIndex);

        if (pointIndex == 0 || pointIndex == geometry.Points.Count - 1)
        {
            throw new InvalidOperationException("Cannot double the trailing-edge endpoint.");
        }

        var editedPoints = geometry.Points.ToList();
        editedPoints.Insert(pointIndex, editedPoints[pointIndex]);

        return BuildResult(geometry, $"{geometry.Name} corn", "CORN", editedPoints, pointIndex, 1, 0, 0);
    }

    public ContourEditResult RefineCorners(
        AirfoilGeometry geometry,
        double angleThresholdDegrees,
        CornerRefinementParameterMode parameterMode = CornerRefinementParameterMode.ArcLength,
        double? minimumX = null,
        double? maximumX = null)
    {
        ValidateGeometry(geometry);

        if (!double.IsFinite(angleThresholdDegrees) || angleThresholdDegrees < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(angleThresholdDegrees), "Corner threshold must be finite and non-negative.");
        }

        var lowerX = minimumX ?? double.NegativeInfinity;
        var upperX = maximumX ?? double.PositiveInfinity;
        if (lowerX > upperX)
        {
            (lowerX, upperX) = (upperX, lowerX);
        }

        var sourcePoints = geometry.Points.ToArray();
        var parameters = BuildSplineParameters(sourcePoints, parameterMode);
        var xSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, sourcePoints.Select(point => point.X).ToArray());
        var ySpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, sourcePoints.Select(point => point.Y).ToArray());

        var refinedPoints = new List<AirfoilPoint> { sourcePoints[0] };
        var refinedCornerCount = 0;
        var insertedPointCount = 0;

        for (var index = 1; index < sourcePoints.Length - 1; index++)
        {
            var angle = ComputeCornerAngleDegrees(sourcePoints, index);
            var shouldRefine = Math.Abs(angle) > angleThresholdDegrees;
            if (shouldRefine)
            {
                var beforeParameter = parameters[index] - (CornerInsertionFraction * (parameters[index] - parameters[index - 1]));
                var beforePoint = EvaluateSplinePoint(beforeParameter, xSpline, ySpline);
                if (beforePoint.X >= lowerX && beforePoint.X <= upperX && TryAppendDistinct(refinedPoints, beforePoint))
                {
                    insertedPointCount++;
                }
            }

            TryAppendDistinct(refinedPoints, sourcePoints[index]);

            if (shouldRefine)
            {
                var afterParameter = parameters[index] + (CornerInsertionFraction * (parameters[index + 1] - parameters[index]));
                var afterPoint = EvaluateSplinePoint(afterParameter, xSpline, ySpline);
                if (afterPoint.X >= lowerX && afterPoint.X <= upperX && TryAppendDistinct(refinedPoints, afterPoint))
                {
                    insertedPointCount++;
                }

                refinedCornerCount++;
            }
        }

        TryAppendDistinct(refinedPoints, sourcePoints[^1]);

        return BuildResult(geometry, $"{geometry.Name} cadd", "CADD", refinedPoints, -1, insertedPointCount, 0, refinedCornerCount);
    }

    private static ContourEditResult BuildResult(
        AirfoilGeometry sourceGeometry,
        string geometryName,
        string operation,
        IReadOnlyList<AirfoilPoint> editedPoints,
        int primaryIndex,
        int insertedPointCount,
        int removedPointCount,
        int refinedCornerCount)
    {
        var editedGeometry = new AirfoilGeometry(
            geometryName,
            editedPoints,
            sourceGeometry.Format,
            sourceGeometry.DomainParameters);
        var (maxCornerAngleDegrees, maxCornerAngleIndex) = ComputeMaximumCornerAngle(editedGeometry.Points);

        return new ContourEditResult(
            editedGeometry,
            operation,
            primaryIndex,
            insertedPointCount,
            removedPointCount,
            refinedCornerCount,
            maxCornerAngleDegrees,
            maxCornerAngleIndex);
    }

    private static double[] BuildSplineParameters(
        IReadOnlyList<AirfoilPoint> points,
        CornerRefinementParameterMode parameterMode)
    {
        var parameters = new double[points.Count];
        for (var index = 1; index < points.Count; index++)
        {
            parameters[index] = parameterMode switch
            {
                CornerRefinementParameterMode.Uniform => parameters[index - 1] + 1d,
                CornerRefinementParameterMode.ArcLength => parameters[index - 1] + Distance(points[index - 1], points[index]),
                _ => throw new ArgumentOutOfRangeException(nameof(parameterMode)),
            };
        }

        return parameters;
    }

    private static (double MaxCornerAngleDegrees, int MaxCornerAngleIndex) ComputeMaximumCornerAngle(IReadOnlyList<AirfoilPoint> points)
    {
        var maxAngle = 0d;
        var maxIndex = 0;

        for (var index = 1; index < points.Count - 1; index++)
        {
            var angle = ComputeCornerAngleDegrees(points, index);
            if (Math.Abs(angle) > Math.Abs(maxAngle))
            {
                maxAngle = angle;
                maxIndex = index;
            }
        }

        return (maxAngle, maxIndex);
    }

    private static double ComputeCornerAngleDegrees(IReadOnlyList<AirfoilPoint> points, int index)
    {
        var current = points[index];
        var previousVector = FindNonZeroVector(points, current, index - 1, -1);
        var nextVector = FindNonZeroVector(points, current, index + 1, 1);
        var previousMagnitude = Math.Sqrt((previousVector.X * previousVector.X) + (previousVector.Y * previousVector.Y));
        var nextMagnitude = Math.Sqrt((nextVector.X * nextVector.X) + (nextVector.Y * nextVector.Y));
        if (previousMagnitude <= DuplicateTolerance || nextMagnitude <= DuplicateTolerance)
        {
            return 0d;
        }

        var cross = (nextVector.X * previousVector.Y) - (nextVector.Y * previousVector.X);
        var dot = (nextVector.X * previousVector.X) + (nextVector.Y * previousVector.Y);
        return Math.Atan2(cross, dot) * 180d / Math.PI;
    }

    private static AirfoilPoint EvaluateSplinePoint(
        double parameter,
        GeometryTransformUtilities.NaturalCubicSpline xSpline,
        GeometryTransformUtilities.NaturalCubicSpline ySpline)
    {
        return new AirfoilPoint(
            xSpline.Evaluate(parameter),
            ySpline.Evaluate(parameter));
    }

    private static AirfoilPoint FindNonZeroVector(
        IReadOnlyList<AirfoilPoint> points,
        AirfoilPoint currentPoint,
        int startIndex,
        int step)
    {
        for (var index = startIndex; index >= 0 && index < points.Count; index += step)
        {
            var vector = new AirfoilPoint(
                currentPoint.X - points[index].X,
                currentPoint.Y - points[index].Y);
            if ((vector.X * vector.X) + (vector.Y * vector.Y) > DuplicateTolerance * DuplicateTolerance)
            {
                return vector;
            }
        }

        return default;
    }

    private static bool TryAppendDistinct(ICollection<AirfoilPoint> points, AirfoilPoint candidate)
    {
        if (points.Count == 0)
        {
            points.Add(candidate);
            return true;
        }

        var lastPoint = points.Last();
        if (Distance(lastPoint, candidate) <= DuplicateTolerance)
        {
            return false;
        }

        points.Add(candidate);
        return true;
    }

    private static double Distance(AirfoilPoint first, AirfoilPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static void ValidateGeometry(AirfoilGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
    }

    private static void ValidateInsertIndex(AirfoilGeometry geometry, int insertIndex)
    {
        if (insertIndex <= 0 || insertIndex >= geometry.Points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(insertIndex), "Insert index must identify an interior interval boundary.");
        }
    }

    private static void ValidatePointIndex(AirfoilGeometry geometry, int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= geometry.Points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pointIndex), "Point index is outside the geometry range.");
        }
    }
}
