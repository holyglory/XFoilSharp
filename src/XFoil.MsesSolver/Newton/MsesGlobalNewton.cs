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
    /// <param name="maxStepNorm">Cap on ‖Δ‖_∞ per step. If the
    /// natural Newton step exceeds this, it's scaled down (damped).
    /// Default = no cap (∞). Typical viscous-inviscid systems
    /// benefit from 0.1–0.5 (scaled-unknown units).</param>
    /// <param name="lineSearch">If true, halve the step up to
    /// <paramref name="maxLineSearchTrials"/> times whenever the
    /// post-step residual is larger than pre-step. Default true.</param>
    /// <param name="maxLineSearchTrials">Max halvings per outer
    /// step. Default 3.</param>
    public static Result Solve(
        double[] initialState,
        System.Func<double[], double[]> residualFunc,
        System.Func<double[], System.Func<double[], double[]>, double[,]> jacobianFunc,
        int maxIterations = 30,
        double resTol = 1e-8,
        double stepTol = 1e-8,
        double maxStepNorm = double.PositiveInfinity,
        bool lineSearch = true,
        int maxLineSearchTrials = 3)
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
            double deltaNorm = InfinityNorm(delta);

            // Damping: scale Δ down if it exceeds the configured cap.
            double damping = 1.0;
            if (deltaNorm > maxStepNorm && maxStepNorm > 0)
            {
                damping = maxStepNorm / deltaNorm;
            }

            // Trial step + optional line search.
            double[] trialState = new double[n];
            double trialResNorm = double.PositiveInfinity;
            double appliedDamping = damping;
            for (int trial = 0; trial <= maxLineSearchTrials; trial++)
            {
                for (int i = 0; i < n; i++)
                    trialState[i] = state[i] + appliedDamping * delta[i];
                var rTrial = residualFunc(trialState);
                trialResNorm = InfinityNorm(rTrial);
                if (!lineSearch || trialResNorm <= resNorm || trial == maxLineSearchTrials)
                    break;
                // Overshoot: halve and retry.
                appliedDamping *= 0.5;
            }
            stepNorm = deltaNorm * appliedDamping;
            for (int i = 0; i < n; i++) state[i] = trialState[i];

            if (trialResNorm < resTol && stepNorm < stepTol)
            {
                converged = true;
                resHist.Add(trialResNorm);
                resNorm = trialResNorm;
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
