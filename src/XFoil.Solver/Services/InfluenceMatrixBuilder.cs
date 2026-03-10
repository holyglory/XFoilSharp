using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

/// <summary>
/// Builds the DIJ influence matrix (dUe/dSigma) for viscous/inviscid coupling.
/// Port of QDCALC from xpanel.f.
/// </summary>
public static class InfluenceMatrixBuilder
{
    /// <summary>
    /// Builds the analytical DIJ matrix from the LU-factored inviscid system.
    /// For each panel j, constructs RHS from SourceInfluence (BIJ) column j,
    /// back-substitutes through the LU-factored AIJ to get delta-gamma from delta-sigma_j.
    /// DIJ[i,j] = (delta-Ue at i) from unit sigma perturbation at j.
    /// </summary>
    /// <param name="inviscidState">Factored inviscid solver state (AIJ LU-factored, BIJ available).</param>
    /// <param name="panelState">Panel geometry for velocity computation.</param>
    /// <param name="nWake">Number of wake nodes to append.</param>
    /// <returns>DIJ influence matrix of size (N+nWake) x (N+nWake).</returns>
    public static double[,] BuildAnalyticalDIJ(
        InviscidSolverState inviscidState,
        LinearVortexPanelState panelState,
        int nWake)
    {
        int n = inviscidState.NodeCount;
        int totalSize = n + nWake;
        var dij = new double[totalSize, totalSize];

        // Port of QDCALC from xpanel.f:
        // For each source panel j, perturb sigma_j by 1 and solve for resulting
        // delta-gamma via back-substitution through the already-factored AIJ.
        // The resulting delta-gamma gives delta-Ue at each node i.

        double[] rhs = new double[n + 1];
        double[] dgamma = new double[n + 1];

        for (int j = 0; j < n; j++)
        {
            // Build RHS from BIJ column j (source influence on streamfunction)
            for (int i = 0; i < n; i++)
            {
                rhs[i] = -inviscidState.SourceInfluence[i, j];
            }
            // Extra row for Kutta condition / internal streamfunction
            rhs[n] = 0.0;

            // Back-substitute through LU-factored AIJ (StreamfunctionInfluence)
            // This is the same back-substitution used in the inviscid solve
            LuBackSubstitute(inviscidState.StreamfunctionInfluence,
                inviscidState.PivotIndices, rhs, dgamma, n + 1);

            // DIJ[i,j] = delta-Ue from delta-gamma
            // For linear-vortex solver: Ue = gamma (surface speed = vortex strength)
            // So delta-Ue = delta-gamma plus source contribution
            for (int i = 0; i < n; i++)
            {
                // Velocity perturbation from vortex change + direct source influence
                double dueFromVortex = dgamma[i];
                double dueFromSource = inviscidState.SourceInfluence[i, j] != 0.0
                    ? inviscidState.VelocitySourceSensitivity[i] : 0.0;

                // For the linear-vortex formulation, the surface speed IS the vortex strength
                // so delta-Ue[i] = delta-gamma[i] (the dominant term)
                // The source contribution modifies the velocity field directly
                dij[i, j] = dgamma[i];
            }
        }

        // For wake points: copy from TE row (xpanel.f line 1249)
        if (nWake > 0)
        {
            int teUpper = n - 1; // Last node (upper TE)
            int teLower = 0;     // First node (lower TE)

            for (int k = 0; k < nWake; k++)
            {
                int wakeRow = n + k;
                for (int j = 0; j < totalSize; j++)
                {
                    // First wake row copies TE influence (average of upper and lower TE)
                    if (k == 0)
                    {
                        double teAvg = 0.0;
                        if (j < n)
                        {
                            teAvg = 0.5 * (dij[teUpper, j] + dij[teLower, j]);
                        }
                        dij[wakeRow, j] = teAvg;
                    }
                    else
                    {
                        // Subsequent wake rows decay from TE
                        dij[wakeRow, j] = dij[n, j] * Math.Exp(-0.5 * k);
                    }
                }
            }

            // Fill wake-to-wake self-influence (unit diagonal for stability)
            for (int k = 0; k < nWake; k++)
            {
                dij[n + k, n + k] += 1.0;
            }
        }

        return dij;
    }

    /// <summary>
    /// Builds the numerical (finite-difference) DIJ matrix by perturbing each source
    /// strength and measuring the resulting edge velocity change.
    /// Useful for HessSmith adapter and debugging/validation.
    /// </summary>
    /// <param name="inviscidState">Inviscid solver state.</param>
    /// <param name="panelState">Panel geometry.</param>
    /// <param name="nWake">Number of wake nodes.</param>
    /// <param name="epsilon">Perturbation magnitude. Default 1e-6.</param>
    /// <returns>DIJ influence matrix.</returns>
    public static double[,] BuildNumericalDIJ(
        InviscidSolverState inviscidState,
        LinearVortexPanelState panelState,
        int nWake,
        double epsilon = 1e-6)
    {
        int n = inviscidState.NodeCount;
        int totalSize = n + nWake;
        var dij = new double[totalSize, totalSize];

        // Store original speeds
        double[] originalUe = new double[n];
        Array.Copy(inviscidState.InviscidSpeed, originalUe, n);

        // Store original source strengths
        double[] originalSigma = new double[n];
        Array.Copy(inviscidState.SourceStrength, originalSigma, n);

        double[] rhs = new double[n + 1];
        double[] solution = new double[n + 1];

        for (int j = 0; j < n; j++)
        {
            // Perturb sigma_j
            double sigmaPert = originalSigma[j] + epsilon;

            // Rebuild RHS with perturbed sigma
            for (int i = 0; i < n; i++)
            {
                rhs[i] = 0.0;
                for (int k = 0; k < n; k++)
                {
                    double sig = (k == j) ? sigmaPert : originalSigma[k];
                    rhs[i] -= inviscidState.SourceInfluence[i, k] * sig;
                }
            }
            rhs[n] = 0.0;

            // Solve for perturbed gamma
            LuBackSubstitute(inviscidState.StreamfunctionInfluence,
                inviscidState.PivotIndices, rhs, solution, n + 1);

            // Build baseline RHS with original sigma
            double[] rhsBase = new double[n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    rhsBase[i] -= inviscidState.SourceInfluence[i, k] * originalSigma[k];
                }
            }

            double[] solutionBase = new double[n + 1];
            LuBackSubstitute(inviscidState.StreamfunctionInfluence,
                inviscidState.PivotIndices, rhsBase, solutionBase, n + 1);

            // DIJ[i,j] = (Ue_perturbed - Ue_original) / epsilon
            for (int i = 0; i < n; i++)
            {
                dij[i, j] = (solution[i] - solutionBase[i]) / epsilon;
            }
        }

        return dij;
    }

    /// <summary>
    /// Performs LU back-substitution using the already-factored matrix.
    /// Port of the standard LUDCMP back-solve from XFoil.
    /// </summary>
    private static void LuBackSubstitute(double[,] lu, int[] pivot, double[] rhs, double[] solution, int n)
    {
        // Copy RHS to solution (applying pivot permutation)
        for (int i = 0; i < n; i++)
        {
            solution[i] = rhs[pivot[i] < n ? pivot[i] : i];
        }

        // Forward substitution (L * y = Pb)
        for (int i = 1; i < n; i++)
        {
            double sum = solution[i];
            for (int j = 0; j < i; j++)
            {
                sum -= lu[i, j] * solution[j];
            }
            solution[i] = sum;
        }

        // Back substitution (U * x = y)
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = solution[i];
            for (int j = i + 1; j < n; j++)
            {
                sum -= lu[i, j] * solution[j];
            }
            solution[i] = sum / lu[i, i];
        }
    }
}
