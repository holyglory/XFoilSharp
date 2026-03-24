using System.IO;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: XYWAKE first wake-spacing setup
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_wakespacing_ref/reference_trace*.jsonl
// Role in port: Verifies the managed first wake-spacing packet against the authoritative Fortran alpha-10 panel-80 trace.
// Differences: The managed port resolves an existing focused trace artifact instead of rediscovering the spacing packet through a broad wake-geometry run.
// Decision: Keep the single-packet oracle because it is the cheapest trustworthy regression for the wake-spacing producer boundary.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityMicro")]
public sealed class WakeSpacingMicroParityTests
{
    private static readonly string ReferencePath = GetReferencePath();
    private static readonly string ManagedPath = GetManagedPath();

    [Fact]
    // Legacy mapping: xpanel.f XYWAKE initial wake-spacing packet on the alpha-10 P80 wake setup.
    // Difference from legacy: The port checks the packet directly from stored traces rather than waiting for a larger wake-geometry consumer to complain.
    // Decision: Keep the packet-level regression because it cleanly isolates the first wake-spacing producer mismatch.
    public void Alpha10_P80_WakeSpacingInput_BitwiseMatchesFortranTrace()
    {
        var selector = new TraceEventSelector(
            Kind: "wake_spacing_input",
            TagFilters: new System.Collections.Generic.Dictionary<string, object?>
            {
                ["upperStart"] = 0.0,
                ["upperEnd"] = 0.0183873251080513,
                ["lowerStart"] = 2.020765781402588,
                ["lowerEnd"] = 2.0391530990600586
            });

        var (reference, managed) = ParityTraceAligner.AlignSingle(ReferencePath, ManagedPath, selector);
        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields: new[]
            {
                new FieldExpectation("data.upperStart"),
                new FieldExpectation("data.upperEnd"),
                new FieldExpectation("data.lowerStart"),
                new FieldExpectation("data.lowerEnd")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.upperDelta"),
                new FieldExpectation("data.lowerDelta"),
                new FieldExpectation("data.ds1")
            },
            blockDescription: "Alpha-10 P80 wake spacing");
    }

    private static string GetReferencePath()
    {
        string directory = Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            "alpha10_p80_wakespacing_ref");
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }

    private static string GetManagedPath()
    {
        const string caseId = "n0012_re1e6_a10_p80_wakespacing_v2";
        FortranReferenceCases.EnsureManagedArtifacts(caseId);
        string path = FortranReferenceCases.GetManagedTracePath(caseId);
        Assert.True(File.Exists(path), $"Managed wake-spacing trace missing: {path}");
        return path;
    }
}
