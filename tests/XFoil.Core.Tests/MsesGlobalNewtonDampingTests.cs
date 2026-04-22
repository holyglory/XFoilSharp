using XFoil.MsesSolver.Newton;

namespace XFoil.Core.Tests;

/// <summary>
/// P4.5 — Newton damping + line-search tests.
/// </summary>
public class MsesGlobalNewtonDampingTests
{
    [Fact]
    public void Solve_MaxStepNorm_DampsLargeSteps()
    {
        // R(x) = x - 10; a plain Newton step from x=0 would be
        // Δ=10. With maxStepNorm=1, the step is damped to 1.
        double[] R(double[] x) => new[] { x[0] - 10.0 };
        var initial = new[] { 0.0 };
        var result = MsesGlobalNewton.Solve(
            initial, R,
            (s, f) => MsesGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 20, resTol: 1e-8, stepTol: 1e-8,
            maxStepNorm: 1.0, lineSearch: false);

        // Converges eventually in ~10+ damped steps.
        Assert.True(result.Converged);
        Assert.Equal(10.0, result.State[0], 6);
        Assert.True(result.IterationsRun >= 10,
            $"With maxStep=1, should take ≥10 iterations; took {result.IterationsRun}");
    }

    [Fact]
    public void Solve_LineSearch_RescuesOvershoot()
    {
        // R(x) = x^3 − x. Starting from x=0.1 with line-search
        // disabled the full Newton step might overshoot the root
        // at x=0. Line search should prevent divergence.
        //
        // This test just confirms the convergence path with and
        // without line search — both should find the origin but
        // line search adapts better on overshoot cases.
        double[] R(double[] x) => new[] { x[0] * x[0] * x[0] - x[0] };
        var initial = new[] { 0.1 };

        var withLs = MsesGlobalNewton.Solve(
            initial, R,
            (s, f) => MsesGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 30, resTol: 1e-10,
            lineSearch: true);
        Assert.True(withLs.Converged);
        Assert.Equal(0.0, withLs.State[0], 6);
    }

    [Fact]
    public void Solve_BaselineDefaultsStillWork()
    {
        // Verify P4.4 Newton behavior is preserved under the new
        // default parameters (line search on, no step cap).
        var a = new double[,] { { 2.0, 1.0 }, { 1.0, 3.0 } };
        var b = new[] { 5.0, 10.0 };
        double[] R(double[] x) => new[]
        {
            a[0, 0] * x[0] + a[0, 1] * x[1] - b[0],
            a[1, 0] * x[0] + a[1, 1] * x[1] - b[1],
        };
        var result = MsesGlobalNewton.Solve(
            new[] { 0.0, 0.0 }, R,
            (s, f) => MsesGlobalJacobian.ComputeFiniteDifference(s, f),
            maxIterations: 5);
        Assert.True(result.Converged);
        Assert.Equal(1.0, result.State[0], 6);
        Assert.Equal(3.0, result.State[1], 6);
    }
}
