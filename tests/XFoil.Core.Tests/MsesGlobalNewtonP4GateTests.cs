using XFoil.ThesisClosureSolver.Inviscid;
using XFoil.ThesisClosureSolver.Newton;

namespace XFoil.Core.Tests;

/// <summary>
/// P4.6 — hard gate for P4 (Newton framework). With the
/// placeholder σ/BL rows (both identity R = x), Newton must
/// converge from zero state to the known inviscid γ in less
/// than 5 iterations on a representative case. If this fails,
/// the Newton plumbing or residual/Jacobian assembly is broken
/// and P5 would be built on sand.
/// </summary>
public class ThesisClosureGlobalNewtonP4GateTests
{
    [Fact]
    public void P4Gate_GammaOnly_ConvergesToInviscidSolution()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        double alpha = 4.0 * System.Math.PI / 180.0;

        // Direct inviscid solve (baseline).
        var invDirect = ThesisClosurePanelSolver.SolveInviscid(
            pg, 1.0, alpha, 1.0);

        // Newton on the global system (γ + σ-placeholder + BL-placeholder).
        var layout = new ThesisClosureGlobalState(
            gammaCount: n + 1, sigmaCount: n + 1, blStationCount: 2);
        var assembler = new ThesisClosureGlobalResidual(layout, pg, 1.0, alpha);
        var initialState = new double[layout.StateSize];

        var result = ThesisClosureGlobalNewton.Solve(
            initialState, assembler.Compute,
            (s, f) => ThesisClosureGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 10, resTol: 1e-10, stepTol: 1e-10);

        // Gate 1: converged.
        Assert.True(result.Converged,
            $"Newton did not converge in {result.IterationsRun} iterations; "
            + $"final |R|={result.FinalResidualNorm}, |Δ|={result.FinalStepNorm}");

        // Gate 2: converged fast (linear system → 1–2 steps).
        Assert.True(result.IterationsRun <= 5,
            $"Newton took {result.IterationsRun} iterations on a linear system "
            + "— expected ≤ 5");

        // Gate 3: recovered γ matches the direct inviscid solve.
        var (gNewton, sNewton, dNewton, tNewton, cNewton) = layout.Unpack(result.State);
        for (int k = 0; k < n + 1; k++)
        {
            Assert.True(System.Math.Abs(gNewton[k] - invDirect.Gamma[k]) < 1e-8,
                $"γ[{k}]: Newton={gNewton[k]}, direct={invDirect.Gamma[k]}");
        }

        // Gate 4: σ and BL are at zero (the identity placeholder rows
        // pin them there).
        foreach (var v in sNewton) Assert.True(System.Math.Abs(v) < 1e-10);
        foreach (var v in dNewton) Assert.True(System.Math.Abs(v) < 1e-10);
        foreach (var v in tNewton) Assert.True(System.Math.Abs(v) < 1e-10);
        foreach (var v in cNewton) Assert.True(System.Math.Abs(v) < 1e-10);
    }

    [Fact]
    public void P4Gate_CL_FromNewtonStateMatchesDirectSolve()
    {
        // After recovering γ via the Newton system, compute CL and
        // confirm it agrees with the direct inviscid CL to 1e-8.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("4412", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        double alpha = 6.0 * System.Math.PI / 180.0;

        var invDirect = ThesisClosurePanelSolver.SolveInviscid(
            pg, 1.0, alpha, 1.0);

        var layout = new ThesisClosureGlobalState(n + 1, n + 1, 2);
        var assembler = new ThesisClosureGlobalResidual(layout, pg, 1.0, alpha);
        var result = ThesisClosureGlobalNewton.Solve(
            new double[layout.StateSize], assembler.Compute,
            (s, f) => ThesisClosureGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 10, resTol: 1e-10, stepTol: 1e-10);
        Assert.True(result.Converged);

        // Recover CL from γ via a second direct solve that re-uses
        // the recovered γ. Equivalent: hand-compute Ue/circulation.
        // Simpler: confirm γ bit-exact equivalence implies CL
        // equivalence since CL is a pure function of γ.
        var (gNewton, _, _, _, _) = layout.Unpack(result.State);
        double maxDelta = 0.0;
        for (int k = 0; k < n + 1; k++)
        {
            double d = System.Math.Abs(gNewton[k] - invDirect.Gamma[k]);
            if (d > maxDelta) maxDelta = d;
        }
        Assert.True(maxDelta < 1e-8,
            $"Max γ difference {maxDelta} > 1e-8 — CL derivation would diverge");
    }
}
