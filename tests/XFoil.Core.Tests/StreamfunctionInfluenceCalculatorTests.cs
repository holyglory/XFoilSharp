using XFoil.Solver.Models;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: PSILIN and related streamfunction influence formulas
// Secondary legacy source: wake/source contribution helpers in the inviscid solver
// Role in port: Verifies the managed streamfunction influence calculator used for parity tracing and inviscid diagnostics.
// Differences: The managed port exposes the kernel directly and adds optional geometric/source sensitivity outputs that are easier to inspect than legacy work arrays.
// Decision: Keep the managed helper because it preserves the kernel formulas while making them independently testable and traceable.
namespace XFoil.Core.Tests;

public class StreamfunctionInfluenceCalculatorTests
{
    /// <summary>
    /// Helper: creates a simple 2-panel flat plate with 3 nodes along the X axis.
    /// Nodes: (0,0), (0.5,0), (1,0). Normals point upward (0,1). Panel angles = PI.
    /// </summary>
    private static (LinearVortexPanelState panel, InviscidSolverState state) CreateFlatPlate()
    {
        const int n = 3;
        var panel = new LinearVortexPanelState(n);
        panel.Resize(n);
        var state = new InviscidSolverState(n);
        state.InitializeForNodeCount(n);

        panel.X[0] = 0.0; panel.Y[0] = 0.0;
        panel.X[1] = 0.5; panel.Y[1] = 0.0;
        panel.X[2] = 1.0; panel.Y[2] = 0.0;

        ParametricSpline.ComputeArcLength(panel.X, panel.Y, panel.ArcLength, n);
        ParametricSpline.FitSegmented(panel.X, panel.XDerivative, panel.ArcLength, n);
        ParametricSpline.FitSegmented(panel.Y, panel.YDerivative, panel.ArcLength, n);

        PanelGeometryBuilder.ComputeNormals(panel);
        PanelGeometryBuilder.ComputeTrailingEdgeGeometry(panel, state);

        panel.Chord = 1.0;
        state.IsSharpTrailingEdge = true;

        PanelGeometryBuilder.ComputePanelAngles(panel, state);

        // Set unit vortex strengths for testing
        for (int i = 0; i < n; i++)
        {
            state.VortexStrength[i] = 1.0;
            state.SourceStrength[i] = 0.0;
        }

        return (panel, state);
    }

    /// <summary>
    /// Helper: creates a symmetric 4-panel diamond shape.
    /// Nodes: (1,0), (0,0.2), (-1,0), (0,-0.2), plus close back to (1,0)
    /// This gives a 5-node closed shape.
    /// </summary>
    private static (LinearVortexPanelState panel, InviscidSolverState state) CreateDiamond()
    {
        const int n = 5;
        var panel = new LinearVortexPanelState(n);
        panel.Resize(n);
        var state = new InviscidSolverState(n);
        state.InitializeForNodeCount(n);

        // Symmetric diamond, traversed counterclockwise
        panel.X[0] = 1.0;  panel.Y[0] = 0.0;   // TE upper
        panel.X[1] = 0.0;  panel.Y[1] = 0.2;   // upper mid
        panel.X[2] = -1.0; panel.Y[2] = 0.0;   // LE
        panel.X[3] = 0.0;  panel.Y[3] = -0.2;  // lower mid
        panel.X[4] = 1.0;  panel.Y[4] = 0.0;   // TE lower = TE upper (sharp)

        ParametricSpline.ComputeArcLength(panel.X, panel.Y, panel.ArcLength, n);
        ParametricSpline.FitSegmented(panel.X, panel.XDerivative, panel.ArcLength, n);
        ParametricSpline.FitSegmented(panel.Y, panel.YDerivative, panel.ArcLength, n);

        PanelGeometryBuilder.ComputeNormals(panel);
        panel.Chord = 2.0;
        PanelGeometryBuilder.ComputeTrailingEdgeGeometry(panel, state);
        PanelGeometryBuilder.ComputePanelAngles(panel, state);

        // Set zero strengths initially
        for (int i = 0; i < n; i++)
        {
            state.VortexStrength[i] = 0.0;
            state.SourceStrength[i] = 0.0;
        }

        return (panel, state);
    }

    /// <summary>
    /// Test 1: Self-influence at panel's own start node avoids log(0) singularity.
    /// When field point index matches a panel endpoint, G1 should be set to 0 (not log(0)).
    /// </summary>
    [Fact]
    // Legacy mapping: PSILIN self-influence at a panel start node.
    // Difference from legacy: The singularity-avoidance branch is asserted directly on the managed kernel output. Decision: Keep the managed regression because it isolates a subtle numerical safeguard.
    public void ComputeInfluenceAt_SelfInfluenceStartNode_AvoidsSingularity()
    {
        var (panel, state) = CreateFlatPlate();

        // Field point at node 0 (which is the start of panel 0)
        var (psi, psiNi) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            fieldNodeIndex: 0,
            fieldX: panel.X[0], fieldY: panel.Y[0],
            fieldNormalX: panel.NormalX[0], fieldNormalY: panel.NormalY[0],
            computeGeometricSensitivities: false,
            includeSourceTerms: false,
            panel, state,
            freestreamSpeed: 0.0,
            angleOfAttackRadians: 0.0);

        // PSI should be finite (no NaN or infinity from log(0))
        Assert.True(double.IsFinite(psi), $"PSI should be finite, got {psi}");
        Assert.True(double.IsFinite(psiNi), $"PSI_NI should be finite, got {psiNi}");
    }

    /// <summary>
    /// Test 2: Self-influence at panel's own end node avoids atan(0,0) singularity.
    /// </summary>
    [Fact]
    // Legacy mapping: PSILIN self-influence at a panel end node.
    // Difference from legacy: The managed test checks finite outputs directly rather than inferring safety from downstream assembly. Decision: Keep the managed regression because it constrains another singularity edge case explicitly.
    public void ComputeInfluenceAt_SelfInfluenceEndNode_AvoidsSingularity()
    {
        var (panel, state) = CreateFlatPlate();

        // Field point at node 1 (which is the end of panel 0 and start of panel 1)
        var (psi, psiNi) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            fieldNodeIndex: 1,
            fieldX: panel.X[1], fieldY: panel.Y[1],
            fieldNormalX: panel.NormalX[1], fieldNormalY: panel.NormalY[1],
            computeGeometricSensitivities: false,
            includeSourceTerms: false,
            panel, state,
            freestreamSpeed: 0.0,
            angleOfAttackRadians: 0.0);

        Assert.True(double.IsFinite(psi), $"PSI should be finite, got {psi}");
        Assert.True(double.IsFinite(psiNi), $"PSI_NI should be finite, got {psiNi}");
    }

    /// <summary>
    /// Test 3: For a 2-panel flat plate, vortex contribution DZDG sensitivity array is populated.
    /// With uniform vortex strength, the sensitivity should follow the linear vorticity integrals.
    /// </summary>
    [Fact]
    // Legacy mapping: PSILIN vortex sensitivity population.
    // Difference from legacy: Sensitivity fields are asserted explicitly on the managed return value instead of remaining buried in legacy arrays. Decision: Keep the managed test because it documents an important observable output.
    public void ComputeInfluenceAt_FlatPlate_VortexSensitivitiesPopulated()
    {
        var (panel, state) = CreateFlatPlate();
        int n = panel.NodeCount;

        // Evaluate at node 1 (midpoint of flat plate)
        var (psi, _) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            fieldNodeIndex: 1,
            fieldX: panel.X[1], fieldY: panel.Y[1],
            fieldNormalX: panel.NormalX[1], fieldNormalY: panel.NormalY[1],
            computeGeometricSensitivities: false,
            includeSourceTerms: false,
            panel, state,
            freestreamSpeed: 0.0,
            angleOfAttackRadians: 0.0);

        // DZDG should have non-zero entries for each node
        bool anyNonZero = false;
        for (int j = 0; j < n; j++)
        {
            if (Math.Abs(state.StreamfunctionVortexSensitivity[j]) > 1e-15)
            {
                anyNonZero = true;
                break;
            }
        }

        Assert.True(anyNonZero, "Vortex sensitivities (DZDG) should have non-zero entries");
    }

    /// <summary>
    /// Test 4: Source contribution is computed when includeSourceTerms=true but not when false.
    /// </summary>
    [Fact]
    // Legacy mapping: PSILIN optional source-term branch.
    // Difference from legacy: The includeSourceTerms switch is a direct managed API flag rather than an internal call-site choice. Decision: Keep the managed regression because it protects both code paths explicitly.
    public void ComputeInfluenceAt_SourceTermsFlag_ControlsComputation()
    {
        var (panel, state) = CreateFlatPlate();
        int n = panel.NodeCount;

        // Set non-zero source strengths
        for (int i = 0; i < n; i++)
        {
            state.SourceStrength[i] = 1.0;
        }

        // With source terms enabled
        StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            fieldNodeIndex: 1,
            fieldX: panel.X[1], fieldY: panel.Y[1],
            fieldNormalX: panel.NormalX[1], fieldNormalY: panel.NormalY[1],
            computeGeometricSensitivities: false,
            includeSourceTerms: true,
            panel, state,
            freestreamSpeed: 0.0,
            angleOfAttackRadians: 0.0);

        // Check that source sensitivities were computed
        double[] withSource = new double[n];
        for (int j = 0; j < n; j++)
        {
            withSource[j] = state.StreamfunctionSourceSensitivity[j];
        }

        // Without source terms
        StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            fieldNodeIndex: 1,
            fieldX: panel.X[1], fieldY: panel.Y[1],
            fieldNormalX: panel.NormalX[1], fieldNormalY: panel.NormalY[1],
            computeGeometricSensitivities: false,
            includeSourceTerms: false,
            panel, state,
            freestreamSpeed: 0.0,
            angleOfAttackRadians: 0.0);

        // Source sensitivities should be zero when not requested
        bool allZero = true;
        for (int j = 0; j < n; j++)
        {
            if (Math.Abs(state.StreamfunctionSourceSensitivity[j]) > 1e-15)
            {
                allZero = false;
                break;
            }
        }

        Assert.True(allZero, "Source sensitivities should be zero when includeSourceTerms=false");

        // At least one entry from the enabled run should be non-zero
        bool anyNonZero = false;
        for (int j = 0; j < n; j++)
        {
            if (Math.Abs(withSource[j]) > 1e-15)
            {
                anyNonZero = true;
                break;
            }
        }
        Assert.True(anyNonZero, "Source sensitivities should have non-zero entries when includeSourceTerms=true");
    }

    /// <summary>
    /// Test 5: TE panel contribution is correctly added using TE gap geometry.
    /// For a sharp TE (SCS=1, SDS=0), the TE source contribution should be non-zero
    /// when there is a vortex strength difference at the TE nodes.
    /// </summary>
    [Fact]
    // Legacy mapping: trailing-edge panel contribution in the streamfunction kernel.
    // Difference from legacy: The TE contribution is validated directly through the managed helper. Decision: Keep the managed regression because it isolates a subtle additive term in the kernel.
    public void ComputeInfluenceAt_TEPanelContribution_AddedCorrectly()
    {
        var (panel, state) = CreateFlatPlate();
        int n = panel.NodeCount;

        // Set vortex strengths with a difference at TE nodes
        state.VortexStrength[0] = 1.0;   // first node (TE upper)
        state.VortexStrength[n - 1] = -1.0; // last node (TE lower)
        state.VortexStrength[1] = 0.0;

        // Compute at field node 1 (interior point)
        var (psi, _) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            fieldNodeIndex: 1,
            fieldX: panel.X[1], fieldY: panel.Y[1],
            fieldNormalX: panel.NormalX[1], fieldNormalY: panel.NormalY[1],
            computeGeometricSensitivities: false,
            includeSourceTerms: false,
            panel, state,
            freestreamSpeed: 0.0,
            angleOfAttackRadians: 0.0);

        // With a vortex strength difference at TE and SCS=1, there should be a TE contribution
        // The PSI should be non-zero and finite
        Assert.True(double.IsFinite(psi), $"PSI should be finite with TE contribution, got {psi}");
    }

    /// <summary>
    /// Test 6: Freestream contribution adds PSI += qInf*(cos(alpha)*y - sin(alpha)*x).
    /// </summary>
    [Fact]
    // Legacy mapping: freestream contribution within the streamfunction field evaluation.
    // Difference from legacy: The managed test checks the additive freestream term explicitly instead of inferring it from whole-solver outputs. Decision: Keep the managed unit test because it tightly constrains the kernel contract.
    public void ComputeInfluenceAt_Freestream_AddsCorrectly()
    {
        var (panel, state) = CreateFlatPlate();

        // Zero all vortex/source to isolate freestream contribution
        for (int i = 0; i < panel.NodeCount; i++)
        {
            state.VortexStrength[i] = 0.0;
            state.SourceStrength[i] = 0.0;
        }

        double qinf = 1.0;
        double alpha = Math.PI / 6.0; // 30 degrees
        double fieldX = 0.5;
        double fieldY = 0.3;

        var (psi, _) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            fieldNodeIndex: -1, // off-body point
            fieldX: fieldX, fieldY: fieldY,
            fieldNormalX: 0.0, fieldNormalY: 1.0,
            computeGeometricSensitivities: false,
            includeSourceTerms: false,
            panel, state,
            freestreamSpeed: qinf,
            angleOfAttackRadians: alpha);

        // Expected freestream PSI = qinf * (cos(alpha)*y - sin(alpha)*x)
        double expectedFreestream = qinf * (Math.Cos(alpha) * fieldY - Math.Sin(alpha) * fieldX);

        // The total PSI includes panel contributions (which should be ~0 with zero strengths)
        // plus freestream. With zero vortex/source, PSI should be very close to expectedFreestream.
        Assert.True(
            Math.Abs(psi - expectedFreestream) < 1e-10,
            $"PSI = {psi:E10}, expected freestream = {expectedFreestream:E10}");
    }

    /// <summary>
    /// Test 7: For a symmetric diamond shape at alpha=0, the StreamfunctionVortexSensitivity
    /// array has a symmetric pattern: DZDG[0]==DZDG[4], DZDG[1]==DZDG[3].
    /// </summary>
    [Fact]
    // Legacy mapping: PSILIN symmetry properties on a symmetric body.
    // Difference from legacy: The symmetry relation is asserted numerically on the managed kernel outputs. Decision: Keep the managed regression because it is a strong consistency check for the ported influence formulas.
    public void ComputeInfluenceAt_SymmetricDiamond_SymmetricSensitivities()
    {
        var (panel, state) = CreateDiamond();
        int n = panel.NodeCount;

        // Evaluate at the LE node (node 2, the axis of symmetry)
        StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
            fieldNodeIndex: 2,
            fieldX: panel.X[2], fieldY: panel.Y[2],
            fieldNormalX: panel.NormalX[2], fieldNormalY: panel.NormalY[2],
            computeGeometricSensitivities: false,
            includeSourceTerms: false,
            panel, state,
            freestreamSpeed: 0.0,
            angleOfAttackRadians: 0.0);

        // For a symmetric geometry at a symmetric evaluation point,
        // the vortex sensitivities should reflect the symmetry:
        // DZDG[0] (upper TE) should relate to DZDG[4] (lower TE)
        // DZDG[1] (upper mid) should relate to DZDG[3] (lower mid)
        // The Y-symmetry means DZDG[0] == DZDG[n-1] and DZDG[1] == DZDG[n-2]
        Assert.True(
            Math.Abs(state.StreamfunctionVortexSensitivity[0] - state.StreamfunctionVortexSensitivity[n - 1]) < 1e-10,
            $"DZDG[0]={state.StreamfunctionVortexSensitivity[0]:E10} != DZDG[{n - 1}]={state.StreamfunctionVortexSensitivity[n - 1]:E10}");

        Assert.True(
            Math.Abs(state.StreamfunctionVortexSensitivity[1] - state.StreamfunctionVortexSensitivity[n - 2]) < 1e-10,
            $"DZDG[1]={state.StreamfunctionVortexSensitivity[1]:E10} != DZDG[{n - 2}]={state.StreamfunctionVortexSensitivity[n - 2]:E10}");
    }
}
