using System;
using System.Linq;
using Xunit;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Integration tests for ViscousSolverEngine.SolveViscous.
/// Exercises the full Newton loop on NACA 0012 at Re=1e6, alpha=0.
/// </summary>
public class ViscousSolverEngineTests
{
    /// <summary>
    /// NACA 0012 at alpha=0, Re=1e6 should converge within 50 iterations.
    /// </summary>
    [Fact]
    public void SolveViscous_Naca0012_AlphaZero_Converges()
    {
        var result = RunNaca0012AlphaZero();

        Assert.True(result.Converged, $"VISCAL should converge but did not after {result.Iterations} iterations");
        Assert.True(result.Iterations <= 50,
            $"Should converge in <= 50 iterations, took {result.Iterations}");
    }

    /// <summary>
    /// Symmetric airfoil at alpha=0: CL should be near zero.
    /// </summary>
    [Fact]
    public void SolveViscous_Naca0012_AlphaZero_CLNearZero()
    {
        var result = RunNaca0012AlphaZero();

        Assert.True(result.Converged, "Must converge first");
        Assert.True(Math.Abs(result.LiftCoefficient) < 0.01,
            $"CL should be near 0 for symmetric airfoil at alpha=0, got {result.LiftCoefficient}");
    }

    /// <summary>
    /// Viscous CD should be positive (drag is always positive).
    /// </summary>
    [Fact]
    public void SolveViscous_Naca0012_AlphaZero_CDPositive()
    {
        var result = RunNaca0012AlphaZero();

        Assert.True(result.Converged, "Must converge first");
        Assert.True(result.DragDecomposition.CD > 0,
            $"CD should be positive, got {result.DragDecomposition.CD}");
    }

    /// <summary>
    /// CD should be physically reasonable for NACA 0012 at Re=1e6.
    /// Expected range: 0.005 < CD < 0.02.
    /// </summary>
    [Fact]
    public void SolveViscous_Naca0012_AlphaZero_CDPhysicallyReasonable()
    {
        var result = RunNaca0012AlphaZero();

        Assert.True(result.Converged, "Must converge first");
        double cd = result.DragDecomposition.CD;
        Assert.True(cd > 0.005 && cd < 0.02,
            $"CD should be in [0.005, 0.02] for NACA 0012 at Re=1e6, got {cd}");
    }

    /// <summary>
    /// Newton residual (RMSBL) should generally decrease during iteration.
    /// Verifies that the iteration history is recorded.
    /// </summary>
    [Fact]
    public void SolveViscous_Naca0012_AlphaZero_ResidualDecreases()
    {
        var result = RunNaca0012AlphaZero();

        Assert.True(result.Converged, "Must converge first");
        Assert.True(result.ConvergenceHistory.Count >= 2,
            "Should have at least 2 iterations of convergence history");

        // The RMS residual should generally decrease.
        // Allow for some non-monotonicity (trust-region can have occasional increases)
        // but overall trend should be downward.
        double firstRms = result.ConvergenceHistory[0].RmsResidual;
        double lastRms = result.ConvergenceHistory[result.ConvergenceHistory.Count - 1].RmsResidual;
        Assert.True(lastRms < firstRms,
            $"Final RMS ({lastRms:E3}) should be less than first RMS ({firstRms:E3})");
    }

    /// <summary>
    /// Convergence history should be captured for all iterations.
    /// </summary>
    [Fact]
    public void SolveViscous_Naca0012_AlphaZero_IterationHistoryCaptured()
    {
        var result = RunNaca0012AlphaZero();

        Assert.True(result.ConvergenceHistory.Count > 0,
            "Should capture convergence history");
        Assert.Equal(result.Iterations, result.ConvergenceHistory.Count);

        // Each history entry should have valid data
        foreach (var info in result.ConvergenceHistory)
        {
            Assert.True(info.RmsResidual >= 0, "RMS residual should be non-negative");
        }
    }

    /// <summary>
    /// Non-convergence case: Returns result with converged=false.
    /// Use extremely tight tolerance that won't be met.
    /// </summary>
    [Fact]
    public void SolveViscous_ExtremelyTightTolerance_ReturnsNonConverged()
    {
        var settings = new AnalysisSettings(
            panelCount: 80,
            reynoldsNumber: 1_000_000,
            inviscidSolverType: InviscidSolverType.LinearVortex,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            maxViscousIterations: 3,
            viscousConvergenceTolerance: 1e-20);

        var result = ViscousSolverEngine.SolveViscous(
            BuildNaca0012Geometry(settings.PanelCount),
            settings,
            0.0); // alpha = 0

        // With only 3 iterations and 1e-20 tolerance, it should not converge
        Assert.False(result.Converged);
        Assert.Equal(3, result.Iterations);
        Assert.True(result.ConvergenceHistory.Count > 0,
            "Should still have convergence history even when not converged");
    }

    // ===============================================================
    // Helper: Run the standard NACA 0012 case
    // ===============================================================

    private static ViscousAnalysisResult RunNaca0012AlphaZero()
    {
        var settings = new AnalysisSettings(
            panelCount: 120,
            reynoldsNumber: 1_000_000,
            inviscidSolverType: InviscidSolverType.LinearVortex,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            maxViscousIterations: 50,
            viscousConvergenceTolerance: 1e-4);

        var geometry = BuildNaca0012Geometry(settings.PanelCount);
        return ViscousSolverEngine.SolveViscous(geometry, settings, 0.0);
    }

    /// <summary>
    /// Builds NACA 0012 coordinates for the given panel count.
    /// Uses cosine distribution.
    /// </summary>
    private static (double[] x, double[] y) BuildNaca0012Geometry(int n)
    {
        // NACA 0012: y = 0.6 * (0.2969*sqrt(x) - 0.126*x - 0.3516*x^2 + 0.2843*x^3 - 0.1036*x^4)
        // for 12% thick airfoil
        double[] xCoords = new double[n + 1];
        double[] yCoords = new double[n + 1];

        int half = n / 2;

        // Upper surface (trailing edge to leading edge): indices 0..half
        for (int i = 0; i <= half; i++)
        {
            double theta = Math.PI * i / half;
            double xc = 0.5 * (1.0 + Math.Cos(theta));
            double yt = 0.6 * (0.2969 * Math.Sqrt(xc) - 0.126 * xc
                - 0.3516 * xc * xc + 0.2843 * xc * xc * xc - 0.1036 * xc * xc * xc * xc);
            xCoords[i] = xc;
            yCoords[i] = yt;
        }

        // Lower surface (leading edge to trailing edge): indices half+1..n
        for (int i = 1; i <= half; i++)
        {
            double theta = Math.PI * i / half;
            double xc = 0.5 * (1.0 - Math.Cos(theta));
            double yt = 0.6 * (0.2969 * Math.Sqrt(xc) - 0.126 * xc
                - 0.3516 * xc * xc + 0.2843 * xc * xc * xc - 0.1036 * xc * xc * xc * xc);
            xCoords[half + i] = xc;
            yCoords[half + i] = -yt;
        }

        return (xCoords, yCoords);
    }
}
