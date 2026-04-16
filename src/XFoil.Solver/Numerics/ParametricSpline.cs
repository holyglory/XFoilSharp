// Legacy audit:
// Primary legacy source: f_xfoil/src/spline.f :: SPLINE/SPLIND/SEGSPL/SEVAL/DEVAL/SINVRT/SCALC
// Secondary legacy source: none
// Role in port: Managed parametric-spline utility surface used by geometry preprocessing, inversion, and parity-sensitive spline evaluation.
// Differences: The algorithms remain aligned with the legacy spline.f family, but the managed port packages boundary conditions as a typed value, shares float/double/generic paths, and adds structured tracing instead of relying on implicit work arrays and ad hoc debugging.
// Decision: Keep the managed spline utility because it preserves the spline.f algorithms while making precision, boundary conditions, and tracing explicit.
using System.Numerics;
using XFoil.Solver.Diagnostics;

namespace XFoil.Solver.Numerics;

/// <summary>
/// Represents a boundary condition for parametric spline fitting.
/// Supports three modes: zero second derivative, zero third derivative, and specified first derivative.
/// </summary>
public readonly struct SplineBoundaryCondition
{
    // Legacy mapping: f_xfoil/src/spline.f :: SPLIND endpoint mode selection.
    // Difference from legacy: Boundary-condition mode and value are packaged as a typed managed value instead of paired integer/value arguments.
    // Decision: Keep the managed value type because it makes spline API calls clearer and safer.
    private SplineBoundaryCondition(BoundaryConditionMode mode, double value)
    {
        Mode = mode;
        Value = value;
    }

    internal enum BoundaryConditionMode
    {
        ZeroSecondDerivative,
        ZeroThirdDerivative,
        SpecifiedFirstDerivative
    }

    internal BoundaryConditionMode Mode { get; }

    internal double Value { get; }

    /// <summary>
    /// Zero second derivative boundary condition (natural spline end condition).
    /// </summary>
    public static SplineBoundaryCondition ZeroSecondDerivative =>
        new(BoundaryConditionMode.ZeroSecondDerivative, 0.0);

    /// <summary>
    /// Zero third derivative boundary condition.
    /// </summary>
    public static SplineBoundaryCondition ZeroThirdDerivative =>
        new(BoundaryConditionMode.ZeroThirdDerivative, 0.0);

    /// <summary>
    /// Specified first derivative boundary condition.
    /// </summary>
    /// <param name="value">The derivative value to impose.</param>
    // Legacy mapping: f_xfoil/src/spline.f :: SPLIND specified first-derivative endpoint condition.
    // Difference from legacy: The managed API constructs the boundary condition through a named factory instead of raw mode flags.
    // Decision: Keep the factory because it makes endpoint intent explicit.
    public static SplineBoundaryCondition SpecifiedDerivative(double value) =>
        new(BoundaryConditionMode.SpecifiedFirstDerivative, value);
}

/// <summary>
/// Static utility class providing all parametric spline operations needed by the
/// linear-vorticity geometry and solver pipeline. This is a direct port of XFoil's
/// spline.f routines (SPLINE, SPLIND, SEGSPL, SEVAL, DEVAL, SINVRT, SCALC) in
/// clean idiomatic C# with 0-based indexing.
/// </summary>
public static class ParametricSpline
{
    /// <summary>
    /// Fits a cubic spline with zero second derivative (natural) end conditions.
    /// Populates <paramref name="derivatives"/> in-place.
    /// This is the algorithm from XFoil's SPLINE routine.
    /// </summary>
    /// <param name="values">Dependent variable array. Length >= count.</param>
    /// <param name="derivatives">Output: dValue/dParameter at each point. Length >= count.</param>
    /// <param name="parameters">Independent variable (arc-length) array. Length >= count.</param>
    /// <param name="count">Number of data points.</param>
    // Legacy mapping: f_xfoil/src/spline.f :: SPLINE natural-spline fit.
    // Difference from legacy: The overload family exposes both float and double entry points while delegating to one shared boundary-condition core.
    // Decision: Keep the overload family because it preserves the natural-spline algorithm and avoids precision-path drift.
    public static void FitWithZeroSecondDerivativeBCs(
        double[] values, double[] derivatives, double[] parameters, int count)
        => FitWithBoundaryConditionsCore(
            values, derivatives, parameters, count,
            SplineBoundaryCondition.ZeroSecondDerivative,
            SplineBoundaryCondition.ZeroSecondDerivative);

    public static void FitWithZeroSecondDerivativeBCs(
        float[] values, float[] derivatives, float[] parameters, int count)
        => FitWithBoundaryConditionsCore(
            values, derivatives, parameters, count,
            SplineBoundaryCondition.ZeroSecondDerivative,
            SplineBoundaryCondition.ZeroSecondDerivative);

    /// <summary>
    /// Fits a cubic spline with configurable boundary conditions per endpoint.
    /// Populates <paramref name="derivatives"/> in-place.
    /// This is the algorithm from XFoil's SPLIND routine with its three BC modes.
    /// </summary>
    /// <param name="values">Dependent variable array. Length >= count.</param>
    /// <param name="derivatives">Output: dValue/dParameter at each point. Length >= count.</param>
    /// <param name="parameters">Independent variable (arc-length) array. Length >= count.</param>
    /// <param name="count">Number of data points.</param>
    /// <param name="startBc">Boundary condition at the start (index 0).</param>
    /// <param name="endBc">Boundary condition at the end (index count-1).</param>
    // Legacy mapping: f_xfoil/src/spline.f :: SPLIND configurable endpoint fit.
    // Difference from legacy: The overload family exposes explicit start/end boundary-condition values rather than mode integers and implicit array conventions.
    // Decision: Keep the overload family because it is the clearest managed surface for SPLIND behavior.
    public static void FitWithBoundaryConditions(
        double[] values, double[] derivatives, double[] parameters, int count,
        SplineBoundaryCondition startBc, SplineBoundaryCondition endBc)
        => FitWithBoundaryConditionsCore(values, derivatives, parameters, count, startBc, endBc);

    public static void FitWithBoundaryConditions(
        float[] values, float[] derivatives, float[] parameters, int count,
        SplineBoundaryCondition startBc, SplineBoundaryCondition endBc)
        => FitWithBoundaryConditionsCore(values, derivatives, parameters, count, startBc, endBc);

    // Legacy mapping: f_xfoil/src/spline.f :: SPLIND tridiagonal system assembly and solve.
    // Difference from legacy: The managed core shares one generic float/double path, emits structured trace rows, and delegates the solve to the shared tridiagonal helper.
    // Decision: Keep the shared core because it preserves SPLIND control flow while centralizing precision handling and instrumentation.
    private static void FitWithBoundaryConditionsCore<T>(
        T[] values, T[] derivatives, T[] parameters, int count,
        SplineBoundaryCondition startBc, SplineBoundaryCondition endBc)
        where T : struct, IFloatingPointIeee754<T>
    {
        const string traceScope = nameof(ParametricSpline);
        string precision = GetPrecisionLabel<T>();
        string startBoundary = DescribeBoundaryCondition(startBc);
        string endBoundary = DescribeBoundaryCondition(endBc);

        var lowerTyped = new T[count];
        var diagonalTyped = new T[count];
        var upperTyped = new T[count];

        T two = T.CreateChecked(2.0);
        T three = T.CreateChecked(3.0);

        for (int i = 1; i < count - 1; i++)
        {
            T dsm = parameters[i] - parameters[i - 1];
            T dsp = parameters[i + 1] - parameters[i];
            lowerTyped[i] = dsp;
            diagonalTyped[i] = two * (dsm + dsp);
            upperTyped[i] = dsm;
            derivatives[i] = three * ((values[i + 1] - values[i]) * dsm / dsp
                                    + (values[i] - values[i - 1]) * dsp / dsm);
        }

        // Start boundary condition
        ApplyStartBc(startBc, values, derivatives, parameters, count, lowerTyped, diagonalTyped, upperTyped);

        // End boundary condition
        ApplyEndBc(endBc, values, derivatives, parameters, count, lowerTyped, diagonalTyped, upperTyped);

        // Special case: N=2 with both zero-third-derivative BCs
        // Fortran: falls back to zero-second-derivative at end
        if (count == 2
            && startBc.Mode == SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative
            && endBc.Mode == SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative)
        {
            lowerTyped[count - 1] = T.One;
            diagonalTyped[count - 1] = two;
            derivatives[count - 1] = three * (values[count - 1] - values[count - 2])
                                    / (parameters[count - 1] - parameters[count - 2]);
        }

        for (int i = 0; i < count; i++)
        {
            TraceSplineSystemRow(
                traceScope,
                "SPLIND",
                i + 1,
                values[i],
                parameters[i],
                lowerTyped[i],
                diagonalTyped[i],
                upperTyped[i],
                derivatives[i],
                startBoundary,
                endBoundary,
                precision);
        }

        TridiagonalSolver.Solve(
            lowerTyped,
            diagonalTyped,
            upperTyped,
            derivatives,
            count,
            nameof(TridiagonalSolver),
            "TRISOL");

        for (int i = 0; i < count; i++)
        {
            TraceSplineSolutionNode(
                traceScope,
                "SPLIND",
                i + 1,
                values[i],
                parameters[i],
                derivatives[i],
                startBoundary,
                endBoundary,
                precision);
        }
    }

    /// <summary>
    /// Detects segment breaks where consecutive arc-length values are identical
    /// (indicating a corner), then fits each segment independently using
    /// zero-third-derivative BCs. This is the algorithm from XFoil's SEGSPL routine.
    /// </summary>
    /// <param name="values">Dependent variable array. Length >= count.</param>
    /// <param name="derivatives">Output: dValue/dParameter at each point. Length >= count.</param>
    /// <param name="parameters">Independent variable (arc-length) array. Length >= count.</param>
    /// <param name="count">Number of data points.</param>
    // Legacy mapping: f_xfoil/src/spline.f :: SEGSPL.
    // Difference from legacy: The overload family exposes segmented fitting directly for float, double, and generic paths while reusing the same segment walker.
    // Decision: Keep the overload family because it prevents duplicated segmented-spline logic across precision modes.
    public static void FitSegmented(
        double[] values, double[] derivatives, double[] parameters, int count)
        => FitSegmentedCore(values, derivatives, parameters, count);

    public static void FitSegmented(
        float[] values, float[] derivatives, float[] parameters, int count)
        => FitSegmentedCore(values, derivatives, parameters, count);

    public static void FitSegmented<T>(T[] values, T[] derivatives, T[] parameters, int count)
        where T : struct, IFloatingPointIeee754<T>
        => FitSegmentedCore(values, derivatives, parameters, count);

    /// <summary>
    /// Evaluates the spline at parameter <paramref name="s"/> using binary search
    /// for interval location, then cubic interpolation.
    /// This is the algorithm from XFoil's SEVAL routine.
    /// </summary>
    /// <param name="s">Parameter value at which to evaluate.</param>
    /// <param name="values">Dependent variable array. Length >= count.</param>
    /// <param name="derivatives">Spline derivative array (from fitting). Length >= count.</param>
    /// <param name="parameters">Independent variable array. Length >= count.</param>
    /// <param name="count">Number of data points.</param>
    /// <returns>The interpolated value at parameter s.</returns>
    // Legacy mapping: f_xfoil/src/spline.f :: SEVAL.
    // Difference from legacy: The overload family exposes typed evaluation entry points over caller-owned arrays instead of using shared spline workspace.
    // Decision: Keep the overload family because it is the natural managed API for spline evaluation.
    public static double Evaluate(
        double s, double[] values, double[] derivatives, double[] parameters, int count)
        => EvaluateCore(s, values, derivatives, parameters, count);

    public static float Evaluate(
        float s, float[] values, float[] derivatives, float[] parameters, int count)
        => EvaluateCore(s, values, derivatives, parameters, count);

    public static T Evaluate<T>(
        T s, T[] values, T[] derivatives, T[] parameters, int count)
        where T : struct, IFloatingPointIeee754<T>
        => EvaluateCore(s, values, derivatives, parameters, count);

    /// <summary>
    /// Evaluates the spline derivative at parameter <paramref name="s"/>.
    /// This is the algorithm from XFoil's DEVAL routine.
    /// </summary>
    /// <param name="s">Parameter value at which to evaluate the derivative.</param>
    /// <param name="values">Dependent variable array. Length >= count.</param>
    /// <param name="derivatives">Spline derivative array (from fitting). Length >= count.</param>
    /// <param name="parameters">Independent variable array. Length >= count.</param>
    /// <param name="count">Number of data points.</param>
    /// <returns>The spline derivative dValue/dParameter at parameter s.</returns>
    // Legacy mapping: f_xfoil/src/spline.f :: DEVAL.
    // Difference from legacy: The overload family exposes typed derivative evaluation entry points while reusing one shared core.
    // Decision: Keep the overload family because it avoids divergence between precision paths.
    public static double EvaluateDerivative(
        double s, double[] values, double[] derivatives, double[] parameters, int count)
        => EvaluateDerivativeCore(s, values, derivatives, parameters, count);

    public static float EvaluateDerivative(
        float s, float[] values, float[] derivatives, float[] parameters, int count)
        => EvaluateDerivativeCore(s, values, derivatives, parameters, count);

    public static T EvaluateDerivative<T>(
        T s, T[] values, T[] derivatives, T[] parameters, int count)
        where T : struct, IFloatingPointIeee754<T>
        => EvaluateDerivativeCore(s, values, derivatives, parameters, count);

    /// <summary>
    /// Computes cumulative arc length from (x,y) coordinates.
    /// arcLength[0] = 0, arcLength[i] = arcLength[i-1] + dist(i, i-1).
    /// This is the algorithm from XFoil's SCALC routine.
    /// </summary>
    /// <param name="x">X coordinate array. Length >= count.</param>
    /// <param name="y">Y coordinate array. Length >= count.</param>
    /// <param name="arcLength">Output: cumulative arc-length array. Length >= count.</param>
    /// <param name="count">Number of points.</param>
    // Legacy mapping: f_xfoil/src/spline.f :: SCALC.
    // Difference from legacy: The overload family exposes arc-length construction for float, double, and generic arrays rather than one implicit REAL path.
    // Decision: Keep the overload family because geometry code needs the same algorithm across precision modes.
    public static void ComputeArcLength(double[] x, double[] y, double[] arcLength, int count)
        => ComputeArcLengthCore(x, y, arcLength, count);

    public static void ComputeArcLength(float[] x, float[] y, float[] arcLength, int count)
        => ComputeArcLengthCore(x, y, arcLength, count);

    public static void ComputeArcLength<T>(T[] x, T[] y, T[] arcLength, int count)
        where T : struct, IFloatingPointIeee754<T>
        => ComputeArcLengthCore(x, y, arcLength, count);

    /// <summary>
    /// Given a target value, find the parameter s where the spline equals that value.
    /// Uses Newton iteration. This is the algorithm from XFoil's SINVRT routine.
    /// </summary>
    /// <param name="targetValue">The value to invert for.</param>
    /// <param name="values">Dependent variable array. Length >= count.</param>
    /// <param name="derivatives">Spline derivative array. Length >= count.</param>
    /// <param name="parameters">Independent variable array. Length >= count.</param>
    /// <param name="count">Number of data points.</param>
    /// <param name="initialGuess">Initial guess for the parameter s.</param>
    /// <returns>The parameter s such that spline(s) is approximately equal to targetValue.</returns>
    // Legacy mapping: f_xfoil/src/spline.f :: SINVRT.
    // Difference from legacy: Newton inversion is packaged as a standalone managed helper instead of a procedural call over global spline buffers.
    // Decision: Keep the helper because it makes spline inversion reusable and testable.
    public static double InvertSpline(
        double targetValue, double[] values, double[] derivatives,
        double[] parameters, int count, double initialGuess)
    {
        double si = initialGuess;
        double siSaved = si;
        double totalSpan = parameters[count - 1] - parameters[0];

        for (int iter = 0; iter < 10; iter++)
        {
            double residual = Evaluate(si, values, derivatives, parameters, count) - targetValue;
            double slope = EvaluateDerivative(si, values, derivatives, parameters, count);
            double ds = -residual / slope;
            si += ds;

            if (Math.Abs(ds / totalSpan) < 1.0e-5)
            {
                return si;
            }
        }

        // Inversion failed -- return initial guess
        return siSaved;
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SPLIND start-boundary assembly.
    // Difference from legacy: The managed core factors start-endpoint row assembly into a dedicated helper instead of inlining all boundary cases in one routine.
    // Decision: Keep the helper because it makes the boundary-condition logic easier to audit.
    private static void ApplyStartBc<T>(
        SplineBoundaryCondition bc,
        T[] values, T[] derivatives, T[] parameters, int count,
        T[] lower, T[] diagonal, T[] upper)
        where T : struct, IFloatingPointIeee754<T>
    {
        T two = T.CreateChecked(2.0);
        T three = T.CreateChecked(3.0);
        switch (bc.Mode)
        {
            case SplineBoundaryCondition.BoundaryConditionMode.ZeroSecondDerivative:
                diagonal[0] = two;
                upper[0] = T.One;
                derivatives[0] = three * (values[1] - values[0]) / (parameters[1] - parameters[0]);
                break;

            case SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative:
                diagonal[0] = T.One;
                upper[0] = T.One;
                derivatives[0] = two * (values[1] - values[0]) / (parameters[1] - parameters[0]);
                break;

            case SplineBoundaryCondition.BoundaryConditionMode.SpecifiedFirstDerivative:
                diagonal[0] = T.One;
                upper[0] = T.Zero;
                derivatives[0] = T.CreateChecked(bc.Value);
                break;
        }
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SPLIND end-boundary assembly.
    // Difference from legacy: The managed core factors end-endpoint row assembly into a dedicated helper instead of inlining all boundary cases in one routine.
    // Decision: Keep the helper because it makes the boundary-condition logic easier to audit.
    private static void ApplyEndBc<T>(
        SplineBoundaryCondition bc,
        T[] values, T[] derivatives, T[] parameters, int count,
        T[] lower, T[] diagonal, T[] upper)
        where T : struct, IFloatingPointIeee754<T>
    {
        int n = count - 1;
        T two = T.CreateChecked(2.0);
        T three = T.CreateChecked(3.0);
        switch (bc.Mode)
        {
            case SplineBoundaryCondition.BoundaryConditionMode.ZeroSecondDerivative:
                lower[n] = T.One;
                diagonal[n] = two;
                derivatives[n] = three * (values[n] - values[n - 1]) / (parameters[n] - parameters[n - 1]);
                break;

            case SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative:
                lower[n] = T.One;
                diagonal[n] = T.One;
                derivatives[n] = two * (values[n] - values[n - 1]) / (parameters[n] - parameters[n - 1]);
                break;

            case SplineBoundaryCondition.BoundaryConditionMode.SpecifiedFirstDerivative:
                diagonal[n] = T.One;
                lower[n] = T.Zero;
                derivatives[n] = T.CreateChecked(bc.Value);
                break;
        }
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SEGSPL per-segment SPLIND application.
    // Difference from legacy: The managed helper copies one segment into local arrays before fitting instead of indexing directly into shared global work arrays.
    // Decision: Keep the helper because it makes segmented fitting explicit and self-contained.
    private static void FitSegment<T>(
        T[] values, T[] derivatives, T[] parameters,
        int offset, int segCount)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (segCount < 2)
        {
            return;
        }

        var segValues = new T[segCount];
        var segDerivatives = new T[segCount];
        var segParameters = new T[segCount];

        Array.Copy(values, offset, segValues, 0, segCount);
        Array.Copy(parameters, offset, segParameters, 0, segCount);

        TraceSplineSegment(
            nameof(ParametricSpline),
            "SEGSPL",
            offset + 1,
            segCount,
            DescribeBoundaryCondition(SplineBoundaryCondition.ZeroThirdDerivative),
            DescribeBoundaryCondition(SplineBoundaryCondition.ZeroThirdDerivative),
            GetPrecisionLabel<T>());

        FitWithBoundaryConditionsCore(
            segValues, segDerivatives, segParameters, segCount,
            SplineBoundaryCondition.ZeroThirdDerivative,
            SplineBoundaryCondition.ZeroThirdDerivative);

        Array.Copy(segDerivatives, 0, derivatives, offset, segCount);
    }

    /// <summary>
    /// Binary search to find the upper index of the interval containing s.
    /// Returns an index i such that parameters[i-1] &lt;= s &lt; parameters[i].
    /// </summary>
    // Legacy mapping: f_xfoil/src/spline.f :: SEGSPL corner/duplicate-parameter segment walk.
    // Difference from legacy: The managed core isolates segment discovery into one helper and uses explicit zero-third-derivative boundary selection.
    // Decision: Keep the core because it preserves SEGSPL behavior while staying readable.
    private static void FitSegmentedCore<T>(T[] values, T[] derivatives, T[] parameters, int count)
        where T : struct, IFloatingPointIeee754<T>
    {
        int segStart = 0;

        for (int i = 1; i < count - 1; i++)
        {
            if (parameters[i] == parameters[i + 1])
            {
                int segCount = i - segStart + 1;
                FitSegment(values, derivatives, parameters, segStart, segCount);
                segStart = i + 1;
            }
        }

        FitSegment(values, derivatives, parameters, segStart, count - segStart);
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SEVAL interpolation polynomial.
    // Difference from legacy: The managed core spells out the contracted parity-sensitive polynomial terms and emits a structured trace of the interval evaluation.
    // Decision: Keep the shared core because it preserves SEVAL arithmetic while making parity-sensitive staging explicit.
    private static T EvaluateCore<T>(T s, T[] values, T[] derivatives, T[] parameters, int count)
        where T : struct, IFloatingPointIeee754<T>
    {
        string precision = GetPrecisionLabel<T>();
        int i = FindUpperIndex(s, parameters, count);
        int iLow = i - 1;

        T one = T.One;
        T ds = parameters[i] - parameters[iLow];
        T t = (s - parameters[iLow]) / ds;
        T valueLow = values[iLow];
        T valueHigh = values[i];
        T derivativeLow = derivatives[iLow];
        T derivativeHigh = derivatives[i];
        T oneMinusT = one - t;
        T cx1 = LegacyPrecisionMath.FusedMultiplyAdd(ds, derivativeLow, -valueHigh) + valueLow;
        // The standalone spline.f driver shows that the native parity build
        // contracts DS*XS(I) - XHIGH before the final +XLOW here.
        T cx2 = LegacyPrecisionMath.FusedMultiplyAdd(ds, derivativeHigh, -valueHigh) + valueLow;
        T linearLow = oneMinusT * valueLow;
        T linearHigh = t * valueHigh;
        // The standalone spline driver shows that the native SEVAL path lands on
        // the contracted T - T*T value in the parity-sensitive float branch.
        T cubicFactor = LegacyPrecisionMath.FusedMultiplySubtract(t, t, t);
        T cubicTerm1 = oneMinusT * cx1;
        T cubicTerm2 = t * cx2;
        T cubicDifference = cubicTerm1 - cubicTerm2;
        T cubic = cubicFactor * cubicDifference;
        // Keep the legacy SEVAL blend unfused. The traced XFoil reference rounds
        // the linear interpolation before adding the cubic correction here.
        T result = (t * valueHigh) + linearLow + cubic;
        TraceSplineEvaluation(
            nameof(ParametricSpline),
            "SEVAL",
            iLow + 1,
            i + 1,
            s,
            ds,
            t,
            valueLow,
            valueHigh,
            derivativeLow,
            derivativeHigh,
            cx1,
            cx2,
            valueHigh - valueLow,
            cubicFactor,
            oneMinusT,
            linearLow,
            linearHigh,
            cubicTerm1,
            cubicTerm2,
            cubicDifference,
            cubic,
            result,
            precision);
        return result;
    }

    // Legacy mapping: f_xfoil/src/spline.f :: DEVAL derivative polynomial.
    // Difference from legacy: The managed core spells out the parity-sensitive coefficient staging and emits a structured trace of the derivative evaluation.
    // Decision: Keep the shared core because it preserves DEVAL arithmetic while making parity-sensitive staging explicit.
    private static T EvaluateDerivativeCore<T>(T s, T[] values, T[] derivatives, T[] parameters, int count)
        where T : struct, IFloatingPointIeee754<T>
    {
        string precision = GetPrecisionLabel<T>();
        int i = FindUpperIndex(s, parameters, count);
        int iLow = i - 1;

        T one = T.One;
        T valueLow = values[iLow];
        T valueHigh = values[i];
        T derivativeLow = derivatives[iLow];
        T derivativeHigh = derivatives[i];
        T ds = parameters[i] - parameters[iLow];
        T t = (s - parameters[iLow]) / ds;
        T cx1 = LegacyPrecisionMath.FusedMultiplyAdd(ds, derivativeLow, -valueHigh) + valueLow;
        T cx2 = LegacyPrecisionMath.FusedMultiplyAdd(ds, derivativeHigh, -valueHigh) + valueLow;
        T four = T.CreateChecked(4.0);
        T three = T.CreateChecked(3.0);
        T two = T.CreateChecked(2.0);
        T delta = valueHigh - valueLow;
        // The standalone spline.f driver shows that DEVAL's FAC1 lands on the
        // native contracted path even though the source is written as
        // 1 - 4*T + 3*T*T. Preserve that parity-only staging here.
        T coeff1 = LegacyPrecisionMath.FusedMultiplyAdd(three * t, t, one - (four * t));
        T coeff2 = t * (three * t - two);
        T product1 = coeff1 * cx1;
        T product2 = coeff2 * cx2;
        T operandCombined = product1 + product2;
        T numerator = (delta + product1) + product2;
        T result = numerator / ds;
        TraceSplineEvaluation(
            nameof(ParametricSpline),
            "DEVAL",
            iLow + 1,
            i + 1,
            s,
            ds,
            t,
            valueLow,
            valueHigh,
            derivativeLow,
            derivativeHigh,
            cx1,
            cx2,
            delta,
            coeff1,
            coeff2,
            product1,
            product2,
            product1,
            product2,
            operandCombined,
            numerator,
            result,
            precision);
        return result;
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SCALC.
    // Difference from legacy: The managed core emits per-step arc-length traces and shares one implementation across float and double.
    // Decision: Keep the shared core because it preserves SCALC behavior while centralizing instrumentation.
    private static void ComputeArcLengthCore<T>(T[] x, T[] y, T[] arcLength, int count)
        where T : struct, IFloatingPointIeee754<T>
    {
        string precision = GetPrecisionLabel<T>();
        arcLength[0] = T.Zero;
        for (int i = 1; i < count; i++)
        {
            T dx = x[i] - x[i - 1];
            T dy = y[i] - y[i - 1];
            T segmentLength = ComputeDistance(dx, dy);
            arcLength[i] = arcLength[i - 1] + segmentLength;
            TraceArcLengthStep(
                nameof(ParametricSpline),
                "SCALC",
                i + 1,
                dx,
                dy,
                segmentLength,
                arcLength[i],
                precision);
        }
    }

    // Legacy mapping: f_xfoil/src/spline.f :: interval search preceding SEVAL/DEVAL.
    // Difference from legacy: Binary interval search is factored into a reusable helper instead of open-coded around each evaluation.
    // Decision: Keep the helper because it centralizes interval selection cleanly.
    private static int FindUpperIndex<T>(T s, T[] parameters, int count)
        where T : struct, IFloatingPointIeee754<T>
    {
        int iLow = 0;
        int i = count - 1;

        while (i - iLow > 1)
        {
            int mid = (i + iLow) / 2;
            if (s < parameters[mid])
            {
                i = mid;
            }
            else
            {
                iLow = mid;
            }
        }

        return i;
    }

    // Legacy mapping: none; boundary-condition naming is managed trace infrastructure.
    // Difference from legacy: Boundary modes are rendered as stable strings for diagnostics instead of remaining implicit numeric cases.
    // Decision: Keep the helper because traces need readable boundary labels.
    private static string DescribeBoundaryCondition(SplineBoundaryCondition bc)
    {
        return bc.Mode switch
        {
            SplineBoundaryCondition.BoundaryConditionMode.ZeroSecondDerivative => "ZeroSecondDerivative",
            SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative => "ZeroThirdDerivative",
            SplineBoundaryCondition.BoundaryConditionMode.SpecifiedFirstDerivative => "SpecifiedFirstDerivative",
            _ => "Unknown"
        };
    }

    // Legacy mapping: none; precision labeling is managed trace infrastructure.
    // Difference from legacy: The active arithmetic mode is surfaced explicitly instead of being implied by compiled type declarations.
    // Decision: Keep the helper because trace consumers need to distinguish float and double runs.
    private static string GetPrecisionLabel<T>()
        where T : struct, IFloatingPointIeee754<T>
        => typeof(T) == typeof(float) ? "Single" : "Double";

    // Legacy mapping: none; structured spline-evaluation tracing is managed-only diagnostics around SEVAL/DEVAL.
    // Difference from legacy: Evaluation state is emitted as structured events instead of ad hoc debugging.
    // Decision: Keep the trace helper because parity debugging needs exact evaluation provenance.
    private static void TraceSplineEvaluation<T>(
        string scope,
        string routine,
        int lowerIndex,
        int upperIndex,
        T parameter,
        T ds,
        T t,
        T valueLow,
        T valueHigh,
        T derivativeLow,
        T derivativeHigh,
        T cx1,
        T cx2,
        T delta,
        T factor1,
        T factor2,
        T product1,
        T product2,
        T operand1,
        T operand2,
        T operandCombined,
        T accumulator,
        T value,
        string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        SolverTrace.Event(
            "spline_eval",
            scope,
            new
            {
                routine,
                lowerIndex,
                upperIndex,
                parameter = double.CreateChecked(parameter),
                ds = double.CreateChecked(ds),
                t = double.CreateChecked(t),
                valueLow = double.CreateChecked(valueLow),
                valueHigh = double.CreateChecked(valueHigh),
                derivativeLow = double.CreateChecked(derivativeLow),
                derivativeHigh = double.CreateChecked(derivativeHigh),
                cx1 = double.CreateChecked(cx1),
                cx2 = double.CreateChecked(cx2),
                delta = double.CreateChecked(delta),
                factor1 = double.CreateChecked(factor1),
                factor2 = double.CreateChecked(factor2),
                product1 = double.CreateChecked(product1),
                product2 = double.CreateChecked(product2),
                operand1 = double.CreateChecked(operand1),
                operand2 = double.CreateChecked(operand2),
                operandCombined = double.CreateChecked(operandCombined),
                accumulator = double.CreateChecked(accumulator),
                value = double.CreateChecked(value),
                precision
            });
    }

    // Legacy mapping: none; structured segment tracing is managed-only diagnostics around SEGSPL.
    // Difference from legacy: Segment boundaries and endpoint modes are emitted as structured events instead of remaining implicit.
    // Decision: Keep the trace helper because segmented-fit debugging needs explicit segment provenance.
    private static void TraceSplineSegment(
        string scope,
        string routine,
        int segmentStart,
        int segmentCount,
        string startBoundaryCondition,
        string endBoundaryCondition,
        string precision)
    {
        SolverTrace.Event(
            "spline_segment",
            scope,
            new
            {
                routine,
                segmentStart,
                segmentCount,
                startBoundaryCondition,
                endBoundaryCondition,
                precision
            });
    }

    // Legacy mapping: none; structured row tracing is managed-only diagnostics around SPLIND.
    // Difference from legacy: The assembled spline system rows are emitted as structured events instead of ad hoc debug output.
    // Decision: Keep the trace helper because parity debugging needs exact row assembly provenance.
    private static void TraceSplineSystemRow<T>(
        string scope,
        string routine,
        int index,
        T value,
        T parameter,
        T lower,
        T diagonal,
        T upper,
        T rhs,
        string startBoundaryCondition,
        string endBoundaryCondition,
        string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        SolverTrace.Event(
            "spline_system_row",
            scope,
            new
            {
                routine,
                index,
                value = double.CreateChecked(value),
                parameter = double.CreateChecked(parameter),
                lower = double.CreateChecked(lower),
                diagonal = double.CreateChecked(diagonal),
                upper = double.CreateChecked(upper),
                rhs = double.CreateChecked(rhs),
                startBoundaryCondition,
                endBoundaryCondition,
                precision
            });
    }

    // Legacy mapping: none; structured solution tracing is managed-only diagnostics around SPLIND/TRISOL.
    // Difference from legacy: Solved derivative nodes are emitted as structured events instead of remaining transient in arrays.
    // Decision: Keep the trace helper because parity debugging needs exact solved-node provenance.
    private static void TraceSplineSolutionNode<T>(
        string scope,
        string routine,
        int index,
        T value,
        T parameter,
        T derivative,
        string startBoundaryCondition,
        string endBoundaryCondition,
        string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        SolverTrace.Event(
            "spline_solution_node",
            scope,
            new
            {
                routine,
                index,
                value = double.CreateChecked(value),
                parameter = double.CreateChecked(parameter),
                derivative = double.CreateChecked(derivative),
                startBoundaryCondition,
                endBoundaryCondition,
                precision
            });
    }

    // Legacy mapping: none; structured arc-length tracing is managed-only diagnostics around SCALC.
    // Difference from legacy: Each distance-accumulation step is emitted as a structured event instead of ad hoc debugging.
    // Decision: Keep the trace helper because parity debugging needs exact cumulative-length provenance.
    private static void TraceArcLengthStep<T>(
        string scope,
        string routine,
        int index,
        T dx,
        T dy,
        T segmentLength,
        T cumulative,
        string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        SolverTrace.Event(
            "arc_length_step",
            scope,
            new
            {
                routine,
                index,
                dx = double.CreateChecked(dx),
                dy = double.CreateChecked(dy),
                segmentLength = double.CreateChecked(segmentLength),
                cumulative = double.CreateChecked(cumulative),
                precision
            });
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SCALC point-to-point distance calculation.
    // Difference from legacy: Distance evaluation is factored into a reusable generic helper instead of being inlined at each arc-length loop.
    // Decision: Keep the helper because it centralizes the parity-sensitive distance formula.
    internal static T ComputeDistance<T>(T dx, T dy)
        where T : struct, IFloatingPointIeee754<T>
    {
        T distanceSquared = (dx * dx) + (dy * dy);
        return T.Sqrt(distanceSquared);
    }
}
