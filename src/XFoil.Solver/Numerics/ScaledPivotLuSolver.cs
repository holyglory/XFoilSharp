// Legacy audit:
// Primary legacy source: f_xfoil/src/xsolve.f :: LUDCMP/BAKSUB
// Secondary legacy source: none
// Role in port: Managed LU decomposition and back-substitution helper used by parity-sensitive inviscid kernels and traced factorization work.
// Differences: The algorithm still follows the legacy scaled-pivot LU path, but the managed port exposes float/double entry points, generic shared cores, and structured trace hooks instead of relying on one implicit REAL/DOUBLE build and ad hoc debug output.
// Decision: Keep the managed shared solver because it preserves the LUDCMP/BAKSUB algorithm while making precision and tracing explicit.
using System.Numerics;
using System.Runtime.CompilerServices;
using XFoil.Solver.Diagnostics;

namespace XFoil.Solver.Numerics;

/// <summary>
/// Static utility class providing LU decomposition with Crout's method and scaled
/// partial pivoting, plus back-substitution. This is a direct port of XFoil's
/// LUDCMP and BAKSUB routines from xsolve.f in clean idiomatic C# with 0-based indexing.
/// </summary>
public static class ScaledPivotLuSolver
{
    /// <summary>
    /// In-place LU decomposition with Crout's method and scaled partial pivoting.
    /// The matrix is modified in-place to contain the L and U factors.
    /// The pivot indices record the row swap history.
    /// </summary>
    /// <remarks>
    /// The key difference from unscaled partial pivoting is that each row is
    /// scaled by 1/max(|row elements|) before pivot comparison. This prevents
    /// rows with large absolute values from dominating the pivot selection.
    /// </remarks>
    /// <param name="matrix">Square matrix to decompose. Modified in-place with L and U factors.</param>
    /// <param name="pivotIndices">Output: row swap history. Length >= size.</param>
    /// <param name="size">Dimension of the system.</param>
    // Legacy mapping: f_xfoil/src/xsolve.f :: LUDCMP.
    // Difference from legacy: The double-precision entry point is an explicit managed API over caller-owned matrices instead of shared work arrays.
    // Decision: Keep the wrapper because it is the natural public entry for the default precision path.
    public static void Decompose(double[,] matrix, int[] pivotIndices, int size, string? traceContext = null)
    {
        DecomposeCore(matrix, pivotIndices, size, traceContext, "Double");
    }

    /// <summary>
    /// Single-precision LU decomposition for parity-only legacy paths.
    /// Uses the same algorithm as the double overload.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xsolve.f :: LUDCMP legacy REAL path.
    // Difference from legacy: The single-precision path is exposed explicitly for parity work instead of being implied by source-level REAL declarations.
    // Decision: Keep the float overload because parity-sensitive kernels rely on it.
    public static void Decompose(float[,] matrix, int[] pivotIndices, int size, string? traceContext = null)
    {
        DecomposeCore(matrix, pivotIndices, size, traceContext, "Single");
    }

    /// <summary>
    /// Solves the system using the LU factors from <see cref="Decompose"/>.
    /// The right-hand side is modified in-place with the solution.
    /// </summary>
    /// <param name="luMatrix">LU-factored matrix from Decompose.</param>
    /// <param name="pivotIndices">Pivot indices from Decompose.</param>
    /// <param name="rhs">Right-hand side, replaced by the solution. Length >= size.</param>
    /// <param name="size">Dimension of the system.</param>
    // Legacy mapping: f_xfoil/src/xsolve.f :: BAKSUB.
    // Difference from legacy: The double-precision entry point is an explicit managed API instead of a procedural call over shared buffers.
    // Decision: Keep the wrapper because it is the natural public entry for the default precision path.
    public static void BackSubstitute(double[,] luMatrix, int[] pivotIndices, double[] rhs, int size, string? traceContext = null)
    {
        BackSubstituteCore(luMatrix, pivotIndices, rhs, size, traceContext, "Double");
    }

    /// <summary>
    /// Single-precision back-substitution for parity-only legacy paths.
    /// Uses the same algorithm as the double overload.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xsolve.f :: BAKSUB legacy REAL path.
    // Difference from legacy: The single-precision path is exposed explicitly for parity work instead of being hidden in type declarations.
    // Decision: Keep the float overload because parity-sensitive kernels rely on it.
    public static void BackSubstitute(float[,] luMatrix, int[] pivotIndices, float[] rhs, int size, string? traceContext = null)
    {
        BackSubstituteCore(luMatrix, pivotIndices, rhs, size, traceContext, "Single");
    }

    // Legacy mapping: f_xfoil/src/xsolve.f :: LUDCMP scaled pivoting and Crout factorization.
    // Difference from legacy: The managed core shares one generic control flow across float and double and makes the parity-sensitive fused subtract staging explicit.
    // Decision: Keep the shared core because it reduces drift between precision modes while preserving the legacy elimination order.
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static void DecomposeCore<T>(T[,] matrix, int[] pivotIndices, int size, string? traceContext, string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        var scaling = new T[size];
        for (int i = 0; i < size; i++)
        {
            T rowMax = T.Zero;
            for (int j = 0; j < size; j++)
            {
                T absVal = T.Abs(matrix[i, j]);
                if (absVal > rowMax)
                {
                    rowMax = absVal;
                }
            }

            scaling[i] = rowMax > T.Zero ? T.One / rowMax : T.One;
        }

        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < j; i++)
            {
                T sum = matrix[i, j];
                for (int k = 0; k < i; k++)
                {
                    T leftValue = matrix[i, k];
                    T rightValue = matrix[k, j];
                    T product = leftValue * rightValue;
                    T sumBefore = sum;
                    // LUDCMP is written as SUM = SUM - A(I,K)*A(K,J) with
                    // separate REAL product and subtraction staging. Preserve
                    // that two-rounding path in parity mode instead of
                    // contracting it to a fused update.
                    sum = LegacyPrecisionMath.SeparateMultiplySubtract(leftValue, rightValue, sum);
                    TraceDecomposeTerm(
                        traceContext,
                        precision,
                        "upper",
                        i,
                        j,
                        k,
                        leftValue,
                        rightValue,
                        product,
                        sumBefore,
                        sum);
                }

                matrix[i, j] = sum;
            }

            T maxScaled = T.Zero;
            int pivotRow = j;

            for (int i = j; i < size; i++)
            {
                T sum = matrix[i, j];
                for (int k = 0; k < j; k++)
                {
                    T leftValue = matrix[i, k];
                    T rightValue = matrix[k, j];
                    T product = leftValue * rightValue;
                    T sumBefore = sum;
                    sum = LegacyPrecisionMath.SeparateMultiplySubtract(leftValue, rightValue, sum);
                    TraceDecomposeTerm(
                        traceContext,
                        precision,
                        "lower",
                        i,
                        j,
                        k,
                        leftValue,
                        rightValue,
                        product,
                        sumBefore,
                        sum);
                    // GDB: trace LU elimination at diagonal j=159 (1-indexed: j=160)
                    if (i == j && j == size - 2 && typeof(T) == typeof(float)
                        && DebugFlags.SetBlHex
                        && (k == 119 || k == 129 || k == 139 || k == 149 || k >= 153))
                    {
                        Console.Error.WriteLine(
                            $"C_LU159 k={k,4} sum={BitConverter.SingleToInt32Bits(float.CreateChecked(sum)):X8}" +
                            $" L={BitConverter.SingleToInt32Bits(float.CreateChecked(leftValue)):X8}" +
                            $" R={BitConverter.SingleToInt32Bits(float.CreateChecked(rightValue)):X8}");
                    }
                }

                matrix[i, j] = sum;

                T scaledMagnitude = scaling[i] * T.Abs(sum);
                if (scaledMagnitude >= maxScaled)
                {
                    pivotRow = i;
                    maxScaled = scaledMagnitude;
                }
            }

            if (j != pivotRow)
            {
                for (int k = 0; k < size; k++)
                {
                    T temp = matrix[pivotRow, k];
                    matrix[pivotRow, k] = matrix[j, k];
                    matrix[j, k] = temp;
                }

                scaling[pivotRow] = scaling[j];
            }

            pivotIndices[j] = pivotRow;

            TracePivotSelection(traceContext, precision, j, pivotRow, matrix[j, j], maxScaled);

            if (j != size - 1)
            {
                T pivotInverse = T.One / matrix[j, j];
                for (int i = j + 1; i < size; i++)
                {
                    matrix[i, j] *= pivotInverse;
                }
            }
        }
    }

    // Legacy mapping: f_xfoil/src/xsolve.f :: BAKSUB forward swap/elimination and backward solve.
    // Difference from legacy: The managed core centralizes float/double behavior and structured tracing while keeping the same solve phases.
    // Decision: Keep the shared core because it preserves the legacy algorithm and centralizes instrumentation.
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static void BackSubstituteCore<T>(T[,] luMatrix, int[] pivotIndices, T[] rhs, int size, string? traceContext, string precision)
        where T : struct, IFloatingPointIeee754<T>
    {
        int ii = -1;

        for (int i = 0; i < size; i++)
        {
            int ll = pivotIndices[i];
            T sum = rhs[ll];
            T sumAfterSwap = sum;
            rhs[ll] = rhs[i];

            if (ii >= 0)
            {
                for (int j = ii; j < i; j++)
                {
                    T product = luMatrix[i, j] * rhs[j];
                    T sumBefore = sum;
                    sum = LegacyPrecisionMath.SeparateMultiplySubtract(luMatrix[i, j], rhs[j], sum);
                    TraceBackSubstituteTerm(
                        traceContext,
                        precision,
                        "forward",
                        i,
                        j,
                        ll,
                        ii,
                        luMatrix[i, j],
                        rhs[j],
                        product,
                        sumBefore,
                        sum);
                }
            }
            else if (sum != T.Zero)
            {
                ii = i;
            }

            rhs[i] = sum;
            TraceBackSubstituteRow(
                traceContext,
                precision,
                "forward",
                i,
                ll,
                ii,
                sumAfterSwap,
                sum,
                rhs[i],
                default,
                hasDivisor: false);
        }

        for (int i = size - 1; i >= 0; i--)
        {
            T sum = rhs[i];
            T sumBeforeElimination = sum;
            for (int j = i + 1; j < size; j++)
            {
                T product = luMatrix[i, j] * rhs[j];
                T sumBefore = sum;
                sum = LegacyPrecisionMath.SeparateMultiplySubtract(luMatrix[i, j], rhs[j], sum);
                TraceBackSubstituteTerm(
                    traceContext,
                    precision,
                    "backward",
                    i,
                    j,
                    pivotRow: pivotIndices[i],
                    ii,
                    luMatrix[i, j],
                    rhs[j],
                    product,
                    sumBefore,
                    sum);
            }

            T divisor = luMatrix[i, i];
            rhs[i] = sum / divisor;
            TraceBackSubstituteRow(
                traceContext,
                precision,
                "backward",
                i,
                pivotIndices[i],
                ii,
                sumBeforeElimination,
                sum,
                rhs[i],
                divisor,
                hasDivisor: true);
        }
    }

    // Legacy mapping: none; pivot tracing is managed-only diagnostics around LUDCMP.
    // Difference from legacy: Pivot choices are emitted as structured events instead of ad hoc debug output.
    // Decision: Keep the trace helper because parity debugging needs exact pivot provenance.
    private static void TracePivotSelection<T>(
        string? traceContext,
        string precision,
        int column,
        int pivotRow,
        T diagonal,
        T maxScaled)
    {
        if (traceContext is null || SolverTrace.Current is null)
        {
            return;
        }

        SolverTrace.Event(
            "lu_pivot",
            SolverTrace.ScopeName(typeof(ScaledPivotLuSolver), nameof(Decompose)),
            new
            {
                context = traceContext,
                column = column + 1,
                pivotRow = pivotRow + 1,
                diagonal,
                maxScaled,
                precision
            });
    }

    private static void TraceDecomposeTerm<T>(
        string? traceContext,
        string precision,
        string phase,
        int row,
        int column,
        int innerColumn,
        T leftValue,
        T rightValue,
        T product,
        T sumBefore,
        T sumAfter)
    {
        if (traceContext is null || SolverTrace.Current is null)
        {
            return;
        }

        SolverTrace.Event(
            "lu_decompose_term",
            SolverTrace.ScopeName(typeof(ScaledPivotLuSolver), nameof(Decompose)),
            new
            {
                context = traceContext,
                phase,
                row = row + 1,
                column = column + 1,
                innerColumn = innerColumn + 1,
                leftValue,
                rightValue,
                product,
                sumBefore,
                sumAfter,
                precision
            });
    }

    // Legacy mapping: none; row-state tracing is managed-only diagnostics around BAKSUB.
    // Difference from legacy: The post-swap and post-elimination row state is emitted as structured events instead of remaining implicit.
    // Decision: Keep the trace helper because parity debugging needs exact row provenance.
    private static void TraceBackSubstituteRow<T>(
        string? traceContext,
        string precision,
        string phase,
        int row,
        int pivotRow,
        int ii,
        T sumBeforeElimination,
        T sumAfterElimination,
        T solutionValue,
        T divisor,
        bool hasDivisor)
    {
        if (traceContext is null || SolverTrace.Current is null)
        {
            return;
        }

        // Parity work needs the exact row state after swaps and elimination so the
        // first differing solved gamma entry can be traced back to one row.
        SolverTrace.Event(
            "lu_back_substitute_row",
            SolverTrace.ScopeName(typeof(ScaledPivotLuSolver), nameof(BackSubstitute)),
            new
            {
                context = traceContext,
                phase,
                row = row + 1,
                pivotRow = pivotRow + 1,
                ii = ii >= 0 ? ii + 1 : 0,
                sumBeforeElimination,
                sumAfterElimination,
                divisor = hasDivisor ? divisor : default(T),
                solutionValue,
                precision
            });
    }

    // Legacy mapping: none; term-by-term tracing is managed-only diagnostics around BAKSUB.
    // Difference from legacy: Individual elimination products and sums are emitted as structured events instead of relying on manual debugging.
    // Decision: Keep the trace helper because it makes first-difference analysis practical.
    private static void TraceBackSubstituteTerm<T>(
        string? traceContext,
        string precision,
        string phase,
        int row,
        int column,
        int pivotRow,
        int ii,
        T matrixValue,
        T rhsValue,
        T product,
        T sumBefore,
        T sumAfter)
    {
        if (traceContext is null || SolverTrace.Current is null)
        {
            return;
        }

        SolverTrace.Event(
            "lu_back_substitute_term",
            SolverTrace.ScopeName(typeof(ScaledPivotLuSolver), nameof(BackSubstitute)),
            new
            {
                context = traceContext,
                phase,
                row = row + 1,
                column = column + 1,
                pivotRow = pivotRow + 1,
                ii = ii >= 0 ? ii + 1 : 0,
                matrixValue,
                rhsValue,
                product,
                sumBefore,
                sumAfter,
                precision
            });
    }
}
