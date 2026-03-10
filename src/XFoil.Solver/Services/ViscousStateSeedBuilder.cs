using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class ViscousStateSeedBuilder
{
    private const double TrailingEdgeGapRatio = 2.5d;

    public ViscousStateSeed Build(InviscidAnalysisResult analysis, BoundaryLayerTopology topology)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (topology is null)
        {
            throw new ArgumentNullException(nameof(topology));
        }

        var (gap, normalGap, streamwiseGap) = ComputeTrailingEdgeGeometry(analysis.Mesh);
        var upperSurface = BuildSurfaceSeed(topology.UpperSurfaceStations);
        var lowerSurface = BuildSurfaceSeed(topology.LowerSurfaceStations);
        var wake = BuildWakeSeed(topology.WakeStations, analysis.Wake, normalGap, gap);

        return new ViscousStateSeed(
            topology,
            upperSurface,
            lowerSurface,
            wake,
            gap,
            normalGap,
            streamwiseGap);
    }

    private static ViscousBranchSeed BuildSurfaceSeed(IReadOnlyList<BoundaryLayerStation> stations)
    {
        var viscousStations = new List<ViscousStationSeed>(stations.Count);
        for (var index = 0; index < stations.Count; index++)
        {
            var station = stations[index];
            viscousStations.Add(new ViscousStationSeed(
                index,
                station.Location,
                station.DistanceFromStagnation,
                station.EdgeVelocity,
                0d));
        }

        return new ViscousBranchSeed(stations[0].Branch, viscousStations);
    }

    private static ViscousBranchSeed BuildWakeSeed(
        IReadOnlyList<BoundaryLayerStation> stations,
        WakeGeometry wake,
        double normalGap,
        double trailingEdgeGap)
    {
        var viscousStations = new List<ViscousStationSeed>(stations.Count);
        var sharpTrailingEdge = trailingEdgeGap < 1e-4;
        var wakeGapDerivative = ComputeWakeGapDerivative(wake);
        var cubicA = 3d + (TrailingEdgeGapRatio * wakeGapDerivative);
        var cubicB = -2d - (TrailingEdgeGapRatio * wakeGapDerivative);

        for (var index = 0; index < stations.Count; index++)
        {
            var station = stations[index];
            var wakePoint = wake.Points[Math.Min(index, wake.Points.Count - 1)];
            var wakeGap = 0d;

            if (!sharpTrailingEdge && normalGap > 1e-9)
            {
                var normalizedDistance = 1d - (wakePoint.DistanceFromTrailingEdge / (TrailingEdgeGapRatio * normalGap));
                if (normalizedDistance >= 0d)
                {
                    wakeGap = normalGap * (cubicA + (cubicB * normalizedDistance)) * normalizedDistance * normalizedDistance;
                }
            }

            viscousStations.Add(new ViscousStationSeed(
                index,
                station.Location,
                station.DistanceFromStagnation,
                Math.Max(0d, wakePoint.VelocityMagnitude),
                Math.Max(0d, wakeGap)));
        }

        return new ViscousBranchSeed(BoundaryLayerBranch.Wake, viscousStations);
    }

    private static (double Gap, double NormalGap, double StreamwiseGap) ComputeTrailingEdgeGeometry(PanelMesh mesh)
    {
        var firstNode = mesh.Nodes[0];
        var lastSurfaceNode = mesh.Nodes[^2];
        var firstPanel = mesh.Panels[0];
        var lastPanel = mesh.Panels[^1];

        var dxte = firstNode.X - lastSurfaceNode.X;
        var dyte = firstNode.Y - lastSurfaceNode.Y;
        var tangentX = 0.5d * (-firstPanel.TangentX + lastPanel.TangentX);
        var tangentY = 0.5d * (-firstPanel.TangentY + lastPanel.TangentY);
        var normalGap = Math.Abs((tangentX * dyte) - (tangentY * dxte));
        var streamwiseGap = (tangentX * dxte) + (tangentY * dyte);
        var gap = Math.Sqrt((dxte * dxte) + (dyte * dyte));
        return (gap, normalGap, streamwiseGap);
    }

    private static double ComputeWakeGapDerivative(WakeGeometry wake)
    {
        if (wake.Points.Count < 2)
        {
            return 0d;
        }

        var firstTangent = wake.Points[1];
        var clampedY = Math.Clamp(firstTangent.TangentY, -0.999999d, 0.999999d);
        return clampedY / Math.Sqrt(Math.Max(1e-12, 1d - (clampedY * clampedY)));
    }
}
