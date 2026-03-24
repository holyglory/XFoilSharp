using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLDIF logarithmic preamble
// Secondary legacy source: focused reference traces under tools/fortran-debug/reference/*bldif* and the reduced alpha-0 full trace corpus
// Role in port: Proves the shared log/ratio preamble that feeds bldif_log_inputs and bldif_common without replaying a broader station-system consumer.
// Differences: Harness-only batch test; it replays the extracted input packet directly through ComputeBldifLogTerms(...) and compares the emitted ratios/logs bitwise against Fortran traces.
// Decision: Keep this as a smaller shared-code owner surface so direct-seed and reduced-panel regressions can route here before reopening wider BLDIF/BLVAR consumers.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class BldifLogTermsFortranParityTests
{
    private const string MatrixCaptureDirectoryPattern = "micro_rig_matrix_bldif-log-terms_*_ref";

    private static readonly string[] ReferenceDirectories =
    {
        "alpha10_p80_station3_iter2_common_ref",
        "alpha10_p80_station4_iter1_eq3_ref",
        "alpha10_p80_bldif_eq2_st15_i5_ref",
        "alpha10_p80_bldif_extrah_iter3_ref",
        "alpha10_p80_bldif_log_iter1_ref",
        "alpha10_p80_bldif_log_iter3_ref",
        "alpha10_p80_bldif_upw_iter7_station15_trigger_ref",
        "alpha10_p80_station15_iter7_fullproducer_ref",
        "n0012_re1e6_a0_p12_n9_full"
    };

    [Fact]
    public void BldifLogTerms_CuratedTraceBatch_BitwiseMatchesFortran()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        int validatedPairs = 0;

        foreach ((string referenceDirectory, string path) in EnumerateReferenceTraces(debugDir))
        {
            IReadOnlyList<ParityTraceRecord>? records = TryReadRelevantRecords(path);
            if (records is null)
            {
                continue;
            }
            IReadOnlyList<ParityTraceRecord> systems = records
                .Where(static record => record.Kind is "laminar_seed_system" or "transition_seed_system")
                .OrderBy(static record => record.Sequence)
                .ToArray();
            if (systems.Count == 0)
            {
                continue;
            }

            for (int systemIndex = 0; systemIndex < systems.Count; systemIndex++)
            {
                ParityTraceRecord system = systems[systemIndex];
                int? side = TryReadInt(system, "side");
                int? station = TryReadInt(system, "station");
                int? iteration = TryReadInt(system, "iteration");
                int? systemType = TryReadInt(system, "ityp");
                long windowStart = systemIndex == 0 ? long.MinValue : systems[systemIndex - 1].Sequence;
                IReadOnlyList<ParityTraceRecord> windowLogs = records
                    .Where(record => record.Kind == "bldif_log_inputs" &&
                                     record.Sequence > windowStart &&
                                     record.Sequence < system.Sequence)
                    .OrderBy(static record => record.Sequence)
                    .ToArray();
                IReadOnlyList<ParityTraceRecord> windowCommons = records
                    .Where(record => record.Kind == "bldif_common" &&
                                     record.Sequence > windowStart &&
                                     record.Sequence < system.Sequence)
                    .OrderBy(static record => record.Sequence)
                    .ToArray();
                var usedCommonSequences = new HashSet<long>();

                foreach (ParityTraceRecord logInput in windowLogs)
                {
                    int ityp = ReadRequiredInt(logInput, "ityp");
                    ParityTraceRecord? common = windowCommons.FirstOrDefault(record =>
                        !usedCommonSequences.Contains(record.Sequence) &&
                        record.Sequence > logInput.Sequence &&
                        ReadRequiredInt(record, "ityp") == ityp);
                    if (common is null)
                    {
                        continue;
                    }

                    usedCommonSequences.Add(common.Sequence);

                    BoundaryLayerSystemAssembler.BldifLogTerms managedTerms = BoundaryLayerSystemAssembler.ComputeBldifLogTerms(
                        ityp,
                        isSimilarityStation: ityp == 0,
                        ReadRequiredDouble(logInput, "x1"),
                        ReadRequiredDouble(logInput, "x2"),
                        ReadRequiredDouble(logInput, "u1"),
                        ReadRequiredDouble(logInput, "u2"),
                        ReadRequiredDouble(logInput, "t1"),
                        ReadRequiredDouble(logInput, "t2"),
                        ReadRequiredDouble(logInput, "hs1"),
                        ReadRequiredDouble(logInput, "hs2"),
                        useLegacyPrecision: true);

                    string context = $"{referenceDirectory} systemKind={system.Kind} side={side} station={station} iteration={iteration} systemType={systemType} ityp={ityp} logSeq={logInput.Sequence} commonSeq={common.Sequence}";
                    AssertHex(GetFloatFieldHex(logInput, "xRatio"), ToHex((float)managedTerms.XRatio), $"{context} field=xRatio");
                    AssertHex(GetFloatFieldHex(logInput, "uRatio"), ToHex((float)managedTerms.URatio), $"{context} field=uRatio");
                    AssertHex(GetFloatFieldHex(logInput, "tRatio"), ToHex((float)managedTerms.TRatio), $"{context} field=tRatio");
                    AssertHex(GetFloatFieldHex(logInput, "hRatio"), ToHex((float)managedTerms.HRatio), $"{context} field=hRatio");
                    AssertHex(GetFloatFieldHex(common, "xlog"), ToHex((float)managedTerms.XLog), $"{context} field=xlog");
                    AssertHex(GetFloatFieldHex(common, "ulog"), ToHex((float)managedTerms.ULog), $"{context} field=ulog");
                    AssertHex(GetFloatFieldHex(common, "tlog"), ToHex((float)managedTerms.TLog), $"{context} field=tlog");
                    AssertHex(GetFloatFieldHex(common, "hlog"), ToHex((float)managedTerms.HLog), $"{context} field=hlog");
                    AssertHex(GetFloatFieldHex(common, "ddlog"), ToHex((float)managedTerms.DdLog), $"{context} field=ddlog");
                    validatedPairs++;
                }
            }
        }

        Assert.True(validatedPairs >= 1000, $"expected >=1000 validated bldif log/common packet pairs, got {validatedPairs}");
    }

    private static string GetLatestTracePath(string directory)
    {
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }

    private static IReadOnlyList<ParityTraceRecord>? TryReadRelevantRecords(string path)
    {
        try
        {
            return ParityTraceLoader.ReadMatching(
                    path,
                    static record => record.Kind is "laminar_seed_system" or "transition_seed_system" or "bldif_log_inputs" or "bldif_common")
                .OrderBy(static record => record.Sequence)
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<(string ReferenceDirectory, string Path)> EnumerateReferenceTraces(string debugDir)
    {
        string referenceRoot = Path.Combine(debugDir, "reference");
        var yieldedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (string referenceDirectory in ReferenceDirectories)
        {
            foreach (string path in EnumerateDirectoryTraceFiles(Path.Combine(referenceRoot, referenceDirectory)))
            {
                if (yieldedPaths.Add(path))
                {
                    yield return (referenceDirectory, path);
                }
            }
        }

        if (!Directory.Exists(referenceRoot))
        {
            yield break;
        }

        foreach (string directory in Directory.EnumerateDirectories(referenceRoot, MatrixCaptureDirectoryPattern, SearchOption.TopDirectoryOnly)
                     .OrderBy(static candidate => candidate, StringComparer.Ordinal))
        {
            string referenceDirectory = Path.GetFileName(directory);
            foreach (string path in EnumerateDirectoryTraceFiles(directory))
            {
                if (yieldedPaths.Add(path))
                {
                    yield return (referenceDirectory, path);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoryTraceFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (string path in Directory.EnumerateFiles(directory, "reference_trace*.jsonl", SearchOption.TopDirectoryOnly)
                     .OrderBy(static candidate => candidate, StringComparer.Ordinal))
        {
            yield return path;
        }
    }

    private static double ReadRequiredDouble(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out var value), $"Missing data field '{field}' in {record.Kind}.");
        return value.GetDouble();
    }

    private static int ReadRequiredInt(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out var value), $"Missing data field '{field}' in {record.Kind}.");
        return value.GetInt32();
    }

    private static int? TryReadInt(ParityTraceRecord record, string field)
    {
        return record.TryGetDataField(field, out var value) && value.TryGetInt32(out int parsed)
            ? parsed
            : null;
    }

    private static string GetFloatFieldHex(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataBits(field, out IReadOnlyDictionary<string, string>? bits), $"Missing dataBits for '{field}' in {record.Kind}.");
        Assert.NotNull(bits);
        Assert.True(bits!.TryGetValue("f32", out string? hex), $"Missing f32 bits for '{field}' in {record.Kind}.");
        Assert.False(string.IsNullOrWhiteSpace(hex), $"Empty f32 bits for '{field}' in {record.Kind}.");
        return hex!;
    }

    private static string ToHex(float value)
    {
        return $"0x{BitConverter.SingleToUInt32Bits(value):X8}";
    }

    private static void AssertHex(string expected, string actual, string context)
    {
        Assert.True(
            string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase),
            $"{context} expected={expected} actual={actual}");
    }
}
