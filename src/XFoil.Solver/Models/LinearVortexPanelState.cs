// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/orrs/src :: linear-vorticity geometry workspace lineage, f_xfoil/src/xpanel.f :: surface geometry arrays
// Role in port: Managed mutable geometry workspace for the alternative linear-vorticity solver.
// Differences: The managed port names and owns the geometry arrays explicitly, whereas legacy implementations relied on shared arrays and solver-local conventions.
// Decision: Keep the managed workspace because it is the cleanest way to expose the alternative solver's geometry state.
namespace XFoil.Solver.Models;

/// <summary>
/// Mutable state container for node-based panel geometry in the linear-vorticity formulation.
/// All arrays use 0-based indexing with length equal to <see cref="MaxNodes"/>.
/// <see cref="NodeCount"/> is set when geometry is loaded via <see cref="Resize"/>.
/// </summary>
public sealed class LinearVortexPanelState
{
    /// <summary>
    /// Creates a new panel state with pre-allocated arrays of the given capacity.
    /// </summary>
    /// <param name="maxNodes">Maximum number of panel nodes. Default is 360.</param>
    // Legacy mapping: f_xfoil/orrs/src :: panel-geometry workspace setup lineage.
    // Difference from legacy: Array ownership and validation are explicit at construction time instead of implicit in shared work arrays.
    // Decision: Keep the managed constructor because it makes the workspace reusable and testable.
    public LinearVortexPanelState(int maxNodes = 360)
    {
        if (maxNodes < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNodes), "Maximum node count must be at least 2.");
        }

        MaxNodes = maxNodes;
        X = new double[maxNodes];
        Y = new double[maxNodes];
        ArcLength = new double[maxNodes];
        XDerivative = new double[maxNodes];
        YDerivative = new double[maxNodes];
        NormalX = new double[maxNodes];
        NormalY = new double[maxNodes];
        PanelAngle = new double[maxNodes];
    }

    /// <summary>
    /// Maximum number of panel nodes this state can hold.
    /// </summary>
    public int MaxNodes { get; }

    /// <summary>
    /// Number of panel nodes currently active on the airfoil surface.
    /// </summary>
    public int NodeCount { get; private set; }

    /// <summary>
    /// Node X coordinates (0-based, length <see cref="MaxNodes"/>).
    /// </summary>
    public double[] X { get; }

    /// <summary>
    /// Node Y coordinates (0-based, length <see cref="MaxNodes"/>).
    /// </summary>
    public double[] Y { get; }

    /// <summary>
    /// Cumulative arc-length parameter at each node.
    /// </summary>
    public double[] ArcLength { get; }

    /// <summary>
    /// Spline derivative dX/dS at each node.
    /// </summary>
    public double[] XDerivative { get; }

    /// <summary>
    /// Spline derivative dY/dS at each node.
    /// </summary>
    public double[] YDerivative { get; }

    /// <summary>
    /// Outward unit normal X component at each node.
    /// </summary>
    public double[] NormalX { get; }

    /// <summary>
    /// Outward unit normal Y component at each node.
    /// </summary>
    public double[] NormalY { get; }

    /// <summary>
    /// Panel angle at each node (atan2 of tangent direction).
    /// </summary>
    public double[] PanelAngle { get; }

    /// <summary>
    /// Arc-length parameter at the leading edge.
    /// </summary>
    public double LeadingEdgeArcLength { get; set; }

    /// <summary>
    /// Leading edge X coordinate.
    /// </summary>
    public double LeadingEdgeX { get; set; }

    /// <summary>
    /// Leading edge Y coordinate.
    /// </summary>
    public double LeadingEdgeY { get; set; }

    /// <summary>
    /// Trailing edge X coordinate (average of first and last node).
    /// </summary>
    public double TrailingEdgeX { get; set; }

    /// <summary>
    /// Trailing edge Y coordinate (average of first and last node).
    /// </summary>
    public double TrailingEdgeY { get; set; }

    /// <summary>
    /// Chord length (distance from leading edge to trailing edge).
    /// </summary>
    public double Chord { get; set; }

    /// <summary>
    /// Sets the active node count and validates it against <see cref="MaxNodes"/>.
    /// </summary>
    /// <param name="nodeCount">Number of active nodes.</param>
    // Legacy mapping: f_xfoil/orrs/src :: active-node resize/reset lineage.
    // Difference from legacy: The active node count is validated and stored through one managed helper instead of being assigned directly.
    // Decision: Keep the helper because it protects the workspace contract.
    public void Resize(int nodeCount)
    {
        if (nodeCount < 2 || nodeCount > MaxNodes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeCount),
                $"Node count must be between 2 and {MaxNodes}.");
        }

        NodeCount = nodeCount;
    }
}
