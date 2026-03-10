namespace XFoil.Solver.Models;

/// <summary>
/// Per-station boundary layer state for result reporting.
/// Immutable output record containing all BL quantities at a single station.
/// </summary>
public readonly record struct BoundaryLayerProfile
{
    /// <summary>
    /// Momentum thickness (theta).
    /// </summary>
    public double Theta { get; init; }

    /// <summary>
    /// Displacement thickness (delta*).
    /// </summary>
    public double DStar { get; init; }

    /// <summary>
    /// Shear stress coefficient (Ctau).
    /// </summary>
    public double Ctau { get; init; }

    /// <summary>
    /// Mass defect (delta* x Ue).
    /// </summary>
    public double MassDefect { get; init; }

    /// <summary>
    /// Edge velocity (Ue).
    /// </summary>
    public double EdgeVelocity { get; init; }

    /// <summary>
    /// Kinematic shape parameter (Hk = delta*/theta adjusted for compressibility).
    /// </summary>
    public double Hk { get; init; }

    /// <summary>
    /// Skin friction coefficient (Cf).
    /// </summary>
    public double Cf { get; init; }

    /// <summary>
    /// Reynolds number based on momentum thickness (Re_theta).
    /// </summary>
    public double ReTheta { get; init; }

    /// <summary>
    /// Amplification factor N (for transition tracking).
    /// </summary>
    public double AmplificationFactor { get; init; }

    /// <summary>
    /// BL arc-length coordinate (xi).
    /// </summary>
    public double Xi { get; init; }
}
