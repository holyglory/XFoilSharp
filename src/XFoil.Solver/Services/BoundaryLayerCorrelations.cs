using System;
using XFoil.Core.Numerics;
using XFoil.Solver.Diagnostics;
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
        if (!useLegacyPrecision)
        {
            return KinematicShapeParameter(h, msq);
        }

        float hf = (float)h;
        float msqf = (float)msq;
        // HKIN is a small algebraic kernel, but the parity path still needs the
        // exact REAL staging because Hk and dHk/dH flow straight into MRCHUE and
        // BLSYS row assembly. Leaving it on double was enough to move row33 by 1 ULP.
        float denom = LegacyPrecisionMath.ProductThenAdd(0.113f, msqf, 1.0f);
        float numerator = LegacyPrecisionMath.SeparateMultiplySubtract(0.29f, msqf, hf);
        double hkExtended = ((double)numerator) / denom;
        float hkf = (float)hkExtended;
        float hkHf = 1.0f / denom;
        // HKIN's derivative numerator is written in the legacy source as
        // "-0.29 - 0.113*HK". The native reference build keeps the just-computed
        // HK value in higher precision for this product before storing HK itself
        // back to REAL. Reusing the rounded HK float here fixes one alpha-10
        // point and breaks the next; keeping the extended HK for the derivative
        // matches both traced seed points.
        float hkMsNumerator = (float)(-0.29f - (0.113f * hkExtended));
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

    // Legacy mapping: f_xfoil/src/xblsys.f :: HSL
    // Difference from legacy: This parity overload replays the native REAL staging and selected fused operations explicitly instead of depending on the managed runtime to match them.
    // Decision: Keep the parity overload because HSL is one of the early correlation boundaries in binary trace matching.
    public static (double Hs, double Hs_Hk, double Hs_Rt, double Hs_Msq) LaminarShapeParameter(
        double hk,
        bool useLegacyPrecision)
    {
        if (!useLegacyPrecision)
        {
            return LaminarShapeParameter(hk);
        }

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
            float hsHkTerm2Inner = MathF.FusedMultiplyAdd(3.0f, tmpSq, -(tmpCube / hkPlusOne));
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
            float diffSqOverHk = (diff * diff) / hkf;
            // This source is a plain REAL product plus constant, so the legacy
            // branch keeps the product round separate from the final add.
            hsf = LegacyPrecisionMath.ProductThenAdd(0.015f, diffSqOverHk, 1.528f);
            // The high-Hk derivative follows the same REAL source order as HSL:
            // round the leading and trailing terms separately before the final
            // subtraction, otherwise HS_HK stays one ULP high in the station-27
            // laminar seed replay and leaks into BLDIF row32.
            float diffSq = diff * diff;
            float hsHkTerm1 = ((0.015f * 2.0f) * diff) / hkf;
            float hsHkTerm2 = (0.015f * diffSq) / (hkf * hkf);
            hsHkf = hsHkTerm1 - hsHkTerm2;
        }

        SolverTrace.Event(
            "hsl_terms",
            SolverTrace.ScopeName(typeof(BoundaryLayerCorrelations)),
            new
            {
                hk = hkf,
                hs = hsf,
                hsHk = hsHkf,
                tmp = tmpTrace,
                hkPlusOne = hkPlusOneTrace,
                hsHkTerm1 = hsHkTerm1Trace,
                hsHkTerm2 = hsHkTerm2Trace,
                hsHkTerm3 = hsHkTerm3Trace,
                hsHkDirect = hsHkDirectTrace,
                hsHkDirectPlainTerm2 = hsHkDirectPlainTerm2Trace,
                hsHkDirectLeftTerm3 = hsHkDirectLeftTerm3Trace,
                hsHkDirectTmpHkTerm3 = hsHkDirectTmpHkTerm3Trace
            });

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
        const double HsMin = 1.500;
        const double DHsInf = 0.015;

        if (!useLegacyPrecision)
        {
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
        float branchf = 0.0f;
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
            branchf = 1.0f;
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
            hsHkf = MathF.FusedMultiplyAdd(hrHkf, hsHkSecondBase, -hsHkFirstMagnitude);
            float hsRtBase = (((((hrSq * 1.5f) / hkPlusHalf) - 1.0f) * 4.0f) / (rtzf * rtzf)) * rtzRtf;
            hsRtf = MathF.FusedMultiplyAdd(hsHkSecondBase, hrRtf, hsRtBase);
            hsRtRawf = hsRtf;
        }
        else
        {
            branchf = 2.0f;
            grtf = LegacyLibm.Log(rtzf);
            hdiff = hkf - hof;
            rtmpf = hkf - hof + (4.0f / grtf);
            htmpf = (0.007f * grtf) / (rtmpf * rtmpf) + (dHsInff / hkf);
            htmpHkf = (-0.014f * grtf) / (rtmpf * rtmpf * rtmpf) - (dHsInff / (hkf * hkf));
            htmpRtf = ((-0.014f * grtf) / (rtmpf * rtmpf * rtmpf)) * (-hoRtf - ((4.0f / (grtf * grtf)) / rtzf) * rtzRtf)
                    + ((0.007f / (rtmpf * rtmpf)) / rtzf) * rtzRtf;

            hsf = MathF.FusedMultiplyAdd(hdiff * hdiff, htmpf, hsMinf);
            hsf += 4.0f / rtzf;
            hsHkTerm1f = hdiff * 2.0f * htmpf;
            hsHkTerm2f = hdiff * hdiff * htmpHkf;
            // The legacy O2 build contracts both separated-branch Jacobian
            // sums into fused multiply-add/subtract instructions. Matching the
            // term values alone is not enough; the final store must use the
            // same single-rounding combine to stay on the Fortran bit path.
            hsHkf = MathF.FusedMultiplyAdd(hdiff * hdiff, htmpHkf, hsHkTerm1f);
            hsRtTerm1f = hdiff * hdiff * htmpRtf;
            hsRtTerm2f = -((4.0f / (rtzf * rtzf)) * rtzRtf);
            hsRtTerm3f = (hdiff * 2.0f * htmpf) * (-hoRtf);
            hsRtf = MathF.FusedMultiplyAdd(
                hsHkTerm1f,
                -hoRtf,
                MathF.FusedMultiplyAdd(hdiff * hdiff, htmpRtf, hsRtTerm2f));
            hsRtRawf = hsRtf;
        }

        float fmf = MathF.FusedMultiplyAdd(msqf, 0.014f, 1.0f);
        hsf = MathF.FusedMultiplyAdd(msqf, 0.028f, hsf) / fmf;
        hsHkf /= fmf;
        hsRtf /= fmf;
        float hsMsqf = (0.028f / fmf) - ((0.014f * hsf) / fmf);

        SolverTrace.Event(
            "hst_terms",
            SolverTrace.ScopeName(typeof(BoundaryLayerCorrelations)),
            new
            {
                hk = hkf,
                rt = rtf,
                msq = msqf,
                branch = branchf,
                ho = hof,
                hoRt = hoRtf,
                rtz = rtzf,
                rtzRt = rtzRtf,
                grt = grtf,
                hdif = hdiff,
                rtmp = rtmpf,
                htmp = htmpf,
                htmpHk = htmpHkf,
                htmpHkBits = unchecked((int)BitConverter.SingleToUInt32Bits(htmpHkf)),
                htmpRt = htmpRtf,
                hsHkTerm1 = hsHkTerm1f,
                hsHkTerm1Bits = unchecked((int)BitConverter.SingleToUInt32Bits(hsHkTerm1f)),
                hsHkTerm2 = hsHkTerm2f,
                hsHkTerm2Bits = unchecked((int)BitConverter.SingleToUInt32Bits(hsHkTerm2f)),
                hsRtRaw = hsRtRawf,
                hsRtTerm1 = hsRtTerm1f,
                hsRtTerm2 = hsRtTerm2f,
                hsRtTerm3 = hsRtTerm3f,
                hs = hsf,
                hsHk = hsHkf,
                hsHkBits = unchecked((int)BitConverter.SingleToUInt32Bits(hsHkf)),
                hsRt = hsRtf,
                hsMsq = hsMsqf,
                fm = fmf
            });

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
        if (!useLegacyPrecision)
        {
            return LaminarSkinFriction(hk, rt, msq);
        }

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
            float numerator = LegacyPrecisionMath.ProductThenSubtract(0.0727f, tmp, 0.07f);
            cf = numerator / rtf;
            cfHk = ((-0.0727f * tmp * 3.0f / diff) - ((0.0727f * tmp) / (hkf + 1.0f))) / rtf;
        }
        else
        {
            float tmp = 1.0f - (1.0f / (hkf - 4.5f));
            // The high-Hk CFL branch is written in the legacy REAL source as
            // `0.015*TMP*TMP - 0.07`. Replacing that with a rounded TMP^2 helper
            // keeps the laminar midpoint CFM one ULP high on the alpha-10 P80
            // transition rung, so parity mode has to preserve the left-associated
            // multiply chain explicitly here.
            float scaledTmp = 0.015f * tmp;
            float numerator = (scaledTmp * tmp) - 0.07f;
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
        if (useLegacyPrecision)
        {
            float hkf = (float)hk;
            float rtf = (float)rt;
            float msqf = (float)msq;
            float cfFacf = (float)cfFac;
            float gm1f = (float)LegacyPrecisionMath.GammaMinusOne(true);
            // The legacy CFT entry follows the native build's single-round
            // `1.0 + 0.5*GM1*MSQ` behavior. Replaying it as a separately rounded
            // product plus add leaves FCARG one ULP low and cascades through the
            // turbulent CF/DI family.
            float fcArg = MathF.FusedMultiplyAdd(0.5f * gm1f, msqf, 1.0f);
            float fc32 = MathF.Sqrt(fcArg);
            float grt32 = LegacyLibm.Log(rtf / fc32);
            grt32 = MathF.Max(grt32, 3.0f);

            float gex32 = MathF.FusedMultiplyAdd(-hkf, 0.31f, -1.74f);

            float arg32 = -1.33f * hkf;
            arg32 = MathF.Max(-20.0f, arg32);

            float thkArg32 = 4.0f - (hkf / 0.875f);
            float thk32 = LegacyLibm.Tanh(thkArg32);

            float grtRatio32 = grt32 / 2.3026f;
            float cfo32 = cfFacf;
            cfo32 *= 0.3f;
            cfo32 *= MathF.Exp(arg32);
            cfo32 *= LegacyLibm.Pow(grtRatio32, gex32);

            float cfTail32 = thk32 - 1.0f;
            float cfNumerator32 = MathF.FusedMultiplyAdd(cfTail32, 1.1e-4f, cfo32);
            float cf32 = cfNumerator32 / fc32;

            float cfHkTerm1 = -1.33f * cfo32;
            float logGrtRatio32 = LegacyLibm.Log(grtRatio32);
            float cfHkTerm2 = (-0.31f * logGrtRatio32) * cfo32;
            float thkSq32 = (float)LegacyPrecisionMath.Multiply(thk32, thk32, true);
            float oneMinusThkSq32 = (float)LegacyPrecisionMath.Subtract(1.0f, thkSq32, true);
            float scaledThkDiff32 = (float)LegacyPrecisionMath.Multiply(-1.1e-4f, oneMinusThkSq32, true);
            float cfHkTerm3 = (float)LegacyPrecisionMath.Divide(scaledThkDiff32, 0.875f, true);
            // CFT stores CFHKTERM1/2/3 to REAL before assembling CF_HK, so the
            // parity path must sum the staged terms rather than recomputing a
            // wider inline numerator.
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

            SolverTrace.Event(
                "cft_terms",
                SolverTrace.ScopeName(typeof(BoundaryLayerCorrelations)),
                new
                {
                    hk = hkf,
                    rt = rtf,
                    msq = msqf,
                    fcArg,
                    fc = fc32,
                    grt = grt32,
                    gex = gex32,
                    arg = arg32,
                    thkArg = thkArg32,
                    thk = thk32,
                    grtRatio = grtRatio32,
                    thkSq = thkSq32,
                    oneMinusThkSq = oneMinusThkSq32,
                    scaledThkDiff = scaledThkDiff32,
                    cfo = cfo32,
                    cfHkTerm1,
                    cfHkTerm2,
                    cfHkTerm3,
                    cfNumerator = cfNumerator32,
                    cf = cf32,
                    cfHk = cfHk32,
                    cfRt = cfRt32,
                    cfMsqScale = cfMsqScale32,
                    cfMsqLeadCore = cfMsqLeadCore32,
                    cfMsqTail = cfMsqTail32,
                    cfMsq = cfMsq32
                });

            return (cf32, cfHk32, cfRt32, cfMsq32);
        }

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
        if (useLegacyPrecision)
        {
            float hkf = (float)hk;
            float rtf = (float)rt;

            float di32;
            float diHk32;
            float hkbTrace = 0.0f;
            float hkbSqTrace = 0.0f;
            float denTrace = 0.0f;
            float ratioTrace = 0.0f;
            float numeratorTrace = 0.0f;

            if (hkf < 4.0f)
            {
                float diff = 4.0f - hkf;
                float pow55 = LegacyLibm.Pow(diff, 5.5f);
                // Fresh DIL micro-driver cases show the low-Hk numerator does not
                // round the product before the final REAL store. Replaying the
                // source tree with plain float operators keeps DI one ULP low on
                // raw-bit comparisons, so keep the multiply-add wide here and
                // cast once at the end.
                float numerator = (float)(((double)0.00205f * pow55) + 0.207f);
                numeratorTrace = numerator;
                di32 = numerator / rtf;
                diHk32 = (-0.00205f * 5.5f * LegacyLibm.Pow(diff, 4.5f)) / rtf;
            }
            else
            {
                // The hk>=4 numerator follows the same native REAL storage rule
                // as the low-Hk branch: keep the product-add wide until the
                // final REAL store. Rounding the product before the add leaves
                // DI two bits low on the standalone DIL driver.
                float hkbf = hkf - 4.0f;
                float hkbSqf = hkbf * hkbf;
                float denf = 1.0f + (0.02f * hkbSqf);
                float ratiof = hkbSqf / denf;
                float numeratorf = (float)(((double)(-0.0016f) * ratiof) + 0.207f);
                hkbTrace = hkbf;
                hkbSqTrace = hkbSqf;
                denTrace = denf;
                ratioTrace = ratiof;
                numeratorTrace = numeratorf;
                di32 = numeratorf / rtf;
                float diHkBracketf = (1.0f / denf) - ((0.02f * hkbSqf) / (denf * denf));
                diHk32 = (((-0.0016f * 2.0f) * hkbf) * diHkBracketf) / rtf;
            }

            float diRt32 = -di32 / rtf;
            SolverTrace.Event(
                "laminar_dissipation",
                SolverTrace.ScopeName(typeof(BoundaryLayerCorrelations)),
                new
                {
                    hk = hkf,
                    rt = rtf,
                    hkb = hkbTrace,
                    hkbSq = hkbSqTrace,
                    den = denTrace,
                    ratio = ratioTrace,
                    numerator = numeratorTrace,
                    di = di32,
                    diHk = diHk32,
                    diRt = diRt32
                });
            return (di32, diHk32, diRt32);
        }

        double di, di_hk;
        double hkbTrace64 = 0.0;
        double hkbSqTrace64 = 0.0;
        double denTrace64 = 0.0;
        double ratioTrace64 = 0.0;
        double numeratorTrace64 = 0.0;

        if (hk < 4.0)
        {
            numeratorTrace64 = 0.00205 * Math.Pow(4.0 - hk, 5.5) + 0.207;
            di = numeratorTrace64 / rt;
            di_hk = (-0.00205 * 5.5 * Math.Pow(4.0 - hk, 4.5)) / rt;
        }
        else
        {
            double hkb = hk - 4.0;
            double hkbSq = hkb * hkb;
            double den = 1.0 + 0.02 * hkbSq;
            double ratio = hkbSq / den;
            double numerator = -0.0016 * ratio + 0.207;
            hkbTrace64 = hkb;
            hkbSqTrace64 = hkbSq;
            denTrace64 = den;
            ratioTrace64 = ratio;
            numeratorTrace64 = numerator;
            di = numerator / rt;
            di_hk = (-0.0016 * 2.0 * hkb * (1.0 / den - 0.02 * hkb * hkb / (den * den))) / rt;
        }

        double di_rt = -di / rt;

        SolverTrace.Event(
            "laminar_dissipation",
            SolverTrace.ScopeName(typeof(BoundaryLayerCorrelations)),
            new
            {
                hk,
                rt,
                hkb = hkbTrace64,
                hkbSq = hkbSqTrace64,
                den = denTrace64,
                ratio = ratioTrace64,
                numerator = numeratorTrace64,
                di,
                diHk = di_hk,
                diRt = di_rt
            });

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
        if (useLegacyPrecision)
        {
            var (hsf, hsHkf, hsRtf, _) = LaminarShapeParameter(hk, useLegacyPrecision: true);
            float hkf = (float)hk;
            float rtf = (float)rt;
            float hsf32 = (float)hsf;
            float hsHk32 = (float)hsHkf;
            float hsRt32 = (float)hsRtf;

            float oneMinusInvHk = 1.0f - 1.0f / hkf;
            float rcd32 = 1.10f * oneMinusInvHk * oneMinusInvHk / hkf;
            float rcdHk32 = 1.10f * (hkf - 1.0f) * (3.0f - hkf) / (hkf * hkf * hkf * hkf);

            float di32 = 2.0f * rcd32 / (hsf32 * rtf);
            float diHk32 = 2.0f * rcdHk32 / (hsf32 * rtf) - (di32 / hsf32) * hsHk32;
            float diRt32 = -di32 / rtf - (di32 / hsf32) * hsRt32;
            return (di32, diHk32, diRt32);
        }

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
