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
