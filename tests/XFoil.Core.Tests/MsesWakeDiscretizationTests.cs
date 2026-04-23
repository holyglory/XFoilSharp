using XFoil.MsesSolver.Topology;

namespace XFoil.Core.Tests;

/// <summary>
/// R5.3 — wake panel discretization tests.
/// </summary>
public class MsesWakeDiscretizationTests
{
    [Fact]
    public void Build_TotalLengthMatchesConfigured()
    {
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: 0.01,
            panelCount: 20,
            totalLength: 0.5);
        double totalActual = 0;
        foreach (var L in wake.Length) totalActual += L;
        Assert.InRange(totalActual, 0.499, 0.501);
    }

    [Fact]
    public void Build_FirstPanelLengthMatchesConfigured()
    {
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: 0.005,
            panelCount: 25,
            totalLength: 0.5);
        Assert.Equal(0.005, wake.Length[0], 10);
    }

    [Fact]
    public void Build_PanelLengthsIncrease_GeometricStretching()
    {
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: 0.005,
            panelCount: 20,
            totalLength: 0.5);
        for (int i = 1; i < wake.Length.Length; i++)
        {
            Assert.True(wake.Length[i] > wake.Length[i - 1] - 1e-12,
                $"Panel {i} length {wake.Length[i]} should be ≥ previous {wake.Length[i-1]}");
        }
    }

    [Fact]
    public void Build_AlphaZero_WakeGoesStraightInX()
    {
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: 0.01,
            panelCount: 20,
            totalLength: 0.5,
            alphaRadians: 0.0);
        // All y coords stay at 0 (wake goes pure +x).
        for (int i = 0; i < wake.NodeY.Length; i++)
        {
            Assert.Equal(0.0, wake.NodeY[i], 12);
        }
        // Last node at x = 1 + 0.5 = 1.5.
        Assert.InRange(wake.NodeX[wake.NodeX.Length - 1], 1.499, 1.501);
    }

    [Fact]
    public void Build_PositiveAlpha_WakeTiltsUpstream()
    {
        double a = 10.0 * System.Math.PI / 180.0;
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: 0.01,
            panelCount: 20,
            totalLength: 0.5,
            alphaRadians: a);
        // At α=10°, the wake direction is (cos α, sin α). Last node
        // should be at y = 0 + 0.5·sin(10°) ≈ 0.0868.
        Assert.InRange(wake.NodeY[wake.NodeY.Length - 1], 0.08, 0.09);
    }

    [Fact]
    public void Build_TangentsAndNormals_AreOrthonormal()
    {
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: 0.005,
            panelCount: 20,
            totalLength: 0.5,
            alphaRadians: 5.0 * System.Math.PI / 180.0);
        for (int i = 0; i < wake.TangentX.Length; i++)
        {
            double tMag = System.Math.Sqrt(wake.TangentX[i] * wake.TangentX[i]
                                         + wake.TangentY[i] * wake.TangentY[i]);
            double nMag = System.Math.Sqrt(wake.NormalX[i] * wake.NormalX[i]
                                         + wake.NormalY[i] * wake.NormalY[i]);
            double dot = wake.TangentX[i] * wake.NormalX[i]
                       + wake.TangentY[i] * wake.NormalY[i];
            Assert.Equal(1.0, tMag, 12);
            Assert.Equal(1.0, nMag, 12);
            Assert.Equal(0.0, dot, 12);
        }
    }

    [Fact]
    public void Build_ArcLengthFromTE_MonotonicallyIncreases()
    {
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: 0.005,
            panelCount: 20,
            totalLength: 0.5);
        for (int i = 1; i < wake.ArcLengthFromTE.Length; i++)
        {
            Assert.True(wake.ArcLengthFromTE[i] > wake.ArcLengthFromTE[i - 1],
                "Arc-length must monotonically increase");
        }
    }

    [Fact]
    public void Build_RejectsTooFewPanels()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => WakeDiscretization.Build(
                1.0, 0.0, 0.01, panelCount: 1, totalLength: 0.5));
    }

    [Fact]
    public void Build_RejectsTotalLengthNotExceedingFirst()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => WakeDiscretization.Build(
                1.0, 0.0, 0.01, panelCount: 20, totalLength: 0.005));
    }
}
