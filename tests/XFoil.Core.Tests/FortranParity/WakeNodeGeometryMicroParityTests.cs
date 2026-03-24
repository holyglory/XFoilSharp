using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: XYWAKE/SETBL wake-node geometry march
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_wakecol_ref/reference_trace*.jsonl
// Role in port: Verifies the managed wake-node geometry packets against the authoritative Fortran alpha-10 panel-80 wake trace.
// Differences: The managed port resolves existing focused trace artifacts instead of rediscovering the wake geometry through a broad wake-column replay.
// Decision: Keep the node-level trace oracle because it is the cheapest promotable proof for the XYWAKE wake-node boundary.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityMicro")]
public sealed class WakeNodeGeometryMicroParityTests
{
    private const long MaxPreferredManagedTraceBytes = 256L * 1024L * 1024L;
    private static readonly Regex VersionedTraceCounterRegex = new(
        @"^csharp_trace\.(?<counter>\d+)\.jsonl$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string ReferencePath = GetReferencePath();
    private static readonly string ManagedPath = GetManagedPath();

    [Fact]
    // Legacy mapping: xpanel.f XYWAKE/SETBL first wake-node block in the authoritative reduced-panel alpha-0 full trace.
    // Difference from legacy: The port compares the first repeated wake-node block from stored full-run artifacts instead of reconstructing the wake topology through a broader full parity rerun.
    // Decision: Keep this dedicated reduced-panel oracle because it isolates wake-node count/topology drift before downstream UESET/SETBL consumers see the extra wake station.
    public void Alpha0_P12_WakeNodeSequence_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        IReadOnlyList<ParityTraceRecord> reference = SelectFirstWakeNodeBlock(
            ParityTraceLoader.ReadMatching(
                GetAlpha0FullReferencePath(),
                record => record.Kind == "wake_node"));
        IReadOnlyList<ParityTraceRecord> managed = SelectFirstWakeNodeBlock(
            ParityTraceLoader.ReadMatching(
                GetAlpha0FullManagedPath(),
                record => record.Kind == "wake_node"));

        Assert.NotEmpty(reference);
        Assert.Equal(reference.Count, managed.Count);

        for (int i = 0; i < reference.Count; i++)
        {
            FortranParityAssert.AssertInputsThenOutputs(
                reference[i],
                managed[i],
                inputFields: new[]
                {
                    new FieldExpectation("data.index", NumericComparisonMode.ExactDouble)
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.x"),
                    new FieldExpectation("data.y"),
                    new FieldExpectation("data.nx"),
                    new FieldExpectation("data.ny"),
                    new FieldExpectation("data.panelAngle")
                },
                blockDescription: $"Alpha-0 P12 wake node {i + 1}");
        }
    }

    [Fact]
    // Legacy mapping: xpanel.f XYWAKE/SETBL wake-node packets on the alpha-10 P80 wake march.
    // Difference from legacy: The port checks the full traced node sequence directly from stored artifacts rather than via a larger wake-column integration test.
    // Decision: Keep the packet sequence because it is stable, fast, and maps directly to the wake-node geometry oracle we want in the matrix.
    public void Alpha10_P80_WakeNodeSequence_BitwiseMatchesFortranTrace()
    {
        IReadOnlyList<ParityTraceRecord> reference = ParityTraceLoader.ReadMatching(
            ReferencePath,
            record => record.Kind == "wake_node");
        IReadOnlyList<ParityTraceRecord> managed = ParityTraceLoader.ReadMatching(
            ManagedPath,
            record => record.Kind == "wake_node");

        Assert.NotEmpty(reference);
        Assert.True(
            managed.Count >= reference.Count,
            $"Managed wake trace is shorter than reference wake trace: managed={managed.Count} reference={reference.Count}");

        for (int i = 0; i < reference.Count; i++)
        {
            FortranParityAssert.AssertInputsThenOutputs(
                reference[i],
                managed[i],
                inputFields: new[]
                {
                    new FieldExpectation("data.index", NumericComparisonMode.ExactDouble)
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.x"),
                    new FieldExpectation("data.y"),
                    new FieldExpectation("data.nx"),
                    new FieldExpectation("data.ny"),
                    new FieldExpectation("data.panelAngle")
                },
                blockDescription: $"Alpha-10 P80 wake node {i + 1}");
        }
    }

    private static string GetReferencePath()
    {
        string directory = Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            "alpha10_p80_wakecol_ref");
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }

    private static string GetManagedPath()
    {
        const string caseId = "n0012_re1e6_a10_p80_wakenode";
        FortranReferenceCases.EnsureManagedArtifacts(caseId);
        string path = FortranReferenceCases.GetManagedTracePath(caseId);
        Assert.True(File.Exists(path), $"Managed wake-node trace missing: {path}");
        return path;
    }

    private static string GetAlpha0FullReferencePath()
    {
        return FortranReferenceCases.GetReferenceTracePath("n0012_re1e6_a0_p12_n9_full");
    }

    private static string GetAlpha0FullManagedPath()
    {
        const string caseId = "n0012_re1e6_a0_p12_n9_full";
        FortranReferenceCases.EnsureManagedArtifacts(caseId);
        string? path = TryGetLatestBoundedManagedTracePathContainingWakeNodes(caseId);
        if (path is null)
        {
            RefreshManagedArtifactsForAlpha0WakeNodes(caseId);
            path = TryGetLatestBoundedManagedTracePathContainingWakeNodes(caseId)
                ?? FortranReferenceCases.GetManagedTracePath(caseId);
        }

        Assert.True(File.Exists(path), $"Managed reduced-panel wake-node trace missing: {path}");
        return path;
    }

    private static string? TryGetLatestBoundedManagedTracePathContainingWakeNodes(string caseId)
    {
        string managedDir = FortranReferenceCases.GetManagedDirectory(caseId);
        if (!Directory.Exists(managedDir))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(managedDir, "csharp_trace*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Counter = TryParseVersionedTraceCounter(path),
                Length = new FileInfo(path).Length
            })
            .Where(candidate => candidate.Counter is not null && candidate.Length <= MaxPreferredManagedTraceBytes)
            .OrderByDescending(candidate => candidate.Counter!.Value)
            .Select(candidate => candidate.Path)
            .FirstOrDefault(ContainsWakeNodePacket);
    }

    private static bool ContainsWakeNodePacket(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.Contains("\"kind\":\"wake_node\"", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static long? TryParseVersionedTraceCounter(string path)
    {
        Match match = VersionedTraceCounterRegex.Match(Path.GetFileName(path));
        return match.Success && long.TryParse(match.Groups["counter"].Value, out long counter)
            ? counter
            : null;
    }

    private static void RefreshManagedArtifactsForAlpha0WakeNodes(string caseId)
    {
        using var environment = new TemporaryEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["XFOILSHARP_FORCE_PARITY_REFRESH"] = "1",
                ["XFOIL_TRACE_KIND_ALLOW"] = "wake_node"
            });
        FortranReferenceCases.RefreshManagedArtifacts(caseId);
    }

    private static IReadOnlyList<ParityTraceRecord> SelectFirstWakeNodeBlock(IReadOnlyList<ParityTraceRecord> records)
    {
        List<ParityTraceRecord> ordered = records
            .OrderBy(record => record.Sequence)
            .ToList();
        Assert.NotEmpty(ordered);

        var block = new List<ParityTraceRecord> { ordered[0] };
        for (int index = 1; index < ordered.Count; index++)
        {
            if (HasExactDataInt(ordered[index], "index", 1))
            {
                break;
            }

            block.Add(ordered[index]);
        }

        return block;
    }

    private static bool HasExactDataInt(ParityTraceRecord record, string field, int expected)
    {
        return record.TryGetDataField(field, out var value) &&
               value.ValueKind == System.Text.Json.JsonValueKind.Number &&
               value.GetInt32() == expected;
    }

    private sealed class TemporaryEnvironmentVariables : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

        public TemporaryEnvironmentVariables(IReadOnlyDictionary<string, string?> values)
        {
            foreach ((string key, string? value) in values)
            {
                _previous[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach ((string key, string? value) in _previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
