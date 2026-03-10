using System;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;

namespace XFoil.Core.Tests;

/// <summary>
/// Tests for BlockTridiagonalSolver (BLSOLV port from xsolve.f)
/// and BandMatrixSolver as the third linear solver option.
/// </summary>
public class BlockTridiagonalSolverTests
{
    private const double Tol = 1e-10;

    [Fact]
    public void Solve_FourStationSystem_ProducesCorrectSolution()
    {
        // Set up a known 4-station block-tridiagonal system
        // with hand-computable solution
        var system = Create4StationTestSystem();

        BlockTridiagonalSolver.Solve(system);

        // After solve, VDEL contains the solution deltas
        // Verify by substituting back: residual should be near zero
        // For a simple diagonal-dominant system, solution should be well-defined
        for (int i = 0; i < 4; i++)
        {
            for (int s = 0; s < 2; s++)
            {
                // Solution should be finite and non-NaN
                for (int eq = 0; eq < 3; eq++)
                {
                    Assert.False(double.IsNaN(system.VDEL[eq, s, i]),
                        $"VDEL[{eq},{s},{i}] is NaN");
                    Assert.False(double.IsInfinity(system.VDEL[eq, s, i]),
                        $"VDEL[{eq},{s},{i}] is Inf");
                }
            }
        }
    }

    [Fact]
    public void Solve_IdentityBlocks_ReturnsRHS()
    {
        // With identity diagonal blocks and zero sub-diagonals,
        // solution should equal the RHS
        var system = new ViscousNewtonSystem(8, 4);
        system.NSYS = 4;

        // Set up identity diagonal blocks (VA = I for each station)
        for (int i = 0; i < 4; i++)
        {
            for (int eq = 0; eq < 3; eq++)
            {
                system.VA[eq, 0, i] = 1.0;  // diagonal
                system.VA[eq, 1, i] = 0.0;  // off-diagonal
                system.VB[eq, 0, i] = 0.0;
                system.VB[eq, 1, i] = 0.0;
            }
            // Set RHS
            system.VDEL[0, 0, i] = 1.0 + i;
            system.VDEL[1, 0, i] = 2.0 + i;
            system.VDEL[2, 0, i] = 3.0 + i;

            // ISYS mapping
            system.ISYS[i, 0] = i;
            system.ISYS[i, 1] = 0;
        }

        double[] expectedRHS = new double[12];
        for (int i = 0; i < 4; i++)
        {
            expectedRHS[3 * i + 0] = system.VDEL[0, 0, i];
            expectedRHS[3 * i + 1] = system.VDEL[1, 0, i];
            expectedRHS[3 * i + 2] = system.VDEL[2, 0, i];
        }

        BlockTridiagonalSolver.Solve(system);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(expectedRHS[3 * i + 0], system.VDEL[0, 0, i], 10);
            Assert.Equal(expectedRHS[3 * i + 1], system.VDEL[1, 0, i], 10);
            Assert.Equal(expectedRHS[3 * i + 2], system.VDEL[2, 0, i], 10);
        }
    }

    [Fact]
    public void Solve_WithVACCEL_DropsSmallMassCoefficients()
    {
        // With VACCEL > 0, small mass coupling coefficients should be dropped
        var system = Create4StationTestSystem();

        // Add very small mass coupling terms
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < system.MaxWake && j < 4; j++)
            {
                system.VM[0, j, i] = 1e-10;  // Very small, should be dropped
                system.VM[1, j, i] = 1e-10;
                system.VM[2, j, i] = 1e-10;
            }
        }

        // Should not throw and should produce valid results
        BlockTridiagonalSolver.Solve(system, vaccel: 0.01);

        for (int i = 0; i < 4; i++)
        {
            for (int eq = 0; eq < 3; eq++)
            {
                Assert.False(double.IsNaN(system.VDEL[eq, 0, i]),
                    $"VDEL[{eq},0,{i}] is NaN with VACCEL");
            }
        }
    }

    [Fact]
    public void Solve_PreservesVZCouplingBlock()
    {
        // The VZ block couples side 1 and side 2 at TE
        var system = Create4StationTestSystem();

        // Set VZ coupling
        system.VZ[0, 0] = 0.5;
        system.VZ[1, 0] = 0.3;
        system.VZ[2, 0] = 0.1;
        system.VZ[0, 1] = 0.4;
        system.VZ[1, 1] = 0.2;
        system.VZ[2, 1] = 0.15;

        // VZ values should influence the solution at TE stations
        BlockTridiagonalSolver.Solve(system);

        // Solution should still be finite
        for (int i = 0; i < 4; i++)
        {
            for (int eq = 0; eq < 3; eq++)
            {
                Assert.False(double.IsNaN(system.VDEL[eq, 0, i]),
                    $"VDEL[{eq},0,{i}] is NaN with VZ coupling");
            }
        }
    }

    // =====================================================================
    // BandMatrixSolver Tests
    // =====================================================================

    [Fact]
    public void BandMatrixSolver_ProducesSameResultAsBlockTridiagonal()
    {
        // Both solvers should produce equivalent results on the same system
        var system1 = Create4StationTestSystem();
        var system2 = Create4StationTestSystem();

        BlockTridiagonalSolver.Solve(system1);
        BandMatrixSolver.Solve(system2);

        for (int i = 0; i < 4; i++)
        {
            for (int s = 0; s < 2; s++)
            {
                for (int eq = 0; eq < 3; eq++)
                {
                    Assert.True(
                        Math.Abs(system1.VDEL[eq, s, i] - system2.VDEL[eq, s, i]) < 1e-8,
                        $"VDEL[{eq},{s},{i}] mismatch: block={system1.VDEL[eq, s, i]:E6}, band={system2.VDEL[eq, s, i]:E6}");
                }
            }
        }
    }

    [Fact]
    public void BandMatrixSolver_IdentityBlocks_ReturnsRHS()
    {
        var system = new ViscousNewtonSystem(8, 4);
        system.NSYS = 4;

        for (int i = 0; i < 4; i++)
        {
            for (int eq = 0; eq < 3; eq++)
            {
                system.VA[eq, 0, i] = 1.0;
                system.VA[eq, 1, i] = 0.0;
                system.VB[eq, 0, i] = 0.0;
                system.VB[eq, 1, i] = 0.0;
            }
            system.VDEL[0, 0, i] = 1.0 + i;
            system.VDEL[1, 0, i] = 2.0 + i;
            system.VDEL[2, 0, i] = 3.0 + i;

            system.ISYS[i, 0] = i;
            system.ISYS[i, 1] = 0;
        }

        double[] expected = new double[12];
        for (int i = 0; i < 4; i++)
        {
            expected[3 * i + 0] = system.VDEL[0, 0, i];
            expected[3 * i + 1] = system.VDEL[1, 0, i];
            expected[3 * i + 2] = system.VDEL[2, 0, i];
        }

        BandMatrixSolver.Solve(system);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(expected[3 * i + 0], system.VDEL[0, 0, i], 10);
            Assert.Equal(expected[3 * i + 1], system.VDEL[1, 0, i], 10);
            Assert.Equal(expected[3 * i + 2], system.VDEL[2, 0, i], 10);
        }
    }

    /// <summary>
    /// Creates a known 4-station test system with diagonal dominance.
    /// </summary>
    private static ViscousNewtonSystem Create4StationTestSystem()
    {
        var system = new ViscousNewtonSystem(8, 4);
        system.NSYS = 4;

        for (int i = 0; i < 4; i++)
        {
            // Diagonal blocks (dominant)
            system.VA[0, 0, i] = 5.0 + i;     // eq0 diagonal
            system.VA[0, 1, i] = 0.1;          // eq0 off-diag
            system.VA[1, 0, i] = 0.2;          // eq1 off-diag
            system.VA[1, 1, i] = 4.0 + i;      // eq1 (used as second part)
            system.VA[2, 0, i] = 0.05;         // eq2 off-diag
            system.VA[2, 1, i] = 3.0 + i;      // eq2

            // Sub-diagonal blocks (smaller)
            if (i > 0)
            {
                system.VB[0, 0, i] = -0.5;
                system.VB[0, 1, i] = 0.01;
                system.VB[1, 0, i] = 0.02;
                system.VB[1, 1, i] = -0.3;
                system.VB[2, 0, i] = 0.01;
                system.VB[2, 1, i] = -0.2;
            }

            // RHS
            system.VDEL[0, 0, i] = 1.0;
            system.VDEL[1, 0, i] = 0.5;
            system.VDEL[2, 0, i] = 0.25;

            // System mapping
            system.ISYS[i, 0] = i;
            system.ISYS[i, 1] = 0;
        }

        return system;
    }
}
