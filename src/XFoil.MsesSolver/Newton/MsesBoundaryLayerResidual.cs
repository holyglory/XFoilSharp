using XFoil.MsesSolver.Closure;

namespace XFoil.MsesSolver.Newton;

/// <summary>
/// P5.1 — Per-station boundary-layer residual functions, mirroring
/// the per-step implicit updates of
/// <see cref="XFoil.MsesSolver.BoundaryLayer.ThesisExactTurbulentMarcher"/>.
/// Each returns zero when the corresponding Drela thesis equation
/// is satisfied at the given discrete state.
///
/// Equations (thesis §6):
///   6.5  momentum:    dθ/dξ = Cf/2 − (H + 2 − Me²)·θ·(dUe/dξ)/Ue
///   6.10 shape param: θ·dH*/dξ = 2·CD − H*·Cf/2
///                                  − (2·H**/H* + 1 − H)·θ·(dUe/dξ)/Ue
///   6.35 Cτ lag:      Cτ_new  = Cτ_eq + (Cτ_prev − Cτ_eq)·exp(−K2·dξ/δ)
///                     (closed-form decay, K2=4.2, δ=H·θ·(3.15+1.72/(Hk−1)))
///
/// These residuals will replace the placeholder identity rows in
/// <see cref="MsesGlobalResidual"/> in a later iteration (P5.3).
/// For now they live as standalone, unit-testable functions.
/// </summary>
public static class MsesBoundaryLayerResidual
{
    /// <summary>
    /// Momentum residual for turbulent BL using trapezoidal midpoint:
    ///   R_θ = (θ_i − θ_{i−1})/dξ
    ///       − [Cf̄/2 − (H̄ + 2 − Me²)·θ̄·(Ue_i − Ue_{i−1})/(dξ·Ue_i)]
    /// where bars denote arithmetic means of endpoint values.
    ///
    /// Pass <paramref name="isWake"/> = true for a wake/free-shear
    /// layer (Cf ≡ 0). Thesis §6.5 uses Cf=0 in the wake momentum
    /// balance; the rest of the form is identical.
    /// </summary>
    public static double MomentumResidual(
        double thetaPrev, double theta,
        double hPrev, double h,
        double uePrev, double ue,
        double dx,
        double nu,
        double me = 0.0,
        bool isWake = false)
    {
        if (dx <= 0.0) throw new System.ArgumentOutOfRangeException(nameof(dx));
        double thetaAvg = 0.5 * (thetaPrev + theta);
        double hAvg = 0.5 * (hPrev + h);
        double ueSafe = System.Math.Max(ue, 1e-12);
        double hkAvg = MsesClosureRelations.ComputeHk(hAvg, me);
        double reThetaAvg = 0.5 * (uePrev + ue) * thetaAvg
                          / System.Math.Max(nu, 1e-18);
        double cfAvg = isWake
            ? 0.0
            : MsesClosureRelations.ComputeCfTurbulent(hkAvg, reThetaAvg, me);
        double uDuDx_over_Ue = (ue - uePrev) / (dx * ueSafe);
        double rhs = 0.5 * cfAvg
                   - (hAvg + 2.0 - me * me) * thetaAvg * uDuDx_over_Ue;
        return (theta - thetaPrev) / dx - rhs;
    }

    /// <summary>
    /// Shape-parameter residual from thesis eq. 6.10 discretized in
    /// backward-Euler form on H* (same form the turbulent marcher's
    /// inner Newton drives to zero):
    ///   R_H = θ_i·(H*_i − H*_{i−1})/dξ
    ///        − [2·CD − H*·Cf/2 − (2·H**/H* + 1 − H)·θ·(dUe/dξ)/Ue]
    /// evaluated at station i.
    ///
    /// At Me = 0 the H** contribution vanishes (compressibility term).
    /// </summary>
    public static double ShapeParamResidual(
        double thetaPrev, double theta,
        double hPrev, double h,
        double cTau,
        double uePrev, double ue,
        double dx,
        double nu,
        double me = 0.0,
        bool isWake = false)
    {
        if (dx <= 0.0) throw new System.ArgumentOutOfRangeException(nameof(dx));
        double ueSafe = System.Math.Max(ue, 1e-12);
        double reTheta = ue * theta / System.Math.Max(nu, 1e-18);
        double reThetaPrev = uePrev * thetaPrev / System.Math.Max(nu, 1e-18);
        double hk = MsesClosureRelations.ComputeHk(h, me);
        double hkPrev = MsesClosureRelations.ComputeHk(hPrev, me);
        double hStar = MsesClosureRelations.ComputeHStarTurbulent(hk, reTheta, me);
        double hStarPrev = MsesClosureRelations.ComputeHStarTurbulent(
            hkPrev, reThetaPrev, me);
        double cf = isWake
            ? 0.0
            : MsesClosureRelations.ComputeCfTurbulent(hk, reTheta, me);
        double cd = MsesClosureRelations.ComputeCDTurbulent(hk, reTheta, me, cTau);
        double uDuDx_over_Ue = (ue - uePrev) / (dx * ueSafe);
        // Bracket(H) = 2·H**/H* + (1 − H). At Me=0, H** = 0.
        double bracket = 1.0 - h;  // H**/H* term skipped at Me=0
        double rhs = 2.0 * cd - hStar * cf * 0.5
                   - bracket * theta * uDuDx_over_Ue;
        return theta * (hStar - hStarPrev) / dx - rhs;
    }

    /// <summary>
    /// R5.6 — TE-merge residuals (thesis eq. 6.63 sharp-TE).
    /// At station 0 of the wake, the merged state comes from the
    /// airfoil's two TE values:
    ///   θ_wake0  = θ_u_TE + θ_l_TE
    ///   δ*_wake0 = δ*_u_TE + δ*_l_TE   (equivalently H·θ on each side)
    ///   Cτ_wake0 = (θ_u·Cτ_u + θ_l·Cτ_l) / (θ_u + θ_l)
    /// These constraint residuals pin the wake-start state to the
    /// sum/average of the upper and lower TE values.
    /// </summary>
    public static (double RTheta, double RDstar, double RCTau) TEMergeResiduals(
        double thetaUpperTE, double thetaLowerTE,
        double dStarUpperTE, double dStarLowerTE,
        double cTauUpperTE, double cTauLowerTE,
        double thetaWake0, double dStarWake0, double cTauWake0)
    {
        double thetaSum = thetaUpperTE + thetaLowerTE;
        double dStarSum = dStarUpperTE + dStarLowerTE;
        double cTauMerged = thetaSum > 1e-18
            ? (thetaUpperTE * cTauUpperTE + thetaLowerTE * cTauLowerTE) / thetaSum
            : 0.5 * (cTauUpperTE + cTauLowerTE);
        return (
            RTheta: thetaWake0 - thetaSum,
            RDstar: dStarWake0 - dStarSum,
            RCTau: cTauWake0 - cTauMerged);
    }

    /// <summary>
    /// P5.2 — σ = d(Ue·δ*)/dξ constraint residual (the definition
    /// of the displacement source strength). Evaluated as a
    /// first-difference per station:
    ///   R_σ[i] = σ_i − (Ue_i·δ*_i − Ue_{i−1}·δ*_{i−1}) / dξ
    /// At a converged state this should vanish — σ becomes the
    /// exact discrete derivative of the displacement flux.
    ///
    /// Note: in the layout where σ is stored at NODES (N+1 values)
    /// but BL state is at STATIONS, the σ at node i is paired with
    /// the panel [i−1, i]. Station 0 is the freestream entry where
    /// no prior exists; its residual convention is "anchor σ[0]=0"
    /// (or to whatever the user fixes).
    /// </summary>
    public static double SourceConstraintResidual(
        double sigma,
        double dStarPrev, double dStar,
        double uePrev, double ue,
        double dx)
    {
        if (dx <= 0.0) throw new System.ArgumentOutOfRangeException(nameof(dx));
        double flux0 = uePrev * dStarPrev;
        double flux1 = ue * dStar;
        return sigma - (flux1 - flux0) / dx;
    }

    /// <summary>
    /// Cτ lag residual from thesis eq. 6.35 (closed-form decay):
    ///   R_Cτ = Cτ_i − [Cτ_eq + (Cτ_{i−1} − Cτ_eq)·exp(−K2·dξ/δ)]
    /// at equilibrium-midpoint (Hk, Reθ) values. K2 = 4.2.
    /// </summary>
    public static double LagResidual(
        double cTauPrev, double cTau,
        double thetaPrev, double theta,
        double hPrev, double h,
        double uePrev, double ue,
        double dx,
        double nu,
        double me = 0.0)
    {
        if (dx <= 0.0) throw new System.ArgumentOutOfRangeException(nameof(dx));
        double ueMid = 0.5 * (uePrev + ue);
        double thetaMid = 0.5 * (thetaPrev + theta);
        double hMid = 0.5 * (hPrev + h);
        double hkMid = MsesClosureRelations.ComputeHk(hMid, me);
        double reTMid = ueMid * thetaMid / System.Math.Max(nu, 1e-18);
        double cTauEq = MsesClosureRelations.ComputeCTauEquilibrium(
            hkMid, reTMid, me);
        // δ(H, θ) per thesis eq. 6.36. Guard for H near 1.
        double deltaMid;
        if (hkMid > 1.05)
        {
            deltaMid = hkMid * thetaMid * (3.15 + 1.72 / (hkMid - 1.0));
        }
        else
        {
            // Near-stagnation fallback: large δ → exp(−K2·dx/δ) ≈ 1 →
            // Cτ stays at prev. Prevents divide-by-zero at Hk→1.
            deltaMid = hkMid * thetaMid * 100.0;
        }
        const double K2 = 4.2;
        double decay = System.Math.Exp(
            -K2 * dx / System.Math.Max(deltaMid, 1e-18));
        double cTauExpected = cTauEq + (cTauPrev - cTauEq) * decay;
        return cTau - cTauExpected;
    }
}
