using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xoper.f :: MRCHUE seed-state lineage
// Secondary legacy source: f_xfoil/src/xfoil.f :: TECALC; f_xfoil/src/xpanel.f :: XYWAKE/WGAP
// Role in port: Builds an explicit managed seed object for diagnostic viscous initialization flows.
// Differences: The file is derived from the legacy seed/topology concepts, but it packages upper, lower, and wake branches plus trailing-edge gap metadata into immutable managed objects instead of mutating XFoil work arrays.
// Decision: Keep the managed seed builder for diagnostics and setup workflows. The strict parity seed path remains inside ViscousSolverEngine.
namespace XFoil.Solver.Services;

public sealed class ViscousStateSeedBuilder
{
    // Legacy mapping: f_xfoil/src/xoper.f :: MRCHUE seed-state lineage.
    // Difference from legacy: The method builds an explicit ViscousStateSeed from managed inviscid results and topology objects rather than filling the seed arrays used by the main XFoil march.
    // Decision: Keep the object-building flow because it is clearer for diagnostics and tests.
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

    // Legacy mapping: managed-derived helper from the legacy surface station seed arrays.
    // Difference from legacy: The port materializes immutable station-seed objects instead of storing branch data in several coordinated arrays.
    // Decision: Keep the helper because it expresses branch seed construction clearly.
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

    // Legacy mapping: f_xfoil/src/xpanel.f :: XYWAKE/WGAP seed-wake lineage.
    // Difference from legacy: The wake seed is built from the managed wake geometry and the isolated WakeGapProfile helper rather than from the original wake arrays inside the viscous march.
    // Decision: Keep the helper because it makes the diagnostic wake seed explicit while preserving the legacy gap-shape law.
    private static ViscousBranchSeed BuildWakeSeed(
        IReadOnlyList<BoundaryLayerStation> stations,
        WakeGeometry wake,
        double normalGap,
        double trailingEdgeGap)
    {
        var viscousStations = new List<ViscousStationSeed>(stations.Count);
        var sharpTrailingEdge = trailingEdgeGap < 1e-4;
        var wakeGapDerivative = ComputeWakeGapDerivative(wake);

        for (var index = 0; index < stations.Count; index++)
        {
            var station = stations[index];
            var wakePoint = wake.Points[Math.Min(index, wake.Points.Count - 1)];
            var wakeGap = WakeGapProfile.Evaluate(
                normalGap,
                wakePoint.DistanceFromTrailingEdge,
                wakeGapDerivative,
                sharpTrailingEdge);

            viscousStations.Add(new ViscousStationSeed(
                index,
                station.Location,
                station.DistanceFromStagnation,
                Math.Max(0d, wakePoint.VelocityMagnitude),
                Math.Max(0d, wakeGap)));
        }

        return new ViscousBranchSeed(BoundaryLayerBranch.Wake, viscousStations);
    }

    // Legacy mapping: f_xfoil/src/xfoil.f :: TECALC.
    // Difference from legacy: The geometric relations are the same, but the managed port exposes them as a helper returning a tuple instead of updating shared TE state.
    // Decision: Keep the helper because it is the cleanest way to feed the managed seed object.
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

    // Legacy mapping: f_xfoil/src/xpanel.f :: XYWAKE wake-gap derivative lineage.
    // Difference from legacy: The derivative extraction is factored into a tiny helper around WakeGapProfile instead of being embedded in the wake marching routine.
    // Decision: Keep the helper because it keeps the managed wake-seed assembly readable.
    private static double ComputeWakeGapDerivative(WakeGeometry wake)
    {
        if (wake.Points.Count < 2)
        {
            return 0d;
        }

        return WakeGapProfile.ComputeDerivativeFromTangentY(wake.Points[1].TangentY);
    }
}
