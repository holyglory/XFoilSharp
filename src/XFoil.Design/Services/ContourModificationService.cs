using XFoil.Core.Models;
using XFoil.Design.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/modify.f :: MODIXY
// Secondary legacy source: f_xfoil/src/xgdes.f :: GDES `MODI` command handling
// Role in port: Applies spline-based contour grafts from external control points.
// Differences: The managed port extracts the legacy contour-modification intent into a direct service call with immutable results and explicit spline helpers.
// Decision: Keep the managed refactor because it preserves the useful geometry-edit behavior without the interactive command machinery.
namespace XFoil.Design.Services;

public sealed class ContourModificationService
{
    // Legacy mapping: f_xfoil/src/modify.f :: MODIXY.
    // Difference from legacy: The managed implementation uses explicit spline helpers and immutable result construction instead of mutating shared geometry buffers in place.
    // Decision: Keep the managed refactor because it makes the modification workflow scriptable and testable.
    public ContourModificationResult ModifyContour(
        AirfoilGeometry geometry,
        IReadOnlyList<AirfoilPoint> controlPoints,
        bool matchEndpointSlope = true)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (controlPoints is null)
        {
            throw new ArgumentNullException(nameof(controlPoints));
        }

        if (controlPoints.Count < 2)
        {
            throw new ArgumentException("At least two control points are required.", nameof(controlPoints));
        }

        var sourcePoints = geometry.Points.ToArray();
        var sourceCurve = GeometryTransformUtilities.SplineCurve2D.Create(sourcePoints);
        var workingControls = controlPoints.ToArray();

        var modifiedStartIndex = FindClosestPointIndex(sourcePoints, workingControls[0]);
        var modifiedEndIndex = FindClosestPointIndex(sourcePoints, workingControls[^1]);
        if (modifiedStartIndex == modifiedEndIndex)
        {
            throw new InvalidOperationException("The graft endpoints must map to distinct contour points.");
        }

        if (modifiedStartIndex > modifiedEndIndex)
        {
            Array.Reverse(workingControls);
            (modifiedStartIndex, modifiedEndIndex) = (modifiedEndIndex, modifiedStartIndex);
        }

        if (matchEndpointSlope || modifiedStartIndex != 0)
        {
            workingControls[0] = sourcePoints[modifiedStartIndex];
        }

        if (matchEndpointSlope || modifiedEndIndex != sourcePoints.Length - 1)
        {
            workingControls[^1] = sourcePoints[modifiedEndIndex];
        }

        var controlParameters = BuildArcLengths(workingControls);
        var modifiedStartArcLength = sourceCurve.ArcLengths[modifiedStartIndex];
        var modifiedEndArcLength = sourceCurve.ArcLengths[modifiedEndIndex];
        RescaleParameters(controlParameters, modifiedStartArcLength, modifiedEndArcLength);

        double? startDerivativeX = null;
        double? startDerivativeY = null;
        double? endDerivativeX = null;
        double? endDerivativeY = null;

        if (matchEndpointSlope && modifiedStartIndex != 0)
        {
            var derivative = sourceCurve.EvaluateDerivative(modifiedStartArcLength);
            startDerivativeX = derivative.X;
            startDerivativeY = derivative.Y;
        }

        if (matchEndpointSlope && modifiedEndIndex != sourcePoints.Length - 1)
        {
            var derivative = sourceCurve.EvaluateDerivative(modifiedEndArcLength);
            endDerivativeX = derivative.X;
            endDerivativeY = derivative.Y;
        }

        var xSpline = new GeometryTransformUtilities.NaturalCubicSpline(
            controlParameters,
            workingControls.Select(point => point.X).ToArray(),
            startDerivativeX,
            endDerivativeX);
        var ySpline = new GeometryTransformUtilities.NaturalCubicSpline(
            controlParameters,
            workingControls.Select(point => point.Y).ToArray(),
            startDerivativeY,
            endDerivativeY);

        var editedPoints = sourcePoints.ToArray();
        // Legacy block: MODIXY spline replacement across the selected contour span.
        // Difference: The managed port rewrites the selected segment from explicit spline evaluations over stored arc lengths instead of operating on the legacy workspace arrays directly.
        // Decision: Keep the explicit loop because it exposes the actual contour replacement clearly.
        for (var index = modifiedStartIndex; index <= modifiedEndIndex; index++)
        {
            var arcLength = sourceCurve.ArcLengths[index];
            editedPoints[index] = new AirfoilPoint(
                xSpline.Evaluate(arcLength),
                ySpline.Evaluate(arcLength));
        }

        var editedGeometry = new AirfoilGeometry(
            $"{geometry.Name} modi",
            editedPoints,
            geometry.Format,
            geometry.DomainParameters);

        return new ContourModificationResult(
            editedGeometry,
            modifiedStartIndex,
            modifiedEndIndex,
            workingControls.Length,
            matchEndpointSlope);
    }

    // Legacy mapping: none; this is a managed nearest-point lookup helper.
    // Difference from legacy: The port selects control-point anchors by explicit distance search rather than by command-local cursor state.
    // Decision: Keep the helper because it is simple and deterministic.
    private static int FindClosestPointIndex(IReadOnlyList<AirfoilPoint> points, AirfoilPoint target)
    {
        var bestIndex = 0;
        var bestDistanceSquared = DistanceSquared(points[0], target);
        for (var index = 1; index < points.Count; index++)
        {
            var distanceSquared = DistanceSquared(points[index], target);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SCALC cumulative arc-length build.
    // Difference from legacy: The helper constructs only the local arc-length parameterization needed by the managed spline fit.
    // Decision: Keep the helper because it localizes the parameterization logic cleanly.
    private static double[] BuildArcLengths(IReadOnlyList<AirfoilPoint> points)
    {
        var arcLengths = new double[points.Count];
        for (var index = 1; index < points.Count; index++)
        {
            var dx = points[index].X - points[index - 1].X;
            var dy = points[index].Y - points[index - 1].Y;
            arcLengths[index] = arcLengths[index - 1] + Math.Sqrt((dx * dx) + (dy * dy));
        }

        return arcLengths;
    }

    // Legacy mapping: none; this is a managed parameter-rescaling helper.
    // Difference from legacy: The port rescales the control-point parameter domain explicitly rather than relying on shared work arrays and in-place normalization.
    // Decision: Keep the helper because it makes the graft interval mapping obvious.
    private static void RescaleParameters(double[] parameters, double targetStart, double targetEnd)
    {
        var currentStart = parameters[0];
        var currentEnd = parameters[^1];
        var scale = currentEnd > currentStart
            ? (targetEnd - targetStart) / (currentEnd - currentStart)
            : 0d;

        for (var index = 0; index < parameters.Length; index++)
        {
            parameters[index] = targetStart + ((parameters[index] - currentStart) * scale);
        }
    }

    // Legacy mapping: none; this is a managed distance helper for anchor selection.
    // Difference from legacy: The helper computes squared distance directly instead of depending on interactive point picking.
    // Decision: Keep the helper because it supports deterministic anchor lookup.
    private static double DistanceSquared(AirfoilPoint first, AirfoilPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return (dx * dx) + (dy * dy);
    }
}
