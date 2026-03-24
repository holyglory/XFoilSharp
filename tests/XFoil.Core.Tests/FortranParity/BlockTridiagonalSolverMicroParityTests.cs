using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xsolve.f :: BLSOLV forward-elimination and back-substitution row snapshots
// Secondary legacy source: tools/fortran-debug/reference/n0012_re1e6_a0_p12_n9_full/reference_trace*.jsonl
// Role in port: Verifies the managed BLSOLV row-array snapshots against the authoritative full alpha-0 Fortran trace.
// Differences: The harness matches the normalized JSONL array packets directly instead of parsing legacy debug dumps.
// Decision: Keep the packet-level oracle because it promotes BLSOLV into the matrix using the same trace contract as other focused producer rigs.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityMicro")]
public sealed class BlockTridiagonalSolverMicroParityTests
{
    private const long MaxPreferredManagedTraceBytes = 256L * 1024L * 1024L;
    private static readonly Regex VersionedTraceCounterRegex = new(
        @"^csharp_trace\.(?<counter>\d+)\.jsonl$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string ReferencePath = GetReferencePath();
    private static readonly string ManagedPath = GetManagedPath();

    [Fact]
    // Legacy mapping: xsolve.f BLSOLV first forward and solved row packets in the full alpha-0 P12/N9 run.
    // Difference from legacy: The port compares the normalized trace arrays directly rather than scraping TEXT dumps from unit 50.
    // Decision: Keep the first-row packet oracle because it is cheap, deterministic, and exercises the new managed BLSOLV trace shape.
    public void Alpha0_P12_Blsolv_FirstForwardAndSolvedRows_BitwiseMatchFortranTrace()
    {
        Alpha0_P12_Blsolv_ForwardRow1_BitwiseMatchesFortranTrace();
        Alpha0_P12_Blsolv_SolvedRow1_BitwiseMatchesFortranTrace();
    }

    [Fact]
    // Legacy mapping: xsolve.f BLSOLV forward-elimination packet for the first traced row.
    // Difference from legacy: The managed port compares the normalized `vdel_fwd` packet directly.
    // Decision: Keep the forward row split so the matrix can route broad BLSOLV failures to the earliest owner packet.
    public void Alpha0_P12_Blsolv_ForwardRow1_BitwiseMatchesFortranTrace()
    {
        AssertArrayPacketMatches("vdel_fwd", iv: 1, "BLSOLV forward row iv=1");
    }

    [Fact]
    // Legacy mapping: xsolve.f BLSOLV backward/solved packet for the first traced row.
    // Difference from legacy: The managed port still emits the historical `vdel_sol` name even though this is the back-substitution packet family.
    // Decision: Keep the solved-row split so broad BLSOLV failures can distinguish forward elimination from the later solve packet.
    public void Alpha0_P12_Blsolv_SolvedRow1_BitwiseMatchesFortranTrace()
    {
        AssertArrayPacketMatches("vdel_sol", iv: 1, "BLSOLV solved row iv=1");
    }

    private static void AssertArrayPacketMatches(string name, int iv, string blockDescription)
    {
        ParityTraceRecord reference = ParityTraceLoader.ReadMatching(
                ReferencePath,
                record => record.Kind == "array" &&
                          record.Scope == "BLSOLV" &&
                          record.Name == name &&
                          HasExactIv(record, iv))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managed = ParityTraceLoader.ReadMatching(
                ManagedPath,
                record => record.Kind == "array" &&
                          record.Scope == "BLSOLV" &&
                          record.Name == name &&
                          HasExactIv(record, iv))
            .OrderBy(static record => record.Sequence)
            .First();

        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields: new[]
            {
                new FieldExpectation("data.iv")
            },
            outputFields: new[]
            {
                new FieldExpectation("values[0]"),
                new FieldExpectation("values[1]"),
                new FieldExpectation("values[2]")
            },
            blockDescription: blockDescription);
    }

    private static bool HasExactIv(ParityTraceRecord record, int iv)
    {
        return record.TryGetDataField("iv", out System.Text.Json.JsonElement dataElement) &&
               dataElement.ValueKind == System.Text.Json.JsonValueKind.Number &&
               dataElement.GetInt32() == iv;
    }

    private static string GetReferencePath()
    {
        string directory = Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            "n0012_re1e6_a0_p12_n9_full");
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }

    private static string GetManagedPath()
    {
        const string caseId = "n0012_re1e6_a0_p12_n9_full";
        FortranReferenceCases.EnsureManagedArtifacts(caseId);
        if (!ManagedDirectoryContainsRequiredPackets(caseId))
        {
            RefreshManagedArtifactsForBlsolv(caseId);
        }
        string path = GetLatestManagedTracePathContainingRequiredPackets(caseId);
        Assert.True(File.Exists(path), $"Managed BLSOLV trace missing: {path}");
        return path;
    }

    private static bool ManagedDirectoryContainsRequiredPackets(string caseId)
    {
        return TryGetLatestBoundedManagedTracePathContainingRequiredPackets(caseId) is not null;
    }

    private static string GetLatestManagedTracePathContainingRequiredPackets(string caseId)
    {
        string? latestMatching = TryGetLatestBoundedManagedTracePathContainingRequiredPackets(caseId);
        if (latestMatching is not null)
        {
            return latestMatching;
        }

        return FortranReferenceCases.GetManagedTracePath(caseId);
    }

    private static string? TryGetLatestBoundedManagedTracePathContainingRequiredPackets(string caseId)
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
            .FirstOrDefault(ContainsRequiredBlsolvPackets);
    }

    private static void RefreshManagedArtifactsForBlsolv(string caseId)
    {
        using var scope = new TemporaryEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["XFOILSHARP_FORCE_PARITY_REFRESH"] = "1",
                ["XFOIL_TRACE_KIND_ALLOW"] = "array",
                ["XFOIL_TRACE_SCOPE_ALLOW"] = "BLSOLV"
            });
        FortranReferenceCases.RefreshManagedArtifacts(caseId);
    }

    private static bool ContainsRequiredBlsolvPackets(string path)
    {
        bool hasForward = false;
        bool hasSolved = false;
        foreach (string line in File.ReadLines(path))
        {
            if (!line.Contains("\"scope\":\"BLSOLV\"", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains("\"name\":\"vdel_fwd\"", StringComparison.Ordinal))
            {
                hasForward = true;
            }
            else if (line.Contains("\"name\":\"vdel_sol\"", StringComparison.Ordinal))
            {
                hasSolved = true;
            }

            if (hasForward && hasSolved)
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
