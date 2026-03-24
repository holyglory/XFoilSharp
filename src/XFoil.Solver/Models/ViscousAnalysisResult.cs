// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xbl.f :: viscous operating-point state, f_xfoil/src/xfoil.f :: CDCALC reporting lineage
// Role in port: Managed immutable result for one converged viscous operating point, including drag, transitions, profiles, and convergence history.
// Differences: Legacy XFoil leaves the same outputs distributed across many solver arrays and reporting scalars, while the managed port packages them into one explicit result object.
// Decision: Keep the managed result object because it is the correct API boundary for viscous analysis output.
using System.Collections.Generic;

namespace XFoil.Solver.Models;

/// <summary>
/// Complete result of a viscous analysis at a single operating point.
/// Contains aerodynamic coefficients, drag decomposition, transition info,
/// BL profiles for all surfaces, and full convergence history.
/// </summary>
public sealed class ViscousAnalysisResult
{
    /// <summary>
    /// Lift coefficient from the viscous solution.
    /// </summary>
    public double LiftCoefficient { get; init; }

    /// <summary>
    /// Drag decomposition (CD, CDF, CDP, cross-check, discrepancy, TE base drag, wave drag).
    /// </summary>
    public DragDecomposition DragDecomposition { get; init; }

    /// <summary>
    /// Moment coefficient about the quarter-chord.
    /// </summary>
    public double MomentCoefficient { get; init; }

    /// <summary>
    /// Transition information for the upper surface.
    /// </summary>
    public TransitionInfo UpperTransition { get; init; }

    /// <summary>
    /// Transition information for the lower surface.
    /// </summary>
    public TransitionInfo LowerTransition { get; init; }

    /// <summary>
    /// BL profile at each station on the upper surface.
    /// </summary>
    public BoundaryLayerProfile[] UpperProfiles { get; init; } = System.Array.Empty<BoundaryLayerProfile>();

    /// <summary>
    /// BL profile at each station on the lower surface.
    /// </summary>
    public BoundaryLayerProfile[] LowerProfiles { get; init; } = System.Array.Empty<BoundaryLayerProfile>();

    /// <summary>
    /// BL profile at each wake station.
    /// </summary>
    public BoundaryLayerProfile[] WakeProfiles { get; init; } = System.Array.Empty<BoundaryLayerProfile>();

    /// <summary>
    /// Per-iteration convergence history.
    /// </summary>
    public List<ViscousConvergenceInfo> ConvergenceHistory { get; init; } = new List<ViscousConvergenceInfo>();

    /// <summary>
    /// Whether the viscous Newton iteration converged within the iteration limit.
    /// </summary>
    public bool Converged { get; init; }

    /// <summary>
    /// Total number of Newton iterations performed.
    /// </summary>
    public int Iterations { get; init; }

    /// <summary>
    /// Angle of attack in degrees at which this result was computed.
    /// Set by polar sweep runners to track the operating point.
    /// </summary>
    public double AngleOfAttackDegrees { get; init; }
}
