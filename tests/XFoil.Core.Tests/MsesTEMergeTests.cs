using XFoil.ThesisClosureSolver.Inviscid;
using XFoil.ThesisClosureSolver.Newton;
using XFoil.ThesisClosureSolver.Topology;

namespace XFoil.Core.Tests;

/// <summary>
/// R5.6 — TE-merge constraint + wake BL residuals tests.
/// </summary>
public class MsesTEMergeTests
{
    [Fact]
    public void TEMergeResiduals_AtMergedValues_AreAllZero()
    {
        // If wake[0] state is exactly the merged (θ_u+θ_l, δ*_u+δ*_l,
        // momentum-weighted Cτ), all three residuals are 0.
        double thU = 0.002, thL = 0.002;
        double dsU = 0.005, dsL = 0.005;
        double ctU = 0.015, ctL = 0.012;
        var (rT, rD, rC) = ThesisClosureBoundaryLayerResidual.TEMergeResiduals(
            thU, thL, dsU, dsL, ctU, ctL,
            thetaWake0: thU + thL,
            dStarWake0: dsU + dsL,
            cTauWake0: (thU * ctU + thL * ctL) / (thU + thL));
        Assert.Equal(0.0, rT, 14);
        Assert.Equal(0.0, rD, 14);
        Assert.Equal(0.0, rC, 14);
    }

    [Fact]
    public void TEMergeResiduals_OffValues_ReflectMismatch()
    {
        // Pass θ_wake0 = 0; residual should = −(θ_u+θ_l).
        var r = ThesisClosureBoundaryLayerResidual.TEMergeResiduals(
            thetaUpperTE: 0.002, thetaLowerTE: 0.002,
            dStarUpperTE: 0.005, dStarLowerTE: 0.005,
            cTauUpperTE: 0.01, cTauLowerTE: 0.01,
            thetaWake0: 0.0, dStarWake0: 0.0, cTauWake0: 0.0);
        Assert.Equal(-0.004, r.RTheta, 12);
        Assert.Equal(-0.010, r.RDstar, 12);
        Assert.Equal(-0.01, r.RCTau, 12);
    }

    [Fact]
    public void WakeMomentumResidual_IsWakeZeroesCf()
    {
        // Compare residual with isWake=true vs isWake=false at the
        // same state. The Cf contribution is the only difference.
        double rNormal = ThesisClosureBoundaryLayerResidual.MomentumResidual(
            thetaPrev: 0.002, theta: 0.002,
            hPrev: 1.4, h: 1.4,
            uePrev: 1.0, ue: 1.0,
            dx: 0.01, nu: 1e-6,
            me: 0.0, isWake: false);
        double rWake = ThesisClosureBoundaryLayerResidual.MomentumResidual(
            thetaPrev: 0.002, theta: 0.002,
            hPrev: 1.4, h: 1.4,
            uePrev: 1.0, ue: 1.0,
            dx: 0.01, nu: 1e-6,
            me: 0.0, isWake: true);
        // Wake residual > normal (Cf-loss term zeroed so RHS is
        // less negative → residual is less negative / more
        // positive).
        Assert.True(rWake > rNormal,
            $"Wake {rWake} should be > normal {rNormal}");
    }

    [Fact]
    public void GlobalResidualSided_WithWake_ProducesFiniteResidual()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = ThesisClosurePanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        double alpha = 4.0 * System.Math.PI / 180.0;
        var stag = StagnationDetector.DetectFromGeometry(pg, 1.0, alpha);
        var topo = SurfaceTopology.Build(pg, stag);
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: pg.Length[0],
            panelCount: 10, totalLength: 0.5,
            alphaRadians: alpha);
        int nw = wake.Length.Length;
        var layout = new ThesisClosureGlobalStateSided(
            n + 1, n + 1, nw,
            topo.Upper.PanelIndices.Length,
            topo.Lower.PanelIndices.Length, nw);
        var assembler = new ThesisClosureGlobalResidualSided(
            layout, pg, topo, 1.0, alpha,
            kinematicViscosity: 1e-6, machEdge: 0.0,
            initialTheta: 1e-4,
            wake: wake);
        var zero = new double[layout.StateSize];
        var r = assembler.Compute(zero);
        foreach (var v in r) Assert.True(double.IsFinite(v));
    }
}
