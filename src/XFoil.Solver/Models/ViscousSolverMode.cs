// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xbl.f :: RLXBL adaptive relaxation lineage
// Role in port: Managed enum selecting which viscous Newton step strategy to run.
// Differences: Classic XFoil only had the adaptive under-relaxation path, while the managed port exposes both that legacy mode and a trust-region mode as first-class choices.
// Decision: Keep the enum because strategy selection is an intentional managed capability.
namespace XFoil.Solver.Models;

/// <summary>
/// Selects which viscous Newton solver strategy to use.
/// </summary>
public enum ViscousSolverMode
{
    /// <summary>
    /// Trust-region Newton solver (Levenberg-Marquardt style).
    /// Default mode: max 50 iterations, tolerance 1e-6.
    /// More robust for difficult cases.
    /// </summary>
    TrustRegion = 0,

    /// <summary>
    /// XFoil-compatible adaptive under-relaxation (RLXBL with DHI=1.5, DLO=-0.5 thresholds).
    /// For Phase 4 verification: max 20 iterations, tolerance 1e-4.
    /// </summary>
    XFoilRelaxation = 1
}
