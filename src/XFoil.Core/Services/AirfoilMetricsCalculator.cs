using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgeom.f :: LEFIND/GEOPAR
// Secondary legacy source: f_xfoil/src/spline.f :: SCALC
// Role in port: Computes coarse airfoil geometry metrics for parsed or generated shapes before paneling.
// Differences: The managed code works directly on discrete points with polyline and x-sampled estimates, while legacy XFoil uses spline derivatives and exact geometry-property routines.
// Decision: Keep the simpler managed helper because it is used for validation and diagnostics rather than solver parity; no parity-only branch is needed here.
namespace XFoil.Core.Services;

public sealed class AirfoilMetricsCalculator
{
    // Legacy mapping: f_xfoil/src/xgeom.f :: LEFIND/GEOPAR, f_xfoil/src/spline.f :: SCALC.
    // Difference from legacy: This method estimates leading edge, arc length, thickness, and camber from discrete points and uniform x sampling instead of spline-based geometry properties.
    // Decision: Keep the simpler managed metric calculation because it is adequate for preprocessing and tests and is not part of the binary parity path.
    public AirfoilMetrics Calculate(AirfoilGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var points = geometry.Points.ToArray();
        var leadingEdgeIndex = FindLeadingEdgeIndex(points);
        var leadingEdge = points[leadingEdgeIndex];
        var trailingEdgeMidpoint = new AirfoilPoint(
            0.5 * (points[0].X + points[^1].X),
            0.5 * (points[0].Y + points[^1].Y));

        var chord = Distance(leadingEdge, trailingEdgeMidpoint);
        var totalArcLength = 0d;
        // Legacy block: SCALC cumulative arc-length sweep.
        // Difference: The managed code uses straight-segment distances between supplied points instead of first building a spline parameterization.
        // Decision: Keep the polyline sweep because this helper is intentionally lightweight.
        for (var index = 1; index < points.Length; index++)
        {
            totalArcLength += Distance(points[index - 1], points[index]);
        }

        var upper = points[..(leadingEdgeIndex + 1)]
            .Reverse()
            .OrderBy(point => point.X)
            .ToArray();

        var lower = points[leadingEdgeIndex..]
            .OrderBy(point => point.X)
            .ToArray();

        var sampleCount = 200;
        var maxThickness = 0d;
        var maxCamber = 0d;

        // Legacy block: managed-only thickness/camber sampling inspired by GEOPAR-style geometry summaries.
        // Difference: Legacy XFoil derives geometry properties from spline-based quantities; this helper samples upper and lower surfaces uniformly in x.
        // Decision: Keep the uniform sampling approach because it is stable, readable, and not a parity-critical solver kernel.
        for (var sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
        {
            var x = sampleIndex / (double)sampleCount;
            var upperY = InterpolateY(upper, x);
            var lowerY = InterpolateY(lower, x);
            var thickness = upperY - lowerY;
            var camber = 0.5 * (upperY + lowerY);
            maxThickness = Math.Max(maxThickness, thickness);
            maxCamber = Math.Max(maxCamber, Math.Abs(camber));
        }

        return new AirfoilMetrics(
            leadingEdge,
            trailingEdgeMidpoint,
            chord,
            totalArcLength,
            maxThickness,
            maxCamber);
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: XLFIND/LEFIND first-guess scan.
    // Difference from legacy: This helper simply picks the minimum-x node instead of refining the leading edge on a spline.
    // Decision: Keep the discrete scan because the surrounding metrics path is intentionally approximate.
    private static int FindLeadingEdgeIndex(IReadOnlyList<AirfoilPoint> points)
    {
        var bestIndex = 0;
        for (var index = 1; index < points.Count; index++)
        {
            if (points[index].X < points[bestIndex].X)
            {
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    // Legacy mapping: none; this is a managed support routine used for the sampled thickness/camber estimate.
    // Difference from legacy: XFoil evaluates geometry on splines rather than linearly interpolating sorted surface points.
    // Decision: Keep the linear interpolation helper because it matches the coarse managed metric strategy.
    private static double InterpolateY(IReadOnlyList<AirfoilPoint> points, double x)
    {
        if (x <= points[0].X)
        {
            return points[0].Y;
        }

        if (x >= points[^1].X)
        {
            return points[^1].Y;
        }

        for (var index = 1; index < points.Count; index++)
        {
            var left = points[index - 1];
            var right = points[index];
            if (x > right.X)
            {
                continue;
            }

            var deltaX = right.X - left.X;
            if (Math.Abs(deltaX) < 1e-12)
            {
                return 0.5 * (left.Y + right.Y);
            }

            var t = (x - left.X) / deltaX;
            return left.Y + (t * (right.Y - left.Y));
        }

        return points[^1].Y;
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SCALC segment-length accumulation.
    // Difference from legacy: The helper returns a single Euclidean segment length instead of feeding a spline arc-length array.
    // Decision: Keep the scalar helper because it is the clearest building block for this simplified metrics path.
    private static double Distance(AirfoilPoint a, AirfoilPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
