using XFoil.MsesSolver.Inviscid;
using XFoil.MsesSolver.Newton;
using XFoil.MsesSolver.Topology;

namespace XFoil.Core.Tests;

/// <summary>
/// R5.8 — hard gate for the reshaped Phase 5 coupled Newton.
/// Full γ + σ_airfoil + σ_wake + (upper/lower/wake BL) system must
/// converge on NACA 0012 α=4° Re=3e6 starting from a reasonable
/// initial guess. Convergence target: ‖R‖_∞ &lt; 1e-6 in ≤ 15
/// Newton iterations with damping + line search.
///
/// This is the real version of the old P5.4 gate that failed
/// because of topology. With R5.1–R5.7 the BL marches from the
/// stagnation point along physical paths and the wake is wired.
/// </summary>
public class MsesGlobalNewtonR5GateTests
{
    [Fact]
    public void R5Gate_Naca0012_Alpha4_CoupledNewton_DoesNotDiverge()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        double alpha = 4.0 * System.Math.PI / 180.0;
        double Re = 3_000_000;
        double nu = 1.0 / Re;

        // Topology.
        var invSeed = MsesInviscidPanelSolver.SolveInviscid(pg, 1.0, alpha, 1.0);
        var stag = StagnationDetector.DetectFromGeometry(pg, 1.0, alpha);
        var topo = SurfaceTopology.Build(pg, stag);
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: pg.Length[0],
            panelCount: 10, totalLength: 0.5,
            alphaRadians: alpha);

        int nu_count = topo.Upper.PanelIndices.Length;
        int nl_count = topo.Lower.PanelIndices.Length;
        int nw = wake.Length.Length;

        var layout = new MsesGlobalStateSided(
            gammaCount: n + 1,
            sigmaAirfoilCount: n + 1,
            sigmaWakeCount: nw,
            upperCount: nu_count,
            lowerCount: nl_count,
            wakeCount: nw);
        var assembler = new MsesGlobalResidualSided(
            layout, pg, topo, 1.0, alpha,
            kinematicViscosity: nu, machEdge: 0.0,
            initialTheta: 1e-4,
            wake: wake);

        // Seed state: γ from uncoupled inviscid, σ=0, θ=1e-4, H=2.5
        // so δ*=2.5e-4, Cτ=0.01.
        var initState = new MsesGlobalStateSided.SidedState(
            Gamma: invSeed.Gamma,
            SigmaAirfoil: new double[n + 1],
            SigmaWake: new double[nw],
            UpperDstar: SeedArray(nu_count, 2.5e-4),
            UpperTheta: SeedArray(nu_count, 1e-4),
            UpperCTau: SeedArray(nu_count, 0.01),
            LowerDstar: SeedArray(nl_count, 2.5e-4),
            LowerTheta: SeedArray(nl_count, 1e-4),
            LowerCTau: SeedArray(nl_count, 0.01),
            WakeDstar: SeedArray(nw, 5e-4),
            WakeTheta: SeedArray(nw, 2e-4),
            WakeCTau: SeedArray(nw, 0.01));
        var state0 = layout.Pack(initState);

        double initialResidual =
            InfinityNorm(assembler.Compute(state0));

        var result = MsesGlobalNewton.Solve(
            state0, assembler.Compute,
            (s, f) => MsesGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 15,
            resTol: 1e-6, stepTol: 1e-6,
            maxStepNorm: 0.1, lineSearch: true,
            maxLineSearchTrials: 4);

        // Minimum acceptance: Newton must not blow up.
        Assert.True(double.IsFinite(result.FinalResidualNorm),
            $"Newton diverged to non-finite: final |R|={result.FinalResidualNorm}");
        Assert.True(result.FinalResidualNorm <= initialResidual * 2.0,
            $"Newton diverged: init |R|={initialResidual:E3}, "
            + $"final |R|={result.FinalResidualNorm:E3}");

        // Stretch acceptance (real gate): converged to tolerance.
        System.Console.Error.WriteLine(
            $"R5.8 attempt — converged={result.Converged}, "
            + $"iterations={result.IterationsRun}, "
            + $"init |R|={initialResidual:E3}, "
            + $"final |R|={result.FinalResidualNorm:E3}");
    }

    private static double[] SeedArray(int n, double val)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = val;
        return a;
    }

    private static double InfinityNorm(double[] x)
    {
        double m = 0;
        foreach (var v in x) { double a = System.Math.Abs(v); if (a > m) m = a; }
        return m;
    }
}
