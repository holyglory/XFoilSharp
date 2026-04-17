// Legacy audit:
// Primary legacy source: f_xfoil/src/spline.f :: SPLIND/SEVAL
// Secondary legacy source: f_xfoil/src/spline.f :: SCALC
// Role in port: Managed cubic-spline helper for preprocessing and geometry interpolation.
// Differences: The helper follows the same natural-spline lineage but packages the fit and evaluation logic into an object with explicit parameter/value arrays and a local tridiagonal solve.
// Decision: Keep the managed spline helper because it preserves the spline workflow while fitting the .NET API surface.
using System;

namespace XFoil.Solver.Numerics;

public sealed class CubicSpline
{
    private readonly double[] slopes;

    // Legacy mapping: f_xfoil/src/spline.f :: SPLIND setup.
    // Difference from legacy: The constructor validates monotonic input and precomputes slopes once into an object-owned cache instead of writing into shared arrays.
    // Decision: Keep the managed constructor because explicit ownership and validation are clearer.
    public CubicSpline(IReadOnlyList<double> parameters, IReadOnlyList<double> values)
    {
        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (parameters.Count != values.Count)
        {
            throw new ArgumentException("The spline parameter and value arrays must have the same length.");
        }

        if (parameters.Count < 2)
        {
            throw new ArgumentException("At least two points are required to build a spline.");
        }

        for (var index = 1; index < parameters.Count; index++)
        {
            if (parameters[index] <= parameters[index - 1])
            {
                throw new ArgumentException("Spline parameters must be strictly increasing.");
            }
        }

        Parameters = parameters.ToArray();
        Values = values.ToArray();
        slopes = ComputeNaturalSlopes(Parameters, Values);
    }

    public IReadOnlyList<double> Parameters { get; }

    public IReadOnlyList<double> Values { get; }

    // Legacy mapping: f_xfoil/src/spline.f :: SEVAL.
    // Difference from legacy: Evaluation is exposed as an object method over cached arrays instead of a procedural call over raw workspace.
    // Decision: Keep the managed method because it is the natural API for the fitted spline.
    public double Evaluate(double parameter)
    {
        if (parameter <= Parameters[0])
        {
            return Values[0];
        }

        if (parameter >= Parameters[^1])
        {
            return Values[^1];
        }

        var upperIndex = FindUpperIndex(parameter);
        var lowerIndex = upperIndex - 1;
        var interval = Parameters[upperIndex] - Parameters[lowerIndex];
        var t = (parameter - Parameters[lowerIndex]) / interval;

        var cx1 = (interval * slopes[lowerIndex]) - Values[upperIndex] + Values[lowerIndex];
        var cx2 = (interval * slopes[upperIndex]) - Values[upperIndex] + Values[lowerIndex];

        return (t * Values[upperIndex])
             + ((1d - t) * Values[lowerIndex])
             + ((t - (t * t)) * (((1d - t) * cx1) - (t * cx2)));
    }

    // Legacy mapping: f_xfoil/src/spline.f :: segment search preceding SEVAL/SPLIND use.
    // Difference from legacy: Segment lookup is encapsulated in a local helper instead of being open-coded at call sites.
    // Decision: Keep the helper because it centralizes interval selection cleanly.
    private int FindUpperIndex(double parameter)
    {
        var lower = 0;
        var upper = Parameters.Count - 1;

        while (upper - lower > 1)
        {
            var middle = (upper + lower) / 2;
            if (parameter < Parameters[middle])
            {
                upper = middle;
            }
            else
            {
                lower = middle;
            }
        }

        return upper;
    }

    // Legacy mapping: f_xfoil/src/spline.f :: SPLIND natural-spline system assembly.
    // Difference from legacy: The managed helper assembles and solves the spline derivative system using ThreadStatic scratch buffers instead of per-instance allocations.
    // Decision: Keep the managed helper because it makes the spline fit self-contained.
    private static double[] ComputeNaturalSlopes(IReadOnlyList<double> parameters, IReadOnlyList<double> values)
    {
        var count = parameters.Count;
        var a = SolverBuffers.SplineCoefA(count);
        var b = SolverBuffers.SplineCoefB(count);
        var c = SolverBuffers.SplineCoefC(count);
        var d = SolverBuffers.SplineCoefD(count);

        for (var index = 1; index < count - 1; index++)
        {
            var deltaMinus = parameters[index] - parameters[index - 1];
            var deltaPlus = parameters[index + 1] - parameters[index];
            b[index] = deltaPlus;
            a[index] = 2d * (deltaMinus + deltaPlus);
            c[index] = deltaMinus;
            d[index] = 3d * (((values[index + 1] - values[index]) * deltaMinus / deltaPlus)
                           + ((values[index] - values[index - 1]) * deltaPlus / deltaMinus));
        }

        a[0] = 2d;
        c[0] = 1d;
        b[0] = 0d;
        d[0] = 3d * (values[1] - values[0]) / (parameters[1] - parameters[0]);

        b[count - 1] = 1d;
        a[count - 1] = 2d;
        c[count - 1] = 0d;
        d[count - 1] = 3d * (values[count - 1] - values[count - 2]) / (parameters[count - 1] - parameters[count - 2]);

        // Output buffer is owned by the spline instance; scratch stays in the
        // ThreadStatic pool.
        var slopesOut = new double[count];
        SolveTriDiagonalInPlace(a, b, c, d, slopesOut, count);
        return slopesOut;
    }

    // Legacy mapping: f_xfoil/src/spline.f :: tridiagonal solve used by SPLIND.
    // Difference from legacy: Thomas sweep runs in place on the supplied coefficient buffers (all pooled) and writes the result directly into the caller-owned output array.
    // Decision: Keep the local helper because it keeps the spline implementation self-contained.
    private static void SolveTriDiagonalInPlace(double[] a, double[] b, double[] c, double[] d, double[] result, int size)
    {
        // Reuse a (as diagonal) and d (as solution) in place. c is modified as
        // the upper diagonal and also reused in place. No allocations.
        for (var index = 1; index < size; index++)
        {
            var previous = index - 1;
            c[previous] /= a[previous];
            d[previous] /= a[previous];
            a[index] -= b[index] * c[previous];
            d[index] -= b[index] * d[previous];
        }

        d[size - 1] /= a[size - 1];
        for (var index = size - 2; index >= 0; index--)
        {
            d[index] -= c[index] * d[index + 1];
        }

        Array.Copy(d, result, size);
    }
}
