using System.IO;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: STFIND stagnation interpolation
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_stag_ref1/reference_trace*.jsonl
// Role in port: Verifies the managed stagnation interpolation packet against the authoritative Fortran alpha-10 panel-80 trace.
// Differences: The managed port resolves existing trace artifacts instead of rediscovering the packet through a broad full-run debug session each time.
// Decision: Keep the focused trace-backed oracle because it is the cheapest trustworthy regression for the stagnation-point interpolation boundary.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityMicro")]
public sealed class StagnationInterpolationMicroParityTests
{
    private static readonly string ReferencePath = GetReferencePath();
    private static readonly string ManagedPath = GetManagedPath();

    [Fact]
    // Legacy mapping: xpanel.f STFIND single-precision sign-change interpolation on the alpha-10 P80 stagnation window.
    // Difference from legacy: The port checks the focused packet directly from stored traces rather than waiting for a larger viscous-run mismatch report.
    // Decision: Keep the packet-level regression because it is stable, fast, and directly tied to the raw-bit oracle we want in the matrix.
    public void Alpha10_P80_StagnationInterpolation_BitwiseMatchesFortranTrace()
    {
        var selector = new TraceEventSelector(
            Kind: "stagnation_interpolation",
            TagFilters: new System.Collections.Generic.Dictionary<string, object?>
            {
                ["index"] = 47,
                ["gammaLeft"] = 0.09568557888269424,
                ["gammaRight"] = -0.09403208643198013
            });

        var (reference, managed) = ParityTraceAligner.AlignSingle(ReferencePath, ManagedPath, selector);
        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields: new[]
            {
                new FieldExpectation("data.index", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.gammaLeft"),
                new FieldExpectation("data.gammaRight"),
                new FieldExpectation("data.dgam"),
                new FieldExpectation("data.ds"),
                new FieldExpectation("data.panelArcLeft"),
                new FieldExpectation("data.panelArcRight")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.usedLeftNode", NumericComparisonMode.LogicalEquivalent)
            },
            blockDescription: "Alpha-10 P80 stagnation interpolation");
    }

    private static string GetReferencePath()
    {
        string directory = Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            "alpha10_p80_stag_ref1");
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }

    private static string GetManagedPath()
    {
        const string caseId = "n0012_re1e6_a10_p80_stagnation";
        FortranReferenceCases.EnsureManagedArtifacts(caseId);
        string path = FortranReferenceCases.GetManagedTracePath(caseId);
        Assert.True(File.Exists(path), $"Managed stagnation trace missing: {path}");
        return path;
    }
}
