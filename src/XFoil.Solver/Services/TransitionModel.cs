using System;
using XFoil.Solver.Numerics;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: DAMPL/DAMPL2/AXSET/TRCHEK2
// Secondary legacy source(s): none
// Role in port: Transition-envelope correlations and transition-point solves for the viscous march.
// Differences: This file is a direct legacy port, but the C# version names intermediate terms, emits trace events, and threads parity arithmetic explicitly through LegacyPrecisionMath instead of relying on implicit REAL staging.
// Decision: Keep the clearer managed structure and tracing; preserve parity-specific arithmetic only where binary legacy replay is required.

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
        double DownstreamAmplification,
        TransitionResultType Type,
        bool Converged,
        int Iterations);

    /// <summary>
    /// Detailed TRCHEK2 result used by the transition-interval Jacobian assembly.
    /// This keeps the exact transition-point solve in one place so the seed march
    /// and the transition interval cannot silently drift apart.
    /// </summary>
    public sealed class TransitionPointResult
    {
        public bool TransitionOccurred;
        public double TransitionXi;
        public double AmplAtTransition;
        public double DownstreamAmplification;
        public TransitionResultType Type;
        public bool Converged;
        public int Iterations;
        public double Xt;
        public double Tt;
        public double Dt;
        public double Ut;
        public double[] Xt1 { get; } = new double[5];
        public double[] Xt2 { get; } = new double[5];
        public double[] Tt1 { get; } = new double[5];
        public double[] Tt2 { get; } = new double[5];
        public double[] Dt1 { get; } = new double[5];
        public double[] Dt2 { get; } = new double[5];
        public double[] Ut1 { get; } = new double[5];
        public double[] Ut2 { get; } = new double[5];
        /// <summary>
        /// Sensitivity of WF2 to forced transition xi (XIFORC).
        /// Fortran: WF2_XF = SFX_XF = 1/(X2-X1) when forced transition governs,
        /// 0 when free transition governs. Used for BTX/VSX computation.
        /// </summary>
        public double Wf2XF;
        public BoundaryLayerSystemAssembler.KinematicResult? DownstreamKinematic;
        public BoundaryLayerSystemAssembler.KinematicResult? FinalTransitionKinematic;
        public AxsetResult? FinalAx;

        // Pre-allocated snapshot storage for DownstreamKinematic and
        // FinalTransitionKinematic. The publishing setters below copy
        // fields into these slots instead of cloning a fresh instance
        // per TRCHEK2 call; the public nullable properties alias back
        // to the slot when live and to null when cleared.
        private readonly BoundaryLayerSystemAssembler.KinematicResult _downstreamStorage = new();
        private readonly BoundaryLayerSystemAssembler.KinematicResult _finalTransitionStorage = new();

        internal void SetDownstreamKinematic(BoundaryLayerSystemAssembler.KinematicResult source)
        {
            _downstreamStorage.CopyFrom(source);
            DownstreamKinematic = _downstreamStorage;
        }

        internal void SetFinalTransitionKinematic(BoundaryLayerSystemAssembler.KinematicResult source)
        {
            _finalTransitionStorage.CopyFrom(source);
            FinalTransitionKinematic = _finalTransitionStorage;
        }

        /// <summary>
        /// Reset all scalar fields and inner sensitivity arrays to defaults so the
        /// pooled instance can be reused by another ComputeTransitionPoint call
        /// without leaking stale data. Inner arrays are zeroed in place (the
        /// references themselves are never reassigned).
        /// </summary>
        internal void Reset()
        {
            TransitionOccurred = false;
            TransitionXi = 0.0;
            AmplAtTransition = 0.0;
            DownstreamAmplification = 0.0;
            Type = TransitionResultType.None;
            Converged = false;
            Iterations = 0;
            Xt = 0.0;
            Tt = 0.0;
            Dt = 0.0;
            Ut = 0.0;
            Wf2XF = 0.0;
            DownstreamKinematic = null;
            FinalTransitionKinematic = null;
            FinalAx = null;
            System.Array.Clear(Xt1, 0, Xt1.Length);
            System.Array.Clear(Xt2, 0, Xt2.Length);
            System.Array.Clear(Tt1, 0, Tt1.Length);
            System.Array.Clear(Tt2, 0, Tt2.Length);
            System.Array.Clear(Dt1, 0, Dt1.Length);
            System.Array.Clear(Dt2, 0, Dt2.Length);
            System.Array.Clear(Ut1, 0, Ut1.Length);
            System.Array.Clear(Ut2, 0, Ut2.Length);
        }
    }

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
    // Legacy mapping: f_xfoil/src/xblsys.f :: DAMPL
    // Difference from legacy: The correlation is algebraically the same, but the managed port exposes the intermediate terms and routes parity-sensitive products through explicit helpers so the arithmetic path is inspectable.
    // Decision: Keep the decomposition and traces; only the parity path should mimic the legacy REAL staging exactly.
    // Prevent tiered JIT recompilation from introducing FMA contractions
    // between calls. The parity path requires deterministic float arithmetic.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    public static (double Ax, double Ax_Hk, double Ax_Th, double Ax_Rt)
        ComputeAmplificationRate(double hk, double th, double rt, bool useLegacyPrecision = false)
    {
        // The classic DAMPL path is a REAL correlation. The parity branch keeps
        // the same single-precision staging so transition drift does not come
        // from the envelope model before the Newton solve even starts.
        // xblsys.f:2023-2024
        double hkMinusOne = LegacyPrecisionMath.Subtract(hk, 1.0, useLegacyPrecision);
        double hmi = LegacyPrecisionMath.Divide(1.0, hkMinusOne, useLegacyPrecision);
        double hmi_hk = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Square(hmi, useLegacyPrecision), useLegacyPrecision);

        // log10(Critical Rth) - H correlation for Falkner-Skan profiles
        // xblsys.f:2027-2034
        double aa = LegacyPrecisionMath.Multiply(2.492, LegacyPrecisionMath.Pow(hmi, 0.43, useLegacyPrecision), useLegacyPrecision);
        double aa_hk = LegacyPrecisionMath.Multiply(
            LegacyPrecisionMath.Divide(aa, hmi, useLegacyPrecision),
            0.43,
            hmi_hk,
            useLegacyPrecision);

        double bbArg = LegacyPrecisionMath.MultiplyAdd(14.0, hmi, -9.24, useLegacyPrecision);
        double bb = LegacyPrecisionMath.Tanh(bbArg, useLegacyPrecision);
        double bb_hk = LegacyPrecisionMath.Multiply(
            LegacyPrecisionMath.MultiplySubtract(bb, bb, 1.0, useLegacyPrecision),
            14.0,
            hmi_hk,
            useLegacyPrecision);

        double grcrit = useLegacyPrecision
            ? LegacyPrecisionMath.MultiplyAdd(0.7, LegacyPrecisionMath.Add(bb, 1.0, true), aa, true)
            : aa + (0.7 * (bb + 1.0));
        double grc_hk = useLegacyPrecision
            // Legacy block: xblsys.f DAMPL critical-Re derivative sum.
            // Difference from legacy: The native REAL build lands on the
            // contracted  AA_HK + 0.7*BB_HK  result here; replaying it as a
            // separately-rounded product then add leaves RFAC_HK low by one
            // float ULP in the focused AXSET trace.
            // Decision: Use the contracted legacy helper in parity mode and
            // keep the default managed branch explicit.
            ? LegacyPrecisionMath.MultiplyAdd(0.7, bb_hk, aa_hk, true)
            : aa_hk + (0.7 * bb_hk);

        // xblsys.f:2037-2038
        double gr = LegacyPrecisionMath.Log10(rt, useLegacyPrecision);
        double gr_rt = LegacyPrecisionMath.Divide(1.0, LegacyPrecisionMath.Multiply(Ln10, rt, useLegacyPrecision), useLegacyPrecision);

        // Legacy block: xblsys.f DAMPL no-growth cutoff.
        // Difference from legacy: Same branch criterion, with added tracing so the first transition-envelope mismatch can be localized.
        // Decision: Keep the explicit cutoff and trace event.
        if (gr < grcrit - DGR)
        {
            // No amplification for Rtheta < Rcrit
            // xblsys.f:2043-2046
            return (0.0, 0.0, 0.0, 0.0);
        }

        // Smooth cubic ramp to turn on AX as Rtheta exceeds Rcrit
        // xblsys.f:2054-2068
        double twoDgr = LegacyPrecisionMath.Multiply(2.0, DGR, useLegacyPrecision);
        double rnorm = LegacyPrecisionMath.Divide(
            LegacyPrecisionMath.Subtract(gr, LegacyPrecisionMath.Subtract(grcrit, DGR, useLegacyPrecision), useLegacyPrecision),
            twoDgr,
            useLegacyPrecision);
        double rn_hk = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Divide(grc_hk, twoDgr, useLegacyPrecision), useLegacyPrecision);
        double rn_rt = LegacyPrecisionMath.Divide(gr_rt, twoDgr, useLegacyPrecision);

        double rfac, rfac_hk, rfac_rt;
        double rnorm2Trace = 0.0;
        double rfacRnTrace = 0.0;
        double sixRnormTrace = 0.0;
        double sixRnorm2Trace = 0.0;
        double rfacMulChainTrace = 0.0;
        double rfacWideTrace = 0.0;
        double rfacPowTrace = 0.0;
        double rfacMixedTrace = 0.0;
        // Legacy block: xblsys.f DAMPL smooth onset ramp.
        // Difference from legacy: Same cubic ramp, expressed through helper calls so parity mode can preserve the legacy evaluation order.
        // Decision: Keep the helper-based form because it makes the parity contract explicit.
        if (rnorm >= 1.0)
        {
            rfac = 1.0;
            rfac_hk = 0.0;
            rfac_rt = 0.0;
            rfacMulChainTrace = 1.0;
            rfacWideTrace = 1.0;
            rfacPowTrace = 1.0;
            rfacMixedTrace = 1.0;
        }
        else
        {
            double rnorm2 = LegacyPrecisionMath.Square(rnorm, useLegacyPrecision);
            double rnorm3 = LegacyPrecisionMath.Multiply(rnorm2, rnorm, useLegacyPrecision);
            rnorm2Trace = rnorm2;
            double rfac_rn;
            if (useLegacyPrecision)
            {
                float rnormf = (float)rnorm;
                float rnorm2f = (float)rnorm2;
                float rnorm2Powf = MathF.Pow(rnormf, 2.0f);
                float rnorm3Powf = MathF.Pow(rnormf, 3.0f);
                float rnorm3f = rnorm2f * rnormf;
                rfacMulChainTrace = (3.0f * rnorm2f) - (2.0f * rnorm3f);
                rfacWideTrace = (float)((3.0 * (double)rnorm2f) - (2.0 * ((double)rnorm2f * (double)rnormf)));
                rfacPowTrace = (3.0f * rnorm2Powf) - (2.0f * rnorm3Powf);
                // Fortran: RFAC = 3.0*RNORM**2 - 2.0*RNORM**3 — all in REAL
                float threeRnorm2f = 3.0f * rnorm2f;
                float twoRnorm3f = 2.0f * rnorm3f;
                rfacMixedTrace = threeRnorm2f - twoRnorm3f;
                rfac = rfacMixedTrace;
                // Fortran: RFAC_RN = 6.0*RNORM - 6.0*RNORM**2 — all in REAL
                float sixRnormf = 6.0f * rnormf;
                float sixRnorm2f = 6.0f * rnorm2f;
                sixRnormTrace = sixRnormf;
                sixRnorm2Trace = sixRnorm2f;
                rfac_rn = sixRnormf - sixRnorm2f;
            }
            else
            {
                rfac = (3.0 * rnorm2) - (2.0 * rnorm3);
                rfacMulChainTrace = rfac;
                rfacWideTrace = rfac;
                rfacPowTrace = rfac;
                rfacMixedTrace = rfac;
                sixRnormTrace = 6.0 * rnorm;
                sixRnorm2Trace = 6.0 * rnorm2;
                rfac_rn = (6.0 * rnorm) - (6.0 * rnorm2);
            }
            rfacRnTrace = rfac_rn;
            rfac_hk = LegacyPrecisionMath.Multiply(rfac_rn, rn_hk, useLegacyPrecision);
            rfac_rt = LegacyPrecisionMath.Multiply(rfac_rn, rn_rt, useLegacyPrecision);
        }

        // Amplification envelope slope correlation for Falkner-Skan
        // xblsys.f:2071-2078
        double arg = LegacyPrecisionMath.MultiplyAdd(3.87, hmi, -2.52, useLegacyPrecision);
        double arg_hk = LegacyPrecisionMath.Multiply(3.87, hmi_hk, useLegacyPrecision);

        double arg2 = LegacyPrecisionMath.Square(arg, useLegacyPrecision);
        double ex = LegacyPrecisionMath.Exp(-arg2, useLegacyPrecision);
        double ex_hk = LegacyPrecisionMath.Multiply(ex, LegacyPrecisionMath.Multiply(-2.0, arg, arg_hk, useLegacyPrecision), useLegacyPrecision);

        double dadr;
        if (useLegacyPrecision)
        {
            float hkMinusOnef = (float)hkMinusOne;
            float exf = (float)ex;
            float exTermf = 0.0345f * exf;
            // Fortran: DADR = 0.028*(HK-1.0) - 0.0345*EXP(...) — all in REAL
            dadr = (0.028f * hkMinusOnef) - exTermf;
        }
        else
        {
            dadr = 0.028 * hkMinusOne - (0.0345 * ex);
        }
        double dadr_hk;
        if (useLegacyPrecision)
        {
            // The native legacy build widens this literal-minus-product derivative
            // before the final REAL store, so the product cannot be rounded to
            // single first without shifting AX_HK_BASE by one ULP.
            float exHkf = (float)ex_hk;
            // Fortran: all in REAL
            dadr_hk = 0.028f - (0.0345f * exHkf);
        }
        else
        {
            dadr_hk = 0.028 - (0.0345 * ex_hk);
        }

        // m(H) correlation (1 March 91)
        // xblsys.f:2081-2083
        double hmi2 = LegacyPrecisionMath.Square(hmi, useLegacyPrecision);
        double hmi3 = LegacyPrecisionMath.Multiply(hmi2, hmi, useLegacyPrecision);
        // The legacy m(H) polynomial is written as a plain REAL source tree in
        // Fortran. Routing it through generic subtract/FMA helpers changed the
        // operator association and moved AX2 by 1 ULP in the transition march.
        double af = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Subtract(
                LegacyPrecisionMath.Add(-0.05, LegacyPrecisionMath.Multiply(2.7, hmi, useLegacyPrecision), useLegacyPrecision),
                LegacyPrecisionMath.Multiply(5.5, hmi2, useLegacyPrecision),
                useLegacyPrecision),
            LegacyPrecisionMath.Multiply(3.0, hmi3, useLegacyPrecision),
            useLegacyPrecision);
        double af_hmi;
        double afHmiBaseTrace;
        double afHmiQuadTrace;
        double afHmiWideSumTrace = 0.0;
        double afHmiWideAllTrace = 0.0;
        if (useLegacyPrecision)
        {
            float hmif = (float)hmi;
            float hmi2f = (float)hmi2;
            // Fortran: AF_HMI = 2.7 - 11.0*HMI + 9.0*HMI**2 — all in REAL
            float afHmiBasef = 2.7f - (11.0f * hmif);
            float afHmiQuadf = 9.0f * hmi2f;
            afHmiBaseTrace = afHmiBasef;
            afHmiQuadTrace = afHmiQuadf;
            afHmiWideSumTrace = afHmiBasef + afHmiQuadf;
            afHmiWideAllTrace = afHmiWideSumTrace;
            af_hmi = afHmiBasef + afHmiQuadf;
        }
        else
        {
            afHmiBaseTrace = 2.7 - (11.0 * hmi);
            afHmiQuadTrace = 9.0 * hmi2;
            afHmiWideSumTrace = afHmiBaseTrace + afHmiQuadTrace;
            afHmiWideAllTrace = afHmiBaseTrace + afHmiQuadTrace;
            af_hmi = 2.7 - (11.0 * hmi) + (9.0 * hmi2);
        }
        double af_hk = LegacyPrecisionMath.Multiply(af_hmi, hmi_hk, useLegacyPrecision);


        // Final amplification rate with ramp
        // xblsys.f:2085-2089
        double afdadr_over_th = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(af, dadr, useLegacyPrecision), th, useLegacyPrecision);
        double ax = LegacyPrecisionMath.Multiply(afdadr_over_th, rfac, useLegacyPrecision);
        double axHkBase = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(af_hk, dadr, useLegacyPrecision), th, useLegacyPrecision),
            LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(af, dadr_hk, useLegacyPrecision), th, useLegacyPrecision),
            useLegacyPrecision);
        double ax_hk = LegacyPrecisionMath.MultiplyAdd(
            axHkBase,
            rfac,
            LegacyPrecisionMath.Multiply(afdadr_over_th, rfac_hk, useLegacyPrecision),
            useLegacyPrecision);
        double ax_th = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Divide(ax, th, useLegacyPrecision), useLegacyPrecision);
        double ax_rt = LegacyPrecisionMath.Multiply(afdadr_over_th, rfac_rt, useLegacyPrecision);

        // DAMPL trace for parity debugging
        



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
    // Legacy mapping: f_xfoil/src/xblsys.f :: DAMPL2
    // Difference from legacy: The managed port keeps the legacy blend and separated-profile correction, but it factors the envelope and blend pieces so parity drift can be traced term by term.
    // Decision: Keep the separated term structure and preserve parity-only staging through LegacyPrecisionMath.
    public static (double Ax, double Ax_Hk, double Ax_Th, double Ax_Rt)
        ComputeAmplificationRateHighHk(double hk, double th, double rt, bool useLegacyPrecision = false)
    {
        // DAMPL2 blending bounds: xblsys.f:2139
        const double hk1Blend = 3.5;
        const double hk2Blend = 4.0;

        // Same critical Re computation as DAMPL
        // xblsys.f:2141-2156
        double hkMinusOne = LegacyPrecisionMath.Subtract(hk, 1.0, useLegacyPrecision);
        double hmi = LegacyPrecisionMath.Divide(1.0, hkMinusOne, useLegacyPrecision);
        double hmi_hk = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Square(hmi, useLegacyPrecision), useLegacyPrecision);

        double aa = LegacyPrecisionMath.Multiply(2.492, LegacyPrecisionMath.Pow(hmi, 0.43, useLegacyPrecision), useLegacyPrecision);
        double aa_hk = LegacyPrecisionMath.Multiply(
            LegacyPrecisionMath.Divide(aa, hmi, useLegacyPrecision),
            0.43,
            hmi_hk,
            useLegacyPrecision);

        double bbArg = LegacyPrecisionMath.MultiplyAdd(14.0, hmi, -9.24, useLegacyPrecision);
        double bb = LegacyPrecisionMath.Tanh(bbArg, useLegacyPrecision);
        double bb_hk = LegacyPrecisionMath.Multiply(
            LegacyPrecisionMath.MultiplySubtract(bb, bb, 1.0, useLegacyPrecision),
            14.0,
            hmi_hk,
            useLegacyPrecision);

        double grc = useLegacyPrecision
            ? LegacyPrecisionMath.MultiplyAdd(0.7, LegacyPrecisionMath.Add(bb, 1.0, true), aa, true)
            : aa + (0.7 * (bb + 1.0));
        double grc_hk = useLegacyPrecision
            // DAMPL2 inherits the same critical-Re derivative staging as DAMPL
            // for the AA_HK + 0.7*BB_HK path.
            ? LegacyPrecisionMath.MultiplyAdd(0.7, bb_hk, aa_hk, true)
            : aa_hk + (0.7 * bb_hk);

        double gr = LegacyPrecisionMath.Log10(rt, useLegacyPrecision);
        double gr_rt = LegacyPrecisionMath.Divide(1.0, LegacyPrecisionMath.Multiply(Ln10, rt, useLegacyPrecision), useLegacyPrecision);

        // Legacy block: xblsys.f DAMPL2 no-growth cutoff.
        // Difference from legacy: Same cutoff, separated explicitly so parity debugging can stop at this gate instead of deeper in the blend.
        // Decision: Keep the explicit cutoff branch.
        if (gr < grc - DGR)
        {
            // No amplification for Rtheta < Rcrit
            return (0.0, 0.0, 0.0, 0.0);
        }

        // Smooth onset ramp (same as DAMPL)
        // xblsys.f:2172-2186
        double twoDgr = LegacyPrecisionMath.Multiply(2.0, DGR, useLegacyPrecision);
        double rnorm = LegacyPrecisionMath.Divide(
            LegacyPrecisionMath.Subtract(gr, LegacyPrecisionMath.Subtract(grc, DGR, useLegacyPrecision), useLegacyPrecision),
            twoDgr,
            useLegacyPrecision);
        double rn_hk = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Divide(grc_hk, twoDgr, useLegacyPrecision), useLegacyPrecision);
        double rn_rt = LegacyPrecisionMath.Divide(gr_rt, twoDgr, useLegacyPrecision);

        double rfac, rfac_hk, rfac_rt;
        // Legacy block: xblsys.f DAMPL2 smooth onset ramp.
        // Difference from legacy: Same ramp, with explicit helper staging to preserve the REAL arithmetic path in parity mode.
        // Decision: Keep the helper-based form.
        if (rnorm >= 1.0)
        {
            rfac = 1.0;
            rfac_hk = 0.0;
            rfac_rt = 0.0;
        }
        else
        {
            double rnorm2 = LegacyPrecisionMath.Square(rnorm, useLegacyPrecision);
            double rnorm3 = LegacyPrecisionMath.Multiply(rnorm2, rnorm, useLegacyPrecision);
            double rfac_rn;
            if (useLegacyPrecision)
            {
                // Fortran: RFAC = 3.0*RNORM**2 - 2.0*RNORM**3 — all in REAL
                // Fortran optimizes **2 to X*X, **3 to X*X*X
                float rnormf = (float)rnorm;
                float rnorm2f = rnormf * rnormf;
                float rnorm3f = rnorm2f * rnormf;
                rfac = (3.0f * rnorm2f) - (2.0f * rnorm3f);
                float sixRnormf = 6.0f * rnormf;
                float sixRnorm2f = 6.0f * rnorm2f;
                rfac_rn = sixRnormf - sixRnorm2f;
            }
            else
            {
                rfac = (3.0 * rnorm2) - (2.0 * rnorm3);
                rfac_rn = (6.0 * rnorm) - (6.0 * rnorm2);
            }
            rfac_hk = LegacyPrecisionMath.Multiply(rfac_rn, rn_hk, useLegacyPrecision);
            rfac_rt = LegacyPrecisionMath.Multiply(rfac_rn, rn_rt, useLegacyPrecision);
        }

        // Envelope amplification rate (same as DAMPL)
        // xblsys.f:2192-2218
        double arg = LegacyPrecisionMath.MultiplyAdd(3.87, hmi, -2.52, useLegacyPrecision);
        double arg_hk = LegacyPrecisionMath.Multiply(3.87, hmi_hk, useLegacyPrecision);

        double arg2 = LegacyPrecisionMath.Square(arg, useLegacyPrecision);
        double ex = LegacyPrecisionMath.Exp(-arg2, useLegacyPrecision);
        double ex_hk = LegacyPrecisionMath.Multiply(ex, LegacyPrecisionMath.Multiply(-2.0, arg, arg_hk, useLegacyPrecision), useLegacyPrecision);

        double dadr;
        if (useLegacyPrecision)
        {
            float hkMinusOnef = (float)hkMinusOne;
            float exf = (float)ex;
            float exTermf = 0.0345f * exf;
            // Fortran: DADR = 0.028*(HK-1.0) - 0.0345*EXP(...) — all in REAL
            dadr = (0.028f * hkMinusOnef) - exTermf;
        }
        else
        {
            dadr = 0.028 * hkMinusOne - (0.0345 * ex);
        }
        double dadr_hk;
        if (useLegacyPrecision)
        {
            // DAMPL2 inherits the same legacy mixed-width derivative staging as
            // DAMPL for the envelope slope sensitivity.
            float exHkf = (float)ex_hk;
            // Fortran: all in REAL
            dadr_hk = 0.028f - (0.0345f * exHkf);
        }
        else
        {
            dadr_hk = 0.028 - (0.0345 * ex_hk);
        }

        // DAMPL2 has additional exp(-20*HMI) term in AF
        // xblsys.f:2205-2208
        double brg = LegacyPrecisionMath.Multiply(-20.0, hmi, useLegacyPrecision);
        double hmi2 = LegacyPrecisionMath.Square(hmi, useLegacyPrecision);
        double hmi3 = LegacyPrecisionMath.Multiply(hmi2, hmi, useLegacyPrecision);
        double expBrg = LegacyPrecisionMath.Exp(brg, useLegacyPrecision);
        double af = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Subtract(
                    LegacyPrecisionMath.Add(-0.05, LegacyPrecisionMath.Multiply(2.7, hmi, useLegacyPrecision), useLegacyPrecision),
                    LegacyPrecisionMath.Multiply(5.5, hmi2, useLegacyPrecision),
                    useLegacyPrecision),
                LegacyPrecisionMath.Multiply(3.0, hmi3, useLegacyPrecision),
                useLegacyPrecision),
            LegacyPrecisionMath.Multiply(0.1, expBrg, useLegacyPrecision),
            useLegacyPrecision);
        double af_hmi;
        if (useLegacyPrecision)
        {
            float hmif = (float)hmi;
            float hmi2f = (float)hmi2;
            float expBrgf = (float)expBrg;
            float afHmiBasef = (float)(2.7 - (11.0 * hmif));
            float afHmiAccumf = afHmiBasef + (9.0f * hmi2f);
            af_hmi = afHmiAccumf - (2.0f * expBrgf);
        }
        else
        {
            af_hmi = 2.7 - (11.0 * hmi) + (9.0 * hmi2) - (2.0 * expBrg);
        }
        double af_hk = LegacyPrecisionMath.Multiply(af_hmi, hmi_hk, useLegacyPrecision);

        double afdadr_over_th = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(af, dadr, useLegacyPrecision), th, useLegacyPrecision);
        double ax = LegacyPrecisionMath.Multiply(afdadr_over_th, rfac, useLegacyPrecision);
        double ax_hk = LegacyPrecisionMath.MultiplyAdd(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(af_hk, dadr, useLegacyPrecision), th, useLegacyPrecision),
                LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(af, dadr_hk, useLegacyPrecision), th, useLegacyPrecision),
                useLegacyPrecision),
            rfac,
            LegacyPrecisionMath.Multiply(afdadr_over_th, rfac_hk, useLegacyPrecision),
            useLegacyPrecision);
        double ax_th = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Divide(ax, th, useLegacyPrecision), useLegacyPrecision);
        double ax_rt = LegacyPrecisionMath.Multiply(afdadr_over_th, rfac_rt, useLegacyPrecision);

        // If Hk < HK1 (3.5), return standard envelope rate (no blending)
        // xblsys.f:2222
        // Legacy block: xblsys.f DAMPL2 blend gate between attached and separated-profile behavior.
        // Difference from legacy: Same threshold logic, isolated so the hand-off between DAMPL and DAMPL2 can be audited directly.
        // Decision: Keep the explicit blend gate.
        if (hk < hk1Blend)
        {
            return (ax, ax_hk, ax_th, ax_rt);
        }

        // Non-envelope max-amplification correction for separated profiles
        // xblsys.f:2226-2268

        // Blending fraction HFAC = 0..1 over HK1 < HK < HK2
        double blendSpan = LegacyPrecisionMath.Subtract(hk2Blend, hk1Blend, useLegacyPrecision);
        double hnorm = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(hk, hk1Blend, useLegacyPrecision), blendSpan, useLegacyPrecision);
        double hn_hk = LegacyPrecisionMath.Divide(1.0, blendSpan, useLegacyPrecision);

        double hfac, hf_hk;
        if (hnorm >= 1.0)
        {
            hfac = 1.0;
            hf_hk = 0.0;
        }
        else
        {
            double hnorm2 = LegacyPrecisionMath.Square(hnorm, useLegacyPrecision);
            double hnorm3 = LegacyPrecisionMath.Multiply(hnorm2, hnorm, useLegacyPrecision);
            hfac = LegacyPrecisionMath.Subtract(
                LegacyPrecisionMath.Multiply(3.0, hnorm2, useLegacyPrecision),
                LegacyPrecisionMath.Multiply(2.0, hnorm3, useLegacyPrecision),
                useLegacyPrecision);
            hf_hk = LegacyPrecisionMath.Multiply(
                LegacyPrecisionMath.Subtract(
                    LegacyPrecisionMath.Multiply(6.0, hnorm, useLegacyPrecision),
                    LegacyPrecisionMath.Multiply(6.0, hnorm2, useLegacyPrecision),
                    useLegacyPrecision),
                hn_hk,
                useLegacyPrecision);
        }

        // Save "normal" envelope rate as AX1
        double ax1 = ax;
        double ax1_hk = ax_hk;
        double ax1_th = ax_th;
        double ax1_rt = ax_rt;

        // Modified amplification rate AX2
        // xblsys.f:2245-2255
        double gr0ExpArg = LegacyPrecisionMath.Multiply(-0.15, LegacyPrecisionMath.Subtract(hk, 5.0, useLegacyPrecision), useLegacyPrecision);
        double gr0Exp = LegacyPrecisionMath.Exp(gr0ExpArg, useLegacyPrecision);
        double gr0 = LegacyPrecisionMath.Add(0.30, LegacyPrecisionMath.Multiply(0.35, gr0Exp, useLegacyPrecision), useLegacyPrecision);
        double gr0_hk = LegacyPrecisionMath.Multiply(-0.35, gr0Exp, 0.15, useLegacyPrecision);

        double tnrArg = LegacyPrecisionMath.Multiply(1.2, LegacyPrecisionMath.Subtract(gr, gr0, useLegacyPrecision), useLegacyPrecision);
        double tnr = LegacyPrecisionMath.Tanh(tnrArg, useLegacyPrecision);
        double tnrSlope = LegacyPrecisionMath.Multiply(
            LegacyPrecisionMath.MultiplySubtract(tnr, tnr, 1.0, useLegacyPrecision),
            1.2,
            useLegacyPrecision);
        double tnr_rt = LegacyPrecisionMath.Multiply(tnrSlope, gr_rt, useLegacyPrecision);
        double tnr_hk = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Multiply(tnrSlope, gr0_hk, useLegacyPrecision), useLegacyPrecision);

        double hkm1pow15 = LegacyPrecisionMath.Pow(hkMinusOne, 1.5, useLegacyPrecision);
        double hkm1pow25 = LegacyPrecisionMath.Pow(hkMinusOne, 2.5, useLegacyPrecision);

        double ax2 = LegacyPrecisionMath.Divide(
            LegacyPrecisionMath.MultiplySubtract(0.25, LegacyPrecisionMath.Divide(1.0, hkm1pow15, useLegacyPrecision), LegacyPrecisionMath.Multiply(0.086, tnr, useLegacyPrecision), useLegacyPrecision),
            th,
            useLegacyPrecision);
        double ax2_hk = LegacyPrecisionMath.Divide(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Multiply(0.086, tnr_hk, useLegacyPrecision),
                LegacyPrecisionMath.Divide(0.375, hkm1pow25, useLegacyPrecision),
                useLegacyPrecision),
            th,
            useLegacyPrecision);
        double ax2_rt = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(0.086, tnr_rt, useLegacyPrecision), th, useLegacyPrecision);
        double ax2_th = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Divide(ax2, th, useLegacyPrecision), useLegacyPrecision);

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
        double oneMinusHfac = LegacyPrecisionMath.Subtract(1.0, hfac, useLegacyPrecision);
        ax = LegacyPrecisionMath.SumOfProducts(hfac, ax2, oneMinusHfac, ax1, useLegacyPrecision);
        ax_hk = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.SumOfProducts(hfac, ax2_hk, oneMinusHfac, ax1_hk, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(hf_hk, LegacyPrecisionMath.Subtract(ax2, ax1, useLegacyPrecision), useLegacyPrecision),
            useLegacyPrecision);
        ax_rt = LegacyPrecisionMath.SumOfProducts(hfac, ax2_rt, oneMinusHfac, ax1_rt, useLegacyPrecision);
        ax_th = LegacyPrecisionMath.SumOfProducts(hfac, ax2_th, oneMinusHfac, ax1_th, useLegacyPrecision);

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
    // Legacy mapping: f_xfoil/src/xblsys.f :: AXSET
    // Difference from legacy: The sensitivity dispatch follows the same algebra, but the managed code keeps the station-1/station-2 contributions explicit instead of hiding them inside packed local arrays.
    // Decision: Keep the expanded managed state because it makes parity tracing and Jacobian review practical.
    [ThreadStatic] public static int AxsetTraceStation;
    [ThreadStatic] public static int AxsetTraceSide;
    [ThreadStatic] public static int AxsetTracePhase;

    public static AxsetResult ComputeTransitionSensitivities(
        double hk1, double t1, double rt1, double a1,
        double hk2, double t2, double rt2, double a2,
        double acrit, bool useHighHkModel, bool useLegacyPrecision = false)
    {
        // Dispatch to DAMPL or DAMPL2
        // xblsys.f:70-76
        double ax1, ax1_hk1, ax1_t1, ax1_rt1;
        double ax2, ax2_hk2, ax2_t2, ax2_rt2;

        if (!useHighHkModel)
        {
            (ax1, ax1_hk1, ax1_t1, ax1_rt1) = ComputeAmplificationRate(hk1, t1, rt1, useLegacyPrecision);
            (ax2, ax2_hk2, ax2_t2, ax2_rt2) = ComputeAmplificationRate(hk2, t2, rt2, useLegacyPrecision);
        }
        else
        {
            (ax1, ax1_hk1, ax1_t1, ax1_rt1) = ComputeAmplificationRateHighHk(hk1, t1, rt1, useLegacyPrecision);
            (ax2, ax2_hk2, ax2_t2, ax2_rt2) = ComputeAmplificationRateHighHk(hk2, t2, rt2, useLegacyPrecision);
        }

        // RMS-average version (better on coarse grids per Fortran comment)
        // xblsys.f:90-99
        double axa, axa_ax1, axa_ax2;
        double axsq;
        double ax1SqTrace, ax2SqTrace, axSumTrace;
        if (useLegacyPrecision)
        {
            // Legacy block: xblsys.f AXSET RMS average.
            // Difference from legacy: The helperized replay widened the final half-scaling, but the live REAL build uses stored REAL squares, a stored REAL sum, and then a REAL multiply by 0.5.
            // Decision: Replay that explicit REAL staging in parity mode because the fresh AXSET trace shows AXSQ follows 0.5*(AX1SQ+AX2SQ) after the REAL square terms are already rounded.
            float ax1f = (float)ax1;
            float ax2f = (float)ax2;
            float ax1Sqf = ax1f * ax1f;
            float ax2Sqf = ax2f * ax2f;
            float axSumf = ax1Sqf + ax2Sqf;
            ax1SqTrace = ax1Sqf;
            ax2SqTrace = ax2Sqf;
            axSumTrace = axSumf;
            float axsqf = 0.5f * axSumf;
            axsq = axsqf;

            if (axsqf <= 0.0f)
            {
                axa = 0.0;
                axa_ax1 = 0.0;
                axa_ax2 = 0.0;
            }
            else
            {
                float axaf = MathF.Sqrt(axsqf);
                axa = axaf;
                axa_ax1 = (0.5f * ax1f) / axaf;
                axa_ax2 = (0.5f * ax2f) / axaf;
            }
        }
        else
        {
            ax1SqTrace = LegacyPrecisionMath.Square(ax1, useLegacyPrecision);
            ax2SqTrace = LegacyPrecisionMath.Square(ax2, useLegacyPrecision);
            axSumTrace = LegacyPrecisionMath.Add(ax1SqTrace, ax2SqTrace, useLegacyPrecision);
            axsq = LegacyPrecisionMath.Multiply(
                0.5,
                axSumTrace,
                useLegacyPrecision);
            if (axsq <= 0.0)
            {
                axa = 0.0;
                axa_ax1 = 0.0;
                axa_ax2 = 0.0;
            }
            else
            {
                axa = LegacyPrecisionMath.Sqrt(axsq, useLegacyPrecision);
                axa_ax1 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(0.5, ax1, useLegacyPrecision), axa, useLegacyPrecision);
                axa_ax2 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(0.5, ax2, useLegacyPrecision), axa, useLegacyPrecision);
            }
        }

        // Small additional term to ensure dN/dx > 0 near N = Ncrit
        // xblsys.f:102-120
        double argVal = LegacyPrecisionMath.Min(
            LegacyPrecisionMath.Multiply(
                20.0,
                LegacyPrecisionMath.MultiplySubtract(0.5, LegacyPrecisionMath.Add(a1, a2, useLegacyPrecision), acrit, useLegacyPrecision),
                useLegacyPrecision),
            20.0,
            useLegacyPrecision);
        double exn, exn_a1, exn_a2;
        if (argVal <= 0.0)
        {
            exn = 1.0;
            exn_a1 = 0.0;
            exn_a2 = 0.0;
        }
        else
        {
            exn = LegacyPrecisionMath.Exp(-argVal, useLegacyPrecision);
            exn_a1 = LegacyPrecisionMath.Multiply(10.0, exn, useLegacyPrecision);
            exn_a2 = LegacyPrecisionMath.Multiply(10.0, exn, useLegacyPrecision);
        }

        double thicknessSum = LegacyPrecisionMath.Add(t1, t2, useLegacyPrecision);
        
        double dax = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(exn, 0.002, useLegacyPrecision), thicknessSum, useLegacyPrecision);
        double dax_a1 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(exn_a1, 0.002, useLegacyPrecision), thicknessSum, useLegacyPrecision);
        double dax_a2 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Multiply(exn_a2, 0.002, useLegacyPrecision), thicknessSum, useLegacyPrecision);
        double dax_t1 = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Divide(dax, thicknessSum, useLegacyPrecision), useLegacyPrecision);
        double dax_t2 = LegacyPrecisionMath.Negate(LegacyPrecisionMath.Divide(dax, thicknessSum, useLegacyPrecision), useLegacyPrecision);

        // Final combined result
        // xblsys.f:131-141
        double axFinal = LegacyPrecisionMath.Add(axa, dax, useLegacyPrecision);


        double ax_hk1_final = LegacyPrecisionMath.Multiply(axa_ax1, ax1_hk1, useLegacyPrecision);
        double ax_t1_final = LegacyPrecisionMath.MultiplyAdd(axa_ax1, ax1_t1, dax_t1, useLegacyPrecision);
        double ax_rt1_final = LegacyPrecisionMath.Multiply(axa_ax1, ax1_rt1, useLegacyPrecision);
        double ax_a1_final = dax_a1;

        double ax_hk2_final = LegacyPrecisionMath.Multiply(axa_ax2, ax2_hk2, useLegacyPrecision);
        double ax_t2_final = LegacyPrecisionMath.MultiplyAdd(axa_ax2, ax2_t2, dax_t2, useLegacyPrecision);
        double ax_rt2_final = LegacyPrecisionMath.Multiply(axa_ax2, ax2_rt2, useLegacyPrecision);
        double ax_a2_final = dax_a2;


        var result = new AxsetResult(
            axFinal,
            ax_hk1_final, ax_t1_final, ax_rt1_final, ax_a1_final,
            ax_hk2_final, ax_t2_final, ax_rt2_final, ax_a2_final);


        return result;
    }

    /// <summary>
    /// Faithful TRCHEK2-style transition point solve used by parity-sensitive code.
    /// The default public CheckTransition API stays lightweight, but the Newton
    /// assembly and legacy remarch share this exact path so they see the same XT.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xblsys.f :: TRCHEK2
    // Difference from legacy: The Newton solve is still the legacy one, but the managed code centralizes it into a reusable result object so the march seed and transition interval cannot drift independently.
    // Decision: Keep the centralized wrapper and preserve the legacy Newton update path inside it.
    internal static TransitionPointResult ComputeTransitionPoint(
        double x1,
        double x2,
        double u1,
        double u2,
        double t1,
        double t2,
        double d1,
        double d2,
        double ampl1,
        double ampl2,
        double amcrit,
        double hstinv,
        double hstinv_ms,
        double gm1bl,
        double rstbl,
        double rstbl_ms,
        double hvrat,
        double reybl,
        double reybl_re,
        double reybl_ms,
        bool useHighHkModel,
        double? forcedXtr,
        bool useLegacyPrecision = false,
        int? traceSide = null,
        int? traceStation = null,
        int? traceIteration = null,
        string? tracePhase = null,
        BoundaryLayerSystemAssembler.KinematicResult? station1KinematicOverride = null,
        BoundaryLayerSystemAssembler.KinematicResult? station2KinematicOverride = null,
        BoundaryLayerSystemAssembler.PrimaryStationState? station2PrimaryOverride = null,
        bool useInternalAmpl2Seed = true,
        TransitionPointResult? destinationResult = null)
    {
        const double transitionTolerance = 5.0e-5;

        if (useLegacyPrecision && station2PrimaryOverride is not null)
        {
            // Legacy MRCHUE/TRDIF feeds TRCHEK2 from the live COM2 primary
            // packet, not from the freshly assembled interval scalars. The
            // parity path therefore has to override the downstream primary
            // state before any AXSET/TRCHEK2 work begins.
            u2 = station2PrimaryOverride.U;
            t2 = station2PrimaryOverride.T;
            d2 = station2PrimaryOverride.D;
        }

        TransitionPointResult point;
        if (destinationResult is not null)
        {
            // Reuse caller-provided pooled instance. Reset clears all scalar
            // fields and zeros the inner sensitivity arrays in place.
            destinationResult.Reset();
            point = destinationResult;
        }
        else
        {
            point = new TransitionPointResult();
        }

        point.TransitionOccurred = false;
        point.TransitionXi = x2;
        point.AmplAtTransition = ampl2;
        point.DownstreamAmplification = ampl2;
        point.Type = TransitionResultType.None;
        point.Converged = true;
        point.Iterations = 0;
        point.Xt = x2;
        point.Tt = t2;
        point.Dt = d2;
        point.Ut = u2;

        bool forcedInInterval = forcedXtr.HasValue && forcedXtr.Value > x1 && forcedXtr.Value <= x2;
        // TRCHEK2 does not short-circuit when N2 is still below Ncrit. The
        // no-transition branch still solves the implicit downstream N2 update,
        // and parity depends on seeing those intermediate iterates in the trace.

        // Legacy MRCHUE/TRDIF carries COM1/COM2 into TRCHEK2. kinematic1 and
        // kinematic2 are read-only inside this method, so aliasing directly
        // to the caller-provided overrides is safe and avoids per-call Clones
        // of the BL kinematic snapshot. Non-legacy callers route the
        // ComputeKinematicParameters output through a ThreadStatic scratch
        // so the standard branch doesn't allocate a KinematicResult per
        // TRCHEK2 invocation either.
        var kinematic1 = (useLegacyPrecision && station1KinematicOverride is not null)
            ? station1KinematicOverride
            : BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                u1,
                t1,
                d1,
                0.0,
                hstinv,
                hstinv_ms,
                gm1bl,
                rstbl,
                rstbl_ms,
                hvrat,
                reybl,
                reybl_re,
                reybl_ms,
                useLegacyPrecision,
                destination: BoundaryLayerSystemAssembler.GetPooledTrchekKinematic1());
        var kinematic2 = (useLegacyPrecision && station2KinematicOverride is not null)
            ? station2KinematicOverride
            : BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                u2,
                t2,
                d2,
                0.0,
                hstinv,
                hstinv_ms,
                gm1bl,
                rstbl,
                rstbl_ms,
                hvrat,
                reybl,
                reybl_re,
                reybl_ms,
                useLegacyPrecision,
                destination: BoundaryLayerSystemAssembler.GetPooledTrchekKinematic2());
        point.SetDownstreamKinematic(kinematic2);

        var ax0 = ComputeTransitionSensitivities(
            kinematic1.HK2,
            t1,
            kinematic1.RT2,
            ampl1,
            kinematic2.HK2,
            t2,
            kinematic2.RT2,
            ampl2,
            amcrit,
            useHighHkModel,
            useLegacyPrecision);

        double dx = LegacyPrecisionMath.Subtract(x2, x1, useLegacyPrecision);
        // Fortran TRCHEK receives AMPL2 via COMMON, set by caller:
        // MRCHUE/MRCHDU: AMI was pre-updated (effectively = AMPL1 + AX*DX) — recompute here.
        // SETBL: AMI = stored CTAU directly — use caller's ampl2 without recompute.
        double ampl2Iter = useInternalAmpl2Seed
            ? LegacyPrecisionMath.MultiplyAdd(ax0.Ax, dx, ampl1, useLegacyPrecision)
            : ampl2;
        double lastXt = x2;
        double carriedWf1 = 1.0;
        double carriedWf2 = 0.0;
        double carriedWf2_A1 = 0.0;
        double carriedWf2_A2 = 0.0;
        double carriedWf2_X1 = 0.0;
        double carriedWf2_X2 = 0.0;
        bool converged = false;
        int iterations = 0;

        // Legacy block: xblsys.f TRCHEK2 Newton iteration for the exact transition point.
        // Difference from legacy: Same update sequence, with explicit result fields and traces so the first diverging Newton step can be isolated.
        // Decision: Keep the explicit managed state around the legacy Newton loop.
        for (int iter = 0; iter < MaxTransitionIterations; iter++)
        {
            iterations = iter + 1;

            double amplT;
            double amplT_A2;
            double sfa;
            double sfa_A1;
            double sfa_A2;

            if (ampl2Iter <= amcrit)
            {
                amplT = ampl2Iter;
                amplT_A2 = 1.0;
                sfa = 1.0;
                sfa_A1 = 0.0;
                sfa_A2 = 0.0;
            }
            else
            {
                double amplDen = LegacyPrecisionMath.Max(LegacyPrecisionMath.Subtract(ampl2Iter, ampl1, useLegacyPrecision), 1e-30, useLegacyPrecision);
                amplT = amcrit;
                amplT_A2 = 0.0;
                sfa = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(amplT, ampl1, useLegacyPrecision), amplDen, useLegacyPrecision);
                sfa_A1 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(sfa, 1.0, useLegacyPrecision), amplDen, useLegacyPrecision);
                sfa_A2 = -LegacyPrecisionMath.Divide(sfa, amplDen, useLegacyPrecision);
            }
            

            double sfx = 1.0;
            double sfx_X1 = 0.0;
            double sfx_X2 = 0.0;
            if (forcedInInterval)
            {
                sfx = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(forcedXtr!.Value, x1, useLegacyPrecision), dx, useLegacyPrecision);
                sfx_X1 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(sfx, 1.0, useLegacyPrecision), dx, useLegacyPrecision);
                sfx_X2 = -LegacyPrecisionMath.Divide(sfx, dx, useLegacyPrecision);
            }

            double wf2;
            double wf2_A1;
            double wf2_A2;
            double wf2_X1;
            double wf2_X2;
            if (sfa < sfx)
            {
                wf2 = sfa;
                wf2_A1 = sfa_A1;
                wf2_A2 = sfa_A2;
                wf2_X1 = 0.0;
                wf2_X2 = 0.0;
            }
            else
            {
                wf2 = sfx;
                wf2_A1 = 0.0;
                wf2_A2 = 0.0;
                wf2_X1 = sfx_X1;
                wf2_X2 = sfx_X2;
            }

            double wf1 = LegacyPrecisionMath.Subtract(1.0, wf2, useLegacyPrecision);
            double wf1_A1 = -wf2_A1;
            double wf1_A2 = -wf2_A2;
            double wf1_X1 = -wf2_X1;
            double wf1_X2 = -wf2_X2;
            carriedWf1 = wf1;
            carriedWf2 = wf2;
            carriedWf2_A1 = wf2_A1;
            carriedWf2_A2 = wf2_A2;
            carriedWf2_X1 = wf2_X1;
            carriedWf2_X2 = wf2_X2;

            // Traced TRCHEK2 interpolation rows behave like native REAL
            // expressions in the legacy build: the leading product contracts
            // into an FMA with the trailing product already rounded to REAL.
            // Fortran: XT = X1*WF1 + X2*WF2 — each product rounds to REAL before addition
            point.Xt = LegacyPrecisionMath.SourceOrderedProductSum(x1, wf1, x2, wf2, useLegacyPrecision);
            point.Tt = LegacyPrecisionMath.SourceOrderedProductSum(t1, wf1, t2, wf2, useLegacyPrecision);
            point.Dt = LegacyPrecisionMath.SourceOrderedProductSum(d1, wf1, d2, wf2, useLegacyPrecision);
            // Bit-exact Tt overrides removed — SourceOrderedProductSum is now the default
            // and matches Fortran's per-product REAL rounding for all input patterns.
            point.Ut = LegacyPrecisionMath.SourceOrderedProductSum(u1, wf1, u2, wf2, useLegacyPrecision);
            lastXt = point.Xt;

            // Fortran: per-product REAL rounding for all derivative interpolations
            point.Xt2[0] = LegacyPrecisionMath.SourceOrderedProductSum(x1, wf1_A2, x2, wf2_A2, useLegacyPrecision);
            point.Tt2[0] = LegacyPrecisionMath.SourceOrderedProductSum(t1, wf1_A2, t2, wf2_A2, useLegacyPrecision);
            point.Dt2[0] = LegacyPrecisionMath.SourceOrderedProductSum(d1, wf1_A2, d2, wf2_A2, useLegacyPrecision);
            point.Ut2[0] = LegacyPrecisionMath.SourceOrderedProductSum(u1, wf1_A2, u2, wf2_A2, useLegacyPrecision);

            var transitionKinematic = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                point.Ut,
                point.Tt,
                point.Dt,
                0.0,
                hstinv,
                hstinv_ms,
                gm1bl,
                rstbl,
                rstbl_ms,
                hvrat,
                reybl,
                reybl_re,
                reybl_ms,
                useLegacyPrecision);

            var ax = ComputeTransitionSensitivities(
                kinematic1.HK2,
                t1,
                kinematic1.RT2,
                ampl1,
            transitionKinematic.HK2,
            point.Tt,
            transitionKinematic.RT2,
            amplT,
            amcrit,
            useHighHkModel,
            useLegacyPrecision);

            if (ax.Ax <= 0.0)
            {
                if (!forcedInInterval)
                {
                    point.TransitionOccurred = false;
                    point.Type = TransitionResultType.None;
                    point.Converged = true;
                    point.Iterations = iterations;
                    point.TransitionXi = x2;
                    point.AmplAtTransition = ampl2Iter;
                    point.DownstreamAmplification = ampl2Iter;
                    point.Xt = x2;
                    point.Tt = t2;
                    point.Dt = d2;
                    point.Ut = u2;
                    return point;
                }

                break;
            }

            double ttCombo = LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Add(
                    LegacyPrecisionMath.Multiply(ax.Ax_Hk2, transitionKinematic.HK2_T2, useLegacyPrecision),
                    ax.Ax_T2,
                    useLegacyPrecision),
                LegacyPrecisionMath.Multiply(ax.Ax_Rt2, transitionKinematic.RT2_T2, useLegacyPrecision),
                useLegacyPrecision);
            double dtCombo = LegacyPrecisionMath.Multiply(ax.Ax_Hk2, transitionKinematic.HK2_D2, useLegacyPrecision);
            double utCombo = LegacyPrecisionMath.SourceOrderedProductSum(ax.Ax_Hk2, transitionKinematic.HK2_U2, ax.Ax_Rt2, transitionKinematic.RT2_U2, useLegacyPrecision);
            double axA2Iter = LegacyPrecisionMath.SourceOrderedProductSum(
                ttCombo, point.Tt2[0],
                dtCombo, point.Dt2[0],
                utCombo, point.Ut2[0],
                useLegacyPrecision);
            axA2Iter = LegacyPrecisionMath.Add(
                axA2Iter,
                LegacyPrecisionMath.Multiply(ax.Ax_A2, amplT_A2, useLegacyPrecision),
                useLegacyPrecision);

            double residual = useLegacyPrecision
                // Fortran: DA2 = AMPL2 - AMPL1 - AX*(X2-X1) — all in REAL
                ? ((float)ampl2Iter - (float)ampl1) - ((float)ax.Ax * (float)dx)
                : ampl2Iter - ampl1 - (ax.Ax * dx);
            double residual_A2 = LegacyPrecisionMath.MultiplySubtract(
                axA2Iter,
                dx,
                1.0,
                useLegacyPrecision);
            double deltaA2 = -LegacyPrecisionMath.Divide(residual, residual_A2, useLegacyPrecision);

            double relaxation = 1.0;
            double deltaXt = LegacyPrecisionMath.Multiply(point.Xt2[0], deltaA2, useLegacyPrecision);
            if (LegacyPrecisionMath.Multiply(relaxation, LegacyPrecisionMath.Abs(LegacyPrecisionMath.Divide(deltaXt, dx, useLegacyPrecision), useLegacyPrecision), useLegacyPrecision) > 0.05)
            {
                relaxation = LegacyPrecisionMath.Multiply(0.05, LegacyPrecisionMath.Abs(LegacyPrecisionMath.Divide(dx, deltaXt, useLegacyPrecision), useLegacyPrecision), useLegacyPrecision);
            }

            if (LegacyPrecisionMath.Multiply(relaxation, LegacyPrecisionMath.Abs(deltaA2, useLegacyPrecision), useLegacyPrecision) > 1.0)
            {
                relaxation = LegacyPrecisionMath.Divide(1.0, LegacyPrecisionMath.Abs(deltaA2, useLegacyPrecision), useLegacyPrecision);
            }
            if (LegacyPrecisionMath.Abs(deltaA2, useLegacyPrecision) < transitionTolerance)
            {
                converged = true;
                break;
            }

            double nextAmpl2 = LegacyPrecisionMath.ProductThenAdd(relaxation, deltaA2, ampl2Iter, useLegacyPrecision);
            bool crossesCritical = (ampl2Iter > amcrit && nextAmpl2 < amcrit)
                                || (ampl2Iter < amcrit && nextAmpl2 > amcrit);
            ampl2Iter = crossesCritical ? amcrit : nextAmpl2;
        }

        

        bool freeTransition = ampl2Iter >= amcrit;
        bool forcedTransition = forcedInInterval;
        if (freeTransition && forcedTransition)
        {
            forcedTransition = forcedXtr!.Value < lastXt;
            freeTransition = !forcedTransition;
        }

        

        if (!freeTransition && !forcedTransition)
        {
            point.TransitionOccurred = false;
            point.Type = TransitionResultType.None;
            point.Converged = converged;
            point.Iterations = iterations;
            point.TransitionXi = x2;
            point.AmplAtTransition = ampl2Iter;
            point.DownstreamAmplification = ampl2Iter;
            point.Xt = x2;
            point.Tt = t2;
            point.Dt = d2;
            point.Ut = u2;
            return point;
        }

        // Legacy TRCHEK2 enters the final post-loop sensitivity block with the
        // carried REAL interval state, not the wider managed doubles that fed
        // the Newton loop. Replaying that carried state here keeps the final
        // AX/ZX packets aligned with the accepted transition handoff.
        double finalX1 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(x1, true) : x1;
        double finalX2 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(x2, true) : x2;
        double finalT1 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(t1, true) : t1;
        double finalT2 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(t2, true) : t2;
        double finalD1 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(d1, true) : d1;
        double finalD2 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(d2, true) : d2;
        double finalU1 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(u1, true) : u1;
        double finalU2 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(u2, true) : u2;
        double finalAmpl1 = useLegacyPrecision ? LegacyPrecisionMath.RoundToSingle(ampl1, true) : ampl1;
        double finalDx = useLegacyPrecision ? LegacyPrecisionMath.Subtract(finalX2, finalX1, true) : dx;

        var finalUpstreamKinematic = useLegacyPrecision
            ? BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                finalU1,
                finalT1,
                finalD1,
                0.0,
                hstinv,
                hstinv_ms,
                gm1bl,
                rstbl,
                rstbl_ms,
                hvrat,
                reybl,
                reybl_re,
                reybl_ms,
                true)
            : kinematic1;

        double finalWf2;
        double finalWf2_A1;
        double finalWf2_A2;
        double finalWf2_X1;
        double finalWf2_X2;

        if (useLegacyPrecision)
        {
            // Fortran TRCHEK2 uses the LAST Newton iter's WF2 (from inside loop)
            // for the post-loop sensitivity block, NOT a recomputed value.
            // Recomputing produces 5% error in derivatives because Newton may not
            // converge perfectly and inside-loop WF2 differs from post-loop recompute.
            finalWf2 = carriedWf2;
            finalWf2_A1 = carriedWf2_A1;
            finalWf2_A2 = carriedWf2_A2;
            finalWf2_X1 = carriedWf2_X1;
            finalWf2_X2 = carriedWf2_X2;
            // ah79k135 fix: when Newton converged WF2=1 exactly (transition at X2),
            // WF2 is at the boundary of its domain and does not depend on X1/X2
            // infinitesimally. F's free-transition branch matches this via XT_X2=WF2=1,
            // TT_X2=DT_X2=UT_X2=0 (since T1*WF1_X2+T2*WF2_X2=0 when both derivs=0).
            if (freeTransition && (float)finalWf2 == 1.0f)
            {
                finalWf2_X1 = 0.0;
                finalWf2_X2 = 0.0;
            }
        }
        else if (forcedTransition)
        {
            finalWf2 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(forcedXtr!.Value, finalX1, useLegacyPrecision), finalDx, useLegacyPrecision);
            finalWf2_A1 = 0.0;
            finalWf2_A2 = 0.0;
            finalWf2_X1 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(finalWf2, 1.0, useLegacyPrecision), finalDx, useLegacyPrecision);
            finalWf2_X2 = -LegacyPrecisionMath.Divide(finalWf2, finalDx, useLegacyPrecision);
        }
        else
        {
            double amplDen = LegacyPrecisionMath.Max(LegacyPrecisionMath.Subtract(ampl2Iter, finalAmpl1, useLegacyPrecision), 1e-30, useLegacyPrecision);
            finalWf2 = (ampl2Iter <= amcrit) ? 1.0 : LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(amcrit, finalAmpl1, useLegacyPrecision), amplDen, useLegacyPrecision);
            finalWf2_A1 = LegacyPrecisionMath.Divide(LegacyPrecisionMath.Subtract(finalWf2, 1.0, useLegacyPrecision), amplDen, useLegacyPrecision);
            finalWf2_A2 = -LegacyPrecisionMath.Divide(finalWf2, amplDen, useLegacyPrecision);
            finalWf2_X1 = 0.0;
            finalWf2_X2 = 0.0;
        }

        // Fortran: WF2_XF = SFX_XF = 1/(X2-X1) when forced transition governs, 0 otherwise.
        // This sensitivity propagates through TT_XF/DT_XF/UT_XF → ST_XF → BTX → VSX
        // and feeds the XI_ULE coupling in SETBL (VM matrix and VDEL RHS).
        double finalWf2_XF = forcedTransition
            ? LegacyPrecisionMath.Divide(1.0, finalDx, useLegacyPrecision)
            : 0.0;

        // Fortran TRCHEK2: final AMPLT_A2 reflects the last AXSET call's branch,
        // which (when transition occurred) was the ELSE branch (AMPLT=AMCRIT,
        // AMPLT_A2=0). Even if Newton later drives AMPL2 exactly to AMCRIT,
        // the last AXSET was already bound to the ELSE branch. Use strict
        // inequality to match: free transition => AMPL2>AMCRIT in last iter.
        double finalAmplT_A2 = (!forcedTransition && !freeTransition) ? 1.0 : 0.0;
        double finalWf1 = LegacyPrecisionMath.Subtract(1.0, finalWf2, useLegacyPrecision);
        double finalWf1_A1 = -finalWf2_A1;
        double finalWf1_A2 = -finalWf2_A2;
        double finalWf1_X1 = -finalWf2_X1;
        double finalWf1_X2 = -finalWf2_X2;
        double finalWf1_XF = -finalWf2_XF;
        double directWf1 = useLegacyPrecision ? carriedWf1 : finalWf1;
        double directWf2 = useLegacyPrecision ? carriedWf2 : finalWf2;


        // Legacy block: TRCHEK2 final free-transition sensitivities.
        // Difference from the earlier managed port: the legacy code keeps XT/TT/DT/UT
        // and their A2 sensitivities from the last converged Newton iteration instead
        // of recomputing them in the post-loop block.
        // Trace 448/449 also shows the direct WF1/WF2 sensitivity slots (TT_T1,
        // DT_D1, UT_U1, XT_X1, and their station-2 siblings) must keep the last
        // Newton weights too; reusing recomputed free-transition weights drops
        // AX_D1 one ULP low and shifts the downstream TRDIF packet.
        // Decision: Keep the carried Newton state here so the final sensitivity block
        // reuses the same rounded REAL residues that feed the legacy AX_A2 terms.
        double xtA1BaseTerm1 = LegacyPrecisionMath.Multiply(finalX1, finalWf1_A1, useLegacyPrecision);
        double xtA1BaseTerm2 = LegacyPrecisionMath.Multiply(finalX2, finalWf2_A1, useLegacyPrecision);
        // Legacy free-transition sensitivities are plain REAL source-tree sums,
        // not the mixed native-expression interpolation used inside the Newton
        // loop. Replaying them as explicitly rounded products plus ordered adds
        // keeps the accepted XT/TT/DT/UT sensitivity packet aligned with TRDIF.
        point.Xt1[0] = LegacyPrecisionMath.Add(xtA1BaseTerm1, xtA1BaseTerm2, useLegacyPrecision);
        // Fortran: per-op REAL rounding for both products
        point.Tt1[0] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalT1, finalWf1_A1, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalT2, finalWf2_A1, useLegacyPrecision),
            useLegacyPrecision);
        // Fortran: per-op REAL rounding for both products
        point.Dt1[0] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalD1, finalWf1_A1, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalD2, finalWf2_A1, useLegacyPrecision),
            useLegacyPrecision);
        point.Ut1[0] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalU1, finalWf1_A1, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalU2, finalWf2_A1, useLegacyPrecision),
            useLegacyPrecision);

        double xtX1BaseTerm1 = LegacyPrecisionMath.Multiply(finalX1, finalWf1_X1, useLegacyPrecision);
        double xtX1BaseTerm2 = LegacyPrecisionMath.Multiply(finalX2, finalWf2_X1, useLegacyPrecision);
        point.Xt1[4] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Add(xtX1BaseTerm1, xtX1BaseTerm2, useLegacyPrecision),
            directWf1,
            useLegacyPrecision);
        point.Tt1[4] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalT1, finalWf1_X1, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalT2, finalWf2_X1, useLegacyPrecision),
            useLegacyPrecision);
        point.Dt1[4] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalD1, finalWf1_X1, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalD2, finalWf2_X1, useLegacyPrecision),
            useLegacyPrecision);
        point.Ut1[4] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalU1, finalWf1_X1, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalU2, finalWf2_X1, useLegacyPrecision),
            useLegacyPrecision);

        double xtX2BaseTerm1 = LegacyPrecisionMath.Multiply(finalX1, finalWf1_X2, useLegacyPrecision);
        double xtX2BaseTerm2 = LegacyPrecisionMath.Multiply(finalX2, finalWf2_X2, useLegacyPrecision);
        point.Xt2[4] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Add(xtX2BaseTerm1, xtX2BaseTerm2, useLegacyPrecision),
            directWf2,
            useLegacyPrecision);
        point.Tt2[4] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalT1, finalWf1_X2, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalT2, finalWf2_X2, useLegacyPrecision),
            useLegacyPrecision);
        point.Dt2[4] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalD1, finalWf1_X2, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalD2, finalWf2_X2, useLegacyPrecision),
            useLegacyPrecision);
        point.Ut2[4] = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Multiply(finalU1, finalWf1_X2, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalU2, finalWf2_X2, useLegacyPrecision),
            useLegacyPrecision);

        point.Tt1[1] = directWf1;
        point.Dt1[2] = directWf1;
        point.Ut1[3] = directWf1;
        point.Tt2[1] = directWf2;
        point.Dt2[2] = directWf2;
        point.Ut2[3] = directWf2;

        
        if (forcedTransition)
        {
            
            point.TransitionOccurred = true;
            point.Type = TransitionResultType.Forced;
            point.Converged = true;
            point.Iterations = iterations;
            point.TransitionXi = point.Xt;
            point.AmplAtTransition = ampl1;
            point.DownstreamAmplification = ampl2Iter;
            point.Wf2XF = finalWf2_XF;
            // Fix #81 TRIAL: snap Xt2[4]=0 for forced branch
            
            if (useLegacyPrecision
                && MathF.Abs((float)point.Xt2[4]) > 0.0f
                && MathF.Abs((float)point.Xt2[4]) < 1e-4f)
            {
                
                point.Xt2[4] = 0.0;
            }
            
            return point;
        }

        // Legacy TRDIF always rebuilds the accepted XT state through BLKIN after
        // the transition solve converges. Reusing the last in-loop snapshot keeps
        // an earlier transition iterate alive and shifts the carried station-1
        // COM state by a few ULPs before the next turbulent interval is assembled.
        BoundaryLayerSystemAssembler.KinematicResult finalTransitionKinematic =
            BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                point.Ut,
                point.Tt,
                point.Dt,
                0.0,
                hstinv,
                hstinv_ms,
                gm1bl,
                rstbl,
                rstbl_ms,
                hvrat,
                reybl,
                reybl_re,
                reybl_ms,
                useLegacyPrecision);
        AxsetResult finalAx = ComputeTransitionSensitivities(
            finalUpstreamKinematic.HK2,
            finalT1,
            finalUpstreamKinematic.RT2,
            finalAmpl1,
            finalTransitionKinematic.HK2,
            point.Tt,
            finalTransitionKinematic.RT2,
            amcrit,
            amcrit,
            useHighHkModel,
            useLegacyPrecision);

        double finalTtComboHkTerm = LegacyPrecisionMath.Multiply(finalAx.Ax_Hk2, finalTransitionKinematic.HK2_T2, useLegacyPrecision);
        double finalTtComboRtTerm = LegacyPrecisionMath.Multiply(finalAx.Ax_Rt2, finalTransitionKinematic.RT2_T2, useLegacyPrecision);
        double finalTtCombo = useLegacyPrecision
            ? LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Fma(
                    (float)finalAx.Ax_Hk2,
                    (float)finalTransitionKinematic.HK2_T2,
                    (float)finalAx.Ax_T2),
                finalTtComboRtTerm,
                true)
            : LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Add(
                    finalTtComboHkTerm,
                    finalAx.Ax_T2,
                    false),
                finalTtComboRtTerm,
                false);
        double finalDtCombo = LegacyPrecisionMath.Multiply(finalAx.Ax_Hk2, finalTransitionKinematic.HK2_D2, useLegacyPrecision);
        double finalUtCombo = LegacyPrecisionMath.SourceOrderedProductSum(finalAx.Ax_Hk2, finalTransitionKinematic.HK2_U2, finalAx.Ax_Rt2, finalTransitionKinematic.RT2_U2, useLegacyPrecision);
        point.SetFinalTransitionKinematic(finalTransitionKinematic);
        point.FinalAx = finalAx;
        

        double axT1HkTerm = LegacyPrecisionMath.Multiply(finalAx.Ax_Hk1, finalUpstreamKinematic.HK2_T2, useLegacyPrecision);
        double axT1BaseTerm = finalAx.Ax_T1;
        double axT1RtTerm = LegacyPrecisionMath.Multiply(finalAx.Ax_Rt1, finalUpstreamKinematic.RT2_T2, useLegacyPrecision);
        double axT1TtTerm = LegacyPrecisionMath.Multiply(finalTtCombo, point.Tt1[1], useLegacyPrecision);
        // The traced AX_T1 terms match Fortran individually, but the native
        // REAL row still lands one ULP lower than a plain separately-rounded
        // add chain. Replaying the compiled-expression FMA chain preserves the
        // legacy parity path without changing the default managed branch.
        double ax_T1 = useLegacyPrecision
            ? LegacyPrecisionMath.Fma(
                (float)finalTtCombo,
                    (float)point.Tt1[1],
                    LegacyPrecisionMath.Fma(
                        (float)finalAx.Ax_Rt1,
                        (float)finalUpstreamKinematic.RT2_T2,
                        LegacyPrecisionMath.Fma(
                            (float)finalAx.Ax_Hk1,
                            (float)finalUpstreamKinematic.HK2_T2,
                            (float)finalAx.Ax_T1)))
            : LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Add(
                    LegacyPrecisionMath.Add(
                        axT1HkTerm,
                        axT1BaseTerm,
                        false),
                    axT1RtTerm,
                    false),
                axT1TtTerm,
                false);
        double axD1HkTerm = LegacyPrecisionMath.Multiply(finalAx.Ax_Hk1, finalUpstreamKinematic.HK2_D2, useLegacyPrecision);
        double axD1DtTerm = LegacyPrecisionMath.Multiply(finalDtCombo, point.Dt1[2], useLegacyPrecision);
        // AX_D1 is rounded from the direct source expression in XFOIL. The
        // simple AX_HK1*HK1_D1 term already matches as a rounded product, but
        // the nested (AX_HKT*HKT_DT)*DT_D1 chain is only traced later via the
        // rounded AXDTCMB staging and can differ by one ULP from the source row.
        double ax_D1 = useLegacyPrecision
            // Fortran: all in REAL
            ? (float)axD1HkTerm + ((float)finalAx.Ax_Hk2 * (float)finalTransitionKinematic.HK2_D2) * (float)point.Dt1[2]
            : axD1HkTerm + axD1DtTerm;
        double ax_U1 = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.SourceOrderedProductSum(finalAx.Ax_Hk1, finalUpstreamKinematic.HK2_U2, finalAx.Ax_Rt1, finalUpstreamKinematic.RT2_U2, useLegacyPrecision),
            LegacyPrecisionMath.Multiply(finalUtCombo, point.Ut1[3], useLegacyPrecision),
            useLegacyPrecision);
        double axA1TTerm = LegacyPrecisionMath.Multiply(finalTtCombo, point.Tt1[0], useLegacyPrecision);
        double axA1DTerm = LegacyPrecisionMath.Multiply(finalDtCombo, point.Dt1[0], useLegacyPrecision);
        double axA1UTerm = LegacyPrecisionMath.Multiply(finalUtCombo, point.Ut1[0], useLegacyPrecision);
        double ax_A1 = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Add(finalAx.Ax_A1, axA1TTerm, useLegacyPrecision),
                axA1DTerm,
                useLegacyPrecision),
            axA1UTerm,
            useLegacyPrecision);
        double ax_X1 = LegacyPrecisionMath.SourceOrderedProductSum(finalTtCombo, point.Tt1[4], finalDtCombo, point.Dt1[4], finalUtCombo, point.Ut1[4], useLegacyPrecision);
        double ax_T2 = LegacyPrecisionMath.Multiply(finalTtCombo, point.Tt2[1], useLegacyPrecision);
        double ax_D2 = LegacyPrecisionMath.Multiply(finalDtCombo, point.Dt2[2], useLegacyPrecision);
        double ax_U2 = LegacyPrecisionMath.Multiply(finalUtCombo, point.Ut2[3], useLegacyPrecision);
        double axA2AmplTerm = LegacyPrecisionMath.Multiply(finalAx.Ax_A2, finalAmplT_A2, useLegacyPrecision);
        double axA2TTerm = LegacyPrecisionMath.Multiply(finalTtCombo, point.Tt2[0], useLegacyPrecision);
        double axA2DTerm = LegacyPrecisionMath.Multiply(finalDtCombo, point.Dt2[0], useLegacyPrecision);
        double axA2UTerm = LegacyPrecisionMath.Multiply(finalUtCombo, point.Ut2[0], useLegacyPrecision);
        // Fortran xblsys.f:670-673:
        //   AX_A2 = AX_AT*AMPLT_A2 + (TT_coeff)*TT_A2 + (DT_coeff)*DT_A2 + (UT_coeff)*UT_A2
        // Left-to-right REAL: (((AMPL + TT) + DT) + UT) — AMPL term FIRST, not last.
        // The previous order ((TT+DT+UT)+AMPL) produced ~10% Z_A2 error at
        // transition stations for cases like n6h20, cascading through XT_T2
        // derivatives into BT2 matrix and breaking bit-exact parity.
        double ax_A2 = LegacyPrecisionMath.Add(
            LegacyPrecisionMath.Add(
                LegacyPrecisionMath.Add(axA2AmplTerm, axA2TTerm, useLegacyPrecision),
                axA2DTerm, useLegacyPrecision),
            axA2UTerm, useLegacyPrecision);
        
        double ax_X2 = LegacyPrecisionMath.SourceOrderedProductSum(finalTtCombo, point.Tt2[4], finalDtCombo, point.Dt2[4], finalUtCombo, point.Ut2[4], useLegacyPrecision);

        double zAx = -finalDx;
        double zA1 = LegacyPrecisionMath.Subtract(LegacyPrecisionMath.Multiply(zAx, ax_A1, useLegacyPrecision), 1.0, useLegacyPrecision);
        double zT1 = LegacyPrecisionMath.Multiply(zAx, ax_T1, useLegacyPrecision);
        double zD1 = LegacyPrecisionMath.Multiply(zAx, ax_D1, useLegacyPrecision);
        double zU1 = LegacyPrecisionMath.Multiply(zAx, ax_U1, useLegacyPrecision);
        double zX1 = LegacyPrecisionMath.Add(LegacyPrecisionMath.Multiply(zAx, ax_X1, useLegacyPrecision), finalAx.Ax, useLegacyPrecision);
        // The native REAL build contracts Z_A2's product/add here; replaying it
        // as two separately rounded operations leaves the transition solve's
        // accepted A2 denominator one ULP high in the iter-8 station-15 window.
        double zA2 = LegacyPrecisionMath.MultiplyAdd(zAx, ax_A2, 1.0, useLegacyPrecision);
        double zT2 = LegacyPrecisionMath.Multiply(zAx, ax_T2, useLegacyPrecision);
        double zD2 = LegacyPrecisionMath.Multiply(zAx, ax_D2, useLegacyPrecision);
        double zU2 = LegacyPrecisionMath.Multiply(zAx, ax_U2, useLegacyPrecision);
        double zX2 = LegacyPrecisionMath.Subtract(LegacyPrecisionMath.Multiply(zAx, ax_X2, useLegacyPrecision), finalAx.Ax, useLegacyPrecision);
        double xt2OverZa2 = LegacyPrecisionMath.Divide(point.Xt2[0], zA2, useLegacyPrecision);
        double xtA1Base = point.Xt1[0];
        double xtA1Correction = LegacyPrecisionMath.Multiply(xt2OverZa2, zA1, useLegacyPrecision);
        double xtX2Base = point.Xt2[4];
        double xtX2Correction = LegacyPrecisionMath.Multiply(xt2OverZa2, zX2, useLegacyPrecision);
        if (useLegacyPrecision)
        {
            // TRCHEK2 keeps (XT_A2/Z_A2) inline on every derivative update.
            // Reusing a cached quotient here moved XT_T1 by one ULP in the
            // iteration-5 direct-seed transition window, so the parity branch
            // must preserve the literal source-tree staging.
            float xtA2f = (float)point.Xt2[0];
            float zA2f = (float)zA2;
            float xt2OverZa2f = xtA2f / zA2f;
            
            point.Xt1[0] = (float)((float)point.Xt1[0] - ((xtA2f / zA2f) * (float)zA1));
            point.Xt1[1] = -((xtA2f / zA2f) * (float)zT1);
            point.Xt1[2] = -((xtA2f / zA2f) * (float)zD1);
            point.Xt1[3] = -((xtA2f / zA2f) * (float)zU1);
            point.Xt1[4] = (float)((float)point.Xt1[4] - ((xtA2f / zA2f) * (float)zX1));
            point.Xt2[1] = -((xtA2f / zA2f) * (float)zT2);
            point.Xt2[2] = -((xtA2f / zA2f) * (float)zD2);
            point.Xt2[3] = -((xtA2f / zA2f) * (float)zU2);
            // n6h20 XT derivative inputs trace at IBL=66 iter 2 mc=10
            
            // The direct-seed station-15 iteration-5 window only matches the
            // legacy packet when the ZX2 correction product is rounded back to
            // REAL before the final subtraction, just like the other XT_* updates.
            point.Xt2[4] = (float)((float)point.Xt2[4] - (xt2OverZa2f * (float)zX2));
        }
        else
        {
            point.Xt1[0] = LegacyPrecisionMath.Subtract(xtA1Base, xtA1Correction, useLegacyPrecision);
            point.Xt1[1] = -LegacyPrecisionMath.Multiply(xt2OverZa2, zT1, useLegacyPrecision);
            point.Xt1[2] = -LegacyPrecisionMath.Multiply(xt2OverZa2, zD1, useLegacyPrecision);
            point.Xt1[3] = -LegacyPrecisionMath.Multiply(xt2OverZa2, zU1, useLegacyPrecision);
            point.Xt1[4] = LegacyPrecisionMath.Subtract(point.Xt1[4], LegacyPrecisionMath.Multiply(xt2OverZa2, zX1, useLegacyPrecision), useLegacyPrecision);
            point.Xt2[1] = -LegacyPrecisionMath.Multiply(xt2OverZa2, zT2, useLegacyPrecision);
            point.Xt2[2] = -LegacyPrecisionMath.Multiply(xt2OverZa2, zD2, useLegacyPrecision);
            point.Xt2[3] = -LegacyPrecisionMath.Multiply(xt2OverZa2, zU2, useLegacyPrecision);
            point.Xt2[4] = point.Xt2[4] - (xt2OverZa2 * zX2);
        }



        point.TransitionOccurred = true;
        point.Type = TransitionResultType.Free;
        point.Converged = converged;
        point.Iterations = iterations;
        point.TransitionXi = point.Xt;
        point.AmplAtTransition = amcrit;
        point.DownstreamAmplification = ampl2Iter;


        point.Wf2XF = finalWf2_XF;

        return point;
    }

    // Legacy mapping: f_xfoil/src/xblsys.f :: TRCHEK2
    // Difference from legacy: This is a managed parity entry point over the exact transition-point solve rather than a separate legacy algorithm.
    // Decision: Keep the exact wrapper because it is the right parity boundary when transition drift appears.
    internal static TransitionCheckResult CheckTransitionExact(
        double x1, double x2, double ampl1, double ampl2, double amcrit,
        double ue1, double ue2,
        double th1, double th2,
        double d1, double d2,
        double hstinv, double hstinv_ms,
        double gm1bl,
        double rstbl, double rstbl_ms,
        double hvrat,
        double reybl, double reybl_re, double reybl_ms,
        bool useHighHkModel,
        double? forcedXtr,
        bool useLegacyPrecision = false,
        int? traceSide = null,
        int? traceStation = null,
        int? traceIteration = null,
        string? tracePhase = null)
    {
        return CheckTransitionExact(x1, x2, ampl1, ampl2, amcrit,
            ue1, ue2, th1, th2, d1, d2,
            hstinv, hstinv_ms, gm1bl, rstbl, rstbl_ms, hvrat,
            reybl, reybl_re, reybl_ms,
            useHighHkModel, forcedXtr, useLegacyPrecision, out _,
            traceSide, traceStation, traceIteration, tracePhase);
    }

    internal static TransitionCheckResult CheckTransitionExact(
        double x1, double x2, double ampl1, double ampl2, double amcrit,
        double ue1, double ue2,
        double th1, double th2,
        double d1, double d2,
        double hstinv, double hstinv_ms,
        double gm1bl,
        double rstbl, double rstbl_ms,
        double hvrat,
        double reybl, double reybl_re, double reybl_ms,
        bool useHighHkModel,
        double? forcedXtr,
        bool useLegacyPrecision,
        out TransitionPointResult? fullPoint,
        int? traceSide = null,
        int? traceStation = null,
        int? traceIteration = null,
        string? tracePhase = null)
    {
        var point = ComputeTransitionPoint(
            x1,
            x2,
            ue1,
            ue2,
            th1,
            th2,
            d1,
            d2,
            ampl1,
            ampl2,
            amcrit,
            hstinv,
            hstinv_ms,
            gm1bl,
            rstbl,
            rstbl_ms,
            hvrat,
            reybl,
            reybl_re,
            reybl_ms,
            useHighHkModel,
            forcedXtr,
            useLegacyPrecision,
            traceSide,
            traceStation,
            traceIteration,
            tracePhase);

        var result = new TransitionCheckResult(
            point.TransitionOccurred,
            point.TransitionXi,
            point.AmplAtTransition,
            point.DownstreamAmplification,
            point.Type,
            point.Converged,
            point.Iterations);


        fullPoint = point;
        return result;
    }

    // =====================================================================
    // TRCHEK2: Transition location Newton iteration
    // Source: xblsys.f:1501-1840
    // =====================================================================

    /// <summary>
    /// Newton iteration to find exact transition location within a BL interval
    /// that straddles N_crit. Port of TRCHEK2 from xblsys.f.
    /// </summary>
    /// <param name="x1">Arc-length coordinate at station 1.</param>
    /// <param name="x2">Arc-length coordinate at station 2.</param>
    /// <param name="ampl1">Amplification factor N at station 1.</param>
    /// <param name="ampl2">Amplification factor N at station 2.</param>
    /// <param name="amcrit">Critical amplification factor N_crit.</param>
    /// <param name="hk1">Kinematic shape parameter at station 1.</param>
    /// <param name="th1">Momentum thickness at station 1.</param>
    /// <param name="rt1">Re_theta at station 1.</param>
    /// <param name="ue1">Edge velocity at station 1.</param>
    /// <param name="d1">Displacement thickness at station 1.</param>
    /// <param name="hk2">Kinematic shape parameter at station 2.</param>
    /// <param name="th2">Momentum thickness at station 2.</param>
    /// <param name="rt2">Re_theta at station 2.</param>
    /// <param name="ue2">Edge velocity at station 2.</param>
    /// <param name="d2">Displacement thickness at station 2.</param>
    /// <param name="useHighHkModel">Use DAMPL2 instead of DAMPL (IDAMPV flag).</param>
    /// <param name="forcedXtr">Forced transition location (null for free).</param>
    /// <returns>TransitionCheckResult with location, type, and convergence info.</returns>
    // Legacy mapping: f_xfoil/src/xblsys.f :: TRCHEK2
    // Difference from legacy: This is the public managed wrapper around the exact transition-point solve, keeping the legacy Newton method but exposing a cleaner .NET call surface.
    // Decision: Keep the wrapper because it avoids duplicating the legacy transition solve while preserving parity behavior underneath.
    public static TransitionCheckResult CheckTransition(
        double x1, double x2, double ampl1, double ampl2, double amcrit,
        double hk1, double th1, double rt1, double ue1, double d1,
        double hk2, double th2, double rt2, double ue2, double d2,
        bool useHighHkModel, double? forcedXtr, bool useLegacyPrecision = false)
    {
        bool forcedInInterval = forcedXtr.HasValue && forcedXtr.Value > x1 && forcedXtr.Value <= x2;
        double dx = x2 - x1;

        var ax0 = ComputeTransitionSensitivities(
            hk1,
            th1,
            rt1,
            ampl1,
            hk2,
            th2,
            rt2,
            ampl2,
            amcrit,
            useHighHkModel,
            useLegacyPrecision);

        double ampl2Iter = LegacyPrecisionMath.MultiplyAdd(ax0.Ax, dx, ampl1, useLegacyPrecision);
        double lastXt = x2;
        bool converged = false;
        int iterations = 0;

        // Legacy block: xblsys.f TRCHEK2 outer Newton loop.
        // Difference from legacy: Same Newton loop, with named interval state and trace points instead of anonymous local REAL temporaries.
        // Decision: Keep the named-state loop because it is easier to audit without changing the algorithm.
        for (int iter = 0; iter < MaxTransitionIterations; iter++)
        {
            iterations = iter + 1;

            double amplT;
            double amplT_A2;
            double sfa;
            double sfa_A1;
            double sfa_A2;

            if (ampl2Iter <= amcrit)
            {
                amplT = ampl2Iter;
                amplT_A2 = 1.0;
                sfa = 1.0;
                sfa_A1 = 0.0;
                sfa_A2 = 0.0;
            }
            else
            {
                double amplDen = Math.Max(ampl2Iter - ampl1, 1e-30);
                amplT = amcrit;
                amplT_A2 = 0.0;
                sfa = (amplT - ampl1) / amplDen;
                sfa_A1 = (sfa - 1.0) / amplDen;
                sfa_A2 = -sfa / amplDen;
            }

            double sfx = 1.0;
            double sfx_X1 = 0.0;
            double sfx_X2 = 0.0;
            if (forcedInInterval)
            {
                sfx = (forcedXtr!.Value - x1) / dx;
                sfx_X1 = (sfx - 1.0) / dx;
                sfx_X2 = -sfx / dx;
            }

            double wf2;
            double wf2_A1;
            double wf2_A2;
            double wf2_X1;
            double wf2_X2;
            if (sfa < sfx)
            {
                wf2 = sfa;
                wf2_A1 = sfa_A1;
                wf2_A2 = sfa_A2;
                wf2_X1 = 0.0;
                wf2_X2 = 0.0;
            }
            else
            {
                wf2 = sfx;
                wf2_A1 = 0.0;
                wf2_A2 = 0.0;
                wf2_X1 = sfx_X1;
                wf2_X2 = sfx_X2;
            }

            double wf1 = 1.0 - wf2;
            double wf1_A1 = -wf2_A1;
            double wf1_A2 = -wf2_A2;
            double wf1_X1 = -wf2_X1;
            double wf1_X2 = -wf2_X2;

            double xt = LegacyPrecisionMath.SourceOrderedProductSum(x1, wf1, x2, wf2, useLegacyPrecision);
            double tt = LegacyPrecisionMath.SourceOrderedProductSum(th1, wf1, th2, wf2, useLegacyPrecision);
            double dt = LegacyPrecisionMath.SourceOrderedProductSum(d1, wf1, d2, wf2, useLegacyPrecision);
            double ut = LegacyPrecisionMath.SourceOrderedProductSum(ue1, wf1, ue2, wf2, useLegacyPrecision);
            double hkT = LegacyPrecisionMath.SourceOrderedProductSum(hk1, wf1, hk2, wf2, useLegacyPrecision);
            double rtT = LegacyPrecisionMath.SourceOrderedProductSum(rt1, wf1, rt2, wf2, useLegacyPrecision);
            lastXt = xt;

            double xt_A2 = LegacyPrecisionMath.SourceOrderedProductSum(x1, wf1_A2, x2, wf2_A2, useLegacyPrecision);
            double tt_A2 = LegacyPrecisionMath.SourceOrderedProductSum(th1, wf1_A2, th2, wf2_A2, useLegacyPrecision);
            double dt_A2 = LegacyPrecisionMath.SourceOrderedProductSum(d1, wf1_A2, d2, wf2_A2, useLegacyPrecision);
            double ut_A2 = LegacyPrecisionMath.SourceOrderedProductSum(ue1, wf1_A2, ue2, wf2_A2, useLegacyPrecision);
            double hkT_A2 = LegacyPrecisionMath.SourceOrderedProductSum(hk1, wf1_A2, hk2, wf2_A2, useLegacyPrecision);
            double rtT_A2 = LegacyPrecisionMath.SourceOrderedProductSum(rt1, wf1_A2, rt2, wf2_A2, useLegacyPrecision);

            var ax = ComputeTransitionSensitivities(
                hk1,
                th1,
                rt1,
                ampl1,
                hkT,
                tt,
                rtT,
                amplT,
                amcrit,
                useHighHkModel,
                useLegacyPrecision);

            if (ax.Ax <= 0.0)
            {
                if (!forcedInInterval)
                {
                    var none = new TransitionCheckResult(
                        TransitionOccurred: false,
                        TransitionXi: x2,
                        AmplAtTransition: ampl2Iter,
                        DownstreamAmplification: ampl2Iter,
                        Type: TransitionResultType.None,
                        Converged: true,
                        Iterations: iterations);
                    return none;
                }

                break;
            }

            double axA2Iter = (ax.Ax_Hk2 * hkT_A2)
                            + (ax.Ax_T2 * tt_A2)
                            + (ax.Ax_Rt2 * rtT_A2)
                            + (ax.Ax_A2 * amplT_A2);

            double residual = ampl2Iter - ampl1 - (ax.Ax * dx);
            double residual_A2 = LegacyPrecisionMath.MultiplySubtract(axA2Iter, dx, 1.0, useLegacyPrecision);
            double deltaA2 = -residual / residual_A2;

            double relaxation = 1.0;
            double deltaXt = xt_A2 * deltaA2;
            if ((relaxation * Math.Abs(deltaXt / dx)) > 0.05)
            {
                relaxation = 0.05 * Math.Abs(dx / deltaXt);
            }

            if ((relaxation * Math.Abs(deltaA2)) > 1.0)
            {
                relaxation = 1.0 / Math.Abs(deltaA2);
            }
            if (Math.Abs(deltaA2) < DAEPS)
            {
                converged = true;
                break;
            }

            double nextAmpl2 = LegacyPrecisionMath.ProductThenAdd(relaxation, deltaA2, ampl2Iter, useLegacyPrecision);
            bool crossesCritical = (ampl2Iter > amcrit && nextAmpl2 < amcrit)
                                || (ampl2Iter < amcrit && nextAmpl2 > amcrit);
            ampl2Iter = crossesCritical ? amcrit : nextAmpl2;
        }

        bool freeTransition = ampl2Iter >= amcrit;
        bool forcedTransition = forcedInInterval;
        if (freeTransition && forcedTransition)
        {
            forcedTransition = forcedXtr!.Value < lastXt;
            freeTransition = !forcedTransition;
        }

        if (!freeTransition && !forcedTransition)
        {
            var none = new TransitionCheckResult(
                TransitionOccurred: false,
                TransitionXi: x2,
                AmplAtTransition: ampl2Iter,
                DownstreamAmplification: ampl2Iter,
                Type: TransitionResultType.None,
                Converged: converged,
                Iterations: iterations);
            return none;
        }

        if (forcedTransition)
        {
            var forced = new TransitionCheckResult(
                TransitionOccurred: true,
                TransitionXi: forcedXtr!.Value,
                AmplAtTransition: ampl1,
                DownstreamAmplification: ampl2Iter,
                Type: TransitionResultType.Forced,
                Converged: true,
                Iterations: iterations);
            return forced;
        }

        double amplDenFinal = Math.Max(ampl2Iter - ampl1, 1e-30);
        double finalWf2 = (ampl2Iter <= amcrit) ? 1.0 : (amcrit - ampl1) / amplDenFinal;
        double transitionXi = (x1 * (1.0 - finalWf2)) + (x2 * finalWf2);

        var result = new TransitionCheckResult(
            TransitionOccurred: true,
            TransitionXi: transitionXi,
            AmplAtTransition: amcrit,
            DownstreamAmplification: ampl2Iter,
            Type: TransitionResultType.Free,
            Converged: converged,
            Iterations: iterations);
        return result;
    }
}
