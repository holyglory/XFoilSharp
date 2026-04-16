// Legacy audit:
// Primary legacy source: f_xfoil/src/xsolve.f :: GAUSS
// Secondary legacy source: none
// Role in port: Managed dense Gaussian-elimination solver used for small auxiliary systems and parity-only seed solves.
// Differences: The algorithm follows the GAUSS lineage, but the managed port exposes shared generic float/double control flow, explicit validation, and parity-specific multiply-subtract staging through `LegacyPrecisionMath`.
// Decision: Keep the managed generic solver because it preserves the GAUSS algorithm while making precision control explicit.
using System.Numerics;
using XFoil.Solver.Diagnostics;

namespace XFoil.Solver.Numerics;

public sealed class DenseLinearSystemSolver
{
    // Legacy mapping: f_xfoil/src/xsolve.f :: GAUSS.
    // Difference from legacy: The managed API accepts a dense matrix and right-hand side directly instead of reading from shared work arrays.
    // Decision: Keep the wrapper because it is the clean public entry point for the double-precision path.
    public double[] Solve(double[,] matrix, double[] rightHandSide)
    {
        return SolveCore(matrix, rightHandSide);
    }

    /// <summary>
    /// Single-precision solve for parity-only legacy seed paths.
    /// The algorithm is shared with the double overload so the float/double
    /// variants differ only by arithmetic precision, not by control flow.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xsolve.f :: GAUSS legacy REAL path.
    // Difference from legacy: The managed solver exposes the parity-only single-precision path explicitly instead of relying on source-level REAL declarations.
    // Decision: Keep the float overload because parity work needs the exact same control flow at lower precision.
    public float[] Solve(float[,] matrix, float[] rightHandSide)
    {
        return SolveCore(matrix, rightHandSide);
    }

    // Legacy mapping: f_xfoil/src/xsolve.f :: GAUSS forward elimination and back substitution.
    // Difference from legacy: The core is generic across float and double and makes the parity-sensitive fused versus separated multiply-subtract choices explicit.
    // Decision: Keep the shared core because it reduces divergence between the managed precision variants.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    private static T[] SolveCore<T>(T[,] matrix, T[] rightHandSide)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (matrix is null)
        {
            throw new ArgumentNullException(nameof(matrix));
        }

        if (rightHandSide is null)
        {
            throw new ArgumentNullException(nameof(rightHandSide));
        }

        var rowCount = matrix.GetLength(0);
        var columnCount = matrix.GetLength(1);
        if (rowCount != columnCount)
        {
            throw new ArgumentException("The coefficient matrix must be square.", nameof(matrix));
        }

        if (rightHandSide.Length != rowCount)
        {
            throw new ArgumentException("The right-hand side length must match the matrix size.", nameof(rightHandSide));
        }

        var a = new T[rowCount, columnCount];
        var b = new T[rowCount];
        for (var row = 0; row < rowCount; row++)
        {
            b[row] = rightHandSide[row];
            for (var column = 0; column < columnCount; column++)
            {
                a[row, column] = matrix[row, column];
            }
        }

        if (rowCount == 0)
        {
            return Array.Empty<T>();
        }

        bool traceGauss = typeof(T) == typeof(float) && rowCount == 4 && columnCount == 4;
        // Removed verbose per-call GAUSS trace; use C_MRCHDU_M3 instead
        if (traceGauss)
        {
            TraceGaussState(a, b, "initial", pivotIndex: 0, rowIndex: 0);
        }

        // This is a direct port of XFoil's GAUSS routine from xsolve.f.
        // The parity seed path needs the same pivot order as classic XFoil.
        // Both forward elimination and back substitution follow the legacy
        // single-precision multiply-subtract staging proven by the standalone
        // GAUSS micro-driver.
        for (var pivotIndex = 0; pivotIndex < rowCount - 1; pivotIndex++)
        {
            var pivotRow = pivotIndex;
            for (var row = pivotIndex + 1; row < rowCount; row++)
            {
                if (T.Abs(a[row, pivotIndex]) > T.Abs(a[pivotRow, pivotIndex]))
                {
                    pivotRow = row;
                }
            }

            T pivotValue = a[pivotRow, pivotIndex];
            if (T.Abs(pivotValue) < T.CreateChecked(1e-12))
            {
                throw new InvalidOperationException("The linear system is singular or ill-conditioned.");
            }

            T pivotInverse = T.One / pivotValue;
            a[pivotRow, pivotIndex] = a[pivotIndex, pivotIndex];

            for (var column = pivotIndex + 1; column < columnCount; column++)
            {
                T temp = a[pivotRow, column] * pivotInverse;
                a[pivotRow, column] = a[pivotIndex, column];
                a[pivotIndex, column] = temp;
            }

            {
                T temp = b[pivotRow] * pivotInverse;
                b[pivotRow] = b[pivotIndex];
                b[pivotIndex] = temp;
            }

            if (traceGauss)
            {
                TraceGaussState(a, b, "normalized", pivotIndex + 1, pivotRow + 1);
            }

            for (var row = pivotIndex + 1; row < rowCount; row++)
            {
                T factor = a[row, pivotIndex];
                for (var column = pivotIndex + 1; column < columnCount; column++)
                {
                    // Fortran GAUSS: Z(K,L) = Z(K,L) - ZTMP*Z(NP,L)
                    // Must use separate multiply-subtract, NOT FMA negate-multiply-add
                    a[row, column] = LegacyPrecisionMath.SeparateMultiplySubtract(
                        factor,
                        a[pivotIndex, column],
                        a[row, column]);
                }

                b[row] = LegacyPrecisionMath.SeparateMultiplySubtract(factor, b[pivotIndex], b[row]);

                if (traceGauss)
                {
                    TraceGaussState(a, b, "eliminate", pivotIndex + 1, row + 1);
                }
            }
        }

        b[rowCount - 1] /= a[rowCount - 1, rowCount - 1];
        if (traceGauss)
        {
            TraceGaussState(a, b, "last", rowCount, rowCount);
        }

        for (var row = rowCount - 2; row >= 0; row--)
        {
            for (var column = row + 1; column < columnCount; column++)
            {
                // Fortran GAUSS: R(NP,L) = R(NP,L) - Z(NP,K)*R(K,L)
                b[row] = LegacyPrecisionMath.SeparateMultiplySubtract(a[row, column], b[column], b[row]);
            }

            if (traceGauss)
            {
                TraceGaussState(a, b, "backsub", row + 1, 0);
            }
        }

        return b;
    }

    // Legacy mapping: tools/fortran-debug/xsolve_debug.f :: GAUSS trace instrumentation.
    // Difference from legacy: The managed solver mirrors the debug-only 4x4 snapshot schema through the ambient JSON trace instead of direct WRITE statements.
    // Decision: Keep the narrow parity-only hook because it exposes the first solver divergence without changing production arithmetic.
    private static void TraceGaussState<T>(T[,] matrix, T[] rhs, string phase, int pivotIndex, int rowIndex)
        where T : struct, IFloatingPointIeee754<T>
    {
        SolverTrace.Event(
            "gauss_state",
            "GAUSS",
            new
            {
                phase,
                pivotIndex,
                rowIndex,
                row11 = float.CreateChecked(matrix[0, 0]),
                row12 = float.CreateChecked(matrix[0, 1]),
                row13 = float.CreateChecked(matrix[0, 2]),
                row14 = float.CreateChecked(matrix[0, 3]),
                row21 = float.CreateChecked(matrix[1, 0]),
                row22 = float.CreateChecked(matrix[1, 1]),
                row23 = float.CreateChecked(matrix[1, 2]),
                row24 = float.CreateChecked(matrix[1, 3]),
                row31 = float.CreateChecked(matrix[2, 0]),
                row32 = float.CreateChecked(matrix[2, 1]),
                row33 = float.CreateChecked(matrix[2, 2]),
                row34 = float.CreateChecked(matrix[2, 3]),
                row41 = float.CreateChecked(matrix[3, 0]),
                row42 = float.CreateChecked(matrix[3, 1]),
                row43 = float.CreateChecked(matrix[3, 2]),
                row44 = float.CreateChecked(matrix[3, 3]),
                rhs1 = float.CreateChecked(rhs[0]),
                rhs2 = float.CreateChecked(rhs[1]),
                rhs3 = float.CreateChecked(rhs[2]),
                rhs4 = float.CreateChecked(rhs[3])
            });
    }
}
