using XFoil.ThesisClosureSolver.Inviscid;

namespace XFoil.ThesisClosureSolver.Newton;

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
public sealed class ThesisClosureGlobalResidual
{
    private readonly ThesisClosureGlobalState _layout;
    private readonly ThesisClosurePanelSolver.PanelizedGeometry _pg;
    private readonly double[,] _aGammaNormal;
    private readonly double[,] _aSigmaNormal;
    private readonly double[,] _aGammaTangent;
    private readonly double[,] _aSigmaTangent;
    private readonly double _freestreamSpeed;
    private readonly double _alphaRadians;
    private readonly double _kinematicViscosity;
    private readonly double _machEdge;
    private readonly bool _useRealBLResiduals;
    private readonly double _initialTheta;

    public ThesisClosureGlobalResidual(
        ThesisClosureGlobalState layout,
        ThesisClosurePanelSolver.PanelizedGeometry pg,
        double freestreamSpeed,
        double alphaRadians,
        double kinematicViscosity = 1e-6,
        double machEdge = 0.0,
        bool useRealBLResiduals = false,
        double initialTheta = 1e-5)
    {
        int n = pg.PanelCount;
        if (layout.GammaCount != n + 1)
            throw new System.ArgumentException(
                $"layout.GammaCount ({layout.GammaCount}) must match N+1 ({n+1})");
        if (layout.SigmaCount != n + 1)
            throw new System.ArgumentException(
                $"layout.SigmaCount ({layout.SigmaCount}) must match N+1 ({n+1})");
        if (useRealBLResiduals && layout.BLStationCount != n)
            throw new System.ArgumentException(
                $"layout.BLStationCount ({layout.BLStationCount}) must match N ({n}) when useRealBLResiduals=true (one station per panel midpoint)");

        _layout = layout;
        _pg = pg;
        _freestreamSpeed = freestreamSpeed;
        _alphaRadians = alphaRadians;
        _kinematicViscosity = kinematicViscosity;
        _machEdge = machEdge;
        _useRealBLResiduals = useRealBLResiduals;
        _initialTheta = initialTheta;
        _aGammaNormal = ThesisClosurePanelSolver.BuildVortexNormalInfluenceMatrix(pg);
        _aSigmaNormal = ThesisClosurePanelSolver.BuildSourceNormalInfluenceMatrix(pg);
        _aGammaTangent = ThesisClosurePanelSolver.BuildVortexInfluenceMatrix(pg);
        _aSigmaTangent = ThesisClosurePanelSolver.BuildSourceTangentInfluenceMatrix(pg);
    }

    /// <summary>
    /// Computes R(state). Returns a fresh array of size
    /// <see cref="ThesisClosureGlobalState.StateSize"/>.
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

        if (!_useRealBLResiduals)
        {
            // σ/BL placeholder identity rows — preserve P4.6 gate path.
            for (int k = 0; k < _layout.SigmaCount; k++)
                r[_layout.SigmaOffset + k] = sigma[k];
            for (int k = 0; k < _layout.BLStationCount; k++)
            {
                r[_layout.DstarOffset + k] = dStar[k];
                r[_layout.ThetaOffset + k] = theta[k];
                r[_layout.CTauOffset + k] = cTau[k];
            }
            return r;
        }

        // P5.3: real BL + σ residuals. BL station i lives on panel
        // midpoint i (N stations total). Ue at midpoint = freestream
        // tangent + vortex tangent contribution + source tangent
        // contribution. March along panel order (TE→upper→LE→lower
        // →TE); per-station residuals use the previous station's
        // (δ*, θ, Cτ) and Ue.
        var ueMid = new double[n];
        for (int i = 0; i < n; i++)
        {
            double ue = vx * _pg.TangentX[i] + vy * _pg.TangentY[i];
            for (int k = 0; k < n + 1; k++)
            {
                ue += _aGammaTangent[i, k] * gamma[k]
                    + _aSigmaTangent[i, k] * sigma[k];
            }
            ueMid[i] = System.Math.Abs(ue);
        }

        // Station 0 anchor: (δ*[0], θ[0], Cτ[0], σ[0]) pinned to
        // laminar initial conditions.
        r[_layout.SigmaOffset + 0] = sigma[0];
        r[_layout.DstarOffset + 0] = dStar[0] - _initialTheta * 2.5;  // H ≈ 2.5 at start
        r[_layout.ThetaOffset + 0] = theta[0] - _initialTheta;
        r[_layout.CTauOffset + 0] = cTau[0];

        // Marching residuals at i = 1 .. N-1 (if BLStationCount == N).
        for (int i = 1; i < _layout.BLStationCount; i++)
        {
            double thetaPrev = theta[i - 1];
            double thetaI = theta[i];
            double dStarPrev = dStar[i - 1];
            double dStarI = dStar[i];
            double hPrev = thetaPrev > 1e-18 ? dStarPrev / thetaPrev : 1.4;
            double hI = thetaI > 1e-18 ? dStarI / thetaI : 1.4;
            double uePrev = ueMid[i - 1];
            double ueI = ueMid[i];
            double dx = System.Math.Max(_pg.Length[i], 1e-12);

            r[_layout.ThetaOffset + i] = ThesisClosureBoundaryLayerResidual.MomentumResidual(
                thetaPrev, thetaI, hPrev, hI, uePrev, ueI, dx,
                _kinematicViscosity, _machEdge);

            // δ* residual derived from the shape-param equation:
            // reuse ShapeParamResidual which folds H* in.
            r[_layout.DstarOffset + i] = ThesisClosureBoundaryLayerResidual.ShapeParamResidual(
                thetaPrev, thetaI, hPrev, hI, cTau[i], uePrev, ueI, dx,
                _kinematicViscosity, _machEdge);

            r[_layout.CTauOffset + i] = ThesisClosureBoundaryLayerResidual.LagResidual(
                cTau[i - 1], cTau[i], thetaPrev, thetaI, hPrev, hI,
                uePrev, ueI, dx, _kinematicViscosity, _machEdge);

            r[_layout.SigmaOffset + i] = ThesisClosureBoundaryLayerResidual.SourceConstraintResidual(
                sigma[i], dStarPrev, dStarI, uePrev, ueI, dx);
        }

        // σ[N] anchor (the extra σ node past the last station): no
        // BL station exists, pin it to zero.
        if (_layout.SigmaCount > _layout.BLStationCount)
        {
            r[_layout.SigmaOffset + _layout.SigmaCount - 1] =
                sigma[_layout.SigmaCount - 1];
        }

        return r;
    }
}
