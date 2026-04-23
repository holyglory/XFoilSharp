using XFoil.ThesisClosureSolver.Inviscid;
using XFoil.ThesisClosureSolver.Newton;

namespace XFoil.Core.Tests;

/// <summary>
/// P4.2 — global-Newton residual assembler tests.
/// </summary>
public class MsesGlobalResidualTests
{
    [Fact]
    public void Compute_AtZeroState_ProducesFreestreamResidual()
    {
        // All state zero: γ=0, σ=0, BL=0.
        // Inviscid row: 0 + 0 + V∞·n_i  — equals V∞·n_i.
        // Kutta row: 0.
        // Placeholder σ/BL rows: 0.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        var layout = new ThesisClosureGlobalState(
            gammaCount: n + 1, sigmaCount: n + 1, blStationCount: 2);
        var assembler = new ThesisClosureGlobalResidual(
            layout, pg, freestreamSpeed: 1.0,
            alphaRadians: 4.0 * System.Math.PI / 180.0);
        var zero = new double[layout.StateSize];
        var r = assembler.Compute(zero);

        for (int i = 0; i < n; i++)
        {
            double expected = System.Math.Cos(4.0 * System.Math.PI / 180.0) * pg.NormalX[i]
                            + System.Math.Sin(4.0 * System.Math.PI / 180.0) * pg.NormalY[i];
            Assert.True(System.Math.Abs(r[i] - expected) < 1e-12,
                $"row {i}: got {r[i]}, expected {expected}");
        }
        Assert.Equal(0.0, r[n]);
        for (int k = n + 1; k < layout.StateSize; k++)
        {
            Assert.Equal(0.0, r[k]);
        }
    }

    [Fact]
    public void Compute_AtInviscidSolution_ZeroResidualOnFlowRows()
    {
        // Seed state with γ from SolveInviscid, σ=BL=0. Inviscid
        // BC rows + Kutta should be ~0 (the solve converged).
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 81);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        double a = 4.0 * System.Math.PI / 180.0;
        var inv = ThesisClosurePanelSolver.SolveInviscid(pg, 1.0, a, 1.0);
        var layout = new ThesisClosureGlobalState(n + 1, n + 1, 2);
        var zeroSigma = new double[n + 1];
        var zeroBl = new double[2];
        var state = layout.Pack(inv.Gamma, zeroSigma, zeroBl, zeroBl, zeroBl);
        var assembler = new ThesisClosureGlobalResidual(layout, pg, 1.0, a);
        var r = assembler.Compute(state);

        // Inviscid rows + Kutta must be near zero.
        for (int i = 0; i <= n; i++)
        {
            Assert.True(System.Math.Abs(r[i]) < 1e-9,
                $"row {i}: |R|={r[i]} should be ~0 at inviscid solution");
        }
    }

    [Fact]
    public void Compute_SigmaPlaceholderRowIsIdentity()
    {
        // Placeholder σ-row returns σ itself. Set σ to known values
        // and verify R_σ equals them.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        var layout = new ThesisClosureGlobalState(n + 1, n + 1, 2);
        var gamma = new double[n + 1];
        var sigma = new double[n + 1];
        for (int i = 0; i < n + 1; i++) sigma[i] = 0.01 * i;
        var bl = new double[2];
        var state = layout.Pack(gamma, sigma, bl, bl, bl);
        var assembler = new ThesisClosureGlobalResidual(layout, pg, 1.0, 0.0);
        var r = assembler.Compute(state);
        for (int k = 0; k < n + 1; k++)
        {
            Assert.Equal(sigma[k], r[layout.SigmaOffset + k]);
        }
    }

    [Fact]
    public void Constructor_LayoutMismatch_Throws()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var badLayout = new ThesisClosureGlobalState(5, 5, 2);  // should be (N+1, N+1, ...)
        Assert.Throws<System.ArgumentException>(
            () => new ThesisClosureGlobalResidual(badLayout, pg, 1.0, 0.0));
    }

    [Fact]
    public void UseRealBLResiduals_ProducesFiniteResiduals()
    {
        // P5.3 wire-in smoke: when useRealBLResiduals=true, the σ
        // and BL rows should compute finite values. Construct with
        // BL station count = N (one per panel midpoint) and evaluate
        // at a rough initial guess.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        var layout = new ThesisClosureGlobalState(
            gammaCount: n + 1, sigmaCount: n + 1, blStationCount: n);
        var assembler = new ThesisClosureGlobalResidual(
            layout, pg, freestreamSpeed: 1.0, alphaRadians: 0.0,
            useRealBLResiduals: true);
        // Initial guess: small θ, H ~ 2.5 (laminar LE-like), σ=0, Cτ=0.01.
        var state = new double[layout.StateSize];
        for (int i = 0; i < n; i++)
        {
            state[layout.ThetaOffset + i] = 1e-4;
            state[layout.DstarOffset + i] = 2.5e-4;  // H = 2.5
            state[layout.CTauOffset + i] = 0.01;
        }
        var r = assembler.Compute(state);
        foreach (var v in r)
        {
            Assert.True(double.IsFinite(v),
                "residual element must be finite under real-BL wiring");
        }
    }

    [Fact]
    public void UseRealBLResiduals_Layout_MustHaveBLStationsEqualN()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        // Wrong BL station count — should throw.
        var badLayout = new ThesisClosureGlobalState(n + 1, n + 1, blStationCount: 2);
        Assert.Throws<System.ArgumentException>(
            () => new ThesisClosureGlobalResidual(
                badLayout, pg, 1.0, 0.0, useRealBLResiduals: true));
    }
}
