using XFoil.Core.Models;

namespace XFoil.Core.Services;

public sealed class AirfoilMetricsCalculator
{
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

    private static double Distance(AirfoilPoint a, AirfoilPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
