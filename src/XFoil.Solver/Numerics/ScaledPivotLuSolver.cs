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
    public static void Decompose(double[,] matrix, int[] pivotIndices, int size)
    {
        // Step 1: Compute row scaling factors (1/max |element| in each row)
        var scaling = new double[size];
        for (int i = 0; i < size; i++)
        {
            double rowMax = 0.0;
            for (int j = 0; j < size; j++)
            {
                double absVal = Math.Abs(matrix[i, j]);
                if (absVal > rowMax)
                {
                    rowMax = absVal;
                }
            }

            scaling[i] = 1.0 / rowMax;
        }

        // Step 2: Crout's method -- column by column
        for (int j = 0; j < size; j++)
        {
            // Compute column of U (rows above diagonal)
            for (int i = 0; i < j; i++)
            {
                double sum = matrix[i, j];
                for (int k = 0; k < i; k++)
                {
                    sum -= matrix[i, k] * matrix[k, j];
                }

                matrix[i, j] = sum;
            }

            // Compute column of L (rows at and below diagonal) and find pivot
            double maxScaled = 0.0;
            int pivotRow = j;

            for (int i = j; i < size; i++)
            {
                double sum = matrix[i, j];
                for (int k = 0; k < j; k++)
                {
                    sum -= matrix[i, k] * matrix[k, j];
                }

                matrix[i, j] = sum;

                // Scaled pivot comparison
                double scaledMagnitude = scaling[i] * Math.Abs(sum);
                if (scaledMagnitude >= maxScaled)
                {
                    pivotRow = i;
                    maxScaled = scaledMagnitude;
                }
            }

            // Swap rows if needed
            if (j != pivotRow)
            {
                for (int k = 0; k < size; k++)
                {
                    double temp = matrix[pivotRow, k];
                    matrix[pivotRow, k] = matrix[j, k];
                    matrix[j, k] = temp;
                }

                scaling[pivotRow] = scaling[j];
            }

            // Record pivot index
            pivotIndices[j] = pivotRow;

            // Divide column below diagonal by pivot
            if (j != size - 1)
            {
                double pivotInverse = 1.0 / matrix[j, j];
                for (int i = j + 1; i < size; i++)
                {
                    matrix[i, j] *= pivotInverse;
                }
            }
        }
    }

    /// <summary>
    /// Solves the system using the LU factors from <see cref="Decompose"/>.
    /// The right-hand side is modified in-place with the solution.
    /// </summary>
    /// <param name="luMatrix">LU-factored matrix from Decompose.</param>
    /// <param name="pivotIndices">Pivot indices from Decompose.</param>
    /// <param name="rhs">Right-hand side, replaced by the solution. Length >= size.</param>
    /// <param name="size">Dimension of the system.</param>
    public static void BackSubstitute(double[,] luMatrix, int[] pivotIndices, double[] rhs, int size)
    {
        // Forward substitution using L factor with pivot unscrambling
        int ii = -1; // Will be set to first non-zero element index

        for (int i = 0; i < size; i++)
        {
            int ll = pivotIndices[i];
            double sum = rhs[ll];
            rhs[ll] = rhs[i];

            if (ii >= 0)
            {
                for (int j = ii; j < i; j++)
                {
                    sum -= luMatrix[i, j] * rhs[j];
                }
            }
            else if (sum != 0.0)
            {
                ii = i;
            }

            rhs[i] = sum;
        }

        // Backward substitution using U factor
        for (int i = size - 1; i >= 0; i--)
        {
            double sum = rhs[i];
            for (int j = i + 1; j < size; j++)
            {
                sum -= luMatrix[i, j] * rhs[j];
            }

            rhs[i] = sum / luMatrix[i, i];
        }
    }
}
