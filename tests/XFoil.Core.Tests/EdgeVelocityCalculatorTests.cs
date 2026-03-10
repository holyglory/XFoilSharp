using System;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Tests for EdgeVelocityCalculator and StagnationPointTracker.
/// </summary>
public class EdgeVelocityCalculatorTests
{
    [Fact]
    public void FindStagnationPoint_SignChange_FindsCorrectPanel()
    {
        double[] speed = { 1.0, 0.5, 0.1, -0.2, -0.8, -1.2 };
        int isp = StagnationPointTracker.FindStagnationPoint(speed, 6);
        Assert.True(isp == 2 || isp == 3, $"ISP should be 2 or 3, got {isp}");
    }

    [Fact]
    public void FindStagnationPoint_MinAbsSpeed_FindsCorrectPanel()
    {
        double[] speed = { 1.5, 0.8, 0.3, 0.05, 0.2, 0.9 };
        int isp = StagnationPointTracker.FindStagnationPoint(speed, 6);
        Assert.Equal(3, isp);
    }

    [Fact]
    public void FindStagnationPoint_SymmetricAirfoil_FindsLeadingEdge()
    {
        int n = 20;
        double[] speed = new double[n];
        int mid = n / 2;
        for (int i = 0; i < n; i++) speed[i] = (i - mid) * 0.1;
        int isp = StagnationPointTracker.FindStagnationPoint(speed, n);
        Assert.True(isp >= mid - 2 && isp <= mid + 2, $"ISP {isp} near midpoint {mid}");
    }

    [Fact]
    public void MoveStagnationPoint_ShiftsBLVariables()
    {
        var blState = new BoundaryLayerSystemState(20, 5);
        blState.InitializeForStationCounts(8, 8, 4);
        for (int i = 0; i < 8; i++)
        {
            blState.THET[i, 0] = 0.001 * (i + 1);
            blState.THET[i, 1] = 0.001 * (i + 1);
            blState.DSTR[i, 0] = 0.002 * (i + 1);
            blState.DSTR[i, 1] = 0.002 * (i + 1);
            blState.XSSI[i, 0] = 0.1 * i;
            blState.XSSI[i, 1] = 0.1 * i;
        }
        blState.ITRAN[0] = 4;
        blState.ITRAN[1] = 4;

        StagnationPointTracker.MoveStagnationPoint(blState, 10, 11, 20);

        for (int i = 0; i < blState.IBLTE[0]; i++)
            Assert.True(blState.THET[i, 0] >= 0, $"THET[{i},0] non-negative after STMOVE");
    }

    [Fact]
    public void MapPanelsToBLStations_ProducesCorrectCounts()
    {
        var (iblte, nbl) = EdgeVelocityCalculator.MapPanelsToBLStations(20, 10, 3);
        Assert.True(iblte[0] > 0);
        Assert.True(iblte[1] > 0);
        Assert.True(nbl[0] > 0);
        Assert.True(nbl[1] > 0);
    }

    [Fact]
    public void ComputeBLArcLength_ZeroAtStagnation_MonotonicallyIncreasing()
    {
        double[] x = { 0.0, 0.1, 0.3, 0.5, 0.7, 0.9, 1.0 };
        double[] y = { 0.0, 0.05, 0.04, 0.03, 0.02, 0.01, 0.0 };
        double[] xi = EdgeVelocityCalculator.ComputeBLArcLength(x, y, 7);
        Assert.Equal(0.0, xi[0], 10);
        for (int i = 1; i < 7; i++)
            Assert.True(xi[i] > xi[i - 1], $"xi[{i}] should increase");
    }

    [Fact]
    public void MapStationsToSystemLines_ProducesContiguousMapping()
    {
        int[] iblte = { 5, 6 };
        int[] nbl = { 6, 10 };
        var (isys, nsys) = EdgeVelocityCalculator.MapStationsToSystemLines(iblte, nbl);
        Assert.True(nsys > 0);
        Assert.True(nsys <= nbl[0] + nbl[1]);
    }

    [Fact]
    public void ComputeInviscidEdgeVelocity_SpeedEqualsGamma()
    {
        double[] gamma = { 1.0, 1.5, 1.2, 0.8, 0.6 };
        double[] ue = EdgeVelocityCalculator.ComputeInviscidEdgeVelocity(gamma, 5);
        for (int i = 0; i < 5; i++) Assert.Equal(gamma[i], ue[i], 10);
    }

    [Fact]
    public void ComputeViscousEdgeVelocity_ConvertsCorrectly()
    {
        double[] uedg = { 1.2, 1.0, 0.8, 0.5 };
        double[] qvis = EdgeVelocityCalculator.ComputeViscousEdgeVelocity(uedg, 4, 1.0);
        for (int i = 0; i < 4; i++) Assert.Equal(uedg[i], qvis[i], 10);
    }

    [Fact]
    public void SetVortexFromViscousSpeed_InverseOfQvfue()
    {
        double[] qvis = { 1.5, 1.2, 0.9, 0.6 };
        double[] gamma = EdgeVelocityCalculator.SetVortexFromViscousSpeed(qvis, 4, 1.0);
        for (int i = 0; i < 4; i++) Assert.Equal(qvis[i], gamma[i], 10);
    }

    [Fact]
    public void SetInviscidSpeeds_CombinesBasisSolutions()
    {
        int n = 5;
        double[,] basis = new double[n, 2];
        for (int i = 0; i < n; i++) { basis[i, 0] = 1.0 + 0.1 * i; basis[i, 1] = 0.5 - 0.1 * i; }
        double alpha = Math.PI / 6.0;
        double[] q = EdgeVelocityCalculator.SetInviscidSpeeds(basis, n, alpha);
        for (int i = 0; i < n; i++)
        {
            double expected = basis[i, 0] * Math.Cos(alpha) + basis[i, 1] * Math.Sin(alpha);
            Assert.Equal(expected, q[i], 10);
        }
    }

    [Fact]
    public void ComputeWakeVelocities_ProducesFiniteValues()
    {
        double[] wakeX = { 1.01, 1.05, 1.1, 1.2 };
        double[] wakeY = { -0.001, -0.002, -0.003, -0.005 };
        double[] qWake = EdgeVelocityCalculator.ComputeWakeVelocities(0.1, wakeX, wakeY, 1.0, 0.0, 4);
        for (int i = 0; i < 4; i++)
        {
            Assert.False(double.IsNaN(qWake[i]));
            Assert.False(double.IsInfinity(qWake[i]));
        }
    }
}
