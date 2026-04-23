using XFoil.ThesisClosureSolver.Inviscid;
using XFoil.ThesisClosureSolver.Newton;
using XFoil.ThesisClosureSolver.Topology;

namespace XFoil.Core.Tests;

/// <summary>
/// R5.5 — per-side residual assembler tests.
/// </summary>
public class ThesisClosureGlobalResidualSidedTests
{
    private static (ThesisClosurePanelSolver.PanelizedGeometry pg,
        SurfaceTopology.Topology topo) BuildTopo(string naca, double alphaDeg)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 81);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        var stag = StagnationDetector.DetectFromGeometry(
            pg, 1.0, alphaDeg * System.Math.PI / 180.0);
        var topo = SurfaceTopology.Build(pg, stag);
        return (pg, topo);
    }

    [Fact]
    public void Compute_AtZeroState_ProducesFiniteResidual()
    {
        var (pg, topo) = BuildTopo("0012", 4.0);
        int n = pg.PanelCount;
        var layout = new ThesisClosureGlobalStateSided(
            gammaCount: n + 1,
            sigmaAirfoilCount: n + 1,
            sigmaWakeCount: 0,
            upperCount: topo.Upper.PanelIndices.Length,
            lowerCount: topo.Lower.PanelIndices.Length,
            wakeCount: 0);
        var assembler = new ThesisClosureGlobalResidualSided(
            layout, pg, topo,
            freestreamSpeed: 1.0,
            alphaRadians: 4.0 * System.Math.PI / 180.0);
        var zero = new double[layout.StateSize];
        var r = assembler.Compute(zero);
        foreach (var v in r)
        {
            Assert.True(double.IsFinite(v),
                "residual element must be finite at zero state");
        }
        Assert.Equal(layout.StateSize, r.Length);
    }

    [Fact]
    public void Compute_StateSize_MatchesLayout()
    {
        var (pg, topo) = BuildTopo("4412", 2.0);
        int n = pg.PanelCount;
        var layout = new ThesisClosureGlobalStateSided(
            n + 1, n + 1, 0,
            topo.Upper.PanelIndices.Length,
            topo.Lower.PanelIndices.Length, 0);
        var assembler = new ThesisClosureGlobalResidualSided(
            layout, pg, topo, 1.0, 2.0 * System.Math.PI / 180.0);
        var state = new double[layout.StateSize];
        var r = assembler.Compute(state);
        Assert.Equal(layout.StateSize, r.Length);
    }

    [Fact]
    public void Compute_InviscidRowsAreLinearInGamma()
    {
        // At σ=0 and BL=0, the inviscid rows should be exactly
        // A_γ·γ + V∞·n_i. Verify by comparing at two different γ
        // values: R(γ=0)_inviscid should equal V∞·n, and R(γ=γ*)
        // should equal V∞·n + A_γ·γ*.
        var (pg, topo) = BuildTopo("0012", 2.0);
        int n = pg.PanelCount;
        var layout = new ThesisClosureGlobalStateSided(
            n + 1, n + 1, 0,
            topo.Upper.PanelIndices.Length,
            topo.Lower.PanelIndices.Length, 0);
        var assembler = new ThesisClosureGlobalResidualSided(
            layout, pg, topo, 1.0, 2.0 * System.Math.PI / 180.0);
        var stateZero = new double[layout.StateSize];
        var rZero = assembler.Compute(stateZero);

        // Perturb γ[5] by 0.01.
        var stateP = new double[layout.StateSize];
        stateP[layout.GammaOffset + 5] = 0.01;
        var rP = assembler.Compute(stateP);
        // Inviscid rows (0..n-1) should differ by 0.01·A_γ_normal[i, 5].
        var aN = ThesisClosurePanelSolver.BuildVortexNormalInfluenceMatrix(pg);
        for (int i = 0; i < n; i++)
        {
            double diff = rP[i] - rZero[i];
            double expected = 0.01 * aN[i, 5];
            Assert.True(System.Math.Abs(diff - expected) < 1e-10,
                $"row {i}: diff {diff}, expected {expected}");
        }
    }

    [Fact]
    public void Compute_KuttaRow_PinsGamma0PlusGammaN()
    {
        var (pg, topo) = BuildTopo("0012", 0.0);
        int n = pg.PanelCount;
        var layout = new ThesisClosureGlobalStateSided(
            n + 1, n + 1, 0,
            topo.Upper.PanelIndices.Length,
            topo.Lower.PanelIndices.Length, 0);
        var assembler = new ThesisClosureGlobalResidualSided(
            layout, pg, topo, 1.0, 0.0);
        var state = new double[layout.StateSize];
        state[layout.GammaOffset + 0] = 1.5;
        state[layout.GammaOffset + n] = -1.5;
        var r = assembler.Compute(state);
        // Kutta row (at offset n) should be γ_0 + γ_N = 0.
        Assert.Equal(0.0, r[n], 12);
    }
}
