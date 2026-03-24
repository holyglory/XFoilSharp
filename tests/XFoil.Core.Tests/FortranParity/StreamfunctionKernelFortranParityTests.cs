using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: PSILIN source-segment kernel
// Secondary legacy source: tools/fortran-debug/json_trace.f PSILIN trace instrumentation
// Role in port: Compares managed PSILIN source-kernel trace records against the instrumented Fortran reference artifacts.
// Differences: The test is managed-only verification infrastructure, but it is aligned directly to the legacy PSILIN event stream instead of using broader solver outputs.
// Decision: Keep the artifact-backed block tests because they isolate the first parity boundary before DIJ and viscous assembly amplify it.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class StreamfunctionKernelFortranParityTests
{
    private const string CaseId = "n0012_re1e6_a0_p12_n9_psilin";
    private const string P80PdyyCaseId = "n0012_re1e6_a0_p80_psilin_pdyy";
    private static readonly FortranReferenceCase[] TePgamCases =
    {
        CreateTePgamCase("n0012_re1e6_am15_p80_psilin_te_terms", "0012", 1_000_000.0, -1.5),
        CreateTePgamCase("n0012_re1e6_a0_p80_psilin_te_terms", "0012", 1_000_000.0, 0.0),
        CreateTePgamCase("n0012_re1e6_a2_p80_psilin_te_terms", "0012", 1_000_000.0, 2.0),
        CreateTePgamCase("n0012_re1e6_a5_p80_psilin_te_terms", "0012", 1_000_000.0, 5.0),
        CreateTePgamCase("n0012_re1e6_a8_p80_psilin_te_terms", "0012", 1_000_000.0, 8.0),
        CreateTePgamCase("n0012_re1e6_a10_p80_psilin_te_terms", "0012", 1_000_000.0, 10.0),
        CreateTePgamCase("n1412_re14e5_a0_p80_psilin_te_terms", "1412", 1_400_000.0, 0.0),
        CreateTePgamCase("n1412_re14e5_a5_p80_psilin_te_terms", "1412", 1_400_000.0, 5.0),
        CreateTePgamCase("n2312_re22e5_a2_p80_psilin_te_terms", "2312", 2_200_000.0, 2.0),
        CreateTePgamCase("n2312_re22e5_a8_p80_psilin_te_terms", "2312", 2_200_000.0, 8.0),
        CreateTePgamCase("n2412_re3e6_a3_p80_psilin_te_terms", "2412", 3_000_000.0, 3.0),
        CreateTePgamCase("n2412_re3e6_a10_p80_psilin_te_terms", "2412", 3_000_000.0, 10.0),
        CreateTePgamCase("n4415_re5e6_a2_p80_psilin_te_terms", "4415", 5_000_000.0, 2.0)
    };
    private static readonly string[] SegmentRequiredKinds =
    {
        "psilin_source_segment"
    };
    private static readonly string[] PdyyWriteRequiredKinds =
    {
        "psilin_source_pdyy_write"
    };
    private static readonly string[] TePgamRequiredKinds =
    {
        "psilin_te_pgam_terms"
    };

    [Fact]
    // Legacy mapping: xpanel.f PSILIN half-2 source polynomial and staged pdif accumulator.
    // Difference from legacy: The managed test reads the emitted trace artifacts rather than relying on whole-solver output deltas. Decision: Keep the block-local regression because this is the first streamfunction mismatch boundary.
    public void Naca0012_Re1e6_A0_SourceHalf2Terms_Record134_MatchFortran()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(CaseId);
        Assert.True(File.Exists(referencePath), $"Fortran reference trace missing: {referencePath}");

        FortranReferenceCases.RefreshManagedArtifacts(CaseId);
        string managedPath = FortranReferenceCases.GetManagedTracePath(CaseId);

        var selector = new TraceEventSelector(
            Kind: "psilin_source_half_terms",
            TagFilters: new Dictionary<string, object?>
            {
                ["fieldIndex"] = 1,
                ["panelIndex"] = 1,
                ["half"] = 2,
                ["precision"] = "Single"
            });

        var (reference, managed) = ParityTraceAligner.AlignSingle(referencePath, managedPath, selector);
        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields: new[]
            {
                new FieldExpectation("data.fieldIndex", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.panelIndex", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.half", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.precision", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.x0"),
                new FieldExpectation("data.psumTerm1"),
                new FieldExpectation("data.psumTerm2"),
                new FieldExpectation("data.psumTerm3"),
                new FieldExpectation("data.pdifTerm1"),
                new FieldExpectation("data.pdifTerm2"),
                new FieldExpectation("data.pdifTerm3"),
                new FieldExpectation("data.pdifTerm4")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.psumAccum"),
                new FieldExpectation("data.psum"),
                new FieldExpectation("data.pdifAccum1"),
                new FieldExpectation("data.pdifAccum2"),
                new FieldExpectation("data.pdifNumerator"),
                new FieldExpectation("data.pdif")
            },
            blockDescription: "PSILIN half-2 source half terms (field=1 panel=1)");
    }

    [Fact]
    // Legacy mapping: xpanel.f PSILIN half-2 dQ/dm derivative tail.
    // Difference from legacy: The managed test enforces the input-first/output-second comparison policy explicitly on the trace record. Decision: Keep the block regression because it guards the known record-134 derivative drift.
    public void Naca0012_Re1e6_A0_SourceHalf2DerivativeTail_Record134_MatchFortran()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(CaseId);
        Assert.True(File.Exists(referencePath), $"Fortran reference trace missing: {referencePath}");

        FortranReferenceCases.RefreshManagedArtifacts(CaseId);
        string managedPath = FortranReferenceCases.GetManagedTracePath(CaseId);

        var selector = new TraceEventSelector(
            Kind: "psilin_source_dq_terms",
            TagFilters: new Dictionary<string, object?>
            {
                ["fieldIndex"] = 1,
                ["panelIndex"] = 1,
                ["half"] = 2,
                ["precision"] = "Single"
            });

        var (reference, managed) = ParityTraceAligner.AlignSingle(referencePath, managedPath, selector);
        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields: new[]
            {
                new FieldExpectation("data.fieldIndex", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.panelIndex", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.half", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.precision", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.dqJoTerm1"),
                new FieldExpectation("data.dqJoTerm2"),
                new FieldExpectation("data.dqJpTerm1"),
                new FieldExpectation("data.dqJpTerm2"),
                new FieldExpectation("data.dqJqTerm1"),
                new FieldExpectation("data.dqJqTerm2")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.dqJmInner"),
                new FieldExpectation("data.dqJoInner"),
                new FieldExpectation("data.dqJpInner"),
                new FieldExpectation("data.dqJqInner")
            },
            blockDescription: "PSILIN half-2 derivative tail (field=1 panel=1)");
    }

    [Fact]
    public void Naca0012_Re1e6_A0_SourceSegments_MatchFortran()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            FortranReferenceCases.GetReferenceDirectory(CaseId),
            SegmentRequiredKinds);
        Assert.True(File.Exists(referencePath), $"Fortran reference trace missing: {referencePath}");

        FortranReferenceCases.RefreshManagedArtifacts(CaseId);
        string managedPath = GetLatestTracePathContainingKinds(
            FortranReferenceCases.GetManagedDirectory(CaseId),
            SegmentRequiredKinds);

        var selector = new TraceEventSelector("psilin_source_segment");
        IReadOnlyDictionary<(int FieldIndex, int PanelIndex, int Half), ParityTraceRecord> reference =
            LoadUniqueByFieldPanelHalf(referencePath, selector);
        IReadOnlyDictionary<(int FieldIndex, int PanelIndex, int Half), ParityTraceRecord> managed =
            LoadUniqueByFieldPanelHalf(managedPath, selector);

        Assert.True(reference.Count >= 300, $"Expected at least 300 focused PSILIN source-segment vectors, found {reference.Count}.");
        Assert.Equal(reference.Count, managed.Count);

        foreach ((int fieldIndex, int panelIndex, int half) in reference.Keys
                     .OrderBy(static value => value.FieldIndex)
                     .ThenBy(static value => value.PanelIndex)
                     .ThenBy(static value => value.Half))
        {
            Assert.True(
                managed.ContainsKey((fieldIndex, panelIndex, half)),
                $"Managed PSILIN source-segment field={fieldIndex} panel={panelIndex} half={half} missing.");

            FortranParityAssert.AssertInputsThenOutputs(
                reference[(fieldIndex, panelIndex, half)],
                managed[(fieldIndex, panelIndex, half)],
                inputFields: new[]
                {
                    new FieldExpectation("data.fieldIndex", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.panelIndex", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.half", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.precision"),
                    new FieldExpectation("data.jm", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.jo", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.jp", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.jq", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.x1"),
                    new FieldExpectation("data.x2"),
                    new FieldExpectation("data.yy"),
                    new FieldExpectation("data.panelAngle"),
                    new FieldExpectation("data.x1i"),
                    new FieldExpectation("data.x2i"),
                    new FieldExpectation("data.yyi"),
                    new FieldExpectation("data.rs0"),
                    new FieldExpectation("data.rs1"),
                    new FieldExpectation("data.rs2"),
                    new FieldExpectation("data.g0"),
                    new FieldExpectation("data.g1"),
                    new FieldExpectation("data.g2"),
                    new FieldExpectation("data.t0"),
                    new FieldExpectation("data.t1"),
                    new FieldExpectation("data.t2"),
                    new FieldExpectation("data.dso"),
                    new FieldExpectation("data.dsio"),
                    new FieldExpectation("data.dsm"),
                    new FieldExpectation("data.dsim"),
                    new FieldExpectation("data.dsp"),
                    new FieldExpectation("data.dsip"),
                    new FieldExpectation("data.dxInv"),
                    new FieldExpectation("data.sourceTermLeft"),
                    new FieldExpectation("data.sourceTermRight")
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.ssum"),
                    new FieldExpectation("data.sdif"),
                    new FieldExpectation("data.psum"),
                    new FieldExpectation("data.pdif"),
                    new FieldExpectation("data.psx0"),
                    new FieldExpectation("data.psx1"),
                    new FieldExpectation("data.psx2"),
                    new FieldExpectation("data.psyy"),
                    new FieldExpectation("data.pdx0Term1"),
                    new FieldExpectation("data.pdx0Term2"),
                    new FieldExpectation("data.pdx0Numerator"),
                    new FieldExpectation("data.pdx0"),
                    new FieldExpectation("data.pdx1Term1"),
                    new FieldExpectation("data.pdx1Term2"),
                    new FieldExpectation("data.pdx1Numerator"),
                    new FieldExpectation("data.pdx1"),
                    new FieldExpectation("data.pdx2Term1"),
                    new FieldExpectation("data.pdx2Term2"),
                    new FieldExpectation("data.pdx2Numerator"),
                    new FieldExpectation("data.pdx2"),
                    new FieldExpectation("data.pdyyTerm1"),
                    new FieldExpectation("data.pdyyTailLinear"),
                    new FieldExpectation("data.pdyyTailAngular"),
                    new FieldExpectation("data.pdyyTerm2"),
                    new FieldExpectation("data.pdyyNumerator"),
                    new FieldExpectation("data.pdyy"),
                    new FieldExpectation("data.psniTerm1"),
                    new FieldExpectation("data.psniTerm2"),
                    new FieldExpectation("data.psniTerm3"),
                    new FieldExpectation("data.psni"),
                    new FieldExpectation("data.pdniTerm1"),
                    new FieldExpectation("data.pdniTerm2"),
                    new FieldExpectation("data.pdniTerm3"),
                    new FieldExpectation("data.pdni"),
                    new FieldExpectation("data.dzJm"),
                    new FieldExpectation("data.dzJo"),
                    new FieldExpectation("data.dzJp"),
                    new FieldExpectation("data.dzJq"),
                    new FieldExpectation("data.dqJm"),
                    new FieldExpectation("data.dqJo"),
                    new FieldExpectation("data.dqJp"),
                    new FieldExpectation("data.dqJq")
                },
                blockDescription: $"PSILIN source segment field={fieldIndex} panel={panelIndex} half={half}");
        }
    }

    [Fact]
    public void Naca0012_Re1e6_A0_SourcePdyyWrites_MatchFortran()
        => AssertSourcePdyyWritesMatchFortran(CaseId, minimumVectors: 300);

    [Fact]
    public void Naca0012_Re1e6_A0_P80_SourcePdyyWrites_MatchFortran_Over1000Vectors()
        => AssertSourcePdyyWritesMatchFortran(P80PdyyCaseId, minimumVectors: 1000);

    [Fact]
    public void MixedP80_TePgamTerms_MatchFortran_Over1000Vectors()
    {
        const int minimumVectors = 1000;
        var selector = new TraceEventSelector("psilin_te_pgam_terms");
        var reference = new Dictionary<(string CaseId, int FieldIndex, int Jo, int Jp), ParityTraceRecord>();
        var managed = new Dictionary<(string CaseId, int FieldIndex, int Jo, int Jp), ParityTraceRecord>();

        foreach (FortranReferenceCase definition in TePgamCases)
        {
            string referencePath = GetLatestTracePathContainingKinds(
                FortranReferenceCases.GetReferenceDirectory(definition.CaseId),
                TePgamRequiredKinds);
            Assert.True(File.Exists(referencePath), $"Fortran TE PGAM reference trace missing: {referencePath}");

            FortranReferenceCases.RefreshManagedArtifacts(definition);
            string managedPath = GetLatestTracePathContainingKinds(
                FortranReferenceCases.GetManagedDirectory(definition.CaseId),
                TePgamRequiredKinds);

            foreach (((int fieldIndex, int jo, int jp), ParityTraceRecord record) in LoadUniqueTePgamByFieldJoJp(referencePath, selector))
            {
                reference.Add((definition.CaseId, fieldIndex, jo, jp), record);
            }

            foreach (((int fieldIndex, int jo, int jp), ParityTraceRecord record) in LoadUniqueTePgamByFieldJoJp(managedPath, selector))
            {
                managed.Add((definition.CaseId, fieldIndex, jo, jp), record);
            }
        }

        Assert.True(reference.Count >= minimumVectors, $"Expected at least {minimumVectors} PSILIN TE PGAM-term vectors, found {reference.Count}.");
        Assert.Equal(reference.Count, managed.Count);

        foreach ((string caseId, int fieldIndex, int jo, int jp) in reference.Keys
                     .OrderBy(static value => value.CaseId, StringComparer.Ordinal)
                     .ThenBy(static value => value.FieldIndex)
                     .ThenBy(static value => value.Jo)
                     .ThenBy(static value => value.Jp))
        {
            Assert.True(
                managed.ContainsKey((caseId, fieldIndex, jo, jp)),
                $"Managed PSILIN TE PGAM-term case={caseId} field={fieldIndex} jo={jo} jp={jp} missing.");

            FortranParityAssert.AssertInputsThenOutputs(
                reference[(caseId, fieldIndex, jo, jp)],
                managed[(caseId, fieldIndex, jo, jp)],
                inputFields: new[]
                {
                    new FieldExpectation("data.fieldIndex", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.jo", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.jp", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.precision")
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.pgamLeadProduct1"),
                    new FieldExpectation("data.pgamLeadProduct2"),
                    new FieldExpectation("data.pgamLeadPair"),
                    new FieldExpectation("data.pgamBase"),
                    new FieldExpectation("data.pgamDt"),
                    new FieldExpectation("data.pgamTail")
                },
                blockDescription: $"PSILIN TE PGAM terms case={caseId} field={fieldIndex} jo={jo} jp={jp}");
        }
    }

    private static void AssertSourcePdyyWritesMatchFortran(string caseId, int minimumVectors)
    {
        string referencePath = GetLatestTracePathContainingKinds(
            FortranReferenceCases.GetReferenceDirectory(caseId),
            PdyyWriteRequiredKinds);
        Assert.True(File.Exists(referencePath), $"Fortran reference trace missing: {referencePath}");

        FortranReferenceCases.RefreshManagedArtifacts(caseId);
        string managedPath = GetLatestTracePathContainingKinds(
            FortranReferenceCases.GetManagedDirectory(caseId),
            PdyyWriteRequiredKinds);

        var selector = new TraceEventSelector("psilin_source_pdyy_write");
        IReadOnlyDictionary<(int FieldIndex, int PanelIndex, int Half), ParityTraceRecord> reference =
            LoadUniqueByFieldPanelHalf(referencePath, selector);
        IReadOnlyDictionary<(int FieldIndex, int PanelIndex, int Half), ParityTraceRecord> managed =
            LoadUniqueByFieldPanelHalf(managedPath, selector);

        Assert.True(reference.Count >= minimumVectors, $"Expected at least {minimumVectors} focused PSILIN PDYY-write vectors, found {reference.Count}.");
        Assert.Equal(reference.Count, managed.Count);

        foreach ((int fieldIndex, int panelIndex, int half) in reference.Keys
                     .OrderBy(static value => value.FieldIndex)
                     .ThenBy(static value => value.PanelIndex)
                     .ThenBy(static value => value.Half))
        {
            Assert.True(
                managed.ContainsKey((fieldIndex, panelIndex, half)),
                $"Managed PSILIN PDYY-write field={fieldIndex} panel={panelIndex} half={half} missing.");

            FortranParityAssert.AssertInputsThenOutputs(
                reference[(fieldIndex, panelIndex, half)],
                managed[(fieldIndex, panelIndex, half)],
                inputFields: new[]
                {
                    new FieldExpectation("data.fieldIndex", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.panelIndex", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.half", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.precision"),
                    new FieldExpectation("data.x0"),
                    new FieldExpectation("data.xEdge"),
                    new FieldExpectation("data.yy"),
                    new FieldExpectation("data.t0"),
                    new FieldExpectation("data.tEdge"),
                    new FieldExpectation("data.psyy"),
                    new FieldExpectation("data.dxInv")
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.pdyyWriteDt"),
                    new FieldExpectation("data.pdyyWriteInner"),
                    new FieldExpectation("data.pdyyWriteHead"),
                    new FieldExpectation("data.pdyyWriteTail"),
                    new FieldExpectation("data.pdyyWriteSum"),
                    new FieldExpectation("data.pdyyWriteValue"),
                    new FieldExpectation("data.pdyyTerm1"),
                    new FieldExpectation("data.pdyyTerm2"),
                    new FieldExpectation("data.pdyyNumerator"),
                    new FieldExpectation("data.pdyy")
                },
                blockDescription: $"PSILIN PDYY write field={fieldIndex} panel={panelIndex} half={half}");
        }
    }

    private static IReadOnlyDictionary<(int FieldIndex, int PanelIndex, int Half), ParityTraceRecord> LoadUniqueByFieldPanelHalf(
        string path,
        TraceEventSelector selector)
    {
        return ParityTraceLoader
            .ReadMatching(path, record => ParityTraceAligner.Matches(record, selector))
            .ToDictionary(
                record => (
                    FieldIndex: record.Data.GetProperty("fieldIndex").GetInt32(),
                    PanelIndex: record.Data.GetProperty("panelIndex").GetInt32(),
                    Half: record.Data.GetProperty("half").GetInt32()),
                record => record);
    }

    private static IReadOnlyDictionary<(int FieldIndex, int Jo, int Jp), ParityTraceRecord> LoadUniqueTePgamByFieldJoJp(
        string path,
        TraceEventSelector selector)
    {
        var unique = new Dictionary<(int FieldIndex, int Jo, int Jp), ParityTraceRecord>();
        foreach (ParityTraceRecord record in ParityTraceLoader.ReadMatching(path, candidate => ParityTraceAligner.Matches(candidate, selector)))
        {
            var key = (
                FieldIndex: record.Data.GetProperty("fieldIndex").GetInt32(),
                Jo: record.Data.GetProperty("jo").GetInt32(),
                Jp: record.Data.GetProperty("jp").GetInt32());

            if (unique.TryGetValue(key, out ParityTraceRecord? existing))
            {
                Assert.True(
                    existing.Data.GetRawText() == record.Data.GetRawText(),
                    $"PSILIN TE PGAM-term duplicates diverged for field={key.FieldIndex} jo={key.Jo} jp={key.Jp} in {path}.");
                continue;
            }

            unique.Add(key, record);
        }

        return unique;
    }

    private static FortranReferenceCase CreateTePgamCase(
        string caseId,
        string airfoilCode,
        double reynoldsNumber,
        double alphaDegrees)
    {
        return new FortranReferenceCase(
            caseId,
            airfoilCode,
            reynoldsNumber,
            alphaDegrees,
            $"{airfoilCode} Re={reynoldsNumber:G} alpha={alphaDegrees:G} p80 PSILIN TE PGAM terms",
            PanelCount: 80,
            MaxViscousIterations: 200,
            CriticalAmplificationFactor: 9.0,
            TraceKindAllowList: "psilin_te_pgam_terms");
    }

    private static string GetLatestTracePathContainingKinds(string directory, params string[] requiredKinds)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "reference_trace*.jsonl")
                     .Concat(Directory.EnumerateFiles(directory, "csharp_trace*.jsonl"))
                     .OrderByDescending(GetArtifactCounter))
        {
            if (!requiredKinds.All(kind => File.ReadLines(path).Any(line => line.Contains($"\"kind\":\"{kind}\"", StringComparison.Ordinal))))
            {
                continue;
            }

            try
            {
                _ = ParityTraceLoader.ReadAll(path);
                return path;
            }
            catch (JsonException)
            {
                continue;
            }
            catch (FormatException)
            {
                continue;
            }
        }

        throw new InvalidOperationException(
            $"No valid trace artifact under '{directory}' contained all required kinds: {string.Join(", ", requiredKinds)}.");
    }

    private static long GetArtifactCounter(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        int lastDot = fileName.LastIndexOf('.');
        if (lastDot >= 0 &&
            long.TryParse(fileName[(lastDot + 1)..], out long counter))
        {
            return counter;
        }

        return long.MinValue;
    }
}
