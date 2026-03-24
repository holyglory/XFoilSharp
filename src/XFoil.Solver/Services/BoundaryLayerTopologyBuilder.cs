using XFoil.Core.Models;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: STFIND
// Secondary legacy source: f_xfoil/src/xbl.f :: station ordering and wake-branch bookkeeping
// Role in port: Builds a stagnation-centered upper/lower/wake topology from managed inviscid results.
// Differences: The topology logic is derived from the legacy stagnation and branch-ordering routines, but it works from high-level managed mesh/result objects rather than the original COMMON-backed panel and BL arrays.
// Decision: Keep the managed topology builder because it is clearer for diagnostics and setup code, while the stricter parity path still uses the fuller XFoil station mapping inside ViscousSolverEngine.
namespace XFoil.Solver.Services;

public sealed class BoundaryLayerTopologyBuilder
{
    // Legacy mapping: f_xfoil/src/xpanel.f :: STFIND combined with xbl.f branch setup conventions.
    // Difference from legacy: The builder assembles an immutable topology object from managed inviscid results instead of mutating the global XFoil station tables in place.
    // Decision: Keep the managed builder because it is easier to inspect and test outside the direct parity march.
    public BoundaryLayerTopology Build(InviscidAnalysisResult analysis)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        var mesh = analysis.Mesh;
        if (mesh.Panels.Count == 0)
        {
            throw new ArgumentException("Analysis mesh must contain panels.", nameof(analysis));
        }

        var nodeArcLengths = BuildNodeArcLengths(mesh);
        var panelArcLengths = BuildPanelArcLengths(nodeArcLengths);
        var tangentialVelocities = analysis.PressureSamples.Select(sample => sample.TangentialVelocity).ToArray();
        var stagnation = FindStagnationLocation(mesh, panelArcLengths, tangentialVelocities);
        var upperStations = BuildUpperStations(mesh, nodeArcLengths, tangentialVelocities, stagnation.ArcLength, stagnation.Point);
        var lowerStations = BuildLowerStations(mesh, nodeArcLengths, tangentialVelocities, stagnation.ArcLength, stagnation.Point);
        var wakeStations = BuildWakeStations(analysis.Wake, lowerStations[^1].DistanceFromStagnation);

        return new BoundaryLayerTopology(
            stagnation.Point,
            stagnation.ArcLength,
            stagnation.PanelIndex,
            upperStations,
            lowerStations,
            wakeStations);
    }

    // Legacy mapping: managed-derived helper from xpanel.f / spline arc-length bookkeeping.
    // Difference from legacy: The managed code accumulates node arc length directly from panel lengths that are already materialized in the mesh.
    // Decision: Keep the helper because it is the natural representation at this abstraction layer.
    private static double[] BuildNodeArcLengths(PanelMesh mesh)
    {
        var nodeArcLengths = new double[mesh.Panels.Count + 1];
        for (var index = 0; index < mesh.Panels.Count; index++)
        {
            nodeArcLengths[index + 1] = nodeArcLengths[index] + mesh.Panels[index].Length;
        }

        return nodeArcLengths;
    }

    // Legacy mapping: managed-derived midpoint arc-length reconstruction matching legacy panel-center usage.
    // Difference from legacy: The legacy code stores related quantities across multiple arrays; the managed port derives them on demand from node arc lengths.
    // Decision: Keep the helper because it makes the midpoint convention explicit without changing behavior.
    private static double[] BuildPanelArcLengths(IReadOnlyList<double> nodeArcLengths)
    {
        var panelArcLengths = new double[nodeArcLengths.Count - 1];
        for (var index = 0; index < panelArcLengths.Length; index++)
        {
            panelArcLengths[index] = 0.5d * (nodeArcLengths[index] + nodeArcLengths[index + 1]);
        }

        return panelArcLengths;
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: STFIND.
    // Difference from legacy: The sign-change search is the same general idea, but the managed code ranks candidates by geometric score and falls back to the minimum-|Ue| panel when no sign change is found.
    // Decision: Keep the managed fallback because it is robust for diagnostics, while the exact parity path uses the fuller STFIND replay elsewhere.
    private static StagnationLocation FindStagnationLocation(
        PanelMesh mesh,
        IReadOnlyList<double> panelArcLengths,
        IReadOnlyList<double> tangentialVelocities)
    {
        var bestPanelIndex = -1;
        var bestScore = double.PositiveInfinity;
        var bestArcLength = panelArcLengths[0];
        var bestPoint = mesh.Panels[0].ControlPoint;

        for (var panelIndex = 0; panelIndex < tangentialVelocities.Count - 1; panelIndex++)
        {
            var velocity1 = tangentialVelocities[panelIndex];
            var velocity2 = tangentialVelocities[panelIndex + 1];
            if (velocity1 * velocity2 > 0d)
            {
                continue;
            }

            var point1 = mesh.Panels[panelIndex].ControlPoint;
            var point2 = mesh.Panels[panelIndex + 1].ControlPoint;
            var score = 0.5d * (point1.X + point2.X);
            if (score >= bestScore)
            {
                continue;
            }

            var interpolation = InterpolateZeroCrossing(velocity1, velocity2);
            bestScore = score;
            bestPanelIndex = panelIndex;
            bestArcLength = panelArcLengths[panelIndex] + (interpolation * (panelArcLengths[panelIndex + 1] - panelArcLengths[panelIndex]));
            bestPoint = Interpolate(point1, point2, interpolation);
        }

        if (bestPanelIndex >= 0)
        {
            return new StagnationLocation(bestPoint, bestArcLength, bestPanelIndex);
        }

        var minimumVelocityIndex = 0;
        for (var panelIndex = 1; panelIndex < tangentialVelocities.Count; panelIndex++)
        {
            if (Math.Abs(tangentialVelocities[panelIndex]) < Math.Abs(tangentialVelocities[minimumVelocityIndex]))
            {
                minimumVelocityIndex = panelIndex;
            }
        }

        return new StagnationLocation(
            mesh.Panels[minimumVelocityIndex].ControlPoint,
            panelArcLengths[minimumVelocityIndex],
            minimumVelocityIndex);
    }

    // Legacy mapping: xbl.f upper-surface station ordering derived from stagnation-centered march setup.
    // Difference from legacy: The managed code materializes BoundaryLayerStation records directly instead of filling side-indexed station arrays.
    // Decision: Keep the record-based station list because it is clearer for downstream managed consumers.
    private static List<BoundaryLayerStation> BuildUpperStations(
        PanelMesh mesh,
        IReadOnlyList<double> nodeArcLengths,
        IReadOnlyList<double> tangentialVelocities,
        double stagnationArcLength,
        AirfoilPoint stagnationPoint)
    {
        var stations = new List<BoundaryLayerStation>
        {
            new(BoundaryLayerBranch.Upper, 0, stagnationPoint, 0d, 0d)
        };

        var stationIndex = 1;
        for (var nodeIndex = nodeArcLengths.Count - 2; nodeIndex >= 0; nodeIndex--)
        {
            var arcLength = nodeArcLengths[nodeIndex];
            if (arcLength >= stagnationArcLength)
            {
                continue;
            }

            stations.Add(new BoundaryLayerStation(
                BoundaryLayerBranch.Upper,
                stationIndex++,
                mesh.Nodes[nodeIndex],
                stagnationArcLength - arcLength,
                EstimateNodeEdgeVelocity(tangentialVelocities, nodeIndex)));
        }

        return stations;
    }

    // Legacy mapping: xbl.f lower-surface station ordering derived from stagnation-centered march setup.
    // Difference from legacy: As with the upper branch, the managed port returns explicit station records instead of mutating legacy side arrays.
    // Decision: Keep the explicit station list representation.
    private static List<BoundaryLayerStation> BuildLowerStations(
        PanelMesh mesh,
        IReadOnlyList<double> nodeArcLengths,
        IReadOnlyList<double> tangentialVelocities,
        double stagnationArcLength,
        AirfoilPoint stagnationPoint)
    {
        var stations = new List<BoundaryLayerStation>
        {
            new(BoundaryLayerBranch.Lower, 0, stagnationPoint, 0d, 0d)
        };

        var stationIndex = 1;
        for (var nodeIndex = 1; nodeIndex < nodeArcLengths.Count - 1; nodeIndex++)
        {
            var arcLength = nodeArcLengths[nodeIndex];
            if (arcLength <= stagnationArcLength)
            {
                continue;
            }

            stations.Add(new BoundaryLayerStation(
                BoundaryLayerBranch.Lower,
                stationIndex++,
                mesh.Nodes[nodeIndex],
                arcLength - stagnationArcLength,
                EstimateNodeEdgeVelocity(tangentialVelocities, nodeIndex)));
        }

        return stations;
    }

    // Legacy mapping: xbl.f wake-branch station ordering derived from the trailing-edge continuation of the surface march.
    // Difference from legacy: The managed builder consumes a precomputed WakeGeometry object rather than constructing wake stations inside the viscous march setup.
    // Decision: Keep the split because it makes wake diagnostics and testing much simpler.
    private static List<BoundaryLayerStation> BuildWakeStations(WakeGeometry wake, double trailingEdgeDistanceFromStagnation)
    {
        var stations = new List<BoundaryLayerStation>(wake.Points.Count);
        for (var index = 0; index < wake.Points.Count; index++)
        {
            var point = wake.Points[index];
            stations.Add(new BoundaryLayerStation(
                BoundaryLayerBranch.Wake,
                index,
                point.Location,
                trailingEdgeDistanceFromStagnation + point.DistanceFromTrailingEdge,
                point.VelocityMagnitude));
        }

        return stations;
    }

    // Legacy mapping: managed-derived approximation of nodewise edge velocity reconstruction from panel-centered inviscid speeds.
    // Difference from legacy: The port averages adjacent panel samples directly rather than replaying the exact side-array interpolation used in the original BL bookkeeping.
    // Decision: Keep the helper because this topology builder is not the parity reference for Ue reconstruction.
    private static double EstimateNodeEdgeVelocity(IReadOnlyList<double> tangentialVelocities, int nodeIndex)
    {
        if (nodeIndex <= 0)
        {
            return Math.Abs(tangentialVelocities[0]);
        }

        if (nodeIndex >= tangentialVelocities.Count)
        {
            return Math.Abs(tangentialVelocities[^1]);
        }

        return 0.5d * (Math.Abs(tangentialVelocities[nodeIndex - 1]) + Math.Abs(tangentialVelocities[nodeIndex]));
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: STFIND sign-change interpolation.
    // Difference from legacy: The managed code factors the zero-crossing fraction into a reusable helper and clamps the result defensively.
    // Decision: Keep the helper and the clamp because they make the stagnation search more robust in standalone use.
    private static double InterpolateZeroCrossing(double value1, double value2)
    {
        var denominator = value2 - value1;
        if (Math.Abs(denominator) <= 1e-12)
        {
            return 0.5d;
        }

        return Math.Clamp(-value1 / denominator, 0d, 1d);
    }

    // Legacy mapping: managed-only geometric interpolation helper with no standalone Fortran analogue.
    // Difference from legacy: The original code performs this interpolation inline while updating multiple coupled arrays.
    // Decision: Keep the helper because it improves readability in the managed topology builder.
    private static AirfoilPoint Interpolate(AirfoilPoint point1, AirfoilPoint point2, double interpolation)
    {
        return new AirfoilPoint(
            point1.X + (interpolation * (point2.X - point1.X)),
            point1.Y + (interpolation * (point2.Y - point1.Y)));
    }

    private readonly record struct StagnationLocation(
        AirfoilPoint Point,
        double ArcLength,
        int PanelIndex);
}
