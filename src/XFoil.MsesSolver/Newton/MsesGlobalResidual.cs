using XFoil.MsesSolver.Inviscid;

namespace XFoil.MsesSolver.Newton;

/// <summary>
/// P4.2 — Residual assembler for the MSES global Newton system.
/// Given a state vector, computes R(state) — the same length as
/// the state, zero at a converged physical solution.
///
/// Current (P4.2 skeleton) rows:
///   0 .. N-1          flow-tangency BC per collocation point:
///                     R_i = Σ A_γ_n[i,k]·γ_k + Σ A_σ_n[i,k]·σ_k
///                           + V∞·n_i
///   N                 Kutta: R_N = γ_0 + γ_N
///   N+1 .. 2N+1       σ-constraint PLACEHOLDER: R = σ_k
///                     (P5.2 replaces with σ = d(Ue·δ*)/ds)
///   2N+2 ..           BL-state PLACEHOLDER: R = δ*, θ, Cτ
///                     (P5 replaces with momentum/shape/lag residuals)
///
/// The placeholders make the initial Newton converge cleanly with
/// σ = BL = 0, giving the P4.6 γ-only self-consistency check a
/// clean target. P5 then replaces the placeholders with the real
/// BL physics.
/// </summary>
public sealed class MsesGlobalResidual
{
    private readonly MsesGlobalState _layout;
    private readonly MsesInviscidPanelSolver.PanelizedGeometry _pg;
    private readonly double[,] _aGammaNormal;
    private readonly double[,] _aSigmaNormal;
    private readonly double _freestreamSpeed;
    private readonly double _alphaRadians;

    public MsesGlobalResidual(
        MsesGlobalState layout,
        MsesInviscidPanelSolver.PanelizedGeometry pg,
        double freestreamSpeed,
        double alphaRadians)
    {
        int n = pg.PanelCount;
        if (layout.GammaCount != n + 1)
            throw new System.ArgumentException(
                $"layout.GammaCount ({layout.GammaCount}) must match N+1 ({n+1})");
        if (layout.SigmaCount != n + 1)
            throw new System.ArgumentException(
                $"layout.SigmaCount ({layout.SigmaCount}) must match N+1 ({n+1})");

        _layout = layout;
        _pg = pg;
        _freestreamSpeed = freestreamSpeed;
        _alphaRadians = alphaRadians;
        _aGammaNormal = MsesInviscidPanelSolver.BuildVortexNormalInfluenceMatrix(pg);
        _aSigmaNormal = MsesInviscidPanelSolver.BuildSourceNormalInfluenceMatrix(pg);
    }

    /// <summary>
    /// Computes R(state). Returns a fresh array of size
    /// <see cref="MsesGlobalState.StateSize"/>.
    /// </summary>
    public double[] Compute(double[] state)
    {
        if (state.Length != _layout.StateSize)
            throw new System.ArgumentException(
                $"state length {state.Length} ≠ expected {_layout.StateSize}");
        int n = _pg.PanelCount;
        var (gamma, sigma, dStar, theta, cTau) = _layout.Unpack(state);

        var r = new double[_layout.StateSize];
        double vx = _freestreamSpeed * System.Math.Cos(_alphaRadians);
        double vy = _freestreamSpeed * System.Math.Sin(_alphaRadians);

        // Rows 0..N-1: flow-tangency residual (V_normal_total = 0).
        for (int i = 0; i < n; i++)
        {
            double rowSum = 0.0;
            for (int k = 0; k < n + 1; k++)
            {
                rowSum += _aGammaNormal[i, k] * gamma[k]
                        + _aSigmaNormal[i, k] * sigma[k];
            }
            rowSum += vx * _pg.NormalX[i] + vy * _pg.NormalY[i];
            r[i] = rowSum;
        }
        // Row N: Kutta.
        r[n] = gamma[0] + gamma[n];

        // σ rows: PLACEHOLDER identity R = σ (P5.2 replaces).
        for (int k = 0; k < _layout.SigmaCount; k++)
        {
            r[_layout.SigmaOffset + k] = sigma[k];
        }
        // BL rows: PLACEHOLDER identity R = (δ*, θ, Cτ) (P5 replaces).
        for (int k = 0; k < _layout.BLStationCount; k++)
        {
            r[_layout.DstarOffset + k] = dStar[k];
            r[_layout.ThetaOffset + k] = theta[k];
            r[_layout.CTauOffset + k] = cTau[k];
        }
        return r;
    }
}
