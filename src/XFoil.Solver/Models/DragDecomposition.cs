namespace XFoil.Solver.Models;

/// <summary>
/// Drag decomposition from the viscous analysis.
/// CD is from Squire-Young wake momentum deficit extrapolation;
/// CDF from skin friction integration; CDP by subtraction.
/// </summary>
public readonly record struct DragDecomposition
{
    /// <summary>
    /// Total drag coefficient (Squire-Young wake momentum deficit).
    /// </summary>
    public double CD { get; init; }

    /// <summary>
    /// Skin friction drag coefficient (integrated from BL solution along both surfaces).
    /// </summary>
    public double CDF { get; init; }

    /// <summary>
    /// Pressure (form) drag coefficient (CD - CDF).
    /// </summary>
    public double CDP { get; init; }

    /// <summary>
    /// Independent surface integration drag for cross-check.
    /// </summary>
    public double CDSurfaceCrossCheck { get; init; }

    /// <summary>
    /// Discrepancy between wake-momentum CD and surface-integrated CD.
    /// Useful for diagnosing convergence quality.
    /// </summary>
    public double DiscrepancyMetric { get; init; }

    /// <summary>
    /// Trailing edge base drag contribution.
    /// </summary>
    public double TEBaseDrag { get; init; }

    /// <summary>
    /// Wave drag estimate for transonic cases (M > 0.7). Null for subsonic.
    /// </summary>
    public double? WaveDrag { get; init; }
}
