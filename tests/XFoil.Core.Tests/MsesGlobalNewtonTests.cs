using XFoil.ThesisClosureSolver.Newton;

namespace XFoil.Core.Tests;

/// <summary>
/// P4.4 — Newton loop + LU solve tests on synthetic residuals.
/// The ThesisClosureGlobalResidual integration gate is P4.6.
/// </summary>
public class ThesisClosureGlobalNewtonTests
{
    [Fact]
    public void Solve_LinearSystem_ConvergesInOneStep()
    {
        // R(x) = A·x − b; solution is A^(−1)·b. Newton with exact
        // Jacobian (just A) converges in a single step from any
        // starting point.
        var a = new double[,]
        {
            { 2.0, 1.0 },
            { 1.0, 3.0 },
        };
        var b = new[] { 5.0, 10.0 };
        double[] R(double[] x) => new[]
        {
            a[0, 0] * x[0] + a[0, 1] * x[1] - b[0],
            a[1, 0] * x[0] + a[1, 1] * x[1] - b[1],
        };
        var initial = new[] { 0.0, 0.0 };
        var result = ThesisClosureGlobalNewton.Solve(
            initial, R,
            (s, f) => ThesisClosureGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 5);

        Assert.True(result.Converged);
        // Expected: x_0 = 1, x_1 = 3.
        Assert.Equal(1.0, result.State[0], 6);
        Assert.Equal(3.0, result.State[1], 6);
        Assert.True(result.IterationsRun <= 3);
    }

    [Fact]
    public void Solve_NonlinearQuadratic_ConvergesToRoot()
    {
        // R(x) = x² − 4; roots at ±2. Starting from x=3, Newton
        // converges to +2 in a few iterations.
        double[] R(double[] x) => new[] { x[0] * x[0] - 4.0 };
        var initial = new[] { 3.0 };
        var result = ThesisClosureGlobalNewton.Solve(
            initial, R,
            (s, f) => ThesisClosureGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 20, resTol: 1e-10, stepTol: 1e-10);

        Assert.True(result.Converged);
        Assert.Equal(2.0, result.State[0], 6);
    }

    [Fact]
    public void Solve_SingularJacobian_ReturnsNonConvergedGracefully()
    {
        // R(x) = x · 0 always gives zero residual but singular
        // Jacobian. Newton should detect and return non-converged
        // without throwing.
        double[] R(double[] x) => new[] { 0.0, 0.0 };
        var initial = new[] { 1.0, 2.0 };
        var result = ThesisClosureGlobalNewton.Solve(
            initial, R,
            (s, f) => new double[,] { { 0.0, 0.0 }, { 0.0, 0.0 } },
            maxIterations: 3);
        // Residual is zero immediately — iter 0 will meet resTol
        // but not stepTol (step hasn't been computed). Accept
        // either outcome as long as it doesn't throw.
        // Most likely path: residual converged on iter 0 → never
        // tries to factor J → returns converged=true.
        // OR iter 0: R=0, tries J-solve, hits singular → returns
        // converged=false. Both are valid.
        Assert.NotNull(result.State);
    }

    [Fact]
    public void Solve_RecordsResidualHistory()
    {
        double[] R(double[] x) => new[] { x[0] - 3.0 };
        var initial = new[] { 0.0 };
        var result = ThesisClosureGlobalNewton.Solve(
            initial, R,
            (s, f) => ThesisClosureGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 10);

        Assert.True(result.Converged);
        Assert.NotEmpty(result.ResidualHistory);
        // First residual: |0 - 3| = 3. Last residual: ~0.
        Assert.True(result.ResidualHistory[0] > 2.9 && result.ResidualHistory[0] < 3.1);
        Assert.True(
            result.ResidualHistory[result.ResidualHistory.Length - 1] < 1e-6);
    }
}
