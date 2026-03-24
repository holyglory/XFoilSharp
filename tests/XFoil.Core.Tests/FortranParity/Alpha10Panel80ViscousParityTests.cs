using System.Collections.Generic;
using System.IO;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLKIN/BLVAR/BLDIF/TRDIF reduced-panel alpha-10 chain
// Secondary legacy source: tools/fortran-debug/reference/n0012_re1e6_a10_p80/reference_trace.*.jsonl
// Role in port: Fast reduced-panel parity ladder for the current viscous mismatch chain.
// Differences: Classic XFoil had no managed block-test harness; this class narrows the proof loop to one small authoritative case instead of the old long alpha-10 solver trace path.
// Decision: Keep the reduced-panel ladder because it gives the parity work a reproducible few-second checkpoint at the current earliest mismatch boundary.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class Alpha10Panel80ViscousParityTests
{
    private const string CaseId = "n0012_re1e6_a10_p80";
    private static readonly string ReferencePath = GetReferencePath();
    private static readonly string ManagedPath = GetManagedPath();

    [Fact]
    // Legacy mapping: xblsys.f BLDIF common preamble for the reduced-panel station-15 third-seed event.
    // Difference from legacy: The managed regression reads the newest numbered P80 artifacts rather than rediscovering the event through a full viscous debug session. Decision: Keep the direct preamble check because the upstream producer search depends on proving this shared state still matches.
    public void Alpha10_P80_BldifCommon_Station15Iteration3_Producer_MatchFortran()
    {
        RunPanel80ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_common",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["cfm"] = -0.00021980341989547014,
                    ["upw"] = 0.5001630783081055,
                    ["xlog"] = 0.056538935750722885
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.ityp", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.cfm")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.upw"),
                new FieldExpectation("data.xlog"),
                new FieldExpectation("data.ulog"),
                new FieldExpectation("data.tlog"),
                new FieldExpectation("data.hlog"),
                new FieldExpectation("data.ddlog")
            },
            blockDescription: "Alpha-10 P80 BLDIF common preamble (station=15 iteration=3 producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f BLDIF UPW derivative chain feeding the reduced-panel station-15 third-seed D-row.
    // Difference from legacy: The regression compares the isolated producer event from the numbered P80 traces instead of waiting for the seed-system failure to expose it. Decision: Keep the focused producer check because this is the first proved live mismatch on the reduced-panel rung.
    public void Alpha10_P80_BldifUpwTerms_Station15Iteration3_Producer_MatchFortran()
    {
        RunPanel80ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_upw_terms",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["hk1"] = 8.315451622009277,
                    ["hk2"] = 7.865179061889648
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.ityp", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.hk1"),
                new FieldExpectation("data.hk2"),
                new FieldExpectation("data.hk1T1"),
                new FieldExpectation("data.hk1D1"),
                new FieldExpectation("data.hk1U1"),
                new FieldExpectation("data.hk1Ms"),
                new FieldExpectation("data.hk2T2"),
                new FieldExpectation("data.hk2D2"),
                new FieldExpectation("data.hk2U2"),
                new FieldExpectation("data.hk2Ms")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.hl"),
                new FieldExpectation("data.hlsq"),
                new FieldExpectation("data.ehh"),
                new FieldExpectation("data.upwHl"),
                new FieldExpectation("data.upwHd"),
                new FieldExpectation("data.upwHk1"),
                new FieldExpectation("data.upwHk2"),
                new FieldExpectation("data.upwT1"),
                new FieldExpectation("data.upwD1"),
                new FieldExpectation("data.upwU1"),
                new FieldExpectation("data.upwT2"),
                new FieldExpectation("data.upwD2"),
                new FieldExpectation("data.upwU2"),
                new FieldExpectation("data.upwMs")
            },
            blockDescription: "Alpha-10 P80 BLDIF UPW terms (station=15 iteration=3 producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f BLDIF eq1 D-row producer immediately downstream of the reduced-panel UPW chain.
    // Difference from legacy: The regression pins the exact numbered P80 producer record instead of re-running the whole transition interval. Decision: Keep the row-level check because it confirms the seed-system mismatch remains downstream of the same producer family.
    public void Alpha10_P80_BldifEq1DTerms_Station15Iteration3_Producer_MatchFortran()
    {
        RunPanel80ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_eq1_d_terms",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["zD1"] = 0.19582046568393707,
                    ["zUpw"] = 0.003334029344841838,
                    ["zDe1"] = 1.1527352333068848,
                    ["zUs1"] = -0.0006096423021517694,
                    ["zCq1"] = 0.017723096534609795,
                    ["zCf1"] = 0.0038971551693975925,
                    ["zHk1"] = -5.017378043703502e-06
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.ityp", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.zD1"),
                new FieldExpectation("data.zUpw"),
                new FieldExpectation("data.upwD1"),
                new FieldExpectation("data.zDe1"),
                new FieldExpectation("data.de1D1"),
                new FieldExpectation("data.zUs1"),
                new FieldExpectation("data.us1D1"),
                new FieldExpectation("data.zCq1"),
                new FieldExpectation("data.cq1D1"),
                new FieldExpectation("data.zCf1"),
                new FieldExpectation("data.cf1D1"),
                new FieldExpectation("data.zHk1"),
                new FieldExpectation("data.hk1D1"),
                new FieldExpectation("data.zD2"),
                new FieldExpectation("data.upwD2"),
                new FieldExpectation("data.zDe2"),
                new FieldExpectation("data.de2D2"),
                new FieldExpectation("data.zUs2"),
                new FieldExpectation("data.us2D2"),
                new FieldExpectation("data.zCq2"),
                new FieldExpectation("data.cq2D2"),
                new FieldExpectation("data.zCf2"),
                new FieldExpectation("data.cf2D2"),
                new FieldExpectation("data.zHk2"),
                new FieldExpectation("data.hk2D2")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.row13BaseTerm"),
                new FieldExpectation("data.row13UpwTerm"),
                new FieldExpectation("data.row13DeTerm"),
                new FieldExpectation("data.row13UsTerm"),
                new FieldExpectation("data.row13Transport"),
                new FieldExpectation("data.row13CqTerm"),
                new FieldExpectation("data.row13CfTerm"),
                new FieldExpectation("data.row13HkTerm"),
                new FieldExpectation("data.row13"),
                new FieldExpectation("data.row23BaseTerm"),
                new FieldExpectation("data.row23UpwTerm"),
                new FieldExpectation("data.row23DeTerm"),
                new FieldExpectation("data.row23UsTerm"),
                new FieldExpectation("data.row23Transport"),
                new FieldExpectation("data.row23CqTerm"),
                new FieldExpectation("data.row23CfTerm"),
                new FieldExpectation("data.row23HkTerm"),
                new FieldExpectation("data.row23")
            },
            blockDescription: "Alpha-10 P80 BLDIF eq1 D terms (station=15 iteration=3 producer)");
    }

    [Fact]
    // Legacy mapping: xbl.f MRCHUE transition-seed local system for the reduced-panel side-1 station-15 third Newton iteration.
    // Difference from legacy: The regression checks the exact numbered P80 seed-system event rather than relying on a monolithic alpha-10 dump. Decision: Keep the cycle-level check because it proves whether upstream producer fixes actually reach the seed solve.
    public void Alpha10_P80_TransitionSeedSystem_Station15Iteration3_MatchFortran()
    {
        RunPanel80ParityBlock(
            new TraceEventSelector(
                Kind: "transition_seed_system",
                TagFilters: new Dictionary<string, object?>
                {
                    ["side"] = 1,
                    ["station"] = 15,
                    ["iteration"] = 3,
                    ["mode"] = "inverse"
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.side", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.station", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.iteration", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.mode", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.xt"),
                new FieldExpectation("data.uei"),
                new FieldExpectation("data.theta"),
                new FieldExpectation("data.dstar"),
                new FieldExpectation("data.ampl"),
                new FieldExpectation("data.ctau"),
                new FieldExpectation("data.hk2"),
                new FieldExpectation("data.hk2_T2"),
                new FieldExpectation("data.hk2_D2"),
                new FieldExpectation("data.hk2_U2"),
                new FieldExpectation("data.htarg")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.residual1"),
                new FieldExpectation("data.residual2"),
                new FieldExpectation("data.residual3"),
                new FieldExpectation("data.row11"),
                new FieldExpectation("data.row12"),
                new FieldExpectation("data.row13"),
                new FieldExpectation("data.row14"),
                new FieldExpectation("data.row21"),
                new FieldExpectation("data.row22"),
                new FieldExpectation("data.row23"),
                new FieldExpectation("data.row24"),
                new FieldExpectation("data.row31"),
                new FieldExpectation("data.row32"),
                new FieldExpectation("data.row33"),
                new FieldExpectation("data.row34")
            },
            blockDescription: "Alpha-10 P80 transition seed system (side=1 station=15 iteration=3)");
    }

    private static string GetReferencePath()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(CaseId);
        Assert.True(File.Exists(referencePath), $"Fortran P80 reference trace missing: {referencePath}");
        return referencePath;
    }

    private static string GetManagedPath()
    {
        FortranReferenceCases.EnsureManagedArtifacts(CaseId);
        string managedPath = FortranReferenceCases.GetManagedTracePath(CaseId);
        Assert.True(File.Exists(managedPath), $"Managed P80 trace missing: {managedPath}");
        return managedPath;
    }

    private static void RunPanel80ParityBlock(
        TraceEventSelector selector,
        FieldExpectation[] inputFields,
        FieldExpectation[] outputFields,
        string blockDescription)
    {
        var (reference, managed) = ParityTraceAligner.AlignSingle(ReferencePath, ManagedPath, selector);
        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields,
            outputFields,
            blockDescription);
    }
}
