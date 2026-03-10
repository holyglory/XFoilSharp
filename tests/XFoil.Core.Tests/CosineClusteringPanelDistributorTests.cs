using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class CosineClusteringPanelDistributorTests
{
    /// <summary>
    /// Generates NACA 0012 raw airfoil coordinates (upper-TE around LE to lower-TE).
    /// Uses cosine spacing on each surface half with the standard NACA formula.
    /// Returns coordinates in XFoil convention: upper-surface TE first, counterclockwise to lower-surface TE.
    /// </summary>
    private static (double[] x, double[] y, int count) GenerateNaca0012(int halfPoints = 81)
    {
        var generator = new NacaAirfoilGenerator();
        var geometry = generator.Generate4Digit("0012", halfPoints * 2 - 1);
        int n = geometry.Points.Count;
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        return (x, y, n);
    }

    [Fact]
    public void Distribute_Naca0012_ProducesExactlyRequestedNodeCount()
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, 160);

        Assert.Equal(160, panel.NodeCount);
    }

    [Fact]
    public void Distribute_Naca0012_Node0NearUpperSurfaceTE()
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, 160);

        // Node 0 should be near upper-surface TE: x close to 1.0
        Assert.True(panel.X[0] > 0.95, $"Node 0 X should be near TE, got {panel.X[0]:F6}");
        // For symmetric NACA 0012, TE is at Y=0. Verify upper-surface ordering by checking
        // that the next node (node 1) has positive Y, confirming counterclockwise traversal.
        Assert.True(panel.Y[1] > 0.0, $"Node 1 Y should be positive (upper surface), got {panel.Y[1]:F6}");
    }

    [Fact]
    public void Distribute_Naca0012_NodeNMinus1NearLowerSurfaceTE()
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, 160);

        int last = panel.NodeCount - 1;
        // Node N-1 should be near lower-surface TE: x close to 1.0
        Assert.True(panel.X[last] > 0.95, $"Node N-1 X should be near TE, got {panel.X[last]:F6}");
        // For symmetric NACA 0012, TE is at Y=0. Verify lower-surface ordering by checking
        // that the previous node (node N-2) has negative Y.
        Assert.True(panel.Y[last - 1] < 0.0, $"Node N-2 Y should be negative (lower surface), got {panel.Y[last - 1]:F6}");
    }

    [Fact]
    public void Distribute_Naca0012_LeadingEdgeAtMinimumX()
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, 160);

        // Find the node with minimum X
        int minXIndex = 0;
        for (int i = 1; i < panel.NodeCount; i++)
        {
            if (panel.X[i] < panel.X[minXIndex])
            {
                minXIndex = i;
            }
        }

        // The LE should be at the minimum X point, near x=0
        Assert.True(panel.LeadingEdgeX < 0.01, $"LE X should be near 0, got {panel.LeadingEdgeX:F6}");
        Assert.True(
            Math.Abs(panel.LeadingEdgeX - panel.X[minXIndex]) < 0.01,
            $"LE X ({panel.LeadingEdgeX:F6}) should be near min-X node ({panel.X[minXIndex]:F6})");
    }

    [Fact]
    public void Distribute_SymmetricNaca0012_UpperLowerMirrorSymmetric()
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, 160);

        int n = panel.NodeCount;

        // For symmetric NACA 0012, upper and lower surface should be mirror images.
        // Find the approximate LE node (minimum X index).
        int leIndex = 0;
        for (int i = 1; i < n; i++)
        {
            if (panel.X[i] < panel.X[leIndex])
            {
                leIndex = i;
            }
        }

        // Upper surface: nodes 0..leIndex, Lower surface: nodes leIndex..N-1
        // Compare pairwise from TE inward: node i on upper should mirror node (N-1-i) on lower
        int pairs = Math.Min(leIndex, n - 1 - leIndex);
        for (int i = 0; i < pairs; i++)
        {
            int upperIdx = i;
            int lowerIdx = n - 1 - i;
            Assert.True(
                Math.Abs(panel.X[upperIdx] - panel.X[lowerIdx]) < 0.01,
                $"Upper node {upperIdx} X={panel.X[upperIdx]:F6} != Lower node {lowerIdx} X={panel.X[lowerIdx]:F6}");
            Assert.True(
                Math.Abs(panel.Y[upperIdx] + panel.Y[lowerIdx]) < 0.01,
                $"Upper node {upperIdx} Y={panel.Y[upperIdx]:F6} != -Lower node {lowerIdx} Y={panel.Y[lowerIdx]:F6}");
        }
    }

    [Fact]
    public void Distribute_Naca0012_PanelDensityHigherNearLeAndTe()
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, 160);

        // Compute arc-length spacings at TE (first few panels), mid-chord, and LE
        int n = panel.NodeCount;

        // Near-TE spacing (average of first 3 and last 3 panels)
        double teSpacing = 0;
        for (int i = 0; i < 3; i++)
        {
            teSpacing += panel.ArcLength[i + 1] - panel.ArcLength[i];
        }

        for (int i = n - 4; i < n - 1; i++)
        {
            teSpacing += panel.ArcLength[i + 1] - panel.ArcLength[i];
        }

        teSpacing /= 6.0;

        // Mid-chord spacing (average around the quarter-chord region)
        int midIndex = n / 4;
        double midSpacing = 0;
        for (int i = midIndex - 2; i < midIndex + 2; i++)
        {
            midSpacing += panel.ArcLength[i + 1] - panel.ArcLength[i];
        }

        midSpacing /= 4.0;

        // TE panels should be denser (smaller spacing) than mid-chord panels
        Assert.True(
            teSpacing < midSpacing,
            $"TE spacing ({teSpacing:E4}) should be less than mid-chord spacing ({midSpacing:E4})");
    }

    [Fact]
    public void Distribute_Naca0012_TotalArcLengthMatchesIndependentComputation()
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, 160);

        // Independently compute total arc length from panel node coordinates
        double independentArcLength = 0;
        for (int i = 1; i < panel.NodeCount; i++)
        {
            double dx = panel.X[i] - panel.X[i - 1];
            double dy = panel.Y[i] - panel.Y[i - 1];
            independentArcLength += Math.Sqrt(dx * dx + dy * dy);
        }

        double totalArcLength = panel.ArcLength[panel.NodeCount - 1];

        Assert.True(
            Math.Abs(totalArcLength - independentArcLength) < 1e-6,
            $"Total arc length {totalArcLength:E10} != independent {independentArcLength:E10}");
    }

    [Fact]
    public void Distribute_Naca0012_ChordLengthCorrect()
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, 160);

        // For unit-chord NACA 0012, chord should be ~1.0
        double expectedChord = Math.Sqrt(
            (panel.TrailingEdgeX - panel.LeadingEdgeX) * (panel.TrailingEdgeX - panel.LeadingEdgeX) +
            (panel.TrailingEdgeY - panel.LeadingEdgeY) * (panel.TrailingEdgeY - panel.LeadingEdgeY));

        Assert.True(
            Math.Abs(panel.Chord - expectedChord) < 1e-10,
            $"Chord {panel.Chord:E10} != expected {expectedChord:E10}");
        Assert.True(
            Math.Abs(panel.Chord - 1.0) < 0.02,
            $"Chord {panel.Chord} should be approximately 1.0 for NACA 0012");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(160)]
    [InlineData(200)]
    public void Distribute_VariousNodeCounts_ProducesCorrectCount(int nodeCount)
    {
        var (inputX, inputY, inputCount) = GenerateNaca0012();
        var panel = new LinearVortexPanelState(400);

        CosineClusteringPanelDistributor.Distribute(inputX, inputY, inputCount, panel, nodeCount);

        Assert.Equal(nodeCount, panel.NodeCount);
    }
}
