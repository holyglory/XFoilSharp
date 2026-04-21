using System;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: none
// Role in port: Provides a managed Viterna-Corrigan fallback when the viscous solver does not converge past stall.
// Differences: XFoil does not have a direct legacy analogue for this file; it is a managed reporting fallback added on top of the viscous solver.
// Decision: Keep the fallback as a managed improvement only. It is explicitly outside the parity-critical legacy solver path.
using XFoil.Solver.Services;
namespace XFoil.Solver.Double.Services;

/// <summary>
/// Viterna-Corrigan post-stall extrapolation for CL and CD.
/// Provides CL/CD estimates at angles of attack beyond stall where
/// the Newton viscous solver cannot converge.
///
/// The Viterna-Corrigan model (1982) smoothly extrapolates from the last converged
/// point toward the flat-plate limit at 90 degrees. It uses:
/// - CL_max from the last converged point
/// - Flat-plate drag coefficient CD_max ~ 1.8 (broadside)
/// - Induced drag correction using effective aspect ratio
///
/// Only activated when UsePostStallExtrapolation is true and the Newton
/// solver reports non-convergence. Default behavior is to report only
/// converged results.
/// </summary>
public static class PostStallExtrapolator
{
    /// <summary>
    /// Maximum flat-plate drag coefficient (CD at 90 degrees).
    /// Typical value for a flat plate broadside to the flow is ~2.0.
    /// Reduced slightly for airfoil shapes.
    /// </summary>
    private const double CDMaxDefault = 1.8;

    /// <summary>
    /// Extrapolates CL and CD to post-stall angles using the Viterna-Corrigan model.
    /// </summary>
    /// <param name="alpha">Current angle of attack (radians).</param>
    /// <param name="lastConvergedAlpha">Last converged point's angle of attack (radians).</param>
    /// <param name="lastConvergedCL">Last converged lift coefficient.</param>
    /// <param name="lastConvergedCD">Last converged drag coefficient.</param>
    /// <param name="aspectRatio">Effective aspect ratio (use 2*pi for 2D airfoil analysis).</param>
    /// <returns>Tuple of (CL, CD) at the requested angle of attack.</returns>
    // Legacy mapping: none; managed-only Viterna-Corrigan fallback.
    // Difference from legacy: The extrapolation is an added post-processing feature and does not correspond to a legacy XFoil subroutine.
    // Decision: Keep the fallback because it is useful for reporting failed post-stall cases, but keep it clearly outside the parity path.
    public static (double CL, double CD) ExtrapolatePostStall(
        double alpha,
        double lastConvergedAlpha,
        double lastConvergedCL,
        double lastConvergedCD,
        double aspectRatio)
        => ExtrapolatePostStall(alpha, lastConvergedAlpha, lastConvergedCL, lastConvergedCD, aspectRatio, CDMaxDefault);

    /// <summary>
    /// Option C — two-anchor-style refinement. See <see cref="XFoil.Solver.Services.PostStallExtrapolator"/>
    /// for the rationale. This double-tree copy is kept in sync so that the
    /// Modern override can call into either tree consistently.
    /// </summary>
    public static (double CL, double CD) ExtrapolatePostStall(
        double alpha,
        double lastConvergedAlpha,
        double lastConvergedCL,
        double lastConvergedCD,
        double aspectRatio,
        double cdMaxOverride)
    {
        // Protect against invalid inputs
        if (double.IsNaN(alpha) || double.IsInfinity(alpha))
            return (0.0, lastConvergedCD);
        if (aspectRatio < 0.1) aspectRatio = 2.0 * Math.PI;

        double sinA = Math.Sin(alpha);
        double cosA = Math.Cos(alpha);
        double sinStall = Math.Sin(lastConvergedAlpha);
        double cosStall = Math.Cos(lastConvergedAlpha);
        double sin2A = Math.Sin(2.0 * alpha);

        // Protect against division by zero
        if (Math.Abs(sinStall) < 1e-10)
            sinStall = (lastConvergedAlpha >= 0 ? 1e-10 : -1e-10);
        if (Math.Abs(cosStall) < 1e-10)
            cosStall = 1e-10;
        if (Math.Abs(sinA) < 1e-10)
            sinA = (alpha >= 0 ? 1e-10 : -1e-10);

        double clMax = lastConvergedCL;
        double cdMax = double.IsFinite(cdMaxOverride) && cdMaxOverride > 0.1
            ? Math.Min(cdMaxOverride, 2.2)
            : CDMaxDefault;

        // Viterna-Corrigan lift model (Viterna & Corrigan, 1982):
        // CL = A1 * sin(2*alpha) + A2 * cos^2(alpha) / sin(alpha)
        //
        // where A1 and A2 are determined by matching CL at alpha_stall:
        //   A1 = CD_max / 2
        //   A2 = (CL_stall - CD_max * sin(a_s) * cos(a_s)) * sin(a_s) / cos^2(a_s)
        //
        // This ensures CL = CL_stall at alpha_stall and CL -> 0 at alpha = 90.
        double a1 = cdMax / 2.0;

        // Compute A2 from the stall matching condition
        double a2 = (clMax - cdMax * sinStall * cosStall) * sinStall
                   / (cosStall * cosStall);

        // CL extrapolation
        double cl;
        if (Math.Abs(sinA) > 1e-8)
        {
            cl = a1 * sin2A + a2 * cosA * cosA / sinA;
        }
        else
        {
            // Near alpha=0: use linear approximation from stall CL
            cl = clMax * alpha / Math.Max(Math.Abs(lastConvergedAlpha), 1e-6);
        }

        // Viterna-Corrigan drag model:
        // CD = B1 * sin^2(alpha) + B2 * cos(alpha)
        //
        // where B1 and B2 match CD at alpha_stall:
        //   B1 = CD_max
        //   B2 = (CD_stall - CD_max * sin^2(a_s)) / cos(a_s)
        double b1 = cdMax;
        double b2 = (lastConvergedCD - cdMax * sinStall * sinStall) / cosStall;

        double cd = b1 * sinA * sinA + b2 * cosA;

        // Ensure CD is at least as large as the last converged value
        // (drag should increase after stall)
        cd = Math.Max(cd, lastConvergedCD);

        // Sanity checks
        if (double.IsNaN(cl) || double.IsInfinity(cl)) cl = 0.0;
        if (double.IsNaN(cd) || double.IsInfinity(cd)) cd = lastConvergedCD;

        return (cl, cd);
    }
}
