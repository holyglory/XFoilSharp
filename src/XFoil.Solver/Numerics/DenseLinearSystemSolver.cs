namespace XFoil.Solver.Numerics;

public sealed class DenseLinearSystemSolver
{
    public double[] Solve(double[,] matrix, double[] rightHandSide)
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

        var a = (double[,])matrix.Clone();
        var b = (double[])rightHandSide.Clone();

        for (var pivotIndex = 0; pivotIndex < rowCount; pivotIndex++)
        {
            var pivotRow = pivotIndex;
            var pivotMagnitude = Math.Abs(a[pivotIndex, pivotIndex]);

            for (var row = pivotIndex + 1; row < rowCount; row++)
            {
                var candidate = Math.Abs(a[row, pivotIndex]);
                if (candidate > pivotMagnitude)
                {
                    pivotMagnitude = candidate;
                    pivotRow = row;
                }
            }

            if (pivotMagnitude < 1e-12)
            {
                throw new InvalidOperationException("The linear system is singular or ill-conditioned.");
            }

            if (pivotRow != pivotIndex)
            {
                SwapRows(a, pivotIndex, pivotRow, columnCount);
                (b[pivotIndex], b[pivotRow]) = (b[pivotRow], b[pivotIndex]);
            }

            var pivot = a[pivotIndex, pivotIndex];
            for (var row = pivotIndex + 1; row < rowCount; row++)
            {
                var factor = a[row, pivotIndex] / pivot;
                if (Math.Abs(factor) < 1e-16)
                {
                    continue;
                }

                a[row, pivotIndex] = 0d;
                for (var column = pivotIndex + 1; column < columnCount; column++)
                {
                    a[row, column] -= factor * a[pivotIndex, column];
                }

                b[row] -= factor * b[pivotIndex];
            }
        }

        var solution = new double[rowCount];
        for (var row = rowCount - 1; row >= 0; row--)
        {
            var sum = b[row];
            for (var column = row + 1; column < columnCount; column++)
            {
                sum -= a[row, column] * solution[column];
            }

            solution[row] = sum / a[row, row];
        }

        return solution;
    }

    private static void SwapRows(double[,] matrix, int firstRow, int secondRow, int columnCount)
    {
        for (var column = 0; column < columnCount; column++)
        {
            (matrix[firstRow, column], matrix[secondRow, column]) =
                (matrix[secondRow, column], matrix[firstRow, column]);
        }
    }
}
