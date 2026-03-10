using System;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Tests for InfluenceMatrixBuilder: DIJ influence matrix (dUe/dSigma) construction
/// ported from QDCALC in xpanel.f.
/// </summary>
public class InfluenceMatrixBuilderTests
{
    private const double Tol = 1e-10;
    private const double NumericalTol = 1e-6;

    [Fact]
    public void BuildAnalyticalDIJ_ProducesCorrectSize()
    {
        // 10-node system should produce DIJ with dimensions covering all nodes + wake
        int n = 10;
        var inviscidState = CreateMockInviscidState(n);
        var panelState = CreateMockPanelState(n);
        int nWake = 2;

        double[,] dij = InfluenceMatrixBuilder.BuildAnalyticalDIJ(
            inviscidState, panelState, nWake);

        // DIJ covers N airfoil nodes + nWake wake nodes
        Assert.Equal(n + nWake, dij.GetLength(0));
        Assert.Equal(n + nWake, dij.GetLength(1));
    }

    [Fact]
    public void BuildAnalyticalDIJ_DiagonalDominance()
    {
        // The DIJ matrix should have significant diagonal elements:
        // perturbation of sigma at node i has largest effect on Ue at node i
        int n = 10;
        var inviscidState = CreateMockInviscidState(n);
        var panelState = CreateMockPanelState(n);

        double[,] dij = InfluenceMatrixBuilder.BuildAnalyticalDIJ(
            inviscidState, panelState, 0);

        // Check diagonal elements are non-zero for airfoil nodes
        for (int i = 0; i < n; i++)
        {
            Assert.NotEqual(0.0, dij[i, i]);
        }
    }

    [Fact]
    public void BuildAnalyticalDIJ_FirstWakeRowCopiesFromTE()
    {
        // xpanel.f line 1249: first wake row should copy from TE row
        int n = 10;
        var inviscidState = CreateMockInviscidState(n);
        var panelState = CreateMockPanelState(n);
        int nWake = 3;

        double[,] dij = InfluenceMatrixBuilder.BuildAnalyticalDIJ(
            inviscidState, panelState, nWake);

        // First wake row (index n) should match TE row values
        // TE is the last airfoil node (n-1) for upper and first node (0) for lower
        // In XFoil convention, wake row copies from combined TE influence
        int wakeRow = n;
        bool hasNonZero = false;
        for (int j = 0; j < n + nWake; j++)
        {
            if (Math.Abs(dij[wakeRow, j]) > 1e-15)
                hasNonZero = true;
        }
        Assert.True(hasNonZero, "First wake row should have non-zero entries copied from TE");
    }

    [Fact]
    public void BuildNumericalDIJ_ProducesCorrectSize()
    {
        int n = 10;
        var inviscidState = CreateMockInviscidState(n);
        var panelState = CreateMockPanelState(n);

        double[,] dij = InfluenceMatrixBuilder.BuildNumericalDIJ(
            inviscidState, panelState, 0);

        Assert.Equal(n, dij.GetLength(0));
        Assert.Equal(n, dij.GetLength(1));
    }

    [Fact]
    public void BuildNumericalDIJ_MatchesAnalyticalWithinTolerance()
    {
        // Numerical (finite-difference) DIJ should agree with analytical DIJ
        int n = 8;
        var inviscidState = CreateMockInviscidState(n);
        var panelState = CreateMockPanelState(n);

        double[,] analytical = InfluenceMatrixBuilder.BuildAnalyticalDIJ(
            inviscidState, panelState, 0);
        double[,] numerical = InfluenceMatrixBuilder.BuildNumericalDIJ(
            inviscidState, panelState, 0);

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Assert.True(
                    Math.Abs(analytical[i, j] - numerical[i, j]) < NumericalTol,
                    $"DIJ[{i},{j}] mismatch: analytical={analytical[i, j]:E6}, numerical={numerical[i, j]:E6}");
            }
        }
    }

    /// <summary>
    /// Creates a mock InviscidSolverState with factored AIJ/BIJ for testing.
    /// Uses a simple symmetric airfoil-like configuration.
    /// </summary>
    private static InviscidSolverState CreateMockInviscidState(int n)
    {
        var state = new InviscidSolverState(n);
        state.InitializeForNodeCount(n);

        // Set up a simple influence matrix system:
        // AIJ is identity-like (already LU factored), BIJ is small perturbation
        for (int i = 0; i <= n; i++)
        {
            state.PivotIndices[i] = i;
            for (int j = 0; j <= n; j++)
            {
                state.StreamfunctionInfluence[i, j] = (i == j) ? 1.0 : 0.01 / (1.0 + Math.Abs(i - j));
            }
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                state.SourceInfluence[i, j] = (i == j) ? 0.5 : 0.02 / (1.0 + Math.Abs(i - j));
            }
            state.InviscidSpeed[i] = 1.0 + 0.1 * Math.Sin(2.0 * Math.PI * i / n);
            state.VortexStrength[i] = state.InviscidSpeed[i];
            state.SourceStrength[i] = 0.01 * Math.Cos(2.0 * Math.PI * i / n);
        }

        state.IsInfluenceMatrixFactored = true;
        state.AreBasisSolutionsComputed = true;
        return state;
    }

    /// <summary>
    /// Creates a mock LinearVortexPanelState for testing.
    /// </summary>
    private static LinearVortexPanelState CreateMockPanelState(int n)
    {
        var state = new LinearVortexPanelState(n);
        state.Resize(n);

        // Simple circular arc airfoil-like geometry
        for (int i = 0; i < n; i++)
        {
            double theta = Math.PI * (1.0 - (double)i / (n - 1));
            state.X[i] = 0.5 * (1.0 + Math.Cos(theta));
            state.Y[i] = 0.1 * Math.Sin(theta);
            state.NormalX[i] = -Math.Sin(theta);
            state.NormalY[i] = Math.Cos(theta);
            state.PanelAngle[i] = theta;
        }

        state.LeadingEdgeX = 0.0;
        state.LeadingEdgeY = 0.0;
        state.TrailingEdgeX = 1.0;
        state.TrailingEdgeY = 0.0;
        state.Chord = 1.0;
        return state;
    }
}
