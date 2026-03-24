using System.Collections.Generic;
using System.IO;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: QDCALC wake-panel state trace lineage
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_wakepanel_psilin_ref/reference_trace*.jsonl
// Role in port: Verifies the managed wake-panel state packets against the authoritative Fortran alpha-10 panel-80 wake-panel trace.
// Differences: The managed port resolves existing focused trace artifacts instead of rediscovering this producer boundary through a larger wake-column replay.
// Decision: Keep the packet-sequence oracle because it is the cheapest promotable proof for the inviscid wake-coupling producer path.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityMicro")]
public sealed class WakePanelStateMicroParityTests
{
    private static readonly string ReferencePath = GetReferencePath();
    private static readonly string ManagedPath = GetManagedPath();

    [Fact]
    // Legacy mapping: xpanel.f QDCALC wake-panel state packets on the alpha-10 P80 wake march.
    // Difference from legacy: The port checks the full traced packet sequence directly from stored artifacts rather than via a downstream wake-column consumer.
    // Decision: Keep the sequence-level regression because it is stable, fast, and isolates the inviscid wake-coupling producer boundary.
    public void Alpha10_P80_WakePanelStateSequence_BitwiseMatchesFortranTrace()
    {
        IReadOnlyList<ParityTraceRecord> reference = ParityTraceLoader.ReadMatching(
            ReferencePath,
            record => record.Kind == "wake_panel_state");
        IReadOnlyList<ParityTraceRecord> managed = ParityTraceLoader.ReadMatching(
            ManagedPath,
            record => record.Kind == "wake_panel_state");

        Assert.NotEmpty(reference);
        Assert.True(
            managed.Count >= reference.Count,
            $"Managed wake-panel trace is shorter than reference wake-panel trace: managed={managed.Count} reference={reference.Count}");

        for (int i = 0; i < reference.Count; i++)
        {
            FortranParityAssert.AssertInputsThenOutputs(
                reference[i],
                managed[i],
                inputFields: new[]
                {
                    new FieldExpectation("data.index", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.fieldIndex", NumericComparisonMode.ExactDouble),
                    new FieldExpectation("data.x"),
                    new FieldExpectation("data.y")
                },
                outputFields: new[]
                {
                    new FieldExpectation("data.psiX"),
                    new FieldExpectation("data.psiY"),
                    new FieldExpectation("data.magnitude"),
                    new FieldExpectation("data.usedFallback", NumericComparisonMode.LogicalEquivalent),
                    new FieldExpectation("data.panelAngle"),
                    new FieldExpectation("data.currentNormalX"),
                    new FieldExpectation("data.currentNormalY"),
                    new FieldExpectation("data.nextNormalX"),
                    new FieldExpectation("data.nextNormalY")
                },
                blockDescription: $"Alpha-10 P80 wake panel state {i + 1}");
        }
    }

    private static string GetReferencePath()
    {
        string directory = Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            "alpha10_p80_wakepanel_psilin_ref");
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }

    private static string GetManagedPath()
    {
        const string caseId = "n0012_re1e6_a10_p80_wakepanelstate";
        FortranReferenceCases.EnsureManagedArtifacts(caseId);
        string path = FortranReferenceCases.GetManagedTracePath(caseId);
        Assert.True(File.Exists(path), $"Managed wake-panel-state trace missing: {path}");
        return path;
    }
}
