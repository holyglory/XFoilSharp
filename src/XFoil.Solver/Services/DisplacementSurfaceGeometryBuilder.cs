using XFoil.Core.Models;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

public sealed class DisplacementSurfaceGeometryBuilder
{
    private const double MaximumProjectedDisplacement = 0.02d;

    public (AirfoilGeometry Geometry, double MaxSurfaceDisplacement) Build(
        PanelMesh mesh,
        ViscousStateEstimate state,
        string name,
        double relaxation = 0.5d)
    {
        if (mesh is null)
        {
            throw new ArgumentNullException(nameof(mesh));
        }

        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A geometry name is required.", nameof(name));
        }

        if (relaxation <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(relaxation), "Relaxation must be positive.");
        }

        var displacements = BuildDisplacementLookup(state);
        var points = new List<AirfoilPoint>(mesh.Nodes.Count - 1);
        var maxDisplacement = 0d;

        for (var nodeIndex = 0; nodeIndex < mesh.Nodes.Count - 1; nodeIndex++)
        {
            var point = mesh.Nodes[nodeIndex];
            var requestedDisplacement = displacements.TryGetValue(point, out var thickness)
                ? relaxation * thickness
                : 0d;
            var displacement = Math.Clamp(
                requestedDisplacement,
                -MaximumProjectedDisplacement,
                MaximumProjectedDisplacement);
            maxDisplacement = Math.Max(maxDisplacement, Math.Abs(displacement));

            var previousPanel = mesh.Panels[nodeIndex == 0 ? mesh.Panels.Count - 1 : nodeIndex - 1];
            var nextPanel = mesh.Panels[nodeIndex];
            var normalX = previousPanel.NormalX + nextPanel.NormalX;
            var normalY = previousPanel.NormalY + nextPanel.NormalY;
            var magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY));
            if (magnitude <= 1e-12)
            {
                normalX = nextPanel.NormalX;
                normalY = nextPanel.NormalY;
                magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY));
            }

            normalX /= magnitude;
            normalY /= magnitude;

            points.Add(new AirfoilPoint(
                point.X + (displacement * normalX),
                point.Y + (displacement * normalY)));
        }

        return (
            new AirfoilGeometry($"{name} displaced", points, AirfoilFormat.PlainCoordinates),
            maxDisplacement);
    }

    private static Dictionary<AirfoilPoint, double> BuildDisplacementLookup(ViscousStateEstimate state)
    {
        var lookup = new Dictionary<AirfoilPoint, double>();
        AddBranchDisplacements(lookup, state.UpperSurface);
        AddBranchDisplacements(lookup, state.LowerSurface);
        return lookup;
    }

    private static void AddBranchDisplacements(Dictionary<AirfoilPoint, double> lookup, ViscousBranchState branch)
    {
        for (var index = 1; index < branch.Stations.Count; index++)
        {
            var station = branch.Stations[index];
            lookup[station.Location] = station.DisplacementThickness;
        }
    }
}
