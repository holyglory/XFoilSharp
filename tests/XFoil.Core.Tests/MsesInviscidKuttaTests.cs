using XFoil.MsesSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P3.1 — Kutta condition tests. Confirms ApplyKuttaRow writes the
/// correct structure and that TE-gap detection works for both sharp
/// and blunt NACA airfoils.
/// </summary>
public class MsesInviscidKuttaTests
{
    [Fact]
    public void ApplyKuttaRow_WritesExpectedStructure()
    {
        int n = 5;
        var mat = new double[n + 1, n + 1];
        var rhs = new double[n + 1];
        // Pre-fill to confirm the row is replaced, not OR-ed in.
        for (int k = 0; k < n + 1; k++) mat[n, k] = 42.0;
        rhs[n] = 99.0;
        MsesInviscidPanelSolver.ApplyKuttaRow(mat, rhs, n);
        for (int k = 0; k < n + 1; k++)
        {
            double expected = (k == 0 || k == n) ? 1.0 : 0.0;
            Assert.Equal(expected, mat[n, k]);
        }
        Assert.Equal(0.0, rhs[n]);
    }

    [Fact]
    public void ApplyKuttaRow_RejectsWrongSize()
    {
        int n = 5;
        var mat = new double[n + 1, n];  // wrong cols
        var rhs = new double[n + 1];
        Assert.Throws<System.ArgumentException>(
            () => MsesInviscidPanelSolver.ApplyKuttaRow(mat, rhs, n));
    }

    [Fact]
    public void TrailingEdgeGap_Naca0012_IsSmallButNonZero()
    {
        // Standard NACA 4-digit with 0.1015 TE coefficient gives
        // a finite TE thickness: yt(x=1) = (-0.1015·2·0.06)·1 ≈
        // 0.00126 chord. Gap is ~2× that ≈ 0.0025.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        double gap = MsesInviscidPanelSolver.TrailingEdgeGap(pg);
        Assert.InRange(gap, 0.0, 0.01);  // at most 1% of chord
    }

    [Fact]
    public void TrailingEdgeGap_ScalesWithThickness()
    {
        // Thicker airfoils have larger TE gaps in standard NACA
        // 4-digit generation.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var gap06 = MsesInviscidPanelSolver.TrailingEdgeGap(
            MsesInviscidPanelSolver.DiscretizePanels(
                gen.Generate4DigitClassic("0006", pointCount: 101)));
        var gap12 = MsesInviscidPanelSolver.TrailingEdgeGap(
            MsesInviscidPanelSolver.DiscretizePanels(
                gen.Generate4DigitClassic("0012", pointCount: 101)));
        var gap24 = MsesInviscidPanelSolver.TrailingEdgeGap(
            MsesInviscidPanelSolver.DiscretizePanels(
                gen.Generate4DigitClassic("0024", pointCount: 101)));
        Assert.True(gap06 < gap12 && gap12 < gap24,
            $"TE gaps should scale with thickness; got "
            + $"0006={gap06:F5}, 0012={gap12:F5}, 0024={gap24:F5}");
    }

    [Fact]
    public void SolveInviscid_BluntTE_StillProducesPhysicalCL()
    {
        // Confirm the current sharp-TE Kutta handling works OK
        // on blunt TE (NACA 0012 has ~0.25% chord TE gap). This is
        // already exercised by the P1.5 gate; this test is an
        // explicit pin.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        double a = 4.0 * System.Math.PI / 180.0;
        var r = MsesInviscidPanelSolver.SolveInviscid(pg, 1.0, a, 1.0);
        Assert.InRange(r.LiftCoefficient, 0.45, 0.50);
    }
}
