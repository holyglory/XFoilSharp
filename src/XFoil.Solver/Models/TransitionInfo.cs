// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xblsys.f :: TRCHEK2 and forced-transition state
// Role in port: Managed summary of transition location, trigger type, and convergence status for one surface.
// Differences: Legacy XFoil stores this information across station indices, trip flags, and amplification arrays, while the managed port packages it into a structured result.
// Decision: Keep the managed DTO because it makes transition reporting explicit and testable.
namespace XFoil.Solver.Models;

/// <summary>
/// Records the transition location and type for one surface.
/// </summary>
public readonly record struct TransitionInfo
{
    /// <summary>
    /// Transition location as fraction of chord (x/c).
    /// </summary>
    public double XTransition { get; init; }

    /// <summary>
    /// BL station index at which transition occurs.
    /// </summary>
    public int StationIndex { get; init; }

    /// <summary>
    /// Whether transition was free (e^N) or user-forced.
    /// </summary>
    public TransitionType TransitionType { get; init; }

    /// <summary>
    /// Amplification factor N at the transition location.
    /// </summary>
    public double AmplificationFactorAtTransition { get; init; }

    /// <summary>
    /// Whether the transition Newton iteration converged.
    /// </summary>
    public bool Converged { get; init; }
}

/// <summary>
/// Transition trigger mechanism.
/// </summary>
public enum TransitionType
{
    /// <summary>
    /// Free transition determined by the e^N method (N reaches NCrit).
    /// </summary>
    Free = 0,

    /// <summary>
    /// Forced transition at a user-prescribed x/c location.
    /// </summary>
    Forced = 1
}
