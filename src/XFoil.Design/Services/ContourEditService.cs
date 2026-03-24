using XFoil.Core.Models;
using XFoil.Design.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f :: GDES CADD and point-edit command workflow
// Secondary legacy source: f_xfoil/src/spline.f :: SCALC
// Role in port: Provides managed pointwise contour edits and corner refinement for geometry-design workflows.
// Differences: Legacy XFoil performs these edits interactively against mutable geometry buffers; the managed port factors them into explicit service methods and immutable result objects.
// Decision: Keep the managed refactor because it makes geometry editing scriptable and testable while preserving the core GDES semantics where they exist.
namespace XFoil.Design.Services;

public sealed class ContourEditService
{
    private const double CornerInsertionFraction = 0.3333d;
    private const double DuplicateTolerance = 1e-12d;

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES `ADDP` command handling.
    // Difference from legacy: The managed port inserts directly into a copied point list and returns a structured result object.
    // Decision: Keep the explicit method because it is clearer and easier to test than the interactive command path.
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

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES `MOVP` command handling.
    // Difference from legacy: The managed port rewrites the target node in a copied array and returns an immutable edit summary.
    // Decision: Keep the explicit method because it fits the service API cleanly.
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

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES `DELP` command handling.
    // Difference from legacy: The managed port removes the selected node from a copied list and rejects underconstrained results with an exception.
    // Decision: Keep the direct service method because it is a clearer non-interactive equivalent.
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

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES `CORN` command handling.
    // Difference from legacy: The managed port duplicates the selected point directly and reports the edit through a result object.
    // Decision: Keep the explicit method because it is the right service-level shape for this operation.
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

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES `CADD` corner-refinement workflow.
    // Difference from legacy: The managed implementation uses explicit spline utilities and immutable point rebuilding instead of mutating the buffer geometry in place through the command loop.
    // Decision: Keep the managed refactor because it preserves the corner-refinement intent while fitting the service API.
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

        // Legacy block: GDES `CADD` corner scan and insert loop.
        // Difference: The managed port computes the refinement candidates from explicit spline samples and x-range filters instead of mutating the working geometry buffers in the command routine.
        // Decision: Keep the decomposed managed loop because it is easier to audit and test.
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

    // Legacy mapping: none; this is a managed result-packaging helper.
    // Difference from legacy: The port computes the renamed geometry and metadata bundle explicitly instead of relying on side effects in the command layer.
    // Decision: Keep the helper because it centralizes the immutable result construction.
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

    // Legacy mapping: f_xfoil/src/spline.f :: SCALC-style cumulative parameter build.
    // Difference from legacy: The managed helper can parameterize uniformly or by arc length, whereas legacy GDES relies on its own working arrays and command options.
    // Decision: Keep the flexible helper because it makes the refinement method explicit.
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

    // Legacy mapping: none; this is a managed summary helper for the edited geometry.
    // Difference from legacy: The helper measures the strongest remaining corner angle explicitly instead of relying on the operator to inspect the result.
    // Decision: Keep the helper because it provides useful deterministic metadata.
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

    // Legacy mapping: f_xfoil/src/xgdes.f :: `CADD` corner-angle test intent.
    // Difference from legacy: The managed helper computes the signed corner angle from explicit neighboring vectors rather than from the legacy command-local state.
    // Decision: Keep the helper because it captures the refinement criterion clearly.
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

    // Legacy mapping: none; this is a managed spline-evaluation helper.
    // Difference from legacy: The helper calls the managed spline utility directly instead of passing through Fortran spline arrays.
    // Decision: Keep the helper because it localizes the interpolation step.
    private static AirfoilPoint EvaluateSplinePoint(
        double parameter,
        GeometryTransformUtilities.NaturalCubicSpline xSpline,
        GeometryTransformUtilities.NaturalCubicSpline ySpline)
    {
        return new AirfoilPoint(
            xSpline.Evaluate(parameter),
            ySpline.Evaluate(parameter));
    }

    // Legacy mapping: none; this is a managed duplicate-skipping vector search helper.
    // Difference from legacy: The port explicitly skips zero-length spans to stabilize duplicate-point handling.
    // Decision: Keep the helper because it protects the angle calculation from degenerate input.
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

    // Legacy mapping: none; this is a managed duplicate-filter helper.
    // Difference from legacy: The port suppresses numerically duplicate insertions explicitly instead of relying on later cleanup or operator judgment.
    // Decision: Keep the helper because it improves deterministic output quality.
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

    // Legacy mapping: f_xfoil/src/spline.f :: SCALC segment-length accumulation.
    // Difference from legacy: The helper computes one Euclidean segment length instead of feeding a global spline-parameter array.
    // Decision: Keep the helper because it is the simplest building block for the managed parameterization path.
    private static double Distance(AirfoilPoint first, AirfoilPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // Legacy mapping: none; this is a managed argument guard.
    // Difference from legacy: The port fails fast with exceptions instead of relying on command-loop discipline.
    // Decision: Keep the explicit guard because it improves robustness.
    private static void ValidateGeometry(AirfoilGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
    }

    // Legacy mapping: none; this is a managed index-range guard.
    // Difference from legacy: The service validates the insertion point explicitly rather than relying on interactive command checks.
    // Decision: Keep the guard because it makes failures deterministic and local.
    private static void ValidateInsertIndex(AirfoilGeometry geometry, int insertIndex)
    {
        if (insertIndex <= 0 || insertIndex >= geometry.Points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(insertIndex), "Insert index must identify an interior interval boundary.");
        }
    }

    // Legacy mapping: none; this is a managed index-range guard.
    // Difference from legacy: The service validates the point index explicitly rather than relying on interactive command checks.
    // Decision: Keep the guard because it makes failures deterministic and local.
    private static void ValidatePointIndex(AirfoilGeometry geometry, int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= geometry.Points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pointIndex), "Point index is outside the geometry range.");
        }
    }
}
