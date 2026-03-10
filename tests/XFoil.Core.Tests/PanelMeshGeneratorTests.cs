using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class PanelMeshGeneratorTests
{
    [Fact]
    public void DefaultPaneling_ClustersMoreStronglyNearLeadingEdgeThanUniformPaneling()
    {
        var generator = new NacaAirfoilGenerator();
        var meshGenerator = new PanelMeshGenerator();
        var geometry = generator.Generate4Digit("0012", 161);

        var weightedMesh = meshGenerator.Generate(geometry, 120, new PanelingOptions());
        var uniformMesh = meshGenerator.Generate(geometry, 120, new PanelingOptions(0d, 0d, 0d));

        var weightedLeadingEdgePanel = weightedMesh.Panels.MinBy(panel => panel.ControlPoint.X)!;
        var uniformLeadingEdgePanel = uniformMesh.Panels.MinBy(panel => panel.ControlPoint.X)!;

        Assert.True(weightedLeadingEdgePanel.Length < uniformLeadingEdgePanel.Length);
    }

    [Fact]
    public void GeneratedMesh_IsClosedAndCounterClockwise()
    {
        var generator = new NacaAirfoilGenerator();
        var meshGenerator = new PanelMeshGenerator();
        var geometry = generator.Generate4Digit("2412", 161);

        var mesh = meshGenerator.Generate(geometry, 120);

        Assert.Equal(mesh.Nodes[0], mesh.Nodes[^1]);
        Assert.True(mesh.IsCounterClockwise);
    }
}
