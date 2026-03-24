// Legacy audit:
// Primary legacy source: f_xfoil/src/spline.f :: TRISOL
// Secondary legacy source: none
// Role in port: Managed Thomas-algorithm solver shared by spline fitting and parity-sensitive single-precision geometry/kernel paths.
// Differences: The algorithm still follows TRISOL, but the managed port exposes float/double/generic entry points and standardized trace events instead of relying on one implicit precision mode and unstructured runtime state.
// Decision: Keep the managed shared solver because it preserves the TRISOL algorithm while making precision and tracing explicit.
using System.Numerics;
using XFoil.Solver.Diagnostics;

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
        => SolveCore(lower, diagonal, upper, rhs, count, traceScope, routine);

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
        => SolveCore(lower, diagonal, upper, rhs, count, traceScope, routine);

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
        => SolveCore(lower, diagonal, upper, rhs, count, traceScope, routine);

    // Legacy mapping: f_xfoil/src/spline.f :: TRISOL forward elimination and back substitution.
    // Difference from legacy: The core makes precision labeling and trace emission explicit while keeping the same Thomas-algorithm recurrence.
    // Decision: Keep the shared core because it preserves the legacy algorithm and centralizes instrumentation.
    private static void SolveCore<T>(
        T[] lower,
        T[] diagonal,
        T[] upper,
        T[] rhs,
        int count,
        string? traceScope,
        string? routine)
        where T : struct, IFloatingPointIeee754<T>
    {
        string scope = string.IsNullOrWhiteSpace(traceScope)
            ? SolverTrace.ScopeName(typeof(TridiagonalSolver))
            : traceScope;
        string traceRoutine = string.IsNullOrWhiteSpace(routine) ? "TRISOL" : routine;
        string precision = GetPrecisionLabel<T>();

        // Forward elimination -- matches TRISOL from spline.f
        for (int k = 1; k < count; k++)
        {
            int km = k - 1;
            T pivot = diagonal[km];
            T lowerValue = lower[k];
            T upperBefore = upper[km];
            T rhsBeforePivot = rhs[km];
            T diagonalBefore = diagonal[k];
            T rhsBefore = rhs[k];

            upper[km] = upper[km] / diagonal[km];
            rhs[km] = rhs[km] / diagonal[km];
            diagonal[k] = T.FusedMultiplyAdd(-lower[k], upper[km], diagonal[k]);
            rhs[k] = T.FusedMultiplyAdd(-lower[k], rhs[km], rhs[k]);

            TraceForwardElimination(
                scope,
                traceRoutine,
                k + 1,
                pivot,
                lowerValue,
                upperBefore,
                rhsBeforePivot,
                upper[km],
                rhs[km],
                diagonalBefore,
                diagonal[k],
                rhsBefore,
                rhs[k],
                precision);
        }

        // Last element
        T lastPivot = diagonal[count - 1];
        T lastRhsBefore = rhs[count - 1];
        rhs[count - 1] = rhs[count - 1] / diagonal[count - 1];
        TraceLastPivot(scope, traceRoutine, count, lastPivot, lastRhsBefore, rhs[count - 1], precision);

        // Back substitution
        for (int k = count - 2; k >= 0; k--)
        {
            T rhsBefore = rhs[k];
            T upperValue = upper[k];
            T nextValue = rhs[k + 1];
            rhs[k] = T.FusedMultiplyAdd(-upper[k], rhs[k + 1], rhs[k]);
            TraceBackSubstitution(scope, traceRoutine, k + 1, upperValue, nextValue, rhsBefore, rhs[k], precision);
        }
    }

    // Legacy mapping: none; precision labeling is managed trace infrastructure.
    // Difference from legacy: The active arithmetic mode is surfaced as a trace label instead of being implicit in compiled type declarations.
    // Decision: Keep the helper because trace consumers need to distinguish float and double runs.
    private static string GetPrecisionLabel<T>()
        where T : struct, IFloatingPointIeee754<T>
        => typeof(T) == typeof(float) ? "Single" : "Double";

    // Legacy mapping: none; structured forward-elimination tracing is managed-only diagnostics around TRISOL.
    // Difference from legacy: The elimination state is emitted as structured JSON-ready events instead of ad hoc debug writes.
    // Decision: Keep the trace helper because it makes parity debugging observable without changing solver logic.
    private static void TraceForwardElimination<T>(
        string scope,
        string routine,
        int index,
        T pivot,
        T lower,
        T upperBefore,
        T rhsBeforePivot,
        T upperAfter,
        T rhsAfterPivot,
        T diagonalBefore,
        T diagonalAfter,
        T rhsBefore,
        T rhsAfter,
        string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        SolverTrace.Event(
            "tridiagonal_forward",
            scope,
            new
            {
                routine,
                index,
                pivot = double.CreateChecked(pivot),
                lower = double.CreateChecked(lower),
                upperBefore = double.CreateChecked(upperBefore),
                rhsBeforePivot = double.CreateChecked(rhsBeforePivot),
                upperAfter = double.CreateChecked(upperAfter),
                rhsAfterPivot = double.CreateChecked(rhsAfterPivot),
                diagonalBefore = double.CreateChecked(diagonalBefore),
                diagonalAfter = double.CreateChecked(diagonalAfter),
                rhsBefore = double.CreateChecked(rhsBefore),
                rhsAfter = double.CreateChecked(rhsAfter),
                precision
            });
    }

    // Legacy mapping: none; structured last-pivot tracing is managed-only diagnostics.
    // Difference from legacy: The final normalization step is emitted as a structured event instead of remaining implicit.
    // Decision: Keep the trace helper because it improves parity diagnostics.
    private static void TraceLastPivot<T>(
        string scope,
        string routine,
        int index,
        T pivot,
        T rhsBefore,
        T rhsAfter,
        string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        SolverTrace.Event(
            "tridiagonal_last_pivot",
            scope,
            new
            {
                routine,
                index,
                pivot = double.CreateChecked(pivot),
                rhsBefore = double.CreateChecked(rhsBefore),
                rhsAfter = double.CreateChecked(rhsAfter),
                precision
            });
    }

    // Legacy mapping: none; structured back-substitution tracing is managed-only diagnostics.
    // Difference from legacy: Backward recurrence terms are emitted as structured events instead of relying on ad hoc debugging.
    // Decision: Keep the trace helper because it improves observability without altering the algorithm.
    private static void TraceBackSubstitution<T>(
        string scope,
        string routine,
        int index,
        T upper,
        T nextValue,
        T rhsBefore,
        T rhsAfter,
        string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        SolverTrace.Event(
            "tridiagonal_back",
            scope,
            new
            {
                routine,
                index,
                upper = double.CreateChecked(upper),
                nextValue = double.CreateChecked(nextValue),
                rhsBefore = double.CreateChecked(rhsBefore),
                rhsAfter = double.CreateChecked(rhsAfter),
                precision
            });
    }
}
