namespace XFoil.Solver.Numerics;

public sealed class CubicSpline
{
    private readonly double[] slopes;

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

    private static double[] ComputeNaturalSlopes(IReadOnlyList<double> parameters, IReadOnlyList<double> values)
    {
        var count = parameters.Count;
        var a = new double[count];
        var b = new double[count];
        var c = new double[count];
        var d = new double[count];

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
        d[0] = 3d * (values[1] - values[0]) / (parameters[1] - parameters[0]);

        b[count - 1] = 1d;
        a[count - 1] = 2d;
        d[count - 1] = 3d * (values[count - 1] - values[count - 2]) / (parameters[count - 1] - parameters[count - 2]);

        return SolveTriDiagonal(a, b, c, d);
    }

    private static double[] SolveTriDiagonal(double[] a, double[] b, double[] c, double[] d)
    {
        var size = a.Length;
        var diagonal = (double[])a.Clone();
        var upper = (double[])c.Clone();
        var solution = (double[])d.Clone();

        for (var index = 1; index < size; index++)
        {
            var previous = index - 1;
            upper[previous] /= diagonal[previous];
            solution[previous] /= diagonal[previous];
            diagonal[index] -= b[index] * upper[previous];
            solution[index] -= b[index] * solution[previous];
        }

        solution[size - 1] /= diagonal[size - 1];
        for (var index = size - 2; index >= 0; index--)
        {
            solution[index] -= upper[index] * solution[index + 1];
        }

        return solution;
    }
}
