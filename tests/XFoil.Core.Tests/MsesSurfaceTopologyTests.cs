using XFoil.ThesisClosureSolver.Inviscid;
using XFoil.ThesisClosureSolver.Topology;

namespace XFoil.Core.Tests;

/// <summary>
/// R5.2 — per-surface topology tests.
/// </summary>
public class MsesSurfaceTopologyTests
{
    [Fact]
    public void Build_Naca0012_Alpha0_UpperLowerAreEqualSize()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var stag = StagnationDetector.DetectFromGeometry(pg, 1.0, 0.0);
        var t = SurfaceTopology.Build(pg, stag);
        // Symmetric α=0: upper and lower surfaces have equal panel
        // counts within ±2 (snap rounding — detected PanelIndex may
        // be off by one panel depending on where the midpoint-Ue
        // zero lands relative to the LE node; combined with the
        // ≥0.5 → next-node snap this can produce a 2-panel offset).
        int diff = System.Math.Abs(t.Upper.PanelIndices.Length - t.Lower.PanelIndices.Length);
        Assert.True(diff <= 2,
            $"Symmetric α=0: upper {t.Upper.PanelIndices.Length} vs lower {t.Lower.PanelIndices.Length} differ by {diff}");
    }

    [Fact]
    public void Build_Naca0012_Alpha0_TotalPanelsConserved()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var stag = StagnationDetector.DetectFromGeometry(pg, 1.0, 0.0);
        var t = SurfaceTopology.Build(pg, stag);
        Assert.Equal(pg.PanelCount,
            t.Upper.PanelIndices.Length + t.Lower.PanelIndices.Length);
    }

    [Fact]
    public void Build_UpperWalksFromStagToTE_IndicesDescending()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("2412", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var stag = StagnationDetector.DetectFromGeometry(
            pg, 1.0, 2.0 * System.Math.PI / 180.0);
        var t = SurfaceTopology.Build(pg, stag);
        // Upper station 0 is the panel just before stag; last is panel 0.
        for (int k = 1; k < t.Upper.PanelIndices.Length; k++)
        {
            Assert.True(t.Upper.PanelIndices[k] < t.Upper.PanelIndices[k - 1],
                $"Upper panel indices should decrease (toward TE_upper at 0)");
        }
        Assert.Equal(0, t.Upper.PanelIndices[t.Upper.PanelIndices.Length - 1]);
    }

    [Fact]
    public void Build_LowerWalksFromStagToTE_IndicesAscending()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("2412", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var stag = StagnationDetector.DetectFromGeometry(
            pg, 1.0, 2.0 * System.Math.PI / 180.0);
        var t = SurfaceTopology.Build(pg, stag);
        for (int k = 1; k < t.Lower.PanelIndices.Length; k++)
        {
            Assert.True(t.Lower.PanelIndices[k] > t.Lower.PanelIndices[k - 1],
                "Lower panel indices should increase (toward TE_lower at N-1)");
        }
        Assert.Equal(pg.PanelCount - 1,
            t.Lower.PanelIndices[t.Lower.PanelIndices.Length - 1]);
    }

    [Fact]
    public void Build_ArcLength_StartsNearZeroAndIncreases()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var stag = StagnationDetector.DetectFromGeometry(pg, 1.0, 0.0);
        var t = SurfaceTopology.Build(pg, stag);
        Assert.True(t.Upper.ArcLength[0] > 0 && t.Upper.ArcLength[0] < 0.02,
            $"Upper first station s={t.Upper.ArcLength[0]} should be small (half a panel)");
        for (int k = 1; k < t.Upper.ArcLength.Length; k++)
        {
            Assert.True(t.Upper.ArcLength[k] > t.Upper.ArcLength[k - 1],
                "Upper arc-length should monotonically increase");
        }
        // Max arc-length ≈ half-perimeter.
        Assert.InRange(t.Upper.ArcLength[t.Upper.ArcLength.Length - 1], 0.9, 1.3);
    }

    [Fact]
    public void Build_RejectsStagnationAtPanel0()
    {
        // If stag is at panel 0 (or clamped there), upper would have
        // 0 panels. Verify we handle it — clamp to leave ≥1 on each side.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var fakeStag = new StagnationDetector.StagnationLocation(
            PanelIndex: 0, InterpolationFraction: 0.0);
        var t = SurfaceTopology.Build(pg, fakeStag);
        Assert.True(t.Upper.PanelIndices.Length >= 1);
        Assert.True(t.Lower.PanelIndices.Length >= 1);
    }
}
