using System;

namespace XFoil.Solver.Services;

/// <summary>
/// Boundary layer correlation functions ported from XFoil's xblsys.f.
/// Each function returns its value and all Jacobian sensitivities needed by the Newton system.
/// Static utility class following Phase 2 convention.
/// </summary>
public static class BoundaryLayerCorrelations
{
    /// <summary>
    /// Default CFFAC (skin friction factor) value. Matches XFoil's initialization in xbl.f:1577.
    /// </summary>
    private const double DefaultCfFac = 1.0;

    /// <summary>
    /// Ratio of specific heats for air.
    /// </summary>
    private const double Gamma = 1.4;

    // =====================================================================
    // HKIN: Kinematic shape parameter (from Whitfield)
    // Source: xblsys.f:2275-2285
    // =====================================================================

    /// <summary>
    /// Computes the kinematic shape parameter Hk from the incompressible shape parameter H
    /// and the local Mach number squared, including compressibility correction (Whitfield).
    /// </summary>
    /// <param name="h">Incompressible shape parameter H = delta*/theta.</param>
    /// <param name="msq">Local Mach number squared.</param>
    /// <returns>(Hk, dHk/dH, dHk/dMsq)</returns>
    public static (double Hk, double Hk_H, double Hk_Msq) KinematicShapeParameter(double h, double msq)
    {
        // HK = (H - 0.29*MSQ) / (1.0 + 0.113*MSQ)
        double denom = 1.0 + 0.113 * msq;
        double hk = (h - 0.29 * msq) / denom;
        double hk_h = 1.0 / denom;
        double hk_msq = (-0.29 - 0.113 * hk) / denom;

        return (hk, hk_h, hk_msq);
    }

    // =====================================================================
    // HSL: Laminar shape parameter (H*)
    // Source: xblsys.f:2326-2348
    // =====================================================================

    /// <summary>
    /// Computes the laminar energy thickness shape parameter H*.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter Hk.</param>
    /// <returns>(Hs, dHs/dHk, dHs/dRt, dHs/dMsq) -- Rt and Msq sensitivities are zero for laminar.</returns>
    public static (double Hs, double Hs_Hk, double Hs_Rt, double Hs_Msq) LaminarShapeParameter(double hk)
    {
        double hs, hs_hk;

        if (hk < 4.35)
        {
            double tmp = hk - 4.35;
            hs = 0.0111 * tmp * tmp / (hk + 1.0)
               - 0.0278 * tmp * tmp * tmp / (hk + 1.0)
               + 1.528
               - 0.0002 * (tmp * hk) * (tmp * hk);

            hs_hk = 0.0111 * (2.0 * tmp - tmp * tmp / (hk + 1.0)) / (hk + 1.0)
                  - 0.0278 * (3.0 * tmp * tmp - tmp * tmp * tmp / (hk + 1.0)) / (hk + 1.0)
                  - 0.0002 * 2.0 * tmp * hk * (tmp + hk);
        }
        else
        {
            hs = 0.015 * (hk - 4.35) * (hk - 4.35) / hk + 1.528;
            hs_hk = 0.015 * 2.0 * (hk - 4.35) / hk
                   - 0.015 * (hk - 4.35) * (hk - 4.35) / (hk * hk);
        }

        return (hs, hs_hk, 0.0, 0.0);
    }

    // =====================================================================
    // HST: Turbulent shape parameter (H*)
    // Source: xblsys.f:2385-2476
    // =====================================================================

    /// <summary>
    /// Computes the turbulent energy thickness shape parameter H*.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter Hk.</param>
    /// <param name="rt">Momentum-thickness Reynolds number Rtheta.</param>
    /// <param name="msq">Local Mach number squared.</param>
    /// <returns>(Hs, dHs/dHk, dHs/dRt, dHs/dMsq)</returns>
    public static (double Hs, double Hs_Hk, double Hs_Rt, double Hs_Msq) TurbulentShapeParameter(
        double hk, double rt, double msq)
    {
        const double HsMin = 1.500;
        const double DHsInf = 0.015;

        // Limited Rtheta dependence for Rtheta < 200 (12/4/94 correction)
        double ho, ho_rt;
        if (rt > 400.0)
        {
            ho = 3.0 + 400.0 / rt;
            ho_rt = -400.0 / (rt * rt);
        }
        else
        {
            ho = 4.0;
            ho_rt = 0.0;
        }

        double rtz, rtz_rt;
        if (rt > 200.0)
        {
            rtz = rt;
            rtz_rt = 1.0;
        }
        else
        {
            rtz = 200.0;
            rtz_rt = 0.0;
        }

        double hs, hs_hk, hs_rt;

        if (hk < ho)
        {
            // Attached branch (new correlation 29 Nov 91)
            double hr = (ho - hk) / (ho - 1.0);
            double hr_hk = -1.0 / (ho - 1.0);
            double hr_rt = (1.0 - hr) / (ho - 1.0) * ho_rt;

            hs = (2.0 - HsMin - 4.0 / rtz) * hr * hr * 1.5 / (hk + 0.5) + HsMin
               + 4.0 / rtz;
            hs_hk = -(2.0 - HsMin - 4.0 / rtz) * hr * hr * 1.5 / ((hk + 0.5) * (hk + 0.5))
                   + (2.0 - HsMin - 4.0 / rtz) * hr * 2.0 * 1.5 / (hk + 0.5) * hr_hk;
            hs_rt = (2.0 - HsMin - 4.0 / rtz) * hr * 2.0 * 1.5 / (hk + 0.5) * hr_rt
                   + (hr * hr * 1.5 / (hk + 0.5) - 1.0) * 4.0 / (rtz * rtz) * rtz_rt;
        }
        else
        {
            // Separated branch
            double grt = Math.Log(rtz);
            double hdif = hk - ho;
            double rtmp = hk - ho + 4.0 / grt;
            double htmp = 0.007 * grt / (rtmp * rtmp) + DHsInf / hk;
            double htmp_hk = -0.014 * grt / (rtmp * rtmp * rtmp) - DHsInf / (hk * hk);
            double htmp_rt = -0.014 * grt / (rtmp * rtmp * rtmp) * (-ho_rt - 4.0 / (grt * grt) / rtz * rtz_rt)
                           + 0.007 / (rtmp * rtmp) / rtz * rtz_rt;

            hs = hdif * hdif * htmp + HsMin + 4.0 / rtz;
            hs_hk = hdif * 2.0 * htmp
                   + hdif * hdif * htmp_hk;
            hs_rt = hdif * hdif * htmp_rt - 4.0 / (rtz * rtz) * rtz_rt
                   + hdif * 2.0 * htmp * (-ho_rt);
        }

        // Whitfield's minor additional compressibility correction
        double fm = 1.0 + 0.014 * msq;
        hs = (hs + 0.028 * msq) / fm;
        hs_hk = hs_hk / fm;
        hs_rt = hs_rt / fm;
        double hs_msq = 0.028 / fm - 0.014 * hs / fm;

        return (hs, hs_hk, hs_rt, hs_msq);
    }

    // =====================================================================
    // CFL: Laminar skin friction (Cf) from Falkner-Skan
    // Source: xblsys.f:2351-2368
    // =====================================================================

    /// <summary>
    /// Computes the laminar skin friction coefficient from Falkner-Skan profiles.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter Hk.</param>
    /// <param name="rt">Momentum-thickness Reynolds number Rtheta.</param>
    /// <param name="msq">Local Mach number squared.</param>
    /// <returns>(Cf, dCf/dHk, dCf/dRt, dCf/dMsq)</returns>
    public static (double Cf, double Cf_Hk, double Cf_Rt, double Cf_Msq) LaminarSkinFriction(
        double hk, double rt, double msq)
    {
        double cf, cf_hk;

        if (hk < 5.5)
        {
            double tmp = Math.Pow(5.5 - hk, 3) / (hk + 1.0);
            cf = (0.0727 * tmp - 0.07) / rt;
            cf_hk = (-0.0727 * tmp * 3.0 / (5.5 - hk) - 0.0727 * tmp / (hk + 1.0)) / rt;
        }
        else
        {
            double tmp = 1.0 - 1.0 / (hk - 4.5);
            cf = (0.015 * tmp * tmp - 0.07) / rt;
            cf_hk = (0.015 * tmp * 2.0 / ((hk - 4.5) * (hk - 4.5))) / rt;
        }

        double cf_rt = -cf / rt;
        double cf_msq = 0.0;

        return (cf, cf_hk, cf_rt, cf_msq);
    }

    // =====================================================================
    // CFT: Turbulent skin friction (Cf) from Coles
    // Source: xblsys.f:2480-2507
    // =====================================================================

    /// <summary>
    /// Computes the turbulent skin friction coefficient using Coles' law of the wall/wake.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter Hk.</param>
    /// <param name="rt">Momentum-thickness Reynolds number Rtheta.</param>
    /// <param name="msq">Local Mach number squared.</param>
    /// <param name="cfFac">Skin friction scale factor (CFFAC from BLPAR.INC). Default 1.0.</param>
    /// <returns>(Cf, dCf/dHk, dCf/dRt, dCf/dMsq)</returns>
    public static (double Cf, double Cf_Hk, double Cf_Rt, double Cf_Msq) TurbulentSkinFriction(
        double hk, double rt, double msq, double cfFac = DefaultCfFac)
    {
        double gm1 = Gamma - 1.0;
        double fc = Math.Sqrt(1.0 + 0.5 * gm1 * msq);
        double grt = Math.Log(rt / fc);
        grt = Math.Max(grt, 3.0);

        double gex = -1.74 - 0.31 * hk;

        double arg = -1.33 * hk;
        arg = Math.Max(-20.0, arg);

        double thk = Math.Tanh(4.0 - hk / 0.875);

        double cfo = cfFac * 0.3 * Math.Exp(arg) * Math.Pow(grt / 2.3026, gex);
        double cf = (cfo + 1.1e-4 * (thk - 1.0)) / fc;

        double cf_hk = (-1.33 * cfo - 0.31 * Math.Log(grt / 2.3026) * cfo
                       - 1.1e-4 * (1.0 - thk * thk) / 0.875) / fc;

        double cf_rt, cf_msq;

        // Only compute Rt/Msq sensitivities when GRT > 3.0 (otherwise clamped)
        if (Math.Log(rt / fc) > 3.0)
        {
            cf_rt = gex * cfo / (fc * grt) / rt;
            cf_msq = gex * cfo / (fc * grt) * (-0.25 * gm1 / (fc * fc)) - 0.25 * gm1 * cf / (fc * fc);
        }
        else
        {
            // GRT is clamped at 3.0, so Rt/Msq sensitivities through GRT vanish
            cf_rt = 0.0;
            cf_msq = -0.25 * gm1 * cf / (fc * fc);
        }

        return (cf, cf_hk, cf_rt, cf_msq);
    }

    // =====================================================================
    // DIL: Laminar dissipation function (2*CD/H*) from Falkner-Skan
    // Source: xblsys.f:2289-2304
    // =====================================================================

    /// <summary>
    /// Computes the laminar dissipation coefficient 2*CD/H* from Falkner-Skan profiles.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter Hk.</param>
    /// <param name="rt">Momentum-thickness Reynolds number Rtheta.</param>
    /// <returns>(Di, dDi/dHk, dDi/dRt)</returns>
    public static (double Di, double Di_Hk, double Di_Rt) LaminarDissipation(double hk, double rt)
    {
        double di, di_hk;

        if (hk < 4.0)
        {
            di = (0.00205 * Math.Pow(4.0 - hk, 5.5) + 0.207) / rt;
            di_hk = (-0.00205 * 5.5 * Math.Pow(4.0 - hk, 4.5)) / rt;
        }
        else
        {
            double hkb = hk - 4.0;
            double den = 1.0 + 0.02 * hkb * hkb;
            di = (-0.0016 * hkb * hkb / den + 0.207) / rt;
            di_hk = (-0.0016 * 2.0 * hkb * (1.0 / den - 0.02 * hkb * hkb / (den * den))) / rt;
        }

        double di_rt = -di / rt;

        return (di, di_hk, di_rt);
    }

    // =====================================================================
    // DILW: Laminar wake dissipation function (2*CD/H*)
    // Source: xblsys.f:2307-2323
    // =====================================================================

    /// <summary>
    /// Computes the laminar wake dissipation coefficient 2*CD/H*.
    /// Uses HSL internally for the laminar shape parameter.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter Hk.</param>
    /// <param name="rt">Momentum-thickness Reynolds number Rtheta.</param>
    /// <returns>(Di, dDi/dHk, dDi/dRt)</returns>
    public static (double Di, double Di_Hk, double Di_Rt) WakeDissipation(double hk, double rt)
    {
        // Call HSL with MSQ=0 (wake is incompressible in XFoil's formulation)
        var (hs, hs_hk, hs_rt, _) = LaminarShapeParameter(hk);

        // RCD = 1.10 * (1 - 1/Hk)^2 / Hk = 1.10 * (Hk-1)^2 / Hk^3
        // Note: Fortran xblsys.f:2315 has a sign error in RCD_HK (negative instead of positive
        // on the first term). Here we use the mathematically correct derivative:
        // d/dHk[(Hk-1)^2/Hk^3] = (Hk-1)(3-Hk)/Hk^4
        double rcd = 1.10 * Math.Pow(1.0 - 1.0 / hk, 2) / hk;
        double rcd_hk = 1.10 * (hk - 1.0) * (3.0 - hk) / (hk * hk * hk * hk);

        // DI = 2*RCD / (HS*RT)
        double di = 2.0 * rcd / (hs * rt);
        double di_hk = 2.0 * rcd_hk / (hs * rt) - (di / hs) * hs_hk;
        double di_rt = -di / rt - (di / hs) * hs_rt;

        return (di, di_hk, di_rt);
    }

    // =====================================================================
    // HCT: Density thickness shape parameter (from Whitfield)
    // Source: xblsys.f:2511-2520
    // =====================================================================

    /// <summary>
    /// Computes the density thickness shape parameter HC from Whitfield's correlation.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter Hk.</param>
    /// <param name="msq">Local Mach number squared.</param>
    /// <returns>(Hc, dHc/dHk, dHc/dMsq)</returns>
    public static (double Hc, double Hc_Hk, double Hc_Msq) DensityThicknessShapeParameter(
        double hk, double msq)
    {
        double hc = msq * (0.064 / (hk - 0.8) + 0.251);
        double hc_hk = msq * (-0.064 / ((hk - 0.8) * (hk - 0.8)));
        double hc_msq = 0.064 / (hk - 0.8) + 0.251;

        return (hc, hc_hk, hc_msq);
    }

    // =====================================================================
    // DIT: Turbulent dissipation function (2*CD/H*)
    // Source: xblsys.f:2372-2382
    // =====================================================================

    /// <summary>
    /// Computes the turbulent dissipation coefficient 2*CD/H*.
    /// </summary>
    /// <param name="hs">Energy thickness shape parameter H*.</param>
    /// <param name="us">Normalized edge velocity Us = 1 - Ue_wake/Ue (slip velocity ratio).</param>
    /// <param name="cf">Skin friction coefficient Cf.</param>
    /// <param name="st">Shear stress coefficient sqrt(Ctau/2).</param>
    /// <returns>(Di, dDi/dHs, dDi/dUs, dDi/dCf, dDi/dSt)</returns>
    public static (double Di, double Di_Hs, double Di_Us, double Di_Cf, double Di_St) TurbulentDissipation(
        double hs, double us, double cf, double st)
    {
        double di = (0.5 * cf * us + st * st * (1.0 - us)) * 2.0 / hs;
        double di_hs = -(0.5 * cf * us + st * st * (1.0 - us)) * 2.0 / (hs * hs);
        double di_us = (0.5 * cf - st * st) * 2.0 / hs;
        double di_cf = (0.5 * us) * 2.0 / hs;
        double di_st = (2.0 * st * (1.0 - us)) * 2.0 / hs;

        return (di, di_hs, di_us, di_cf, di_st);
    }

    // =====================================================================
    // EquilibriumShearCoefficient: Equilibrium Ctau from BLVAR
    // Source: xblsys.f:860-883 (inline in BLVAR)
    //
    // CQ = sqrt( CTCON * HS * HKB * HKC^2 / (USB * H * HK^2) )
    //
    // where HKB = HK-1, HKC = HK-1-GCC/RT (clamped >= 0.01),
    //       USB = 1-US.
    //
    // This standalone version takes the fully resolved intermediate values
    // (hk, hs, us, h) and uses the default CTCON = 0.5/(GA^2*GB) from
    // BoundaryLayerCorrelationConstants.Default.
    // =====================================================================

    /// <summary>
    /// Computes the equilibrium shear stress coefficient Ctau_eq from the turbulent BL correlations.
    /// Port of the CQ2 computation from BLVAR in xblsys.f.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter Hk.</param>
    /// <param name="hs">Energy thickness shape parameter H*.</param>
    /// <param name="us">Normalized slip velocity Us (0 on surface, nonzero in wake).</param>
    /// <param name="h">Incompressible shape parameter H = delta*/theta.</param>
    /// <param name="ctcon">Equilibrium Ctau constant (CTCON from BLPAR). Default from BoundaryLayerCorrelationConstants.</param>
    /// <param name="gccon">Green's lag constant (GCCON). Default 18.0.</param>
    /// <param name="rt">Reynolds number based on theta (for HKC computation). Default 10000.</param>
    /// <returns>(Cteq, dCteq/dHk, dCteq/dUs, dCteq/dHs, dCteq/dH)</returns>
    public static (double Cteq, double Cteq_Hk, double Cteq_Us, double Cteq_Hs, double Cteq_H)
        EquilibriumShearCoefficient(
            double hk, double hs, double us, double h,
            double ctcon = 0.01486326, // 0.5 / (6.7^2 * 0.75) from BLPAR defaults
            double gccon = 18.0,
            double rt = 10000.0)
    {
        double hkb = hk - 1.0;
        if (hkb < 0.01) hkb = 0.01;

        double hkc = hk - 1.0 - gccon / rt;
        double hkc_hk = 1.0;
        if (hkc < 0.01)
        {
            hkc = 0.01;
            hkc_hk = 0.0;
        }

        double usb = 1.0 - us;
        if (usb < 0.01) usb = 0.01;

        // CQ = sqrt( CTCON * HS * HKB * HKC^2 / (USB * H * HK^2) )
        double num = ctcon * hs * hkb * hkc * hkc;
        double den = usb * h * hk * hk;

        // Guard against negative argument under sqrt
        double ratio = num / den;
        if (ratio < 1e-20) ratio = 1e-20;

        double cteq = Math.Sqrt(ratio);

        // Partial derivatives via chain rule: d(sqrt(f))/dx = 0.5/sqrt(f) * df/dx
        double halfOverCteq = 0.5 / cteq;

        // d(ratio)/dHk = ctcon*hs/(usb*h) * [ hkc^2/(hk^2) + hkb*2*hkc*hkc_hk/(hk^2) - hkb*hkc^2*2/(hk^3) ]
        // Using product rule on hkb * hkc^2 / hk^2:
        // d/dHk( hkb * hkc^2 / hk^2 ) = hkc^2/hk^2 + hkb*2*hkc*hkc_hk/hk^2 - 2*hkb*hkc^2/hk^3
        //   (since d(hkb)/dHk = 1 when hkb is not clamped, or 0 when clamped)
        double dhkb_dhk = (hk - 1.0 >= 0.01) ? 1.0 : 0.0;
        double dRatio_dHk = ctcon * hs / (usb * h) * (
            dhkb_dhk * hkc * hkc / (hk * hk)
            + hkb * 2.0 * hkc * hkc_hk / (hk * hk)
            - hkb * hkc * hkc * 2.0 / (hk * hk * hk));
        double cteq_hk = halfOverCteq * dRatio_dHk;

        // d(ratio)/dHs = ctcon * hkb * hkc^2 / (usb * h * hk^2)
        double dRatio_dHs = ctcon * hkb * hkc * hkc / (usb * h * hk * hk);
        double cteq_hs = halfOverCteq * dRatio_dHs;

        // d(ratio)/dUs = ctcon * hs * hkb * hkc^2 / (h * hk^2) * (1 / usb^2)
        //   since d(1/usb)/d(us) = 1/usb^2  (usb = 1-us, d(usb)/d(us) = -1)
        double dUsb_dUs = (1.0 - us >= 0.01) ? -1.0 : 0.0;
        double dRatio_dUs = -num / (usb * usb * h * hk * hk) * dUsb_dUs;
        double cteq_us = halfOverCteq * dRatio_dUs;

        // d(ratio)/dH = -ctcon * hs * hkb * hkc^2 / (usb * h^2 * hk^2)
        double dRatio_dH = -num / (usb * h * h * hk * hk);
        double cteq_h = halfOverCteq * dRatio_dH;

        return (cteq, cteq_hk, cteq_us, cteq_hs, cteq_h);
    }
}
