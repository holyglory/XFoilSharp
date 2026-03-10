using System;

namespace XFoil.Solver.Services;

/// <summary>
/// Transition model for the e^N envelope method.
/// Port of DAMPL, DAMPL2, AXSET, and TRCHEK2 from xblsys.f.
/// Static utility class following Phase 2 convention.
/// </summary>
public static class TransitionModel
{
    /// <summary>
    /// Smooth onset ramp width in decades of log10(Re_theta).
    /// Source: xblsys.f DATA DGR / 0.08 /
    /// </summary>
    private const double DGR = 0.08;

    /// <summary>
    /// Natural logarithm of 10, used for log10 conversions.
    /// </summary>
    private const double Ln10 = 2.3025851;

    /// <summary>
    /// Convergence tolerance for TRCHEK2 transition Newton iteration.
    /// Source: xblsys.f DAEPS = 5e-5
    /// </summary>
    private const double DAEPS = 5e-5;

    /// <summary>
    /// Maximum iterations for TRCHEK2 transition Newton iteration.
    /// </summary>
    private const int MaxTransitionIterations = 30;

    /// <summary>
    /// Result type for AXSET (ComputeTransitionSensitivities).
    /// </summary>
    public readonly record struct AxsetResult(
        double Ax, double Ax_Hk1, double Ax_T1, double Ax_Rt1, double Ax_A1,
        double Ax_Hk2, double Ax_T2, double Ax_Rt2, double Ax_A2);

    /// <summary>
    /// Result type for TRCHEK2 (CheckTransition).
    /// </summary>
    public readonly record struct TransitionCheckResult(
        bool TransitionOccurred,
        double TransitionXi,
        double AmplAtTransition,
        TransitionResultType Type,
        bool Converged,
        int Iterations);

    /// <summary>
    /// Transition result classification.
    /// </summary>
    public enum TransitionResultType
    {
        /// <summary>No transition in this interval.</summary>
        None = 0,
        /// <summary>Free (natural) transition via e^N method.</summary>
        Free = 1,
        /// <summary>Forced transition at user-prescribed location.</summary>
        Forced = 2
    }

    // =====================================================================
    // DAMPL: Envelope spatial amplification rate
    // Source: xblsys.f:1980-2094
    // =====================================================================

    /// <summary>
    /// Computes the envelope spatial amplification rate dN/dx for the e^N method.
    /// Port of DAMPL from xblsys.f:1980-2094.
    /// Uses Falkner-Skan profiles for Hk correlation.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter.</param>
    /// <param name="th">Momentum thickness.</param>
    /// <param name="rt">Momentum-thickness Reynolds number (Re_theta).</param>
    /// <returns>(AX, dAX/dHk, dAX/dTh, dAX/dRt)</returns>
    public static (double Ax, double Ax_Hk, double Ax_Th, double Ax_Rt)
        ComputeAmplificationRate(double hk, double th, double rt)
    {
        // xblsys.f:2023-2024
        double hmi = 1.0 / (hk - 1.0);
        double hmi_hk = -hmi * hmi;

        // log10(Critical Rth) - H correlation for Falkner-Skan profiles
        // xblsys.f:2027-2034
        double aa = 2.492 * Math.Pow(hmi, 0.43);
        double aa_hk = (aa / hmi) * 0.43 * hmi_hk;

        double bb = Math.Tanh(14.0 * hmi - 9.24);
        double bb_hk = (1.0 - bb * bb) * 14.0 * hmi_hk;

        double grcrit = aa + 0.7 * (bb + 1.0);
        double grc_hk = aa_hk + 0.7 * bb_hk;

        // xblsys.f:2037-2038
        double gr = Math.Log10(rt);
        double gr_rt = 1.0 / (Ln10 * rt);

        if (gr < grcrit - DGR)
        {
            // No amplification for Rtheta < Rcrit
            // xblsys.f:2043-2046
            return (0.0, 0.0, 0.0, 0.0);
        }

        // Smooth cubic ramp to turn on AX as Rtheta exceeds Rcrit
        // xblsys.f:2054-2068
        double rnorm = (gr - (grcrit - DGR)) / (2.0 * DGR);
        double rn_hk = -grc_hk / (2.0 * DGR);
        double rn_rt = gr_rt / (2.0 * DGR);

        double rfac, rfac_hk, rfac_rt;
        if (rnorm >= 1.0)
        {
            rfac = 1.0;
            rfac_hk = 0.0;
            rfac_rt = 0.0;
        }
        else
        {
            rfac = 3.0 * rnorm * rnorm - 2.0 * rnorm * rnorm * rnorm;
            double rfac_rn = 6.0 * rnorm - 6.0 * rnorm * rnorm;
            rfac_hk = rfac_rn * rn_hk;
            rfac_rt = rfac_rn * rn_rt;
        }

        // Amplification envelope slope correlation for Falkner-Skan
        // xblsys.f:2071-2078
        double arg = 3.87 * hmi - 2.52;
        double arg_hk = 3.87 * hmi_hk;

        double ex = Math.Exp(-arg * arg);
        double ex_hk = ex * (-2.0 * arg * arg_hk);

        double dadr = 0.028 * (hk - 1.0) - 0.0345 * ex;
        double dadr_hk = 0.028 - 0.0345 * ex_hk;

        // m(H) correlation (1 March 91)
        // xblsys.f:2081-2083
        double af = -0.05 + 2.7 * hmi - 5.5 * hmi * hmi + 3.0 * hmi * hmi * hmi;
        double af_hmi = 2.7 - 11.0 * hmi + 9.0 * hmi * hmi;
        double af_hk = af_hmi * hmi_hk;

        // Final amplification rate with ramp
        // xblsys.f:2085-2089
        double ax = (af * dadr / th) * rfac;
        double ax_hk = (af_hk * dadr / th + af * dadr_hk / th) * rfac
                     + (af * dadr / th) * rfac_hk;
        double ax_th = -ax / th;
        double ax_rt = (af * dadr / th) * rfac_rt;

        return (ax, ax_hk, ax_th, ax_rt);
    }

    // =====================================================================
    // DAMPL2: Modified envelope amplification rate for separated profiles
    // Source: xblsys.f:2098-2271
    // =====================================================================

    /// <summary>
    /// Computes the modified envelope spatial amplification rate for separated profiles (Hk > 3.5).
    /// For Hk below 3.5, returns same as DAMPL. For Hk > 3.5, blends in Orr-Sommerfeld
    /// maximum amplification rate correction. Port of DAMPL2 from xblsys.f:2098-2271.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter.</param>
    /// <param name="th">Momentum thickness.</param>
    /// <param name="rt">Momentum-thickness Reynolds number (Re_theta).</param>
    /// <returns>(AX, dAX/dHk, dAX/dTh, dAX/dRt)</returns>
    public static (double Ax, double Ax_Hk, double Ax_Th, double Ax_Rt)
        ComputeAmplificationRateHighHk(double hk, double th, double rt)
    {
        // DAMPL2 blending bounds: xblsys.f:2139
        const double hk1Blend = 3.5;
        const double hk2Blend = 4.0;

        // Same critical Re computation as DAMPL
        // xblsys.f:2141-2156
        double hmi = 1.0 / (hk - 1.0);
        double hmi_hk = -hmi * hmi;

        double aa = 2.492 * Math.Pow(hmi, 0.43);
        double aa_hk = (aa / hmi) * 0.43 * hmi_hk;

        double bb = Math.Tanh(14.0 * hmi - 9.24);
        double bb_hk = (1.0 - bb * bb) * 14.0 * hmi_hk;

        double grc = aa + 0.7 * (bb + 1.0);
        double grc_hk = aa_hk + 0.7 * bb_hk;

        double gr = Math.Log10(rt);
        double gr_rt = 1.0 / (Ln10 * rt);

        if (gr < grc - DGR)
        {
            // No amplification for Rtheta < Rcrit
            return (0.0, 0.0, 0.0, 0.0);
        }

        // Smooth onset ramp (same as DAMPL)
        // xblsys.f:2172-2186
        double rnorm = (gr - (grc - DGR)) / (2.0 * DGR);
        double rn_hk = -grc_hk / (2.0 * DGR);
        double rn_rt = gr_rt / (2.0 * DGR);

        double rfac, rfac_hk, rfac_rt;
        if (rnorm >= 1.0)
        {
            rfac = 1.0;
            rfac_hk = 0.0;
            rfac_rt = 0.0;
        }
        else
        {
            rfac = 3.0 * rnorm * rnorm - 2.0 * rnorm * rnorm * rnorm;
            double rfac_rn = 6.0 * rnorm - 6.0 * rnorm * rnorm;
            rfac_hk = rfac_rn * rn_hk;
            rfac_rt = rfac_rn * rn_rt;
        }

        // Envelope amplification rate (same as DAMPL)
        // xblsys.f:2192-2218
        double arg = 3.87 * hmi - 2.52;
        double arg_hk = 3.87 * hmi_hk;

        double ex = Math.Exp(-arg * arg);
        double ex_hk = ex * (-2.0 * arg * arg_hk);

        double dadr = 0.028 * (hk - 1.0) - 0.0345 * ex;
        double dadr_hk = 0.028 - 0.0345 * ex_hk;

        // DAMPL2 has additional exp(-20*HMI) term in AF
        // xblsys.f:2205-2208
        double brg = -20.0 * hmi;
        double af = -0.05 + 2.7 * hmi - 5.5 * hmi * hmi + 3.0 * hmi * hmi * hmi + 0.1 * Math.Exp(brg);
        double af_hmi = 2.7 - 11.0 * hmi + 9.0 * hmi * hmi - 2.0 * Math.Exp(brg);
        double af_hk = af_hmi * hmi_hk;

        double ax = (af * dadr / th) * rfac;
        double ax_hk = (af_hk * dadr / th + af * dadr_hk / th) * rfac
                     + (af * dadr / th) * rfac_hk;
        double ax_th = -ax / th;
        double ax_rt = (af * dadr / th) * rfac_rt;

        // If Hk < HK1 (3.5), return standard envelope rate (no blending)
        // xblsys.f:2222
        if (hk < hk1Blend)
        {
            return (ax, ax_hk, ax_th, ax_rt);
        }

        // Non-envelope max-amplification correction for separated profiles
        // xblsys.f:2226-2268

        // Blending fraction HFAC = 0..1 over HK1 < HK < HK2
        double hnorm = (hk - hk1Blend) / (hk2Blend - hk1Blend);
        double hn_hk = 1.0 / (hk2Blend - hk1Blend);

        double hfac, hf_hk;
        if (hnorm >= 1.0)
        {
            hfac = 1.0;
            hf_hk = 0.0;
        }
        else
        {
            hfac = 3.0 * hnorm * hnorm - 2.0 * hnorm * hnorm * hnorm;
            hf_hk = (6.0 * hnorm - 6.0 * hnorm * hnorm) * hn_hk;
        }

        // Save "normal" envelope rate as AX1
        double ax1 = ax;
        double ax1_hk = ax_hk;
        double ax1_th = ax_th;
        double ax1_rt = ax_rt;

        // Modified amplification rate AX2
        // xblsys.f:2245-2255
        double gr0 = 0.30 + 0.35 * Math.Exp(-0.15 * (hk - 5.0));
        double gr0_hk = -0.35 * Math.Exp(-0.15 * (hk - 5.0)) * 0.15;

        double tnr = Math.Tanh(1.2 * (gr - gr0));
        double tnr_rt = (1.0 - tnr * tnr) * 1.2 * gr_rt;
        double tnr_hk = -(1.0 - tnr * tnr) * 1.2 * gr0_hk;

        double hkm1pow15 = Math.Pow(hk - 1.0, 1.5);
        double hkm1pow25 = Math.Pow(hk - 1.0, 2.5);

        double ax2 = (0.086 * tnr - 0.25 / hkm1pow15) / th;
        double ax2_hk = (0.086 * tnr_hk + 1.5 * 0.25 / hkm1pow25) / th;
        double ax2_rt = (0.086 * tnr_rt) / th;
        double ax2_th = -ax2 / th;

        // Clamp AX2 to non-negative
        // xblsys.f:2257-2262
        if (ax2 < 0.0)
        {
            ax2 = 0.0;
            ax2_hk = 0.0;
            ax2_rt = 0.0;
            ax2_th = 0.0;
        }

        // Blend the two amplification rates
        // xblsys.f:2265-2268
        ax = hfac * ax2 + (1.0 - hfac) * ax1;
        ax_hk = hfac * ax2_hk + (1.0 - hfac) * ax1_hk + hf_hk * (ax2 - ax1);
        ax_rt = hfac * ax2_rt + (1.0 - hfac) * ax1_rt;
        ax_th = hfac * ax2_th + (1.0 - hfac) * ax1_th;

        return (ax, ax_hk, ax_th, ax_rt);
    }

    // =====================================================================
    // AXSET: Combined amplification rate dispatch
    // Source: xblsys.f:35-144
    // =====================================================================

    /// <summary>
    /// Returns the average amplification rate AX over interval 1..2,
    /// dispatching between DAMPL and DAMPL2 based on useHighHkModel flag (IDAMPV).
    /// Uses RMS averaging of station 1 and station 2 rates plus a small
    /// additional term to ensure dN/dx > 0 near N = Ncrit.
    /// Port of AXSET from xblsys.f:35-144.
    /// </summary>
    /// <param name="hk1">Kinematic shape parameter at station 1.</param>
    /// <param name="t1">Momentum thickness at station 1.</param>
    /// <param name="rt1">Re_theta at station 1.</param>
    /// <param name="a1">Amplification N at station 1.</param>
    /// <param name="hk2">Kinematic shape parameter at station 2.</param>
    /// <param name="t2">Momentum thickness at station 2.</param>
    /// <param name="rt2">Re_theta at station 2.</param>
    /// <param name="a2">Amplification N at station 2.</param>
    /// <param name="acrit">Critical amplification factor N_crit.</param>
    /// <param name="useHighHkModel">If true, use DAMPL2; if false, use DAMPL (IDAMPV flag).</param>
    /// <returns>AxsetResult with AX and all sensitivities.</returns>
    public static AxsetResult ComputeTransitionSensitivities(
        double hk1, double t1, double rt1, double a1,
        double hk2, double t2, double rt2, double a2,
        double acrit, bool useHighHkModel)
    {
        // Dispatch to DAMPL or DAMPL2
        // xblsys.f:70-76
        double ax1, ax1_hk1, ax1_t1, ax1_rt1;
        double ax2, ax2_hk2, ax2_t2, ax2_rt2;

        if (!useHighHkModel)
        {
            (ax1, ax1_hk1, ax1_t1, ax1_rt1) = ComputeAmplificationRate(hk1, t1, rt1);
            (ax2, ax2_hk2, ax2_t2, ax2_rt2) = ComputeAmplificationRate(hk2, t2, rt2);
        }
        else
        {
            (ax1, ax1_hk1, ax1_t1, ax1_rt1) = ComputeAmplificationRateHighHk(hk1, t1, rt1);
            (ax2, ax2_hk2, ax2_t2, ax2_rt2) = ComputeAmplificationRateHighHk(hk2, t2, rt2);
        }

        // RMS-average version (better on coarse grids per Fortran comment)
        // xblsys.f:90-99
        double axsq = 0.5 * (ax1 * ax1 + ax2 * ax2);
        double axa, axa_ax1, axa_ax2;
        if (axsq <= 0.0)
        {
            axa = 0.0;
            axa_ax1 = 0.0;
            axa_ax2 = 0.0;
        }
        else
        {
            axa = Math.Sqrt(axsq);
            axa_ax1 = 0.5 * ax1 / axa;
            axa_ax2 = 0.5 * ax2 / axa;
        }

        // Small additional term to ensure dN/dx > 0 near N = Ncrit
        // xblsys.f:102-120
        double argVal = Math.Min(20.0 * (acrit - 0.5 * (a1 + a2)), 20.0);
        double exn, exn_a1, exn_a2;
        if (argVal <= 0.0)
        {
            exn = 1.0;
            exn_a1 = 0.0;
            exn_a2 = 0.0;
        }
        else
        {
            exn = Math.Exp(-argVal);
            exn_a1 = 20.0 * 0.5 * exn;
            exn_a2 = 20.0 * 0.5 * exn;
        }

        double dax = exn * 0.002 / (t1 + t2);
        double dax_a1 = exn_a1 * 0.002 / (t1 + t2);
        double dax_a2 = exn_a2 * 0.002 / (t1 + t2);
        double dax_t1 = -dax / (t1 + t2);
        double dax_t2 = -dax / (t1 + t2);

        // Final combined result
        // xblsys.f:131-141
        double axFinal = axa + dax;

        double ax_hk1_final = axa_ax1 * ax1_hk1;
        double ax_t1_final = axa_ax1 * ax1_t1 + dax_t1;
        double ax_rt1_final = axa_ax1 * ax1_rt1;
        double ax_a1_final = dax_a1;

        double ax_hk2_final = axa_ax2 * ax2_hk2;
        double ax_t2_final = axa_ax2 * ax2_t2 + dax_t2;
        double ax_rt2_final = axa_ax2 * ax2_rt2;
        double ax_a2_final = dax_a2;

        return new AxsetResult(
            axFinal,
            ax_hk1_final, ax_t1_final, ax_rt1_final, ax_a1_final,
            ax_hk2_final, ax_t2_final, ax_rt2_final, ax_a2_final);
    }
}
