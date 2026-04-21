using System;
using XFoil.Core.Numerics;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: HKIN/HSL/HST/CFL/CFT/DIL/DILW/HCT/DIT
// Secondary legacy source(s): f_xfoil/src/xblsys.f :: inline BLVAR CQ relation
// Role in port: Scalar boundary-layer correlation kernels used by BLKIN/BLVAR/BLDIF and the viscous seed march.
// Differences: The managed port keeps each correlation as a focused function with typed return tuples, and it adds explicit parity-only overloads where the legacy REAL evaluation order must be replayed exactly instead of relying on implicit single-precision behavior.
// Decision: Keep the split managed kernels for readability and reuse, but preserve legacy arithmetic staging only in the parity overloads and in places where the original formulas are the solver fidelity reference.

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
    // Legacy mapping: f_xfoil/src/xblsys.f :: HKIN
    // Difference from legacy: The algebra is the same, but the managed port returns a tuple instead of filling shared arrays.
    // Decision: Keep the tuple return and preserve the original Whitfield formula.
    public static (double Hk, double Hk_H, double Hk_Msq) KinematicShapeParameter(double h, double msq)
    {
        // HK = (H - 0.29*MSQ) / (1.0 + 0.113*MSQ)
        double denom = 1.0 + 0.113 * msq;
        double hk = (h - 0.29 * msq) / denom;
        double hk_h = 1.0 / denom;
        double hk_msq = (-0.29 - 0.113 * hk) / denom;

        return (hk, hk_h, hk_msq);
    }

    // Legacy mapping: f_xfoil/src/xblsys.f :: HKIN
    // Difference from legacy: This overload exists only to replay the legacy REAL staging explicitly when parity mode is enabled.
    // Decision: Keep both overloads; the parity branch must preserve the single-precision order while the default path stays simple.
    public static (double Hk, double Hk_H, double Hk_Msq) KinematicShapeParameter(
        double h,
        double msq,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. The 2-arg overload remains modern double.
        float hf = (float)h;
        float msqf = (float)msq;
        // HKIN is a small algebraic kernel, but the parity path still needs the
        // exact REAL staging because Hk and dHk/dH flow straight into MRCHUE and
        // BLSYS row assembly. Leaving it on double was enough to move row33 by 1 ULP.
        float denom = LegacyPrecisionMath.ProductThenAdd(0.113f, msqf, 1.0f);
        float numerator = LegacyPrecisionMath.SeparateMultiplySubtract(0.29f, msqf, hf);
        float hkf = numerator / denom;
        float hkHf = 1.0f / denom;
        // Fortran: HK_MSQ = (-0.29 - 0.113*HK) / DENOM, all in REAL.
        // Use the rounded hkf to match Fortran's REAL staging.
        float hkMsNumerator = -0.29f - (0.113f * hkf);
        float hkMsqf = hkMsNumerator / denom;
        return (hkf, hkHf, hkMsqf);
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: HSL
    // Difference from legacy: The correlation is unchanged, but the managed port exposes the derivative tuple directly.
    // Decision: Keep the tuple return and preserve the legacy branch formulas across the two Hk regimes.
    public static (double Hs, double Hs_Hk, double Hs_Rt, double Hs_Msq) LaminarShapeParameter(double hk)
    {
        double hs, hs_hk;

        if (hk < 4.35)
        {
            double tmp = hk - 4.35;
            hs = 0.0111 * tmp * tmp / (hk + 1.0)
               - 0.0278 * tmp * tmp * tmp / (hk + 1.0)
               + 1.528
               - 0.0002 * ((tmp * hk) * (tmp * hk));

            hs_hk = 0.0111 * (2.0 * tmp - tmp * tmp / (hk + 1.0)) / (hk + 1.0)
                  - 0.0278 * (3.0 * tmp * tmp - tmp * tmp * tmp / (hk + 1.0)) / (hk + 1.0)
                  - 0.0002 * 2.0 * tmp * hk * (tmp + hk);
        }
        else
        {
            hs = 0.015 * ((hk - 4.35) * (hk - 4.35)) / hk + 1.528;
            hs_hk = 0.015 * 2.0 * (hk - 4.35) / hk
                   - 0.015 * ((hk - 4.35) * (hk - 4.35)) / (hk * hk);
        }

        return (hs, hs_hk, 0.0, 0.0);
    }

    // Legacy mapping: f_xfoil/src/xblsys.f :: HSL
    // Difference from legacy: This parity overload replays the native REAL staging and selected fused operations explicitly instead of depending on the managed runtime to match them.
    // Decision: Keep the parity overload because HSL is one of the early correlation boundaries in binary trace matching.
    public static (double Hs, double Hs_Hk, double Hs_Rt, double Hs_Msq) LaminarShapeParameter(
        double hk,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. The 1-arg overload remains modern.
        float hkf = (float)hk;
        float hsf;
        float hsHkf;
        float tmpTrace = 0.0f;
        float hkPlusOneTrace = 0.0f;
        float hsHkTerm1Trace = 0.0f;
        float hsHkTerm2Trace = 0.0f;
        float hsHkTerm3Trace = 0.0f;
        float hsHkDirectTrace = 0.0f;
        float hsHkDirectPlainTerm2Trace = 0.0f;
        float hsHkDirectLeftTerm3Trace = 0.0f;
        float hsHkDirectTmpHkTerm3Trace = 0.0f;

        if (hkf < 4.35f)
        {
            float tmp = hkf - 4.35f;
            float hkPlusOne = hkf + 1.0f;
            float tmpSq = tmp * tmp;
            float tmpCube = tmpSq * tmp;
            float tmpHk = tmp * hkf;
            hsf = ((0.0111f * tmpSq) / hkPlusOne)
                - ((0.0278f * tmpCube) / hkPlusOne)
                + 1.528f
                - (0.0002f * (tmpHk * tmpHk));

            // The HSL derivative's second term needs the same fused
            // 3*TMP^2 - TMP^3/(HK+1) staging as the native REAL build.
            // Leaving it as a plain product/subtract keeps .NET one ULP low.
            float hsHkTerm2Inner = LegacyPrecisionMath.Fma(3.0f, tmpSq, -(tmpCube / hkPlusOne));
            float hsHkTerm1 = 0.0111f * ((2.0f * tmp) - (tmpSq / hkPlusOne)) / hkPlusOne;
            float hsHkTerm2 = 0.0278f * hsHkTerm2Inner / hkPlusOne;
            // The native REAL build keeps `0.0002*2.0*TMP` on the left but
            // pairs `HK*(TMP+HK)` on the right before the final multiply.
            // Both the fully left-associated chain and the TMP*HK shortcut
            // miss traced low-Hk seed points by one ULP.
            float hsHkTerm3 = (0.0002f * 2.0f * tmp) * (hkf * (tmp + hkf));
            float hsHkDirect = (0.0111f * ((2.0f * tmp) - (tmpSq / hkPlusOne)) / hkPlusOne)
                             - (0.0278f * hsHkTerm2Inner / hkPlusOne)
                             - ((0.0002f * 2.0f * tmp) * (hkf * (tmp + hkf)));
            float hsHkDirectPlainTerm2 = (0.0111f * ((2.0f * tmp) - (tmpSq / hkPlusOne)) / hkPlusOne)
                                       - (0.0278f * ((3.0f * tmpSq) - (tmpCube / hkPlusOne)) / hkPlusOne)
                                       - ((0.0002f * 2.0f * tmp) * (hkf * (tmp + hkf)));
            float hsHkDirectLeftTerm3 = (0.0111f * ((2.0f * tmp) - (tmpSq / hkPlusOne)) / hkPlusOne)
                                      - (0.0278f * hsHkTerm2Inner / hkPlusOne)
                                      - (0.0002f * 2.0f * tmp * hkf * (tmp + hkf));
            float hsHkDirectTmpHkTerm3 = (0.0111f * ((2.0f * tmp) - (tmpSq / hkPlusOne)) / hkPlusOne)
                                       - (0.0278f * hsHkTerm2Inner / hkPlusOne)
                                       - (0.0002f * ((2.0f * (tmp * hkf)) * (tmp + hkf)));
            hsHkf = hsHkDirectLeftTerm3;
            tmpTrace = tmp;
            hkPlusOneTrace = hkPlusOne;
            hsHkTerm1Trace = hsHkTerm1;
            hsHkTerm2Trace = -hsHkTerm2;
            hsHkTerm3Trace = -hsHkTerm3;
            hsHkDirectTrace = hsHkDirect;
            hsHkDirectPlainTerm2Trace = hsHkDirectPlainTerm2;
            hsHkDirectLeftTerm3Trace = hsHkDirectLeftTerm3;
            hsHkDirectTmpHkTerm3Trace = hsHkDirectTmpHkTerm3;
        }
        else
        {
            float diff = hkf - 4.35f;
            // Replay the classic REAL source expression exactly:
            // HS = 0.015*(HK-4.35)**2/HK + 1.528
            // Staging the divided term first keeps the lower-side station-5
            // transition HSL packet one ULP low.
            hsf = ((0.015f * ((hkf - 4.35f) * (hkf - 4.35f))) / hkf) + 1.528f;
            // The high-Hk derivative follows the same REAL source order as HSL:
            // round the leading and trailing terms separately before the final
            // subtraction, otherwise HS_HK stays one ULP high in the station-27
            // laminar seed replay and leaks into BLDIF row32.
            float diffSq = diff * diff;
            float hsHkTerm1 = ((0.015f * 2.0f) * diff) / hkf;
            float hsHkTerm2 = (0.015f * diffSq) / (hkf * hkf);
            hsHkf = hsHkTerm1 - hsHkTerm2;
        }


        return (hsf, hsHkf, 0.0, 0.0);
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: HST
    // Difference from legacy: The same attached/separated correlation is preserved, but the managed code presents it as one readable kernel instead of an inline block inside the larger BLVAR routine.
    // Decision: Keep the extracted kernel and preserve the original branch logic and compressibility correction.
    public static (double Hs, double Hs_Hk, double Hs_Rt, double Hs_Msq) TurbulentShapeParameter(
        double hk, double rt, double msq)
        => TurbulentShapeParameter(hk, rt, msq, useLegacyPrecision: false);

    // Legacy mapping: f_xfoil/src/xblsys.f :: HST
    // Difference from legacy: The parity overload replays the source-ordered REAL staging explicitly because the turbulent H* chain feeds directly into BLVAR/TRDIF sensitivity rows.
    // Decision: Keep the readable double path for normal execution, but preserve the classic REAL ordering in parity mode.
    public static (double Hs, double Hs_Hk, double Hs_Rt, double Hs_Msq) TurbulentShapeParameter(
        double hk,
        double rt,
        double msq,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. 3-arg overload provides modern.
        float hkf = (float)hk;
        float rtf = (float)rt;
        float msqf = (float)msq;
        const float hsMinf = 1.500f;
        const float dHsInff = 0.015f;

        float hof;
        float hoRtf;
        if (rtf > 400.0f)
        {
            hof = 3.0f + (400.0f / rtf);
            hoRtf = -(400.0f / (rtf * rtf));
        }
        else
        {
            hof = 4.0f;
            hoRtf = 0.0f;
        }

        float rtzf;
        float rtzRtf;
        if (rtf > 200.0f)
        {
            rtzf = rtf;
            rtzRtf = 1.0f;
        }
        else
        {
            rtzf = 200.0f;
            rtzRtf = 0.0f;
        }

        float hsf;
        float hsHkf;
        float hsRtf;
        float grtf = 0.0f;
        float hdiff = 0.0f;
        float rtmpf = 0.0f;
        float htmpf = 0.0f;
        float htmpHkf = 0.0f;
        float htmpRtf = 0.0f;
        float hsHkTerm1f = 0.0f;
        float hsHkTerm2f = 0.0f;
        float hsRtRawf = 0.0f;
        float hsRtTerm1f = 0.0f;
        float hsRtTerm2f = 0.0f;
        float hsRtTerm3f = 0.0f;
        if (hkf < hof)
        {
            float hr = (hof - hkf) / (hof - 1.0f);
            float hrHkf = -1.0f / (hof - 1.0f);
            float hrRtf = ((1.0f - hr) / (hof - 1.0f)) * hoRtf;
            float coeff = (2.0f - hsMinf) - (4.0f / rtzf);
            float hrSq = hr * hr;
            float hkPlusHalf = hkf + 0.5f;
            float hsAttachedBase = (coeff * hrSq) * 1.5f;
            float hsHkFirstMagnitude = hsAttachedBase / (hkPlusHalf * hkPlusHalf);
            float hsHkSecondBase = (((coeff * hr) * 2.0f) * 1.5f) / hkPlusHalf;

            hsf = (hsAttachedBase / hkPlusHalf) + hsMinf;
            hsf += 4.0f / rtzf;

            // The attached-branch HS_HK combine is also emitted as a fused
            // negative multiply-add by the native legacy build.
            hsHkf = LegacyPrecisionMath.Fma(hrHkf, hsHkSecondBase, -hsHkFirstMagnitude);
            float hsRtBase = (((((hrSq * 1.5f) / hkPlusHalf) - 1.0f) * 4.0f) / (rtzf * rtzf)) * rtzRtf;
            hsRtf = LegacyPrecisionMath.Fma(hsHkSecondBase, hrRtf, hsRtBase);
            hsRtRawf = hsRtf;
        }
        else
        {
            grtf = LegacyLibm.Log(rtzf);
            hdiff = hkf - hof;
            rtmpf = hkf - hof + (4.0f / grtf);
            htmpf = (0.007f * grtf) / (rtmpf * rtmpf) + (dHsInff / hkf);
            htmpHkf = (-0.014f * grtf) / (rtmpf * rtmpf * rtmpf) - (dHsInff / (hkf * hkf));
            htmpRtf = ((-0.014f * grtf) / (rtmpf * rtmpf * rtmpf)) * (-hoRtf - ((4.0f / (grtf * grtf)) / rtzf) * rtzRtf)
                    + ((0.007f / (rtmpf * rtmpf)) / rtzf) * rtzRtf;

            hsf = LegacyPrecisionMath.Fma(hdiff * hdiff, htmpf, hsMinf);
            hsf += 4.0f / rtzf;
            hsHkTerm1f = hdiff * 2.0f * htmpf;
            hsHkTerm2f = hdiff * hdiff * htmpHkf;
            // The legacy O2 build contracts both separated-branch Jacobian
            // sums into fused multiply-add/subtract instructions. Matching the
            // term values alone is not enough; the final store must use the
            // same single-rounding combine to stay on the Fortran bit path.
            hsHkf = LegacyPrecisionMath.Fma(hdiff * hdiff, htmpHkf, hsHkTerm1f);
            hsRtTerm1f = hdiff * hdiff * htmpRtf;
            hsRtTerm2f = -((4.0f / (rtzf * rtzf)) * rtzRtf);
            hsRtTerm3f = (hdiff * 2.0f * htmpf) * (-hoRtf);
            hsRtf = LegacyPrecisionMath.Fma(
                hsHkTerm1f,
                -hoRtf,
                LegacyPrecisionMath.Fma(hdiff * hdiff, htmpRtf, hsRtTerm2f));
            hsRtRawf = hsRtf;
        }

        float fmf = LegacyPrecisionMath.Fma(msqf, 0.014f, 1.0f);
        hsf = LegacyPrecisionMath.Fma(msqf, 0.028f, hsf) / fmf;
        hsHkf /= fmf;
        hsRtf /= fmf;
        float hsMsqf = (0.028f / fmf) - ((0.014f * hsf) / fmf);


        return (hsf, hsHkf, hsRtf, hsMsqf);
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: CFL
    // Difference from legacy: The same Falkner-Skan piecewise correlation is preserved, but the managed port returns the value and derivatives directly.
    // Decision: Keep the extracted kernel and preserve the legacy branch formulas.
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

    // Legacy mapping: f_xfoil/src/xblsys.f :: CFL
    // Difference from legacy: This overload exists to preserve the native REAL product/subtract staging that the parity path depends on.
    // Decision: Keep the parity overload because early BLVAR/BLDIF traces are sensitive to this arithmetic order.
    public static (double Cf, double Cf_Hk, double Cf_Rt, double Cf_Msq) LaminarSkinFriction(
        double hk,
        double rt,
        double msq,
        bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. The 3-arg overload remains modern.
        float hkf = (float)hk;
        float rtf = (float)rt;
        float cf;
        float cfHk;

        if (hkf < 5.5f)
        {
            // The classic REAL path behaves like explicit repeated multiplies here,
            // not a generic Pow() call. Keeping the staged float products avoids a
            // one-ULP drift that otherwise appears immediately in the BLDIF trace.
            float diff = 5.5f - hkf;
            float diffSq = diff * diff;
            float diffCube = diffSq * diff;
            float tmp = diffCube / (hkf + 1.0f);
            // CFL is written as PRODUCT minus constant in Fortran. Matching the
            // native REAL path requires preserving the intermediate product
            // rounding instead of contracting to a fused multiply-add.
            float product_cf = 0.0727f * tmp;
            float numerator = product_cf - 0.07f;
            cf = numerator / rtf;
            cfHk = ((-0.0727f * tmp * 3.0f / diff) - ((0.0727f * tmp) / (hkf + 1.0f))) / rtf;
        }
        else
        {
            float tmp = 1.0f - (1.0f / (hkf - 4.5f));
            float numerator;
            if (LegacyPrecisionMath.DisableFma)
            {
                // Fortran -O0: 0.015*TMP**2 evaluates as 0.015*(TMP*TMP).
                float tmpSq = tmp * tmp;
                numerator = (0.015f * tmpSq) - 0.07f;
            }
            else
            {
                // FMA mode: left-to-right (0.015*TMP)*TMP matches the traced parity.
                float scaledTmp = 0.015f * tmp;
                numerator = (scaledTmp * tmp) - 0.07f;
            }
            cf = numerator / rtf;
            cfHk = ((0.015f * tmp * 2.0f) / ((hkf - 4.5f) * (hkf - 4.5f))) / rtf;
        }

        float cfRt = -cf / rtf;
        return (cf, cfHk, cfRt, 0.0);
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: CFT
    // Difference from legacy: The correlation is the same Coles-law kernel, but the managed version leaves it as a standalone function with an explicit `cfFac` argument rather than reading a shared BLPAR value.
    // Decision: Keep the explicit argument and preserve the legacy formula and clamp behavior.
    public static (double Cf, double Cf_Hk, double Cf_Rt, double Cf_Msq) TurbulentSkinFriction(
        double hk, double rt, double msq, double cfFac = DefaultCfFac)
        => TurbulentSkinFriction(hk, rt, msq, useLegacyPrecision: false, cfFac);

    // Legacy mapping: f_xfoil/src/xblsys.f :: CFT
    // Difference from legacy: This overload exists only to replay the native REAL CFT staging explicitly when parity mode is enabled.
    // Decision: Keep the default double overload for normal managed execution, but route parity-sensitive BLVAR callers through this legacy path because CFT hidden partials feed downstream DI and BLDIF rows.
    public static (double Cf, double Cf_Hk, double Cf_Rt, double Cf_Msq) TurbulentSkinFriction(
        double hk, double rt, double msq, bool useLegacyPrecision, double cfFac = DefaultCfFac)
    {
        // Phase 1 strip: float-only path. 3-arg overload provides modern.
        float hkf = (float)hk;
        float rtf = (float)rt;
        float msqf = (float)msq;
        float cfFacf = (float)cfFac;
        float gm1f = (float)LegacyPrecisionMath.GammaMinusOne(true);
        // The legacy CFT entry follows the native build's single-round
        // `1.0 + 0.5*GM1*MSQ` behavior. Replaying it as a separately rounded
        // product plus add leaves FCARG one ULP low and cascades through the
        // turbulent CF/DI family.
        float fcArg = LegacyPrecisionMath.Fma(0.5f * gm1f, msqf, 1.0f);
        float fc32 = MathF.Sqrt(fcArg);
        float grt32 = LegacyLibm.Log(rtf / fc32);
        grt32 = MathF.Max(grt32, 3.0f);

        float gex32 = LegacyPrecisionMath.Fma(-hkf, 0.31f, -1.74f);

        float arg32 = -1.33f * hkf;
        arg32 = MathF.Max(-20.0f, arg32);

        float thkArg32 = 4.0f - (hkf / 0.875f);
        float thk32 = LegacyLibm.Tanh(thkArg32);

        float grtRatio32 = grt32 / 2.3026f;
        float cfo32 = cfFacf;
        cfo32 *= 0.3f;
        cfo32 *= LegacyLibm.Exp(arg32);
        cfo32 *= LegacyLibm.Pow(grtRatio32, gex32);

        float cfTail32 = thk32 - 1.0f;
        float cfNumerator32 = LegacyPrecisionMath.Fma(cfTail32, 1.1e-4f, cfo32);
        float cf32 = cfNumerator32 / fc32;

        float cfHkTerm1 = -1.33f * cfo32;
        float logGrtRatio32 = LegacyLibm.Log(grtRatio32);
        float cfHkTerm2 = (-0.31f * logGrtRatio32) * cfo32;
        float thkSq32 = LegacyPrecisionMath.MultiplyF(thk32, thk32);
        float oneMinusThkSq32 = LegacyPrecisionMath.SubtractF(1.0f, thkSq32);
        float scaledThkDiff32 = LegacyPrecisionMath.MultiplyF(-1.1e-4f, oneMinusThkSq32);
        float cfHkTerm3 = LegacyPrecisionMath.DivideF(scaledThkDiff32, 0.875f);
        // CFT stores CFHKTERM1/2/3 to REAL before assembling CF_HK; sum the
        // staged terms rather than recomputing a wider inline numerator.
        float cfHk32 = (cfHkTerm1 + cfHkTerm2 + cfHkTerm3) / fc32;

        float cfRt32 = ((gex32 * cfo32) / (fc32 * grt32)) / rtf;
        float fcSq32 = fc32 * fc32;
        float cfMsqScaleConstant32 = 0.25f * gm1f;
        float cfMsqScale32 = cfMsqScaleConstant32 / fcSq32;
        float cfMsqLeadNumerator32 = gex32 * cfo32;
        float cfMsqLeadDenominator32 = fc32 * grt32;
        float cfMsqLeadCore32 = cfMsqLeadNumerator32 / cfMsqLeadDenominator32;
        float cfMsqTail32 = cf32 * cfMsqScale32;
        float cfMsqLeadTerm32 = cfMsqLeadCore32 * (-cfMsqScale32);
        float cfMsq32 = cfMsqLeadTerm32 - cfMsqTail32;

        return (cf32, cfHk32, cfRt32, cfMsq32);
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: DIL
    // Difference from legacy: This wrapper just forwards to the full implementation instead of duplicating the correlation.
    // Decision: Keep the wrapper to centralize the main implementation.
    public static (double Di, double Di_Hk, double Di_Rt) LaminarDissipation(double hk, double rt)
        => LaminarDissipation(hk, rt, useLegacyPrecision: false);

    // Legacy mapping: f_xfoil/src/xblsys.f :: DIL
    // Difference from legacy: The default branch preserves the formula directly, while the parity branch explicitly preserves the legacy REAL staging and fused terms.
    // Decision: Keep the shared implementation with a parity branch because DIL is part of the active parity boundary search.
    public static (double Di, double Di_Hk, double Di_Rt) LaminarDissipation(double hk, double rt, bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. 2-arg overload provides modern.
        float hkf = (float)hk;
        float rtf = (float)rt;

        float di32;
        float diHk32;

        if (hkf < 4.0f)
        {
            float diff = 4.0f - hkf;
            float pow55 = LegacyLibm.Pow(diff, 5.5f);
            float numerator = LegacyPrecisionMath.DisableFma
                ? (0.00205f * pow55) + 0.207f
                : (float)(((double)0.00205f * pow55) + 0.207f);
            di32 = numerator / rtf;
            diHk32 = (-0.00205f * 5.5f * LegacyLibm.Pow(diff, 4.5f)) / rtf;
        }
        else
        {
            float hkbf = hkf - 4.0f;
            float hkbSqf = hkbf * hkbf;
            float denf = 1.0f + (0.02f * hkbSqf);
            float ratiof = hkbSqf / denf;
            float numeratorf = LegacyPrecisionMath.DisableFma
                ? ((-0.0016f) * ratiof) + 0.207f
                : (float)(((double)(-0.0016f) * ratiof) + 0.207f);
            di32 = numeratorf / rtf;
            float diHkBracketf = (1.0f / denf) - ((0.02f * hkbSqf) / (denf * denf));
            diHk32 = (((-0.0016f * 2.0f) * hkbf) * diHkBracketf) / rtf;
        }

        float diRt32 = -di32 / rtf;
        return (di32, diHk32, diRt32);
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: DILW
    // Difference from legacy: This wrapper forwards to the full implementation so wake dissipation stays centralized.
    // Decision: Keep the wrapper for consistency with the laminar dissipation API.
    public static (double Di, double Di_Hk, double Di_Rt) WakeDissipation(double hk, double rt)
        => WakeDissipation(hk, rt, useLegacyPrecision: false);

    // Legacy mapping: f_xfoil/src/xblsys.f :: DILW
    // Difference from legacy: The managed port keeps the corrected mathematical derivative note in the default branch and exposes an explicit parity branch for the legacy REAL staging.
    // Decision: Keep the corrected managed documentation and parity overload; preserve the legacy formula inputs while retaining the clarified derivative implementation.
    public static (double Di, double Di_Hk, double Di_Rt) WakeDissipation(double hk, double rt, bool useLegacyPrecision)
    {
        // Phase 1 strip: float-only path. 2-arg overload provides modern.
        var (hsf, hsHkf, hsRtf, _) = LaminarShapeParameter(hk, useLegacyPrecision: true);
        float hkf = (float)hk;
        float rtf = (float)rt;
        float hsf32 = (float)hsf;
        float hsHk32 = (float)hsHkf;
        float hsRt32 = (float)hsRtf;

        float oneMinusInvHk = 1.0f - 1.0f / hkf;
        float oneMinusInvHkSq = oneMinusInvHk * oneMinusInvHk;
        float rcd32 = 1.10f * oneMinusInvHkSq / hkf;
        float hkCubed = hkf * hkf * hkf;
        float rcdHk32 = -1.10f * oneMinusInvHk * 2.0f / hkCubed - rcd32 / hkf;

        float di32 = 2.0f * rcd32 / (hsf32 * rtf);
        float diHk32 = 2.0f * rcdHk32 / (hsf32 * rtf) - (di32 / hsf32) * hsHk32;
        float diRt32 = -di32 / rtf - (di32 / hsf32) * hsRt32;
        return (di32, diHk32, diRt32);
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: HCT
    // Difference from legacy: The correlation is unchanged; the managed port simply returns the value and sensitivities as a tuple.
    // Decision: Keep the extracted kernel and preserve the Whitfield relation.
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: DIT
    // Difference from legacy: The same local dissipation relation is preserved, but the managed code exposes it as a reusable tuple-returning helper.
    // Decision: Keep the helper because BLVAR/BLDIF and newer managed code paths both reuse it.
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
    // Legacy mapping: f_xfoil/src/xblsys.f :: inline BLVAR CQ relation
    // Difference from legacy: The managed port lifts the inline BLVAR equation into a reusable helper and documents the chain rule explicitly instead of leaving it embedded in the large BLVAR routine.
    // Decision: Keep the helper extraction and preserve the legacy clamp semantics and derivative structure.
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
