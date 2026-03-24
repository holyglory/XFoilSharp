using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLSYS similarity precombine row assembly
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_eq2bundle_ref2/reference_trace*.jsonl
// Role in port: Replays the similarity precombine row packet family directly from captured traces so the managed BLSYS combine order can be checked without the wider seed march.
// Differences: The harness is managed-only infrastructure, but it compares the traced row-combine payload against authoritative Fortran packets with the same field contract.
// Decision: Keep the packet-level oracle because it isolates the similarity combine point and gives the matrix another trace-backed rig that is independent of the station-16 eq1 residual frontier.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class SimilaritySeedPrecombineRowsMicroParityTests
{
    private const string CaseId = "n0012_re1e6_a10_p80";

    [Fact]
    public void Alpha10_P80_SimilarityPrecombineRows_BitwiseMatchesFortranTraceVectors()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_eq2bundle_ref2"));

        FortranReferenceCases.EnsureManagedArtifacts(CaseId);
        string managedPath = FortranReferenceCases.GetManagedTracePath(CaseId);

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "simi_precombine_rows" && record.Scope == "BLSYS")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = ParityTraceLoader.ReadMatching(
                managedPath,
                static record => record.Kind == "simi_precombine_rows" && record.Scope == "BLSYS")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.NotEmpty(referenceRecords);
        Assert.Equal(referenceRecords.Count, managedRecords.Count);

        for (int index = 0; index < referenceRecords.Count; index++)
        {
            ParityTraceRecord reference = referenceRecords[index];
            ParityTraceRecord managed = managedRecords[index];

            FortranParityAssert.AssertInputsThenOutputs(
                reference,
                managed,
                inputFields: new[]
                {
                    new FieldExpectation("data.side", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.station", NumericComparisonMode.ExactDouble)
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.eq2Vs1_22"),
                    new FieldExpectation("data.eq2Vs2_22"),
                    new FieldExpectation("data.eq2Combined22"),
                    new FieldExpectation("data.eq2Vs1_24"),
                    new FieldExpectation("data.eq2Vs2_24"),
                    new FieldExpectation("data.eq2Combined24"),
                    new FieldExpectation("data.eq3Vs1_32"),
                    new FieldExpectation("data.eq3Vs2_32"),
                    new FieldExpectation("data.eq3Combined32"),
                    new FieldExpectation("data.eq3Vs1_33"),
                    new FieldExpectation("data.eq3Vs2_33"),
                    new FieldExpectation("data.eq3Combined33")
                },
                blockDescription: $"alpha10 p80 similarity precombine rows #{index + 1}");
        }
    }

    private static string GetLatestTracePath(string directory)
    {
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }
}
