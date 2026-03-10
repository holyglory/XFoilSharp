namespace XFoil.Solver.Models;

/// <summary>
/// Per-iteration convergence diagnostics for the viscous Newton solver.
/// </summary>
public readonly record struct ViscousConvergenceInfo
{
    /// <summary>
    /// RMS of the BL residual vector.
    /// </summary>
    public double RmsResidual { get; init; }

    /// <summary>
    /// Maximum residual magnitude across all BL stations.
    /// </summary>
    public double MaxResidual { get; init; }

    /// <summary>
    /// BL station index where the maximum residual occurs.
    /// </summary>
    public int MaxResidualStation { get; init; }

    /// <summary>
    /// Side (0=upper, 1=lower) where the maximum residual occurs.
    /// </summary>
    public int MaxResidualSide { get; init; }

    /// <summary>
    /// Relaxation factor applied to the Newton step this iteration.
    /// </summary>
    public double RelaxationFactor { get; init; }

    /// <summary>
    /// Trust-region radius for Levenberg-Marquardt mode.
    /// </summary>
    public double TrustRegionRadius { get; init; }

    /// <summary>
    /// Lift coefficient at this iteration.
    /// </summary>
    public double CL { get; init; }

    /// <summary>
    /// Drag coefficient at this iteration.
    /// </summary>
    public double CD { get; init; }

    /// <summary>
    /// Moment coefficient at this iteration.
    /// </summary>
    public double CM { get; init; }

    /// <summary>
    /// Iteration number (0-based).
    /// </summary>
    public int Iteration { get; init; }
}
