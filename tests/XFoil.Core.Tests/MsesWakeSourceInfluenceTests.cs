using XFoil.MsesSolver.Inviscid;
using XFoil.MsesSolver.Newton;
using XFoil.MsesSolver.Topology;

namespace XFoil.Core.Tests;

/// <summary>
/// R5.7 — wake σ contribution to airfoil surface velocity tests.
/// </summary>
public class MsesWakeSourceInfluenceTests
{
    [Fact]
    public void BuildWakeSourceInfluenceMatrix_Shape_IsNxNw()
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: pg.Length[0],
            panelCount: 10, totalLength: 0.5);
        var aT = MsesInviscidPanelSolver.BuildWakeSourceInfluenceMatrix(
            pg, wake, normal: false);
        Assert.Equal(pg.PanelCount, aT.GetLength(0));
        Assert.Equal(wake.Length.Length, aT.GetLength(1));
    }

    [Fact]
    public void BuildWakeSourceInfluenceMatrix_CloserToTE_HasLargerInfluence()
    {
        // Wake σ influence on airfoil decays with distance. Panels
        // near the TE feel the wake σ more than panels near the LE.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: pg.Length[0],
            panelCount: 10, totalLength: 0.5);
        var aT = MsesInviscidPanelSolver.BuildWakeSourceInfluenceMatrix(
            pg, wake, normal: false);
        // Wake panel 0 (closest to TE). Its tangential influence
        // should be larger at airfoil panel 0 (TE_upper) than at
        // panel 20 (near LE).
        int nearTE = 0;
        int nearLE = pg.PanelCount / 2;
        Assert.True(System.Math.Abs(aT[nearTE, 0]) > System.Math.Abs(aT[nearLE, 0]),
            $"Wake panel 0 should have more influence at TE (|{aT[nearTE,0]}|) "
            + $"than LE (|{aT[nearLE,0]}|)");
    }

    [Fact]
    public void MsesGlobalResidualSided_WakeSigma_ShiftsInviscidRows()
    {
        // With a non-zero σ_wake, the inviscid rows should change
        // compared to σ_wake=0, proving the wake-σ→inviscid coupling
        // is wired.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 41);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        double alpha = 2.0 * System.Math.PI / 180.0;
        var stag = StagnationDetector.DetectFromGeometry(pg, 1.0, alpha);
        var topo = SurfaceTopology.Build(pg, stag);
        var wake = WakeDiscretization.Build(
            teX: 1.0, teY: 0.0,
            firstPanelLength: pg.Length[0],
            panelCount: 10, totalLength: 0.5,
            alphaRadians: alpha);
        int nw = wake.Length.Length;
        var layout = new MsesGlobalStateSided(
            n + 1, n + 1, nw,
            topo.Upper.PanelIndices.Length,
            topo.Lower.PanelIndices.Length, nw);
        var assembler = new MsesGlobalResidualSided(
            layout, pg, topo, 1.0, alpha,
            kinematicViscosity: 1e-6, initialTheta: 1e-4, wake: wake);
        var stateZero = new double[layout.StateSize];
        var rZero = assembler.Compute(stateZero);

        var stateP = new double[layout.StateSize];
        stateP[layout.SigmaWakeOffset + 0] = 0.1;  // perturb wake σ[0]
        var rP = assembler.Compute(stateP);
        // At least one inviscid row must have changed.
        bool anyChange = false;
        for (int i = 0; i < n; i++)
        {
            if (System.Math.Abs(rP[i] - rZero[i]) > 1e-10)
            {
                anyChange = true;
                break;
            }
        }
        Assert.True(anyChange,
            "σ_wake perturbation should shift at least one inviscid row");
    }
}
