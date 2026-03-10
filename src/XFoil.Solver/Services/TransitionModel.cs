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
    /// Result type for AXSET (ComputeTransitionSensitivities).
    /// </summary>
    public readonly record struct AxsetResult(
        double Ax, double Ax_Hk1, double Ax_T1, double Ax_Rt1, double Ax_A1,
        double Ax_Hk2, double Ax_T2, double Ax_Rt2, double Ax_A2);

    // =====================================================================
    // DAMPL: Envelope spatial amplification rate
    // Source: xblsys.f:1980-2094
    // =====================================================================

    /// <summary>
    /// Computes the envelope spatial amplification rate dN/dx for the e^N method.
    /// Port of DAMPL from xblsys.f:1980-2094.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter.</param>
    /// <param name="th">Momentum thickness.</param>
    /// <param name="rt">Momentum-thickness Reynolds number (Re_theta).</param>
    /// <returns>(AX, dAX/dHk, dAX/dTh, dAX/dRt)</returns>
    public static (double Ax, double Ax_Hk, double Ax_Th, double Ax_Rt)
        ComputeAmplificationRate(double hk, double th, double rt)
    {
        throw new NotImplementedException("DAMPL not yet ported");
    }

    // =====================================================================
    // DAMPL2: Modified envelope amplification rate for separated profiles
    // Source: xblsys.f:2098-2271
    // =====================================================================

    /// <summary>
    /// Computes the modified envelope spatial amplification rate for separated profiles (Hk > 3.5).
    /// Port of DAMPL2 from xblsys.f:2098-2271.
    /// </summary>
    /// <param name="hk">Kinematic shape parameter.</param>
    /// <param name="th">Momentum thickness.</param>
    /// <param name="rt">Momentum-thickness Reynolds number (Re_theta).</param>
    /// <returns>(AX, dAX/dHk, dAX/dTh, dAX/dRt)</returns>
    public static (double Ax, double Ax_Hk, double Ax_Th, double Ax_Rt)
        ComputeAmplificationRateHighHk(double hk, double th, double rt)
    {
        throw new NotImplementedException("DAMPL2 not yet ported");
    }

    // =====================================================================
    // AXSET: Combined amplification rate dispatch
    // Source: xblsys.f:35-144
    // =====================================================================

    /// <summary>
    /// Returns the average amplification rate AX over interval 1..2,
    /// dispatching between DAMPL and DAMPL2 based on IDAMPV flag.
    /// Port of AXSET from xblsys.f:35-144.
    /// </summary>
    public static AxsetResult ComputeTransitionSensitivities(
        double hk1, double t1, double rt1, double a1,
        double hk2, double t2, double rt2, double a2,
        double acrit, bool useHighHkModel)
    {
        throw new NotImplementedException("AXSET not yet ported");
    }
}
