using XFoil.Core.Models;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class BoundaryLayerTopologyBuilder
{
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

    private static double[] BuildNodeArcLengths(PanelMesh mesh)
    {
        var nodeArcLengths = new double[mesh.Panels.Count + 1];
        for (var index = 0; index < mesh.Panels.Count; index++)
        {
            nodeArcLengths[index + 1] = nodeArcLengths[index] + mesh.Panels[index].Length;
        }

        return nodeArcLengths;
    }

    private static double[] BuildPanelArcLengths(IReadOnlyList<double> nodeArcLengths)
    {
        var panelArcLengths = new double[nodeArcLengths.Count - 1];
        for (var index = 0; index < panelArcLengths.Length; index++)
        {
            panelArcLengths[index] = 0.5d * (nodeArcLengths[index] + nodeArcLengths[index + 1]);
        }

        return panelArcLengths;
    }

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

    private static double InterpolateZeroCrossing(double value1, double value2)
    {
        var denominator = value2 - value1;
        if (Math.Abs(denominator) <= 1e-12)
        {
            return 0.5d;
        }

        return Math.Clamp(-value1 / denominator, 0d, 1d);
    }

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
