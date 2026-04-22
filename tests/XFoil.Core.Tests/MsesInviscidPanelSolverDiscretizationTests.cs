using XFoil.MsesSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P1.1 — panel discretization unit tests for
/// <see cref="MsesInviscidPanelSolver.DiscretizePanels"/>.
/// </summary>
public class MsesInviscidPanelSolverDiscretizationTests
{
    [Fact]
    public void DiscretizePanels_Naca0012_PanelCountIsNodeCountMinusOne()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        Assert.Equal(161 - 1, pg.PanelCount);
        Assert.Equal(161, pg.NodeX.Length);
        Assert.Equal(161 - 1, pg.MidX.Length);
    }

    [Fact]
    public void DiscretizePanels_UnitLengthTangents()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        for (int i = 0; i < pg.PanelCount; i++)
        {
            double mag = System.Math.Sqrt(
                pg.TangentX[i] * pg.TangentX[i]
                + pg.TangentY[i] * pg.TangentY[i]);
            Assert.True(System.Math.Abs(mag - 1.0) < 1e-12,
                $"Panel {i} tangent magnitude {mag}, expected 1.0");
        }
    }

    [Fact]
    public void DiscretizePanels_NormalsPerpendicularToTangents()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("2412", pointCount: 101);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        for (int i = 0; i < pg.PanelCount; i++)
        {
            double dot = pg.TangentX[i] * pg.NormalX[i]
                       + pg.TangentY[i] * pg.NormalY[i];
            Assert.True(System.Math.Abs(dot) < 1e-12,
                $"Panel {i}: tangent·normal = {dot}, expected 0");
        }
    }

    [Fact]
    public void DiscretizePanels_MidpointsEquidistantFromNodes()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 201);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        for (int i = 0; i < pg.PanelCount; i++)
        {
            double dxA = pg.MidX[i] - pg.NodeX[i];
            double dyA = pg.MidY[i] - pg.NodeY[i];
            double dxB = pg.NodeX[i + 1] - pg.MidX[i];
            double dyB = pg.NodeY[i + 1] - pg.MidY[i];
            double dA = System.Math.Sqrt(dxA * dxA + dyA * dyA);
            double dB = System.Math.Sqrt(dxB * dxB + dyB * dyB);
            Assert.True(System.Math.Abs(dA - dB) < 1e-12,
                $"Panel {i}: midpoint asymmetric ({dA} vs {dB})");
        }
    }

    [Fact]
    public void DiscretizePanels_SymmetricAirfoil_ProducesSymmetricNormals()
    {
        // Symmetric airfoil: the normal at panel i (walking LE→TE
        // on upper) should match the y-flipped normal at the
        // mirror-image panel (walking TE→LE on lower).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        // With 161 nodes (160 panels), the TE is at index 0 and 160.
        // Upper surface: panels 0..79 (from TE to LE).
        // Lower surface: panels 80..159 (from LE to TE).
        // Panel i on upper mirrors panel (159 - i) on lower.
        for (int i = 0; i < 80; i++)
        {
            int mirror = 159 - i;
            Assert.True(System.Math.Abs(pg.MidX[i] - pg.MidX[mirror]) < 1e-9,
                $"x mismatch: panel {i} vs mirror {mirror}");
            Assert.True(System.Math.Abs(pg.MidY[i] + pg.MidY[mirror]) < 1e-9,
                $"y mismatch: panel {i}={pg.MidY[i]} vs mirror {mirror}={pg.MidY[mirror]}");
        }
    }

    [Fact]
    public void DiscretizePanels_PanelLengthsAllPositive()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        foreach (var L in pg.Length)
        {
            Assert.True(L > 0.0, $"non-positive panel length {L}");
        }
    }
}
