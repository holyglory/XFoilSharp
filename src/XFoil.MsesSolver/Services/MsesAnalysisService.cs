using XFoil.Core.Models;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.MsesSolver.Services;

/// <summary>
/// MSES-thesis-based implementation of <see cref="IAirfoilAnalysisService"/>.
///
/// Phase-0 scaffolding: right now the service delegates inviscid to
/// the existing linear-vortex solver (via the Modern tree) and
/// returns a minimal viscous stub. The real value-add lands in
/// later phases, when the closure library + BL marchers plug into
/// a proper Newton-coupled global solve (Phase 5 per
/// <c>agents/architecture/MsesClosurePlan.md</c>).
///
/// Consumers can already target <see cref="IAirfoilAnalysisService"/>
/// and swap this implementation in for the Modern facade once the
/// viscous path is fleshed out.
/// </summary>
public class MsesAnalysisService : IAirfoilAnalysisService
{
    private readonly IAirfoilAnalysisService _inner;

    /// <summary>
    /// Constructs the MSES analyzer with an injected inviscid
    /// provider. Defaults to the Modern tree (linear-vortex +
    /// solution-adaptive paneling) when no dependency is passed.
    /// </summary>
    public MsesAnalysisService(IAirfoilAnalysisService? inviscidProvider = null)
    {
        _inner = inviscidProvider
            ?? new XFoil.Solver.Modern.Services.AirfoilAnalysisService();
    }

    /// <inheritdoc />
    public InviscidAnalysisResult AnalyzeInviscid(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
        => _inner.AnalyzeInviscid(geometry, angleOfAttackDegrees, settings);

    /// <summary>
    /// Viscous analysis via the MSES-thesis closure.
    ///
    /// Current implementation (Phase 0 stub): throws
    /// <see cref="System.NotImplementedException"/>. The Phase 5
    /// implementation will: (1) solve inviscid, (2) extract Ue(x)
    /// on both surfaces, (3) run the composite
    /// laminar→transition→turbulent marcher, (4) integrate the BL
    /// displacement thickness back into the Karman-Tsien corrected
    /// surface speed, (5) iterate the coupled system via Newton.
    /// </summary>
    public ViscousAnalysisResult AnalyzeViscous(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        throw new System.NotImplementedException(
            "MSES viscous analysis is not yet wired end-to-end. "
            + "Phase 5 of MsesClosurePlan.md — Newton-coupled "
            + "viscous/inviscid interaction — is still pending. "
            + "Use XFoil.Solver.Modern.Services.AirfoilAnalysisService "
            + "for viscous analysis in the meantime.");
    }
}
