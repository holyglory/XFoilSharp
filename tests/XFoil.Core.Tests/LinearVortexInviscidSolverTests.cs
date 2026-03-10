using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class LinearVortexInviscidSolverTests
{
    // ---- Task 1 unit tests: solver internals ----

    [Fact]
    public void AssembleAndFactorSystem_SetsAreBasisSolutionsComputedFlag()
    {
        var (panel, state) = CreatePanelAndState("0012", 61, 60);

        LinearVortexInviscidSolver.AssembleAndFactorSystem(panel, state, 1.0);

        Assert.True(state.AreBasisSolutionsComputed,
            "After system assembly, AreBasisSolutionsComputed should be true.");
        Assert.True(state.IsInfluenceMatrixFactored,
            "After system assembly, IsInfluenceMatrixFactored should be true.");
    }

    [Fact]
    public void AssembleAndFactorSystem_ProducesTwoBasisSolutions()
    {
        var (panel, state) = CreatePanelAndState("0012", 61, 60);
        int n = panel.NodeCount;

        LinearVortexInviscidSolver.AssembleAndFactorSystem(panel, state, 1.0);

        // Both basis columns should be non-trivial (not all zeros)
        double sumCol0 = 0.0;
        double sumCol1 = 0.0;
        for (int i = 0; i < n; i++)
        {
            sumCol0 += Math.Abs(state.BasisVortexStrength[i, 0]);
            sumCol1 += Math.Abs(state.BasisVortexStrength[i, 1]);
        }

        Assert.True(sumCol0 > 0.0, "Basis solution column 0 (alpha=0) should be non-trivial.");
        Assert.True(sumCol1 > 0.0, "Basis solution column 1 (alpha=90) should be non-trivial.");
    }

    [Fact]
    public void SolveAtAlpha0_SymmetricAirfoil_ProducesAntisymmetricVortexStrength()
    {
        var (panel, state) = CreatePanelAndState("0012", 101, 100);
        int n = panel.NodeCount;

        var result = LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            0.0, panel, state, 1.0, 0.0);

        // For symmetric airfoil at alpha=0, vortex strength should be antisymmetric
        // about the midpoint: gamma[i] ~ -gamma[N-1-i]
        double asymmetry = 0.0;
        for (int i = 0; i < n / 2; i++)
        {
            asymmetry += Math.Abs(state.VortexStrength[i] + state.VortexStrength[n - 1 - i]);
        }

        double avgGamma = 0.0;
        for (int i = 0; i < n; i++)
        {
            avgGamma += Math.Abs(state.VortexStrength[i]);
        }

        avgGamma /= n;

        // The antisymmetry residual should be small relative to the average gamma
        if (avgGamma > 1e-10)
        {
            double normalizedAsymmetry = asymmetry / (n * avgGamma);
            Assert.True(normalizedAsymmetry < 0.1,
                $"Vortex strength should be approximately antisymmetric for NACA 0012 at alpha=0. " +
                $"Normalized asymmetry = {normalizedAsymmetry:E3}");
        }
    }

    [Fact]
    public void ComputeInviscidSpeed_SuperimposesBasicSpeedVectors()
    {
        var (panel, state) = CreatePanelAndState("0012", 61, 60);
        int n = panel.NodeCount;

        LinearVortexInviscidSolver.AssembleAndFactorSystem(panel, state, 1.0);

        double alpha = 5.0 * Math.PI / 180.0;
        LinearVortexInviscidSolver.ComputeInviscidSpeed(alpha, state, n);

        // Verify superposition: Q[i] = cos(alpha)*QBasis[i,0] + sin(alpha)*QBasis[i,1]
        double cosa = Math.Cos(alpha);
        double sina = Math.Sin(alpha);
        for (int i = 0; i < n; i++)
        {
            double expected = cosa * state.BasisInviscidSpeed[i, 0] + sina * state.BasisInviscidSpeed[i, 1];
            Assert.Equal(expected, state.InviscidSpeed[i], 12);
        }
    }

    [Fact]
    public void IntegratePressureForces_Incompressible_ProducesCL()
    {
        // For NACA 0012 at alpha=5 deg, M=0: CL should be positive and near theory
        var (panel, state) = CreatePanelAndState("0012", 161, 160);

        double alpha = 5.0 * Math.PI / 180.0;
        var result = LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alpha, panel, state, 1.0, 0.0);

        Assert.True(result.LiftCoefficient > 0.0,
            "CL should be positive for NACA 0012 at alpha=5 deg.");
        // Thin-airfoil theory: CL = 2*pi*sin(alpha) ~ 0.548
        Assert.InRange(result.LiftCoefficient, 0.40, 0.70);
    }

    [Fact]
    public void ComputePressureCoefficients_IncompressibleM0_CorrectFormula()
    {
        // At M=0, Cp = 1 - (Q/Qinf)^2 (no KT correction)
        double qinf = 1.0;
        double[] surfaceSpeed = { 0.5, 1.0, 1.5, 2.0 };
        double[] cp = new double[4];

        LinearVortexInviscidSolver.ComputePressureCoefficients(surfaceSpeed, qinf, 0.0, cp, 4);

        Assert.Equal(1.0 - 0.25, cp[0], 10);   // 1 - (0.5)^2 = 0.75
        Assert.Equal(0.0, cp[1], 10);            // 1 - (1.0)^2 = 0.0
        Assert.Equal(1.0 - 2.25, cp[2], 10);    // 1 - (1.5)^2 = -1.25
        Assert.Equal(1.0 - 4.0, cp[3], 10);     // 1 - (2.0)^2 = -3.0
    }

    // ---- Helpers ----

    private static (LinearVortexPanelState panel, InviscidSolverState state) CreatePanelAndState(
        string nacaDesignation, int generatorPoints, int panelNodes)
    {
        var generator = new NacaAirfoilGenerator();
        var geometry = generator.Generate4Digit(nacaDesignation, generatorPoints);

        var points = geometry.Points;
        var x = new double[points.Count];
        var y = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            x[i] = points[i].X;
            y[i] = points[i].Y;
        }

        var panel = new LinearVortexPanelState(panelNodes + 20);
        var state = new InviscidSolverState(panelNodes + 20);

        CosineClusteringPanelDistributor.Distribute(x, y, points.Count, panel, panelNodes);
        state.InitializeForNodeCount(panel.NodeCount);

        return (panel, state);
    }
}
