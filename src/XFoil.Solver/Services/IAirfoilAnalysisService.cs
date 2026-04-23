using XFoil.Core.Models;
using XFoil.Solver.Models;

namespace XFoil.Solver.Services;

/// <summary>
/// Airfoil analysis contract shared by the three analysis trees
/// (Float / Doubled / Modern) and available for future alternative
/// implementations such as <see cref="XFoil.ThesisClosureSolver"/>.
///
/// Extracted as part of the MSES port scaffolding (Phase 0 per
/// <c>agents/architecture/ThesisClosurePlan.md</c>) so that callers
/// can target this interface rather than the concrete
/// <see cref="AirfoilAnalysisService"/>. The concrete class still
/// remains the inheritance root of the parity-critical Float +
/// Doubled + Modern trees — this interface is additive, with no
/// effect on the bit-exact parity path.
///
/// Additional methods on the concrete service (inverse-CL etc.) are
/// intentionally not on the interface: only the two primary
/// analysis entry points are required for a drop-in alternative
/// solver implementation.
/// </summary>
public interface IAirfoilAnalysisService
{
    /// <summary>
    /// Runs an inviscid single-point analysis. Returns lift,
    /// moment, pressure distribution, and surface-speed field.
    /// </summary>
    InviscidAnalysisResult AnalyzeInviscid(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null);

    /// <summary>
    /// Runs a single-point viscous analysis. Implementations must
    /// produce a full <see cref="ViscousAnalysisResult"/> with
    /// CL/CD/CM, drag decomposition, and per-station boundary-
    /// layer profiles for both surfaces and the wake.
    /// </summary>
    ViscousAnalysisResult AnalyzeViscous(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null);
}
