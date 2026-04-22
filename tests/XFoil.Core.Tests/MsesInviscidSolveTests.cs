using XFoil.MsesSolver.Inviscid;

namespace XFoil.Core.Tests;

/// <summary>
/// P1.3 — inviscid solve tests. Validate γ extraction, Cp, and
/// CL on the clean-room panel solver. No Karman-Tsien yet (P1.4);
/// runs at M=0 only.
/// </summary>
public class MsesInviscidSolveTests
{
    [Fact]
    public void SolveInviscid_Naca0012_Alpha0_CL_IsZero()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        var r = MsesInviscidPanelSolver.SolveInviscid(
            pg, freestreamSpeed: 1.0, alphaRadians: 0.0, chord: 1.0);
        Assert.True(System.Math.Abs(r.LiftCoefficient) < 1e-6,
            $"Symmetric α=0 should give CL=0; got {r.LiftCoefficient}");
    }

    [Fact]
    public void SolveInviscid_Naca0012_Alpha4_CL_IsPositiveAndReasonable()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        double a = 4.0 * System.Math.PI / 180.0;
        var r = MsesInviscidPanelSolver.SolveInviscid(
            pg, freestreamSpeed: 1.0, alphaRadians: a, chord: 1.0);
        // Thin-airfoil theory: CL = 2π·α ≈ 0.439 at α=4°.
        // NACA 0012 is 12% thick so actual CL is a bit higher (~0.48).
        Assert.InRange(r.LiftCoefficient, 0.4, 0.55);
    }

    [Fact]
    public void SolveInviscid_Naca4412_Alpha0_CL_IsPositive()
    {
        // 4% camber NACA 4412: CL at α=0 is approx 0.4.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        var r = MsesInviscidPanelSolver.SolveInviscid(
            pg, freestreamSpeed: 1.0, alphaRadians: 0.0, chord: 1.0);
        Assert.InRange(r.LiftCoefficient, 0.3, 0.55);
    }

    [Fact]
    public void SolveInviscid_CpAtStagnation_IsOne()
    {
        // On NACA 0012 at α=0, Cp at the stagnation point (LE)
        // should equal 1. Find the max Cp panel.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        var r = MsesInviscidPanelSolver.SolveInviscid(
            pg, freestreamSpeed: 1.0, alphaRadians: 0.0, chord: 1.0);
        double cpMax = double.NegativeInfinity;
        foreach (var c in r.CpMidpoint) if (c > cpMax) cpMax = c;
        Assert.InRange(cpMax, 0.9, 1.02);
    }

    [Fact]
    public void SolveInviscid_Naca0012_CL_AlphaLinearInSmallAlphaRegime()
    {
        // dCL/dα ≈ 2π per radian for thin airfoils. Check linearity
        // by comparing CL at α=2° and α=4°.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 161);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        double a2 = 2.0 * System.Math.PI / 180.0;
        double a4 = 4.0 * System.Math.PI / 180.0;
        var r2 = MsesInviscidPanelSolver.SolveInviscid(pg, 1.0, a2, 1.0);
        var r4 = MsesInviscidPanelSolver.SolveInviscid(pg, 1.0, a4, 1.0);
        double ratio = r4.LiftCoefficient / r2.LiftCoefficient;
        Assert.InRange(ratio, 1.95, 2.05);
    }

    [Fact]
    public void SolveLinearSystem_Solves3x3Case()
    {
        // Simple 3x3 linear system: verify Gaussian elimination
        // with partial pivoting returns the correct answer.
        var a = new double[,]
        {
            { 2.0, 1.0, 1.0 },
            { 4.0, 3.0, 3.0 },
            { 8.0, 7.0, 9.0 },
        };
        var b = new double[] { 4.0, 10.0, 26.0 };
        var x = MsesInviscidPanelSolver.SolveLinearSystem(a, b);
        // Exact solution: x = (1, 1, 1) produces 4, 10, 24 (not 26).
        // Let's pick b so solution is clean: x=(1,2,3) → 2+2+3=7,
        // 4+6+9=19, 8+14+27=49. Redo with those values:
        var a2 = new double[,]
        {
            { 2.0, 1.0, 1.0 },
            { 4.0, 3.0, 3.0 },
            { 8.0, 7.0, 9.0 },
        };
        var b2 = new double[] { 7.0, 19.0, 49.0 };
        var x2 = MsesInviscidPanelSolver.SolveLinearSystem(a2, b2);
        Assert.Equal(1.0, x2[0], 9);
        Assert.Equal(2.0, x2[1], 9);
        Assert.Equal(3.0, x2[2], 9);
    }
}
