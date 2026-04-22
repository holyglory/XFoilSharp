using XFoil.MsesSolver.Inviscid;

namespace XFoil.MsesSolver.Newton;

/// <summary>
/// P4.4 — Newton iteration loop for the MSES global viscous-
/// inviscid system.
///
/// Each step:
///   1. R   = residualFunc(state)
///   2. J   = jacobianFunc(state, residualFunc)   (FD or analytical)
///   3. Solve J·Δ = −R   (Gaussian elimination from
///      MsesInviscidPanelSolver.SolveLinearSystem)
///   4. state ← state + Δ  (damping/under-relaxation added in P4.5)
///
/// Convergence: ‖R‖_∞ &lt; resTol AND ‖Δ‖_∞ &lt; stepTol.
/// </summary>
public static class MsesGlobalNewton
{
    public readonly record struct Result(
        double[] State,
        bool Converged,
        int IterationsRun,
        double FinalResidualNorm,
        double FinalStepNorm,
        double[] ResidualHistory);

    /// <summary>
    /// Drives the Newton iteration from an initial guess to
    /// convergence (or the iteration cap).
    /// </summary>
    public static Result Solve(
        double[] initialState,
        System.Func<double[], double[]> residualFunc,
        System.Func<double[], System.Func<double[], double[]>, double[,]> jacobianFunc,
        int maxIterations = 30,
        double resTol = 1e-8,
        double stepTol = 1e-8)
    {
        if (initialState is null) throw new System.ArgumentNullException(nameof(initialState));
        if (residualFunc is null) throw new System.ArgumentNullException(nameof(residualFunc));
        if (jacobianFunc is null) throw new System.ArgumentNullException(nameof(jacobianFunc));

        int n = initialState.Length;
        var state = new double[n];
        System.Array.Copy(initialState, state, n);
        var resHist = new System.Collections.Generic.List<double>();
        double resNorm = double.NaN;
        double stepNorm = double.NaN;
        bool converged = false;
        int iter;
        for (iter = 0; iter < maxIterations; iter++)
        {
            var r = residualFunc(state);
            resNorm = InfinityNorm(r);
            resHist.Add(resNorm);
            if (resNorm < resTol && iter > 0 && stepNorm < stepTol)
            {
                converged = true;
                break;
            }
            var jac = jacobianFunc(state, residualFunc);
            // Solve J·Δ = −R.
            var negR = new double[n];
            for (int i = 0; i < n; i++) negR[i] = -r[i];
            double[] delta;
            try
            {
                delta = MsesInviscidPanelSolver.SolveLinearSystem(jac, negR);
            }
            catch (System.InvalidOperationException)
            {
                // Singular Jacobian — typically means the state is
                // degenerate. Report the current iteration as
                // non-converged and return.
                break;
            }
            stepNorm = InfinityNorm(delta);
            for (int i = 0; i < n; i++) state[i] += delta[i];
            if (resNorm < resTol && stepNorm < stepTol)
            {
                converged = true;
                // Force one more residual evaluation so history ends
                // on the converged value.
                var rFinal = residualFunc(state);
                resHist.Add(InfinityNorm(rFinal));
                resNorm = InfinityNorm(rFinal);
                iter++;
                break;
            }
        }
        return new Result(state, converged, iter, resNorm, stepNorm, resHist.ToArray());
    }

    private static double InfinityNorm(double[] x)
    {
        double m = 0.0;
        foreach (var v in x)
        {
            double a = System.Math.Abs(v);
            if (a > m) m = a;
        }
        return m;
    }
}
