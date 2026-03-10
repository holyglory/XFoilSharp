namespace XFoil.Solver.Models;

/// <summary>
/// Mutable workspace for the linear-vorticity inviscid solver.
/// All arrays use 0-based indexing. Dimensions are set via <see cref="InitializeForNodeCount"/>.
/// </summary>
public sealed class InviscidSolverState
{
    private readonly int _maxNodes;

    /// <summary>
    /// Creates a new solver state with pre-allocated arrays of the given capacity.
    /// </summary>
    /// <param name="maxNodes">Maximum number of panel nodes.</param>
    public InviscidSolverState(int maxNodes)
    {
        if (maxNodes < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNodes), "Maximum node count must be at least 2.");
        }

        _maxNodes = maxNodes;

        VortexStrength = new double[maxNodes];
        BasisVortexStrength = new double[maxNodes + 1, 2];
        SourceStrength = new double[maxNodes];
        StreamfunctionInfluence = new double[maxNodes + 1, maxNodes + 1];
        SourceInfluence = new double[maxNodes, maxNodes];
        PivotIndices = new int[maxNodes + 1];
        InviscidSpeed = new double[maxNodes];
        BasisInviscidSpeed = new double[maxNodes, 2];
        InviscidSpeedAlphaDerivative = new double[maxNodes];
        VortexStrengthAlphaDerivative = new double[maxNodes];

        StreamfunctionVortexSensitivity = new double[maxNodes];
        StreamfunctionNormalSensitivity = new double[maxNodes];
        StreamfunctionSourceSensitivity = new double[maxNodes];
        VelocityVortexSensitivity = new double[maxNodes];
        VelocitySourceSensitivity = new double[maxNodes];
    }

    /// <summary>
    /// Number of active panel nodes in the current solve.
    /// </summary>
    public int NodeCount { get; private set; }

    /// <summary>
    /// Vortex strength (gamma) at each node.
    /// </summary>
    public double[] VortexStrength { get; }

    /// <summary>
    /// Two basis vortex strength distributions. Dimensions [NodeCount+1, 2].
    /// The extra row stores the internal streamfunction for each basis solution.
    /// Column 0: alpha=0 basis, Column 1: alpha=90 basis.
    /// </summary>
    public double[,] BasisVortexStrength { get; }

    /// <summary>
    /// Source strength (sigma) at each node.
    /// </summary>
    public double[] SourceStrength { get; }

    /// <summary>
    /// Streamfunction influence matrix (AIJ). Dimensions [NodeCount+1, NodeCount+1].
    /// After LU factoring, contains the L and U factors.
    /// </summary>
    public double[,] StreamfunctionInfluence { get; }

    /// <summary>
    /// Source influence matrix (BIJ). Dimensions [NodeCount, NodeCount].
    /// Stores source contribution to streamfunction at each node.
    /// </summary>
    public double[,] SourceInfluence { get; }

    /// <summary>
    /// LU pivot ordering from decomposition.
    /// </summary>
    public int[] PivotIndices { get; }

    /// <summary>
    /// Inviscid surface speed (Q) at each node.
    /// </summary>
    public double[] InviscidSpeed { get; }

    /// <summary>
    /// Two basis inviscid surface speed distributions. Dimensions [NodeCount, 2].
    /// Column 0: alpha=0 basis, Column 1: alpha=90 basis.
    /// </summary>
    public double[,] BasisInviscidSpeed { get; }

    /// <summary>
    /// Derivative of inviscid surface speed with respect to angle of attack (dQ/dalpha).
    /// </summary>
    public double[] InviscidSpeedAlphaDerivative { get; }

    /// <summary>
    /// Derivative of vortex strength with respect to angle of attack (dGamma/dalpha).
    /// </summary>
    public double[] VortexStrengthAlphaDerivative { get; }

    /// <summary>
    /// Internal streamfunction value (psi0).
    /// </summary>
    public double InternalStreamfunction { get; set; }

    /// <summary>
    /// Trailing edge panel vortex strength.
    /// </summary>
    public double TrailingEdgeVortexStrength { get; set; }

    /// <summary>
    /// Trailing edge panel source strength.
    /// </summary>
    public double TrailingEdgeSourceStrength { get; set; }

    /// <summary>
    /// Magnitude of the trailing edge gap.
    /// </summary>
    public double TrailingEdgeGap { get; set; }

    /// <summary>
    /// TE gap direction component in the normal direction (ANTE in XFoil).
    /// </summary>
    public double TrailingEdgeAngleNormal { get; set; }

    /// <summary>
    /// TE gap direction component in the streamwise direction (ASTE in XFoil).
    /// </summary>
    public double TrailingEdgeAngleStreamwise { get; set; }

    /// <summary>
    /// True if the trailing edge gap is less than 0.0001 times the chord length.
    /// </summary>
    public bool IsSharpTrailingEdge { get; set; }

    /// <summary>
    /// Flag indicating whether the basis solutions (GAMU) have been computed.
    /// </summary>
    public bool AreBasisSolutionsComputed { get; set; }

    /// <summary>
    /// Flag indicating whether the influence matrix (AIJ) has been LU-factored.
    /// </summary>
    public bool IsInfluenceMatrixFactored { get; set; }

    /// <summary>
    /// Workspace: streamfunction sensitivity to vortex strength (DZDG in XFoil).
    /// </summary>
    public double[] StreamfunctionVortexSensitivity { get; }

    /// <summary>
    /// Workspace: streamfunction sensitivity to normal direction (DZDN in XFoil).
    /// </summary>
    public double[] StreamfunctionNormalSensitivity { get; }

    /// <summary>
    /// Workspace: streamfunction sensitivity to source strength (DZDM in XFoil).
    /// </summary>
    public double[] StreamfunctionSourceSensitivity { get; }

    /// <summary>
    /// Workspace: velocity sensitivity to vortex strength (DQDG in XFoil).
    /// </summary>
    public double[] VelocityVortexSensitivity { get; }

    /// <summary>
    /// Workspace: velocity sensitivity to source strength (DQDM in XFoil).
    /// </summary>
    public double[] VelocitySourceSensitivity { get; }

    /// <summary>
    /// Zeros all arrays and sets the active node count.
    /// </summary>
    /// <param name="nodeCount">Number of active nodes.</param>
    public void InitializeForNodeCount(int nodeCount)
    {
        if (nodeCount < 2 || nodeCount > _maxNodes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeCount),
                $"Node count must be between 2 and {_maxNodes}.");
        }

        NodeCount = nodeCount;

        Array.Clear(VortexStrength);
        Array.Clear(BasisVortexStrength);
        Array.Clear(SourceStrength);
        Array.Clear(StreamfunctionInfluence);
        Array.Clear(SourceInfluence);
        Array.Clear(PivotIndices);
        Array.Clear(InviscidSpeed);
        Array.Clear(BasisInviscidSpeed);
        Array.Clear(InviscidSpeedAlphaDerivative);
        Array.Clear(VortexStrengthAlphaDerivative);
        Array.Clear(StreamfunctionVortexSensitivity);
        Array.Clear(StreamfunctionNormalSensitivity);
        Array.Clear(StreamfunctionSourceSensitivity);
        Array.Clear(VelocityVortexSensitivity);
        Array.Clear(VelocitySourceSensitivity);

        InternalStreamfunction = 0.0;
        TrailingEdgeVortexStrength = 0.0;
        TrailingEdgeSourceStrength = 0.0;
        TrailingEdgeGap = 0.0;
        TrailingEdgeAngleNormal = 0.0;
        TrailingEdgeAngleStreamwise = 0.0;
        IsSharpTrailingEdge = false;
        AreBasisSolutionsComputed = false;
        IsInfluenceMatrixFactored = false;
    }
}
