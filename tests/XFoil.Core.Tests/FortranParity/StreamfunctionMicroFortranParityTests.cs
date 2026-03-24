using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: PSILIN
// Secondary legacy source: tools/fortran-debug/json_trace.f :: focused PSILIN trace hooks
// Role in port: Verifies the managed single-precision PSILIN replay against a standalone Fortran micro-driver on a few curated kernel cases.
// Differences: The micro-driver is new managed/Fortran harness infrastructure, but the compared arithmetic is the real legacy PSILIN kernel and the managed parity replay path.
// Decision: Keep the micro-driver because it localizes streamfunction/source-kernel mismatches before DIJ and viscous marching amplify them.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class StreamfunctionMicroFortranParityTests
{
    private const string Alpha10P80CaseId = "n0012_re1e6_a10_p80";
    private const int ClassicXFoilNacaPointCount = 239;
    private const float FirstWakePointFieldX = 1.000100016593933f;
    private const float FirstWakePointFieldY = -6.620715087279905e-11f;
    private static readonly string Alpha10P80Panel81ReferenceDirectory =
        Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            "alpha10_p80_psilin_panel81_ref3");

    private static readonly string[] PanelFields =
    {
        "panelXJo", "panelYJo", "panelXJp", "panelYJp", "panelDx", "panelDy", "dso", "dsio", "panelAngle",
        "rx1", "ry1", "rx2", "ry2", "sx", "sy", "x1", "x2", "yy", "rs1", "rs2", "sgn", "g1", "g2", "t1", "t2",
        "x1i", "x2i", "yyi"
    };

    private static readonly string[] HalfFields =
    {
        "x0", "psumTerm1", "psumTerm2", "psumTerm3", "psumAccum", "psum",
        "pdifTerm1", "pdifTerm2", "pdifTerm3", "pdifTerm4", "pdifAccum1", "pdifAccum2",
        "pdifNumerator", "pdif"
    };

    private static readonly string[] DzFields =
    {
        "dzJmTerm1", "dzJmTerm2", "dzJmInner", "dzJoTerm1", "dzJoTerm2", "dzJoInner",
        "dzJpTerm1", "dzJpTerm2", "dzJpInner", "dzJqTerm1", "dzJqTerm2", "dzJqInner"
    };

    private static readonly string[] DqFields =
    {
        "dqJmTerm1", "dqJmTerm2", "dqJmInner", "dqJoTerm1", "dqJoTerm2", "dqJoInner",
        "dqJpTerm1", "dqJpTerm2", "dqJpInner", "dqJqTerm1", "dqJqTerm2", "dqJqInner"
    };

    private static readonly string[] SegmentFields =
    {
        "x1", "x2", "yy", "panelAngle", "x1i", "x2i", "yyi",
        "rs0", "rs1", "rs2", "g0", "g1", "g2", "t0", "t1", "t2",
        "dso", "dsio", "dsm", "dsim", "dsp", "dsip", "dxInv",
        "sourceTermLeft", "sourceTermRight",
        "ssum", "sdif", "psum", "pdif",
        "psx0", "psx1", "psx2", "psyy",
        "pdx0Term1", "pdx0Term2", "pdx0Numerator", "pdx0",
        "pdx1Term1", "pdx1Term2", "pdx1Numerator", "pdx1",
        "pdx2Term1", "pdx2Term2", "pdx2Numerator", "pdx2",
        "pdyyTerm1", "pdyyTerm2", "pdyyNumerator", "pdyy",
        "psniTerm1", "psniTerm2", "psniTerm3", "psni",
        "pdniTerm1", "pdniTerm2", "pdniTerm3", "pdni",
        "dzJm", "dzJo", "dzJp", "dzJq",
        "dqJm", "dqJo", "dqJp", "dqJq"
    };

    private static readonly string[] VortexFields =
    {
        "x1", "x2", "yy", "rs1", "rs2", "g1", "g2", "t1", "t2", "dxInv",
        "psisTerm1", "psisTerm2", "psisTerm3", "psisTerm4", "psis",
        "psidTerm1", "psidTerm2", "psidTerm3", "psidTerm4", "psidTerm5", "psidHalfTerm", "psid",
        "psx1", "psx2", "psyy",
        "pdxSum", "pdx1Mul", "pdx1PanelTerm", "pdx1Accum1", "pdx1Accum2", "pdx1Numerator", "pdx1",
        "pdx2Mul", "pdx2PanelTerm", "pdx2Accum1", "pdx2Accum2", "pdx2Numerator", "pdx2", "pdyy",
        "gammaJo", "gammaJp", "gsum", "gdif", "psni", "pdni", "psiDelta", "psiNiDelta", "dzJo", "dzJp", "dqJo", "dqJp"
    };

    private static readonly string[] TeFields =
    {
        "psig", "pgam", "psigni", "pgamni", "sigte", "gamte", "scs", "sds",
        "dzJoTeSig", "dzJpTeSig", "dzJoTeGam", "dzJpTeGam",
        "dqJoTeSigHalf", "dqJoTeSigTerm", "dqJoTeGamHalf", "dqJoTeGamTerm",
        "dqTeInner", "dqJoTe", "dqJpTe"
    };

    private static readonly string[] AccumFields =
    {
        "psiBefore", "psiNormalBefore", "psi", "psiNormalDerivative"
    };

    private static readonly string[] ResultTermFields =
    {
        "psiBeforeFreestream", "psiNormalBeforeFreestream", "psiFreestreamDelta", "psiNormalFreestreamDelta"
    };

    private static readonly string[] ResultFields =
    {
        "psi", "psiNormalDerivative"
    };

    [Fact]
    public void CuratedPsilinCases_BitwiseMatchFortranDriver()
    {
        int caseIndex = 0;
        foreach (FortranPsilinCase @case in BuildCases())
        {
            FortranPsilinResult fortran = FortranPsilinDriver.RunCase(@case);
            ManagedPsilinResult managed = RunManagedCase(@case);

            AssertPanelRecordsEqual(fortran.PanelStates, managed.PanelStates, "PANEL", PanelFields, caseIndex);
            AssertRecordsEqual(fortran.HalfTerms, managed.HalfTerms, "HALF", HalfFields, caseIndex);
            AssertRecordsEqual(fortran.Segments, managed.Segments, "SEG", SegmentFields, caseIndex);
            AssertRecordsEqual(fortran.DzTerms, managed.DzTerms, "DZ", DzFields, caseIndex);
            AssertRecordsEqual(fortran.DqTerms, managed.DqTerms, "DQ", DqFields, caseIndex);
            AssertPairRecordsEqual(fortran.VortexSegments, managed.VortexSegments, "VOR", VortexFields, caseIndex);
            AssertPairRecordsEqual(fortran.TeCorrections, managed.TeCorrections, "TE", TeFields, caseIndex);
            AssertAccumRecordsEqual(fortran.AccumStates, managed.AccumStates, AccumFields, caseIndex);
            AssertResultRecordsEqual(fortran.ResultTerms, managed.ResultTerms, "RTERM", ResultTermFields, caseIndex);
            AssertResultRecordsEqual(fortran.Results, managed.Results, "RESULT", ResultFields, caseIndex);
            Assert.Equal(fortran.FinalBits, managed.FinalBits);
            caseIndex++;
        }
    }

    [Fact]
    public void Alpha10_P80_FirstWakePoint_BitwiseMatchesFortranDriver()
    {
        FortranPsilinCase @case = BuildAlpha10P80FirstWakePointCase(fieldNormalX: 0.0f, fieldNormalY: 1.0f);
        FortranPsilinResult fortran = FortranPsilinDriver.RunCase(@case);
        ManagedPsilinResult managed = RunManagedCase(@case);

        AssertPanelRecordsEqual(fortran.PanelStates, managed.PanelStates, "PANEL", PanelFields, caseIndex: -1);
        AssertRecordsEqual(fortran.HalfTerms, managed.HalfTerms, "HALF", HalfFields, caseIndex: -1);
        AssertRecordsEqual(fortran.Segments, managed.Segments, "SEG", SegmentFields, caseIndex: -1);
        AssertRecordsEqual(fortran.DzTerms, managed.DzTerms, "DZ", DzFields, caseIndex: -1);
        AssertRecordsEqual(fortran.DqTerms, managed.DqTerms, "DQ", DqFields, caseIndex: -1);
        AssertPairRecordsEqual(fortran.VortexSegments, managed.VortexSegments, "VOR", VortexFields, caseIndex: -1);
        AssertPairRecordsEqual(fortran.TeCorrections, managed.TeCorrections, "TE", TeFields, caseIndex: -1);
        AssertAccumRecordsEqual(fortran.AccumStates, managed.AccumStates, AccumFields, caseIndex: -1);
        AssertResultRecordsEqual(fortran.ResultTerms, managed.ResultTerms, "RTERM", ResultTermFields, caseIndex: -1);
        AssertResultRecordsEqual(fortran.Results, managed.Results, "RESULT", ResultFields, caseIndex: -1);
        Assert.Equal(fortran.FinalBits, managed.FinalBits);
    }

    [Fact]
    public void Alpha10_P80_FirstWakePoint_XNormal_BitwiseMatchesFortranDriver()
    {
        FortranPsilinCase @case = BuildAlpha10P80FirstWakePointCase(fieldNormalX: 1.0f, fieldNormalY: 0.0f);
        FortranPsilinResult fortran = FortranPsilinDriver.RunCase(@case);
        ManagedPsilinResult managed = RunManagedCase(@case);

        AssertPanelRecordsEqual(fortran.PanelStates, managed.PanelStates, "PANEL", PanelFields, caseIndex: -2);
        AssertRecordsEqual(fortran.HalfTerms, managed.HalfTerms, "HALF", HalfFields, caseIndex: -2);
        AssertRecordsEqual(fortran.Segments, managed.Segments, "SEG", SegmentFields, caseIndex: -2);
        AssertRecordsEqual(fortran.DzTerms, managed.DzTerms, "DZ", DzFields, caseIndex: -2);
        AssertRecordsEqual(fortran.DqTerms, managed.DqTerms, "DQ", DqFields, caseIndex: -2);
        AssertPairRecordsEqual(fortran.VortexSegments, managed.VortexSegments, "VOR", VortexFields, caseIndex: -2);
        AssertPairRecordsEqual(fortran.TeCorrections, managed.TeCorrections, "TE", TeFields, caseIndex: -2);
        AssertAccumRecordsEqual(fortran.AccumStates, managed.AccumStates, AccumFields, caseIndex: -2);
        AssertResultRecordsEqual(fortran.ResultTerms, managed.ResultTerms, "RTERM", ResultTermFields, caseIndex: -2);
        AssertResultRecordsEqual(fortran.Results, managed.Results, "RESULT", ResultFields, caseIndex: -2);
        Assert.Equal(fortran.FinalBits, managed.FinalBits);
    }

    [Fact]
    public void Alpha10_P80_FirstWakePoint_XNormal_VortexTraceObserverAndFileCountsAgree()
    {
        using var envScope = TraceEnvironmentIsolation.Clear();

        FortranPsilinCase @case = BuildAlpha10P80FirstWakePointCase(fieldNormalX: 1.0f, fieldNormalY: 0.0f);
        ManagedTraceProbe probe = ProbeManagedTrace(@case);

        Assert.True(
            probe.ObserverCounts.TryGetValue("psilin_vortex_segment", out int observerCount),
            $"observerKinds={FormatCounts(probe.ObserverCounts)}");
        Assert.True(
            probe.FileCounts.TryGetValue("psilin_vortex_segment", out int fileCount),
            $"fileKinds={FormatCounts(probe.FileCounts)}");
        Assert.Equal(observerCount, fileCount);
        Assert.True(observerCount > 0, $"observerCount={observerCount} fileCount={fileCount}");
    }

    [Fact]
    public void CuratedPsilinCase1_SegmentInputs_BitwiseMatchFortranDriver()
    {
        FortranPsilinCase @case = BuildCases()[1];
        FortranPsilinResult fortran = FortranPsilinDriver.RunCase(@case);
        ManagedPsilinResult managed = RunManagedCase(@case);

        AssertPanelRecordsEqual(fortran.PanelStates, managed.PanelStates, "PANEL", PanelFields, caseIndex: -11);
        AssertRecordsEqual(fortran.Segments, managed.Segments, "SEG", SegmentFields, caseIndex: -11);
    }

    [Fact]
    public void Alpha10_P80_FirstWakePoint_XNormal_PanelTrace_MatchesAuthoritativeReference()
    {
        FortranPsilinCase @case = BuildAlpha10P80FirstWakePointCase(
            fieldNormalX: 1.0f,
            fieldNormalY: 0.0f);

        ManagedPsilinResult managed = RunManagedCase(@case);
        IReadOnlyList<PsilinPanelHexRecord> expected = ReadReferencePanelTraceBlock(blockIndex: 0);

        AssertPanelRecordsEqual(expected, managed.PanelStates, "PANEL", PanelFields, caseIndex: -101);
    }

    [Fact]
    public void Alpha10_P80_FirstWakePoint_YNormal_PanelTrace_MatchesAuthoritativeReference()
    {
        FortranPsilinCase @case = BuildAlpha10P80FirstWakePointCase(
            fieldNormalX: 0.0f,
            fieldNormalY: 1.0f);

        ManagedPsilinResult managed = RunManagedCase(@case);
        IReadOnlyList<PsilinPanelHexRecord> expected = ReadReferencePanelTraceBlock(blockIndex: 1);

        AssertPanelRecordsEqual(expected, managed.PanelStates, "PANEL", PanelFields, caseIndex: -102);
    }

    [Fact]
    public void Alpha10_P80_FirstWakePoint_SourceEnabledNegativeYNormal_PanelTrace_MatchesAuthoritativeReference()
    {
        FortranPsilinCase @case = BuildAlpha10P80FirstWakePointSourceCase();

        ManagedPsilinResult managed = RunManagedCase(@case);
        IReadOnlyList<PsilinPanelHexRecord> expected = ReadReferencePanelTraceBlock(blockIndex: 2);

        AssertPanelRecordsEqual(expected, managed.PanelStates, "PANEL", PanelFields, caseIndex: -103);
    }

    private static IReadOnlyList<FortranPsilinCase> BuildCases()
    {
        var cases = new List<FortranPsilinCase>();

        {
            var (panel, state) = CreateFlatPlate();
            state.VortexStrength[0] = 0.75;
            state.VortexStrength[1] = -0.25;
            state.VortexStrength[2] = 0.5;
            state.SourceStrength[0] = 0.1;
            state.SourceStrength[1] = -0.2;
            state.SourceStrength[2] = 0.3;
            cases.Add(CaptureCase(
                panel,
                state,
                fieldNodeIndex: 1,
                fieldX: (float)panel.X[1],
                fieldY: (float)panel.Y[1],
                fieldNormalX: (float)panel.NormalX[1],
                fieldNormalY: (float)panel.NormalY[1],
                includeSourceTerms: true,
                freestreamSpeed: 0.3f,
                angleOfAttackRadians: 0.1f));
        }

        {
            var (panel, state) = CreateDiamond();
            state.VortexStrength[0] = 0.4;
            state.VortexStrength[1] = -0.6;
            state.VortexStrength[2] = 0.3;
            state.VortexStrength[3] = 0.2;
            state.VortexStrength[4] = -0.1;
            state.SourceStrength[0] = 0.05;
            state.SourceStrength[1] = 0.1;
            state.SourceStrength[2] = -0.15;
            state.SourceStrength[3] = 0.2;
            state.SourceStrength[4] = -0.05;
            cases.Add(CaptureCase(
                panel,
                state,
                fieldNodeIndex: -1,
                fieldX: 0.15f,
                fieldY: 0.05f,
                fieldNormalX: 0.0f,
                fieldNormalY: 1.0f,
                includeSourceTerms: true,
                freestreamSpeed: 1.1f,
                angleOfAttackRadians: -0.2f));
        }

        {
            var (panel, state) = CreateFlatPlate();
            state.IsSharpTrailingEdge = false;
            state.TrailingEdgeGap = 0.08;
            state.TrailingEdgeAngleNormal = 0.03;
            state.TrailingEdgeAngleStreamwise = 0.07;
            state.VortexStrength[0] = 1.0;
            state.VortexStrength[1] = -0.5;
            state.VortexStrength[2] = 0.25;
            cases.Add(CaptureCase(
                panel,
                state,
                fieldNodeIndex: -1,
                fieldX: 0.75f,
                fieldY: 0.1f,
                fieldNormalX: 0.0f,
                fieldNormalY: 1.0f,
                includeSourceTerms: false,
                freestreamSpeed: 0.8f,
                angleOfAttackRadians: 0.25f));
        }

        cases.Add(BuildAlpha10P80FirstWakePointCase(fieldNormalX: 0.0f, fieldNormalY: 1.0f));
        cases.Add(BuildAlpha10P80FirstWakePointCase(fieldNormalX: 1.0f, fieldNormalY: 0.0f));

        return cases;
    }

    private static FortranPsilinCase BuildAlpha10P80FirstWakePointCase(
        float fieldNormalX,
        float fieldNormalY,
        bool includeSourceTerms = false)
    {
        BuildAlpha10P80State(out LinearVortexPanelState panel, out InviscidSolverState state, out double alphaRadians);

        return CaptureCase(
            panel,
            state,
            fieldNodeIndex: panel.NodeCount,
            fieldX: FirstWakePointFieldX,
            fieldY: FirstWakePointFieldY,
            fieldNormalX,
            fieldNormalY,
            includeSourceTerms,
            freestreamSpeed: 1.0f,
            angleOfAttackRadians: (float)alphaRadians);
    }

    private static FortranPsilinCase BuildAlpha10P80FirstWakePointSourceCase()
    {
        BuildAlpha10P80State(out LinearVortexPanelState panel, out InviscidSolverState state, out double alphaRadians);

        MethodInfo buildWakeGeometry = typeof(InfluenceMatrixBuilder).GetMethod(
            "BuildWakeGeometry",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildWakeGeometry method not found.");

        int nWake = Math.Max((panel.NodeCount / 8) + 2, 3);
        object wakeGeometry = buildWakeGeometry.Invoke(
            null,
            new object[] { panel, state, nWake, 1.0, alphaRadians })!;

        double[] wakeX = (double[])wakeGeometry.GetType().GetProperty("X")!.GetValue(wakeGeometry)!;
        double[] wakeY = (double[])wakeGeometry.GetType().GetProperty("Y")!.GetValue(wakeGeometry)!;
        double[] wakeNormalX = (double[])wakeGeometry.GetType().GetProperty("NormalX")!.GetValue(wakeGeometry)!;
        double[] wakeNormalY = (double[])wakeGeometry.GetType().GetProperty("NormalY")!.GetValue(wakeGeometry)!;

        return CaptureCase(
            panel,
            state,
            fieldNodeIndex: panel.NodeCount,
            fieldX: (float)wakeX[0],
            fieldY: (float)wakeY[0],
            fieldNormalX: (float)wakeNormalX[0],
            fieldNormalY: (float)wakeNormalY[0],
            includeSourceTerms: true,
            freestreamSpeed: 1.0f,
            angleOfAttackRadians: (float)alphaRadians);
    }

    private static void BuildAlpha10P80State(out LinearVortexPanelState panel, out InviscidSolverState state, out double alphaRadians)
    {
        FortranReferenceCase definition = FortranReferenceCases.Get(Alpha10P80CaseId);
        alphaRadians = definition.AlphaDegrees * Math.PI / 180.0;

        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic(definition.AirfoilCode, ClassicXFoilNacaPointCount);
        var x = new double[geometry.Points.Count];
        var y = new double[geometry.Points.Count];
        for (int i = 0; i < geometry.Points.Count; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        panel = new LinearVortexPanelState(definition.PanelCount + 40);
        CosineClusteringPanelDistributor.Distribute(
            x,
            y,
            x.Length,
            panel,
            desiredNodeCount: definition.PanelCount,
            useLegacyPrecision: true);

        state = new InviscidSolverState(panel.MaxNodes);
        state.InitializeForNodeCount(panel.NodeCount);
        state.UseLegacyKernelPrecision = true;
        state.UseLegacyPanelingPrecision = true;

        LinearVortexInviscidSolver.SolveAtAngleOfAttack(
            alphaRadians,
            panel,
            state,
            freestreamSpeed: 1.0,
            machNumber: 0.0);
    }

    private static ManagedPsilinResult RunManagedCase(FortranPsilinCase @case)
    {
        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-psilin-trace-{Guid.NewGuid():N}.jsonl");

        try
        {
            BuildManagedState(@case, out LinearVortexPanelState panel, out InviscidSolverState state);
            using var envScope = TraceEnvironmentIsolation.Clear();

            using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "psilin-micro" });
            using var traceScope = SolverTrace.Begin(traceWriter);

            (double psi, double psiNi) = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
                @case.FieldNodeIndex,
                @case.FieldX,
                @case.FieldY,
                @case.FieldNormalX,
                @case.FieldNormalY,
                computeGeometricSensitivities: false,
                includeSourceTerms: @case.IncludeSourceTerms,
                panel,
                state,
                @case.FreestreamSpeed,
                @case.AngleOfAttackRadians);

            IReadOnlyList<ParityTraceRecord> records = ParityTraceLoader.ReadAll(tracePath);

            return new ManagedPsilinResult(
                ReadPanelTraceRecords(records, "psilin_panel", PanelFields),
                ReadTraceRecords(records, "psilin_source_half_terms", HalfFields),
                ReadTraceRecords(records, "psilin_source_dz_terms", DzFields),
                ReadTraceRecords(records, "psilin_source_dq_terms", DqFields),
                ReadTraceRecords(records, "psilin_source_segment", SegmentFields),
                ReadPairTraceRecords(records, "psilin_vortex_segment", VortexFields),
                ReadPairTraceRecords(records, "psilin_te_correction", TeFields),
                ReadAccumTraceRecords(records),
                ReadResultTraceRecords(records, "psilin_result_terms", ResultTermFields),
                ReadResultTraceRecords(records, "psilin_result", ResultFields),
                BuildFinalBits(state, (float)psi, (float)psiNi));
        }
        finally
        {
            if (File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    private static ManagedTraceProbe ProbeManagedTrace(FortranPsilinCase @case)
    {
        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-psilin-probe-{Guid.NewGuid():N}.jsonl");
        var observerCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            BuildManagedState(@case, out LinearVortexPanelState panel, out InviscidSolverState state);
            using var envScope = TraceEnvironmentIsolation.Clear();

            using var traceWriter = new JsonlTraceWriter(
                tracePath,
                runtime: "csharp",
                session: new { caseName = "psilin-micro-probe" },
                serializedRecordObserver: json =>
                {
                    ParityTraceRecord? record = ParityTraceLoader.DeserializeLine(json);
                    if (record is null)
                    {
                        return;
                    }

                    observerCounts[record.Kind] = observerCounts.TryGetValue(record.Kind, out int count)
                        ? count + 1
                        : 1;
                });
            using var traceScope = SolverTrace.Begin(traceWriter);

            _ = StreamfunctionInfluenceCalculator.ComputeInfluenceAt(
                @case.FieldNodeIndex,
                @case.FieldX,
                @case.FieldY,
                @case.FieldNormalX,
                @case.FieldNormalY,
                computeGeometricSensitivities: false,
                includeSourceTerms: @case.IncludeSourceTerms,
                panel,
                state,
                @case.FreestreamSpeed,
                @case.AngleOfAttackRadians);

            IReadOnlyList<ParityTraceRecord> records = ParityTraceLoader.ReadAll(tracePath);
            var fileCounts = records
                .GroupBy(record => record.Kind, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            return new ManagedTraceProbe(observerCounts, fileCounts);
        }
        finally
        {
            if (File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    private static string FormatCounts(IReadOnlyDictionary<string, int> counts)
    {
        return string.Join(
            ",",
            counts.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => $"{kvp.Key}:{kvp.Value}"));
    }

    private static IReadOnlyList<PsilinHexRecord> ReadTraceRecords(IReadOnlyList<ParityTraceRecord> records, string kind, IReadOnlyList<string> fields)
    {
        return records
            .Where(record => record.Kind == kind)
            .Select(record => new PsilinHexRecord(
                kind,
                record.Data.GetProperty("panelIndex").GetInt32(),
                record.Data.GetProperty("half").GetInt32(),
                fields.Select(field => ReadTraceBits(record, field)).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<PsilinPanelHexRecord> ReadPanelTraceRecords(IReadOnlyList<ParityTraceRecord> records, string kind, IReadOnlyList<string> fields)
    {
        return records
            .Where(record => record.Kind == kind)
            .Select(record => new PsilinPanelHexRecord(
                kind,
                record.Data.GetProperty("panelIndex").GetInt32(),
                fields.Select(field => ReadTraceBits(record, field)).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<PsilinPanelHexRecord> ReadReferencePanelTraceBlock(int blockIndex)
    {
        IReadOnlyList<IReadOnlyList<PsilinPanelHexRecord>> blocks = ReadReferencePanelTraceBlocks();

        Assert.InRange(blockIndex, 0, blocks.Count - 1);
        return blocks[blockIndex];
    }

    private static FortranPsilinCase BuildAlpha10P80FirstWakePointCaseFromReferenceBlock(int blockIndex, bool includeSourceTerms)
    {
        IReadOnlyList<IReadOnlyList<ParityTraceRecord>> blocks = ReadReferencePanelTraceRecordBlocks();
        Assert.InRange(blockIndex, 0, blocks.Count - 1);
        ParityTraceRecord firstRecord = blocks[blockIndex][0];

        float sx = ReadTraceFloat(firstRecord, "sx");
        float sy = ReadTraceFloat(firstRecord, "sy");
        float x1i = ReadTraceFloat(firstRecord, "x1i");
        float yyi = ReadTraceFloat(firstRecord, "yyi");

        float fieldNormalX = MathF.FusedMultiplyAdd(sx, x1i, -(sy * yyi));
        float fieldNormalY = MathF.FusedMultiplyAdd(sy, x1i, sx * yyi);

        return BuildAlpha10P80FirstWakePointCase(fieldNormalX, fieldNormalY, includeSourceTerms);
    }

    private static IReadOnlyList<IReadOnlyList<PsilinPanelHexRecord>> SplitPanelTraceBlocks(IReadOnlyList<PsilinPanelHexRecord> records)
    {
        var blocks = new List<IReadOnlyList<PsilinPanelHexRecord>>();
        var current = new List<PsilinPanelHexRecord>();

        foreach (PsilinPanelHexRecord record in records)
        {
            if (current.Count > 0 && record.PanelIndex <= current[^1].PanelIndex)
            {
                blocks.Add(current.ToArray());
                current.Clear();
            }

            current.Add(record);
        }

        if (current.Count > 0)
        {
            blocks.Add(current.ToArray());
        }

        return blocks;
    }

    private static IReadOnlyList<IReadOnlyList<PsilinPanelHexRecord>> ReadReferencePanelTraceBlocks()
    {
        string tracePath = GetLatestVersionedTracePath(Alpha10P80Panel81ReferenceDirectory);
        IReadOnlyList<PsilinPanelHexRecord> records = ReadPanelTraceRecords(ParityTraceLoader.ReadAll(tracePath), "psilin_panel", PanelFields);
        return SplitPanelTraceBlocks(records);
    }

    private static IReadOnlyList<IReadOnlyList<ParityTraceRecord>> ReadReferencePanelTraceRecordBlocks()
    {
        string tracePath = GetLatestVersionedTracePath(Alpha10P80Panel81ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> records = ParityTraceLoader.ReadAll(tracePath)
            .Where(record => record.Kind == "psilin_panel")
            .ToArray();

        var blocks = new List<IReadOnlyList<ParityTraceRecord>>();
        var current = new List<ParityTraceRecord>();

        foreach (ParityTraceRecord record in records)
        {
            int panelIndex = record.Data.GetProperty("panelIndex").GetInt32();
            if (current.Count > 0 && panelIndex <= current[^1].Data.GetProperty("panelIndex").GetInt32())
            {
                blocks.Add(current.ToArray());
                current.Clear();
            }

            current.Add(record);
        }

        if (current.Count > 0)
        {
            blocks.Add(current.ToArray());
        }

        return blocks;
    }

    private static string GetLatestVersionedTracePath(string directory)
    {
        Assert.True(Directory.Exists(directory), $"Reference trace directory missing: {directory}");

        string? latest = Directory.EnumerateFiles(directory, "reference_trace.*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Counter = TryParseVersionedTraceCounter(Path.GetFileName(path))
            })
            .Where(entry => entry.Counter is not null)
            .OrderByDescending(entry => entry.Counter)
            .Select(entry => entry.Path)
            .FirstOrDefault();

        Assert.False(string.IsNullOrWhiteSpace(latest), $"No numbered reference trace found in {directory}");
        return latest!;
    }

    private static long? TryParseVersionedTraceCounter(string fileName)
    {
        const string prefix = "reference_trace.";
        const string suffix = ".jsonl";

        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        string middle = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        return long.TryParse(middle, NumberStyles.Integer, CultureInfo.InvariantCulture, out long counter)
            ? counter
            : null;
    }

    private static float ReadTraceFloat(ParityTraceRecord record, string fieldName)
    {
        if (record.TryGetDataBits(fieldName, out IReadOnlyDictionary<string, string>? bits) && bits is not null)
        {
            if (bits.TryGetValue("f32", out string? single))
            {
                int raw = int.Parse(single[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return BitConverter.Int32BitsToSingle(raw);
            }

            if (bits.TryGetValue("f64", out string? dbl))
            {
                ulong raw = ulong.Parse(dbl[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return (float)BitConverter.Int64BitsToDouble(unchecked((long)raw));
            }
        }

        return (float)record.Data.GetProperty(fieldName).GetDouble();
    }

    private static IReadOnlyList<PsilinPairHexRecord> ReadPairTraceRecords(IReadOnlyList<ParityTraceRecord> records, string kind, IReadOnlyList<string> fields)
    {
        return records
            .Where(record => record.Kind == kind)
            .Select(record => new PsilinPairHexRecord(
                kind,
                record.Data.GetProperty("jo").GetInt32(),
                record.Data.GetProperty("jp").GetInt32(),
                fields.Select(field => ReadTraceBits(record, field)).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<PsilinAccumHexRecord> ReadAccumTraceRecords(IReadOnlyList<ParityTraceRecord> records)
    {
        return records
            .Where(record => record.Kind == "psilin_accum_state")
            .Select(record => new PsilinAccumHexRecord(
                record.Data.GetProperty("stage").GetString() ?? string.Empty,
                record.Data.GetProperty("jo").GetInt32(),
                record.Data.GetProperty("jp").GetInt32(),
                AccumFields.Select(field => ReadTraceBits(record, field)).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<PsilinResultHexRecord> ReadResultTraceRecords(IReadOnlyList<ParityTraceRecord> records, string kind, IReadOnlyList<string> fields)
    {
        return records
            .Where(record => record.Kind == kind)
            .Select(record => new PsilinResultHexRecord(
                kind,
                fields.Select(field => ReadTraceBits(record, field)).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<string> BuildFinalBits(InviscidSolverState state, float psi, float psiNi)
    {
        int last = state.NodeCount - 1;
        return new[]
        {
            ToHex(psi),
            ToHex(psiNi),
            ToHex((float)state.StreamfunctionVortexSensitivity[0]),
            ToHex((float)state.StreamfunctionVortexSensitivity[Math.Min(1, last)]),
            ToHex((float)state.StreamfunctionVortexSensitivity[Math.Min(2, last)]),
            ToHex((float)state.StreamfunctionVortexSensitivity[last]),
            ToHex((float)state.StreamfunctionSourceSensitivity[0]),
            ToHex((float)state.StreamfunctionSourceSensitivity[Math.Min(1, last)]),
            ToHex((float)state.StreamfunctionSourceSensitivity[Math.Min(2, last)]),
            ToHex((float)state.StreamfunctionSourceSensitivity[last]),
            ToHex((float)state.VelocityVortexSensitivity[0]),
            ToHex((float)state.VelocityVortexSensitivity[Math.Min(1, last)]),
            ToHex((float)state.VelocityVortexSensitivity[Math.Min(2, last)]),
            ToHex((float)state.VelocityVortexSensitivity[last]),
            ToHex((float)state.VelocitySourceSensitivity[0]),
            ToHex((float)state.VelocitySourceSensitivity[Math.Min(1, last)]),
            ToHex((float)state.VelocitySourceSensitivity[Math.Min(2, last)]),
            ToHex((float)state.VelocitySourceSensitivity[last])
        };
    }

    private static string ReadTraceBits(ParityTraceRecord record, string fieldName)
    {
        if (record.TryGetDataBits(fieldName, out IReadOnlyDictionary<string, string>? bits) && bits is not null)
        {
            if (bits.TryGetValue("f32", out string? single))
            {
                return single[2..];
            }

            if (bits.TryGetValue("f64", out string? dbl))
            {
                ulong doubleValue = ulong.Parse(dbl[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                double asDouble = BitConverter.Int64BitsToDouble(unchecked((long)doubleValue));
                return ToHex((float)asDouble);
            }
        }

        return ToHex((float)record.Data.GetProperty(fieldName).GetDouble());
    }

    private static void AssertRecordsEqual(
        IReadOnlyList<PsilinHexRecord> expected,
        IReadOnlyList<PsilinHexRecord> actual,
        string kind,
        IReadOnlyList<string> fieldNames,
        int caseIndex)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            Assert.Equal(expected[recordIndex].PanelIndex, actual[recordIndex].PanelIndex);
            Assert.Equal(expected[recordIndex].Half, actual[recordIndex].Half);
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"case={caseIndex} kind={kind} record={recordIndex} panel={expected[recordIndex].PanelIndex} half={expected[recordIndex].Half} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static void AssertPanelRecordsEqual(
        IReadOnlyList<PsilinPanelHexRecord> expected,
        IReadOnlyList<PsilinPanelHexRecord> actual,
        string kind,
        IReadOnlyList<string> fieldNames,
        int caseIndex)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            Assert.Equal(expected[recordIndex].PanelIndex, actual[recordIndex].PanelIndex);
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"case={caseIndex} kind={kind} record={recordIndex} panel={expected[recordIndex].PanelIndex} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static void AssertPairRecordsEqual(
        IReadOnlyList<PsilinPairHexRecord> expected,
        IReadOnlyList<PsilinPairHexRecord> actual,
        string kind,
        IReadOnlyList<string> fieldNames,
        int caseIndex)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            Assert.Equal(expected[recordIndex].Jo, actual[recordIndex].Jo);
            Assert.Equal(expected[recordIndex].Jp, actual[recordIndex].Jp);
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"case={caseIndex} kind={kind} record={recordIndex} jo={expected[recordIndex].Jo} jp={expected[recordIndex].Jp} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static void AssertAccumRecordsEqual(
        IReadOnlyList<PsilinAccumHexRecord> expected,
        IReadOnlyList<PsilinAccumHexRecord> actual,
        IReadOnlyList<string> fieldNames,
        int caseIndex)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            Assert.Equal(expected[recordIndex].Stage, actual[recordIndex].Stage);
            Assert.Equal(expected[recordIndex].Jo, actual[recordIndex].Jo);
            Assert.Equal(expected[recordIndex].Jp, actual[recordIndex].Jp);
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"case={caseIndex} kind=ACCUM stage={expected[recordIndex].Stage} record={recordIndex} jo={expected[recordIndex].Jo} jp={expected[recordIndex].Jp} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static void AssertResultRecordsEqual(
        IReadOnlyList<PsilinResultHexRecord> expected,
        IReadOnlyList<PsilinResultHexRecord> actual,
        string kind,
        IReadOnlyList<string> fieldNames,
        int caseIndex)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"case={caseIndex} kind={kind} record={recordIndex} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static FortranPsilinCase CaptureCase(
        LinearVortexPanelState panel,
        InviscidSolverState state,
        int fieldNodeIndex,
        float fieldX,
        float fieldY,
        float fieldNormalX,
        float fieldNormalY,
        bool includeSourceTerms,
        float freestreamSpeed,
        float angleOfAttackRadians)
    {
        int n = panel.NodeCount;
        return new FortranPsilinCase(
            fieldNodeIndex,
            includeSourceTerms,
            state.IsSharpTrailingEdge,
            fieldX,
            fieldY,
            fieldNormalX,
            fieldNormalY,
            freestreamSpeed,
            angleOfAttackRadians,
            (float)state.TrailingEdgeGap,
            (float)state.TrailingEdgeAngleNormal,
            (float)state.TrailingEdgeAngleStreamwise,
            panel.X.Take(n).Select(value => (float)value).ToArray(),
            panel.Y.Take(n).Select(value => (float)value).ToArray(),
            panel.ArcLength.Take(n).Select(value => (float)value).ToArray(),
            panel.PanelAngle.Take(n).Select(value => (float)value).ToArray(),
            state.VortexStrength.Take(n).Select(value => (float)value).ToArray(),
            state.SourceStrength.Take(n).Select(value => (float)value).ToArray());
    }

    private static void BuildManagedState(FortranPsilinCase @case, out LinearVortexPanelState panel, out InviscidSolverState state)
    {
        int n = @case.X.Length;
        panel = new LinearVortexPanelState(n);
        panel.Resize(n);
        state = new InviscidSolverState(n);
        state.InitializeForNodeCount(n);
        state.UseLegacyKernelPrecision = true;

        for (int i = 0; i < n; i++)
        {
            panel.X[i] = @case.X[i];
            panel.Y[i] = @case.Y[i];
            panel.ArcLength[i] = @case.ArcLength[i];
            panel.PanelAngle[i] = @case.PanelAngle[i];
            state.VortexStrength[i] = @case.VortexStrength[i];
            state.SourceStrength[i] = @case.SourceStrength[i];
        }

        ParametricSpline.FitSegmented(panel.X, panel.XDerivative, panel.ArcLength, n);
        ParametricSpline.FitSegmented(panel.Y, panel.YDerivative, panel.ArcLength, n);
        PanelGeometryBuilder.ComputeNormals(panel);

        state.IsSharpTrailingEdge = @case.IsSharpTrailingEdge;
        state.TrailingEdgeGap = @case.TrailingEdgeGap;
        state.TrailingEdgeAngleNormal = @case.TrailingEdgeAngleNormal;
        state.TrailingEdgeAngleStreamwise = @case.TrailingEdgeAngleStreamwise;
    }

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
        panel.Chord = 1.0;
        PanelGeometryBuilder.ComputeTrailingEdgeGeometry(panel, state);
        state.IsSharpTrailingEdge = true;
        PanelGeometryBuilder.ComputePanelAngles(panel, state);

        return (panel, state);
    }

    private static (LinearVortexPanelState panel, InviscidSolverState state) CreateDiamond()
    {
        const int n = 5;
        var panel = new LinearVortexPanelState(n);
        panel.Resize(n);
        var state = new InviscidSolverState(n);
        state.InitializeForNodeCount(n);

        panel.X[0] = 1.0; panel.Y[0] = 0.0;
        panel.X[1] = 0.0; panel.Y[1] = 0.2;
        panel.X[2] = -1.0; panel.Y[2] = 0.0;
        panel.X[3] = 0.0; panel.Y[3] = -0.2;
        panel.X[4] = 1.0; panel.Y[4] = 0.0;

        ParametricSpline.ComputeArcLength(panel.X, panel.Y, panel.ArcLength, n);
        ParametricSpline.FitSegmented(panel.X, panel.XDerivative, panel.ArcLength, n);
        ParametricSpline.FitSegmented(panel.Y, panel.YDerivative, panel.ArcLength, n);
        PanelGeometryBuilder.ComputeNormals(panel);
        panel.Chord = 2.0;
        PanelGeometryBuilder.ComputeTrailingEdgeGeometry(panel, state);
        PanelGeometryBuilder.ComputePanelAngles(panel, state);

        return (panel, state);
    }

    private static string ToHex(float value)
        => $"{BitConverter.SingleToInt32Bits(value):X8}";

    private sealed record ManagedPsilinResult(
        IReadOnlyList<PsilinPanelHexRecord> PanelStates,
        IReadOnlyList<PsilinHexRecord> HalfTerms,
        IReadOnlyList<PsilinHexRecord> DzTerms,
        IReadOnlyList<PsilinHexRecord> DqTerms,
        IReadOnlyList<PsilinHexRecord> Segments,
        IReadOnlyList<PsilinPairHexRecord> VortexSegments,
        IReadOnlyList<PsilinPairHexRecord> TeCorrections,
        IReadOnlyList<PsilinAccumHexRecord> AccumStates,
        IReadOnlyList<PsilinResultHexRecord> ResultTerms,
        IReadOnlyList<PsilinResultHexRecord> Results,
        IReadOnlyList<string> FinalBits);

    private sealed record ManagedTraceProbe(
        IReadOnlyDictionary<string, int> ObserverCounts,
        IReadOnlyDictionary<string, int> FileCounts);
}
