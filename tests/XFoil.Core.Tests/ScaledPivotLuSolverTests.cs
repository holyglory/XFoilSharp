using XFoil.Solver.Numerics;

namespace XFoil.Core.Tests;

public class ScaledPivotLuSolverTests
{
    /// <summary>
    /// Test 1: Decompose + backsolve a 3x3 system with known solution.
    /// System: [2 1 1; 4 3 3; 8 7 9] * x = [4; 10; 24] => x = [1; 1; 1]
    /// </summary>
    [Fact]
    public void DecomposeAndBackSubstitute_3x3KnownSolution_ExactMatch()
    {
        var matrix = new double[3, 3]
        {
            { 2.0, 1.0, 1.0 },
            { 4.0, 3.0, 3.0 },
            { 8.0, 7.0, 9.0 }
        };
        var pivots = new int[3];
        var rhs = new double[] { 4.0, 10.0, 24.0 };

        ScaledPivotLuSolver.Decompose(matrix, pivots, 3);
        ScaledPivotLuSolver.BackSubstitute(matrix, pivots, rhs, 3);

        Assert.True(Math.Abs(rhs[0] - 1.0) < 1e-12, $"x[0] = {rhs[0]}, expected 1.0");
        Assert.True(Math.Abs(rhs[1] - 1.0) < 1e-12, $"x[1] = {rhs[1]}, expected 1.0");
        Assert.True(Math.Abs(rhs[2] - 1.0) < 1e-12, $"x[2] = {rhs[2]}, expected 1.0");
    }

    /// <summary>
    /// Test 2: Decompose + backsolve a 5x5 system requiring row swaps.
    /// Uses a system where the natural pivot order is suboptimal.
    /// </summary>
    [Fact]
    public void DecomposeAndBackSubstitute_5x5RequiringRowSwaps_CorrectSolution()
    {
        // A system where row swaps are essential for stability
        var matrix = new double[5, 5]
        {
            { 0.001, 2.0,   3.0,   4.0,   5.0 },
            { 1.0,   1.0,   2.0,   3.0,   4.0 },
            { 2.0,   3.0,   0.001, 5.0,   6.0 },
            { 3.0,   4.0,   5.0,   0.001, 7.0 },
            { 4.0,   5.0,   6.0,   7.0,   0.001 }
        };

        // Expected solution x = [1, 1, 1, 1, 1]
        double[] expectedSolution = { 1.0, 1.0, 1.0, 1.0, 1.0 };

        // Compute RHS = A * x
        var rhs = new double[5];
        for (int i = 0; i < 5; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < 5; j++)
            {
                sum += matrix[i, j] * expectedSolution[j];
            }
            rhs[i] = sum;
        }

        var pivots = new int[5];
        ScaledPivotLuSolver.Decompose(matrix, pivots, 5);
        ScaledPivotLuSolver.BackSubstitute(matrix, pivots, rhs, 5);

        for (int i = 0; i < 5; i++)
        {
            Assert.True(
                Math.Abs(rhs[i] - expectedSolution[i]) < 1e-10,
                $"x[{i}] = {rhs[i]:E15}, expected {expectedSolution[i]}");
        }
    }

    /// <summary>
    /// Test 3: Verify scaled pivoting selects the correct pivot row
    /// (not just max absolute value, but max after row scaling).
    /// </summary>
    [Fact]
    public void Decompose_ScaledPivoting_SelectsCorrectPivotRow()
    {
        // Row 0: [1000, 999] -- max element = 1000, scale = 1/1000
        // Row 1: [1, 0.5]    -- max element = 1, scale = 1/1
        // Unscaled: pivot on row 0 (|1000| > |1|)
        // Scaled: row 0 gives 1000 * (1/1000) = 1.0, row 1 gives 1 * (1/1) = 1.0
        // For column 0, both scaled values equal; Fortran picks the later one if >=.

        // Use a matrix where scaled pivoting clearly differs from unscaled
        // Row 0: [1e6, 1e6-1] -- scale = 1/1e6
        // Row 1: [1.0, 0.0]   -- scale = 1/1
        // Unscaled: pivot row 0 (|1e6| >> |1|)
        // Scaled: row 0 = 1e6 * 1/1e6 = 1.0, row 1 = 1 * 1/1 = 1.0
        // Fortran: GE with >= picks row 1 (last row with max scaled value)

        // More decisive: row0 has large values but nearly-singular leading element relative to its scale
        var matrix = new double[3, 3]
        {
            { 1e-8, 1.0,   1.0 },
            { 1.0,  1e-8,  1.0 },
            { 1.0,  1.0,   1e-8 }
        };

        // Without pivoting this would be catastrophic.
        // Scaled pivoting must choose a row where the scaled |element| is largest.
        var pivots = new int[3];
        var matCopy = (double[,])matrix.Clone();

        ScaledPivotLuSolver.Decompose(matCopy, pivots, 3);

        // The pivot for column 0 should NOT be row 0 (which has tiny leading element relative to its max)
        // Row 0 max = 1.0, so scale = 1.0, scaled value = |1e-8| * 1 = 1e-8
        // Row 1 max = 1.0, so scale = 1.0, scaled value = |1| * 1 = 1.0
        // Row 2 max = 1.0, so scale = 1.0, scaled value = |1| * 1 = 1.0
        // Pivot should be row 1 or row 2 (last >= wins in Fortran, so row 2)
        Assert.NotEqual(0, pivots[0]);

        // Verify the LU factors produce correct results
        var rhs = new double[] { 2.0 + 1e-8, 2.0 + 1e-8, 2.0 + 1e-8 };
        ScaledPivotLuSolver.BackSubstitute(matCopy, pivots, rhs, 3);

        // Expected solution is approximately [1, 1, 1]
        for (int i = 0; i < 3; i++)
        {
            Assert.True(
                Math.Abs(rhs[i] - 1.0) < 1e-4,
                $"x[{i}] = {rhs[i]:E15}, expected ~1.0");
        }
    }

    /// <summary>
    /// Test 4: Backsolve two different RHS vectors against the same LU-factored matrix.
    /// </summary>
    [Fact]
    public void BackSubstitute_TwoRhsVectors_BothCorrect()
    {
        var matrix = new double[3, 3]
        {
            { 4.0, 2.0, 1.0 },
            { 2.0, 5.0, 3.0 },
            { 1.0, 3.0, 6.0 }
        };

        var pivots = new int[3];
        ScaledPivotLuSolver.Decompose(matrix, pivots, 3);

        // RHS 1: solve for x = [1, 0, 0]
        // Original matrix * [1,0,0] = [4, 2, 1]
        var rhs1 = new double[] { 4.0, 2.0, 1.0 };
        ScaledPivotLuSolver.BackSubstitute(matrix, pivots, rhs1, 3);
        Assert.True(Math.Abs(rhs1[0] - 1.0) < 1e-12, $"rhs1[0] = {rhs1[0]}");
        Assert.True(Math.Abs(rhs1[1]) < 1e-12, $"rhs1[1] = {rhs1[1]}");
        Assert.True(Math.Abs(rhs1[2]) < 1e-12, $"rhs1[2] = {rhs1[2]}");

        // RHS 2: solve for x = [0, 1, 0]
        // Original matrix * [0,1,0] = [2, 5, 3]
        var rhs2 = new double[] { 2.0, 5.0, 3.0 };
        ScaledPivotLuSolver.BackSubstitute(matrix, pivots, rhs2, 3);
        Assert.True(Math.Abs(rhs2[0]) < 1e-12, $"rhs2[0] = {rhs2[0]}");
        Assert.True(Math.Abs(rhs2[1] - 1.0) < 1e-12, $"rhs2[1] = {rhs2[1]}");
        Assert.True(Math.Abs(rhs2[2]) < 1e-12, $"rhs2[2] = {rhs2[2]}");
    }

    /// <summary>
    /// Test 5: Near-singular matrix (condition number ~1e10) still produces reasonable solution.
    /// </summary>
    [Fact]
    public void DecomposeAndBackSubstitute_NearSingularMatrix_ReasonableSolution()
    {
        // Hilbert-like matrix with high condition number
        const int n = 4;
        var matrix = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                matrix[i, j] = 1.0 / (i + j + 1);
            }
        }

        // Solution x = [1, 1, 1, 1], compute b = A*x
        var rhs = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < n; j++)
            {
                sum += matrix[i, j];
            }
            rhs[i] = sum;
        }

        var pivots = new int[n];
        ScaledPivotLuSolver.Decompose(matrix, pivots, n);
        ScaledPivotLuSolver.BackSubstitute(matrix, pivots, rhs, n);

        // With condition number ~1.5e4 for 4x4 Hilbert, expect solution within ~1e-10
        for (int i = 0; i < n; i++)
        {
            Assert.True(
                Math.Abs(rhs[i] - 1.0) < 1e-8,
                $"x[{i}] = {rhs[i]:E15}, expected 1.0 (near-singular system)");
        }
    }
}
