using XFoil.Solver.Models;

namespace XFoil.ThesisClosureSolver.Services;

/// <summary>
/// Heuristic stall detector for uncoupled MSES viscous analysis.
///
/// Since the uncoupled path has no viscous feedback on CL, the
/// inviscid CL over-predicts dramatically once the upper-surface
/// BL is heavily separated. This detector inspects the BL profile
/// at the upper-surface TE and across the back half of the airfoil
/// to diagnose whether the case is likely past stall.
///
/// Signals (any one triggers):
/// - Upper TE kinematic shape parameter H > 2.2 (trailing
///   separation).
/// - Upper TE displacement thickness δ* > 4 % chord (thickened BL).
/// - Any upper-surface station past 50 % chord with H > 3.5
///   (upstream separation bubble that doesn't close by TE).
///
/// Thresholds are empirical — tuned against NACA 0012 Re=3e6 polar
/// at α ∈ [0, 20°] so attached cases (α ≤ 10°) report false and
/// most stalled cases (α ≥ 12°) report true.
/// </summary>
public static class ThesisClosureStallHeuristic
{
    /// <summary>
    /// Returns true if the upper-surface BL state indicates the case
    /// is likely past stall (viscous CL correction would be
    /// significant). False if attached or only mildly separated.
    /// </summary>
    /// <param name="upperProfiles">Upper-surface per-station BL
    /// profiles (LE → TE ordering). Pass
    /// <see cref="ViscousAnalysisResult.UpperProfiles"/>.</param>
    public static bool IsLikelyStalled(
        System.Collections.Generic.IReadOnlyList<BoundaryLayerProfile> upperProfiles)
    {
        if (upperProfiles is null || upperProfiles.Count < 2) return false;
        var uTE = upperProfiles[upperProfiles.Count - 1];
        if (uTE.Hk > 2.2) return true;
        if (uTE.DStar > 0.04) return true;

        // Back-half scan.
        int iStart = upperProfiles.Count / 2;
        for (int i = iStart; i < upperProfiles.Count; i++)
        {
            if (upperProfiles[i].Hk > 3.5) return true;
        }
        return false;
    }
}
