// Legacy audit:
// Primary legacy source: f_xfoil/src/spline.f :: TRISOL
// Secondary legacy source: none
// Role in port: Managed Thomas-algorithm solver shared by spline fitting and parity-sensitive single-precision geometry/kernel paths.
// Differences: The algorithm still follows TRISOL, but the managed port exposes float/double/generic entry points instead of relying on one implicit precision mode.
// Decision: Keep the managed shared solver because it preserves the TRISOL algorithm while making precision explicit.
using System.Numerics;

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
    // Legacy mapping: f_xfoil/src/spline.f :: TRISOL.
    // Difference from legacy: The double-precision entry point is an explicit managed API instead of a direct call over shared arrays.
    // Decision: Keep the wrapper because it is the natural public entry for the default precision path.
    public static void Solve(
        double[] lower,
        double[] diagonal,
        double[] upper,
        double[] rhs,
        int count,
        string? traceScope = null,
        string? routine = null)
        => SolveCore(lower, diagonal, upper, rhs, count);

    /// <summary>
    /// Single-precision overload used by parity-only legacy geometry and kernel paths.
    /// Keeps the algorithm identical to the double-precision implementation.
    /// </summary>
    // Legacy mapping: f_xfoil/src/spline.f :: TRISOL legacy REAL path.
    // Difference from legacy: The single-precision path is exposed explicitly for parity work instead of being implied by source-level REAL declarations.
    // Decision: Keep the float overload because parity-sensitive geometry and kernel paths rely on it.
    public static void Solve(
        float[] lower,
        float[] diagonal,
        float[] upper,
        float[] rhs,
        int count,
        string? traceScope = null,
        string? routine = null)
        => SolveCore(lower, diagonal, upper, rhs, count);

    /// <summary>
    /// Generic entry point shared by the parity precision modes so the Thomas algorithm
    /// remains identical across float and double implementations.
    /// </summary>
    // Legacy mapping: f_xfoil/src/spline.f :: TRISOL.
    // Difference from legacy: The managed port shares one generic control flow across precision modes so the algorithm does not drift between overloads.
    // Decision: Keep the generic entry because it reduces divergence between float and double implementations.
    public static void Solve<T>(
        T[] lower,
        T[] diagonal,
        T[] upper,
        T[] rhs,
        int count,
        string? traceScope = null,
        string? routine = null)
        where T : struct, IFloatingPointIeee754<T>
        => SolveCore(lower, diagonal, upper, rhs, count);

    // Legacy mapping: f_xfoil/src/spline.f :: TRISOL forward elimination and back substitution.
    // Difference from legacy: The core keeps the same Thomas-algorithm recurrence with explicit precision handling.
    // Decision: Keep the shared core because it preserves the legacy algorithm.
    private static void SolveCore<T>(
        T[] lower,
        T[] diagonal,
        T[] upper,
        T[] rhs,
        int count)
        where T : struct, IFloatingPointIeee754<T>
    {
        // Forward elimination -- matches TRISOL from spline.f
        for (int k = 1; k < count; k++)
        {
            int km = k - 1;
            upper[km] = upper[km] / diagonal[km];
            rhs[km] = rhs[km] / diagonal[km];
            // Fortran TRISOL uses mulss;subss (separate, no FMA contraction)
            // Both diagonal and RHS use separate multiply-subtract to match.
            diagonal[k] = LegacyPrecisionMath.SeparateMultiplySubtract(lower[k], upper[km], diagonal[k]);
            rhs[k] = LegacyPrecisionMath.SeparateMultiplySubtract(lower[k], rhs[km], rhs[k]);
        }

        // Last element
        rhs[count - 1] = rhs[count - 1] / diagonal[count - 1];

        // Back substitution
        for (int k = count - 2; k >= 0; k--)
        {
            rhs[k] = LegacyPrecisionMath.SeparateMultiplySubtract(upper[k], rhs[k + 1], rhs[k]);
        }
    }
}
