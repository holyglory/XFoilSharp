using System.Collections.Generic;
using System.IO;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLVAR/BLDIF/MRCHUE alpha-10 parity chain
// Secondary legacy source: tools/fortran-debug numbered alpha-10 trace artifacts
// Role in port: Focused block and cycle parity regressions for the current alpha-10 viscous mismatch chain.
// Differences: The managed tests compare numbered trace artifacts instead of rerunning broad parity suites; this narrows the proof loop to one block family at a time.
// Decision: Keep these focused regressions because they let parity work prove the first live mismatch without paying the full polar-sweep audit cost on every iteration.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class Alpha10ViscousParityTests
{
    [Fact]
    // Legacy mapping: xblsys.f BLDIF secondary station-2 snapshot feeding the station-29 transition seed path.
    // Difference from legacy: The test pins the exact numbered trace record rather than stepping through the full transition interval manually. Decision: Keep the focused producer check because it proves the upstream BLVAR station state already matches before we patch later DI assembly.
    public void Alpha10_BldifSecondaryStation2_SeedProducer_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_secondary_station",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["station"] = 2,
                    ["hs"] = 1.5262064933776855,
                    ["us"] = 0.08296453952789307,
                    ["cf"] = 0.0004375661665108055,
                    ["di"] = 0.014306314289569855
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.ityp", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.station", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.hc"),
                new FieldExpectation("data.hs"),
                new FieldExpectation("data.hsHk"),
                new FieldExpectation("data.hkD"),
                new FieldExpectation("data.hsD"),
                new FieldExpectation("data.hsT"),
                new FieldExpectation("data.us"),
                new FieldExpectation("data.usT"),
                new FieldExpectation("data.hkU"),
                new FieldExpectation("data.rtT"),
                new FieldExpectation("data.rtU"),
                new FieldExpectation("data.cq")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.cf"),
                new FieldExpectation("data.cfU"),
                new FieldExpectation("data.cfT"),
                new FieldExpectation("data.cfD"),
                new FieldExpectation("data.cfMs"),
                new FieldExpectation("data.cfmU"),
                new FieldExpectation("data.cfmT"),
                new FieldExpectation("data.cfmD"),
                new FieldExpectation("data.cfmMs"),
                new FieldExpectation("data.di"),
                new FieldExpectation("data.diT"),
                new FieldExpectation("data.de")
            },
            blockDescription: "Alpha-10 BLDIF secondary station-2 state (station-29 seed producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f DIL hk>4 laminar dissipation branch feeding the later side-1 laminar seed.
    // Difference from legacy: The regression pins the exact numbered alpha-10 laminar producer record instead of inferring the DIL staging drift from the downstream seed-system row. Decision: Keep the focused record check because it proves whether the parity branch reproduces the native hk>4 DIL bit pattern before BLDIF assembles row13.
    public void Alpha10_BldifSecondaryStation2_LaminarSeedProducer_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_secondary_station",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 1,
                    ["station"] = 2,
                    ["hs"] = 1.5434763431549072,
                    ["us"] = -0.11121433228254318,
                    ["cf"] = -0.00032954648486338556,
                    ["de"] = 0.0008210661471821368
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.ityp", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.station", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.hc"),
                new FieldExpectation("data.hs"),
                new FieldExpectation("data.hsHk"),
                new FieldExpectation("data.hkD"),
                new FieldExpectation("data.hsD"),
                new FieldExpectation("data.hsT"),
                new FieldExpectation("data.us"),
                new FieldExpectation("data.usT"),
                new FieldExpectation("data.hkU"),
                new FieldExpectation("data.rtT"),
                new FieldExpectation("data.rtU"),
                new FieldExpectation("data.cq"),
                new FieldExpectation("data.cf"),
                new FieldExpectation("data.cfU"),
                new FieldExpectation("data.cfT"),
                new FieldExpectation("data.cfD"),
                new FieldExpectation("data.cfMs"),
                new FieldExpectation("data.cfmU"),
                new FieldExpectation("data.cfmT"),
                new FieldExpectation("data.cfmD"),
                new FieldExpectation("data.cfmMs"),
                new FieldExpectation("data.de")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.di"),
                new FieldExpectation("data.diT")
            },
            blockDescription: "Alpha-10 BLDIF secondary station-2 state (laminar hk>4 DIL producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f BLDIF eq1 D-derivative row assembly feeding the station-29 transition seed path.
    // Difference from legacy: The regression targets the one numbered producer record that feeds the failing row33 chain instead of rerunning the whole seed solve to rediscover it. Decision: Keep the focused row check because it proves whether the derivative producers already match before blaming BLVAR DI staging.
    public void Alpha10_BldifEq1DTerms_SeedProducer_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_eq1_d_terms",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["zD2"] = 0.15715432167053223,
                    ["upwD2"] = -1191.589111328125,
                    ["us2D2"] = -529.971435546875,
                    ["cf2D2"] = -3.9483609199523926,
                    ["hk2D2"] = 4630.7138671875
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
            blockDescription: "Alpha-10 BLDIF eq1 D terms (station-29 seed producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f BLDIF common preamble supplying the shared logarithmic and upwinding state for the station-29 seed path.
    // Difference from legacy: The test isolates the preamble state directly from the numbered traces rather than rediscovering it through row-level failures. Decision: Keep the shared preamble check because `zUpw` cannot be trusted until this common state matches exactly.
    public void Alpha10_BldifCommon_SeedProducer_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_common",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["cfm"] = -0.0001948491408256814,
                    ["upw"] = 0.8158127069473267,
                    ["xlog"] = 0.053956564515829086
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
            blockDescription: "Alpha-10 BLDIF common preamble (station-29 seed producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f BLDIF UPW derivative chain feeding the eq1/eq3 Jacobian rows for the station-29 seed path.
    // Difference from legacy: The regression checks the shared UPW chain directly instead of inferring it from later row mismatches. Decision: Keep the producer check because it cleanly separates UPW drift from the downstream `zUpw` combine.
    public void Alpha10_BldifUpwTerms_SeedProducer_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_upw_terms",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["hk1"] = 8.761836051940918,
                    ["hk2"] = 3.0162277221679688,
                    ["upwD1"] = 370.8720703125,
                    ["upwD2"] = -1191.589111328125
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
            blockDescription: "Alpha-10 BLDIF UPW terms (station-29 seed producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f BLDIF turbulent eq1 z_upw combine feeding the station-29 seed D-row.
    // Difference from legacy: The regression isolates the four-term z_upw accumulation instead of rediscovering it through the later row13/row23 failures. Decision: Keep the combine-level check because it proves whether the remaining drift is in a producer term or only in the final accumulation order.
    public void Alpha10_BldifZUpwTerms_SeedProducer_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_z_upw_terms",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["zCqA"] = 0.031011948361992836,
                    ["cqDelta"] = -0.034115634858608246,
                    ["sDelta"] = -0.031322181224823,
                    ["zCfA"] = 0.009617337025702,
                    ["hkDelta"] = -5.745608329772949
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.ityp", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.zCqA"),
                new FieldExpectation("data.cqDelta"),
                new FieldExpectation("data.zSa"),
                new FieldExpectation("data.sDelta"),
                new FieldExpectation("data.zCfA"),
                new FieldExpectation("data.cfDelta"),
                new FieldExpectation("data.zHkA"),
                new FieldExpectation("data.hkDelta")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.cqTerm"),
                new FieldExpectation("data.sTerm"),
                new FieldExpectation("data.cfTerm"),
                new FieldExpectation("data.hkTerm"),
                new FieldExpectation("data.sum12"),
                new FieldExpectation("data.sum123"),
                new FieldExpectation("data.zUpw")
            },
            blockDescription: "Alpha-10 BLDIF zUpw terms (station-29 seed producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f BLVAR turbulent DI low-Hk correction and outer-layer derivative accumulation.
    // Difference from legacy: The regression reads the latest numbered alpha-10 traces rather than stepping through the full viscous sweep manually. Decision: Keep the block test because it pins the first surviving row33 producer mismatch.
    public void Alpha10_BlvarTurbulentDiTerms_Station29SeedProducer_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "blvar_turbulent_di_terms",
                TagFilters: new Dictionary<string, object?>
                {
                    ["s"] = 0.1080101728439331,
                    ["hk"] = 3.0162277221679688,
                    ["hs"] = 1.5262064933776855,
                    ["us"] = 0.08296453952789307,
                    ["rt"] = 473.9051513671875
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.s"),
                new FieldExpectation("data.hk"),
                new FieldExpectation("data.hs"),
                new FieldExpectation("data.us"),
                new FieldExpectation("data.rt"),
                new FieldExpectation("data.cf2t"),
                new FieldExpectation("data.cf2tHk"),
                new FieldExpectation("data.cf2tRt"),
                new FieldExpectation("data.cf2tM"),
                new FieldExpectation("data.cf2tD"),
                new FieldExpectation("data.diWallRaw"),
                new FieldExpectation("data.diWallHs"),
                new FieldExpectation("data.diWallUs"),
                new FieldExpectation("data.diWallCf"),
                new FieldExpectation("data.grt"),
                new FieldExpectation("data.hmin"),
                new FieldExpectation("data.hmRt"),
                new FieldExpectation("data.fl"),
                new FieldExpectation("data.dfac"),
                new FieldExpectation("data.dd"),
                new FieldExpectation("data.ddHs"),
                new FieldExpectation("data.ddUs"),
                new FieldExpectation("data.ddl"),
                new FieldExpectation("data.ddlHs"),
                new FieldExpectation("data.ddlUs"),
                new FieldExpectation("data.ddlRt"),
                new FieldExpectation("data.dil"),
                new FieldExpectation("data.dilHk"),
                new FieldExpectation("data.dilRt"),
                new FieldExpectation("data.usedLaminar", NumericComparisonMode.LogicalEquivalent)
            },
            outputFields: new[]
            {
                new FieldExpectation("data.diWallDPreDfac"),
                new FieldExpectation("data.dfHk"),
                new FieldExpectation("data.dfRt"),
                new FieldExpectation("data.dfTermD"),
                new FieldExpectation("data.diWallDPostDfac"),
                new FieldExpectation("data.ddD"),
                new FieldExpectation("data.ddlD"),
                new FieldExpectation("data.finalDi"),
                new FieldExpectation("data.finalDiD")
            },
            blockDescription: "Alpha-10 BLVAR turbulent DI terms (station-29 seed producer)");
    }

    [Fact]
    // Legacy mapping: xblsys.f BLDIF eq3 D2 row assembly feeding TRDIF BT2(3,3).
    // Difference from legacy: The test compares the isolated row33 producer event rather than rechecking the whole transition interval. Decision: Keep the focused regression because it proves whether the DI-chain fix really propagates into BLDIF.
    public void Alpha10_BldifEq3D2Terms_Station29SeedProducer_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "bldif_eq3_d2_terms",
                TagFilters: new Dictionary<string, object?>
                {
                    ["ityp"] = 2,
                    ["xot1"] = 766.4983520507812,
                    ["xot2"] = 355.1582336425781,
                    ["cf1"] = -0.00021994256530888379,
                    ["cf2"] = 0.0004375661665108055,
                    ["di1"] = 0.028971588239073753,
                    ["di2"] = 0.014306314289569855,
                    ["upwD"] = -1191.589111328125
                }),
            inputFields: new[]
            {
                new FieldExpectation("data.ityp", NumericComparisonMode.ExactDouble),
                new FieldExpectation("data.baseHs"),
                new FieldExpectation("data.baseCf"),
                new FieldExpectation("data.extraH"),
                new FieldExpectation("data.xot1"),
                new FieldExpectation("data.xot2"),
                new FieldExpectation("data.cf1"),
                new FieldExpectation("data.cf2"),
                new FieldExpectation("data.di1"),
                new FieldExpectation("data.di2"),
                new FieldExpectation("data.zCfx"),
                new FieldExpectation("data.zDix"),
                new FieldExpectation("data.cfxUpw"),
                new FieldExpectation("data.dixUpw"),
                new FieldExpectation("data.zUpw"),
                new FieldExpectation("data.upwD")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.baseDi"),
                new FieldExpectation("data.extraUpw"),
                new FieldExpectation("data.baseStored33"),
                new FieldExpectation("data.row33")
            },
            blockDescription: "Alpha-10 BLDIF eq3 D2 terms (station-29 seed producer)");
    }

    [Fact]
    // Legacy mapping: xbl.f MRCHUE transition-seed local 4x4 system for side 1 station 29 iteration 8.
    // Difference from legacy: The managed regression asserts the exact numbered trace row instead of depending on a long-form diagnostic dump. Decision: Keep the cycle-level check because it proves the block fixes reach the actual seed solve.
    public void Alpha10_TransitionSeedSystem_Station29Iteration8_MatchFortran()
    {
        RunAlpha10ParityBlock(
            new TraceEventSelector(
                Kind: "transition_seed_system",
                TagFilters: new Dictionary<string, object?>
                {
                    ["side"] = 1,
                    ["station"] = 29,
                    ["iteration"] = 8,
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
                new FieldExpectation("data.htarg"),
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
                new FieldExpectation("data.row32")
            },
            outputFields: new[]
            {
                new FieldExpectation("data.row33"),
                new FieldExpectation("data.row34")
            },
            blockDescription: "Alpha-10 transition seed system (side=1 station=29 iteration=8)");
    }

    private static void RunAlpha10ParityBlock(
        TraceEventSelector selector,
        FieldExpectation[] inputFields,
        FieldExpectation[] outputFields,
        string blockDescription)
    {
        string referencePath = FocusedAlpha10TraceArtifacts.GetLatestReferenceTracePath(selector);
        string managedPath = FocusedAlpha10TraceArtifacts.GetLatestManagedSolverTracePath(selector);

        Assert.True(File.Exists(referencePath), $"Fortran alpha-10 reference trace missing: {referencePath}");
        Assert.True(File.Exists(managedPath), $"Managed alpha-10 solver trace missing: {managedPath}");

        var (reference, managed) = ParityTraceAligner.AlignSingle(referencePath, managedPath, selector);
        FortranParityAssert.AssertInputsThenOutputs(
            reference,
            managed,
            inputFields,
            outputFields,
            blockDescription);
    }
}
