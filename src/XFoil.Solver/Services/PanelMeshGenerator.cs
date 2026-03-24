using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f :: PANGEN
// Secondary legacy source: f_xfoil/src/xgeom.f :: SCALC/LEFIND
// Role in port: Builds the default managed panel mesh from normalized geometry.
// Differences: This file is not a direct PANGEN port; it uses cubic-spline sampling and Gaussian density weights to approximate legacy panel bunching with a simpler managed mesh API.
// Decision: Keep the managed implementation for default preprocessing. The direct legacy paneling path lives in CosineClusteringPanelDistributor for parity-sensitive work.
namespace XFoil.Solver.Services;

public sealed class PanelMeshGenerator
{
    private readonly AirfoilNormalizer normalizer = new();

    // Legacy mapping: managed-only convenience overload around the managed paneling path derived from PANGEN.
    // Difference from legacy: XFoil does not expose this split overload; the port adds it to centralize default options.
    // Decision: Keep the overload because it is a clean API wrapper and does not affect parity-sensitive solver math.
    public PanelMesh Generate(AirfoilGeometry geometry, int panelCount)
    {
        return Generate(geometry, panelCount, new PanelingOptions());
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: PANGEN (managed-derived default mesh generation path).
    // Difference from legacy: This method normalizes the geometry first and then builds a weighted spline distribution instead of replaying the exact PANGEN curvature-smoothing/Newton redistribution sequence.
    // Decision: Keep the managed approximation here because it is the default mesh builder; exact legacy paneling is preserved separately in the dedicated parity-oriented distributor.
    public PanelMesh Generate(AirfoilGeometry geometry, int panelCount, PanelingOptions panelingOptions)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (panelCount < AnalysisSettings.MinimumSupportedPanelCount)
        {
            throw new ArgumentException(
                $"Panel count must be at least {AnalysisSettings.MinimumSupportedPanelCount}.",
                nameof(panelCount));
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
        // Legacy block: managed replacement for the PANGEN node-spacing stage.
        // Difference: This block uses sampled curvature and Gaussian LE/TE weighting instead of the legacy diffusion-smoothed curvature and Newton equalization.
        // Decision: Keep the simpler managed distribution in this file and rely on the direct PANGEN port when binary legacy behavior is needed.
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

    // Legacy mapping: managed-derived replacement for f_xfoil/src/xfoil.f :: PANGEN spacing redistribution.
    // Difference from legacy: The method estimates density from sampled curvature plus LE/TE Gaussian envelopes rather than solving the legacy curvature-smoothing and equal-spacing system.
    // Decision: Keep this managed weighting model because it is simpler and stable for the default Hess-Smith mesh path.
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

    // Legacy mapping: managed-only curvature estimate used by the default mesh distribution.
    // Difference from legacy: XFoil obtains curvature from spline derivatives, while this helper uses a centered finite-difference estimate on sampled points.
    // Decision: Keep the simpler estimate because this file is intentionally an approximate paneling path rather than the parity reference.
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

    // Legacy mapping: managed-only weighting helper with no direct Fortran analogue.
    // Difference from legacy: The Gaussian envelope is a managed design choice used to emphasize LE clustering without replaying the legacy curvature diffusion model.
    // Decision: Keep the helper because it expresses the managed bunching policy clearly.
    private static double Gaussian(double value, double mean, double sigma)
    {
        var distance = value - mean;
        return Math.Exp(-(distance * distance) / (2d * sigma * sigma));
    }

    // Legacy mapping: managed-only inverse-CDF interpolation helper for the weighted mesh distribution.
    // Difference from legacy: XFoil's direct PANGEN path does not use this binary-search interpolation because it updates node positions through Newton iterations.
    // Decision: Keep the helper because it is the right fit for the sampled managed distribution used here.
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

    // Legacy mapping: f_xfoil/src/xgeom.f :: SCALC (managed-derived arc-length accumulation).
    // Difference from legacy: The managed path computes polyline arc length directly from normalized points instead of working from the legacy spline workspace.
    // Decision: Keep the direct accumulation because it is sufficient for the default preprocessing mesh.
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

    // Legacy mapping: managed-only polygon orientation helper with no direct Fortran analogue.
    // Difference from legacy: The signed-area test is a .NET-side convenience for constructing outward panel normals and is not a named XFoil routine.
    // Decision: Keep the helper because it makes mesh orientation explicit in the managed API.
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
