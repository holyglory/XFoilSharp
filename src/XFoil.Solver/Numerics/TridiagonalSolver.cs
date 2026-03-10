namespace XFoil.Solver.Numerics;

/// <summary>
/// Static utility class implementing the Thomas algorithm for tridiagonal systems.
/// This is a direct port of XFoil's TRISOL routine from spline.f.
/// </summary>
public static class TridiagonalSolver
{
    /// <summary>
    /// Solves a tridiagonal system using the Thomas algorithm.
    /// The right-hand side array is replaced with the solution in-place.
    /// The diagonal and upper arrays are destroyed during the solve.
    /// </summary>
    /// <remarks>
    /// The system has the form:
    /// <code>
    ///   diagonal[0]  upper[0]                          | rhs[0]
    ///   lower[1]     diagonal[1]  upper[1]             | rhs[1]
    ///                lower[2]     diagonal[2]  ...     | ...
    ///                             ...          upper   | ...
    ///                                  lower   diagonal | rhs[count-1]
    /// </code>
    /// </remarks>
    /// <param name="lower">Sub-diagonal elements (lower[0] is unused). Length >= count.</param>
    /// <param name="diagonal">Main diagonal elements. Length >= count. Destroyed on output.</param>
    /// <param name="upper">Super-diagonal elements (upper[count-1] is unused). Length >= count. Destroyed on output.</param>
    /// <param name="rhs">Right-hand side, replaced by the solution. Length >= count.</param>
    /// <param name="count">Number of equations.</param>
    public static void Solve(double[] lower, double[] diagonal, double[] upper, double[] rhs, int count)
    {
        // Forward elimination -- matches TRISOL from spline.f
        for (int k = 1; k < count; k++)
        {
            int km = k - 1;
            upper[km] = upper[km] / diagonal[km];
            rhs[km] = rhs[km] / diagonal[km];
            diagonal[k] = diagonal[k] - lower[k] * upper[km];
            rhs[k] = rhs[k] - lower[k] * rhs[km];
        }

        // Last element
        rhs[count - 1] = rhs[count - 1] / diagonal[count - 1];

        // Back substitution
        for (int k = count - 2; k >= 0; k--)
        {
            rhs[k] = rhs[k] - upper[k] * rhs[k + 1];
        }
    }
}
