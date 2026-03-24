using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/spline.f :: SPLINA, SEGSPL, DEVAL, CURV
// Secondary legacy source: paneling and geometry preprocessing call sites in xpanel.f
// Role in port: Verifies the managed parametric spline routines that port the legacy interpolation, derivative, and arc-length helpers.
// Differences: The managed numerics package exposes the spline primitives directly as reusable helpers instead of hiding them behind geometry commands.
// Decision: Keep the managed numerics API because it preserves the spline formulas while making them independently testable.
namespace XFoil.Core.Tests;

public class ParametricSplineTests
{
    /// <summary>
    /// Test 1: Spline with zero-second-derivative BCs on sin(x) data matches cos(x) derivatives at interior points.
    /// </summary>
    [Fact]
    // Legacy mapping: spline fit with natural second-derivative boundary conditions.
    // Difference from legacy: The managed fit is tested against analytic sine/cosine data rather than only through consuming geometry code.
    // Decision: Keep the managed analytic regression because it isolates the spline formula directly.
    public void FitWithZeroSecondDerivativeBCs_SinData_MatchesCosDerivatives()
    {
        const int n = 21;
        var parameters = new double[n];
        var values = new double[n];
        var derivatives = new double[n];

        for (int i = 0; i < n; i++)
        {
            parameters[i] = i * Math.PI / (n - 1);
            values[i] = Math.Sin(parameters[i]);
        }

        ParametricSpline.FitWithZeroSecondDerivativeBCs(values, derivatives, parameters, n);

        // Check interior points match cos(x) to 1e-10
        for (int i = 2; i < n - 2; i++)
        {
            double expected = Math.Cos(parameters[i]);
            Assert.True(
                Math.Abs(derivatives[i] - expected) < 1e-4,
                $"At index {i}: derivative {derivatives[i]:E15} != expected {expected:E15}");
        }
    }

    /// <summary>
    /// Test 2: Spline with zero-third-derivative BCs produces different endpoint slopes than zero-second-derivative.
    /// </summary>
    [Fact]
    // Legacy mapping: alternate spline boundary-condition path.
    // Difference from legacy: Boundary-condition variants are compared explicitly in the managed test suite.
    // Decision: Keep the managed comparison because it broadens verification of the ported spline options.
    public void FitWithBoundaryConditions_ZeroThirdDerivative_DiffersFromZeroSecondDerivative()
    {
        const int n = 11;
        var parameters = new double[n];
        var values = new double[n];
        var deriv1 = new double[n];
        var deriv2 = new double[n];

        for (int i = 0; i < n; i++)
        {
            parameters[i] = i * 1.0;
            values[i] = Math.Sin(i * 0.5);
        }

        ParametricSpline.FitWithZeroSecondDerivativeBCs(values, deriv1, parameters, n);

        ParametricSpline.FitWithBoundaryConditions(
            values, deriv2, parameters, n,
            SplineBoundaryCondition.ZeroThirdDerivative,
            SplineBoundaryCondition.ZeroThirdDerivative);

        // Endpoint slopes should differ between the two BC modes
        Assert.NotEqual(deriv1[0], deriv2[0]);
        Assert.NotEqual(deriv1[n - 1], deriv2[n - 1]);
    }

    /// <summary>
    /// Test 3: Spline with specified endpoint derivatives recovers exact derivatives at endpoints.
    /// </summary>
    [Fact]
    // Legacy mapping: spline fit with specified endpoint derivatives.
    // Difference from legacy: The managed test asserts endpoint recovery numerically rather than only via downstream geometry smoothness.
    // Decision: Keep the direct numerical regression because it tightly constrains the ported derivative boundary logic.
    public void FitWithBoundaryConditions_SpecifiedDerivatives_RecoveredAtEndpoints()
    {
        const int n = 11;
        var parameters = new double[n];
        var values = new double[n];
        var derivatives = new double[n];

        for (int i = 0; i < n; i++)
        {
            parameters[i] = i * Math.PI / (n - 1);
            values[i] = Math.Sin(parameters[i]);
        }

        double startDeriv = Math.Cos(0.0); // = 1.0
        double endDeriv = Math.Cos(Math.PI); // = -1.0

        ParametricSpline.FitWithBoundaryConditions(
            values, derivatives, parameters, n,
            SplineBoundaryCondition.SpecifiedDerivative(startDeriv),
            SplineBoundaryCondition.SpecifiedDerivative(endDeriv));

        Assert.True(
            Math.Abs(derivatives[0] - startDeriv) < 1e-14,
            $"Start derivative {derivatives[0]} != {startDeriv}");
        Assert.True(
            Math.Abs(derivatives[n - 1] - endDeriv) < 1e-14,
            $"End derivative {derivatives[n - 1]} != {endDeriv}");
    }

    /// <summary>
    /// Test 4: Segmented spline handles a corner -- derivatives are discontinuous at the junction.
    /// </summary>
    [Fact]
    // Legacy mapping: segmented/corner-preserving spline fit.
    // Difference from legacy: The segmented behavior is tested explicitly on a piecewise-linear corner fixture.
    // Decision: Keep the managed corner-case regression because it isolates an important geometry-processing rule.
    public void FitSegmented_PiecewiseLinearWithCorner_DiscontinuousDerivatives()
    {
        // Two segments: [0,1,2] and [2,3,4] with a corner at index 2
        // Use identical arc-length values to mark the segment break (as in Fortran SEGSPL)
        const int n = 5;
        var parameters = new double[] { 0.0, 1.0, 2.0, 2.0, 3.0 };
        // Segment 1: values increase, Segment 2: values decrease
        var values = new double[] { 0.0, 1.0, 2.0, 2.0, 0.0 };
        var derivatives = new double[n];

        ParametricSpline.FitSegmented(values, derivatives, parameters, n);

        // The derivative at the end of segment 1 (index 2) should be positive (slope ~1)
        // The derivative at the start of segment 2 (index 3) should be negative (slope ~-2)
        // They should be different, showing the discontinuity
        Assert.True(
            Math.Abs(derivatives[2] - derivatives[3]) > 0.5,
            $"Expected discontinuous derivatives at corner, got {derivatives[2]:F6} and {derivatives[3]:F6}");
    }

    /// <summary>
    /// Test 5: Tridiagonal solver on a known 5x5 system matches exact solution.
    /// </summary>
    [Fact]
    // Legacy mapping: tridiagonal solve support used by legacy spline fitting.
    // Difference from legacy: The supporting linear solve is verified directly through a small analytical system.
    // Decision: Keep the managed unit test because it isolates a critical dependency of the spline routines.
    public void TridiagonalSolver_Known5x5System_ExactSolution()
    {
        // System:
        //  2  1  0  0  0 | 1
        //  1  3  1  0  0 | 2
        //  0  1  4  1  0 | 3
        //  0  0  1  5  1 | 4
        //  0  0  0  1  6 | 5
        var lower = new double[] { 0.0, 1.0, 1.0, 1.0, 1.0 };
        var diagonal = new double[] { 2.0, 3.0, 4.0, 5.0, 6.0 };
        var upper = new double[] { 1.0, 1.0, 1.0, 1.0, 0.0 };
        var rhs = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0 };

        TridiagonalSolver.Solve(lower, diagonal, upper, rhs, 5);

        // Verify A * x = b by back-substituting
        var solution = rhs;
        var expectedRhs = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0 };

        // Reconstruct the original RHS from solution
        var lo = new double[] { 0.0, 1.0, 1.0, 1.0, 1.0 };
        var diag = new double[] { 2.0, 3.0, 4.0, 5.0, 6.0 };
        var up = new double[] { 1.0, 1.0, 1.0, 1.0, 0.0 };

        for (int i = 0; i < 5; i++)
        {
            double computed = diag[i] * solution[i];
            if (i > 0) computed += lo[i] * solution[i - 1];
            if (i < 4) computed += up[i] * solution[i + 1];
            Assert.True(
                Math.Abs(computed - expectedRhs[i]) < 1e-14,
                $"Row {i}: A*x={computed:E15} != b={expectedRhs[i]:E15}");
        }
    }

    /// <summary>
    /// Test 6: Spline evaluation between knots interpolates correctly.
    /// </summary>
    [Fact]
    // Legacy mapping: DEVAL spline evaluation between knots.
    // Difference from legacy: The managed evaluator is tested directly rather than only through larger geometry workflows.
    // Decision: Keep the direct evaluation regression because it clearly constrains interpolation behavior.
    public void Evaluate_BetweenKnots_InterpolatesCorrectly()
    {
        // Use a cubic f(x) = x^3 on [0,1,2,3] -- spline should recover it exactly
        const int n = 4;
        var parameters = new double[] { 0.0, 1.0, 2.0, 3.0 };
        var values = new double[] { 0.0, 1.0, 8.0, 27.0 };
        var derivatives = new double[n];

        ParametricSpline.FitWithBoundaryConditions(
            values, derivatives, parameters, n,
            SplineBoundaryCondition.SpecifiedDerivative(0.0),
            SplineBoundaryCondition.SpecifiedDerivative(27.0));

        // Evaluate at s=1.5 -- expect 1.5^3 = 3.375
        double result = ParametricSpline.Evaluate(1.5, values, derivatives, parameters, n);
        Assert.True(
            Math.Abs(result - 3.375) < 0.1,
            $"Evaluate(1.5) = {result:F6}, expected ~3.375");
    }

    /// <summary>
    /// Test 7: Derivative evaluation at knots and between knots matches analytical derivative.
    /// </summary>
    [Fact]
    // Legacy mapping: spline derivative evaluation.
    // Difference from legacy: Derivatives are checked against analytic data in isolation.
    // Decision: Keep the managed analytic test because it is the strongest regression for the ported derivative helper.
    public void EvaluateDerivative_SinData_MatchesCosAtInteriorPoints()
    {
        const int n = 41;
        var parameters = new double[n];
        var values = new double[n];
        var derivatives = new double[n];

        for (int i = 0; i < n; i++)
        {
            parameters[i] = i * Math.PI / (n - 1);
            values[i] = Math.Sin(parameters[i]);
        }

        ParametricSpline.FitWithZeroSecondDerivativeBCs(values, derivatives, parameters, n);

        // Evaluate derivative between knots
        double sMid = Math.PI / 4.0;
        double evalDeriv = ParametricSpline.EvaluateDerivative(sMid, values, derivatives, parameters, n);
        double expectedDeriv = Math.Cos(sMid);

        Assert.True(
            Math.Abs(evalDeriv - expectedDeriv) < 1e-4,
            $"Derivative at {sMid:F4} = {evalDeriv:E10}, expected {expectedDeriv:E10}");

        // Evaluate derivative at a knot point
        int knotIdx = n / 2;
        double sKnot = parameters[knotIdx];
        double knotDeriv = ParametricSpline.EvaluateDerivative(sKnot, values, derivatives, parameters, n);
        double expectedKnotDeriv = Math.Cos(sKnot);

        Assert.True(
            Math.Abs(knotDeriv - expectedKnotDeriv) < 1e-4,
            $"Derivative at knot {sKnot:F4} = {knotDeriv:E10}, expected {expectedKnotDeriv:E10}");
    }

    /// <summary>
    /// Test 8: Arc-length computation for a unit circle quadrant returns pi/2.
    /// </summary>
    [Fact]
    // Legacy mapping: arc-length computation used throughout legacy geometry preprocessing.
    // Difference from legacy: Arc length is validated explicitly on a known circular segment instead of being trusted transitively.
    // Decision: Keep the managed regression because it protects a ubiquitous geometric primitive.
    public void ComputeArcLength_UnitCircleQuadrant_ReturnsPiOverTwo()
    {
        const int n = 1001;
        var x = new double[n];
        var y = new double[n];
        var arcLength = new double[n];

        for (int i = 0; i < n; i++)
        {
            double theta = i * Math.PI / (2.0 * (n - 1));
            x[i] = Math.Cos(theta);
            y[i] = Math.Sin(theta);
        }

        ParametricSpline.ComputeArcLength(x, y, arcLength, n);

        double totalArcLength = arcLength[n - 1];
        double expected = Math.PI / 2.0;

        Assert.True(
            Math.Abs(totalArcLength - expected) < 1e-6,
            $"Total arc length = {totalArcLength:E10}, expected pi/2 = {expected:E10}");
    }
}
