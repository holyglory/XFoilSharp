using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: SETBL VSREZ row emission during Newton-system assembly
// Secondary legacy source: tools/fortran-debug/reference/n0012_re1e6_a0_p12_n9_full/reference_trace*.jsonl authoritative full alpha-0 trace
// Role in port: Verifies the leading managed VSREZ array packets against the existing authoritative Fortran trace without requiring new solver instrumentation.
// Differences: The Fortran JSON trace omits station metadata on these array packets, so the test aligns the earliest packets by deterministic full-trace order and validates the managed side/station/iv selector fields separately.
// Decision: Keep this narrow oracle because it promotes VSREZ into a dedicated packet-level test surface while staying inside the current full-trace artifact contract.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityMicro")]
public sealed class VsrezUpstreamMicroParityTests
{
    private const string CaseId = "n0012_re1e6_a0_p12_n9_full";
    private const long MaxPreferredManagedTraceBytes = 256L * 1024L * 1024L;
    private static readonly Regex VersionedTraceCounterRegex = new(
        @"^csharp_trace\.(?<counter>\d+)\.jsonl$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string ReferencePath = FortranReferenceCases.GetReferenceTracePath(CaseId);

    [Fact]
    // Legacy mapping: xbl.f SETBL first upper-side VSREZ row in the authoritative reduced-panel alpha-0 run.
    // Difference from legacy: The port compares structured array packets instead of parsing unit-50 text dumps.
    // Decision: Keep this first packet oracle because it provides a cheap producer-side tripwire for the Newton residual row family.
    public void Alpha0_P12_Vsrez_FirstUpperPacket_BitwiseMatchesFortranTrace()
    {
        AssertOrderedPacketMatches(
            ordinal: 0,
            expectedManagedSide: 1,
            expectedManagedStation: 2,
            expectedManagedIv: 1,
            blockDescription: "VSREZ leading upper packet");
    }

    [Fact]
    // Legacy mapping: xbl.f SETBL earliest live upper-side VSREZ owner on the authoritative reduced-panel alpha-0 run.
    // Difference from legacy: The port still aligns by ordered packet index because the Fortran array trace has no explicit station tags.
    // Decision: Keep this second upper packet oracle because the broader parity notes track it as the earliest remaining alpha-0 viscous producer boundary.
    public void Alpha0_P12_Vsrez_SecondUpperPacket_BitwiseMatchesFortranTrace()
    {
        AssertOrderedPacketMatches(
            ordinal: 1,
            expectedManagedSide: 1,
            expectedManagedStation: 3,
            expectedManagedIv: 2,
            blockDescription: "VSREZ second upper packet");
    }

    [Fact]
    // Legacy mapping: xbl.f SETBL first lower-side VSREZ row in the authoritative reduced-panel alpha-0 run.
    // Difference from legacy: The Fortran packet still has no station tags, so the alignment uses the seventh ordered VSREZ record in the full trace and confirms the managed selector payload explicitly.
    // Decision: Keep this first-lower packet oracle because it proves the side handoff order without requiring a broader dump parser.
    public void Alpha0_P12_Vsrez_FirstLowerPacket_BitwiseMatchesFortranTrace()
    {
        AssertOrderedPacketMatches(
            ordinal: 6,
            expectedManagedSide: 2,
            expectedManagedStation: 2,
            expectedManagedIv: 7,
            blockDescription: "VSREZ leading lower packet");
    }

    private static void AssertOrderedPacketMatches(
        int ordinal,
        int expectedManagedSide,
        int expectedManagedStation,
        int expectedManagedIv,
        string blockDescription)
    {
        ParityTraceRecord reference = GetOrderedVsrezPackets(ReferencePath)
            .ElementAt(ordinal);
        ParityTraceRecord managed = GetOrderedVsrezPackets(GetManagedPath())
            .ElementAt(ordinal);

        AssertExactDataInt(managed, "side", expectedManagedSide, blockDescription);
        AssertExactDataInt(managed, "station", expectedManagedStation, blockDescription);
        AssertExactDataInt(managed, "iv", expectedManagedIv, blockDescription);

        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields: Array.Empty<FieldExpectation>(),
            outputFields: new[]
            {
                new FieldExpectation("values[0]"),
                new FieldExpectation("values[1]"),
                new FieldExpectation("values[2]")
            },
            blockDescription: blockDescription);
    }

    private static IOrderedEnumerable<ParityTraceRecord> GetOrderedVsrezPackets(string path)
    {
        return ParityTraceLoader.ReadMatching(
                path,
                static record => record.Kind == "array" && IsVsrezPacket(record))
            .OrderBy(static record => record.Sequence);
    }

    private static bool IsVsrezPacket(ParityTraceRecord record)
    {
        return string.Equals(record.Name, "VSREZ", StringComparison.Ordinal) ||
               string.Equals(record.Name, "vsrez", StringComparison.Ordinal);
    }

    private static void AssertExactDataInt(ParityTraceRecord record, string field, int expected, string blockDescription)
    {
        Assert.True(
            record.TryGetDataField(field, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number,
            $"{blockDescription}: missing numeric data field '{field}'.");
        Assert.Equal(expected, value.GetInt32());
    }

    private static string GetManagedPath()
    {
        FortranReferenceCases.EnsureManagedArtifacts(CaseId);
        string path = BuildFilteredVsrezTracePath();
        Assert.True(File.Exists(path), $"Managed VSREZ trace missing: {path}");
        return path;
    }

    private static string BuildFilteredVsrezTracePath()
    {
        string managedDirectory = FortranReferenceCases.GetManagedDirectory(CaseId);
        string sourcePath = Path.Combine(managedDirectory, "csharp_trace.jsonl");
        string filteredPath = Path.Combine(managedDirectory, "csharp_trace.vsrez.jsonl");

        Assert.True(File.Exists(sourcePath), $"Managed canonical VSREZ source trace missing: {sourcePath}");

        DateTime sourceWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath);
        DateTime filteredWriteTimeUtc = File.Exists(filteredPath)
            ? File.GetLastWriteTimeUtc(filteredPath)
            : DateTime.MinValue;

        if (filteredWriteTimeUtc >= sourceWriteTimeUtc && ContainsVsrezPacket(filteredPath))
        {
            return filteredPath;
        }

        using var reader = new StreamReader(sourcePath);
        using var writer = new StreamWriter(filteredPath, append: false);
        while (reader.ReadLine() is { } line)
        {
            if (line.Contains("\"kind\":\"session_start\"", StringComparison.Ordinal) ||
                line.Contains("\"kind\":\"session_end\"", StringComparison.Ordinal) ||
                line.Contains("\"name\":\"VSREZ\"", StringComparison.Ordinal) ||
                line.Contains("\"name\":\"vsrez\"", StringComparison.Ordinal))
            {
                writer.WriteLine(line);
            }
        }

        return filteredPath;
    }

    private static string GetLatestBoundedVsrezManagedTracePath()
    {
        string managedDirectory = FortranReferenceCases.GetManagedDirectory(CaseId);
        string? latestMatching = Directory
            .EnumerateFiles(managedDirectory, "csharp_trace*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                Counter = TryParseVersionedTraceCounter(path),
                Length = new FileInfo(path).Length
            })
            .Where(candidate => candidate.Counter is not null && candidate.Length <= MaxPreferredManagedTraceBytes)
            .OrderByDescending(candidate => candidate.Counter!.Value)
            .Select(candidate => candidate.Path)
            .FirstOrDefault(ContainsVsrezPacket);

        return latestMatching ?? FortranReferenceCases.GetManagedTracePath(CaseId);
    }

    private static long? TryParseVersionedTraceCounter(string path)
    {
        string fileName = Path.GetFileName(path);
        Match match = VersionedTraceCounterRegex.Match(fileName);
        return match.Success && long.TryParse(match.Groups["counter"].Value, out long counter)
            ? counter
            : null;
    }

    private static bool ContainsVsrezPacket(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.Contains("\"name\":\"VSREZ\"", StringComparison.Ordinal) ||
                line.Contains("\"name\":\"vsrez\"", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
