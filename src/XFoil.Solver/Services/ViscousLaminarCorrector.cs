using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

/// <summary>
/// [DEPRECATED] The surrogate laminar corrector has been replaced by the Newton-coupled
/// viscous solver (ViscousSolverEngine). All methods throw NotSupportedException.
/// Use ViscousSolverEngine.SolveViscous() or PolarSweepRunner for viscous analysis.
/// </summary>
public sealed class ViscousLaminarCorrector
{
    [Obsolete("Surrogate viscous pipeline removed. Use ViscousSolverEngine.SolveViscous() instead.")]
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
