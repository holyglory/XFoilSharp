using System.Numerics;

namespace XFoil.Solver.Numerics;

public sealed class DenseLinearSystemSolver
{
    public static readonly DenseLinearSystemSolver Shared = new();

    public double[] Solve(double[,] matrix, double[] rightHandSide)
    {
        return SolveCore(matrix, rightHandSide);
    }

    public float[] Solve(float[,] matrix, float[] rightHandSide)
    {
        return SolveCore(matrix, rightHandSide);
    }

    public void SolveInPlace(double[,] matrix, double[] rightHandSide)
        => SolveCoreInPlace(matrix, rightHandSide);

    public void SolveInPlace(float[,] matrix, float[] rightHandSide)
        => SolveCoreInPlace(matrix, rightHandSide);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    private static T[] SolveCore<T>(T[,] matrix, T[] rightHandSide)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (matrix is null)
            throw new ArgumentNullException(nameof(matrix));
        if (rightHandSide is null)
            throw new ArgumentNullException(nameof(rightHandSide));

        var rowCount = matrix.GetLength(0);
        var columnCount = matrix.GetLength(1);
        if (rowCount != columnCount)
            throw new ArgumentException("The coefficient matrix must be square.", nameof(matrix));
        if (rightHandSide.Length != rowCount)
            throw new ArgumentException("The right-hand side length must match the matrix size.", nameof(rightHandSide));

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
            return Array.Empty<T>();

        for (var pivotIndex = 0; pivotIndex < rowCount - 1; pivotIndex++)
        {
            var pivotRow = pivotIndex;
            for (var row = pivotIndex + 1; row < rowCount; row++)
            {
                if (T.Abs(a[row, pivotIndex]) > T.Abs(a[pivotRow, pivotIndex]))
                    pivotRow = row;
            }

            T pivotValue = a[pivotRow, pivotIndex];
            if (T.Abs(pivotValue) < T.CreateChecked(1e-12))
                throw new InvalidOperationException("The linear system is singular or ill-conditioned.");

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

            for (var row = pivotIndex + 1; row < rowCount; row++)
            {
                T factor = a[row, pivotIndex];
                for (var column = pivotIndex + 1; column < columnCount; column++)
                {
                    a[row, column] = LegacyPrecisionMath.SeparateMultiplySubtract(
                        factor,
                        a[pivotIndex, column],
                        a[row, column]);
                }

                b[row] = LegacyPrecisionMath.SeparateMultiplySubtract(factor, b[pivotIndex], b[row]);
            }
        }

        b[rowCount - 1] /= a[rowCount - 1, rowCount - 1];

        for (var row = rowCount - 2; row >= 0; row--)
        {
            for (var column = row + 1; column < columnCount; column++)
            {
                b[row] = LegacyPrecisionMath.SeparateMultiplySubtract(a[row, column], b[column], b[row]);
            }
        }

        return b;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    private static void SolveCoreInPlace<T>(T[,] matrix, T[] rightHandSide)
        where T : struct, IFloatingPointIeee754<T>
    {
        if (matrix is null)
            throw new ArgumentNullException(nameof(matrix));
        if (rightHandSide is null)
            throw new ArgumentNullException(nameof(rightHandSide));

        var rowCount = matrix.GetLength(0);
        var columnCount = matrix.GetLength(1);
        if (rowCount != columnCount)
            throw new ArgumentException("The coefficient matrix must be square.", nameof(matrix));
        if (rightHandSide.Length != rowCount)
            throw new ArgumentException("The right-hand side length must match the matrix size.", nameof(rightHandSide));

        if (rowCount == 0)
            return;

        T[,] a = matrix;
        T[] b = rightHandSide;

        for (var pivotIndex = 0; pivotIndex < rowCount - 1; pivotIndex++)
        {
            var pivotRow = pivotIndex;
            for (var row = pivotIndex + 1; row < rowCount; row++)
            {
                if (T.Abs(a[row, pivotIndex]) > T.Abs(a[pivotRow, pivotIndex]))
                    pivotRow = row;
            }

            T pivotValue = a[pivotRow, pivotIndex];
            if (T.Abs(pivotValue) < T.CreateChecked(1e-12))
                throw new InvalidOperationException("The linear system is singular or ill-conditioned.");

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

            for (var row = pivotIndex + 1; row < rowCount; row++)
            {
                T factor = a[row, pivotIndex];
                for (var column = pivotIndex + 1; column < columnCount; column++)
                {
                    a[row, column] = LegacyPrecisionMath.SeparateMultiplySubtract(
                        factor,
                        a[pivotIndex, column],
                        a[row, column]);
                }

                b[row] = LegacyPrecisionMath.SeparateMultiplySubtract(factor, b[pivotIndex], b[row]);
            }
        }

        b[rowCount - 1] /= a[rowCount - 1, rowCount - 1];

        for (var row = rowCount - 2; row >= 0; row--)
        {
            for (var column = row + 1; column < columnCount; column++)
            {
                b[row] = LegacyPrecisionMath.SeparateMultiplySubtract(a[row, column], b[column], b[row]);
            }
        }
    }
}
