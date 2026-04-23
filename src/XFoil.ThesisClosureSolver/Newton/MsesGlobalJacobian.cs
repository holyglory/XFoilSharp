namespace XFoil.ThesisClosureSolver.Newton;

/// <summary>
/// P4.3 — Finite-difference Jacobian of a residual function.
/// Used by the global Newton solver as the initial (robust but
/// slow) Jacobian. P5 will replace BL-block entries with analytical
/// derivatives where tractable; inviscid blocks stay FD-or-analytic
/// depending on needs.
///
/// ∂R_i / ∂x_j via central difference with per-unknown adaptive ε:
///   ε_j = max(|x_j| · relEps, absEps)
///   J[i, j] ≈ (R_i(x + ε_j·e_j) − R_i(x − ε_j·e_j)) / (2·ε_j)
///
/// Cost: 2·N residual evaluations. For N=1000 and a 10 ms residual,
/// that's 20 s per Jacobian build — acceptable for P4 bring-up.
/// </summary>
public static class ThesisClosureGlobalJacobian
{
    /// <summary>
    /// Computes the Jacobian J[i, j] = ∂R_i / ∂x_j at state x
    /// via central differences. Returns a new (N × N) dense matrix.
    /// </summary>
    /// <param name="state">Point at which to evaluate the Jacobian.</param>
    /// <param name="residualFunc">Computes R(x) as a fresh array.</param>
    /// <param name="relEps">Relative step size per unknown (default 1e-6).</param>
    /// <param name="absEps">Absolute floor on step size (default 1e-8).</param>
    public static double[,] ComputeFiniteDifference(
        double[] state,
        System.Func<double[], double[]> residualFunc,
        double relEps = 1e-6,
        double absEps = 1e-8)
    {
        if (state is null) throw new System.ArgumentNullException(nameof(state));
        if (residualFunc is null) throw new System.ArgumentNullException(nameof(residualFunc));
        if (relEps <= 0.0) throw new System.ArgumentOutOfRangeException(nameof(relEps));
        if (absEps < 0.0) throw new System.ArgumentOutOfRangeException(nameof(absEps));

        int n = state.Length;
        var jac = new double[n, n];
        var probe = new double[n];
        System.Array.Copy(state, probe, n);

        for (int j = 0; j < n; j++)
        {
            double eps = System.Math.Max(System.Math.Abs(state[j]) * relEps, absEps);
            double orig = state[j];

            probe[j] = orig + eps;
            var rPlus = residualFunc(probe);
            if (rPlus.Length != n) throw new System.InvalidOperationException(
                $"residualFunc returned length {rPlus.Length}, expected {n}");

            probe[j] = orig - eps;
            var rMinus = residualFunc(probe);

            probe[j] = orig;  // restore

            double invDenom = 1.0 / (2.0 * eps);
            for (int i = 0; i < n; i++)
            {
                jac[i, j] = (rPlus[i] - rMinus[i]) * invDenom;
            }
        }
        return jac;
    }
}
