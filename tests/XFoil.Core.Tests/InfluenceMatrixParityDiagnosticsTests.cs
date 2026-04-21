using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using XFoil.Core.Diagnostics;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f influence assembly and paneling lineage
// Secondary legacy source: tools/fortran-debug trace instrumentation and reference runtime dumps
// Role in port: Verifies the managed parity-diagnostics harness used to localize influence-matrix mismatches against the legacy runtime.
// Differences: These tests are managed-only diagnostic infrastructure that compare JSON traces and structured mismatch reports rather than exercising a legacy feature directly.
// Decision: Keep this managed-only diagnostics suite because it is essential to solver-fidelity work and has no direct Fortran analogue as a test harness.
namespace XFoil.Core.Tests;

[Trait("Category", "Diagnostic")]
public class InfluenceMatrixParityDiagnosticsTests
{
    private const int ClassicXFoilNacaPointCount = 239;
    private readonly ITestOutputHelper output;

    public InfluenceMatrixParityDiagnosticsTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    // Legacy mapping: initial airfoil DIJ mismatch against the legacy xpanel assembly.
    // Difference from legacy: The managed test reports the first mismatch through structured diagnostics rather than manual debugging. Decision: Keep the managed parity harness because it is the main localization tool for this subsystem.
    public void Naca0012_Re1e6_Alpha0_ReportsInitialAirfoilDijMismatch()
    {
        const int panelCount = 160;
        var geometry = BuildNacaSymmetric0012(panelCount);
        var settings = new AnalysisSettings(
            panelCount: panelCount,
            reynoldsNumber: 1_000_000,
            machNumber: 0.0,

            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: 20,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: 9.0,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);

        int maxNodes = settings.PanelCount + 40;
        var panel = new LinearVortexPanelState(maxNodes);
        var inviscidState = new InviscidSolverState(maxNodes);

        CurvatureAdaptivePanelDistributor.Distribute(
            geometry.x, geometry.y, geometry.x.Length,
            panel, settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        inviscidState.InitializeForNodeCount(panel.NodeCount);
        ConfigureLegacyParityState(inviscidState);

        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians: 0.0,
            panel,
            inviscidState,
            settings.FreestreamVelocity,
            settings.MachNumber);

        int n = panel.NodeCount;
        int nWake = Math.Max((n / 8) + 2, 3);
        double[] qinv = EdgeVelocityCalculator.SetInviscidSpeeds(
            inviscidState.BasisInviscidSpeed, n, 0.0);
        var (isp, sst) = InvokeFindStagnationPoint(qinv, panel, n);
        isp = Math.Max(1, Math.Min(n - 2, isp));
        var (iblte, nbl) = InvokeComputeStationCounts(n, isp, nWake);

        int maxStations = Math.Max(nbl[0], nbl[1]) + nWake + 10;
        var blState = new BoundaryLayerSystemState(maxStations, nWake);
        blState.IBLTE[0] = iblte[0];
        blState.IBLTE[1] = iblte[1];
        blState.NBL[0] = nbl[0];
        blState.NBL[1] = nbl[1];

        InvokeInitializeStationMapping(blState, panel, qinv, isp, sst, n, nWake);
        InvokeInitializeBlThwaites(blState, settings, settings.ReynoldsNumber);

        object wakeGeometry = InvokeBuildWakeGeometry(
            panel,
            inviscidState,
            nWake,
            settings.FreestreamVelocity,
            angleOfAttackRadians: 0.0);

        double[] wakeX = GetWakeArray(wakeGeometry, "X");
        double[] wakeY = GetWakeArray(wakeGeometry, "Y");
        double[] wakeNx = GetWakeArray(wakeGeometry, "NormalX");
        double[] wakeNy = GetWakeArray(wakeGeometry, "NormalY");
        double[] wakePanelAngle = GetWakeArray(wakeGeometry, "PanelAngle");

        var wakeSurfaceSensitivity = InvokeComputeWakeSourceSensitivitiesAt(
            wakeGeometry,
            fieldNodeIndex: blState.IPAN[1, 0] + 1,
            panel.X[blState.IPAN[1, 0]],
            panel.Y[blState.IPAN[1, 0]],
            panel.NormalX[blState.IPAN[1, 0]],
            panel.NormalY[blState.IPAN[1, 0]],
            fieldWakeIndex: -1,
            useLegacyPrecision: true);

        var ueInv = new double[maxStations, 2];
        for (int side = 0; side < 2; side++)
        {
            for (int ibl = 0; ibl < blState.NBL[side]; ibl++)
            {
                ueInv[ibl, side] = blState.UEDG[ibl, side];
            }
        }

        double[,] numerical = InfluenceMatrixBuilder.BuildNumericalDIJ(inviscidState, panel, nWake);
        double[,] analytical = InfluenceMatrixBuilder.BuildAnalyticalDIJ(inviscidState, panel, nWake);
        double[,] hybrid = BuildHybridDij(analytical, numerical, n, nWake);

        var marchedState = Clone(blState);
        double marchedResidual = InvokeMarchBoundaryLayer(marchedState, settings, settings.ReynoldsNumber);

        var numericalUpper = ComputeUsavSplit(blState, numerical, ueInv, side: 0, ibl: 1);
        var analyticalUpper = ComputeUsavSplit(blState, analytical, ueInv, side: 0, ibl: 1);
        var hybridUpper = ComputeUsavSplit(blState, hybrid, ueInv, side: 0, ibl: 1);
        var marchedUpper = ComputeUsavSplit(marchedState, numerical, ueInv, side: 0, ibl: 1);

        var numericalLower = ComputeUsavSplit(blState, numerical, ueInv, side: 1, ibl: 1);
        var analyticalLower = ComputeUsavSplit(blState, analytical, ueInv, side: 1, ibl: 1);
        var hybridLower = ComputeUsavSplit(blState, hybrid, ueInv, side: 1, ibl: 1);
        var marchedLower = ComputeUsavSplit(marchedState, numerical, ueInv, side: 1, ibl: 1);

        var airfoilError = FindWorstAirfoilBlockDifference(analytical, numerical, n);
        var wakeColumnComparison = SolveFirstWakeColumnOnUnfactoredMatrix(
            panel,
            wakeGeometry,
            maxNodes,
            settings.FreestreamVelocity);
        var referencePanelComparison = TrySolveFirstWakeColumnOnReferencePanelGeometry(
            maxNodes,
            settings.FreestreamVelocity);
        var referenceAijComparison = TryCompareReferenceAijAndGamu(
            maxNodes,
            settings.FreestreamVelocity);
        var referenceCrossSolve = TryCrossSolveReferenceWakeColumn(
            maxNodes,
            settings.FreestreamVelocity);
        var referenceWakeKernelComparison = TryCompareReferenceWakeKernel(
            maxNodes,
            settings.FreestreamVelocity);
        var referenceWakeKernelWithFieldGeometryComparison = TryCompareReferenceWakeKernelWithReferenceFieldGeometry();
        var referencePanelGeometryComparison = TryCompareReferencePanelGeometry(
            maxNodes,
            settings.FreestreamVelocity);
        var referenceGamuComparison = TryCompareReferenceGamuWindow(
            maxNodes,
            settings.FreestreamVelocity);

        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Airfoil DIJ worst delta: row={0} col={1} analytical={2:E8} numerical={3:E8} abs={4:E8}",
            airfoilError.row,
            airfoilError.col,
            airfoilError.analytical,
            airfoilError.numerical,
            airfoilError.error));
        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "MarchBoundaryLayer RMS={0:E8}", marchedResidual));
        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Wake column solve: row={0} dense={1:E8} lu={2:E8} analytical={3:E8} maxDenseLu={4:E8} maxDenseAnalytical={5:E8}",
            wakeColumnComparison.probeRow + 1,
            wakeColumnComparison.denseProbe,
            wakeColumnComparison.luProbe,
            wakeColumnComparison.analyticalProbe,
            wakeColumnComparison.maxDenseLuError,
            wakeColumnComparison.maxDenseAnalyticalError));
        if (referencePanelComparison is not null)
        {
            var reference = referencePanelComparison.Value;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Reference panel wake column: row={0} dense={1:E8} ref={2:E8} maxRhsDelta={3:E8}@{4} c#rhs={5:E8} refrhs={6:E8} maxSolDelta={7:E8}",
                reference.probeRow + 1,
                reference.denseProbe,
                reference.referenceProbe,
                reference.maxRhsDelta,
                reference.maxRhsRow + 1,
                reference.csharpRhsAtMaxRow,
                reference.referenceRhsAtMaxRow,
                reference.maxSolutionDelta));
            if (reference.wakeGeometryDelta is not null)
            {
                var wake = reference.wakeGeometryDelta.Value;
                output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Reference wake geometry delta: x={0:E8} y={1:E8} nx={2:E8} ny={3:E8} apan={4:E8}",
                    wake.maxX,
                    wake.maxY,
                    wake.maxNx,
                    wake.maxNy,
                    wake.maxPanelAngle));
            }
        }
        if (referenceAijComparison is not null)
        {
            var reference = referenceAijComparison.Value;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Reference AIJ/GAMU: row1={0:E8}@{1} rowMid={2:E8}@{3} rowTe={4:E8}@{5} rowKutta={6:E8}@{7} gamu={8:E8}",
                reference.row1.delta,
                reference.row1.column,
                reference.rowMid.delta,
                reference.rowMid.column,
                reference.rowTe.delta,
                reference.rowTe.column,
                reference.rowKutta.delta,
                reference.rowKutta.column,
                reference.gamuDelta));
        }
        if (referenceCrossSolve is not null)
        {
            var reference = referenceCrossSolve.Value;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Reference cross-solve: ref/ref={0:E8} ref/c#rhs={1:E8} c#mat/refrhs={2:E8} ref={3:E8} maxRefRef={4:E8} maxRefCSharpRhs={5:E8} maxCSharpRefRhs={6:E8}",
                reference.refRefProbe,
                reference.refWithCSharpRhsProbe,
                reference.csharpWithRefRhsProbe,
                reference.referenceProbe,
                reference.maxRefRefDelta,
                reference.maxRefWithCSharpRhsDelta,
                reference.maxCSharpWithRefRhsDelta));
        }
        if (referenceWakeKernelComparison is not null)
        {
            var reference = referenceWakeKernelComparison.Value;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Reference wake kernel: managed/refGeom row={0} rhs={1:E8} ref={2:E8} maxDelta={3:E8}@{4} c#rhs={5:E8} refrhs={6:E8}",
                reference.probeRow + 1,
                reference.managedProbe,
                reference.referenceProbe,
                reference.maxDelta,
                reference.maxDeltaRow + 1,
                reference.managedAtMaxRow,
                reference.referenceAtMaxRow));
        }
        if (referenceWakeKernelWithFieldGeometryComparison is not null)
        {
            var reference = referenceWakeKernelWithFieldGeometryComparison.Value;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Reference wake kernel + fieldGeom: row={0} rhs={1:E8} ref={2:E8} maxDelta={3:E8}@{4} c#rhs={5:E8} refrhs={6:E8}",
                reference.probeRow + 1,
                reference.managedProbe,
                reference.referenceProbe,
                reference.maxDelta,
                reference.maxDeltaRow + 1,
                reference.managedAtMaxRow,
                reference.referenceAtMaxRow));
        }
        if (referencePanelGeometryComparison is not null)
        {
            var reference = referencePanelGeometryComparison.Value;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Reference panel geometry: nx79={0:E8} ny79={1:E8} apan79={2:E8} | nx80={3:E8} ny80={4:E8} apan80={5:E8} | nx81={6:E8} ny81={7:E8} apan81={8:E8}",
                reference.nx79Delta,
                reference.ny79Delta,
                reference.apan79Delta,
                reference.nx80Delta,
                reference.ny80Delta,
                reference.apan80Delta,
                reference.nx81Delta,
                reference.ny81Delta,
                reference.apan81Delta));
        }
        if (referenceGamuComparison is not null)
        {
            var reference = referenceGamuComparison.Value;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Reference GAMU window: u1_79={0:E8} u1_80={1:E8} u1_81={2:E8} u1_82={3:E8} u1_83={4:E8}",
                reference.u1_79Delta,
                reference.u1_80Delta,
                reference.u1_81Delta,
                reference.u1_82Delta,
                reference.u1_83Delta));
        }
        for (int row = 0; row < wakeColumnComparison.rhs.Length; row++)
        {
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "C# WAKE_RHS_COL I={0,4} BIJ={1:E8}",
                row + 1,
                wakeColumnComparison.rhs[row]));
        }

        for (int row = 0; row < wakeColumnComparison.denseSolution.Length; row++)
        {
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "C# WAKE_SOL_COL I={0,4} BIJ={1:E8}",
                row + 1,
                wakeColumnComparison.denseSolution[row]));
        }

        for (int iw = 0; iw < Math.Min(5, wakeX.Length); iw++)
        {
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Wake node {0}: x={1:E8} y={2:E8} nx={3:E8} ny={4:E8} apan={5:E8} rhs={6:E8}",
                iw + 1,
                wakeX[iw],
                wakeY[iw],
                wakeNx[iw],
                wakeNy[iw],
                iw < wakePanelAngle.Length ? wakePanelAngle[iw] : 0.0,
                -wakeSurfaceSensitivity.dzdm[iw]));
        }

        output.WriteLine(FormatSplit("Numerical upper", numericalUpper));
        output.WriteLine(FormatSplit("Analytical upper", analyticalUpper));
        output.WriteLine(FormatSplit("Hybrid upper", hybridUpper));
        output.WriteLine(FormatSplit("Marched upper", marchedUpper));
        output.WriteLine(FormatSplit("Numerical lower", numericalLower));
        output.WriteLine(FormatSplit("Analytical lower", analyticalLower));
        output.WriteLine(FormatSplit("Hybrid lower", hybridLower));
        output.WriteLine(FormatSplit("Marched lower", marchedLower));

        Assert.True(double.IsFinite(airfoilError.error));
    }

    [Fact]
    // Legacy mapping: legacy spline derivative boundary during reference panel-node generation.
    // Difference from legacy: The parity boundary is surfaced as a managed assertion over trace data. Decision: Keep the managed diagnostic regression because it codifies a previously manual parity check.
    public void ReferencePanelNodes_LegacySplineReportsDerivativeParityBoundary()
    {
        string tracePath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_trace.jsonl");
        if (!File.Exists(tracePath))
        {
            throw new FileNotFoundException("Reference trace not found.", tracePath);
        }

        var referenceNodes = ReadReferencePanelNodes(tracePath);
        Assert.NotEmpty(referenceNodes);

        var panel = BuildPanelFromNodes(referenceNodes.Select(node => (node.x, node.y)).ToList());
        PanelGeometryBuilder.ComputeNormals(panel, useLegacyPrecision: true);

        double maxXpDelta = 0.0;
        int maxXpIndex = -1;
        double maxYpDelta = 0.0;
        int maxYpIndex = -1;
        double maxNxDelta = 0.0;
        int maxNxIndex = -1;
        double maxNyDelta = 0.0;
        int maxNyIndex = -1;

        for (int i = 0; i < Math.Min(panel.NodeCount, referenceNodes.Count); i++)
        {
            double xpDelta = Math.Abs(panel.XDerivative[i] - referenceNodes[i].xp);
            double ypDelta = Math.Abs(panel.YDerivative[i] - referenceNodes[i].yp);
            double nxDelta = Math.Abs(panel.NormalX[i] - referenceNodes[i].nx);
            double nyDelta = Math.Abs(panel.NormalY[i] - referenceNodes[i].ny);

            if (xpDelta > maxXpDelta)
            {
                maxXpDelta = xpDelta;
                maxXpIndex = i + 1;
            }

            if (ypDelta > maxYpDelta)
            {
                maxYpDelta = ypDelta;
                maxYpIndex = i + 1;
            }

            if (nxDelta > maxNxDelta)
            {
                maxNxDelta = nxDelta;
                maxNxIndex = i + 1;
            }

            if (nyDelta > maxNyDelta)
            {
                maxNyDelta = nyDelta;
                maxNyIndex = i + 1;
            }
        }

        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Reference-node derivative replay: maxXp={0:E8}@{1} maxYp={2:E8}@{3} maxNx={4:E8}@{5} maxNy={6:E8}@{7}",
            maxXpDelta,
            maxXpIndex,
            maxYpDelta,
            maxYpIndex,
            maxNxDelta,
            maxNxIndex,
            maxNyDelta,
            maxNyIndex));

        Assert.True(double.IsFinite(maxXpDelta));
    }

    [Fact]
    // Legacy mapping: reference buffer-node generation with legacy spline rules.
    // Difference from legacy: The managed test inspects structured parity diagnostics instead of ad hoc debug output. Decision: Keep the managed harness because it preserves a reproducible parity-localization step.
    public void ReferenceBufferNodes_LegacySplineReportsParityBoundary()
    {
        string tracePath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_trace.jsonl");
        if (!File.Exists(tracePath))
        {
            throw new FileNotFoundException("Reference trace not found.", tracePath);
        }

        var bufferNodes = ReadReferenceBufferSplineNodes(tracePath);
        Assert.NotEmpty(bufferNodes);

        var x = bufferNodes.Select(node => (float)node.x).ToArray();
        var y = bufferNodes.Select(node => (float)node.y).ToArray();
        var s = new float[bufferNodes.Count];
        var xp = new float[bufferNodes.Count];
        var yp = new float[bufferNodes.Count];

        ParametricSpline.ComputeArcLength(x, y, s, bufferNodes.Count);
        ParametricSpline.FitSegmented(x, xp, s, bufferNodes.Count);
        ParametricSpline.FitSegmented(y, yp, s, bufferNodes.Count);

        double maxArcDelta = 0.0;
        int maxArcIndex = -1;
        double maxXpDelta = 0.0;
        int maxXpIndex = -1;
        double maxYpDelta = 0.0;
        int maxYpIndex = -1;

        for (int i = 0; i < bufferNodes.Count; i++)
        {
            double arcDelta = Math.Abs(s[i] - bufferNodes[i].arcLength);
            double xpDelta = Math.Abs(xp[i] - bufferNodes[i].xp);
            double ypDelta = Math.Abs(yp[i] - bufferNodes[i].yp);

            if (arcDelta > maxArcDelta)
            {
                maxArcDelta = arcDelta;
                maxArcIndex = i + 1;
            }

            if (xpDelta > maxXpDelta)
            {
                maxXpDelta = xpDelta;
                maxXpIndex = i + 1;
            }

            if (ypDelta > maxYpDelta)
            {
                maxYpDelta = ypDelta;
                maxYpIndex = i + 1;
            }
        }

        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Reference-buffer spline replay: maxArc={0:E8}@{1} maxXp={2:E8}@{3} maxYp={4:E8}@{5}",
            maxArcDelta,
            maxArcIndex,
            maxXpDelta,
            maxXpIndex,
            maxYpDelta,
            maxYpIndex));

        Assert.True(double.IsFinite(maxXpDelta));
    }

    [Fact]
    // Legacy mapping: classic NACA generator contribution to the reference buffer-node chain.
    // Difference from legacy: The managed parity harness isolates this generator-level boundary explicitly. Decision: Keep the managed diagnostic regression because it helps localize pre-paneling mismatches.
    public void ReferenceBufferNodes_ClassicNacaGeneratorReportsParityBoundary()
    {
        string tracePath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_trace.jsonl");
        if (!File.Exists(tracePath))
        {
            throw new FileNotFoundException("Reference trace not found.", tracePath);
        }

        var referenceNodes = ReadReferenceBufferNodes(tracePath);
        var generated = BuildNacaSymmetric0012(160);
        Assert.Equal(referenceNodes.Count, generated.x.Length);

        double maxXDelta = 0.0;
        double maxYDelta = 0.0;
        int maxXIndex = -1;
        int maxYIndex = -1;

        for (int i = 0; i < referenceNodes.Count; i++)
        {
            double xDelta = Math.Abs(generated.x[i] - referenceNodes[i].x);
            double yDelta = Math.Abs(generated.y[i] - referenceNodes[i].y);

            if (xDelta > maxXDelta)
            {
                maxXDelta = xDelta;
                maxXIndex = i + 1;
            }

            if (yDelta > maxYDelta)
            {
                maxYDelta = yDelta;
                maxYIndex = i + 1;
            }
        }

        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Reference-buffer geometry: maxX={0:E8}@{1} maxY={2:E8}@{3}",
            maxXDelta,
            maxXIndex,
            maxYDelta,
            maxYIndex));

        Assert.True(double.IsFinite(maxXDelta));
    }

    [Fact]
    // Legacy mapping: classic NACA trace path for reference buffer nodes.
    // Difference from legacy: JSONL trace emission is a managed-only observability feature. Decision: Keep the managed diagnostic test because it is required for repeatable parity debugging.
    public void ReferenceBufferNodes_ClassicNacaTrace_WritesJsonl()
    {
        string repoRoot = FindRepoRoot();
        string tracePath = Path.Combine(repoRoot, "tools", "fortran-debug", "csharp_reference_naca_trace.jsonl");

        using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new
        {
            caseName = "ReferenceBufferClassicNacaTrace",
            designation = "0012",
            pointCount = ClassicXFoilNacaPointCount
        });
        using var coreTrace = CoreTrace.Begin((kind, scope, data) => traceWriter.WriteEvent(kind, scope, data));

        _ = BuildNacaSymmetric0012(160);

        output.WriteLine($"Reference buffer NACA trace written to: {tracePath}");
        Assert.True(File.Exists(tracePath));
    }

    [Fact]
    // Legacy mapping: PANGEN contribution to the reference buffer-node chain.
    // Difference from legacy: The parity boundary is asserted through managed trace comparison rather than manual analysis. Decision: Keep the managed diagnostic regression because it isolates paneling mismatches reproducibly.
    public void ReferenceBufferNodes_PangenReportsParityBoundary()
    {
        string tracePath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_trace.jsonl");
        if (!File.Exists(tracePath))
        {
            throw new FileNotFoundException("Reference trace not found.", tracePath);
        }

        var referenceBufferNodes = ReadReferenceBufferNodes(tracePath);
        var referencePanelNodes = ReadReferencePanelNodes(tracePath);

        var panel = new LinearVortexPanelState(referencePanelNodes.Count);
        CurvatureAdaptivePanelDistributor.Distribute(
            referenceBufferNodes.Select(node => node.x).ToArray(),
            referenceBufferNodes.Select(node => node.y).ToArray(),
            referenceBufferNodes.Count,
            panel,
            referencePanelNodes.Count,
            useLegacyPrecision: true);

        double maxXDelta = 0.0;
        double maxYDelta = 0.0;
        double maxXpDelta = 0.0;
        double maxYpDelta = 0.0;
        int maxXIndex = -1;
        int maxYIndex = -1;
        int maxXpIndex = -1;
        int maxYpIndex = -1;

        for (int i = 0; i < referencePanelNodes.Count; i++)
        {
            double xDelta = Math.Abs(panel.X[i] - referencePanelNodes[i].x);
            double yDelta = Math.Abs(panel.Y[i] - referencePanelNodes[i].y);
            double xpDelta = Math.Abs(panel.XDerivative[i] - referencePanelNodes[i].xp);
            double ypDelta = Math.Abs(panel.YDerivative[i] - referencePanelNodes[i].yp);

            if (xDelta > maxXDelta)
            {
                maxXDelta = xDelta;
                maxXIndex = i + 1;
            }

            if (yDelta > maxYDelta)
            {
                maxYDelta = yDelta;
                maxYIndex = i + 1;
            }

            if (xpDelta > maxXpDelta)
            {
                maxXpDelta = xpDelta;
                maxXpIndex = i + 1;
            }

            if (ypDelta > maxYpDelta)
            {
                maxYpDelta = ypDelta;
                maxYpIndex = i + 1;
            }
        }

        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Reference-buffer PANGEN replay: maxX={0:E8}@{1} maxY={2:E8}@{3} maxXp={4:E8}@{5} maxYp={6:E8}@{7}",
            maxXDelta,
            maxXIndex,
            maxYDelta,
            maxYIndex,
            maxXpDelta,
            maxXpIndex,
            maxYpDelta,
            maxYpIndex));

        Assert.True(double.IsFinite(maxXpDelta));
    }

    [Fact]
    // Legacy mapping: PANGEN trace path in the paneling parity workflow.
    // Difference from legacy: JSONL diagnostics are a managed-only tracing layer. Decision: Keep the managed diagnostic test because it preserves the trace artifact needed for parity work.
    public void ReferenceBufferNodes_PangenTrace_WritesJsonl()
    {
        string repoRoot = FindRepoRoot();
        string referenceTracePath = Path.Combine(repoRoot, "tools", "fortran-debug", "reference_trace.jsonl");
        if (!File.Exists(referenceTracePath))
        {
            throw new FileNotFoundException("Reference trace not found.", referenceTracePath);
        }

        var referenceBufferNodes = ReadReferenceBufferNodes(referenceTracePath);
        var referencePanelNodes = ReadReferencePanelNodes(referenceTracePath);
        var panel = new LinearVortexPanelState(referencePanelNodes.Count);

        string tracePath = Path.Combine(repoRoot, "tools", "fortran-debug", "csharp_reference_pangen_trace.jsonl");
        using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new
        {
            caseName = "ReferenceBufferPangenTrace",
            bufferNodeCount = referenceBufferNodes.Count,
            panelNodeCount = referencePanelNodes.Count
        });
        using var traceScope = SolverTrace.Begin(traceWriter);

        CurvatureAdaptivePanelDistributor.Distribute(
            referenceBufferNodes.Select(node => node.x).ToArray(),
            referenceBufferNodes.Select(node => node.y).ToArray(),
            referenceBufferNodes.Count,
            panel,
            referencePanelNodes.Count,
            useLegacyPrecision: true);

        output.WriteLine($"Reference buffer PANGEN trace written to: {tracePath}");
        Assert.True(File.Exists(tracePath));
    }

    [Fact]
    // Legacy mapping: legacy wake-kernel influence path.
    // Difference from legacy: The managed test writes and validates structured wake-kernel traces rather than relying on manual runtime instrumentation. Decision: Keep the managed diagnostic harness because it is essential for localizing wake-kernel parity drift.
    public void ReferenceWakeKernelTrace_WritesJsonl()
    {
        string repoRoot = FindRepoRoot();
        string referenceTracePath = Path.Combine(repoRoot, "tools", "fortran-debug", "reference_trace.jsonl");
        string referenceDumpPath = Path.Combine(repoRoot, "tools", "fortran-debug", "reference_dump.txt");
        if (!File.Exists(referenceTracePath))
        {
            throw new FileNotFoundException("Reference trace not found.", referenceTracePath);
        }
        if (!File.Exists(referenceDumpPath))
        {
            throw new FileNotFoundException("Reference dump not found.", referenceDumpPath);
        }

        var referencePanelNodes = ReadReferencePanelNodes(referenceTracePath);
        var referenceWakeNodes = ReadReferenceWakeNodesFromDump(referenceDumpPath);
        Assert.NotEmpty(referencePanelNodes);
        Assert.NotEmpty(referenceWakeNodes);

        var panel = BuildPanelFromNodes(referencePanelNodes.Select(node => (node.x, node.y)).ToList());
        object wakeGeometry = BuildWakeGeometryFromReference(referenceWakeNodes, referenceWakeNodes.Count);

        string tracePath = Path.Combine(repoRoot, "tools", "fortran-debug", "csharp_reference_kernel_trace.jsonl");
        using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new
        {
            caseName = "ReferenceWakeKernelTrace",
            fieldIndexFrom = 70,
            fieldIndexTo = 85
        });
        using var traceScope = SolverTrace.Begin(traceWriter);

        for (int fieldIndex = 70; fieldIndex <= 85; fieldIndex++)
        {
            int zeroBased = fieldIndex - 1;
            InvokeComputeWakeSourceSensitivitiesAt(
                wakeGeometry,
                fieldNodeIndex: fieldIndex,
                fieldX: referencePanelNodes[zeroBased].x,
                fieldY: referencePanelNodes[zeroBased].y,
                fieldNormalX: referencePanelNodes[zeroBased].nx,
                fieldNormalY: referencePanelNodes[zeroBased].ny,
                fieldWakeIndex: -1,
                useLegacyPrecision: true);
        }

        output.WriteLine($"Reference wake kernel trace written to: {tracePath}");
        Assert.True(File.Exists(tracePath));
    }

    private static void ConfigureLegacyParityState(InviscidSolverState state)
    {
        state.UseLegacyKernelPrecision = true;
        state.UseLegacyPanelingPrecision = true;
    }

    private static List<(double x, double y, double xp, double yp, double nx, double ny)> ReadReferencePanelNodes(string tracePath)
    {
        var nodes = new List<(double x, double y, double xp, double yp, double nx, double ny)>();
        bool inBlock = false;
        foreach (string line in File.ReadLines(tracePath))
        {
            if (!line.Contains("\"kind\":\"panel_node\"", StringComparison.Ordinal))
            {
                if (inBlock)
                {
                    break;
                }

                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "panel_node")
            {
                continue;
            }

            JsonElement data = root.GetProperty("data");
            inBlock = true;
            nodes.Add((
                data.GetProperty("x").GetDouble(),
                data.GetProperty("y").GetDouble(),
                data.GetProperty("xp").GetDouble(),
                data.GetProperty("yp").GetDouble(),
                data.GetProperty("nx").GetDouble(),
                data.GetProperty("ny").GetDouble()));
        }

        return nodes;
    }

    private static List<(double x, double y)> ReadReferenceBufferNodes(string tracePath)
    {
        var nodes = new List<(double x, double y)>();
        bool inBlock = false;
        foreach (string line in File.ReadLines(tracePath))
        {
            if (!line.Contains("\"kind\":\"buffer_node\"", StringComparison.Ordinal))
            {
                if (inBlock)
                {
                    break;
                }

                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "buffer_node")
            {
                continue;
            }

            JsonElement data = root.GetProperty("data");
            inBlock = true;
            int index = data.GetProperty("index").GetInt32();
            if (nodes.Count > 0 && index == 1)
            {
                break;
            }

            nodes.Add((
                data.GetProperty("x").GetDouble(),
                data.GetProperty("y").GetDouble()));
        }

        return nodes;
    }

    private static List<(double x, double y, double arcLength, double xp, double yp)> ReadReferenceBufferSplineNodes(string tracePath)
    {
        var nodes = new List<(double x, double y, double arcLength, double xp, double yp)>();
        bool inBlock = false;
        foreach (string line in File.ReadLines(tracePath))
        {
            if (!line.Contains("\"kind\":\"buffer_spline_node\"", StringComparison.Ordinal))
            {
                if (inBlock)
                {
                    break;
                }

                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "buffer_spline_node")
            {
                continue;
            }

            JsonElement data = root.GetProperty("data");
            inBlock = true;
            nodes.Add((
                data.GetProperty("x").GetDouble(),
                data.GetProperty("y").GetDouble(),
                data.GetProperty("arcLength").GetDouble(),
                data.GetProperty("xp").GetDouble(),
                data.GetProperty("yp").GetDouble()));
        }

        return nodes;
    }

    private static List<(double x, double y, double nx, double ny, double panelAngle)> ReadReferenceWakeNodesFromDump(string dumpPath)
    {
        var nodes = new List<(double x, double y, double nx, double ny, double panelAngle)>();
        var wakeNodeRegex = new Regex(
            @"^WAKE_NODE IW=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)\s+NX=\s*([+-]?\d\.\d+E[+-]\d+)\s+NY=\s*([+-]?\d\.\d+E[+-]\d+)\s+APAN=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match match = wakeNodeRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (nodes.Count > 0 && index == 1)
            {
                break;
            }

            nodes.Add((
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture)));
        }

        return nodes;
    }

    private static (int isp, double sst) InvokeFindStagnationPoint(
        double[] qinv,
        LinearVortexPanelState panel,
        int n)
    {
        var method = typeof(ViscousSolverEngine).GetMethod(
            "FindStagnationPointXFoil",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("FindStagnationPointXFoil not found.");

        return ((int, double))method.Invoke(null, new object[] { qinv, panel, n, true })!;
    }

    private static (int[] iblte, int[] nbl) InvokeComputeStationCounts(int n, int isp, int nWake)
    {
        var method = typeof(ViscousSolverEngine).GetMethod(
            "ComputeStationCountsXFoil",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeStationCountsXFoil not found.");

        return ((int[], int[]))method.Invoke(null, new object[] { n, isp, nWake })!;
    }

    private static void InvokeInitializeStationMapping(
        BoundaryLayerSystemState blState,
        LinearVortexPanelState panel,
        double[] qinv,
        int isp,
        double sst,
        int n,
        int nWake)
    {
        var method = typeof(ViscousSolverEngine).GetMethod(
            "InitializeXFoilStationMapping",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("InitializeXFoilStationMapping not found.");

        method.Invoke(null, new object[] { blState, panel, qinv, isp, sst, n, nWake });
    }

    private static void InvokeInitializeBlThwaites(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double reinf)
    {
        var method = typeof(ViscousSolverEngine).GetMethod(
            "InitializeBLThwaitesXFoil",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(BoundaryLayerSystemState), typeof(AnalysisSettings), typeof(double) },
            modifiers: null)
            ?? throw new InvalidOperationException("InitializeBLThwaitesXFoil not found.");

        method.Invoke(null, new object[] { blState, settings, reinf });
    }

    private static object InvokeBuildWakeGeometry(
        LinearVortexPanelState panel,
        InviscidSolverState inviscidState,
        int nWake,
        double freestreamSpeed,
        double angleOfAttackRadians)
    {
        var method = typeof(InfluenceMatrixBuilder).GetMethod(
            "BuildWakeGeometry",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildWakeGeometry not found.");

        return method.Invoke(null, new object[] { panel, inviscidState, nWake, freestreamSpeed, angleOfAttackRadians })!;
    }

    private static (double[] dzdm, double[] dqdm) InvokeComputeWakeSourceSensitivitiesAt(
        object wakeGeometry,
        int fieldNodeIndex,
        double fieldX,
        double fieldY,
        double fieldNormalX,
        double fieldNormalY,
        int fieldWakeIndex,
        bool useLegacyPrecision = false)
    {
        var method = typeof(InfluenceMatrixBuilder).GetMethod(
            useLegacyPrecision
                ? "ComputeWakeSourceSensitivitiesAtLegacyPrecision"
                : "ComputeWakeSourceSensitivitiesAt",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeWakeSourceSensitivitiesAt method not found.");

        object?[] args =
        {
            wakeGeometry,
            fieldNodeIndex,
            fieldX,
            fieldY,
            fieldNormalX,
            fieldNormalY,
            fieldWakeIndex,
            null,
            null
        };

        method.Invoke(null, args);
        return ((double[])args[7]!, (double[])args[8]!);
    }

    private static double[] GetWakeArray(object wakeGeometry, string propertyName)
    {
        var property = wakeGeometry.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"{propertyName} not found on wake geometry.");

        return (double[])property.GetValue(wakeGeometry)!;
    }

    private static (int probeRow, double denseProbe, double luProbe, double analyticalProbe, double maxDenseLuError, double maxDenseAnalyticalError, double[] rhs, double[] denseSolution)
        SolveFirstWakeColumnOnUnfactoredMatrix(
            LinearVortexPanelState panel,
            object wakeGeometry,
            int maxNodes,
            double freestreamSpeed)
    {
        var assembledState = new InviscidSolverState(maxNodes);
        assembledState.InitializeForNodeCount(panel.NodeCount);
        ConfigureLegacyParityState(assembledState);
        int systemSize = InvokeAssembleSystem(panel, assembledState, freestreamSpeed);
        int n = panel.NodeCount;

        double[] rhs = BuildWakeColumnRightHandSide(panel, assembledState, wakeGeometry, wakeColumnIndex: 0);
        double[] denseSolution = new DenseLinearSystemSolver().Solve(
            CloneMatrix(assembledState.StreamfunctionInfluence, systemSize),
            rhs);

        double[,] luMatrix = CloneMatrix(assembledState.StreamfunctionInfluence, systemSize);
        int[] pivots = new int[systemSize];
        double[] luSolution = (double[])rhs.Clone();
        ScaledPivotLuSolver.Decompose(luMatrix, pivots, systemSize);
        ScaledPivotLuSolver.BackSubstitute(luMatrix, pivots, luSolution, systemSize);

        var analyticalState = new InviscidSolverState(maxNodes);
        analyticalState.InitializeForNodeCount(panel.NodeCount);
        ConfigureLegacyParityState(analyticalState);
        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians: 0.0,
            panel,
            analyticalState,
            freestreamSpeed,
            machNumber: 0.0);

        double[,] analytical = InfluenceMatrixBuilder.BuildAnalyticalDIJ(analyticalState, panel, nWake: GetWakeArray(wakeGeometry, "X").Length);
        int probeRow = n / 2 - 1;
        double maxDenseLuError = 0.0;
        double maxDenseAnalyticalError = 0.0;
        for (int row = 0; row < n; row++)
        {
            maxDenseLuError = Math.Max(maxDenseLuError, Math.Abs(denseSolution[row] - luSolution[row]));
            maxDenseAnalyticalError = Math.Max(maxDenseAnalyticalError, Math.Abs(denseSolution[row] - analytical[row, n]));
        }

        return (
            probeRow,
            denseSolution[probeRow],
            luSolution[probeRow],
            analytical[probeRow, n],
            maxDenseLuError,
            maxDenseAnalyticalError,
            rhs,
            denseSolution);
    }

    private static (int probeRow, double denseProbe, double referenceProbe, double maxRhsDelta, int maxRhsRow, double csharpRhsAtMaxRow, double referenceRhsAtMaxRow, double maxSolutionDelta, (double maxX, double maxY, double maxNx, double maxNy, double maxPanelAngle)? wakeGeometryDelta)? TrySolveFirstWakeColumnOnReferencePanelGeometry(
        int maxNodes,
        double freestreamSpeed)
    {
        string dumpPath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_dump.txt");
        if (!File.Exists(dumpPath))
        {
            return null;
        }

        var panelNodes = new List<(double x, double y)>();
        var referenceWakeNodes = ReadReferenceWakeNodesFromDump(dumpPath);
        var referenceRhs = new List<double>();
        var referenceSolution = new List<double>();
        var panelRegex = new Regex(
            @"^PANEL_NODE I=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var rhsRegex = new Regex(
            @"^WAKE_RHS_COL I=\s*(\d+)\s+BIJ=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var solRegex = new Regex(
            @"^WAKE_SOL_COL I=\s*(\d+)\s+BIJ=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match panelMatch = panelRegex.Match(line);
            if (panelMatch.Success)
            {
                panelNodes.Add((
                    double.Parse(panelMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(panelMatch.Groups[3].Value, CultureInfo.InvariantCulture)));
                continue;
            }

            Match rhsMatch = rhsRegex.Match(line);
            if (rhsMatch.Success)
            {
                referenceRhs.Add(double.Parse(rhsMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                continue;
            }

            Match solMatch = solRegex.Match(line);
            if (solMatch.Success)
            {
                referenceSolution.Add(double.Parse(solMatch.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }

        if (panelNodes.Count == 0 || referenceRhs.Count == 0 || referenceSolution.Count == 0)
        {
            return null;
        }

        var referencePanel = BuildPanelFromNodes(panelNodes);
        var analyticalState = new InviscidSolverState(maxNodes);
        analyticalState.InitializeForNodeCount(referencePanel.NodeCount);
        ConfigureLegacyParityState(analyticalState);
        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians: 0.0,
            referencePanel,
            analyticalState,
            freestreamSpeed,
            machNumber: 0.0);

        int nWake = Math.Max((referencePanel.NodeCount / 8) + 2, 3);
        object wakeGeometry = InvokeBuildWakeGeometry(
            referencePanel,
            analyticalState,
            nWake,
            freestreamSpeed,
            angleOfAttackRadians: 0.0);
        double[] wakeX = GetWakeArray(wakeGeometry, "X");
        double[] wakeY = GetWakeArray(wakeGeometry, "Y");
        double[] wakeNx = GetWakeArray(wakeGeometry, "NormalX");
        double[] wakeNy = GetWakeArray(wakeGeometry, "NormalY");
        double[] wakePanelAngle = GetWakeArray(wakeGeometry, "PanelAngle");
        var comparison = SolveFirstWakeColumnOnUnfactoredMatrix(
            referencePanel,
            wakeGeometry,
            maxNodes,
            freestreamSpeed);

        int rowCount = Math.Min(referencePanel.NodeCount + 1, Math.Min(referenceRhs.Count, referenceSolution.Count));
        double maxRhsDelta = 0.0;
        int maxRhsRow = -1;
        double csharpRhsAtMaxRow = 0.0;
        double referenceRhsAtMaxRow = 0.0;
        double maxSolutionDelta = 0.0;
        for (int row = 0; row < rowCount; row++)
        {
            double rhsDelta = Math.Abs(comparison.rhs[row] - referenceRhs[row]);
            if (rhsDelta > maxRhsDelta)
            {
                maxRhsDelta = rhsDelta;
                maxRhsRow = row;
                csharpRhsAtMaxRow = comparison.rhs[row];
                referenceRhsAtMaxRow = referenceRhs[row];
            }

            maxSolutionDelta = Math.Max(maxSolutionDelta, Math.Abs(comparison.denseSolution[row] - referenceSolution[row]));
        }

        (double maxX, double maxY, double maxNx, double maxNy, double maxPanelAngle)? wakeGeometryDelta = null;
        if (referenceWakeNodes.Count > 0)
        {
            double maxX = 0.0;
            double maxY = 0.0;
            double maxNx = 0.0;
            double maxNy = 0.0;
            double maxPanelAngle = 0.0;
            int count = Math.Min(referenceWakeNodes.Count, wakeX.Length);
            for (int i = 0; i < count; i++)
            {
                maxX = Math.Max(maxX, Math.Abs(wakeX[i] - referenceWakeNodes[i].x));
                maxY = Math.Max(maxY, Math.Abs(wakeY[i] - referenceWakeNodes[i].y));
                maxNx = Math.Max(maxNx, Math.Abs(wakeNx[i] - referenceWakeNodes[i].nx));
                maxNy = Math.Max(maxNy, Math.Abs(wakeNy[i] - referenceWakeNodes[i].ny));
                if (i < wakePanelAngle.Length)
                {
                    maxPanelAngle = Math.Max(maxPanelAngle, Math.Abs(wakePanelAngle[i] - referenceWakeNodes[i].panelAngle));
                }
            }

            wakeGeometryDelta = (maxX, maxY, maxNx, maxNy, maxPanelAngle);
        }

        int probeRow = referencePanel.NodeCount / 2 - 1;
        return (
            probeRow,
            comparison.denseSolution[probeRow],
            referenceSolution[probeRow],
            maxRhsDelta,
            maxRhsRow,
            csharpRhsAtMaxRow,
            referenceRhsAtMaxRow,
            maxSolutionDelta,
            wakeGeometryDelta);
    }

    private static ((double delta, int column, double csharp, double reference) row1,
        (double delta, int column, double csharp, double reference) rowMid,
        (double delta, int column, double csharp, double reference) rowTe,
        (double delta, int column, double csharp, double reference) rowKutta,
        double gamuDelta)? TryCompareReferenceAijAndGamu(
        int maxNodes,
        double freestreamSpeed)
    {
        string dumpPath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_dump.txt");
        if (!File.Exists(dumpPath))
        {
            return null;
        }

        var panelNodes = new List<(double x, double y)>();
        var aijRows = new Dictionary<int, List<double>>();
        var gamuRows = new List<(double u1, double u2)>();
        var panelRegex = new Regex(
            @"^PANEL_NODE I=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var aijRegex = new Regex(
            @"^AIJ_ROW I=\s*(\d+)\s+J=\s*(\d+)\s+VAL=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var gamuRegex = new Regex(
            @"^GAMU_ROW I=\s*(\d+)\s+U1=\s*([+-]?\d\.\d+E[+-]\d+)\s+U2=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match panelMatch = panelRegex.Match(line);
            if (panelMatch.Success)
            {
                panelNodes.Add((
                    double.Parse(panelMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(panelMatch.Groups[3].Value, CultureInfo.InvariantCulture)));
                continue;
            }

            Match aijMatch = aijRegex.Match(line);
            if (aijMatch.Success)
            {
                int row = int.Parse(aijMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                double value = double.Parse(aijMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                if (!aijRows.TryGetValue(row, out List<double>? values))
                {
                    values = new List<double>();
                    aijRows[row] = values;
                }

                values.Add(value);
                continue;
            }

            Match gamuMatch = gamuRegex.Match(line);
            if (gamuMatch.Success)
            {
                gamuRows.Add((
                    double.Parse(gamuMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(gamuMatch.Groups[3].Value, CultureInfo.InvariantCulture)));
            }
        }

        if (panelNodes.Count == 0 || aijRows.Count == 0 || gamuRows.Count == 0)
        {
            return null;
        }

        var panel = BuildPanelFromNodes(panelNodes);
        var assembledState = new InviscidSolverState(maxNodes);
        assembledState.InitializeForNodeCount(panel.NodeCount);
        ConfigureLegacyParityState(assembledState);
        int systemSize = InvokeAssembleSystem(panel, assembledState, freestreamSpeed);
        int n = panel.NodeCount;
        int midRow = n / 2;

        var row1Delta = CompareReferenceRow(aijRows, 1, assembledState.StreamfunctionInfluence, systemSize);
        var rowMidDelta = CompareReferenceRow(aijRows, midRow, assembledState.StreamfunctionInfluence, systemSize);
        var rowTeDelta = CompareReferenceRow(aijRows, n, assembledState.StreamfunctionInfluence, systemSize);
        var rowKuttaDelta = CompareReferenceRow(aijRows, n + 1, assembledState.StreamfunctionInfluence, systemSize);

        var solvedState = new InviscidSolverState(maxNodes);
        solvedState.InitializeForNodeCount(panel.NodeCount);
        ConfigureLegacyParityState(solvedState);
        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians: 0.0,
            panel,
            solvedState,
            freestreamSpeed,
            machNumber: 0.0);

        double gamuDelta = 0.0;
        int gamuCount = Math.Min(systemSize, gamuRows.Count);
        for (int row = 0; row < gamuCount; row++)
        {
            gamuDelta = Math.Max(gamuDelta, Math.Abs(solvedState.BasisVortexStrength[row, 0] - gamuRows[row].u1));
            gamuDelta = Math.Max(gamuDelta, Math.Abs(solvedState.BasisVortexStrength[row, 1] - gamuRows[row].u2));
        }

        return (row1Delta, rowMidDelta, rowTeDelta, rowKuttaDelta, gamuDelta);
    }

    private static (double refRefProbe, double refWithCSharpRhsProbe, double csharpWithRefRhsProbe, double referenceProbe, double maxRefRefDelta, double maxRefWithCSharpRhsDelta, double maxCSharpWithRefRhsDelta)? TryCrossSolveReferenceWakeColumn(
        int maxNodes,
        double freestreamSpeed)
    {
        string dumpPath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_dump.txt");
        if (!File.Exists(dumpPath))
        {
            return null;
        }

        var panelNodes = new List<(double x, double y)>();
        var aijEntries = new Dictionary<(int row, int col), double>();
        var referenceRhs = new List<double>();
        var referenceSolution = new List<double>();
        var panelRegex = new Regex(
            @"^PANEL_NODE I=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var aijRegex = new Regex(
            @"^AIJ_ROW I=\s*(\d+)\s+J=\s*(\d+)\s+VAL=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var rhsRegex = new Regex(
            @"^WAKE_RHS_COL I=\s*(\d+)\s+BIJ=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var solRegex = new Regex(
            @"^WAKE_SOL_COL I=\s*(\d+)\s+BIJ=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match panelMatch = panelRegex.Match(line);
            if (panelMatch.Success)
            {
                panelNodes.Add((
                    double.Parse(panelMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(panelMatch.Groups[3].Value, CultureInfo.InvariantCulture)));
                continue;
            }

            Match aijMatch = aijRegex.Match(line);
            if (aijMatch.Success)
            {
                int row = int.Parse(aijMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                int col = int.Parse(aijMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                double value = double.Parse(aijMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                aijEntries[(row, col)] = value;
                continue;
            }

            Match rhsMatch = rhsRegex.Match(line);
            if (rhsMatch.Success)
            {
                referenceRhs.Add(double.Parse(rhsMatch.Groups[2].Value, CultureInfo.InvariantCulture));
                continue;
            }

            Match solMatch = solRegex.Match(line);
            if (solMatch.Success)
            {
                referenceSolution.Add(double.Parse(solMatch.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }

        if (panelNodes.Count == 0 || aijEntries.Count == 0 || referenceRhs.Count == 0 || referenceSolution.Count == 0)
        {
            return null;
        }

        int systemSize = aijEntries.Keys.Max(entry => entry.row);
        var referenceMatrix = new double[systemSize, systemSize];
        foreach (KeyValuePair<(int row, int col), double> entry in aijEntries)
        {
            referenceMatrix[entry.Key.row - 1, entry.Key.col - 1] = entry.Value;
        }

        double[] referenceRhsVector = referenceRhs.Take(systemSize).ToArray();
        double[] referenceSolutionVector = referenceSolution.Take(systemSize).ToArray();
        if (referenceRhsVector.Length != systemSize || referenceSolutionVector.Length != systemSize)
        {
            return null;
        }

        var referencePanel = BuildPanelFromNodes(panelNodes);
        var analyticalState = new InviscidSolverState(maxNodes);
        analyticalState.InitializeForNodeCount(referencePanel.NodeCount);
        ConfigureLegacyParityState(analyticalState);
        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians: 0.0,
            referencePanel,
            analyticalState,
            freestreamSpeed,
            machNumber: 0.0);

        int nWake = Math.Max((referencePanel.NodeCount / 8) + 2, 3);
        object wakeGeometry = InvokeBuildWakeGeometry(
            referencePanel,
            analyticalState,
            nWake,
            freestreamSpeed,
            angleOfAttackRadians: 0.0);

        var assembledState = new InviscidSolverState(maxNodes);
        assembledState.InitializeForNodeCount(referencePanel.NodeCount);
        ConfigureLegacyParityState(assembledState);
        InvokeAssembleSystem(referencePanel, assembledState, freestreamSpeed);
        double[,] csharpMatrix = CloneMatrix(assembledState.StreamfunctionInfluence, systemSize);
        double[] csharpRhs = BuildWakeColumnRightHandSide(referencePanel, assembledState, wakeGeometry, wakeColumnIndex: 0, useLegacyPrecision: true);

        double[] refRef = SolveWithScaledPivotLu(referenceMatrix, referenceRhsVector, systemSize);
        double[] refWithCSharpRhs = SolveWithScaledPivotLu(referenceMatrix, csharpRhs, systemSize);
        double[] csharpWithRefRhs = SolveWithScaledPivotLu(csharpMatrix, referenceRhsVector, systemSize);

        double maxRefRefDelta = 0.0;
        double maxRefWithCSharpRhsDelta = 0.0;
        double maxCSharpWithRefRhsDelta = 0.0;
        for (int row = 0; row < systemSize; row++)
        {
            maxRefRefDelta = Math.Max(maxRefRefDelta, Math.Abs(refRef[row] - referenceSolutionVector[row]));
            maxRefWithCSharpRhsDelta = Math.Max(maxRefWithCSharpRhsDelta, Math.Abs(refWithCSharpRhs[row] - referenceSolutionVector[row]));
            maxCSharpWithRefRhsDelta = Math.Max(maxCSharpWithRefRhsDelta, Math.Abs(csharpWithRefRhs[row] - referenceSolutionVector[row]));
        }

        int probeRow = referencePanel.NodeCount / 2 - 1;
        return (
            refRef[probeRow],
            refWithCSharpRhs[probeRow],
            csharpWithRefRhs[probeRow],
            referenceSolutionVector[probeRow],
            maxRefRefDelta,
            maxRefWithCSharpRhsDelta,
            maxCSharpWithRefRhsDelta);
    }

    private static (int probeRow, double managedProbe, double referenceProbe, double maxDelta, int maxDeltaRow, double managedAtMaxRow, double referenceAtMaxRow)? TryCompareReferenceWakeKernel(
        int maxNodes,
        double freestreamSpeed)
    {
        string dumpPath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_dump.txt");
        if (!File.Exists(dumpPath))
        {
            return null;
        }

        var panelNodes = new List<(double x, double y)>();
        var referenceWakeNodes = ReadReferenceWakeNodesFromDump(dumpPath);
        var referenceRhs = new List<double>();
        var panelRegex = new Regex(
            @"^PANEL_NODE I=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var rhsRegex = new Regex(
            @"^WAKE_RHS_COL I=\s*(\d+)\s+BIJ=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match panelMatch = panelRegex.Match(line);
            if (panelMatch.Success)
            {
                panelNodes.Add((
                    double.Parse(panelMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(panelMatch.Groups[3].Value, CultureInfo.InvariantCulture)));
                continue;
            }

            Match rhsMatch = rhsRegex.Match(line);
            if (rhsMatch.Success)
            {
                referenceRhs.Add(double.Parse(rhsMatch.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }

        if (panelNodes.Count == 0 || referenceWakeNodes.Count == 0 || referenceRhs.Count == 0)
        {
            return null;
        }

        var panel = BuildPanelFromNodes(panelNodes);
        var state = new InviscidSolverState(maxNodes);
        state.InitializeForNodeCount(panel.NodeCount);
        ConfigureLegacyParityState(state);
        InvokeAssembleSystem(panel, state, freestreamSpeed);

        int nWake = Math.Max((panel.NodeCount / 8) + 2, 3);
        object wakeGeometry = BuildWakeGeometryFromReference(referenceWakeNodes, nWake);
        double[] managedRhs = BuildWakeColumnRightHandSide(panel, state, wakeGeometry, wakeColumnIndex: 0, useLegacyPrecision: true);

        int rowCount = Math.Min(panel.NodeCount + 1, referenceRhs.Count);
        double maxDelta = 0.0;
        int maxDeltaRow = -1;
        double managedAtMaxRow = 0.0;
        double referenceAtMaxRow = 0.0;
        for (int row = 0; row < rowCount; row++)
        {
            double delta = Math.Abs(managedRhs[row] - referenceRhs[row]);
            if (delta > maxDelta)
            {
                maxDelta = delta;
                maxDeltaRow = row;
                managedAtMaxRow = managedRhs[row];
                referenceAtMaxRow = referenceRhs[row];
            }
        }

        int probeRow = panel.NodeCount / 2 - 1;
        return (
            probeRow,
            managedRhs[probeRow],
            referenceRhs[probeRow],
            maxDelta,
            maxDeltaRow,
            managedAtMaxRow,
            referenceAtMaxRow);
    }

    private static (int probeRow, double managedProbe, double referenceProbe, double maxDelta, int maxDeltaRow, double managedAtMaxRow, double referenceAtMaxRow)? TryCompareReferenceWakeKernelWithReferenceFieldGeometry()
    {
        string dumpPath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_dump.txt");
        if (!File.Exists(dumpPath))
        {
            return null;
        }

        var referencePanelNodes = new Dictionary<int, (double x, double y, double nx, double ny)>();
        var referenceWakeNodes = ReadReferenceWakeNodesFromDump(dumpPath);
        var referenceRhs = new List<double>();
        var panelRegex = new Regex(
            @"^PANEL_NODE I=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)\s+NX=\s*([+-]?\d\.\d+E[+-]\d+)\s+NY=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var rhsRegex = new Regex(
            @"^WAKE_RHS_COL I=\s*(\d+)\s+BIJ=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match panelMatch = panelRegex.Match(line);
            if (panelMatch.Success)
            {
                int index = int.Parse(panelMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                referencePanelNodes[index] = (
                    double.Parse(panelMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(panelMatch.Groups[3].Value, CultureInfo.InvariantCulture),
                    double.Parse(panelMatch.Groups[4].Value, CultureInfo.InvariantCulture),
                    double.Parse(panelMatch.Groups[5].Value, CultureInfo.InvariantCulture));
                continue;
            }

            Match rhsMatch = rhsRegex.Match(line);
            if (rhsMatch.Success)
            {
                referenceRhs.Add(double.Parse(rhsMatch.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }

        if (referencePanelNodes.Count == 0 || referenceWakeNodes.Count == 0 || referenceRhs.Count == 0)
        {
            return null;
        }

        object wakeGeometry = BuildWakeGeometryFromReference(referenceWakeNodes, referenceWakeNodes.Count);
        int rowCount = Math.Min(referencePanelNodes.Count + 1, referenceRhs.Count);
        var managedRhs = new double[rowCount];
        for (int row = 0; row < rowCount - 1; row++)
        {
            var field = referencePanelNodes[row + 1];
            var wakeSurfaceSensitivity = InvokeComputeWakeSourceSensitivitiesAt(
                wakeGeometry,
                fieldNodeIndex: row + 2,
                field.x,
                field.y,
                field.nx,
                field.ny,
                fieldWakeIndex: -1,
                useLegacyPrecision: true);

            managedRhs[row] = -wakeSurfaceSensitivity.dzdm[0];
        }

        managedRhs[rowCount - 1] = 0.0;

        double maxDelta = 0.0;
        int maxDeltaRow = -1;
        double managedAtMaxRow = 0.0;
        double referenceAtMaxRow = 0.0;
        for (int row = 0; row < rowCount; row++)
        {
            double delta = Math.Abs(managedRhs[row] - referenceRhs[row]);
            if (delta > maxDelta)
            {
                maxDelta = delta;
                maxDeltaRow = row;
                managedAtMaxRow = managedRhs[row];
                referenceAtMaxRow = referenceRhs[row];
            }
        }

        int probeRow = (referencePanelNodes.Count / 2) - 1;
        return (
            probeRow,
            managedRhs[probeRow],
            referenceRhs[probeRow],
            maxDelta,
            maxDeltaRow,
            managedAtMaxRow,
            referenceAtMaxRow);
    }

    private static (
        double nx79Delta,
        double ny79Delta,
        double apan79Delta,
        double nx80Delta,
        double ny80Delta,
        double apan80Delta,
        double nx81Delta,
        double ny81Delta,
        double apan81Delta)? TryCompareReferencePanelGeometry(
        int maxNodes,
        double freestreamSpeed)
    {
        string dumpPath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_dump.txt");
        if (!File.Exists(dumpPath))
        {
            return null;
        }

        var panelNodes = new List<(double x, double y)>();
        var referenceGeometry = new Dictionary<int, (double nx, double ny, double apan)>();
        var panelRegex = new Regex(
            @"^PANEL_NODE I=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)\s+NX=\s*([+-]?\d\.\d+E[+-]\d+)\s+NY=\s*([+-]?\d\.\d+E[+-]\d+)\s+APAN=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match panelMatch = panelRegex.Match(line);
            if (!panelMatch.Success)
            {
                continue;
            }

            int index = int.Parse(panelMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            panelNodes.Add((
                double.Parse(panelMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(panelMatch.Groups[3].Value, CultureInfo.InvariantCulture)));
            referenceGeometry[index] = (
                double.Parse(panelMatch.Groups[4].Value, CultureInfo.InvariantCulture),
                double.Parse(panelMatch.Groups[5].Value, CultureInfo.InvariantCulture),
                double.Parse(panelMatch.Groups[6].Value, CultureInfo.InvariantCulture));
        }

        if (panelNodes.Count == 0)
        {
            return null;
        }

        var panel = BuildPanelFromNodes(panelNodes);
        var state = new InviscidSolverState(maxNodes);
        state.InitializeForNodeCount(panel.NodeCount);
        ConfigureLegacyParityState(state);
        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians: 0.0,
            panel,
            state,
            freestreamSpeed,
            machNumber: 0.0);

        static double Delta(
            IReadOnlyDictionary<int, (double nx, double ny, double apan)> reference,
            LinearVortexPanelState panel,
            int oneBasedIndex,
            Func<(double nx, double ny, double apan), double> selector,
            Func<LinearVortexPanelState, int, double> managedSelector)
        {
            return !reference.TryGetValue(oneBasedIndex, out var referenceValue)
                ? double.NaN
                : managedSelector(panel, oneBasedIndex - 1) - selector(referenceValue);
        }

        return (
            Delta(referenceGeometry, panel, 79, value => value.nx, (managed, index) => managed.NormalX[index]),
            Delta(referenceGeometry, panel, 79, value => value.ny, (managed, index) => managed.NormalY[index]),
            Delta(referenceGeometry, panel, 79, value => value.apan, (managed, index) => managed.PanelAngle[index]),
            Delta(referenceGeometry, panel, 80, value => value.nx, (managed, index) => managed.NormalX[index]),
            Delta(referenceGeometry, panel, 80, value => value.ny, (managed, index) => managed.NormalY[index]),
            Delta(referenceGeometry, panel, 80, value => value.apan, (managed, index) => managed.PanelAngle[index]),
            Delta(referenceGeometry, panel, 81, value => value.nx, (managed, index) => managed.NormalX[index]),
            Delta(referenceGeometry, panel, 81, value => value.ny, (managed, index) => managed.NormalY[index]),
            Delta(referenceGeometry, panel, 81, value => value.apan, (managed, index) => managed.PanelAngle[index]));
    }

    private static (
        double u1_79Delta,
        double u1_80Delta,
        double u1_81Delta,
        double u1_82Delta,
        double u1_83Delta)? TryCompareReferenceGamuWindow(
        int maxNodes,
        double freestreamSpeed)
    {
        string dumpPath = Path.Combine(FindRepoRoot(), "tools", "fortran-debug", "reference_dump.txt");
        if (!File.Exists(dumpPath))
        {
            return null;
        }

        var panelNodes = new List<(double x, double y)>();
        var referenceGamu = new Dictionary<int, (double u1, double u2)>();
        var panelRegex = new Regex(
            @"^PANEL_NODE I=\s*(\d+)\s+X=\s*([+-]?\d\.\d+E[+-]\d+)\s+Y=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);
        var gamuRegex = new Regex(
            @"^GAMU_ROW I=\s*(\d+)\s+U1=\s*([+-]?\d\.\d+E[+-]\d+)\s+U2=\s*([+-]?\d\.\d+E[+-]\d+)",
            RegexOptions.Compiled);

        foreach (string line in File.ReadLines(dumpPath))
        {
            Match panelMatch = panelRegex.Match(line);
            if (panelMatch.Success)
            {
                panelNodes.Add((
                    double.Parse(panelMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(panelMatch.Groups[3].Value, CultureInfo.InvariantCulture)));
                continue;
            }

            Match gamuMatch = gamuRegex.Match(line);
            if (gamuMatch.Success)
            {
                int index = int.Parse(gamuMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                referenceGamu[index] = (
                    double.Parse(gamuMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(gamuMatch.Groups[3].Value, CultureInfo.InvariantCulture));
            }
        }

        if (panelNodes.Count == 0 || referenceGamu.Count == 0)
        {
            return null;
        }

        var panel = BuildPanelFromNodes(panelNodes);
        var state = new InviscidSolverState(maxNodes);
        state.InitializeForNodeCount(panel.NodeCount);
        ConfigureLegacyParityState(state);
        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians: 0.0,
            panel,
            state,
            freestreamSpeed,
            machNumber: 0.0);

        double Delta(int oneBasedIndex)
        {
            return !referenceGamu.TryGetValue(oneBasedIndex, out var referenceValue)
                ? double.NaN
                : state.BasisVortexStrength[oneBasedIndex - 1, 0] - referenceValue.u1;
        }

        return (
            Delta(79),
            Delta(80),
            Delta(81),
            Delta(82),
            Delta(83));
    }

    private static double[] SolveWithScaledPivotLu(double[,] matrix, double[] rhs, int systemSize)
    {
        double[,] luMatrix = CloneMatrix(matrix, systemSize);
        double[] solution = (double[])rhs.Clone();
        int[] pivots = new int[systemSize];
        ScaledPivotLuSolver.Decompose(luMatrix, pivots, systemSize);
        ScaledPivotLuSolver.BackSubstitute(luMatrix, pivots, solution, systemSize);
        return solution;
    }

    private static double[,] BuildHybridDij(double[,] analytical, double[,] numerical, int n, int nWake)
    {
        int totalSize = n + nWake;
        var hybrid = new double[totalSize, totalSize];

        for (int row = 0; row < totalSize; row++)
        {
            for (int col = 0; col < totalSize; col++)
            {
                bool airfoilBlock = row < n && col < n;
                hybrid[row, col] = airfoilBlock ? analytical[row, col] : numerical[row, col];
            }
        }

        return hybrid;
    }

    private static BoundaryLayerSystemState Clone(BoundaryLayerSystemState source)
    {
        var clone = new BoundaryLayerSystemState(source.MaxStations, source.MaxWakeStations);

        Array.Copy(source.IPAN, clone.IPAN, source.IPAN.Length);
        Array.Copy(source.VTI, clone.VTI, source.VTI.Length);
        Array.Copy(source.UEDG, clone.UEDG, source.UEDG.Length);
        Array.Copy(source.THET, clone.THET, source.THET.Length);
        Array.Copy(source.DSTR, clone.DSTR, source.DSTR.Length);
        Array.Copy(source.CTAU, clone.CTAU, source.CTAU.Length);
        Array.Copy(source.MASS, clone.MASS, source.MASS.Length);
        Array.Copy(source.XSSI, clone.XSSI, source.XSSI.Length);
        Array.Copy(source.ITRAN, clone.ITRAN, source.ITRAN.Length);
        Array.Copy(source.IBLTE, clone.IBLTE, source.IBLTE.Length);
        Array.Copy(source.NBL, clone.NBL, source.NBL.Length);
        Array.Copy(source.TINDEX, clone.TINDEX, source.TINDEX.Length);
        clone.Converged = source.Converged;
        clone.RmsResidual = source.RmsResidual;
        clone.MaxResidualLocation = source.MaxResidualLocation;
        clone.Iteration = source.Iteration;

        return clone;
    }

    private static double InvokeMarchBoundaryLayer(
        BoundaryLayerSystemState blState,
        AnalysisSettings settings,
        double reinf)
    {
        var method = typeof(ViscousSolverEngine).GetMethod(
            "MarchBoundaryLayer",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MarchBoundaryLayer not found.");

        return (double)method.Invoke(null, new object[] { blState, settings, reinf })!;
    }

    private static (double uinv, double air, double wake, double usav) ComputeUsavSplit(
        BoundaryLayerSystemState blState,
        double[,] dij,
        double[,] ueInv,
        int side,
        int ibl)
    {
        double predicted = ueInv[ibl, side];
        double airfoilContribution = 0.0;
        double wakeContribution = 0.0;
        int iPan = GetPanelIndex(blState, ibl, side);
        double vtiI = blState.VTI[ibl, side];

        for (int jSide = 0; jSide < 2; jSide++)
        {
            for (int jbl = 1; jbl < blState.NBL[jSide]; jbl++)
            {
                int jPan = GetPanelIndex(blState, jbl, jSide);
                if (iPan < 0 || iPan >= dij.GetLength(0) || jPan < 0 || jPan >= dij.GetLength(1))
                {
                    continue;
                }

                double vtiJ = blState.VTI[jbl, jSide];
                double contribution = -vtiI * vtiJ * dij[iPan, jPan] * blState.MASS[jbl, jSide];
                predicted += contribution;

                if (jbl > blState.IBLTE[jSide])
                {
                    wakeContribution += contribution;
                }
                else
                {
                    airfoilContribution += contribution;
                }
            }
        }

        return (ueInv[ibl, side], airfoilContribution, wakeContribution, predicted);
    }

    private static (int row, int col, double analytical, double numerical, double error)
        FindWorstAirfoilBlockDifference(double[,] analytical, double[,] numerical, int n)
    {
        int bestRow = -1;
        int bestCol = -1;
        double bestAnalytical = 0.0;
        double bestNumerical = 0.0;
        double bestError = double.NegativeInfinity;

        for (int row = 0; row < n; row++)
        {
            for (int col = 0; col < n; col++)
            {
                double error = Math.Abs(analytical[row, col] - numerical[row, col]);
                if (error > bestError)
                {
                    bestError = error;
                    bestRow = row;
                    bestCol = col;
                    bestAnalytical = analytical[row, col];
                    bestNumerical = numerical[row, col];
                }
            }
        }

        return (bestRow, bestCol, bestAnalytical, bestNumerical, bestError);
    }

    private static int GetPanelIndex(BoundaryLayerSystemState blState, int ibl, int side)
    {
        return blState.IPAN[ibl, side];
    }

    private static int InvokeAssembleSystem(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        double freestreamSpeed)
    {
        var method = typeof(LinearVortexInviscidSolver).GetMethod(
            "AssembleSystem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("AssembleSystem not found.");

        return (int)method.Invoke(null, new object[] { panel, state, freestreamSpeed })!;
    }

    private static double[] BuildWakeColumnRightHandSide(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        object wakeGeometry,
        int wakeColumnIndex,
        bool useLegacyPrecision = false)
    {
        int n = panel.NodeCount;
        var rhs = new double[n + 1];
        for (int row = 0; row < n; row++)
        {
            var wakeSurfaceSensitivity = InvokeComputeWakeSourceSensitivitiesAt(
                wakeGeometry,
                fieldNodeIndex: row + 1,
                panel.X[row],
                panel.Y[row],
                panel.NormalX[row],
                panel.NormalY[row],
                fieldWakeIndex: -1,
                useLegacyPrecision: useLegacyPrecision);

            rhs[row] = -wakeSurfaceSensitivity.dzdm[wakeColumnIndex];
        }

        rhs[n] = 0.0;
        return rhs;
    }

    private static object BuildWakeGeometryFromReference(
        IReadOnlyList<(double x, double y, double nx, double ny, double panelAngle)> referenceWakeNodes,
        int nWake)
    {
        int count = Math.Min(referenceWakeNodes.Count, nWake);
        var x = new double[count];
        var y = new double[count];
        var nx = new double[count];
        var ny = new double[count];
        var panelAngle = new double[Math.Max(count - 1, 1)];

        for (int i = 0; i < count; i++)
        {
            var node = referenceWakeNodes[i];
            x[i] = node.x;
            y[i] = node.y;
            nx[i] = node.nx;
            ny[i] = node.ny;
            if (i < count - 1)
            {
                panelAngle[i] = node.panelAngle;
            }
        }

        if (count == 1)
        {
            panelAngle[0] = referenceWakeNodes[0].panelAngle;
        }

        Type wakeGeometryType = typeof(InfluenceMatrixBuilder).GetNestedType(
            "WakeGeometryData",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WakeGeometryData not found.");

        return Activator.CreateInstance(
            wakeGeometryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { x, y, nx, ny, panelAngle },
            culture: CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("WakeGeometryData construction failed.");
    }

    private static double[,] CloneMatrix(double[,] matrix, int size)
    {
        var clone = new double[size, size];
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                clone[row, col] = matrix[row, col];
            }
        }

        return clone;
    }

    private static LinearVortexPanelState BuildPanelFromNodes(IReadOnlyList<(double x, double y)> nodes)
    {
        var panel = new LinearVortexPanelState(nodes.Count);
        panel.Resize(nodes.Count);
        var x = new float[nodes.Count];
        var y = new float[nodes.Count];
        var s = new float[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            x[i] = (float)nodes[i].x;
            y[i] = (float)nodes[i].y;
            panel.X[i] = x[i];
            panel.Y[i] = y[i];
        }

        ParametricSpline.ComputeArcLength(x, y, s, nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            panel.ArcLength[i] = s[i];
        }

        int leadingEdgeIndex = 0;
        for (int i = 1; i < nodes.Count; i++)
        {
            if (panel.X[i] < panel.X[leadingEdgeIndex])
            {
                leadingEdgeIndex = i;
            }
        }

        panel.LeadingEdgeX = panel.X[leadingEdgeIndex];
        panel.LeadingEdgeY = panel.Y[leadingEdgeIndex];
        panel.LeadingEdgeArcLength = panel.ArcLength[leadingEdgeIndex];
        panel.TrailingEdgeX = 0.5 * (panel.X[0] + panel.X[nodes.Count - 1]);
        panel.TrailingEdgeY = 0.5 * (panel.Y[0] + panel.Y[nodes.Count - 1]);
        double chordDx = panel.TrailingEdgeX - panel.LeadingEdgeX;
        double chordDy = panel.TrailingEdgeY - panel.LeadingEdgeY;
        panel.Chord = Math.Sqrt((chordDx * chordDx) + (chordDy * chordDy));

        return panel;
    }

    private static (double delta, int column, double csharp, double reference) CompareReferenceRow(
        IReadOnlyDictionary<int, List<double>> referenceRows,
        int oneBasedRow,
        double[,] matrix,
        int systemSize)
    {
        if (!referenceRows.TryGetValue(oneBasedRow, out List<double>? row))
        {
            return (double.NaN, -1, double.NaN, double.NaN);
        }

        int zeroBasedRow = oneBasedRow - 1;
        double maxDelta = 0.0;
        int maxColumn = -1;
        double maxCsharp = 0.0;
        double maxReference = 0.0;
        int count = Math.Min(systemSize, row.Count);
        for (int col = 0; col < count; col++)
        {
            double csharpValue = matrix[zeroBasedRow, col];
            double referenceValue = row[col];
            double delta = Math.Abs(csharpValue - referenceValue);
            if (delta > maxDelta)
            {
                maxDelta = delta;
                maxColumn = col + 1;
                maxCsharp = csharpValue;
                maxReference = referenceValue;
            }
        }

        return (maxDelta, maxColumn, maxCsharp, maxReference);
    }

    private static string FindRepoRoot()
    {
        string[] startPoints =
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
        };

        foreach (string start in startPoints)
        {
            string? dir = Path.GetFullPath(start);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir, "src")) &&
                    Directory.Exists(Path.Combine(dir, "tests")) &&
                    Directory.Exists(Path.Combine(dir, "f_xfoil")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static string FormatSplit(string label, (double uinv, double air, double wake, double usav) split)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}: UINV={1:E8} AIR={2:E8} WAKE={3:E8} USAV={4:E8}",
            label,
            split.uinv,
            split.air,
            split.wake,
            split.usav);
    }

    private static (double[] x, double[] y) BuildNacaSymmetric0012(int n)
    {
        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic("0012", ClassicXFoilNacaPointCount);
        var x = new double[geometry.Points.Count];
        var y = new double[geometry.Points.Count];
        for (int i = 0; i < geometry.Points.Count; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        return (x, y);
    }
}
