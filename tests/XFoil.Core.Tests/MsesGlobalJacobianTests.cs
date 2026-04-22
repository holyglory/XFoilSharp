using XFoil.MsesSolver.Inviscid;
using XFoil.MsesSolver.Newton;

namespace XFoil.Core.Tests;

/// <summary>
/// P4.3 — finite-difference Jacobian tests on toy residuals and
/// on the real MsesGlobalResidual assembler.
/// </summary>
public class MsesGlobalJacobianTests
{
    [Fact]
    public void FiniteDifference_OnLinearResidual_RecoversCoefficientMatrix()
    {
        // R(x) = A·x + b should give J(x) = A identically.
        var a = new double[,]
        {
            { 2.0, 1.0, 0.0 },
            { 1.0, 3.0, 1.0 },
            { 0.0, 1.0, 2.0 },
        };
        var b = new[] { 5.0, 6.0, 7.0 };
        double[] R(double[] x) => new[]
        {
            a[0, 0] * x[0] + a[0, 1] * x[1] + a[0, 2] * x[2] + b[0],
            a[1, 0] * x[0] + a[1, 1] * x[1] + a[1, 2] * x[2] + b[1],
            a[2, 0] * x[0] + a[2, 1] * x[1] + a[2, 2] * x[2] + b[2],
        };
        var state = new[] { 1.0, 2.0, 3.0 };
        var jac = MsesGlobalJacobian.ComputeFiniteDifference(state, R);
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            Assert.True(System.Math.Abs(jac[i, j] - a[i, j]) < 1e-7,
                $"[{i},{j}]: got {jac[i, j]}, expected {a[i, j]}");
        }
    }

    [Fact]
    public void FiniteDifference_OnQuadraticResidual_GivesLinearDerivative()
    {
        // R(x) = (x_0² + x_1², 2·x_0·x_1) gives J(x) = [[2x_0, 2x_1],
        //                                              [2x_1, 2x_0]].
        double[] R(double[] x) => new[]
        {
            x[0] * x[0] + x[1] * x[1],
            2.0 * x[0] * x[1],
        };
        var state = new[] { 3.0, 4.0 };
        var jac = MsesGlobalJacobian.ComputeFiniteDifference(state, R);
        Assert.Equal(6.0, jac[0, 0], 4);
        Assert.Equal(8.0, jac[0, 1], 4);
        Assert.Equal(8.0, jac[1, 0], 4);
        Assert.Equal(6.0, jac[1, 1], 4);
    }

    [Fact]
    public void FiniteDifference_OnMsesGlobalResidual_AtZero_HasExpectedStructure()
    {
        // At state=0, MsesGlobalResidual is linear, so FD Jacobian
        // should match the analytical influence matrix + Kutta row +
        // identity σ/BL blocks exactly.
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic("0012", pointCount: 21);
        var pg = MsesInviscidPanelSolver.DiscretizePanels(geom);
        int n = pg.PanelCount;
        var layout = new MsesGlobalState(n + 1, n + 1, 2);
        var assembler = new MsesGlobalResidual(
            layout, pg, freestreamSpeed: 1.0,
            alphaRadians: 2.0 * System.Math.PI / 180.0);
        var zero = new double[layout.StateSize];
        var jac = MsesGlobalJacobian.ComputeFiniteDifference(
            zero, assembler.Compute);

        // Inviscid γ-column block (top-left (N × N+1)) should match
        // the precomputed normal-influence matrix.
        var aN = MsesInviscidPanelSolver.BuildVortexNormalInfluenceMatrix(pg);
        for (int i = 0; i < n; i++)
        for (int k = 0; k < n + 1; k++)
        {
            Assert.True(System.Math.Abs(jac[i, k] - aN[i, k]) < 1e-6,
                $"[{i},{k}] inviscid block: got {jac[i, k]}, expected {aN[i, k]}");
        }
        // Kutta row N: 1 at column 0 and column N, 0 elsewhere.
        Assert.True(System.Math.Abs(jac[n, 0] - 1.0) < 1e-7);
        Assert.True(System.Math.Abs(jac[n, n] - 1.0) < 1e-7);
        // σ placeholder rows: identity block at σ columns.
        for (int k = 0; k < layout.SigmaCount; k++)
        {
            int row = layout.SigmaOffset + k;
            int col = layout.SigmaOffset + k;
            Assert.True(System.Math.Abs(jac[row, col] - 1.0) < 1e-7,
                $"σ identity [{row},{col}]: got {jac[row, col]}");
        }
    }

    [Fact]
    public void FiniteDifference_ResidualLengthMismatch_Throws()
    {
        double[] R(double[] x) => new[] { 1.0, 2.0 };  // returns length 2, state is 3
        var state = new[] { 0.0, 0.0, 0.0 };
        Assert.Throws<System.InvalidOperationException>(
            () => MsesGlobalJacobian.ComputeFiniteDifference(state, R));
    }
}
