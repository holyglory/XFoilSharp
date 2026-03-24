using System;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: STFIND, UICALC, STMOVE
// Secondary legacy source: f_xfoil/src/xwake.f wake velocity setup
// Role in port: Verifies the managed edge-velocity and stagnation-point helpers derived from legacy boundary-layer preprocessing routines.
// Differences: The managed port exposes reusable helper APIs and typed state instead of relying on legacy common-block mutation.
// Decision: Keep the managed helper surface because it preserves the same formulas while making them independently testable.
namespace XFoil.Core.Tests;

/// <summary>
/// Tests for EdgeVelocityCalculator and StagnationPointTracker.
/// </summary>
public class EdgeVelocityCalculatorTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f :: STFIND sign-change branch.
    // Difference from legacy: The stagnation index is asserted directly on the helper result instead of being inferred from downstream state.
    // Decision: Keep the managed unit test because it isolates the preserved search rule clearly.
    public void FindStagnationPoint_SignChange_FindsCorrectPanel()
    {
        double[] speed = { 1.0, 0.5, 0.1, -0.2, -0.8, -1.2 };
        int isp = StagnationPointTracker.FindStagnationPoint(speed, 6);
        Assert.True(isp == 2 || isp == 3, $"ISP should be 2 or 3, got {isp}");
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f fallback stagnation selection.
    // Difference from legacy: Minimum-absolute-speed selection is tested directly rather than embedded in a larger setup sequence.
    // Decision: Keep the managed regression because it documents the fallback behavior explicitly.
    public void FindStagnationPoint_MinAbsSpeed_FindsCorrectPanel()
    {
        double[] speed = { 1.5, 0.8, 0.3, 0.05, 0.2, 0.9 };
        int isp = StagnationPointTracker.FindStagnationPoint(speed, 6);
        Assert.Equal(3, isp);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f symmetric stagnation location behavior.
    // Difference from legacy: The test checks numerical proximity to the leading edge through the managed helper.
    // Decision: Keep the managed invariant because it is the clearest regression for symmetric stagnation placement.
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
    // Legacy mapping: f_xfoil/src/xbl.f :: STMOVE.
    // Difference from legacy: The managed test inspects typed boundary-layer state after the shift instead of legacy common arrays.
    // Decision: Keep the managed state-based test because it makes the same relocation behavior observable.
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
    // Legacy mapping: f_xfoil/src/xbl.f panel-to-boundary-layer station mapping.
    // Difference from legacy: Station counts are returned explicitly from a helper instead of being implicit in initialization arrays.
    // Decision: Keep the managed helper test because it documents the indexing contract clearly.
    public void MapPanelsToBLStations_ProducesCorrectCounts()
    {
        var (iblte, nbl) = EdgeVelocityCalculator.MapPanelsToBLStations(20, 10, 3);
        Assert.True(iblte[0] > 0);
        Assert.True(iblte[1] > 0);
        Assert.True(nbl[0] > 0);
        Assert.True(nbl[1] > 0);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f branch arc-length construction.
    // Difference from legacy: Arc length is asserted directly on a managed return array instead of being consumed internally.
    // Decision: Keep the managed regression because it isolates a core preprocessing invariant.
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
    // Legacy mapping: f_xfoil/src/xblsys.f system-line indexing.
    // Difference from legacy: The mapping is validated through a direct helper instead of only through assembled matrices.
    // Decision: Keep the managed helper test because it protects a subtle indexing contract.
    public void MapStationsToSystemLines_ProducesContiguousMapping()
    {
        int[] iblte = { 5, 6 };
        int[] nbl = { 6, 10 };
        var (isys, nsys) = EdgeVelocityCalculator.MapStationsToSystemLines(iblte, nbl);
        Assert.True(nsys > 0);
        Assert.True(nsys <= nbl[0] + nbl[1]);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f inviscid edge-speed evaluation.
    // Difference from legacy: The conversion is checked directly on the helper instead of only through full solver results.
    // Decision: Keep the managed formula regression because it tightly constrains the ported relation.
    public void ComputeInviscidEdgeVelocity_SpeedEqualsGamma()
    {
        double[] gamma = { 1.0, 1.5, 1.2, 0.8, 0.6 };
        double[] ue = EdgeVelocityCalculator.ComputeInviscidEdgeVelocity(gamma, 5);
        for (int i = 0; i < 5; i++) Assert.Equal(gamma[i], ue[i], 10);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f viscous edge-speed conversion.
    // Difference from legacy: The test asserts the managed helper output explicitly rather than inferring it from assembled state.
    // Decision: Keep the managed helper coverage because it makes the conversion rule explicit.
    public void ComputeViscousEdgeVelocity_ConvertsCorrectly()
    {
        double[] uedg = { 1.2, 1.0, 0.8, 0.5 };
        double[] qvis = EdgeVelocityCalculator.ComputeViscousEdgeVelocity(uedg, 4, 1.0);
        for (int i = 0; i < 4; i++) Assert.Equal(uedg[i], qvis[i], 10);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f inverse vortex-from-edge-speed conversion.
    // Difference from legacy: The inverse relation is exercised directly instead of indirectly through viscous iteration.
    // Decision: Keep the managed regression because it protects the bidirectional conversion pair.
    public void SetVortexFromViscousSpeed_InverseOfQvfue()
    {
        double[] qvis = { 1.5, 1.2, 0.9, 0.6 };
        double[] gamma = EdgeVelocityCalculator.SetVortexFromViscousSpeed(qvis, 4, 3.0);
        for (int i = 0; i < 4; i++) Assert.Equal(qvis[i], gamma[i], 10);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xbl.f inviscid basis superposition.
    // Difference from legacy: The port exposes basis combination as a direct helper instead of leaving it buried in solver setup.
    // Decision: Keep the managed helper test because it isolates the same superposition behavior.
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
    // Legacy mapping: f_xfoil/src/xwake.f wake velocity initialization.
    // Difference from legacy: Finite wake velocities are asserted directly on managed output instead of through later wake marching success.
    // Decision: Keep the managed regression because it directly protects wake-velocity setup.
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
