using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xbl.f :: old staged viscous correction lineage
// Role in port: Retains a deprecated entry point from an older managed surrogate viscous pipeline.
// Differences: The current file is only a deprecation shim that throws; there is no direct legacy implementation corresponding to its present behavior.
// Decision: Keep the stub only as a migration guard. Do not treat it as part of the legacy or parity solver path.
namespace XFoil.Solver.Services;

/// <summary>
/// [DEPRECATED] The surrogate laminar corrector has been replaced by the Newton-coupled
/// viscous solver (ViscousSolverEngine). All methods throw NotSupportedException.
/// Use ViscousSolverEngine.SolveViscous() or PolarSweepRunner for viscous analysis.
/// </summary>
public sealed class ViscousLaminarCorrector
{
    [Obsolete("Surrogate viscous pipeline removed. Use ViscousSolverEngine.SolveViscous() instead.")]
    // Legacy mapping: none; managed-only deprecation shim.
    // Difference from legacy: The method no longer performs any correction work and instead directs callers to the Newton-coupled viscous solver.
    // Decision: Keep the explicit throw until all obsolete entry points are retired.
    public ViscousCorrectionResult Correct(
        ViscousIntervalSystem initialSystem,
        AnalysisSettings settings,
        int iterations = 3,
        double momentumRelaxation = 0.25d,
        double shapeRelaxation = 0.35d,
        double transitionRelaxation = 0.20d)
    {
        throw new NotSupportedException(
            "The surrogate laminar corrector has been replaced by the Newton-coupled viscous solver. " +
            "Use ViscousSolverEngine.SolveViscous() or AirfoilAnalysisService.AnalyzeViscous() instead.");
    }
}
