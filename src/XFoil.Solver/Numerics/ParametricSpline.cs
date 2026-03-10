namespace XFoil.Solver.Numerics;

/// <summary>
/// Represents a boundary condition for parametric spline fitting.
/// Supports three modes: zero second derivative, zero third derivative, and specified first derivative.
/// </summary>
public readonly struct SplineBoundaryCondition
{
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
    public static void FitWithZeroSecondDerivativeBCs(
        double[] values, double[] derivatives, double[] parameters, int count)
    {
        FitWithBoundaryConditions(
            values, derivatives, parameters, count,
            SplineBoundaryCondition.ZeroSecondDerivative,
            SplineBoundaryCondition.ZeroSecondDerivative);
    }

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
    public static void FitWithBoundaryConditions(
        double[] values, double[] derivatives, double[] parameters, int count,
        SplineBoundaryCondition startBc, SplineBoundaryCondition endBc)
    {
        var lower = new double[count];
        var diagonal = new double[count];
        var upper = new double[count];

        // Interior equations -- same for all BC modes
        for (int i = 1; i < count - 1; i++)
        {
            double dsm = parameters[i] - parameters[i - 1];
            double dsp = parameters[i + 1] - parameters[i];
            lower[i] = dsp;
            diagonal[i] = 2.0 * (dsm + dsp);
            upper[i] = dsm;
            derivatives[i] = 3.0 * ((values[i + 1] - values[i]) * dsm / dsp
                                   + (values[i] - values[i - 1]) * dsp / dsm);
        }

        // Start boundary condition
        ApplyStartBc(startBc, values, derivatives, parameters, count, lower, diagonal, upper);

        // End boundary condition
        ApplyEndBc(endBc, values, derivatives, parameters, count, lower, diagonal, upper);

        // Special case: N=2 with both zero-third-derivative BCs
        // Fortran: falls back to zero-second-derivative at end
        if (count == 2
            && startBc.Mode == SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative
            && endBc.Mode == SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative)
        {
            lower[count - 1] = 1.0;
            diagonal[count - 1] = 2.0;
            derivatives[count - 1] = 3.0 * (values[count - 1] - values[count - 2])
                                    / (parameters[count - 1] - parameters[count - 2]);
        }

        // Solve the tridiagonal system
        TridiagonalSolver.Solve(lower, diagonal, upper, derivatives, count);
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
    public static void FitSegmented(
        double[] values, double[] derivatives, double[] parameters, int count)
    {
        int segStart = 0;

        for (int i = 1; i < count - 1; i++)
        {
            if (parameters[i] == parameters[i + 1])
            {
                // End of a segment
                int segCount = i - segStart + 1;
                FitSegment(values, derivatives, parameters, segStart, segCount);
                segStart = i + 1;
            }
        }

        // Final segment
        int lastSegCount = count - segStart;
        FitSegment(values, derivatives, parameters, segStart, lastSegCount);
    }

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
    public static double Evaluate(
        double s, double[] values, double[] derivatives, double[] parameters, int count)
    {
        int i = FindUpperIndex(s, parameters, count);
        int iLow = i - 1;

        double ds = parameters[i] - parameters[iLow];
        double t = (s - parameters[iLow]) / ds;
        double cx1 = ds * derivatives[iLow] - values[i] + values[iLow];
        double cx2 = ds * derivatives[i] - values[i] + values[iLow];

        return t * values[i] + (1.0 - t) * values[iLow]
             + (t - t * t) * ((1.0 - t) * cx1 - t * cx2);
    }

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
    public static double EvaluateDerivative(
        double s, double[] values, double[] derivatives, double[] parameters, int count)
    {
        int i = FindUpperIndex(s, parameters, count);
        int iLow = i - 1;

        double ds = parameters[i] - parameters[iLow];
        double t = (s - parameters[iLow]) / ds;
        double cx1 = ds * derivatives[iLow] - values[i] + values[iLow];
        double cx2 = ds * derivatives[i] - values[i] + values[iLow];

        double result = values[i] - values[iLow]
                      + (1.0 - 4.0 * t + 3.0 * t * t) * cx1
                      + t * (3.0 * t - 2.0) * cx2;
        return result / ds;
    }

    /// <summary>
    /// Computes cumulative arc length from (x,y) coordinates.
    /// arcLength[0] = 0, arcLength[i] = arcLength[i-1] + dist(i, i-1).
    /// This is the algorithm from XFoil's SCALC routine.
    /// </summary>
    /// <param name="x">X coordinate array. Length >= count.</param>
    /// <param name="y">Y coordinate array. Length >= count.</param>
    /// <param name="arcLength">Output: cumulative arc-length array. Length >= count.</param>
    /// <param name="count">Number of points.</param>
    public static void ComputeArcLength(double[] x, double[] y, double[] arcLength, int count)
    {
        arcLength[0] = 0.0;
        for (int i = 1; i < count; i++)
        {
            double dx = x[i] - x[i - 1];
            double dy = y[i] - y[i - 1];
            arcLength[i] = arcLength[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
    }

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

    private static void ApplyStartBc(
        SplineBoundaryCondition bc,
        double[] values, double[] derivatives, double[] parameters, int count,
        double[] lower, double[] diagonal, double[] upper)
    {
        switch (bc.Mode)
        {
            case SplineBoundaryCondition.BoundaryConditionMode.ZeroSecondDerivative:
                diagonal[0] = 2.0;
                upper[0] = 1.0;
                derivatives[0] = 3.0 * (values[1] - values[0]) / (parameters[1] - parameters[0]);
                break;

            case SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative:
                diagonal[0] = 1.0;
                upper[0] = 1.0;
                derivatives[0] = 2.0 * (values[1] - values[0]) / (parameters[1] - parameters[0]);
                break;

            case SplineBoundaryCondition.BoundaryConditionMode.SpecifiedFirstDerivative:
                diagonal[0] = 1.0;
                upper[0] = 0.0;
                derivatives[0] = bc.Value;
                break;
        }
    }

    private static void ApplyEndBc(
        SplineBoundaryCondition bc,
        double[] values, double[] derivatives, double[] parameters, int count,
        double[] lower, double[] diagonal, double[] upper)
    {
        int n = count - 1;
        switch (bc.Mode)
        {
            case SplineBoundaryCondition.BoundaryConditionMode.ZeroSecondDerivative:
                lower[n] = 1.0;
                diagonal[n] = 2.0;
                derivatives[n] = 3.0 * (values[n] - values[n - 1]) / (parameters[n] - parameters[n - 1]);
                break;

            case SplineBoundaryCondition.BoundaryConditionMode.ZeroThirdDerivative:
                lower[n] = 1.0;
                diagonal[n] = 1.0;
                derivatives[n] = 2.0 * (values[n] - values[n - 1]) / (parameters[n] - parameters[n - 1]);
                break;

            case SplineBoundaryCondition.BoundaryConditionMode.SpecifiedFirstDerivative:
                diagonal[n] = 1.0;
                lower[n] = 0.0;
                derivatives[n] = bc.Value;
                break;
        }
    }

    private static void FitSegment(
        double[] values, double[] derivatives, double[] parameters,
        int offset, int segCount)
    {
        if (segCount < 2)
        {
            return;
        }

        // Create temporary arrays for this segment
        var segValues = new double[segCount];
        var segDerivatives = new double[segCount];
        var segParameters = new double[segCount];

        Array.Copy(values, offset, segValues, 0, segCount);
        Array.Copy(parameters, offset, segParameters, 0, segCount);

        FitWithBoundaryConditions(
            segValues, segDerivatives, segParameters, segCount,
            SplineBoundaryCondition.ZeroThirdDerivative,
            SplineBoundaryCondition.ZeroThirdDerivative);

        Array.Copy(segDerivatives, 0, derivatives, offset, segCount);
    }

    /// <summary>
    /// Binary search to find the upper index of the interval containing s.
    /// Returns an index i such that parameters[i-1] &lt;= s &lt; parameters[i].
    /// </summary>
    private static int FindUpperIndex(double s, double[] parameters, int count)
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
}
