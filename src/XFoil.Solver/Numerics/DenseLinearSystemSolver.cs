using System.Numerics;

namespace XFoil.Solver.Numerics;

public sealed class DenseLinearSystemSolver
{
    public static readonly DenseLinearSystemSolver Shared = new();

    public double[] Solve(double[,] matrix, double[] rightHandSide)
    {
        // Route through pooled workspace so the allocating Solve overload does
        // not emit per-call heap objects. The returned array is the pooled
        // ThreadStatic scratch vector; callers must consume it before the
        // next Solve call on the same thread (matches the prior semantics —
        // the caller always copied/returned the result immediately).
        int n = matrix.GetLength(0);
        var workspace = SolverBuffers.DenseScratchMatrixDouble(n, n);
        var result = SolverBuffers.DenseScratchVectorDouble(n);
        for (int row = 0; row < n; row++)
        {
            result[row] = rightHandSide[row];
            for (int column = 0; column < n; column++)
            {
                workspace[row, column] = matrix[row, column];
            }
        }
        SolveCoreInPlace(workspace, result, n);
        return result;
    }

    public float[] Solve(float[,] matrix, float[] rightHandSide)
    {
        int n = matrix.GetLength(0);
        var workspace = SolverBuffers.DenseScratchMatrixFloat(n, n);
        var result = SolverBuffers.DenseScratchVectorFloat(n);
        for (int row = 0; row < n; row++)
        {
            result[row] = rightHandSide[row];
            for (int column = 0; column < n; column++)
            {
                workspace[row, column] = matrix[row, column];
            }
        }
        SolveCoreInPlace(workspace, result, n);
        return result;
    }

    public void SolveInPlace(double[,] matrix, double[] rightHandSide)
        => SolveCoreInPlace(matrix, rightHandSide, matrix.GetLength(0));

    public void SolveInPlace(float[,] matrix, float[] rightHandSide)
        => SolveCoreInPlace(matrix, rightHandSide, matrix.GetLength(0));

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    private static void SolveCoreInPlace<T>(T[,] matrix, T[] rightHandSide, int size)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (matrix is null)
            throw new ArgumentNullException(nameof(matrix));
        if (rightHandSide is null)
            throw new ArgumentNullException(nameof(rightHandSide));

        // The buffers may be pooled and hence larger than `size`. The
        // solver operates strictly within the leading size × size block.
        if (matrix.GetLength(0) < size || matrix.GetLength(1) < size)
            throw new ArgumentException("The coefficient matrix must contain at least size rows and columns.", nameof(matrix));
        if (rightHandSide.Length < size)
            throw new ArgumentException("The right-hand side length must be at least size.", nameof(rightHandSide));

        if (size == 0)
            return;

        T[,] a = matrix;
        T[] b = rightHandSide;

        for (var pivotIndex = 0; pivotIndex < size - 1; pivotIndex++)
        {
            var pivotRow = pivotIndex;
            for (var row = pivotIndex + 1; row < size; row++)
            {
                if (T.Abs(a[row, pivotIndex]) > T.Abs(a[pivotRow, pivotIndex]))
                    pivotRow = row;
            }

            T pivotValue = a[pivotRow, pivotIndex];
            if (T.Abs(pivotValue) < T.CreateChecked(1e-12))
                throw new InvalidOperationException("The linear system is singular or ill-conditioned.");

            T pivotInverse = T.One / pivotValue;
            a[pivotRow, pivotIndex] = a[pivotIndex, pivotIndex];

            for (var column = pivotIndex + 1; column < size; column++)
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

            for (var row = pivotIndex + 1; row < size; row++)
            {
                T factor = a[row, pivotIndex];
                for (var column = pivotIndex + 1; column < size; column++)
                {
                    a[row, column] = LegacyPrecisionMath.SeparateMultiplySubtract(
                        factor,
                        a[pivotIndex, column],
                        a[row, column]);
                }

                b[row] = LegacyPrecisionMath.SeparateMultiplySubtract(factor, b[pivotIndex], b[row]);
            }
        }

        b[size - 1] /= a[size - 1, size - 1];

        for (var row = size - 2; row >= 0; row--)
        {
            for (var column = row + 1; column < size; column++)
            {
                b[row] = LegacyPrecisionMath.SeparateMultiplySubtract(a[row, column], b[column], b[row]);
            }
        }
    }
}
