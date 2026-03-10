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
