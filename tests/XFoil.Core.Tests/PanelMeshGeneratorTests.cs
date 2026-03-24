using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: PANGEN
// Secondary legacy source: f_xfoil/src/spline.f
// Role in port: Verifies the managed panel-mesh generator derived from the legacy panel distribution workflow.
// Differences: The managed generator exposes paneling options directly and returns immutable mesh objects instead of filling legacy common arrays.
// Decision: Keep the managed generator surface because it preserves the paneling behavior while making it independently reusable.
namespace XFoil.Core.Tests;

public sealed class PanelMeshGeneratorTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xpanel.f :: PANGEN weighted clustering.
    // Difference from legacy: The test compares managed option sets directly instead of observing node placement indirectly after a full solve.
    // Decision: Keep the managed comparison because it isolates the intended leading-edge clustering behavior.
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
    // Legacy mapping: f_xfoil/src/xpanel.f :: PANGEN contour closure and orientation checks.
    // Difference from legacy: The managed mesh object exposes closure and orientation explicitly instead of through downstream solver assumptions.
    // Decision: Keep the managed mesh-invariant test because it documents the preprocessing contract clearly.
    public void GeneratedMesh_IsClosedAndCounterClockwise()
    {
        var generator = new NacaAirfoilGenerator();
        var meshGenerator = new PanelMeshGenerator();
        var geometry = generator.Generate4Digit("2412", 161);

        var mesh = meshGenerator.Generate(geometry, 120);

        Assert.Equal(mesh.Nodes[0], mesh.Nodes[^1]);
        Assert.True(mesh.IsCounterClockwise);
    }

    [Fact]
    // Legacy mapping: XFoil can run coarse panel counts for debugging and cheap exploratory solves.
    // Difference from legacy: The managed port previously rejected counts below 16 even though coarse legacy runs remain useful for parity work.
    // Decision: Keep 12-panel support so the parity harness can use tiny reproducible cases without patching callers.
    public void GeneratedMesh_SupportsTwelvePanelsForParityScouting()
    {
        var generator = new NacaAirfoilGenerator();
        var meshGenerator = new PanelMeshGenerator();
        var geometry = generator.Generate4Digit("0012", 161);

        var mesh = meshGenerator.Generate(geometry, 12);

        Assert.Equal(13, mesh.Nodes.Count);
        Assert.Equal(12, mesh.Panels.Count);
        Assert.Equal(mesh.Nodes[0], mesh.Nodes[^1]);
    }
}
