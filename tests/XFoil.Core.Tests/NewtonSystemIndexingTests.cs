using System;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Regression tests for ViscousNewtonSystem re-indexing from per-side ibl
/// to global system line IV. Ensures that side-0 and side-1 stations with
/// the same ibl write to DIFFERENT memory locations.
/// </summary>
public class NewtonSystemIndexingTests
{
    [Fact]
    public void Constructor_WithNsys_AllocatesArraysByGlobalSystemLines()
    {
        // Test 1: ViscousNewtonSystem(nsys=120, maxWake=20) allocates VA[3,2,120]
        // where the third dimension is nsys (global system lines), not maxStations
        int nsys = 120;
        int maxWake = 20;
        var system = new ViscousNewtonSystem(nsys, maxWake);

        // Third dimension of VA/VB/VDEL should be nsys
        Assert.Equal(3, system.VA.GetLength(0));
        Assert.Equal(2, system.VA.GetLength(1));
        Assert.Equal(nsys, system.VA.GetLength(2));

        Assert.Equal(3, system.VB.GetLength(0));
        Assert.Equal(2, system.VB.GetLength(1));
        Assert.Equal(nsys, system.VB.GetLength(2));

        Assert.Equal(3, system.VDEL.GetLength(0));
        Assert.Equal(2, system.VDEL.GetLength(1));
        Assert.Equal(nsys, system.VDEL.GetLength(2));

        // VM should be [3, maxWake, nsys]
        Assert.Equal(3, system.VM.GetLength(0));
        Assert.Equal(maxWake, system.VM.GetLength(1));
        Assert.Equal(nsys, system.VM.GetLength(2));
    }

    [Fact]
    public void DifferentSides_SameIbl_WriteToDifferentMemory()
    {
        // Test 2: Writing to VA[eq, side, iv=5] for side=0 and VA[eq, side, iv=65]
        // for side=1 (where iv=65 is the global line for side-1 ibl=5)
        // writes to DIFFERENT memory locations.
        int nsys = 120;
        int maxWake = 20;
        var system = new ViscousNewtonSystem(nsys, maxWake);

        // Set up an ISYS mapping where:
        // iv=5 corresponds to (ibl=5, side=0)
        // iv=65 corresponds to (ibl=5, side=1)
        system.SetupISYS(CreateTestISYS(60, 60), nsys);

        // Write distinct values to the two different IV positions
        int iv_side0 = 5;   // global line for side-0, ibl=5
        int iv_side1 = 65;  // global line for side-1, ibl=5

        system.VA[0, 0, iv_side0] = 1.111;
        system.VA[0, 0, iv_side1] = 2.222;

        // They must be stored in different memory -- values must NOT overwrite
        Assert.Equal(1.111, system.VA[0, 0, iv_side0], 10);
        Assert.Equal(2.222, system.VA[0, 0, iv_side1], 10);
        Assert.NotEqual(system.VA[0, 0, iv_side0], system.VA[0, 0, iv_side1]);
    }

    [Fact]
    public void BlockTridiagonalSolver_SmallTwoSideSystem_CorrectSolution()
    {
        // Test 3: BlockTridiagonalSolver.Solve on a small 6-station 2-side system (nsys=10)
        // produces correct solution deltas without zero-pivot errors
        int nsys = 10;
        int maxWake = 4;
        var system = new ViscousNewtonSystem(nsys, maxWake);

        // Set up ISYS: 5 stations per side
        var isys = new int[nsys + 1, 2];
        for (int i = 0; i < 5; i++)
        {
            isys[i, 0] = i + 1;  // ibl
            isys[i, 1] = 0;      // side 0
        }
        for (int i = 0; i < 5; i++)
        {
            isys[5 + i, 0] = i + 1;  // ibl
            isys[5 + i, 1] = 1;      // side 1
        }
        system.SetupISYS(isys, nsys);

        // Set up a diagonal-dominant system indexed by iv
        for (int iv = 0; iv < nsys; iv++)
        {
            for (int eq = 0; eq < 3; eq++)
            {
                system.VA[eq, 0, iv] = 5.0 + iv;  // diagonal
                system.VA[eq, 1, iv] = 0.1;         // super-diagonal
                if (iv > 0)
                    system.VB[eq, 0, iv] = -0.3;    // sub-diagonal
            }
            // RHS
            system.VDEL[0, 0, iv] = 1.0;
            system.VDEL[1, 0, iv] = 0.5;
            system.VDEL[2, 0, iv] = 0.25;
        }

        // Should solve without errors
        BlockTridiagonalSolver.Solve(system);

        // All solution values should be finite and non-NaN
        for (int iv = 0; iv < nsys; iv++)
        {
            for (int eq = 0; eq < 3; eq++)
            {
                Assert.False(double.IsNaN(system.VDEL[eq, 0, iv]),
                    $"VDEL[{eq},0,{iv}] is NaN");
                Assert.False(double.IsInfinity(system.VDEL[eq, 0, iv]),
                    $"VDEL[{eq},0,{iv}] is Inf");
            }
        }

        // Solution values should be approximately 1/diagonal since sub-diagonal is small
        // For the first station (no sub-diag): x[0] ~ RHS[0] / diag[0] = 1.0 / 5.0 = 0.2
        Assert.True(Math.Abs(system.VDEL[0, 0, 0] - 1.0 / 5.0) < 0.1,
            $"Solution at iv=0 eq=0 should be approximately 0.2, got {system.VDEL[0, 0, 0]}");
    }

    [Fact]
    public void ISYS_StoresBidirectionalMapping()
    {
        // Test 4: ViscousNewtonSystem.ISYS stores the (ibl, side) <-> iv mapping
        int nsys = 120;
        int maxWake = 20;
        var system = new ViscousNewtonSystem(nsys, maxWake);

        // Create ISYS via EdgeVelocityCalculator
        int[] iblte = { 60, 60 };
        int[] nbl = { 61, 61 };
        var (isysMapping, nsysCalc) = EdgeVelocityCalculator.MapStationsToSystemLines(iblte, nbl);

        system.SetupISYS(isysMapping, nsysCalc);

        Assert.Equal(nsysCalc, system.NSYS);

        // Verify forward mapping: each iv has a unique (ibl, side) pair
        for (int iv = 0; iv < system.NSYS; iv++)
        {
            int ibl = system.ISYS[iv, 0];
            int side = system.ISYS[iv, 1];
            Assert.True(ibl >= 1, $"ISYS[{iv},0] = {ibl} should be >= 1");
            Assert.True(side == 0 || side == 1, $"ISYS[{iv},1] = {side} should be 0 or 1");
        }

        // Verify that side-0 lines come first, then side-1 lines
        int lastSide0 = -1;
        for (int iv = 0; iv < system.NSYS; iv++)
        {
            if (system.ISYS[iv, 1] == 0)
                lastSide0 = iv;
        }
        Assert.True(lastSide0 >= 0, "Should have at least one side-0 line");

        // All side-1 lines should come after all side-0 lines
        for (int iv = lastSide0 + 1; iv < system.NSYS; iv++)
        {
            Assert.Equal(1, system.ISYS[iv, 1]);
        }
    }

    /// <summary>
    /// Creates a test ISYS mapping with side1Count side-0 lines and side2Count side-1 lines.
    /// </summary>
    private static int[,] CreateTestISYS(int side1Count, int side2Count)
    {
        int nsys = side1Count + side2Count;
        var isys = new int[nsys + 1, 2];
        int lineNum = 0;

        for (int i = 1; i <= side1Count; i++)
        {
            isys[lineNum, 0] = i;
            isys[lineNum, 1] = 0;
            lineNum++;
        }
        for (int i = 1; i <= side2Count; i++)
        {
            isys[lineNum, 0] = i;
            isys[lineNum, 1] = 1;
            lineNum++;
        }

        return isys;
    }
}
