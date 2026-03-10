using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

namespace XFoil.Solver.Services;

public sealed class PanelMeshGenerator
{
    private readonly AirfoilNormalizer normalizer = new();

    public PanelMesh Generate(AirfoilGeometry geometry, int panelCount)
    {
        return Generate(geometry, panelCount, new PanelingOptions());
    }

    public PanelMesh Generate(AirfoilGeometry geometry, int panelCount, PanelingOptions panelingOptions)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (panelCount < 16)
        {
            throw new ArgumentException("Panel count must be at least 16.", nameof(panelCount));
        }

        if (panelingOptions is null)
        {
            throw new ArgumentNullException(nameof(panelingOptions));
        }

        var normalized = normalizer.Normalize(geometry);
        var sourcePoints = normalized.Points.ToArray();
        var parameters = BuildArcLengthParameters(sourcePoints);
        var xSpline = new CubicSpline(parameters, sourcePoints.Select(point => point.X).ToArray());
        var ySpline = new CubicSpline(parameters, sourcePoints.Select(point => point.Y).ToArray());
        var totalLength = parameters[^1];
        var targetParameters = BuildWeightedDistribution(
            xSpline,
            ySpline,
            totalLength,
            panelCount,
            panelingOptions);

        var nodes = new AirfoilPoint[panelCount + 1];
        for (var index = 0; index <= panelCount; index++)
        {
            var parameter = targetParameters[index];
            nodes[index] = new AirfoilPoint(xSpline.Evaluate(parameter), ySpline.Evaluate(parameter));
        }

        nodes[^1] = nodes[0];
        var isCounterClockwise = ComputeSignedArea(nodes) > 0d;
        var panels = new Panel[panelCount];

        for (var index = 0; index < panelCount; index++)
        {
            var start = nodes[index];
            var end = nodes[index + 1];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= 1e-10)
            {
                throw new InvalidOperationException("Panel generation produced a zero-length panel.");
            }

            var tangentX = dx / length;
            var tangentY = dy / length;
            var normalX = isCounterClockwise ? tangentY : -tangentY;
            var normalY = isCounterClockwise ? -tangentX : tangentX;

            panels[index] = new Panel(
                index,
                start,
                end,
                new AirfoilPoint(0.5d * (start.X + end.X), 0.5d * (start.Y + end.Y)),
                length,
                tangentX,
                tangentY,
                normalX,
                normalY);
        }

        return new PanelMesh(nodes, panels, isCounterClockwise);
    }

    private static double[] BuildWeightedDistribution(
        CubicSpline xSpline,
        CubicSpline ySpline,
        double totalLength,
        int panelCount,
        PanelingOptions panelingOptions)
    {
        var sampleCount = Math.Max(panelCount * 8, 512);
        var sampleParameters = new double[sampleCount];
        var samplePoints = new AirfoilPoint[sampleCount];
        var curvature = new double[sampleCount];

        for (var index = 0; index < sampleCount; index++)
        {
            var parameter = totalLength * index / (sampleCount - 1d);
            sampleParameters[index] = parameter;
            samplePoints[index] = new AirfoilPoint(xSpline.Evaluate(parameter), ySpline.Evaluate(parameter));
        }

        var leadingEdgeIndex = 0;
        for (var index = 1; index < sampleCount; index++)
        {
            if (samplePoints[index].X < samplePoints[leadingEdgeIndex].X)
            {
                leadingEdgeIndex = index;
            }
        }

        var leadingEdgeLocation = sampleParameters[leadingEdgeIndex] / totalLength;
        var maxCurvature = 0d;
        var sampleSpacing = totalLength / (sampleCount - 1d);
        for (var index = 1; index < sampleCount - 1; index++)
        {
            curvature[index] = EstimateCurvature(samplePoints[index - 1], samplePoints[index], samplePoints[index + 1], sampleSpacing);
            maxCurvature = Math.Max(maxCurvature, curvature[index]);
        }

        var density = new double[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var contourLocation = sampleParameters[index] / totalLength;
            var normalizedCurvature = maxCurvature > 1e-12 ? curvature[index] / maxCurvature : 0d;
            var leadingEdgeShape = Gaussian(contourLocation, leadingEdgeLocation, 0.06d);
            var trailingEdgeDistance = Math.Min(contourLocation, 1d - contourLocation);
            var trailingEdgeShape = Math.Exp(-(trailingEdgeDistance * trailingEdgeDistance) / (2d * 0.035d * 0.035d));

            density[index] = 1d
                           + (panelingOptions.CurvatureWeight * normalizedCurvature)
                           + (panelingOptions.LeadingEdgeWeight * leadingEdgeShape)
                           + (panelingOptions.TrailingEdgeWeight * trailingEdgeShape);
        }

        var cumulative = new double[sampleCount];
        for (var index = 1; index < sampleCount; index++)
        {
            var interval = sampleParameters[index] - sampleParameters[index - 1];
            cumulative[index] = cumulative[index - 1]
                              + 0.5d * (density[index - 1] + density[index]) * interval;
        }

        var weightedLength = cumulative[^1];
        var result = new double[panelCount + 1];
        result[0] = 0d;
        result[^1] = totalLength;

        for (var index = 1; index < panelCount; index++)
        {
            var target = weightedLength * index / panelCount;
            result[index] = InterpolateParameter(sampleParameters, cumulative, target);
        }

        return result;
    }

    private static double EstimateCurvature(AirfoilPoint previous, AirfoilPoint current, AirfoilPoint next, double spacing)
    {
        var dx = (next.X - previous.X) / (2d * spacing);
        var dy = (next.Y - previous.Y) / (2d * spacing);
        var ddx = (next.X - (2d * current.X) + previous.X) / (spacing * spacing);
        var ddy = (next.Y - (2d * current.Y) + previous.Y) / (spacing * spacing);
        var denominator = Math.Pow((dx * dx) + (dy * dy), 1.5d);
        if (denominator <= 1e-16)
        {
            return 0d;
        }

        return Math.Abs((dx * ddy) - (dy * ddx)) / denominator;
    }

    private static double Gaussian(double value, double mean, double sigma)
    {
        var distance = value - mean;
        return Math.Exp(-(distance * distance) / (2d * sigma * sigma));
    }

    private static double InterpolateParameter(
        IReadOnlyList<double> parameters,
        IReadOnlyList<double> cumulative,
        double target)
    {
        var lower = 0;
        var upper = cumulative.Count - 1;
        while (upper - lower > 1)
        {
            var middle = (upper + lower) / 2;
            if (target < cumulative[middle])
            {
                upper = middle;
            }
            else
            {
                lower = middle;
            }
        }

        var span = cumulative[upper] - cumulative[lower];
        if (span <= 1e-16)
        {
            return parameters[lower];
        }

        var t = (target - cumulative[lower]) / span;
        return parameters[lower] + t * (parameters[upper] - parameters[lower]);
    }

    private static double[] BuildArcLengthParameters(IReadOnlyList<AirfoilPoint> points)
    {
        var parameters = new double[points.Count];
        for (var index = 1; index < points.Count; index++)
        {
            var dx = points[index].X - points[index - 1].X;
            var dy = points[index].Y - points[index - 1].Y;
            parameters[index] = parameters[index - 1] + Math.Sqrt((dx * dx) + (dy * dy));
        }

        return parameters;
    }

    private static double ComputeSignedArea(IReadOnlyList<AirfoilPoint> polygon)
    {
        var area = 0d;
        for (var index = 0; index < polygon.Count - 1; index++)
        {
            area += (polygon[index].X * polygon[index + 1].Y) - (polygon[index + 1].X * polygon[index].Y);
        }

        return 0.5d * area;
    }
}
