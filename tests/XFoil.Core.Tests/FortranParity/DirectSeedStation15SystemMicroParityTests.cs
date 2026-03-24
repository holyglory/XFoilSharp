using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Numerics;
using XFoil.Solver.Models;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: MRCHUE station-15 direct/inverse seed interval
// and f_xfoil/src/xblsys.f :: BLSYS/TRDIF path for the same interval.
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_blsys_inputs_station15_ref/reference_trace.*.jsonl
// and tools/fortran-debug/reference/alpha10_p80_directseed_station16_ref/reference_trace.*.jsonl
// Role in port: Replays the exact station-15 interval inputs into AssembleStationSystem so
// transition/direct-seed parity can be debugged without replaying the whole MRCHUE setup.
// Differences: Harness-only infrastructure; it compares the local managed system matrix against
// authoritative Fortran trace words captured for the same interval inputs.
// Decision: Keep this micro-engine because station-15 is the current proven one-bit frontier in
// the transition/direct-seed chain and local replay is much faster than full pre-newton reruns.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class DirectSeedStation15SystemMicroParityTests
{
    private const string CaseId = "n0012_re1e6_a10_p80";
    private const int ClassicXFoilNacaPointCount = 239;
    private static readonly Lazy<Station15CarryOverrides> CachedStation15CarryOverrides = new(LoadStation15CarryOverrides);
    private static readonly MethodInfo SolveSeedLinearSystemMethod = typeof(ViscousSolverEngine).GetMethod(
        "SolveSeedLinearSystem",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SolveSeedLinearSystem method not found.");
    private static readonly MethodInfo ComputeSeedStepMetricsMethod = typeof(ViscousSolverEngine).GetMethod(
        "ComputeSeedStepMetrics",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ComputeSeedStepMetrics method not found.");
    private static readonly MethodInfo ComputeLegacySeedRelaxationMethod = typeof(ViscousSolverEngine).GetMethod(
        "ComputeLegacySeedRelaxation",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ComputeLegacySeedRelaxation method not found.");
    private static readonly MethodInfo ApplySeedDslimMethod = typeof(ViscousSolverEngine).GetMethod(
        "ApplySeedDslim",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ApplySeedDslim method not found.");

    [Fact]
    public void Alpha10_P80_DirectSeedStation15System_BitwiseMatchesFortranTraceVectors()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        foreach (DirectSeedStation15Vector vector in vectors)
        {
            BoundaryLayerSystemAssembler.BlsysResult result;
            if (vector.Iteration == 4)
            {
                Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;
                result = AssembleVector(
                    vector,
                    includeTraceContext: false,
                    station1KinematicOverride: carryOverrides.Station1Kinematic,
                    station1SecondaryOverride: carryOverrides.Station1Secondary,
                    useDefaultStation1Carry: false);
            }
            else if (vector.Iteration == 5)
            {
                Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;
                DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
                (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
                    LoadTransitionCarriedVector(sourceVector, 5);
                result = AssembleVector(
                    windowVector,
                    includeTraceContext: false,
                    station1KinematicOverride: carryOverrides.Station1Kinematic,
                    station1SecondaryOverride: carryOverrides.Station1Secondary,
                    station2KinematicOverride: carriedStation2Kinematic,
                    useDefaultStation1Carry: false);
            }
            else
            {
                result = AssembleVector(vector, includeTraceContext: false);
            }

            AssertHex(vector.ExpectedHk2Hex, ToHex((float)result.HK2), $"iter={vector.Iteration} hk2");
            AssertHex(vector.ExpectedHk2T2Hex, ToHex((float)result.HK2_T2), $"iter={vector.Iteration} hk2_T2");
            AssertHex(vector.ExpectedHk2D2Hex, ToHex((float)result.HK2_D2), $"iter={vector.Iteration} hk2_D2");

            AssertHex(vector.ExpectedResidual1Hex, ToHex((float)result.Residual[0]), $"iter={vector.Iteration} residual1");
            AssertHex(vector.ExpectedResidual2Hex, ToHex((float)result.Residual[1]), $"iter={vector.Iteration} residual2");
            AssertHex(vector.ExpectedResidual3Hex, ToHex((float)result.Residual[2]), $"iter={vector.Iteration} residual3");

            AssertHex(vector.ExpectedRow11Hex, ToHex((float)result.VS2[0, 0]), $"iter={vector.Iteration} row11");
            AssertHex(vector.ExpectedRow12Hex, ToHex((float)result.VS2[0, 1]), $"iter={vector.Iteration} row12");
            AssertHex(vector.ExpectedRow13Hex, ToHex((float)result.VS2[0, 2]), $"iter={vector.Iteration} row13");
            AssertHex(vector.ExpectedRow14Hex, ToHex((float)result.VS2[0, 3]), $"iter={vector.Iteration} row14");
            AssertHex(vector.ExpectedRow21Hex, ToHex((float)result.VS2[1, 0]), $"iter={vector.Iteration} row21");
            AssertHex(vector.ExpectedRow22Hex, ToHex((float)result.VS2[1, 1]), $"iter={vector.Iteration} row22");
            AssertHex(vector.ExpectedRow23Hex, ToHex((float)result.VS2[1, 2]), $"iter={vector.Iteration} row23");
            AssertHex(vector.ExpectedRow24Hex, ToHex((float)result.VS2[1, 3]), $"iter={vector.Iteration} row24");
            AssertHex(vector.ExpectedRow31Hex, ToHex((float)result.VS2[2, 0]), $"iter={vector.Iteration} row31");
            AssertHex(vector.ExpectedRow32Hex, ToHex((float)result.VS2[2, 1]), $"iter={vector.Iteration} row32");
            AssertHex(vector.ExpectedRow33Hex, ToHex((float)result.VS2[2, 2]), $"iter={vector.Iteration} row33");
            AssertHex(vector.ExpectedRow34Hex, ToHex((float)result.VS2[2, 3]), $"iter={vector.Iteration} row34");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalRows_Iteration5_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
        Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_trdif_rows_iter5_ref"),
            "laminar_seed_system",
            "transition_interval_rows");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "transition_interval_rows")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord referenceRows = referenceRecords
            .Where(static record => record.Kind == "transition_interval_rows")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 5);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            windowVector,
            carryOverrides.Station1Kinematic,
            carryOverrides.Station1Secondary,
            carriedStation2Kinematic,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "transition_interval_rows");
        ParityTraceRecord managedRows = managedRecords.Single(static record => record.Kind == "transition_interval_rows");

        foreach (string field in new[]
                 {
                     "laminarVs2_34",
                     "turbulentVs2_34",
                     "finalVs2_34"
                 })
        {
            AssertFloatFieldHex(referenceRows, managedRows, field, $"iter=5 transition rows");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration4_LiveCarryHandoff_MatchesFullMarchSystem()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        (ParityTraceRecord liveSystem, ParityTraceRecord liveInput) = LoadLiveIterationHandoff(iteration: 4);

        AssertHex(ToHex((float)vector.U2), GetFloatFieldHex(liveInput, "u2"), "iter=4 live carry handoff u2");
        AssertHex(ToHex((float)vector.T2), GetFloatFieldHex(liveInput, "t2"), "iter=4 live carry handoff theta");
        AssertHex(ToHex((float)vector.D2), GetFloatFieldHex(liveInput, "d2"), "iter=4 live carry handoff dstar");
        AssertHex(ToHex((float)vector.S2), GetFloatFieldHex(liveInput, "s2"), "iter=4 live carry handoff ctau");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration4SystemRow11OwnerEq1RowsRow21_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_eq1_rows_station15_iter4_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_eq1_rows")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_eq1_rows");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        ParityTraceRecord managedSystem = managedRecords.Single(
            static record => (record.Kind == "laminar_seed_system" || record.Kind == "transition_seed_system") &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));

        ParityTraceRecord referenceRows = referenceRecords
            .Where(static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedRows = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceRows, managedRows, "row21", "iter=4 system-row11 owner eq1 rows row21");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifUpwTerms_Iteration4_BitwiseMatchFortranTrace()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_bldif_upw_iter4_station15_ref"),
            "laminar_seed_system",
            "bldif_upw_terms");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_upw_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_upw_terms");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        ParityTraceRecord managedSystem = managedRecords.Single(
            static record => (record.Kind == "laminar_seed_system" || record.Kind == "transition_seed_system") &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));

        ParityTraceRecord referenceUpw = referenceRecords
            .Where(record => record.Kind == "bldif_upw_terms" &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedUpw = managedRecords
            .Where(static record => record.Kind == "bldif_upw_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceUpw, managedUpw, "ityp", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1T1", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1D1", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1U1", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1Ms", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2T2", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2D2", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2U2", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2Ms", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hl", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hlsq", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "ehh", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHl", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHd", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHk1", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHk2", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwT1", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwD1", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwU1", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwT2", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwD2", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwU2", "iter=4 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwMs", "iter=4 bldif upw");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration5_RebuiltStation2Carry_MatchesLiveFullMarchSystem()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        DirectSeedStation15Vector referenceIteration5Vector = vectors.Single(static candidate => candidate.Iteration == 5);
        DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
        Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;
        (ParityTraceRecord liveSystem, ParityTraceRecord liveInput) = LoadLiveIterationHandoff(iteration: 5);

        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 5);

        BoundaryLayerSystemAssembler.BlsysResult carriedResult = AssembleVector(
            windowVector,
            includeTraceContext: false,
            station1KinematicOverride: carryOverrides.Station1Kinematic,
            station1SecondaryOverride: carryOverrides.Station1Secondary,
            station2KinematicOverride: carriedStation2Kinematic,
            useDefaultStation1Carry: false);
        BoundaryLayerSystemAssembler.BlsysResult rebuiltResult = AssembleVector(
            windowVector,
            includeTraceContext: false,
            station1KinematicOverride: carryOverrides.Station1Kinematic,
            station1SecondaryOverride: carryOverrides.Station1Secondary,
            station2KinematicOverride: null,
            useDefaultStation1Carry: false);
        var (station2U, _, _) = BoundaryLayerSystemAssembler.ConvertToCompressible(
            windowVector.U2,
            LegacyIncompressibleParityConstants.Tkbl,
            LegacyIncompressibleParityConstants.Qinfbl,
            LegacyIncompressibleParityConstants.TkblMs,
            useLegacyPrecision: true);
        var rebuiltStation2Primary = new BoundaryLayerSystemAssembler.PrimaryStationState
        {
            U = station2U,
            T = windowVector.T2,
            D = windowVector.D2
        };
        BoundaryLayerSystemAssembler.BlsysResult primaryOnlyResult = AssembleVector(
            windowVector,
            includeTraceContext: false,
            station1KinematicOverride: carryOverrides.Station1Kinematic,
            station1SecondaryOverride: carryOverrides.Station1Secondary,
            station2KinematicOverride: null,
            station2PrimaryOverride: rebuiltStation2Primary,
            useDefaultStation1Carry: false);
        BoundaryLayerSystemAssembler.KinematicResult rebuiltStation2Kinematic = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
            station2U,
            windowVector.T2,
            windowVector.D2,
            0.0,
            LegacyIncompressibleParityConstants.Hstinv,
            LegacyIncompressibleParityConstants.HstinvMs,
            LegacyIncompressibleParityConstants.Gm1Bl,
            LegacyIncompressibleParityConstants.Rstbl,
            LegacyIncompressibleParityConstants.RstblMs,
            LegacyIncompressibleParityConstants.Hvrat,
            LegacyIncompressibleParityConstants.Reybl,
            LegacyIncompressibleParityConstants.ReyblRe,
            LegacyIncompressibleParityConstants.ReyblMs,
            useLegacyPrecision: true);
        BoundaryLayerSystemAssembler.BlsysResult rebuiltBothResult = AssembleVector(
            windowVector,
            includeTraceContext: false,
            station1KinematicOverride: carryOverrides.Station1Kinematic,
            station1SecondaryOverride: carryOverrides.Station1Secondary,
            station2KinematicOverride: rebuiltStation2Kinematic,
            station2PrimaryOverride: rebuiltStation2Primary,
            useDefaultStation1Carry: false);

        // This is the live carry handoff boundary: the accepted transition
        // interval must be the last station-15 BLSYS input packet immediately
        // before the emitted iteration-5 system in the full march.
        AssertHex(ToHex((float)referenceIteration5Vector.U2), GetFloatFieldHex(liveInput, "u2"), "iter=5 live carry handoff u2");
        AssertHex(ToHex((float)referenceIteration5Vector.T2), GetFloatFieldHex(liveInput, "t2"), "iter=5 live carry handoff theta");
        AssertHex(ToHex((float)referenceIteration5Vector.D2), GetFloatFieldHex(liveInput, "d2"), "iter=5 live carry handoff dstar");
        AssertHex(ToHex((float)referenceIteration5Vector.S2), GetFloatFieldHex(liveInput, "s2"), "iter=5 live carry handoff ctau");

        Assert.Equal(
            ToHex((float)carriedResult.Residual[0]),
            GetFloatFieldHex(liveSystem, "residual1"));

        AssertHex(GetFloatFieldHex(liveInput, "u2"), ToHex((float)windowVector.U2), "iter=5 live input uei");
        AssertHex(GetFloatFieldHex(liveInput, "t2"), ToHex((float)windowVector.T2), "iter=5 live input theta");
        AssertHex(GetFloatFieldHex(liveInput, "d2"), ToHex((float)windowVector.D2), "iter=5 live input dstar");
        AssertHex(GetFloatFieldHex(liveInput, "s2"), ToHex((float)windowVector.S2), "iter=5 live input ctau");

        string liveResidual1Hex = GetFloatFieldHex(liveSystem, "residual1");
        string rebuiltResidual1Hex = ToHex((float)rebuiltResult.Residual[0]);
        string primaryOnlyResidual1Hex = ToHex((float)primaryOnlyResult.Residual[0]);
        string rebuiltBothResidual1Hex = ToHex((float)rebuiltBothResult.Residual[0]);

        Assert.Equal(liveResidual1Hex, rebuiltResidual1Hex);
        Assert.Equal(liveResidual1Hex, primaryOnlyResidual1Hex);
        Assert.Equal(liveResidual1Hex, rebuiltBothResidual1Hex);
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration5EmittedSystemState_MatchesLiveHandoffInput()
    {
        (ParityTraceRecord liveSystem, ParityTraceRecord liveInput) = LoadLiveIterationHandoff(iteration: 5);

        AssertHex(GetFloatFieldHex(liveInput, "u2"), GetFloatFieldHex(liveSystem, "uei"), "iter=5 emitted system state uei");
        AssertHex(GetFloatFieldHex(liveInput, "t2"), GetFloatFieldHex(liveSystem, "theta"), "iter=5 emitted system state theta");
        AssertHex(GetFloatFieldHex(liveInput, "d2"), GetFloatFieldHex(liveSystem, "dstar"), "iter=5 emitted system state dstar");
        AssertHex(GetFloatFieldHex(liveInput, "s2"), GetFloatFieldHex(liveSystem, "ctau"), "iter=5 emitted system state ctau");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration5LiveHandoff_RemainsStableAfterCarryBootstrap()
    {
        (_, ParityTraceRecord baselineInput) = LoadLiveIterationHandoff(iteration: 5);
        _ = CachedStation15CarryOverrides.Value;
        (_, ParityTraceRecord reloadedInput) = LoadLiveIterationHandoff(iteration: 5);

        AssertHex("0x40132F75", GetFloatFieldHex(baselineInput, "u2"), "iter=5 carry bootstrap baseline u2");
        AssertHex(GetFloatFieldHex(baselineInput, "u2"), GetFloatFieldHex(reloadedInput, "u2"), "iter=5 carry bootstrap live handoff u2");
        AssertHex(GetFloatFieldHex(baselineInput, "t2"), GetFloatFieldHex(reloadedInput, "t2"), "iter=5 carry bootstrap live handoff theta");
        AssertHex(GetFloatFieldHex(baselineInput, "d2"), GetFloatFieldHex(reloadedInput, "d2"), "iter=5 carry bootstrap live handoff dstar");
        AssertHex(GetFloatFieldHex(baselineInput, "s2"), GetFloatFieldHex(reloadedInput, "s2"), "iter=5 carry bootstrap live handoff ctau");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration5LiveHandoff_RemainsStableAfterVectorLoad()
    {
        (_, ParityTraceRecord baselineInput) = LoadLiveIterationHandoff(iteration: 5);
        _ = LoadVectors();
        (_, ParityTraceRecord reloadedInput) = LoadLiveIterationHandoff(iteration: 5);

        AssertHex("0x40132F75", GetFloatFieldHex(baselineInput, "u2"), "iter=5 vector load baseline u2");
        AssertHex(GetFloatFieldHex(baselineInput, "u2"), GetFloatFieldHex(reloadedInput, "u2"), "iter=5 vector load live handoff u2");
        AssertHex(GetFloatFieldHex(baselineInput, "t2"), GetFloatFieldHex(reloadedInput, "t2"), "iter=5 vector load live handoff theta");
        AssertHex(GetFloatFieldHex(baselineInput, "d2"), GetFloatFieldHex(reloadedInput, "d2"), "iter=5 vector load live handoff dstar");
        AssertHex(GetFloatFieldHex(baselineInput, "s2"), GetFloatFieldHex(reloadedInput, "s2"), "iter=5 vector load live handoff ctau");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration5LiveHandoff_RemainsStableAfterVectorLoadAndCarryBootstrap()
    {
        (_, ParityTraceRecord baselineInput) = LoadLiveIterationHandoff(iteration: 5);
        _ = LoadVectors();
        _ = CachedStation15CarryOverrides.Value;
        (_, ParityTraceRecord reloadedInput) = LoadLiveIterationHandoff(iteration: 5);

        AssertHex("0x40132F75", GetFloatFieldHex(baselineInput, "u2"), "iter=5 vector+carry baseline u2");
        AssertHex(GetFloatFieldHex(baselineInput, "u2"), GetFloatFieldHex(reloadedInput, "u2"), "iter=5 vector+carry live handoff u2");
        AssertHex(GetFloatFieldHex(baselineInput, "t2"), GetFloatFieldHex(reloadedInput, "t2"), "iter=5 vector+carry live handoff theta");
        AssertHex(GetFloatFieldHex(baselineInput, "d2"), GetFloatFieldHex(reloadedInput, "d2"), "iter=5 vector+carry live handoff dstar");
        AssertHex(GetFloatFieldHex(baselineInput, "s2"), GetFloatFieldHex(reloadedInput, "s2"), "iter=5 vector+carry live handoff ctau");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_KinematicResult_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 3);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_kinematic_iter3_ref"));

        IReadOnlyList<ParityTraceRecord> referenceKinematicCandidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "kinematic_result")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.NotEmpty(referenceKinematicCandidates);

        string expectedU2Hex = ToHex((float)vector.U2);
        string expectedT2Hex = ToHex((float)vector.T2);
        string expectedD2Hex = ToHex((float)vector.D2);

        ParityTraceRecord referenceKinematic = referenceKinematicCandidates
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedU2Hex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedT2Hex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedD2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            "kinematic_result");
        ParityTraceRecord managedKinematic = managedRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedU2Hex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedT2Hex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedD2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "m2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "m2_u2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "m2_ms", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "r2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "r2_u2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "r2_ms", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_u2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_t2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_d2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_ms", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2_he", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2MsReyblTerm", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2MsHeTerm", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2_ms", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_u2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_t2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_ms", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_re", $"iter={vector.Iteration} kinematic");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_KinematicResult_Iteration4_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_iter4_vector_primary_ref"));

        IReadOnlyList<ParityTraceRecord> referenceKinematicCandidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "kinematic_result")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.NotEmpty(referenceKinematicCandidates);

        string expectedU2Hex = ToHex((float)vector.U2);
        string expectedT2Hex = ToHex((float)vector.T2);
        string expectedD2Hex = ToHex((float)vector.D2);

        ParityTraceRecord referenceKinematic = referenceKinematicCandidates
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedU2Hex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedT2Hex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedD2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            "kinematic_result");
        ParityTraceRecord managedKinematic = managedRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedU2Hex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedT2Hex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedD2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "m2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "m2_u2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "m2_ms", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "r2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "r2_u2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "r2_ms", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_u2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_t2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_d2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_ms", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2_he", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2MsReyblTerm", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2MsHeTerm", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "v2_ms", $"iter={vector.Iteration} kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_u2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_t2", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_ms", $"iter={vector.Iteration} kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_re", $"iter={vector.Iteration} kinematic");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_KinematicResult_Iteration4_Station1_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_kin_primary_iter4_focus_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "kinematic_result" or "bldif_primary_station" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.NotEmpty(referenceRecords);

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        string expectedStation2UHex = ToHex((float)vector.U2);
        string expectedStation2THex = ToHex((float)vector.T2);
        string expectedStation2DHex = ToHex((float)vector.D2);
        ParityTraceRecord referenceStation2 = referenceRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             record.Sequence < referenceSystem.Sequence &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 2) &&
                             ToHex((float)ReadRequiredDouble(record, "u")) == expectedStation2UHex &&
                             ToHex((float)ReadRequiredDouble(record, "t")) == expectedStation2THex &&
                             ToHex((float)ReadRequiredDouble(record, "d")) == expectedStation2DHex)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceStation1 = referenceRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             record.Sequence < referenceStation2.Sequence &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1))
            .OrderBy(static record => record.Sequence)
            .Last();
        string expectedStation1UHex = ToHex((float)ReadRequiredDouble(referenceStation1, "u"));
        string expectedStation1THex = ToHex((float)ReadRequiredDouble(referenceStation1, "t"));
        string expectedStation1DHex = ToHex((float)ReadRequiredDouble(referenceStation1, "d"));
        ParityTraceRecord referenceKinematic = referenceRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedStation1UHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedStation1THex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedStation1DHex)
            .Where(record => record.Sequence < referenceStation1.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            "kinematic_result",
            "bldif_primary_station");
        ParityTraceRecord managedStation2 = managedRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 2) &&
                             ToHex((float)ReadRequiredDouble(record, "u")) == expectedStation2UHex &&
                             ToHex((float)ReadRequiredDouble(record, "t")) == expectedStation2THex &&
                             ToHex((float)ReadRequiredDouble(record, "d")) == expectedStation2DHex)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedStation1 = managedRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             record.Sequence < managedStation2.Sequence &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1))
            .OrderBy(static record => record.Sequence)
            .Last();
        IReadOnlyList<ParityTraceRecord> managedKinematicCandidates = managedRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence < managedStation1.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedExactKinematicCandidates = managedKinematicCandidates
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedStation1UHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedStation1THex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedStation1DHex)
            .ToArray();

        Assert.True(
            managedExactKinematicCandidates.Count > 0,
            $"iter={vector.Iteration} no managed final station1 kinematic before primary station " +
            $"primary(u={ToHex((float)ReadRequiredDouble(managedStation1, "u"))}," +
            $"t={ToHex((float)ReadRequiredDouble(managedStation1, "t"))}," +
            $"d={ToHex((float)ReadRequiredDouble(managedStation1, "d"))}," +
            $"hk={ToHex((float)ReadRequiredDouble(managedStation1, "hk"))}) " +
            $"candidates={string.Join(";", managedKinematicCandidates.TakeLast(6).Select(static record => "seq=" + record.Sequence +
                ",u=" + ToHex((float)ReadRequiredDouble(record, "u2")) +
                ",t=" + ToHex((float)ReadRequiredDouble(record, "t2")) +
                ",d=" + ToHex((float)ReadRequiredDouble(record, "d2")) +
                ",hk=" + ToHex((float)ReadRequiredDouble(record, "hK2"))))}");

        ParityTraceRecord managedKinematic = managedExactKinematicCandidates.Last();

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", $"iter={vector.Iteration} station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", $"iter={vector.Iteration} station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", $"iter={vector.Iteration} station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "m2", $"iter={vector.Iteration} station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "r2", $"iter={vector.Iteration} station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", $"iter={vector.Iteration} station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", $"iter={vector.Iteration} station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_u2", $"iter={vector.Iteration} station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_t2", $"iter={vector.Iteration} station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_d2", $"iter={vector.Iteration} station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_ms", $"iter={vector.Iteration} station1 kinematic");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_KinematicResult_Iteration4_Station2_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_iter4_vector_primary_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "kinematic_result" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        string expectedUHex = ToHex((float)vector.U2);
        string expectedTHex = ToHex((float)vector.T2);
        string expectedDHex = ToHex((float)vector.D2);
        ParityTraceRecord referenceKinematic = referenceRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedUHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedTHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedDHex)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            "kinematic_result");
        ParityTraceRecord managedKinematic = managedRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedUHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedTHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedDHex)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", $"iter={vector.Iteration} station2 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", $"iter={vector.Iteration} station2 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", $"iter={vector.Iteration} station2 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", $"iter={vector.Iteration} station2 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", $"iter={vector.Iteration} station2 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", $"iter={vector.Iteration} station2 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_t2", $"iter={vector.Iteration} station2 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_d2", $"iter={vector.Iteration} station2 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_u2", $"iter={vector.Iteration} station2 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_t2", $"iter={vector.Iteration} station2 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_re", $"iter={vector.Iteration} station2 kinematic");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_KinematicResult_Iteration4_RawStation1_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_iter4_vector_primary_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "kinematic_result" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        string expectedUHex = ToHex((float)vector.U1);
        string expectedTHex = ToHex((float)vector.T1);
        string expectedDHex = ToHex((float)vector.D1);
        ParityTraceRecord referenceKinematic = referenceRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedUHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedTHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedDHex)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            vector,
            station1KinematicOverride: null,
            station1SecondaryOverride: null,
            station2KinematicOverride: null,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "kinematic_result");
        ParityTraceRecord managedKinematic = managedRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == expectedUHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "t2")) == expectedTHex)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "d2")) == expectedDHex)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", $"iter={vector.Iteration} raw station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", $"iter={vector.Iteration} raw station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", $"iter={vector.Iteration} raw station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", $"iter={vector.Iteration} raw station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", $"iter={vector.Iteration} raw station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", $"iter={vector.Iteration} raw station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_t2", $"iter={vector.Iteration} raw station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_d2", $"iter={vector.Iteration} raw station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_u2", $"iter={vector.Iteration} raw station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_t2", $"iter={vector.Iteration} raw station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_re", $"iter={vector.Iteration} raw station1 kinematic");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalRow12Bt2Terms_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_trdif_row22_ref"),
            "laminar_seed_system",
            "transition_interval_bt2_terms");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceBt2Candidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "row", 1) &&
                                 HasExactDataInt(record, "column", 2))
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(vectors.Count, referenceSystems.Count);
        Assert.NotEmpty(referenceBt2Candidates);

        for (int index = 0; index < vectors.Count; index++)
        {
            DirectSeedStation15Vector vector = vectors[index];
            ParityTraceRecord referenceSystem = referenceSystems[index];
            ParityTraceRecord referenceBt2 = referenceBt2Candidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();

            IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
                vector,
                "transition_interval_bt2_terms");
            ParityTraceRecord managedBt2 = managedRecords.Single(
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "row", 1) &&
                                 HasExactDataInt(record, "column", 2));

            int expectedBaseBits = ReadRequiredInt(referenceBt2, "baseBits");
            int actualBaseBits = ReadRequiredInt(managedBt2, "baseBits");
            Assert.True(
                expectedBaseBits == actualBaseBits,
                $"iter={vector.Iteration} transition bt2 row12 field=baseBits expected={expectedBaseBits} actual={actualBaseBits} " +
                $"source2Bits={ReadRequiredInt(managedBt2, "source2Bits")} coeff2Bits={ReadRequiredInt(managedBt2, "coeff2Bits")} " +
                $"source3Bits={ReadRequiredInt(managedBt2, "source3Bits")} coeff3Bits={ReadRequiredInt(managedBt2, "coeff3Bits")} " +
                $"source4Bits={ReadRequiredInt(managedBt2, "source4Bits")} coeff4Bits={ReadRequiredInt(managedBt2, "coeff4Bits")} " +
                $"source5Bits={ReadRequiredInt(managedBt2, "source5Bits")} coeff5Bits={ReadRequiredInt(managedBt2, "coeff5Bits")} " +
                $"baseVs2={ReadRequiredDouble(managedBt2, "baseVs2"):R}");
            int expectedStBits = ReadRequiredInt(referenceBt2, "stBits");
            int actualStBits = ReadRequiredInt(managedBt2, "stBits");
            Assert.True(
                expectedStBits == actualStBits,
                $"iter={vector.Iteration} transition bt2 row12 field=stBits expected={expectedStBits} actual={actualStBits} " +
                $"source1Bits={ReadRequiredInt(managedBt2, "source1Bits")} coeff1Bits={ReadRequiredInt(managedBt2, "coeff1Bits")} " +
                $"source1={ReadRequiredDouble(managedBt2, "source1"):R} coeff1={ReadRequiredDouble(managedBt2, "coeff1"):R} " +
                $"stTerm={ReadRequiredDouble(managedBt2, "stTerm"):R} wideStTerm={ReadRequiredDouble(managedBt2, "wideStTerm"):R}");
            int expectedTtBits = ReadRequiredInt(referenceBt2, "ttBits");
            int actualTtBits = ReadRequiredInt(managedBt2, "ttBits");
            Assert.True(
                expectedTtBits == actualTtBits,
                $"iter={vector.Iteration} transition bt2 row12 field=ttBits expected={expectedTtBits} actual={actualTtBits} " +
                $"source2Bits={ReadRequiredInt(managedBt2, "source2Bits")} coeff2Bits={ReadRequiredInt(managedBt2, "coeff2Bits")} " +
                $"source2={ReadRequiredDouble(managedBt2, "source2"):R} coeff2={ReadRequiredDouble(managedBt2, "coeff2"):R} " +
                $"ttTerm={ReadRequiredDouble(managedBt2, "ttTerm"):R} wideTtTerm={ReadRequiredDouble(managedBt2, "wideTtTerm"):R}");
            int expectedDtBits = ReadRequiredInt(referenceBt2, "dtBits");
            int actualDtBits = ReadRequiredInt(managedBt2, "dtBits");
            Assert.True(
                expectedDtBits == actualDtBits,
                $"iter={vector.Iteration} transition bt2 row12 field=dtBits expected={expectedDtBits} actual={actualDtBits} " +
                $"source3Bits={ReadRequiredInt(managedBt2, "source3Bits")} coeff3Bits={ReadRequiredInt(managedBt2, "coeff3Bits")} " +
                $"source3={ReadRequiredDouble(managedBt2, "source3"):R} coeff3={ReadRequiredDouble(managedBt2, "coeff3"):R} " +
                $"dtTerm={ReadRequiredDouble(managedBt2, "dtTerm"):R} wideDtTerm={ReadRequiredDouble(managedBt2, "wideDtTerm"):R}");
            AssertIntField(referenceBt2, managedBt2, "utBits", $"iter={vector.Iteration} transition bt2 row12");
            AssertIntField(referenceBt2, managedBt2, "xtBits", $"iter={vector.Iteration} transition bt2 row12");
            int expectedFinalBits = ReadRequiredInt(referenceBt2, "finalBits");
            int actualFinalBits = ReadRequiredInt(managedBt2, "finalBits");
            Assert.True(
                expectedFinalBits == actualFinalBits,
                $"iter={vector.Iteration} transition bt2 row12 field=finalBits expected={expectedFinalBits} actual={actualFinalBits} " +
                $"base={ReadRequiredInt(managedBt2, "baseBits")} st={ReadRequiredInt(managedBt2, "stBits")} " +
                $"tt={ReadRequiredInt(managedBt2, "ttBits")} dt={ReadRequiredInt(managedBt2, "dtBits")} " +
                $"ut={ReadRequiredInt(managedBt2, "utBits")} xt={ReadRequiredInt(managedBt2, "xtBits")} " +
                $"wide={ReadRequiredInt(managedBt2, "wideOriginalOperandsBits")} " +
                $"wideSt={ReadRequiredDouble(managedBt2, "wideStTerm"):R} wideTt={ReadRequiredDouble(managedBt2, "wideTtTerm"):R} " +
                $"wideDt={ReadRequiredDouble(managedBt2, "wideDtTerm"):R} wideUt={ReadRequiredDouble(managedBt2, "wideUtTerm"):R} " +
                $"wideXt={ReadRequiredDouble(managedBt2, "wideXtTerm"):R}");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalRow13Bt2Terms_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_trdif_row22_ref"),
            "laminar_seed_system",
            "transition_interval_bt2_terms");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceBt2Candidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "row", 1) &&
                                 HasExactDataInt(record, "column", 3))
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(vectors.Count, referenceSystems.Count);
        Assert.NotEmpty(referenceBt2Candidates);

        for (int index = 0; index < vectors.Count; index++)
        {
            DirectSeedStation15Vector vector = vectors[index];
            ParityTraceRecord referenceSystem = referenceSystems[index];
            ParityTraceRecord referenceBt2 = referenceBt2Candidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();

            IReadOnlyList<ParityTraceRecord> managedRecords;
            if (vector.Iteration == 5)
            {
                DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
                (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
                    LoadTransitionCarriedVector(sourceVector, 5);
                managedRecords = RunManagedTrace(
                    windowVector,
                    carriedStation2Kinematic,
                    station2PrimaryOverride: null,
                    "transition_interval_bt2_terms");
            }
            else
            {
                managedRecords = RunManagedTrace(
                    vector,
                    "transition_interval_bt2_terms");
            }

            ParityTraceRecord managedBt2 = managedRecords.Single(
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "row", 1) &&
                                 HasExactDataInt(record, "column", 3));

            int expectedBaseBits = ReadRequiredInt(referenceBt2, "baseBits");
            int actualBaseBits = ReadRequiredInt(managedBt2, "baseBits");
            Assert.True(
                expectedBaseBits == actualBaseBits,
                $"iter={vector.Iteration} transition bt2 row13 field=baseBits expected={expectedBaseBits} actual={actualBaseBits} " +
                $"source2Bits={ReadRequiredInt(managedBt2, "source2Bits")} coeff2Bits={ReadRequiredInt(managedBt2, "coeff2Bits")} " +
                $"source3Bits={ReadRequiredInt(managedBt2, "source3Bits")} coeff3Bits={ReadRequiredInt(managedBt2, "coeff3Bits")} " +
                $"source5Bits={ReadRequiredInt(managedBt2, "source5Bits")} coeff5Bits={ReadRequiredInt(managedBt2, "coeff5Bits")}");
            AssertIntField(referenceBt2, managedBt2, "stBits", $"iter={vector.Iteration} transition bt2 row13");
            int expectedTtBits = ReadRequiredInt(referenceBt2, "ttBits");
            int actualTtBits = ReadRequiredInt(managedBt2, "ttBits");
            Assert.True(
                expectedTtBits == actualTtBits,
                $"iter={vector.Iteration} transition bt2 row13 field=ttBits expected={expectedTtBits} actual={actualTtBits} " +
                $"source2Bits={ReadRequiredInt(managedBt2, "source2Bits")} coeff2Bits={ReadRequiredInt(managedBt2, "coeff2Bits")} " +
                $"source2={ReadRequiredDouble(managedBt2, "source2"):R} coeff2={ReadRequiredDouble(managedBt2, "coeff2"):R} " +
                $"ttTerm={ReadRequiredDouble(managedBt2, "ttTerm"):R} wideTtTerm={ReadRequiredDouble(managedBt2, "wideTtTerm"):R}");
            int expectedDtBits = ReadRequiredInt(referenceBt2, "dtBits");
            int actualDtBits = ReadRequiredInt(managedBt2, "dtBits");
            Assert.True(
                expectedDtBits == actualDtBits,
                $"iter={vector.Iteration} transition bt2 row13 field=dtBits expected={expectedDtBits} actual={actualDtBits} " +
                $"source3Bits={ReadRequiredInt(managedBt2, "source3Bits")} coeff3Bits={ReadRequiredInt(managedBt2, "coeff3Bits")} " +
                $"source3={ReadRequiredDouble(managedBt2, "source3"):R} coeff3={ReadRequiredDouble(managedBt2, "coeff3"):R} " +
                $"dtTerm={ReadRequiredDouble(managedBt2, "dtTerm"):R} wideDtTerm={ReadRequiredDouble(managedBt2, "wideDtTerm"):R}");
            AssertIntField(referenceBt2, managedBt2, "utBits", $"iter={vector.Iteration} transition bt2 row13");
            int expectedXtBits = ReadRequiredInt(referenceBt2, "xtBits");
            int actualXtBits = ReadRequiredInt(managedBt2, "xtBits");
            Assert.True(
                expectedXtBits == actualXtBits,
                $"iter={vector.Iteration} transition bt2 row13 field=xtBits expected={expectedXtBits} actual={actualXtBits} " +
                $"baseBits={ReadRequiredInt(managedBt2, "baseBits")} " +
                $"source5Bits={ReadRequiredInt(managedBt2, "source5Bits")} coeff5Bits={ReadRequiredInt(managedBt2, "coeff5Bits")} " +
                $"source5={ReadRequiredDouble(managedBt2, "source5"):R} coeff5={ReadRequiredDouble(managedBt2, "coeff5"):R} " +
                $"xtTerm={ReadRequiredDouble(managedBt2, "xtTerm"):R} wideXtTerm={ReadRequiredDouble(managedBt2, "wideXtTerm"):R}");

            int expectedFinalBits = ReadRequiredInt(referenceBt2, "finalBits");
            int actualFinalBits = ReadRequiredInt(managedBt2, "finalBits");
            Assert.True(
                expectedFinalBits == actualFinalBits,
                $"iter={vector.Iteration} transition bt2 row13 field=finalBits expected={expectedFinalBits} actual={actualFinalBits} " +
                $"base={ReadRequiredInt(managedBt2, "baseBits")} st={ReadRequiredInt(managedBt2, "stBits")} " +
                $"tt={ReadRequiredInt(managedBt2, "ttBits")} dt={ReadRequiredInt(managedBt2, "dtBits")} " +
                $"ut={ReadRequiredInt(managedBt2, "utBits")} xt={ReadRequiredInt(managedBt2, "xtBits")} " +
                $"wide={ReadRequiredInt(managedBt2, "wideOriginalOperandsBits")} fusedSourceOrder={ReadRequiredInt(managedBt2, "fusedSourceOrderBits")} " +
                $"wideSt={ReadRequiredDouble(managedBt2, "wideStTerm"):R} wideTt={ReadRequiredDouble(managedBt2, "wideTtTerm"):R} " +
                $"wideDt={ReadRequiredDouble(managedBt2, "wideDtTerm"):R} wideUt={ReadRequiredDouble(managedBt2, "wideUtTerm"):R} wideXt={ReadRequiredDouble(managedBt2, "wideXtTerm"):R}");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalRow22Terms_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_trdif_row22_ref"),
            "laminar_seed_system",
            "transition_interval_bt2_terms",
            "transition_interval_final_terms");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceBt2Candidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "row", 2) &&
                                 HasExactDataInt(record, "column", 2))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceFinalCandidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_final_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "row", 2) &&
                                 HasExactDataInt(record, "column", 2))
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(vectors.Count, referenceSystems.Count);
        Assert.NotEmpty(referenceBt2Candidates);
        Assert.NotEmpty(referenceFinalCandidates);

        for (int index = 0; index < vectors.Count; index++)
        {
            DirectSeedStation15Vector vector = vectors[index];
            ParityTraceRecord referenceSystem = referenceSystems[index];
            ParityTraceRecord referenceBt2 = referenceBt2Candidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();
            ParityTraceRecord referenceFinal = referenceFinalCandidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();

            IReadOnlyList<ParityTraceRecord> managedRecords;
            if (vector.Iteration == 5)
            {
                DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
                (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
                    LoadTransitionCarriedVector(sourceVector, 5);
                managedRecords = RunManagedTrace(
                    windowVector,
                    carriedStation2Kinematic,
                    station2PrimaryOverride: null,
                    "transition_interval_bt2_terms",
                    "transition_interval_final_terms",
                    "transition_interval_inputs",
                    "transition_interval_term_components");
            }
            else
            {
                managedRecords = RunManagedTrace(
                    vector,
                    "transition_interval_bt2_terms",
                    "transition_interval_final_terms",
                    "transition_interval_inputs",
                    "transition_interval_term_components");
            }
            ParityTraceRecord managedBt2 = managedRecords.Single(
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "row", 2) &&
                                 HasExactDataInt(record, "column", 2));
            ParityTraceRecord managedInputs = managedRecords.Single(static record => record.Kind == "transition_interval_inputs");
            ParityTraceRecord managedTermComponents = managedRecords.Single(static record => record.Kind == "transition_interval_term_components");
            ParityTraceRecord managedFinal = managedRecords.Single(
                static record => record.Kind == "transition_interval_final_terms" &&
                                 HasExactDataInt(record, "row", 2) &&
                                 HasExactDataInt(record, "column", 2));

            AssertIntField(referenceBt2, managedBt2, "baseBits", $"iter={vector.Iteration} transition bt2 row22");
            AssertIntField(referenceBt2, managedBt2, "stBits", $"iter={vector.Iteration} transition bt2 row22");
            int expectedTtBits = ReadRequiredInt(referenceBt2, "ttBits");
            int actualTtBits = ReadRequiredInt(managedBt2, "ttBits");
            Assert.True(
                expectedTtBits == actualTtBits,
                $"iter={vector.Iteration} transition bt2 row22 field=ttBits expected={expectedTtBits} actual={actualTtBits} " +
                $"source2Bits={ReadRequiredInt(managedBt2, "source2Bits")} coeff2Bits={ReadRequiredInt(managedBt2, "coeff2Bits")} " +
                $"source2={ReadRequiredDouble(managedBt2, "source2"):R} coeff2={ReadRequiredDouble(managedBt2, "coeff2"):R} " +
                $"ttTerm={ReadRequiredDouble(managedBt2, "ttTerm"):R} " +
                $"t1Original={ReadRequiredDouble(managedInputs, "t1Original"):R} t2={ReadRequiredDouble(managedInputs, "t2"):R} " +
                $"wf2={ReadRequiredDouble(managedTermComponents, "wf2"):R} wf1T2={ReadRequiredDouble(managedTermComponents, "wf1T2"):R} wf2T2={ReadRequiredDouble(managedTermComponents, "wf2T2"):R}");
            int expectedDtBits = ReadRequiredInt(referenceBt2, "dtBits");
            int actualDtBits = ReadRequiredInt(managedBt2, "dtBits");
            Assert.True(
                expectedDtBits == actualDtBits,
                $"iter={vector.Iteration} transition bt2 row22 field=dtBits expected={expectedDtBits} actual={actualDtBits} " +
                $"source3Bits={ReadRequiredInt(managedBt2, "source3Bits")} coeff3Bits={ReadRequiredInt(managedBt2, "coeff3Bits")} " +
                $"source3={ReadRequiredDouble(managedBt2, "source3"):R} coeff3={ReadRequiredDouble(managedBt2, "coeff3"):R} " +
                $"dtTerm={ReadRequiredDouble(managedBt2, "dtTerm")} wideDtTerm={ReadRequiredDouble(managedBt2, "wideDtTerm")} " +
                $"wideDtBits={unchecked((int)BitConverter.SingleToUInt32Bits((float)ReadRequiredDouble(managedBt2, "wideDtTerm")))}");
            AssertIntField(referenceBt2, managedBt2, "utBits", $"iter={vector.Iteration} transition bt2 row22");
            AssertIntField(referenceBt2, managedBt2, "xtBits", $"iter={vector.Iteration} transition bt2 row22");
            int expectedFinalBits = ReadRequiredInt(referenceBt2, "finalBits");
            int actualFinalBits = ReadRequiredInt(managedBt2, "finalBits");
            int wideOriginalBits = ReadRequiredInt(managedBt2, "wideOriginalOperandsBits");
            Assert.True(
                expectedFinalBits == actualFinalBits,
                $"iter={vector.Iteration} transition bt2 row22 field=finalBits expected={expectedFinalBits} actual={actualFinalBits} " +
                $"wide={wideOriginalBits} st={ReadRequiredInt(managedBt2, "stBits")} tt={ReadRequiredInt(managedBt2, "ttBits")} " +
                $"dt={ReadRequiredInt(managedBt2, "dtBits")} ut={ReadRequiredInt(managedBt2, "utBits")} xt={ReadRequiredInt(managedBt2, "xtBits")} " +
                $"wideSt={ReadRequiredDouble(managedBt2, "wideStTerm"):R} wideTt={ReadRequiredDouble(managedBt2, "wideTtTerm"):R} " +
                $"wideDt={ReadRequiredDouble(managedBt2, "wideDtTerm"):R} wideUt={ReadRequiredDouble(managedBt2, "wideUtTerm"):R} wideXt={ReadRequiredDouble(managedBt2, "wideXtTerm"):R}");

            AssertIntField(referenceFinal, managedFinal, "laminarBits", $"iter={vector.Iteration} transition final row22");
            AssertIntField(referenceFinal, managedFinal, "turbulentBits", $"iter={vector.Iteration} transition final row22");
            AssertIntField(referenceFinal, managedFinal, "finalBits", $"iter={vector.Iteration} transition final row22");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalRow22Terms_Iteration5_ExactCarry_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
        Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_trdif_row22_ref"),
            "laminar_seed_system",
            "transition_interval_bt2_terms",
            "transition_interval_final_terms");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "transition_interval_bt2_terms" or "transition_interval_final_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord referenceBt2 = referenceRecords
            .Where(record => record.Kind == "transition_interval_bt2_terms" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "row", 2) &&
                             HasExactDataInt(record, "column", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceFinal = referenceRecords
            .Where(record => record.Kind == "transition_interval_final_terms" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "row", 2) &&
                             HasExactDataInt(record, "column", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 5);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            windowVector,
            carryOverrides.Station1Kinematic,
            carryOverrides.Station1Secondary,
            carriedStation2Kinematic,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "transition_interval_bt2_terms",
            "transition_interval_final_terms");
        ParityTraceRecord managedBt2 = managedRecords.Single(
            static record => record.Kind == "transition_interval_bt2_terms" &&
                             HasExactDataInt(record, "row", 2) &&
                             HasExactDataInt(record, "column", 2));
        ParityTraceRecord managedFinal = managedRecords.Single(
            static record => record.Kind == "transition_interval_final_terms" &&
                             HasExactDataInt(record, "row", 2) &&
                             HasExactDataInt(record, "column", 2));

        AssertIntField(referenceBt2, managedBt2, "baseBits", "iter=5 exact-carry transition bt2 row22");
        AssertIntField(referenceBt2, managedBt2, "stBits", "iter=5 exact-carry transition bt2 row22");
        AssertIntField(referenceBt2, managedBt2, "ttBits", "iter=5 exact-carry transition bt2 row22");
        AssertIntField(referenceBt2, managedBt2, "dtBits", "iter=5 exact-carry transition bt2 row22");
        AssertIntField(referenceBt2, managedBt2, "utBits", "iter=5 exact-carry transition bt2 row22");
        AssertIntField(referenceBt2, managedBt2, "xtBits", "iter=5 exact-carry transition bt2 row22");
        AssertIntField(referenceBt2, managedBt2, "finalBits", "iter=5 exact-carry transition bt2 row22");

        AssertIntField(referenceFinal, managedFinal, "laminarBits", "iter=5 exact-carry transition final row22");
        AssertIntField(referenceFinal, managedFinal, "turbulentBits", "iter=5 exact-carry transition final row22");
        AssertIntField(referenceFinal, managedFinal, "finalBits", "iter=5 exact-carry transition final row22");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalRow23Terms_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_trdif_row23_ref"),
            "laminar_seed_system",
            "transition_interval_bt2_terms",
            "transition_interval_final_terms");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceBt2Candidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "row", 2) &&
                                 HasExactDataInt(record, "column", 3))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceFinalCandidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_final_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "row", 2) &&
                                 HasExactDataInt(record, "column", 3))
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(vectors.Count, referenceSystems.Count);
        Assert.NotEmpty(referenceBt2Candidates);
        Assert.NotEmpty(referenceFinalCandidates);

        for (int index = 0; index < vectors.Count; index++)
        {
            DirectSeedStation15Vector vector = vectors[index];
            ParityTraceRecord referenceSystem = referenceSystems[index];
            ParityTraceRecord referenceBt2 = referenceBt2Candidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();
            ParityTraceRecord referenceFinal = referenceFinalCandidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();

            IReadOnlyList<ParityTraceRecord> managedRecords;
            if (vector.Iteration == 5)
            {
                DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
                (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
                    LoadTransitionCarriedVector(sourceVector, 5);
                managedRecords = RunManagedTrace(
                    windowVector,
                    carriedStation2Kinematic,
                    station2PrimaryOverride: null,
                    "transition_interval_bt2_terms",
                    "transition_interval_final_terms");
            }
            else
            {
                managedRecords = RunManagedTrace(
                    vector,
                    "transition_interval_bt2_terms",
                    "transition_interval_final_terms");
            }

            ParityTraceRecord managedBt2 = managedRecords.Single(
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "row", 2) &&
                                 HasExactDataInt(record, "column", 3));
            ParityTraceRecord managedFinal = managedRecords.Single(
                static record => record.Kind == "transition_interval_final_terms" &&
                                 HasExactDataInt(record, "row", 2) &&
                                 HasExactDataInt(record, "column", 3));

            AssertIntField(referenceBt2, managedBt2, "baseBits", $"iter={vector.Iteration} transition bt2 row23");
            AssertIntField(referenceBt2, managedBt2, "stBits", $"iter={vector.Iteration} transition bt2 row23");
            AssertIntField(referenceBt2, managedBt2, "ttBits", $"iter={vector.Iteration} transition bt2 row23");
            AssertIntField(referenceBt2, managedBt2, "dtBits", $"iter={vector.Iteration} transition bt2 row23");
            AssertIntField(referenceBt2, managedBt2, "utBits", $"iter={vector.Iteration} transition bt2 row23");
            AssertIntField(referenceBt2, managedBt2, "xtBits", $"iter={vector.Iteration} transition bt2 row23");
            int expectedFinalBits = ReadRequiredInt(referenceBt2, "finalBits");
            int actualFinalBits = ReadRequiredInt(managedBt2, "finalBits");
            Assert.True(
                expectedFinalBits == actualFinalBits,
                $"iter={vector.Iteration} transition bt2 row23 field=finalBits expected={expectedFinalBits} actual={actualFinalBits} " +
                $"base={ReadRequiredInt(managedBt2, "baseBits")} st={ReadRequiredInt(managedBt2, "stBits")} " +
                $"tt={ReadRequiredInt(managedBt2, "ttBits")} dt={ReadRequiredInt(managedBt2, "dtBits")} " +
                $"ut={ReadRequiredInt(managedBt2, "utBits")} xt={ReadRequiredInt(managedBt2, "xtBits")} " +
                $"wide={ReadRequiredInt(managedBt2, "wideOriginalOperandsBits")} fusedSourceOrder={ReadRequiredInt(managedBt2, "fusedSourceOrderBits")} " +
                $"wideSt={ReadRequiredDouble(managedBt2, "wideStTerm"):R} wideTt={ReadRequiredDouble(managedBt2, "wideTtTerm"):R} " +
                $"wideDt={ReadRequiredDouble(managedBt2, "wideDtTerm"):R} wideUt={ReadRequiredDouble(managedBt2, "wideUtTerm"):R} wideXt={ReadRequiredDouble(managedBt2, "wideXtTerm"):R}");

            AssertIntField(referenceFinal, managedFinal, "laminarBits", $"iter={vector.Iteration} transition final row23");
            AssertIntField(referenceFinal, managedFinal, "turbulentBits", $"iter={vector.Iteration} transition final row23");
            AssertIntField(referenceFinal, managedFinal, "finalBits", $"iter={vector.Iteration} transition final row23");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalRow33Bt2Terms_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_trdif_row22_ref"),
            "laminar_seed_system",
            "transition_interval_bt2_terms");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceBt2Candidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "row", 3) &&
                                 HasExactDataInt(record, "column", 3))
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(vectors.Count, referenceSystems.Count);
        Assert.NotEmpty(referenceBt2Candidates);

        for (int index = 0; index < vectors.Count; index++)
        {
            DirectSeedStation15Vector vector = vectors[index];
            ParityTraceRecord referenceSystem = referenceSystems[index];
            ParityTraceRecord referenceBt2 = referenceBt2Candidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();

            IReadOnlyList<ParityTraceRecord> managedRecords;
            if (vector.Iteration == 5)
            {
                DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
                (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
                    LoadTransitionCarriedVector(sourceVector, 5);
                managedRecords = RunManagedTrace(
                    windowVector,
                    carriedStation2Kinematic,
                    station2PrimaryOverride: null,
                    "transition_interval_bt2_terms");
            }
            else
            {
                managedRecords = RunManagedTrace(
                    vector,
                    "transition_interval_bt2_terms");
            }
            ParityTraceRecord managedBt2 = managedRecords.Single(
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "row", 3) &&
                                 HasExactDataInt(record, "column", 3));

            AssertIntField(referenceBt2, managedBt2, "baseBits", $"iter={vector.Iteration} transition bt2 row33");
            int expectedStBits = ReadRequiredInt(referenceBt2, "stBits");
            int actualStBits = ReadRequiredInt(managedBt2, "stBits");
            Assert.True(
                expectedStBits == actualStBits,
                $"iter={vector.Iteration} transition bt2 row33 field=stBits expected={expectedStBits} actual={actualStBits} " +
                $"source1Bits={ReadRequiredInt(managedBt2, "source1Bits")} coeff1Bits={ReadRequiredInt(managedBt2, "coeff1Bits")} " +
                $"source1={ReadRequiredDouble(managedBt2, "source1"):R} coeff1={ReadRequiredDouble(managedBt2, "coeff1"):R} " +
                $"stTerm={ReadRequiredDouble(managedBt2, "stTerm"):R} wideStTerm={ReadRequiredDouble(managedBt2, "wideStTerm"):R}");
            AssertIntField(referenceBt2, managedBt2, "ttBits", $"iter={vector.Iteration} transition bt2 row33");
            AssertIntField(referenceBt2, managedBt2, "dtBits", $"iter={vector.Iteration} transition bt2 row33");
            AssertIntField(referenceBt2, managedBt2, "utBits", $"iter={vector.Iteration} transition bt2 row33");
            AssertIntField(referenceBt2, managedBt2, "xtBits", $"iter={vector.Iteration} transition bt2 row33");
            int expectedFinalBits = ReadRequiredInt(referenceBt2, "finalBits");
            int actualFinalBits = ReadRequiredInt(managedBt2, "finalBits");
            int wideFinalBits = unchecked((int)BitConverter.SingleToUInt32Bits((float)ReadRequiredDouble(managedBt2, "wideOriginalOperands")));
            int fusedSourceOrderBits = ReadRequiredInt(managedBt2, "fusedSourceOrderBits");
            string fusedCandidates = DescribeMatchingFusedCandidates(managedBt2, expectedFinalBits);
            Assert.True(
                expectedFinalBits == actualFinalBits,
                $"iter={vector.Iteration} transition bt2 row33 field=finalBits expected={expectedFinalBits} actual={actualFinalBits} wide={wideFinalBits} fusedSourceOrder={fusedSourceOrderBits} candidates={fusedCandidates} wideSt={ReadRequiredDouble(managedBt2, "wideStTerm"):R} wideTt={ReadRequiredDouble(managedBt2, "wideTtTerm"):R} wideDt={ReadRequiredDouble(managedBt2, "wideDtTerm"):R} wideUt={ReadRequiredDouble(managedBt2, "wideUtTerm"):R} wideXt={ReadRequiredDouble(managedBt2, "wideXtTerm"):R}");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalRow34Bt2Terms_Iteration5_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
        Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_trdif_row34_bt2_ref"),
            "laminar_seed_system",
            "transition_interval_bt2_terms");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "transition_interval_bt2_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord referenceBt2 = referenceRecords
            .Where(static record => record.Kind == "transition_interval_bt2_terms")
            .Where(static record => HasExactDataInt(record, "row", 3) && HasExactDataInt(record, "column", 4))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 5);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            windowVector,
            carryOverrides.Station1Kinematic,
            carryOverrides.Station1Secondary,
            carriedStation2Kinematic,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "transition_interval_bt2_terms");
        ParityTraceRecord managedBt2 = managedRecords.Single(
            static record => record.Kind == "transition_interval_bt2_terms" &&
                             HasExactDataInt(record, "row", 3) &&
                             HasExactDataInt(record, "column", 4));

        AssertIntField(referenceBt2, managedBt2, "baseBits", "iter=5 transition bt2 row34");
        AssertIntField(referenceBt2, managedBt2, "stBits", "iter=5 transition bt2 row34");
        AssertIntField(referenceBt2, managedBt2, "ttBits", "iter=5 transition bt2 row34");
        AssertIntField(referenceBt2, managedBt2, "dtBits", "iter=5 transition bt2 row34");
        AssertIntField(referenceBt2, managedBt2, "utBits", "iter=5 transition bt2 row34");
        AssertIntField(referenceBt2, managedBt2, "xtBits", "iter=5 transition bt2 row34");
        int expectedFinalBits = ReadRequiredInt(referenceBt2, "finalBits");
        int actualFinalBits = ReadRequiredInt(managedBt2, "finalBits");
        Assert.True(
            expectedFinalBits == actualFinalBits,
            $"iter=5 transition bt2 row34 field=finalBits expected={expectedFinalBits} actual={actualFinalBits} " +
            $"base={ReadRequiredInt(managedBt2, "baseBits")} st={ReadRequiredInt(managedBt2, "stBits")} " +
            $"tt={ReadRequiredInt(managedBt2, "ttBits")} dt={ReadRequiredInt(managedBt2, "dtBits")} " +
            $"ut={ReadRequiredInt(managedBt2, "utBits")} xt={ReadRequiredInt(managedBt2, "xtBits")} " +
            $"wideSt={ReadRequiredDouble(managedBt2, "wideStTerm"):R} wideTt={ReadRequiredDouble(managedBt2, "wideTtTerm"):R} " +
            $"wideDt={ReadRequiredDouble(managedBt2, "wideDtTerm"):R} wideUt={ReadRequiredDouble(managedBt2, "wideUtTerm"):R} wideXt={ReadRequiredDouble(managedBt2, "wideXtTerm"):R} " +
            $"wide={ToHex((float)ReadRequiredDouble(managedBt2, "wideOriginalOperands"))}");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifEq2D1Terms_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_eq2_st15_i5_ref"));
        string transitionWindowPath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter5_transition_window_ref"),
            "kinematic_result",
            "laminar_seed_system",
            "transition_point_iteration");

        IReadOnlyList<ParityTraceRecord> referenceRecords = File.ReadLines(referencePath)
            .Select(ParityTraceLoader.DeserializeLine)
            .Where(static record => record is not null)
            .Select(static record => record!)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> transitionWindowRecords = ParityTraceLoader.ReadMatching(
                transitionWindowPath,
                static record => record.Kind is "kinematic_result" or "laminar_seed_system" or "transition_point_iteration")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord transitionWindowSystem = transitionWindowRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord previousTransitionWindowSystem = transitionWindowRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < transitionWindowSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord firstTransitionPointIteration = transitionWindowRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousTransitionWindowSystem.Sequence &&
                             record.Sequence < transitionWindowSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord carriedStation2State = transitionWindowRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence > previousTransitionWindowSystem.Sequence &&
                             record.Sequence < firstTransitionPointIteration.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        IReadOnlyList<ParityTraceRecord> referenceEq2Candidates = referenceRecords
            .Where(static record => record.Kind == "bldif_eq2_d1_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 15) &&
                                    HasExactDataInt(record, "ityp", 2))
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceLogCandidates = referenceRecords
            .Where(static record => record.Kind == "bldif_log_inputs" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 15) &&
                                    HasExactDataInt(record, "ityp", 2))
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceCommonCandidates = referenceRecords
            .Where(static record => record.Kind == "bldif_common")
            .ToArray();

        Assert.NotEmpty(referenceEq2Candidates);
        Assert.NotEmpty(referenceLogCandidates);
        Assert.True(
            referenceCommonCandidates.Count > 0,
            $"No bldif_common records found in reference trace {referencePath}");
        ParityTraceRecord referenceEq2 = referenceEq2Candidates
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceLog = referenceLogCandidates
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceCommon = referenceCommonCandidates
            .Where(record => HasExactDataInt(record, "ityp", 2) && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        DirectSeedStation15Vector windowVector = vector with
        {
            U2 = FromSingleHex(GetSingleHex(carriedStation2State, "u2")),
            T2 = FromSingleHex(GetSingleHex(carriedStation2State, "t2")),
            D2 = FromSingleHex(GetSingleHex(carriedStation2State, "d2"))
        };

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            windowVector,
            ReadKinematicResult(carriedStation2State),
            station2PrimaryOverride: null,
            "bldif_log_inputs",
            "bldif_common",
            "bldif_eq2_d1_terms");
        ParityTraceRecord managedLog = managedRecords.Single(
            static record => record.Kind == "bldif_log_inputs" &&
                             HasExactDataInt(record, "ityp", 2));
        ParityTraceRecord managedCommon = managedRecords.Single(
            static record => record.Kind == "bldif_common" &&
                             HasExactDataInt(record, "ityp", 2));
        ParityTraceRecord managedEq2 = managedRecords.Single(
            static record => record.Kind == "bldif_eq2_d1_terms" &&
                             HasExactDataInt(record, "ityp", 2));

        AssertIntField(referenceLog, managedLog, "x1Bits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "x2Bits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "u1Bits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "u2Bits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "t1Bits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "t2Bits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "hs1Bits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "hs2Bits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "xRatioBits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "uRatioBits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "tRatioBits", $"iter={vector.Iteration} bldif log inputs");
        AssertIntField(referenceLog, managedLog, "hRatioBits", $"iter={vector.Iteration} bldif log inputs");

        AssertIntField(referenceCommon, managedCommon, "ityp", $"iter={vector.Iteration} bldif eq2 common");
        AssertIntField(referenceCommon, managedCommon, "cfmBits", $"iter={vector.Iteration} bldif eq2 common");
        AssertIntField(referenceCommon, managedCommon, "upwBits", $"iter={vector.Iteration} bldif eq2 common");
        AssertIntField(referenceCommon, managedCommon, "xlogBits", $"iter={vector.Iteration} bldif eq2 common");
        AssertIntField(referenceCommon, managedCommon, "ulogBits", $"iter={vector.Iteration} bldif eq2 common");
        AssertIntField(referenceCommon, managedCommon, "tlogBits", $"iter={vector.Iteration} bldif eq2 common");
        AssertIntField(referenceCommon, managedCommon, "hlogBits", $"iter={vector.Iteration} bldif eq2 common");
        AssertIntField(referenceCommon, managedCommon, "ddlogBits", $"iter={vector.Iteration} bldif eq2 common");

        AssertFloatFieldHex(referenceEq2, managedEq2, "zHaHalf", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "zCfm", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "zCf1", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "h1D1", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "cfmD1", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "cf1D1", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "vs1Row23Ha", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "vs1Row23Cfm", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "vs1Row23Cf", $"iter={vector.Iteration} bldif eq2 d1");
        AssertFloatFieldHex(referenceEq2, managedEq2, "vs1Row23", $"iter={vector.Iteration} bldif eq2 d1");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifEq1ResidualTerms_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_eq1_residual_station15_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = File.ReadLines(referencePath)
            .Select(ParityTraceLoader.DeserializeLine)
            .Where(static record => record is not null)
            .Select(static record => record!)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceSystems = referenceRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceResiduals = referenceRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 15) &&
                                    HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(vectors.Count, referenceSystems.Count);
        Assert.True(referenceResiduals.Count >= vectors.Count);

        string[] fields =
        [
            "upw",
            "oneMinusUpw",
            "s1",
            "s2",
            "saLeftTerm",
            "saRightTerm",
            "sa",
            "cq1",
            "cq2",
            "cqaLeftTerm",
            "cqaRightTerm",
            "cqa",
            "ald",
            "scc",
            "dxi",
            "dea",
            "slog",
            "ulog",
            "uq",
            "eq1Source",
            "eq1Production",
            "eq1LogLoss",
            "eq1Convection",
            "eq1DuxGain",
            "eq1SubStored",
            "eq1SubInlineProduction",
            "eq1SubInlineFull",
            "rezc"
        ];

        for (int index = 0; index < vectors.Count; index++)
        {
            DirectSeedStation15Vector vector = vectors[index];
            ParityTraceRecord referenceSystem = referenceSystems[index];
            ParityTraceRecord referenceResidual = referenceResiduals
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();
            IReadOnlyList<ParityTraceRecord> managedRecords;
            if (vector.Iteration == 3)
            {
                ParityTraceRecord carriedStation2State = LoadTransitionCarriedStation2State(3);
                managedRecords = RunManagedTrace(
                    vector,
                    ReadKinematicResult(carriedStation2State),
                    ReadPrimaryState(carriedStation2State),
                    "bldif_eq1_residual_terms");
            }
            else if (vector.Iteration == 5)
            {
                DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
                (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
                    LoadTransitionCarriedVector(sourceVector, 5);
                managedRecords = RunManagedTrace(
                    windowVector,
                    station2KinematicOverride: carriedStation2Kinematic,
                    station2PrimaryOverride: null,
                    "bldif_eq1_residual_terms");
            }
            else if (vector.Iteration == 7)
            {
                managedRecords = RunManagedTraceCore(
                    LoadVectors().Single(static candidate => candidate.Iteration == 7),
                    station1KinematicOverride: null,
                    station1SecondaryOverride: null,
                    station2KinematicOverride: null,
                    station2PrimaryOverride: null,
                    useDefaultStation1Carry: false,
                    "bldif_eq1_residual_terms");
            }
            else
            {
                managedRecords = RunManagedTrace(
                    vector,
                    "bldif_eq1_residual_terms");
            }
            ParityTraceRecord managedResidual = vector.Iteration == 7
                ? managedRecords.Single(
                    static record => record.Kind == "bldif_eq1_residual_terms" &&
                                     HasExactDataInt(record, "ityp", 2) &&
                                     HasExactDataInt(record, "station", 15) &&
                                     HasExactDataInt(record, "iteration", 7))
                : managedRecords.Single(
                    static record => record.Kind == "bldif_eq1_residual_terms" &&
                                     HasExactDataInt(record, "ityp", 2));

            foreach (string field in fields)
            {
                AssertFloatFieldHex(referenceResidual, managedResidual, field, $"iter={vector.Iteration} bldif eq1 residual");
            }
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifUpwTerms_Iteration5_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector sourceVector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_bldif_upw_iter5_station15_ref"),
            "laminar_seed_system",
            "bldif_upw_terms");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_upw_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord referenceUpw = referenceRecords
            .Where(record => record.Kind == "bldif_upw_terms" &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 5);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            windowVector,
            station2KinematicOverride: carriedStation2Kinematic,
            station2PrimaryOverride: null,
            "bldif_upw_terms");
        ParityTraceRecord managedUpw = managedRecords.Single(
            static record => record.Kind == "bldif_upw_terms" &&
                             HasExactDataInt(record, "ityp", 2));

        AssertIntField(referenceUpw, managedUpw, "ityp", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1T1", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1D1", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1Ms", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2T2", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2D2", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2Ms", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hl", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hlsq", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "ehh", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHl", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHd", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHk1", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHk2", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwT1", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwD1", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwT2", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwD2", "iter=5 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwMs", "iter=5 bldif upw");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifEq3D2Terms_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_row33_ref"));

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceBldifCandidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq3_d2_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(vectors.Count, referenceSystems.Count);
        Assert.NotEmpty(referenceBldifCandidates);

        foreach (DirectSeedStation15Vector vector in vectors)
        {
            ParityTraceRecord referenceSystem = referenceSystems[vector.Iteration - 1];
            ParityTraceRecord referenceBldif = referenceBldifCandidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();

            IReadOnlyList<ParityTraceRecord> managedRecords;
            if (vector.Iteration == 3)
            {
                ParityTraceRecord carriedStation2State = LoadTransitionCarriedStation2State(3);
                managedRecords = RunManagedTrace(
                    vector,
                    ReadKinematicResult(carriedStation2State),
                    ReadPrimaryState(carriedStation2State),
                    "bldif_eq3_d2_terms");
            }
            else if (vector.Iteration == 5)
            {
                DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
                (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
                    LoadTransitionCarriedVector(sourceVector, 5);
                managedRecords = RunManagedTrace(
                    windowVector,
                    carriedStation2Kinematic,
                    station2PrimaryOverride: null,
                    "bldif_eq3_d2_terms");
            }
            else
            {
                managedRecords = RunManagedTrace(
                    vector,
                    "bldif_eq3_d2_terms");
            }
            ParityTraceRecord managedBldif = managedRecords.Single(
                static record => record.Kind == "bldif_eq3_d2_terms" &&
                                 HasExactDataInt(record, "ityp", 2));

            AssertFloatFieldHex(referenceBldif, managedBldif, "xot1", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "xot2", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "cf1", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "cf2", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "di1", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "di2", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "zCfx", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "zDix", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "cfxUpw", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "dixUpw", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "zUpw", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "upwD", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "baseHs", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "baseCf", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "baseDi", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "extraH", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "extraUpw", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "baseStored33", $"iter={vector.Iteration} bldif eq3 d2");
            AssertFloatFieldHex(referenceBldif, managedBldif, "row33", $"iter={vector.Iteration} bldif eq3 d2");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifEq3D2InputChain_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 1);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_inputchain_iter1_common_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "blvar_turbulent_di_terms" or "bldif_common" or "bldif_upw_terms" or "bldif_eq3_d2_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        Assert.NotEmpty(referenceRecords);

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            "blvar_turbulent_di_terms",
            "bldif_common",
            "bldif_upw_terms",
            "bldif_eq3_d2_terms");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 1));
        ParityTraceRecord referenceBldif = referenceRecords
            .Where(record => record.Kind == "bldif_eq3_d2_terms" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedBldif = managedRecords
            .Where(static record => record.Kind == "bldif_eq3_d2_terms")
            .OrderBy(static record => record.Sequence)
            .Last();

        string referenceDi2Hex = ToHex((float)ReadRequiredDouble(referenceBldif, "di2"));
        ParityTraceRecord referenceBlvar = referenceRecords
            .Where(record => record.Kind == "blvar_turbulent_di_terms" &&
                             record.Sequence < referenceSystem.Sequence &&
                             ToHex((float)ReadRequiredDouble(record, "finalDi")) == referenceDi2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();
        IReadOnlyList<ParityTraceRecord> managedBlvarCandidates = managedRecords
            .Where(static record => record.Kind == "blvar_turbulent_di_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        string managedCandidateHexes = string.Join(
            ";",
            managedBlvarCandidates.Select(record =>
                $"finalDi={ToHex((float)ReadRequiredDouble(record, "finalDi"))},s={ToHex((float)ReadRequiredDouble(record, "s"))}"));
        ParityTraceRecord managedBlvar = managedBlvarCandidates.SingleOrDefault(
                record => ToHex((float)ReadRequiredDouble(record, "finalDi")) == referenceDi2Hex)
            ?? throw new Xunit.Sdk.XunitException(
                $"iter=1 bldif eq3 d2 input chain no managed blvar_turbulent_di_terms matched finalDi={referenceDi2Hex}; candidates={managedCandidateHexes}");

        AssertFloatFieldHex(referenceBlvar, managedBlvar, "s", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "rt", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "finalDi", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "cf2tD", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "diWallDPreDfac", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "dfTermD", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "diWallDPostDfac", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "ddD", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "ddlD", "iter=1 bldif eq3 d2 input chain blvar");
        AssertFloatFieldHex(referenceBlvar, managedBlvar, "finalDiD", "iter=1 bldif eq3 d2 input chain blvar");

        ParityTraceRecord referenceCommon = referenceRecords
            .Where(record => record.Kind == "bldif_common" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCommon = managedRecords
            .Where(static record => record.Kind == "bldif_common")
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceCommon, managedCommon, "ityp", "iter=1 bldif eq3 d2 input chain common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "upw", "iter=1 bldif eq3 d2 input chain common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "xlog", "iter=1 bldif eq3 d2 input chain common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "ulog", "iter=1 bldif eq3 d2 input chain common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "tlog", "iter=1 bldif eq3 d2 input chain common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "hlog", "iter=1 bldif eq3 d2 input chain common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "ddlog", "iter=1 bldif eq3 d2 input chain common");

        ParityTraceRecord referenceUpw = referenceRecords
            .Where(record => record.Kind == "bldif_upw_terms" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedUpw = managedRecords
            .Where(static record => record.Kind == "bldif_upw_terms")
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceUpw, managedUpw, "ityp", "iter=1 bldif eq3 d2 input chain upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2", "iter=1 bldif eq3 d2 input chain upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2D2", "iter=1 bldif eq3 d2 input chain upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHk2", "iter=1 bldif eq3 d2 input chain upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwD2", "iter=1 bldif eq3 d2 input chain upw");

        AssertIntField(referenceBldif, managedBldif, "ityp", "iter=1 bldif eq3 d2 input chain row33");
        AssertFloatFieldHex(referenceBldif, managedBldif, "xot2", "iter=1 bldif eq3 d2 input chain row33");
        AssertFloatFieldHex(referenceBldif, managedBldif, "zDix", "iter=1 bldif eq3 d2 input chain row33");
        AssertFloatFieldHex(referenceBldif, managedBldif, "upwD", "iter=1 bldif eq3 d2 input chain row33");
        AssertFloatFieldHex(referenceBldif, managedBldif, "baseDi", "iter=1 bldif eq3 d2 input chain row33");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_CfmD1InputChain_Iteration4_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_blmid_cf_iter4_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "blmid_cf_terms" or "bldif_secondary_station" or "bldif_eq2_d1_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        Assert.NotEmpty(referenceRecords);

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            "blmid_cf_terms",
            "bldif_secondary_station",
            "bldif_eq2_d1_terms");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));

        ParityTraceRecord referenceBlmid = referenceRecords
            .Where(record => record.Kind == "blmid_cf_terms" &&
                             record.Sequence < referenceSystem.Sequence &&
                             HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedBlmid = managedRecords
            .Where(record => record.Kind == "blmid_cf_terms" &&
                             HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceBlmid, managedBlmid, "cfm", "iter=4 cfm d1 chain blmid");
        AssertFloatFieldHex(referenceBlmid, managedBlmid, "cfmHka", "iter=4 cfm d1 chain blmid");
        AssertFloatFieldHex(referenceBlmid, managedBlmid, "cfmRta", "iter=4 cfm d1 chain blmid");
        AssertFloatFieldHex(referenceBlmid, managedBlmid, "cfmMa", "iter=4 cfm d1 chain blmid");
        AssertFloatFieldHex(referenceBlmid, managedBlmid, "cfmD1", "iter=4 cfm d1 chain blmid");

        ParityTraceRecord referenceSecondary = referenceRecords
            .Where(record => record.Kind == "bldif_secondary_station" &&
                             record.Sequence < referenceSystem.Sequence &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedSecondary = managedRecords
            .Where(record => record.Kind == "bldif_secondary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceSecondary, managedSecondary, "hkD", "iter=4 cfm d1 chain secondary");
        AssertFloatFieldHex(referenceSecondary, managedSecondary, "cfmD", "iter=4 cfm d1 chain secondary");
        AssertFloatFieldHex(referenceSecondary, managedSecondary, "cfD", "iter=4 cfm d1 chain secondary");

        ParityTraceRecord referenceEq2 = referenceRecords
            .Where(record => record.Kind == "bldif_eq2_d1_terms" &&
                             record.Sequence < referenceSystem.Sequence &&
                             HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedEq2 = managedRecords
            .Where(record => record.Kind == "bldif_eq2_d1_terms" &&
                             HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceEq2, managedEq2, "cfmD1", "iter=4 cfm d1 chain eq2");
        AssertFloatFieldHex(referenceEq2, managedEq2, "vs1Row23", "iter=4 cfm d1 chain eq2");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BlmidCandidateCf_Iteration4_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_cft_iter4_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "blmid_candidate_cf_terms" or "cft_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        Assert.NotEmpty(referenceRecords);

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            "laminar_seed_system",
            "blmid_candidate_cf_terms",
            "cft_terms");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        ParityTraceRecord referenceCandidate = referenceRecords
            .Where(record => record.Kind == "blmid_candidate_cf_terms" &&
                             record.Sequence < referenceSystem.Sequence &&
                             HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCandidate = managedRecords
            .Where(record => record.Kind == "blmid_candidate_cf_terms" &&
                             HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceCandidate, managedCandidate, "hka", "iter=4 blmid candidate");
        AssertFloatFieldHex(referenceCandidate, managedCandidate, "rta", "iter=4 blmid candidate");
        AssertFloatFieldHex(referenceCandidate, managedCandidate, "cfmTurb", "iter=4 blmid candidate");
        AssertFloatFieldHex(referenceCandidate, managedCandidate, "cfmTurbHka", "iter=4 blmid candidate");
        AssertFloatFieldHex(referenceCandidate, managedCandidate, "cfmTurbRta", "iter=4 blmid candidate");
        AssertIntField(referenceCandidate, managedCandidate, "usedLaminar", "iter=4 blmid candidate");
        AssertFloatFieldHex(referenceCandidate, managedCandidate, "cfm", "iter=4 blmid candidate");
        AssertFloatFieldHex(referenceCandidate, managedCandidate, "cfmHka", "iter=4 blmid candidate");
        AssertFloatFieldHex(referenceCandidate, managedCandidate, "cfmRta", "iter=4 blmid candidate");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_PrimaryStation_Iteration4_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_iter4_vector_primary_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_primary_station")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        Assert.NotEmpty(referenceRecords);

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));

        string expectedU2Hex = ToHex((float)vector.U2);
        string expectedT2Hex = ToHex((float)vector.T2);
        string expectedD2Hex = ToHex((float)vector.D2);
        ParityTraceRecord referenceStation2 = referenceRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             record.Sequence < referenceSystem.Sequence &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 2) &&
                             ToHex((float)ReadRequiredDouble(record, "u")) == expectedU2Hex &&
                             ToHex((float)ReadRequiredDouble(record, "t")) == expectedT2Hex &&
                             ToHex((float)ReadRequiredDouble(record, "d")) == expectedD2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceStation1 = referenceRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             record.Sequence < referenceStation2.Sequence &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1))
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            "bldif_primary_station");
        IReadOnlyList<ParityTraceRecord> managedStation2Candidates = managedRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 2) &&
                             ToHex((float)ReadRequiredDouble(record, "u")) == expectedU2Hex &&
                             ToHex((float)ReadRequiredDouble(record, "t")) == expectedT2Hex &&
                             ToHex((float)ReadRequiredDouble(record, "d")) == expectedD2Hex)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        Assert.True(
            managedStation2Candidates.Count > 0,
            $"iter=4 no managed primary station2 with exact inputs " +
            $"expected(u={expectedU2Hex},t={expectedT2Hex},d={expectedD2Hex}) " +
            $"candidates={DescribePrimaryStationCandidates(managedRecords, 2)}");
        ParityTraceRecord managedStation2 = managedStation2Candidates.Last();
        ParityTraceRecord managedStation1 = managedRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             record.Sequence < managedStation2.Sequence &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceStation1, managedStation1, "s", "iter=4 primary station1");
        AssertFloatFieldHex(referenceStation1, managedStation1, "hk", "iter=4 primary station1");
        AssertFloatFieldHex(referenceStation1, managedStation1, "rt", "iter=4 primary station1");

        AssertFloatFieldHex(referenceStation2, managedStation2, "s", "iter=4 primary station2");
        AssertFloatFieldHex(referenceStation2, managedStation2, "hk", "iter=4 primary station2");
        AssertFloatFieldHex(referenceStation2, managedStation2, "rt", "iter=4 primary station2");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifEq3D2ExtraHInputChain_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 3);
        ParityTraceRecord carriedStation2State = LoadTransitionCarriedStation2State(3);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_extrah_iter3_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_secondary_station" or "bldif_common" or "bldif_eq2_d2_terms" or "bldif_eq3_d2_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            ReadKinematicResult(carriedStation2State),
            ReadPrimaryState(carriedStation2State),
            "bldif_secondary_station",
            "bldif_common",
            "bldif_eq2_d2_terms",
            "bldif_eq3_d2_terms");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 3));

        ParityTraceRecord referenceSecondary = referenceRecords
            .Where(record => record.Kind == "bldif_secondary_station" &&
                             record.Sequence < referenceSystem.Sequence &&
                             HasExactDataInt(record, "station", 2))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedSecondary = managedRecords
            .Where(record => record.Kind == "bldif_secondary_station" &&
                             HasExactDataInt(record, "station", 2))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceSecondary, managedSecondary, "ityp", "iter=3 bldif eq3 d2 extraH secondary");
        AssertFloatFieldHex(referenceSecondary, managedSecondary, "hc", "iter=3 bldif eq3 d2 extraH secondary");
        AssertFloatFieldHex(referenceSecondary, managedSecondary, "hkD", "iter=3 bldif eq3 d2 extraH secondary");

        ParityTraceRecord referenceCommon = referenceRecords
            .Where(record => record.Kind == "bldif_common" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCommon = managedRecords
            .Where(static record => record.Kind == "bldif_common")
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceCommon, managedCommon, "ityp", "iter=3 bldif eq3 d2 extraH common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "ulog", "iter=3 bldif eq3 d2 extraH common");

        ParityTraceRecord referenceEq2 = referenceRecords
            .Where(record => record.Kind == "bldif_eq2_d2_terms" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedEq2 = managedRecords
            .Where(static record => record.Kind == "bldif_eq2_d2_terms")
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceEq2, managedEq2, "zHaHalf", "iter=3 bldif eq3 d2 extraH eq2");
        AssertFloatFieldHex(referenceEq2, managedEq2, "h2D2", "iter=3 bldif eq3 d2 extraH eq2");
        AssertFloatFieldHex(referenceEq2, managedEq2, "row23Ha", "iter=3 bldif eq3 d2 extraH eq2");

        ParityTraceRecord referenceEq3 = referenceRecords
            .Where(record => record.Kind == "bldif_eq3_d2_terms" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedEq3 = managedRecords
            .Where(static record => record.Kind == "bldif_eq3_d2_terms")
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceEq3, managedEq3, "extraH", "iter=3 bldif eq3 d2 extraH eq3");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifLogInputs_Iteration1_BitwiseMatchFortranTrace()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_log_iter1_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_log_inputs" or "bldif_common")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 1));
        IReadOnlyList<ParityTraceRecord> referenceInputs = referenceRecords
            .Where(record => record.Kind == "bldif_log_inputs" &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .TakeLast(2)
            .ToArray();
        Assert.Equal(2, referenceInputs.Count);

        for (int index = 0; index < referenceInputs.Count; index++)
        {
            ParityTraceRecord referenceInput = referenceInputs[index];
            int ityp = ReadRequiredInt(referenceInput, "ityp");
            BoundaryLayerSystemAssembler.BldifLogTerms managedTerms = BoundaryLayerSystemAssembler.ComputeBldifLogTerms(
                ityp,
                isSimilarityStation: false,
                ReadRequiredDouble(referenceInput, "x1"),
                ReadRequiredDouble(referenceInput, "x2"),
                ReadRequiredDouble(referenceInput, "u1"),
                ReadRequiredDouble(referenceInput, "u2"),
                ReadRequiredDouble(referenceInput, "t1"),
                ReadRequiredDouble(referenceInput, "t2"),
                ReadRequiredDouble(referenceInput, "hs1"),
                ReadRequiredDouble(referenceInput, "hs2"),
                useLegacyPrecision: true);

            AssertHex(GetFloatFieldHex(referenceInput, "xRatio"), ToHex((float)managedTerms.XRatio), $"iter=1 bldif log inputs record={index} ityp={ityp} field=xRatio");
            AssertHex(GetFloatFieldHex(referenceInput, "uRatio"), ToHex((float)managedTerms.URatio), $"iter=1 bldif log inputs record={index} ityp={ityp} field=uRatio");
            AssertHex(GetFloatFieldHex(referenceInput, "tRatio"), ToHex((float)managedTerms.TRatio), $"iter=1 bldif log inputs record={index} ityp={ityp} field=tRatio");
            AssertHex(GetFloatFieldHex(referenceInput, "hRatio"), ToHex((float)managedTerms.HRatio), $"iter=1 bldif log inputs record={index} ityp={ityp} field=hRatio");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifCommon_Iteration1_BitwiseMatchFortranTrace()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_log_iter1_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_log_inputs" or "bldif_common")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 1));
        ParityTraceRecord referenceInput = referenceRecords
            .Where(record => record.Kind == "bldif_log_inputs" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceCommon = referenceRecords
            .Where(record => record.Kind == "bldif_common" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        BoundaryLayerSystemAssembler.BldifLogTerms managedTerms = BoundaryLayerSystemAssembler.ComputeBldifLogTerms(
            bldifType: 2,
            isSimilarityStation: false,
            ReadRequiredDouble(referenceInput, "x1"),
            ReadRequiredDouble(referenceInput, "x2"),
            ReadRequiredDouble(referenceInput, "u1"),
            ReadRequiredDouble(referenceInput, "u2"),
            ReadRequiredDouble(referenceInput, "t1"),
            ReadRequiredDouble(referenceInput, "t2"),
            ReadRequiredDouble(referenceInput, "hs1"),
            ReadRequiredDouble(referenceInput, "hs2"),
            useLegacyPrecision: true);

        AssertHex(GetFloatFieldHex(referenceCommon, "xlog"), ToHex((float)managedTerms.XLog), "iter=1 bldif common field=xlog");
        AssertHex(GetFloatFieldHex(referenceCommon, "ulog"), ToHex((float)managedTerms.ULog), "iter=1 bldif common field=ulog");
        AssertHex(GetFloatFieldHex(referenceCommon, "tlog"), ToHex((float)managedTerms.TLog), "iter=1 bldif common field=tlog");
        AssertHex(GetFloatFieldHex(referenceCommon, "hlog"), ToHex((float)managedTerms.HLog), "iter=1 bldif common field=hlog");
        AssertHex(GetFloatFieldHex(referenceCommon, "ddlog"), ToHex((float)managedTerms.DdLog), "iter=1 bldif common field=ddlog");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifLogInputs_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 3);
        ParityTraceRecord carriedStation2State = LoadTransitionCarriedStation2State(3);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_log_iter3_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_log_inputs" or "bldif_common")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            ReadKinematicResult(carriedStation2State),
            ReadPrimaryState(carriedStation2State),
            "bldif_log_inputs",
            "bldif_common");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 3));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> referenceInputs = referenceRecords
            .Where(record => record.Kind == "bldif_log_inputs" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedInputs = managedRecords
            .Where(static record => record.Kind == "bldif_log_inputs")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        Assert.Equal(referenceInputs.Count, managedInputs.Count);

        for (int index = 0; index < referenceInputs.Count; index++)
        {
            ParityTraceRecord referenceInput = referenceInputs[index];
            ParityTraceRecord managedInput = managedInputs[index];
            int ityp = ReadRequiredInt(referenceInput, "ityp");

            AssertIntField(referenceInput, managedInput, "ityp", $"iter=3 bldif log inputs record={index}");
            AssertFloatFieldHex(referenceInput, managedInput, "x1", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "x2", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "u1", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "u2", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "t1", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "t2", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "hs1", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "hs2", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "xRatio", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "uRatio", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "tRatio", $"iter=3 bldif log inputs record={index} ityp={ityp}");
            AssertFloatFieldHex(referenceInput, managedInput, "hRatio", $"iter=3 bldif log inputs record={index} ityp={ityp}");
        }

        ParityTraceRecord referenceCommon = referenceRecords
            .Where(record => record.Kind == "bldif_common" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCommon = managedRecords
            .Where(static record => record.Kind == "bldif_common")
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceCommon, managedCommon, "xlog", "iter=3 bldif log common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "ulog", "iter=3 bldif log common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "tlog", "iter=3 bldif log common");
        AssertFloatFieldHex(referenceCommon, managedCommon, "hlog", "iter=3 bldif log common");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifCommonUpw_Iteration3_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 3);
        ParityTraceRecord carriedStation2State = LoadTransitionCarriedStation2State(3);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_log_iter3_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_common")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            ReadKinematicResult(carriedStation2State),
            ReadPrimaryState(carriedStation2State),
            "bldif_common");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 3));
        ParityTraceRecord referenceCommon = referenceRecords
            .Where(record => record.Kind == "bldif_common" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCommon = managedRecords.Single(
            static record => record.Kind == "bldif_common" &&
                             HasExactDataInt(record, "ityp", 2));

        AssertIntField(referenceCommon, managedCommon, "ityp", "iter=3 bldif common upw");
        AssertFloatFieldHex(referenceCommon, managedCommon, "upw", "iter=3 bldif common upw");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifEq1SaBlend_Iteration5_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_bldif_eq1_residual_station15_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = File.ReadLines(referencePath)
            .Select(ParityTraceLoader.DeserializeLine)
            .Where(static record => record is not null)
            .Select(static record => record!)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord referenceResidual = referenceRecords
            .Where(record => record.Kind == "bldif_eq1_residual_terms" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "ityp", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 5);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            windowVector,
            station2KinematicOverride: carriedStation2Kinematic,
            station2PrimaryOverride: null,
            "bldif_eq1_residual_terms");
        ParityTraceRecord managedResidual = managedRecords.Single(
            static record => record.Kind == "bldif_eq1_residual_terms" &&
                             HasExactDataInt(record, "ityp", 2));

        AssertFloatFieldHex(referenceResidual, managedResidual, "upw", "iter=5 bldif eq1 sa blend");
        AssertFloatFieldHex(referenceResidual, managedResidual, "oneMinusUpw", "iter=5 bldif eq1 sa blend");
        AssertFloatFieldHex(referenceResidual, managedResidual, "s1", "iter=5 bldif eq1 sa blend");
        AssertFloatFieldHex(referenceResidual, managedResidual, "s2", "iter=5 bldif eq1 sa blend");
        AssertFloatFieldHex(referenceResidual, managedResidual, "saLeftTerm", "iter=5 bldif eq1 sa blend");
        AssertFloatFieldHex(referenceResidual, managedResidual, "saRightTerm", "iter=5 bldif eq1 sa blend");
        AssertFloatFieldHex(referenceResidual, managedResidual, "sa", "iter=5 bldif eq1 sa blend");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifEq1TTerms_Iteration5_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        DirectSeedStation15Vector sourceVector = vectors.Single(static candidate => candidate.Iteration == 4);
        Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_bldif_eq1_t_withsys_ref"),
            "laminar_seed_system",
            "bldif_eq1_t_terms");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_eq1_t_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord referenceTerms = referenceRecords
            .Where(record => record.Kind == "bldif_eq1_t_terms" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 5);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            windowVector,
            carryOverrides.Station1Kinematic,
            carryOverrides.Station1Secondary,
            carriedStation2Kinematic,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "bldif_eq1_t_terms");
        ParityTraceRecord managedTerms = managedRecords.Single(
            static record => record.Kind == "bldif_eq1_t_terms" &&
                             HasExactDataInt(record, "ityp", 2));

        AssertIntField(referenceTerms, managedTerms, "ityp", "iter=5 bldif eq1 t terms");
        AssertFloatFieldHex(referenceTerms, managedTerms, "upwT1Term", "iter=5 bldif eq1 t terms");
        AssertFloatFieldHex(referenceTerms, managedTerms, "de1T1Term", "iter=5 bldif eq1 t terms");
        AssertFloatFieldHex(referenceTerms, managedTerms, "us1T1Term", "iter=5 bldif eq1 t terms");
        AssertFloatFieldHex(referenceTerms, managedTerms, "row12Transport", "iter=5 bldif eq1 t terms");
        AssertFloatFieldHex(referenceTerms, managedTerms, "cq1T1Term", "iter=5 bldif eq1 t terms");
        AssertFloatFieldHex(referenceTerms, managedTerms, "cf1T1Term", "iter=5 bldif eq1 t terms");
        AssertFloatFieldHex(referenceTerms, managedTerms, "hk1T1Term", "iter=5 bldif eq1 t terms");
        AssertFloatFieldHex(referenceTerms, managedTerms, "row12", "iter=5 bldif eq1 t terms");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifCommonUpw_Iteration7_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 7);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_bldif_upw_iter7_station15_trigger_ref"),
            "laminar_seed_system",
            "bldif_common");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_common")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            vector,
            station1KinematicOverride: null,
            station1SecondaryOverride: null,
            station2KinematicOverride: null,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "bldif_common");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));
        ParityTraceRecord referenceCommon = referenceRecords
            .Where(record => record.Kind == "bldif_common" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCommon = managedRecords.Single(
            static record => record.Kind == "bldif_common" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));

        AssertIntField(referenceCommon, managedCommon, "ityp", "iter=7 bldif common upw");
        AssertFloatFieldHex(referenceCommon, managedCommon, "upw", "iter=7 bldif common upw");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifUpwTerms_Iteration7_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 7);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_bldif_upw_iter7_station15_trigger_ref"),
            "laminar_seed_system",
            "bldif_upw_terms");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_upw_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            vector,
            station1KinematicOverride: null,
            station1SecondaryOverride: null,
            station2KinematicOverride: null,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "bldif_upw_terms");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));
        ParityTraceRecord referenceUpw = referenceRecords
            .Where(record => record.Kind == "bldif_upw_terms" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedUpw = managedRecords.Single(
            static record => record.Kind == "bldif_upw_terms" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));

        AssertIntField(referenceUpw, managedUpw, "ityp", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk1", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hk2", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hl", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "hlsq", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "ehh", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHl", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHd", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHk1", "iter=7 bldif upw");
        AssertFloatFieldHex(referenceUpw, managedUpw, "upwHk2", "iter=7 bldif upw");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BldifPrimaryStation1_Iteration7_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 7);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_bldif_station1_iter7_producer_ref"),
            "laminar_seed_system",
            "bldif_primary_station");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_primary_station")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            vector,
            station1KinematicOverride: null,
            station1SecondaryOverride: null,
            station2KinematicOverride: null,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "bldif_primary_station");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));
        ParityTraceRecord referenceStation1 = referenceRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedStation1 = managedRecords.Single(
            static record => record.Kind == "bldif_primary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1) &&
                             HasExactDataInt(record, "intervalStation", 15) &&
                             HasExactDataInt(record, "iteration", 7));

        AssertIntField(referenceStation1, managedStation1, "ityp", "iter=7 bldif primary station1");
        AssertIntField(referenceStation1, managedStation1, "station", "iter=7 bldif primary station1");
        AssertFloatFieldHex(referenceStation1, managedStation1, "u", "iter=7 bldif primary station1");
        AssertFloatFieldHex(referenceStation1, managedStation1, "t", "iter=7 bldif primary station1");
        AssertFloatFieldHex(referenceStation1, managedStation1, "d", "iter=7 bldif primary station1");
        AssertFloatFieldHex(referenceStation1, managedStation1, "s", "iter=7 bldif primary station1");
        AssertFloatFieldHex(referenceStation1, managedStation1, "hk", "iter=7 bldif primary station1");
        AssertFloatFieldHex(referenceStation1, managedStation1, "rt", "iter=7 bldif primary station1");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_KinematicResult_Iteration7_Station1_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 7);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_bldif_station1_iter7_producer_ref"),
            "laminar_seed_system",
            "bldif_primary_station",
            "kinematic_result");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_primary_station" or "kinematic_result")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTraceCore(
            vector,
            station1KinematicOverride: null,
            station1SecondaryOverride: null,
            station2KinematicOverride: null,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: false,
            "kinematic_result",
            "bldif_primary_station");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));
        ParityTraceRecord referenceStation1 = referenceRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceKinematic = referenceRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence < referenceStation1.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        ParityTraceRecord managedStation1 = managedRecords.Single(
            static record => record.Kind == "bldif_primary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1) &&
                             HasExactDataInt(record, "intervalStation", 15) &&
                             HasExactDataInt(record, "iteration", 7));
        ParityTraceRecord managedKinematic = managedRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence < managedStation1.Sequence)
            .Where(record => ToHex((float)ReadRequiredDouble(record, "u2")) == ToHex((float)ReadRequiredDouble(managedStation1, "u")))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "iter=7 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "iter=7 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "iter=7 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "iter=7 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "iter=7 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "iter=7 station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_t2", "iter=7 station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_d2", "iter=7 station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "hK2_ms", "iter=7 station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_u2", "iter=7 station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_t2", "iter=7 station1 kinematic");
        AssertFloatFieldHex(referenceKinematic, managedKinematic, "rT2_re", "iter=7 station1 kinematic");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionWindow_Iteration3_PointIterationsAndFinalSensitivities_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 3);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter4_transition_window_ref"),
            "kinematic_result",
            "laminar_seed_system",
            "transition_point_iteration",
            "transition_final_sensitivities");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "kinematic_result" or "transition_point_iteration" or "transition_final_sensitivities" or "transition_interval_inputs" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 3));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord firstReferencePointIteration = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord carriedStation2State = referenceRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < firstReferencePointIteration.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            ReadKinematicResult(carriedStation2State),
            ReadPrimaryState(carriedStation2State),
            "transition_final_sensitivities",
            "transition_point_iteration",
            "transition_interval_inputs",
            "laminar_seed_system");
        ParityTraceRecord referenceFinalSensitivities = referenceRecords
            .Where(record => record.Kind == "transition_final_sensitivities" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Single();
        ParityTraceRecord managedFinalSensitivities = managedRecords.Single(static record => record.Kind == "transition_final_sensitivities");

        IReadOnlyList<ParityTraceRecord> referencePointIterations = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedPointIterations = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        string referencePointSummary = string.Join(
            ";",
            referencePointIterations.Select(record =>
                $"iter={ReadRequiredDouble(record, "iteration"):0},a2={ToHex((float)ReadRequiredDouble(record, "ampl2"))},ax={ToHex((float)ReadRequiredDouble(record, "ax"))},wf2={ToHex((float)ReadRequiredDouble(record, "wf2"))}"));
        string managedPointSummary = string.Join(
            ";",
            managedPointIterations.Select(record =>
                $"iter={ReadRequiredDouble(record, "iteration"):0},a2={ToHex((float)ReadRequiredDouble(record, "ampl2"))},ax={ToHex((float)ReadRequiredDouble(record, "ax"))},wf2={ToHex((float)ReadRequiredDouble(record, "wf2"))}"));
        Assert.True(
            referencePointIterations.Count == managedPointIterations.Count,
            $"iter=4 transition point count expected={referencePointIterations.Count} actual={managedPointIterations.Count} reference=[{referencePointSummary}] managed=[{managedPointSummary}]");
        for (int index = 0; index < referencePointIterations.Count; index++)
        {
            ParityTraceRecord referencePoint = referencePointIterations[index];
            ParityTraceRecord managedPoint = managedPointIterations[index];

            AssertIntField(referencePoint, managedPoint, "iteration", $"iter=3 transition point record={index}");

            string[] pointFields =
            [
                "x1",
                "x2",
                "ampl1",
                "amcrit",
                "ax",
                "ampl2",
                "wf2",
                "xt",
                "tt",
                "dt",
                "ut",
                "residual",
                "residual_A2",
                "deltaA2",
                "relaxation"
            ];

            foreach (string field in pointFields)
            {
                AssertFloatFieldHex(referencePoint, managedPoint, field, $"iter=3 transition point record={index}");
            }
        }

        string[] finalSensitivityFields =
        [
            "xtA2",
            "zA2",
            "zD2",
            "zX2",
            "xtD2",
            "xtX2Base",
            "xtX2Correction",
            "xtX2"
        ];

        foreach (string field in finalSensitivityFields)
        {
            AssertFloatFieldHex(referenceFinalSensitivities, managedFinalSensitivities, field, $"iter=3 transition final sensitivities field={field}");
        }

        // The accepted transition-interval packet at this seam is owned by the
        // dedicated direct-seed transition-interval-input oracle. This replay
        // stays on the transition-point/final-sensitivity side of that seam.
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionWindow_Iteration4_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 3);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter4_transition_window_ref"),
            "transition_point_iteration",
            "transition_final_sensitivities",
            "transition_interval_inputs",
            "laminar_seed_system",
            "kinematic_result");
        string seedReferencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter4_transition_seed_ref"),
            "transition_point_seed",
            "laminar_seed_system");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "transition_point_iteration" or "transition_final_sensitivities" or "transition_interval_inputs" or "laminar_seed_system" or "kinematic_result")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> seedReferenceRecords = ParityTraceLoader.ReadMatching(
                seedReferencePath,
                static record => record.Kind is "transition_point_seed" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> referencePointIterations = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceInputs = referenceRecords
            .Where(record => record.Kind == "transition_interval_inputs" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord carriedStation2State = referenceRecords
            .Where(record => record.Kind == "kinematic_result" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referencePointIterations[0].Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        DirectSeedStation15Vector windowVector = vector with
        {
            U2 = FromSingleHex(GetSingleHex(carriedStation2State, "u2")),
            T2 = FromSingleHex(GetSingleHex(carriedStation2State, "t2")),
            D2 = FromSingleHex(GetSingleHex(carriedStation2State, "d2"))
        };
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            windowVector,
            ReadKinematicResult(carriedStation2State),
            station2PrimaryOverride: null,
            "transition_point_seed",
            "transition_point_iteration",
            "transition_final_sensitivities",
            "transition_interval_inputs",
            "laminar_seed_system");
        ParityTraceRecord referenceSeedSystem = seedReferenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 3));
        ParityTraceRecord referenceSeed = seedReferenceRecords
            .Where(static record => record.Kind == "transition_point_seed")
            .Where(record => record.Sequence > referenceSeedSystem.Sequence &&
                             ToHex((float)ReadRequiredDouble(record, "x1")) == ToHex((float)windowVector.X1) &&
                             ToHex((float)ReadRequiredDouble(record, "x2")) == ToHex((float)windowVector.X2) &&
                             ToHex((float)ReadRequiredDouble(record, "hk2")) == ToHex((float)ReadRequiredDouble(carriedStation2State, "hK2")) &&
                             ToHex((float)ReadRequiredDouble(record, "rt2")) == ToHex((float)ReadRequiredDouble(carriedStation2State, "rT2")))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSeed = managedRecords.Single(static record => record.Kind == "transition_point_seed");

        string[] seedFields =
        [
            "x1",
            "x2",
            "dx",
            "hk1",
            "t1",
            "rt1",
            "a1",
            "hk2",
            "t2",
            "rt2",
            "a2Input",
            "acrit",
            "ax0",
            "seedAmpl2"
        ];

        foreach (string field in seedFields)
        {
            AssertFloatFieldHex(referenceSeed, managedSeed, field, $"iter=4 transition window seed field={field}");
        }

        ParityTraceRecord referenceFinalSensitivities = referenceRecords
            .Where(record => record.Kind == "transition_final_sensitivities" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Single();
        ParityTraceRecord managedFinalSensitivities = managedRecords.Single(static record => record.Kind == "transition_final_sensitivities");

        string[] finalSensitivityFields =
        [
            "xtA2",
            "wf2A1",
            "xtA1BaseTerm1",
            "xtA1BaseTerm2",
            "xtA1Base",
            "axA1Base",
            "ttCombo",
            "dtCombo",
            "utCombo",
            "axA1TTerm",
            "axA1DTerm",
            "axA1UTerm",
            "axA1",
            "zA1",
            "zA2",
            "xtA1Correction",
            "xtA1"
        ];

        foreach (string field in finalSensitivityFields)
        {
            AssertFloatFieldHex(referenceFinalSensitivities, managedFinalSensitivities, field, $"iter=4 transition final sensitivities field={field}");
        }
        IReadOnlyList<ParityTraceRecord> managedPointIterations = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        string referencePointSummary = string.Join(
            ";",
            referencePointIterations.Select(record =>
                $"iter={ReadRequiredDouble(record, "iteration"):0},a2={ToHex((float)ReadRequiredDouble(record, "ampl2"))},ax={ToHex((float)ReadRequiredDouble(record, "ax"))},wf2={ToHex((float)ReadRequiredDouble(record, "wf2"))}"));
        string managedPointSummary = string.Join(
            ";",
            managedPointIterations.Select(record =>
                $"iter={ReadRequiredDouble(record, "iteration"):0},a2={ToHex((float)ReadRequiredDouble(record, "ampl2"))},ax={ToHex((float)ReadRequiredDouble(record, "ax"))},wf2={ToHex((float)ReadRequiredDouble(record, "wf2"))}"));
        Assert.True(
            referencePointIterations.Count == managedPointIterations.Count,
            $"iter=4 transition point count expected={referencePointIterations.Count} actual={managedPointIterations.Count} reference=[{referencePointSummary}] managed=[{managedPointSummary}]");
        for (int index = 0; index < referencePointIterations.Count; index++)
        {
            ParityTraceRecord referencePoint = referencePointIterations[index];
            ParityTraceRecord managedPoint = managedPointIterations[index];

            AssertIntField(referencePoint, managedPoint, "iteration", $"iter=4 transition point record={index}");

            string[] pointFields =
            [
                "x1",
                "x2",
                "ampl1",
                "ampl2",
                "amcrit",
                "ax",
                "wf2",
                "xt",
                "tt",
                "dt",
                "ut",
                "residual",
                "residual_A2",
                "deltaA2",
                "relaxation"
            ];

            foreach (string field in pointFields)
            {
                AssertFloatFieldHex(referencePoint, managedPoint, field, $"iter=4 transition point record={index}");
            }
        }

        ParityTraceRecord managedInputs = managedRecords
            .Where(static record => record.Kind == "transition_interval_inputs")
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatFieldHex(referenceFinalSensitivities, referenceInputs, "xtX2", "iter=4 reference transition handoff");
        AssertFloatFieldHex(managedFinalSensitivities, managedInputs, "xtX2", "iter=4 managed transition handoff");

        string[] intervalFields =
        [
            "x1",
            "x2",
            "xt",
            "x1Original",
            "t1Original",
            "d1Original",
            "u1Original",
            "t1",
            "t2",
            "d1",
            "d2",
            "u1",
            "u2",
            "xtA1",
            "xtT1",
            "xtT2",
            "xtD1",
            "xtD2",
            "xtU1",
            "xtU2",
            "xtX1",
            "xtX2",
            "wf2A1",
            "wf2T1",
            "wf2T2",
            "wf2D1",
            "wf2D2",
            "wf2U1",
            "wf2U2",
            "wf2X1",
            "wf2X2",
            "ttA1",
            "ttT1",
            "ttT2",
            "ttD1",
            "ttD2",
            "dtA1",
            "dtT1",
            "dtT2",
            "dtD1",
            "dtD2",
            "utA1",
            "utT1",
            "utT2",
            "utD1",
            "utD2",
            "utU1",
            "utU2",
            "st",
            "stA1",
            "stT1",
            "stT2",
            "stD1",
            "stD2",
            "stU1",
            "stU2",
            "stX1",
            "stX2"
        ];

        foreach (string field in intervalFields)
        {
            AssertFloatFieldHex(referenceInputs, managedInputs, field, "iter=4 transition interval inputs");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionWindow_Iteration5_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter5_transition_window_ref"),
            "transition_point_iteration",
            "transition_final_sensitivities",
            "transition_interval_st_terms",
            "transition_interval_inputs",
            "laminar_seed_system",
            "kinematic_result");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "transition_point_iteration" or "transition_final_sensitivities" or "transition_interval_st_terms" or "transition_interval_inputs" or "laminar_seed_system" or "kinematic_result")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> referencePointIterations = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceInputs = referenceRecords
            .Where(record => record.Kind == "transition_interval_inputs" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord carriedStation2State = referenceRecords
            .Where(record => record.Kind == "kinematic_result" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referencePointIterations[0].Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        DirectSeedStation15Vector windowVector = vector with
        {
            U2 = FromSingleHex(GetSingleHex(carriedStation2State, "u2")),
            T2 = FromSingleHex(GetSingleHex(carriedStation2State, "t2")),
            D2 = FromSingleHex(GetSingleHex(carriedStation2State, "d2"))
        };
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            windowVector,
            ReadKinematicResult(carriedStation2State),
            station2PrimaryOverride: null,
            "transition_final_sensitivities",
            "transition_interval_st_terms",
            "transition_point_iteration",
            "transition_interval_inputs",
            "laminar_seed_system");
        ParityTraceRecord referenceFinalSensitivities = referenceRecords
            .Where(record => record.Kind == "transition_final_sensitivities" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Single();
        ParityTraceRecord managedFinalSensitivities = managedRecords.Single(static record => record.Kind == "transition_final_sensitivities");

        string[] finalSensitivityFields =
        [
            "xtA2",
            "ttCombo",
            "axT1HkTerm",
            "axT1BaseTerm",
            "axT1RtTerm",
            "axT1TtTerm",
            "axT1",
            "zT1",
            "zA2",
            "zX2",
            "xtT1",
            "xtX2Base",
            "xtX2Correction",
            "xtX2",
            "wf2A1",
            "xtA1BaseTerm1",
            "xtA1BaseTerm2",
            "xtA1Base",
            "xtA1Correction",
            "xtA1"
        ];

        foreach (string field in finalSensitivityFields)
        {
            AssertFloatFieldHex(referenceFinalSensitivities, managedFinalSensitivities, field, $"iter=5 transition final sensitivities field={field}");
        }

        IReadOnlyList<ParityTraceRecord> managedPointIterations = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        string referencePointSummary = string.Join(
            ";",
            referencePointIterations.Select(record =>
                $"iter={ReadRequiredDouble(record, "iteration"):0},a2={ToHex((float)ReadRequiredDouble(record, "ampl2"))},ax={ToHex((float)ReadRequiredDouble(record, "ax"))},wf2={ToHex((float)ReadRequiredDouble(record, "wf2"))}"));
        string managedPointSummary = string.Join(
            ";",
            managedPointIterations.Select(record =>
                $"iter={ReadRequiredDouble(record, "iteration"):0},a2={ToHex((float)ReadRequiredDouble(record, "ampl2"))},ax={ToHex((float)ReadRequiredDouble(record, "ax"))},wf2={ToHex((float)ReadRequiredDouble(record, "wf2"))}"));
        Assert.True(
            referencePointIterations.Count == managedPointIterations.Count,
            $"iter=5 transition point count expected={referencePointIterations.Count} actual={managedPointIterations.Count} reference=[{referencePointSummary}] managed=[{managedPointSummary}]");
        for (int index = 0; index < referencePointIterations.Count; index++)
        {
            ParityTraceRecord referencePoint = referencePointIterations[index];
            ParityTraceRecord managedPoint = managedPointIterations[index];

            AssertIntField(referencePoint, managedPoint, "iteration", $"iter=5 transition point record={index}");

            string[] pointFields =
            [
                "x1",
                "x2",
                "ampl1",
                "ampl2",
                "amcrit",
                "ax",
                "wf2",
                "xt",
                "tt",
                "dt",
                "ut",
                "residual",
                "residual_A2",
                "deltaA2",
                "relaxation"
            ];

            foreach (string field in pointFields)
            {
                AssertFloatFieldHex(referencePoint, managedPoint, field, $"iter=5 transition point record={index}");
            }
        }

        ParityTraceRecord managedInputs = managedRecords
            .Where(static record => record.Kind == "transition_interval_inputs")
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceStTerms = referenceRecords
            .Where(record => record.Kind == "transition_interval_st_terms" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Single();
        ParityTraceRecord managedStTerms = managedRecords
            .Where(static record => record.Kind == "transition_interval_st_terms")
            .OrderBy(static record => record.Sequence)
            .Single();

        AssertFloatFieldHex(referenceFinalSensitivities, referenceInputs, "xtX2", "iter=5 reference transition handoff");
        AssertFloatFieldHex(managedFinalSensitivities, managedInputs, "xtX2", "iter=5 managed transition handoff");

        string[] stFields =
        [
            "ctr",
            "ctrHk2",
            "cqT",
            "cqTTt",
            "hk2Tt",
            "stTt",
            "stDt",
            "stUt",
            "ttA1",
            "dtA1",
            "utA1",
            "stA1"
        ];

        foreach (string field in stFields)
        {
            AssertFloatFieldHex(referenceStTerms, managedStTerms, field, $"iter=5 transition st terms field={field}");
        }

        string[] intervalFields =
        [
            "x1",
            "x2",
            "xt",
            "x1Original",
            "t1Original",
            "d1Original",
            "u1Original",
            "t1",
            "t2",
            "d1",
            "d2",
            "u1",
            "u2",
            "xtA1",
            "xtT1",
            "xtT2",
            "xtD1",
            "xtD2",
            "xtU1",
            "xtU2",
            "xtX1",
            "xtX2",
            "wf2A1",
            "wf2T1",
            "wf2T2",
            "wf2D1",
            "wf2D2",
            "wf2U1",
            "wf2U2",
            "wf2X1",
            "wf2X2",
            "ttA1",
            "ttT1",
            "ttT2",
            "ttD1",
            "ttD2",
            "dtA1",
            "dtT1",
            "dtT2",
            "dtD1",
            "dtD2",
            "utA1",
            "utT1",
            "utT2",
            "utD1",
            "utD2",
            "utU1",
            "utU2",
            "st",
            "stA1",
            "stT1",
            "stT2",
            "stD1",
            "stD2",
            "stU1",
            "stU2",
            "stX1",
            "stX2"
        ];

        foreach (string field in intervalFields)
        {
            AssertFloatFieldHex(referenceInputs, managedInputs, field, "iter=5 transition interval inputs");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration5AcceptedState_ReplaysToIteration6SystemInputs()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string iteration5Path = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter5_transition_window_ref"),
            "laminar_seed_system");
        string broadPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_directseed_station16_ref"));

        ParityTraceRecord iteration5System = ParityTraceLoader.ReadMatching(
                iteration5Path,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "iteration", 5))
            .Single();
        ParityTraceRecord iteration6System = ParityTraceLoader.ReadMatching(
                broadPath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "iteration", 6))
            .Single();

        double[,] matrix = new double[4, 4];
        matrix[0, 0] = ReadRequiredDouble(iteration5System, "row11");
        matrix[0, 1] = ReadRequiredDouble(iteration5System, "row12");
        matrix[0, 2] = ReadRequiredDouble(iteration5System, "row13");
        matrix[0, 3] = ReadRequiredDouble(iteration5System, "row14");
        matrix[1, 0] = ReadRequiredDouble(iteration5System, "row21");
        matrix[1, 1] = ReadRequiredDouble(iteration5System, "row22");
        matrix[1, 2] = ReadRequiredDouble(iteration5System, "row23");
        matrix[1, 3] = ReadRequiredDouble(iteration5System, "row24");
        matrix[2, 0] = ReadRequiredDouble(iteration5System, "row31");
        matrix[2, 1] = ReadRequiredDouble(iteration5System, "row32");
        matrix[2, 2] = ReadRequiredDouble(iteration5System, "row33");
        matrix[2, 3] = ReadRequiredDouble(iteration5System, "row34");
        matrix[3, 0] = ReadRequiredDouble(iteration5System, "row41");
        matrix[3, 1] = ReadRequiredDouble(iteration5System, "row42");
        matrix[3, 2] = ReadRequiredDouble(iteration5System, "row43");
        matrix[3, 3] = ReadRequiredDouble(iteration5System, "row44");

        double[] rhs =
        [
            ReadRequiredDouble(iteration5System, "residual1"),
            ReadRequiredDouble(iteration5System, "residual2"),
            ReadRequiredDouble(iteration5System, "residual3"),
            ReadRequiredDouble(iteration5System, "residual4")
        ];

        double uei = ReadRequiredDouble(iteration5System, "uei");
        double theta = ReadRequiredDouble(iteration5System, "theta");
        double dstar = ReadRequiredDouble(iteration5System, "dstar");
        double ctau = ReadRequiredDouble(iteration5System, "ctau");

        var solver = new DenseLinearSystemSolver();
        double[] delta = (double[])SolveSeedLinearSystemMethod.Invoke(
            null,
            new object?[] { solver, matrix, rhs, true })!;
        object stepMetrics = ComputeSeedStepMetricsMethod.Invoke(
            null,
            new object?[]
            {
                delta,
                theta,
                dstar,
                Math.Max(ctau, 1.0e-7),
                uei,
                1,
                15,
                5,
                "accepted_state_replay",
                true,
                true
            })!;
        double dmax = (double)GetProperty(stepMetrics, "Dmax");
        double rlx = (double)ComputeLegacySeedRelaxationMethod.Invoke(null, new object?[] { dmax, true })!;

        ctau = Math.Min(Math.Max(LegacyPrecisionMath.AddScaled(ctau, rlx, delta[0], useLegacyPrecision: true), 1.0e-7), 0.30);
        theta = Math.Max(LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], useLegacyPrecision: true), 1.0e-10);
        dstar = Math.Max(LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], useLegacyPrecision: true), 1.0e-10);
        uei = Math.Max(LegacyPrecisionMath.AddScaled(uei, rlx, delta[3], useLegacyPrecision: true), 1.0e-10);
        dstar = (double)ApplySeedDslimMethod.Invoke(null, new object?[] { dstar, theta, 0.0, 1.02, true })!;

        AssertHex(GetFloatFieldHex(iteration6System, "uei"), ToHex((float)uei), "iter=5 accepted state to iter=6 uei");
        AssertHex(GetFloatFieldHex(iteration6System, "theta"), ToHex((float)theta), "iter=5 accepted state to iter=6 theta");
        AssertHex(GetFloatFieldHex(iteration6System, "dstar"), ToHex((float)dstar), "iter=5 accepted state to iter=6 dstar");
        AssertHex(GetFloatFieldHex(iteration6System, "ctau"), ToHex((float)ctau), "iter=5 accepted state to iter=6 ctau");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration9AcceptedState_ReplaysToFinalCarry()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string broadPath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_directseed_station16_ref"),
            "laminar_seed_system",
            "laminar_seed_final");

        ParityTraceRecord iteration9System = ParityTraceLoader.ReadMatching(
                broadPath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "iteration", 9))
            .Single();
        ParityTraceRecord finalCarry = ParityTraceLoader.ReadMatching(
                broadPath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .Single();

        double[,] matrix = new double[4, 4];
        matrix[0, 0] = ReadRequiredDouble(iteration9System, "row11");
        matrix[0, 1] = ReadRequiredDouble(iteration9System, "row12");
        matrix[0, 2] = ReadRequiredDouble(iteration9System, "row13");
        matrix[0, 3] = ReadRequiredDouble(iteration9System, "row14");
        matrix[1, 0] = ReadRequiredDouble(iteration9System, "row21");
        matrix[1, 1] = ReadRequiredDouble(iteration9System, "row22");
        matrix[1, 2] = ReadRequiredDouble(iteration9System, "row23");
        matrix[1, 3] = ReadRequiredDouble(iteration9System, "row24");
        matrix[2, 0] = ReadRequiredDouble(iteration9System, "row31");
        matrix[2, 1] = ReadRequiredDouble(iteration9System, "row32");
        matrix[2, 2] = ReadRequiredDouble(iteration9System, "row33");
        matrix[2, 3] = ReadRequiredDouble(iteration9System, "row34");
        matrix[3, 0] = ReadRequiredDouble(iteration9System, "row41");
        matrix[3, 1] = ReadRequiredDouble(iteration9System, "row42");
        matrix[3, 2] = ReadRequiredDouble(iteration9System, "row43");
        matrix[3, 3] = ReadRequiredDouble(iteration9System, "row44");

        double[] rhs =
        [
            ReadRequiredDouble(iteration9System, "residual1"),
            ReadRequiredDouble(iteration9System, "residual2"),
            ReadRequiredDouble(iteration9System, "residual3"),
            ReadRequiredDouble(iteration9System, "residual4")
        ];

        double uei = ReadRequiredDouble(iteration9System, "uei");
        double theta = ReadRequiredDouble(iteration9System, "theta");
        double dstar = ReadRequiredDouble(iteration9System, "dstar");
        double ctau = ReadRequiredDouble(iteration9System, "ctau");

        var solver = new DenseLinearSystemSolver();
        double[] delta = (double[])SolveSeedLinearSystemMethod.Invoke(
            null,
            new object?[] { solver, matrix, rhs, true })!;
        object stepMetrics = ComputeSeedStepMetricsMethod.Invoke(
            null,
            new object?[]
            {
                delta,
                theta,
                dstar,
                Math.Max(ctau, 1.0e-7),
                uei,
                1,
                15,
                9,
                "accepted_state_replay",
                true,
                true
            })!;
        double dmax = (double)GetProperty(stepMetrics, "Dmax");
        double rlx = (double)ComputeLegacySeedRelaxationMethod.Invoke(null, new object?[] { dmax, true })!;

        ctau = Math.Min(Math.Max(LegacyPrecisionMath.AddScaled(ctau, rlx, delta[0], useLegacyPrecision: true), 1.0e-7), 0.30);
        theta = Math.Max(LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], useLegacyPrecision: true), 1.0e-10);
        dstar = Math.Max(LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], useLegacyPrecision: true), 1.0e-10);
        uei = Math.Max(LegacyPrecisionMath.AddScaled(uei, rlx, delta[3], useLegacyPrecision: true), 1.0e-10);
        dstar = (double)ApplySeedDslimMethod.Invoke(null, new object?[] { dstar, theta, 0.0, 1.02, true })!;
        double mass = LegacyPrecisionMath.Multiply(dstar, uei, useLegacyPrecision: true);

        AssertHex(GetFloatFieldHex(finalCarry, "theta"), ToHex((float)theta), "iter=9 accepted state to final theta");
        AssertHex(GetFloatFieldHex(finalCarry, "dstar"), ToHex((float)dstar), "iter=9 accepted state to final dstar");
        AssertHex(GetFloatFieldHex(finalCarry, "ctau"), ToHex((float)ctau), "iter=9 accepted state to final ctau");
        AssertHex(GetFloatFieldHex(finalCarry, "mass"), ToHex((float)mass), "iter=9 accepted state to final mass");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionPointSeed_Iteration7_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector sourceVector = LoadVectors().Single(static candidate => candidate.Iteration == 6);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter7_transition_seed_ref"),
            "transition_point_seed",
            "transition_point_iteration",
            "kinematic_result",
            "laminar_seed_system");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "transition_point_seed" or "transition_point_iteration" or "kinematic_result" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord firstReferencePointIteration = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord carriedStation2Kinematic = referenceRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < firstReferencePointIteration.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSeed = referenceRecords
            .Where(static record => record.Kind == "transition_point_seed")
            .Where(record => record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Single();

        BoundaryLayerSystemAssembler.KinematicResult carriedKinematicOverride =
            ReadKinematicResult(carriedStation2Kinematic);
        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2WindowKinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 7);

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            windowVector,
            carriedStation2WindowKinematic,
            station2PrimaryOverride: null,
            "transition_point_seed");
        ParityTraceRecord managedSeed = managedRecords.Single(static record => record.Kind == "transition_point_seed");

        AssertHex(
            ToHex((float)ReadRequiredDouble(carriedStation2Kinematic, "hK2")),
            ToHex((float)ReadRequiredDouble(referenceSeed, "hk2")),
            "iter=7 transition seed carried station2 hk2");
        AssertHex(
            ToHex((float)ReadRequiredDouble(carriedStation2Kinematic, "rT2")),
            ToHex((float)ReadRequiredDouble(referenceSeed, "rt2")),
            "iter=7 transition seed carried station2 rt2");

        AssertIntField(referenceSeed, managedSeed, "idampv", "iter=7 transition seed");

        string[] fields =
        [
            "x1",
            "x2",
            "dx",
            "hk1",
            "t1",
            "rt1",
            "a1",
            "hk2",
            "t2",
            "rt2",
            "a2Input",
            "acrit",
            "ax0",
            "seedAmpl2"
        ];

        foreach (string field in fields)
        {
            AssertFloatFieldHex(referenceSeed, managedSeed, field, $"iter=7 transition seed field={field}");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionPointIteration_Iteration7_BitwiseMatchFortranTrace()
    {
        Iteration7TransitionWindowRecords window = LoadIteration7TransitionWindowRecords();

        string referencePointSummary = string.Join(
            ";",
            window.ReferencePointIterations.Select(record =>
                $"iter={ReadRequiredDouble(record, "iteration"):0},a2={ToHex((float)ReadRequiredDouble(record, "ampl2"))},ax={ToHex((float)ReadRequiredDouble(record, "ax"))},wf2={ToHex((float)ReadRequiredDouble(record, "wf2"))}"));
        string managedPointSummary = string.Join(
            ";",
            window.ManagedPointIterations.Select(record =>
                $"iter={ReadRequiredDouble(record, "iteration"):0},a2={ToHex((float)ReadRequiredDouble(record, "ampl2"))},ax={ToHex((float)ReadRequiredDouble(record, "ax"))},wf2={ToHex((float)ReadRequiredDouble(record, "wf2"))}"));
        Assert.True(
            window.ReferencePointIterations.Count == window.ManagedPointIterations.Count,
            $"iter=7 transition point count expected={window.ReferencePointIterations.Count} actual={window.ManagedPointIterations.Count} reference=[{referencePointSummary}] managed=[{managedPointSummary}]");

        for (int index = 0; index < window.ReferencePointIterations.Count; index++)
        {
            ParityTraceRecord referencePoint = window.ReferencePointIterations[index];
            ParityTraceRecord managedPoint = window.ManagedPointIterations[index];

            AssertIntField(referencePoint, managedPoint, "iteration", $"iter=7 transition point record={index}");

            string[] pointFields =
            [
                "x1",
                "x2",
                "ampl1",
                "ampl2",
                "amcrit",
                "ax",
                "wf2",
                "xt",
                "tt",
                "dt",
                "ut",
                "residual",
                "residual_A2",
                "deltaA2",
                "relaxation"
            ];

            foreach (string field in pointFields)
            {
                AssertFloatFieldHex(referencePoint, managedPoint, field, $"iter=7 transition point record={index}");
            }
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionFinalSensitivities_Iteration7_BitwiseMatchFortranTrace()
    {
        Iteration7TransitionWindowRecords window = LoadIteration7TransitionWindowRecords();

        string[] finalSensitivityFields =
        [
            "xtA2",
            "wf2A1",
            "wf2A2",
            "xtA1BaseTerm1",
            "xtA1BaseTerm2",
            "xtA1Base",
            "xtA1Correction",
            "xtA1",
            "xtT1",
            "xtD1",
            "xtU1",
            "xtX1",
            "xtX2"
        ];

        foreach (string field in finalSensitivityFields)
        {
            AssertFloatFieldHex(window.ReferenceFinalSensitivities, window.ManagedFinalSensitivities, field, $"iter=7 transition final sensitivities field={field}");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionIntervalInputs_Iteration7_BitwiseMatchFortranTrace()
    {
        Iteration7TransitionWindowRecords window = LoadIteration7TransitionWindowRecords();

        string[] intervalFields =
        [
            "x1",
            "x2",
            "xt",
            "x1Original",
            "t1Original",
            "d1Original",
            "u1Original",
            "t1",
            "t2",
            "d1",
            "d2",
            "u1",
            "u2",
            "xtA1",
            "xtT1",
            "xtT2",
            "xtD1",
            "xtD2",
            "xtU1",
            "xtU2",
            "xtX1",
            "xtX2",
            "wf2A1",
            "wf2T1",
            "wf2T2",
            "wf2D1",
            "wf2D2",
            "wf2U1",
            "wf2U2",
            "wf2X1",
            "wf2X2",
            "ttA1",
            "ttT1",
            "ttT2",
            "ttD1",
            "ttD2",
            "dtA1",
            "dtT1",
            "dtT2",
            "dtD1",
            "dtD2",
            "utA1",
            "utT1",
            "utT2",
            "utD1",
            "utD2",
            "utU1",
            "utU2",
            "st",
            "stA1",
            "stT1",
            "stT2",
            "stD1",
            "stD2",
            "stU1",
            "stU2",
            "stX1",
            "stX2"
        ];

        foreach (string field in intervalFields)
        {
            AssertFloatFieldHex(window.ReferenceTransitionInputs, window.ManagedTransitionInputs, field, "iter=7 transition interval inputs");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_Iteration6AcceptedState_ReplaysToIteration7SystemInputs()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string broadPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_directseed_station16_ref"));

        ParityTraceRecord iteration6System = ParityTraceLoader.ReadMatching(
                broadPath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "iteration", 6))
            .Single();
        ParityTraceRecord iteration7System = ParityTraceLoader.ReadMatching(
                broadPath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "iteration", 7))
            .Single();

        double[,] matrix = new double[4, 4];
        matrix[0, 0] = ReadRequiredDouble(iteration6System, "row11");
        matrix[0, 1] = ReadRequiredDouble(iteration6System, "row12");
        matrix[0, 2] = ReadRequiredDouble(iteration6System, "row13");
        matrix[0, 3] = ReadRequiredDouble(iteration6System, "row14");
        matrix[1, 0] = ReadRequiredDouble(iteration6System, "row21");
        matrix[1, 1] = ReadRequiredDouble(iteration6System, "row22");
        matrix[1, 2] = ReadRequiredDouble(iteration6System, "row23");
        matrix[1, 3] = ReadRequiredDouble(iteration6System, "row24");
        matrix[2, 0] = ReadRequiredDouble(iteration6System, "row31");
        matrix[2, 1] = ReadRequiredDouble(iteration6System, "row32");
        matrix[2, 2] = ReadRequiredDouble(iteration6System, "row33");
        matrix[2, 3] = ReadRequiredDouble(iteration6System, "row34");
        matrix[3, 0] = ReadRequiredDouble(iteration6System, "row41");
        matrix[3, 1] = ReadRequiredDouble(iteration6System, "row42");
        matrix[3, 2] = ReadRequiredDouble(iteration6System, "row43");
        matrix[3, 3] = ReadRequiredDouble(iteration6System, "row44");

        double[] rhs =
        [
            ReadRequiredDouble(iteration6System, "residual1"),
            ReadRequiredDouble(iteration6System, "residual2"),
            ReadRequiredDouble(iteration6System, "residual3"),
            ReadRequiredDouble(iteration6System, "residual4")
        ];

        double uei = ReadRequiredDouble(iteration6System, "uei");
        double theta = ReadRequiredDouble(iteration6System, "theta");
        double dstar = ReadRequiredDouble(iteration6System, "dstar");
        double ctau = ReadRequiredDouble(iteration6System, "ctau");

        var solver = new DenseLinearSystemSolver();
        double[] delta = (double[])SolveSeedLinearSystemMethod.Invoke(
            null,
            new object?[] { solver, matrix, rhs, true })!;
        object stepMetrics = ComputeSeedStepMetricsMethod.Invoke(
            null,
            new object?[]
            {
                delta,
                theta,
                dstar,
                Math.Max(ctau, 1.0e-7),
                uei,
                1,
                15,
                6,
                "accepted_state_replay",
                true,
                true
            })!;
        double dmax = (double)GetProperty(stepMetrics, "Dmax");
        double rlx = (double)ComputeLegacySeedRelaxationMethod.Invoke(null, new object?[] { dmax, true })!;

        ctau = Math.Min(Math.Max(LegacyPrecisionMath.AddScaled(ctau, rlx, delta[0], useLegacyPrecision: true), 1.0e-7), 0.30);
        theta = Math.Max(LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], useLegacyPrecision: true), 1.0e-10);
        dstar = Math.Max(LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], useLegacyPrecision: true), 1.0e-10);
        uei = Math.Max(LegacyPrecisionMath.AddScaled(uei, rlx, delta[3], useLegacyPrecision: true), 1.0e-10);
        dstar = (double)ApplySeedDslimMethod.Invoke(null, new object?[] { dstar, theta, 0.0, 1.02, true })!;

        AssertHex(GetFloatFieldHex(iteration7System, "uei"), ToHex((float)uei), "iter=6 accepted state to iter=7 uei");
        AssertHex(GetFloatFieldHex(iteration7System, "theta"), ToHex((float)theta), "iter=6 accepted state to iter=7 theta");
        AssertHex(GetFloatFieldHex(iteration7System, "dstar"), ToHex((float)dstar), "iter=6 accepted state to iter=7 dstar");
        AssertHex(GetFloatFieldHex(iteration7System, "ctau"), ToHex((float)ctau), "iter=6 accepted state to iter=7 ctau");
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_TransitionSeed_Iteration4_BitwiseMatchFortranTrace()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter4_transition_seed_ref"),
            "transition_point_seed",
            "laminar_seed_system");
        string kinematicPath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter4_transition_window_ref"),
            "kinematic_result",
            "laminar_seed_system",
            "transition_point_iteration",
            "transition_final_sensitivities");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "transition_point_seed" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> kinematicRecords = ParityTraceLoader.ReadMatching(
                kinematicPath,
                static record => record.Kind is "kinematic_result" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord kinematicSystem = kinematicRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        ParityTraceRecord carriedStation2Kinematic = kinematicRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence > kinematicSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        BoundaryLayerSystemAssembler.KinematicResult carriedKinematicOverride =
            ReadKinematicResult(carriedStation2Kinematic);
        BoundaryLayerSystemAssembler.PrimaryStationState carriedPrimaryOverride =
            ReadPrimaryState(carriedStation2Kinematic);

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            vector,
            carriedKinematicOverride,
            carriedPrimaryOverride,
            "transition_point_seed",
            "laminar_seed_system");

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        ParityTraceRecord referenceSeed = referenceRecords
            .Where(static record => record.Kind == "transition_point_seed")
            .Where(record => record.Sequence > referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSeed = managedRecords.Single(static record => record.Kind == "transition_point_seed");

        AssertHex(
            ToHex((float)ReadRequiredDouble(carriedStation2Kinematic, "hK2")),
            ToHex((float)ReadRequiredDouble(referenceSeed, "hk2")),
            "iter=4 transition seed carried station2 hk2");
        AssertHex(
            ToHex((float)ReadRequiredDouble(carriedStation2Kinematic, "rT2")),
            ToHex((float)ReadRequiredDouble(referenceSeed, "rt2")),
            "iter=4 transition seed carried station2 rt2");

        AssertIntField(referenceSeed, managedSeed, "idampv", "iter=4 transition seed");

        string[] fields =
        [
            "x1",
            "x2",
            "dx",
            "hk1",
            "t1",
            "rt1",
            "a1",
            "hk2",
            "t2",
            "rt2",
            "a2Input",
            "acrit",
            "ax0",
            "seedAmpl2"
        ];

        foreach (string field in fields)
        {
            AssertFloatFieldHex(referenceSeed, managedSeed, field, $"iter=4 transition seed field={field}");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BlvarTurbulentDiTerms_BitwiseMatchFortranTrace()
    {
        IReadOnlyList<DirectSeedStation15Vector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathWithoutKind(
            Path.Combine(debugDir, "reference", "alpha10_p80_blvar_di_ref"),
            "hst_terms");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceBlvarCandidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "blvar_turbulent_di_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> referenceDUpdateCandidates = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "blvar_turbulent_d_update_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(vectors.Count, referenceSystems.Count);
        Assert.NotEmpty(referenceBlvarCandidates);
        Assert.NotEmpty(referenceDUpdateCandidates);

        for (int index = 0; index < vectors.Count; index++)
        {
            DirectSeedStation15Vector vector = vectors[index];
            ParityTraceRecord referenceSystem = referenceSystems[index];
            ParityTraceRecord referenceBlvar = referenceBlvarCandidates
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();
            string referenceSHex = ToHex((float)ReadRequiredDouble(referenceBlvar, "s"));
            string referenceRtHex = GetFloatFieldHex(referenceBlvar, "rt");
            string referenceHkHex = GetFloatFieldHex(referenceBlvar, "hk");

            IReadOnlyList<ParityTraceRecord> managedRecords;
            if (vector.Iteration is 4 or 5)
            {
                Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;
                (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, BoundaryLayerSystemAssembler.PrimaryStationState? carriedStation2Primary, _) =
                    LoadTransitionWindowVector(vector, vector.Iteration);
                managedRecords = RunManagedTrace(
                    windowVector,
                    carryOverrides.Station1Kinematic,
                    carryOverrides.Station1Secondary,
                    carriedStation2Kinematic,
                    carriedStation2Primary,
                    "blvar_turbulent_d_update_terms",
                    "blvar_turbulent_di_terms");
            }
            else
            {
                managedRecords = RunManagedTrace(
                    vector,
                    "blvar_turbulent_d_update_terms",
                    "blvar_turbulent_di_terms");
            }
            IReadOnlyList<ParityTraceRecord> managedBlvarCandidates = managedRecords
                .Where(static record => record.Kind == "blvar_turbulent_di_terms")
                .OrderBy(static record => record.Sequence)
                .ToArray();
            string managedCandidateHexes = string.Join(
                ";",
                managedBlvarCandidates
                    .Select(record =>
                        $"s={ToHex((float)ReadRequiredDouble(record, "s"))},rt={ToHex((float)ReadRequiredDouble(record, "rt"))}"));
            IReadOnlyList<ParityTraceRecord> matchingManagedBlvarCandidates = managedBlvarCandidates
                .Where(record => ToHex((float)ReadRequiredDouble(record, "s")) == referenceSHex &&
                                 GetFloatFieldHex(record, "rt") == referenceRtHex &&
                                 GetFloatFieldHex(record, "hk") == referenceHkHex)
                .OrderBy(static record => record.Sequence)
                .ToArray();
            ParityTraceRecord? managedBlvar = matchingManagedBlvarCandidates.LastOrDefault();
            Assert.True(
                managedBlvar is not null,
                $"iter={vector.Iteration} no managed blvar_turbulent_di_terms matched s={referenceSHex} rt={referenceRtHex} hk={referenceHkHex}; candidates={managedCandidateHexes}");
            ParityTraceRecord managedBlvarRecord = managedBlvar!;
            ParityTraceRecord referenceDUpdate = referenceDUpdateCandidates
                .Where(record => record.Sequence < referenceSystem.Sequence &&
                                 GetFloatFieldHex(record, "s") == referenceSHex)
                .OrderBy(static record => record.Sequence)
                .Last();
            ParityTraceRecord managedDUpdate = managedRecords
                .Where(static record => record.Kind == "blvar_turbulent_d_update_terms")
                .Where(record => GetFloatFieldHex(record, "s") == referenceSHex)
                .OrderBy(static record => record.Sequence)
                .Last();
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "diWallDPostDfac", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "ddHs", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "hsHk", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "hkD", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "hsD", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "ddUs", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "usD", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "ddD", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "ddlHs", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "ddlUs", $"iter={vector.Iteration} blvar d update");
            AssertFloatFieldHex(referenceDUpdate, managedDUpdate, "ddlD", $"iter={vector.Iteration} blvar d update");

            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "s", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "hk", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "hs", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "us", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "rt", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "cf2t", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "cf2tHk", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "cf2tRt", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "cf2tM", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "cf2tD", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "diWallRaw", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "diWallHs", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "diWallUs", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "diWallCf", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "diWallDPreDfac", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "grt", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "hmin", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "hmRt", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "fl", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "dfac", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "dfHk", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "dfRt", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "dfTermD", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "diWallDPostDfac", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "dd", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "ddHs", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "ddUs", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "ddD", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "ddl", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "ddlHs", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "ddlUs", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "ddlRt", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "ddlD", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "dil", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "dilHk", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "dilRt", $"iter={vector.Iteration} blvar turbulent di");
            bool expectedUsedLaminar = ReadBooleanish(referenceBlvar, "usedLaminar");
            bool actualUsedLaminar = ReadBooleanish(managedBlvarRecord, "usedLaminar");
            Assert.True(
                expectedUsedLaminar == actualUsedLaminar,
                $"iter={vector.Iteration} blvar turbulent di field=usedLaminar expected={expectedUsedLaminar} actual={actualUsedLaminar}");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "finalDi", $"iter={vector.Iteration} blvar turbulent di");
            AssertFloatFieldHex(referenceBlvar, managedBlvarRecord, "finalDiD", $"iter={vector.Iteration} blvar turbulent di");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeedStation15_BlvarTurbulentDiTerms_Iteration4_WindowMatchesReferenceOrder()
    {
        DirectSeedStation15Vector vector = LoadVectors().Single(static candidate => candidate.Iteration == 4);

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_blvar_iter4_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "blvar_turbulent_di_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();
        Assert.NotEmpty(referenceRecords);

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 4));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> referenceWindowAll = referenceRecords
            .Where(record => record.Kind == "blvar_turbulent_di_terms" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;
        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, BoundaryLayerSystemAssembler.PrimaryStationState? carriedStation2Primary, _) =
            LoadTransitionWindowVector(vector, 4);
        IReadOnlyList<ParityTraceRecord> managedWindow = RunManagedTrace(
                windowVector,
                carryOverrides.Station1Kinematic,
                carryOverrides.Station1Secondary,
                carriedStation2Kinematic,
                carriedStation2Primary,
                "blvar_turbulent_di_terms")
            .Where(static record => record.Kind == "blvar_turbulent_di_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.NotEmpty(referenceWindowAll);
        Assert.NotEmpty(managedWindow);

        ParityTraceRecord referenceRecord = referenceWindowAll.Last();
        ParityTraceRecord managedRecord = managedWindow.Last();
        string context =
            $"iter=4 final blvar record reference=[s={GetFloatFieldHex(referenceRecord, "s")},hk={GetFloatFieldHex(referenceRecord, "hk")},rt={GetFloatFieldHex(referenceRecord, "rt")}] " +
            $"managed=[s={GetFloatFieldHex(managedRecord, "s")},hk={GetFloatFieldHex(managedRecord, "hk")},rt={GetFloatFieldHex(managedRecord, "rt")}]";
        AssertFloatFieldHex(referenceRecord, managedRecord, "s", context);
        AssertFloatFieldHex(referenceRecord, managedRecord, "hk", context);
        AssertFloatFieldHex(referenceRecord, managedRecord, "rt", context);
        AssertFloatFieldHex(referenceRecord, managedRecord, "finalDi", context);
        AssertFloatFieldHex(referenceRecord, managedRecord, "finalDiD", context);
    }

    private static IReadOnlyList<DirectSeedStation15Vector> LoadVectors()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string tracePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_directseed_station16_ref"),
            "blsys_interval_inputs",
            "laminar_seed_system");

        IReadOnlyList<ParityTraceRecord> inputRecords = ParityTraceLoader.ReadMatching(
            tracePath,
            static record => record.Kind == "blsys_interval_inputs" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> outputRecords = ParityTraceLoader.ReadMatching(
            tracePath,
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.NotEmpty(inputRecords);
        Assert.NotEmpty(outputRecords);

        var vectors = new List<DirectSeedStation15Vector>(outputRecords.Count);
        foreach (ParityTraceRecord output in outputRecords)
        {
            ParityTraceRecord input = inputRecords
                .Where(record => record.Sequence < output.Sequence)
                .OrderBy(static record => record.Sequence)
                .Last();

            vectors.Add(BuildVector(input, output));
        }

        return vectors;
    }

    private static DirectSeedStation15Vector BuildVector(ParityTraceRecord input, ParityTraceRecord output)
    {
        return new DirectSeedStation15Vector(
            Iteration: (int)ReadRequiredDouble(output, "iteration"),
            Wake: ReadRequiredBoolean(input, "wake"),
            Turb: ReadRequiredBoolean(input, "turb"),
            Tran: ReadRequiredBoolean(input, "tran"),
            Simi: ReadRequiredBoolean(input, "simi"),
            X1: FromSingleHex(GetSingleHex(input, "x1")),
            X2: FromSingleHex(GetSingleHex(input, "x2")),
            U1: FromSingleHex(GetSingleHex(input, "u1")),
            U2: FromSingleHex(GetSingleHex(input, "u2")),
            T1: FromSingleHex(GetSingleHex(input, "t1")),
            T2: FromSingleHex(GetSingleHex(input, "t2")),
            D1: FromSingleHex(GetSingleHex(input, "d1")),
            D2: FromSingleHex(GetSingleHex(input, "d2")),
            S1: FromSingleHex(GetSingleHex(input, "s1")),
            S2: FromSingleHex(GetSingleHex(input, "s2")),
            Dw1: FromSingleHex(GetSingleHex(input, "dw1")),
            Dw2: FromSingleHex(GetSingleHex(input, "dw2")),
            Ampl1: FromSingleHex(GetSingleHex(input, "ampl1")),
            Ampl2: FromSingleHex(GetSingleHex(input, "ampl2")),
            ExpectedHk2Hex: GetSingleHex(output, "hk2"),
            ExpectedHk2T2Hex: GetSingleHex(output, "hk2_T2"),
            ExpectedHk2D2Hex: GetSingleHex(output, "hk2_D2"),
            ExpectedResidual1Hex: GetSingleHex(output, "residual1"),
            ExpectedResidual2Hex: GetSingleHex(output, "residual2"),
            ExpectedResidual3Hex: GetSingleHex(output, "residual3"),
            ExpectedRow11Hex: GetSingleHex(output, "row11"),
            ExpectedRow12Hex: GetSingleHex(output, "row12"),
            ExpectedRow13Hex: GetSingleHex(output, "row13"),
            ExpectedRow14Hex: GetSingleHex(output, "row14"),
            ExpectedRow21Hex: GetSingleHex(output, "row21"),
            ExpectedRow22Hex: GetSingleHex(output, "row22"),
            ExpectedRow23Hex: GetSingleHex(output, "row23"),
            ExpectedRow24Hex: GetSingleHex(output, "row24"),
            ExpectedRow31Hex: GetSingleHex(output, "row31"),
            ExpectedRow32Hex: GetSingleHex(output, "row32"),
            ExpectedRow33Hex: GetSingleHex(output, "row33"),
            ExpectedRow34Hex: GetSingleHex(output, "row34"));
    }

    private static string GetLatestTracePath(string directory)
    {
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }

    private static string GetLatestTracePathWithoutKind(string directory, string excludedKind)
    {
        string? latestClean = Directory
            .EnumerateFiles(directory, "reference_trace*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Reverse()
            .FirstOrDefault(path => !File.ReadAllText(path).Contains($"\"kind\":\"{excludedKind}\"", StringComparison.Ordinal));

        return latestClean ?? GetLatestTracePath(directory);
    }

    private static string GetLatestTracePathContainingKinds(string directory, params string[] requiredKinds)
    {
        string? latestMatching = Directory
            .EnumerateFiles(directory, "reference_trace*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Reverse()
            .FirstOrDefault(path =>
            {
                string text = File.ReadAllText(path);
                return requiredKinds.All(kind => text.Contains($"\"kind\":\"{kind}\"", StringComparison.Ordinal));
            });

        return latestMatching ?? GetLatestTracePath(directory);
    }

    private static (ParityTraceRecord LiveSystem, ParityTraceRecord LiveInput) LoadLiveIterationHandoff(int iteration)
    {
        IReadOnlyList<ParityTraceRecord> liveRecords = RunManagedPreparationTrace(
            "blsys_interval_inputs",
            "laminar_seed_system",
            "transition_seed_system");
        ParityTraceRecord liveSystem = liveRecords
            .Where(record => (record.Kind == "laminar_seed_system" || record.Kind == "transition_seed_system") &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15))
            .Single(record => HasExactDataInt(record, "iteration", iteration));
        ParityTraceRecord liveInput = liveRecords
            .Where(static record => record.Kind == "blsys_interval_inputs" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 15))
            .Where(record => record.Sequence < liveSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        return (liveSystem, liveInput);
    }

    private static Iteration7TransitionWindowRecords LoadIteration7TransitionWindowRecords()
    {
        DirectSeedStation15Vector sourceVector = LoadVectors().Single(static candidate => candidate.Iteration == 6);
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_iter7_transition_window_ref"),
            "transition_point_iteration",
            "transition_final_sensitivities",
            "transition_interval_inputs",
            "laminar_seed_system");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "transition_point_iteration" or "transition_final_sensitivities" or "transition_interval_inputs" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        IReadOnlyList<ParityTraceRecord> referencePointIterations = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceFinalSensitivities = referenceRecords
            .Where(record => record.Kind == "transition_final_sensitivities" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceInputs = referenceRecords
            .Where(record => record.Kind == "transition_interval_inputs" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        (DirectSeedStation15Vector windowVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation2Kinematic, _) =
            LoadTransitionCarriedVector(sourceVector, 7);

        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedTrace(
            windowVector,
            carriedStation2Kinematic,
            station2PrimaryOverride: null,
            "transition_point_iteration",
            "transition_final_sensitivities",
            "transition_interval_inputs");
        ParityTraceRecord managedInputs = managedRecords
            .Where(static record => record.Kind == "transition_interval_inputs")
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedFinalSensitivities = managedRecords
            .Where(static record => record.Kind == "transition_final_sensitivities")
            .Where(record => record.Sequence < managedInputs.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        IReadOnlyList<ParityTraceRecord> managedPointIterations = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .Where(record => record.Sequence < managedFinalSensitivities.Sequence)
            .OrderBy(static record => record.Sequence)
            .TakeLast(referencePointIterations.Count)
            .ToArray();

        return new Iteration7TransitionWindowRecords(
            referencePointIterations,
            referenceFinalSensitivities,
            referenceInputs,
            managedPointIterations,
            managedFinalSensitivities,
            managedInputs);
    }

    private static DirectSeedStation15Vector LoadIteration7BldifProducerVector()
    {
        DirectSeedStation15Vector sourceVector = LoadVectors().Single(static candidate => candidate.Iteration == 7);
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", "alpha10_p80_bldif_station1_iter7_producer_ref"),
            "laminar_seed_system",
            "bldif_primary_station");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_primary_station")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             HasExactDataInt(record, "iteration", 7));
        ParityTraceRecord referenceStation1 = referenceRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 1) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord referenceStation2 = referenceRecords
            .Where(record => record.Kind == "bldif_primary_station" &&
                             HasExactDataInt(record, "ityp", 2) &&
                             HasExactDataInt(record, "station", 2) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        return sourceVector with
        {
            X1 = ReadRequiredDouble(referenceStation1, "x"),
            U1 = ReadRequiredDouble(referenceStation1, "u"),
            T1 = ReadRequiredDouble(referenceStation1, "t"),
            D1 = ReadRequiredDouble(referenceStation1, "d"),
            S1 = ReadRequiredDouble(referenceStation1, "s"),
            X2 = ReadRequiredDouble(referenceStation2, "x"),
            U2 = ReadRequiredDouble(referenceStation2, "u"),
            T2 = ReadRequiredDouble(referenceStation2, "t"),
            D2 = ReadRequiredDouble(referenceStation2, "d"),
            S2 = ReadRequiredDouble(referenceStation2, "s")
        };
    }

    private static bool HasExactDataInt(ParityTraceRecord record, string field, int expected)
    {
        return record.TryGetDataField(field, out var value) &&
               value.ValueKind == System.Text.Json.JsonValueKind.Number &&
               value.TryGetInt32(out int actual) &&
               actual == expected;
    }

    private static bool ReadRequiredBoolean(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out var value), $"Missing data field '{field}' in {record.Kind}.");
        return value.GetBoolean();
    }

    private static double ReadRequiredDouble(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out var value), $"Missing data field '{field}' in {record.Kind}.");
        return value.GetDouble();
    }

    private static bool ReadBooleanish(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out var value), $"Missing data field '{field}' in {record.Kind}.");
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Number => value.GetDouble() != 0.0,
            _ => throw new InvalidOperationException($"Field '{field}' in {record.Kind} is not booleanish.")
        };
    }

    private static int ReadRequiredInt(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out var value), $"Missing data field '{field}' in {record.Kind}.");
        return value.GetInt32();
    }

    private static double ReadOptionalDouble(ParityTraceRecord record, string field, double defaultValue = 0.0)
    {
        return record.TryGetDataField(field, out var value)
            ? value.GetDouble()
            : defaultValue;
    }

    private static string GetSingleHex(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataBits(field, out IReadOnlyDictionary<string, string>? bits), $"Missing dataBits for '{field}' in {record.Kind}.");
        Assert.NotNull(bits);
        Assert.True(bits!.TryGetValue("f32", out string? hex), $"Missing f32 bits for '{field}' in {record.Kind}.");
        Assert.False(string.IsNullOrWhiteSpace(hex), $"Empty f32 bits for '{field}' in {record.Kind}.");
        return hex!;
    }

    private static double FromSingleHex(string hex)
    {
        string normalized = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? hex[2..]
            : hex;
        uint bits = uint.Parse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    private static string ToHex(float value)
    {
        return $"0x{BitConverter.SingleToUInt32Bits(value):X8}";
    }

    private static object GetProperty(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {instance.GetType().FullName}.");
        return property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' on {instance.GetType().FullName} evaluated to null.");
    }

    private static (DirectSeedStation15Vector WindowVector, BoundaryLayerSystemAssembler.KinematicResult CarriedStation2Kinematic, BoundaryLayerSystemAssembler.PrimaryStationState? CarriedStation2Primary, ParityTraceRecord CarriedStation2State) LoadTransitionWindowVector(
        DirectSeedStation15Vector vector,
        int iteration)
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        int captureIteration = ResolveTransitionWindowCaptureIteration(iteration);
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", $"alpha10_p80_iter{captureIteration}_transition_window_ref"));

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "transition_point_iteration" or "transition_interval_inputs" or "kinematic_result" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            record => record.Kind == "laminar_seed_system" &&
                      HasExactDataInt(record, "side", 1) &&
                      HasExactDataInt(record, "station", 15) &&
                      HasExactDataInt(record, "iteration", iteration));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord firstReferencePointIteration = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord carriedStation2State = referenceRecords
            .Where(record => record.Kind == "kinematic_result" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < firstReferencePointIteration.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord transitionIntervalInputs = referenceRecords
            .Where(record => record.Kind == "transition_interval_inputs" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        DirectSeedStation15Vector windowVector = vector with
        {
            // The last BLVAR event ahead of the laminar seed uses the accepted
            // transition-point state (x1/t1/d1/u1/st), not the original
            // downstream station-2 carry state (x2/t2/d2/u2/s2Original).
            X2 = FromSingleHex(GetSingleHex(transitionIntervalInputs, "x1")),
            U2 = FromSingleHex(GetSingleHex(transitionIntervalInputs, "u1")),
            T2 = FromSingleHex(GetSingleHex(transitionIntervalInputs, "t1")),
            D2 = FromSingleHex(GetSingleHex(transitionIntervalInputs, "d1")),
            S2 = FromSingleHex(GetSingleHex(transitionIntervalInputs, "st"))
        };

        BoundaryLayerSystemAssembler.KinematicResult preBlvarStation2Kinematic =
            BoundaryLayerSystemAssembler.ComputeKinematicParameters(
                windowVector.U2,
                windowVector.T2,
                windowVector.D2,
                windowVector.Dw2,
                LegacyIncompressibleParityConstants.Hstinv,
                LegacyIncompressibleParityConstants.HstinvMs,
                LegacyIncompressibleParityConstants.Gm1Bl,
                LegacyIncompressibleParityConstants.Rstbl,
                LegacyIncompressibleParityConstants.RstblMs,
                LegacyIncompressibleParityConstants.Hvrat,
                LegacyIncompressibleParityConstants.Reybl,
                LegacyIncompressibleParityConstants.ReyblRe,
                LegacyIncompressibleParityConstants.ReyblMs,
                useLegacyPrecision: true);

        return (windowVector, preBlvarStation2Kinematic, null, transitionIntervalInputs);
    }

    private static ParityTraceRecord LoadTransitionCarriedStation2State(int iteration)
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        int captureIteration = ResolveTransitionWindowCaptureIteration(iteration);
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", $"alpha10_p80_iter{captureIteration}_transition_window_ref"),
            "kinematic_result",
            "laminar_seed_system",
            "transition_point_iteration");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "transition_point_iteration" or "kinematic_result" or "laminar_seed_system")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            record => record.Kind == "laminar_seed_system" &&
                      HasExactDataInt(record, "side", 1) &&
                      HasExactDataInt(record, "station", 15) &&
                      HasExactDataInt(record, "iteration", iteration));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord firstReferencePointIteration = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();

        return referenceRecords
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < firstReferencePointIteration.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
    }

    private static (DirectSeedStation15Vector WindowVector, BoundaryLayerSystemAssembler.KinematicResult CarriedStation2Kinematic, ParityTraceRecord CarriedStation2State) LoadTransitionCarriedVector(
        DirectSeedStation15Vector vector,
        int iteration)
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        int captureIteration = ResolveTransitionWindowCaptureIteration(iteration);
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", $"alpha10_p80_iter{captureIteration}_transition_window_ref"),
            "kinematic_result",
            "laminar_seed_system",
            "transition_point_iteration",
            "transition_interval_inputs");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "transition_point_iteration" or "kinematic_result" or "laminar_seed_system" or "transition_interval_inputs")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            record => record.Kind == "laminar_seed_system" &&
                      HasExactDataInt(record, "side", 1) &&
                      HasExactDataInt(record, "station", 15) &&
                      HasExactDataInt(record, "iteration", iteration));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord firstReferencePointIteration = referenceRecords
            .Where(record => record.Kind == "transition_point_iteration" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord carriedStation2State = referenceRecords
            .Where(record => record.Kind == "kinematic_result" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < firstReferencePointIteration.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord transitionIntervalInputs = referenceRecords
            .Where(record => record.Kind == "transition_interval_inputs" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        DirectSeedStation15Vector windowVector = vector with
        {
            U2 = FromSingleHex(GetSingleHex(carriedStation2State, "u2")),
            T2 = FromSingleHex(GetSingleHex(carriedStation2State, "t2")),
            D2 = FromSingleHex(GetSingleHex(carriedStation2State, "d2")),
            S2 = FromSingleHex(GetFloatFieldHex(transitionIntervalInputs, "s2Original"))
        };

        return (windowVector, ReadKinematicResult(carriedStation2State), carriedStation2State);
    }

    private static int ResolveTransitionWindowCaptureIteration(int iteration)
    {
        return iteration == 3 ? 4 : iteration;
    }

    private static (DirectSeedStation15Vector Vector, BoundaryLayerSystemAssembler.KinematicResult CarriedStation1Kinematic) LoadTransitionFinalVector(
        DirectSeedStation15Vector vector,
        int iteration)
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePathContainingKinds(
            Path.Combine(debugDir, "reference", $"alpha10_p80_iter{iteration}_transition_window_ref"),
            "kinematic_result",
            "laminar_seed_system",
            "transition_interval_inputs");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "kinematic_result" or "laminar_seed_system" or "transition_interval_inputs")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            record => record.Kind == "laminar_seed_system" &&
                      HasExactDataInt(record, "side", 1) &&
                      HasExactDataInt(record, "station", 15) &&
                      HasExactDataInt(record, "iteration", iteration));
        ParityTraceRecord previousReferenceSystem = referenceRecords
            .Where(record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 15) &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord transitionIntervalInputs = referenceRecords
            .Where(record => record.Kind == "transition_interval_inputs" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string carriedUHex = GetSingleHex(transitionIntervalInputs, "u1");
        string carriedTHex = GetSingleHex(transitionIntervalInputs, "t1");
        string carriedDHex = GetSingleHex(transitionIntervalInputs, "d1");
        ParityTraceRecord carriedStation1State = referenceRecords
            .Where(record => record.Kind == "kinematic_result" &&
                             record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < transitionIntervalInputs.Sequence)
            .Where(record => GetSingleHex(record, "u2") == carriedUHex)
            .Where(record => GetSingleHex(record, "t2") == carriedTHex)
            .Where(record => GetSingleHex(record, "d2") == carriedDHex)
            .OrderBy(static record => record.Sequence)
            .Last();

        DirectSeedStation15Vector transitionFinalVector = vector with
        {
            X1 = FromSingleHex(GetSingleHex(transitionIntervalInputs, "x1")),
            U1 = FromSingleHex(carriedUHex),
            T1 = FromSingleHex(carriedTHex),
            D1 = FromSingleHex(carriedDHex),
            S1 = FromSingleHex(GetFloatFieldHex(transitionIntervalInputs, "st"))
        };

        return (transitionFinalVector, ReadKinematicResult(carriedStation1State));
    }

    private static string DescribeMatchingFusedCandidates(ParityTraceRecord managedBt2, int expectedFinalBits)
    {
        float baseValue = (float)ReadRequiredDouble(managedBt2, "baseVs2");
        var terms = new (float Left, float Right, string Label)[]
        {
            ((float)ReadRequiredDouble(managedBt2, "source1"), (float)ReadRequiredDouble(managedBt2, "coeff1"), "st"),
            ((float)ReadRequiredDouble(managedBt2, "source2"), (float)ReadRequiredDouble(managedBt2, "coeff2"), "tt"),
            ((float)ReadRequiredDouble(managedBt2, "source3"), (float)ReadRequiredDouble(managedBt2, "coeff3"), "dt"),
            ((float)ReadRequiredDouble(managedBt2, "source4"), (float)ReadRequiredDouble(managedBt2, "coeff4"), "ut"),
            ((float)ReadRequiredDouble(managedBt2, "source5"), (float)ReadRequiredDouble(managedBt2, "coeff5"), "xt"),
        };

        var matches = new List<string>();
        int[] permutation = { 0, 1, 2, 3, 4 };
        SearchFusedCandidatePermutations(terms, permutation, 0, baseValue, expectedFinalBits, matches);

        return matches.Count == 0
            ? "none"
            : string.Join(";", matches);
    }

    private static void SearchFusedCandidatePermutations(
        (float Left, float Right, string Label)[] terms,
        int[] permutation,
        int index,
        float baseValue,
        int expectedFinalBits,
        List<string> matches)
    {
        if (index == permutation.Length)
        {
            float baseFirst = baseValue;
            for (int i = 0; i < permutation.Length; i++)
            {
                int termIndex = permutation[i];
                baseFirst = MathF.FusedMultiplyAdd(terms[termIndex].Left, terms[termIndex].Right, baseFirst);
            }

            if (unchecked((int)BitConverter.SingleToUInt32Bits(baseFirst)) == expectedFinalBits)
            {
                matches.Add("baseFirst:" + string.Join(",", permutation.Select(i => terms[i].Label)));
            }

            float productsFirst = terms[permutation[0]].Left * terms[permutation[0]].Right;
            for (int i = 1; i < permutation.Length; i++)
            {
                int termIndex = permutation[i];
                productsFirst = MathF.FusedMultiplyAdd(terms[termIndex].Left, terms[termIndex].Right, productsFirst);
            }

            productsFirst = productsFirst + baseValue;
            if (unchecked((int)BitConverter.SingleToUInt32Bits(productsFirst)) == expectedFinalBits)
            {
                matches.Add("productsFirst:" + string.Join(",", permutation.Select(i => terms[i].Label)));
            }

            float roundedBaseThenFirst = baseValue + (terms[permutation[0]].Left * terms[permutation[0]].Right);
            for (int i = 1; i < permutation.Length; i++)
            {
                int termIndex = permutation[i];
                roundedBaseThenFirst = MathF.FusedMultiplyAdd(
                    terms[termIndex].Left,
                    terms[termIndex].Right,
                    roundedBaseThenFirst);
            }

            if (unchecked((int)BitConverter.SingleToUInt32Bits(roundedBaseThenFirst)) == expectedFinalBits)
            {
                matches.Add("roundedBaseThenFirst:" + string.Join(",", permutation.Select(i => terms[i].Label)));
            }

            return;
        }

        for (int i = index; i < permutation.Length; i++)
        {
            (permutation[index], permutation[i]) = (permutation[i], permutation[index]);
            SearchFusedCandidatePermutations(terms, permutation, index + 1, baseValue, expectedFinalBits, matches);
            (permutation[index], permutation[i]) = (permutation[i], permutation[index]);
        }
    }

    private static BoundaryLayerSystemAssembler.BlsysResult AssembleVector(
        DirectSeedStation15Vector vector,
        bool includeTraceContext,
        BoundaryLayerSystemAssembler.KinematicResult? station1KinematicOverride = null,
        BoundaryLayerSystemAssembler.SecondaryStationResult? station1SecondaryOverride = null,
        BoundaryLayerSystemAssembler.KinematicResult? station2KinematicOverride = null,
        BoundaryLayerSystemAssembler.PrimaryStationState? station2PrimaryOverride = null,
        bool useDefaultStation1Carry = true)
    {
        DirectSeedStation15Vector resolvedVector = vector;
        BoundaryLayerSystemAssembler.KinematicResult? resolvedStation1KinematicOverride =
            station1KinematicOverride?.Clone();
        BoundaryLayerSystemAssembler.SecondaryStationResult? resolvedStation1SecondaryOverride =
            station1SecondaryOverride?.Clone();

        if (useDefaultStation1Carry &&
            resolvedStation1KinematicOverride is null &&
            resolvedStation1SecondaryOverride is null)
        {
            if (vector.Iteration is 4 or 5)
            {
                (resolvedVector, BoundaryLayerSystemAssembler.KinematicResult carriedStation1Kinematic) =
                    LoadTransitionFinalVector(vector, vector.Iteration);
                resolvedStation1KinematicOverride = carriedStation1Kinematic;
                resolvedStation1SecondaryOverride = null;
            }
            else
            {
                Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;
                resolvedStation1KinematicOverride = carryOverrides.Station1Kinematic;
                resolvedStation1SecondaryOverride = carryOverrides.Station1Secondary;
            }
        }

        return BoundaryLayerSystemAssembler.AssembleStationSystem(
            isWake: resolvedVector.Wake,
            isTurbOrTran: resolvedVector.Turb || resolvedVector.Tran,
            isTran: resolvedVector.Tran,
            isSimi: resolvedVector.Simi,
            x1: resolvedVector.X1,
            x2: resolvedVector.X2,
            uei1: resolvedVector.U1,
            uei2: resolvedVector.U2,
            t1: resolvedVector.T1,
            t2: resolvedVector.T2,
            d1: resolvedVector.D1,
            d2: resolvedVector.D2,
            s1: resolvedVector.S1,
            s2: resolvedVector.S2,
            dw1: resolvedVector.Dw1,
            dw2: resolvedVector.Dw2,
            ampl1: resolvedVector.Ampl1,
            ampl2: resolvedVector.Ampl2,
            amcrit: (double)(float)9.0f,
            tkbl: LegacyIncompressibleParityConstants.Tkbl,
            qinfbl: LegacyIncompressibleParityConstants.Qinfbl,
            tkbl_ms: LegacyIncompressibleParityConstants.TkblMs,
            hstinv: LegacyIncompressibleParityConstants.Hstinv,
            hstinv_ms: LegacyIncompressibleParityConstants.HstinvMs,
            gm1bl: LegacyIncompressibleParityConstants.Gm1Bl,
            rstbl: LegacyIncompressibleParityConstants.Rstbl,
            rstbl_ms: LegacyIncompressibleParityConstants.RstblMs,
            hvrat: LegacyIncompressibleParityConstants.Hvrat,
            reybl: LegacyIncompressibleParityConstants.Reybl,
            reybl_re: LegacyIncompressibleParityConstants.ReyblRe,
            reybl_ms: LegacyIncompressibleParityConstants.ReyblMs,
            useLegacyPrecision: true,
            station1KinematicOverride: resolvedStation1KinematicOverride,
            station1SecondaryOverride: resolvedStation1SecondaryOverride,
            station2KinematicOverride: station2KinematicOverride,
            station2PrimaryOverride: station2PrimaryOverride,
            traceSide: includeTraceContext ? 1 : null,
            traceStation: includeTraceContext ? 15 : null,
            traceIteration: includeTraceContext ? resolvedVector.Iteration : null,
            tracePhase: includeTraceContext ? "mrchue" : null);
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedTrace(DirectSeedStation15Vector vector, params string[] kinds)
    {
        Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;
        return RunManagedTraceCore(
            vector,
            station1KinematicOverride: carryOverrides.Station1Kinematic,
            station1SecondaryOverride: carryOverrides.Station1Secondary,
            station2KinematicOverride: null,
            station2PrimaryOverride: null,
            useDefaultStation1Carry: true,
            kinds);
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedTrace(
        DirectSeedStation15Vector vector,
        BoundaryLayerSystemAssembler.KinematicResult? station2KinematicOverride,
        BoundaryLayerSystemAssembler.PrimaryStationState? station2PrimaryOverride,
        params string[] kinds)
    {
        Station15CarryOverrides carryOverrides = CachedStation15CarryOverrides.Value;
        return RunManagedTraceCore(
            vector,
            station1KinematicOverride: carryOverrides.Station1Kinematic,
            station1SecondaryOverride: carryOverrides.Station1Secondary,
            station2KinematicOverride: station2KinematicOverride,
            station2PrimaryOverride: station2PrimaryOverride,
            useDefaultStation1Carry: true,
            kinds);
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedTrace(
        DirectSeedStation15Vector vector,
        BoundaryLayerSystemAssembler.KinematicResult? station1KinematicOverride,
        BoundaryLayerSystemAssembler.SecondaryStationResult? station1SecondaryOverride,
        BoundaryLayerSystemAssembler.KinematicResult? station2KinematicOverride,
        BoundaryLayerSystemAssembler.PrimaryStationState? station2PrimaryOverride,
        params string[] kinds)
    {
        return RunManagedTraceCore(
            vector,
            station1KinematicOverride,
            station1SecondaryOverride,
            station2KinematicOverride,
            station2PrimaryOverride,
            useDefaultStation1Carry: true,
            kinds);
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedTraceCore(
        DirectSeedStation15Vector vector,
        BoundaryLayerSystemAssembler.KinematicResult? station1KinematicOverride,
        BoundaryLayerSystemAssembler.SecondaryStationResult? station1SecondaryOverride,
        BoundaryLayerSystemAssembler.KinematicResult? station2KinematicOverride,
        BoundaryLayerSystemAssembler.PrimaryStationState? station2PrimaryOverride,
        bool useDefaultStation1Carry,
        params string[] kinds)
    {
        var lines = new List<string>();
        using (var traceWriter = new JsonlTraceWriter(
                   TextWriter.Null,
                   runtime: "csharp",
                   session: new { caseName = "direct-seed-station15-system-micro" },
                   serializedRecordObserver: lines.Add))
        {
            using var traceScope = SolverTrace.Begin(traceWriter);
            _ = AssembleVector(
                vector,
                includeTraceContext: true,
                station1KinematicOverride: station1KinematicOverride,
                station1SecondaryOverride: station1SecondaryOverride,
                station2KinematicOverride: station2KinematicOverride,
                station2PrimaryOverride: station2PrimaryOverride,
                useDefaultStation1Carry: useDefaultStation1Carry);
        }

        HashSet<string> requiredKinds = new(kinds, StringComparer.Ordinal);
        return lines
            .Select(ParityTraceLoader.DeserializeLine)
            .Where(static record => record is not null)
            .Select(static record => record!)
            .Where(record => requiredKinds.Contains(record.Kind))
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedPreparationTrace(params string[] kinds)
    {
        FortranReferenceCase definition = FortranReferenceCases.Get(CaseId);
        AnalysisSettings settings = new(
            panelCount: definition.PanelCount,
            reynoldsNumber: definition.ReynoldsNumber,
            machNumber: 0.0,
            inviscidSolverType: InviscidSolverType.LinearVortex,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: definition.MaxViscousIterations,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: definition.CriticalAmplificationFactor,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyWakeSourceKernelPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);
        double alphaRadians = definition.AlphaDegrees * Math.PI / 180.0;

        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic(
            definition.AirfoilCode,
            ClassicXFoilNacaPointCount);
        var x = new double[geometry.Points.Count];
        var y = new double[geometry.Points.Count];
        for (int i = 0; i < geometry.Points.Count; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        var lines = new List<string>();
        using (var traceWriter = new JsonlTraceWriter(
                   TextWriter.Null,
                   runtime: "csharp",
                   session: new { caseName = "direct-seed-station15-full-prep" },
                   serializedRecordObserver: lines.Add))
        {
            using var traceScope = SolverTrace.Begin(traceWriter);
            _ = ViscousSolverEngine.PrepareLegacyPreNewtonContext((x, y), settings, alphaRadians);
        }

        HashSet<string> requiredKinds = new(kinds, StringComparer.Ordinal);
        return lines
            .Select(ParityTraceLoader.DeserializeLine)
            .Where(static record => record is not null)
            .Select(static record => record!)
            .Where(record => requiredKinds.Contains(record.Kind))
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static BoundaryLayerSystemAssembler.KinematicResult ReadKinematicResult(ParityTraceRecord record)
    {
        return new BoundaryLayerSystemAssembler.KinematicResult
        {
            M2 = ReadRequiredDouble(record, "m2"),
            M2_U2 = ReadRequiredDouble(record, "m2_u2"),
            M2_MS = ReadRequiredDouble(record, "m2_ms"),
            R2 = ReadRequiredDouble(record, "r2"),
            R2_U2 = ReadRequiredDouble(record, "r2_u2"),
            R2_MS = ReadRequiredDouble(record, "r2_ms"),
            H2 = ReadRequiredDouble(record, "h2"),
            H2_D2 = ReadOptionalDouble(record, "h2_d2"),
            H2_T2 = ReadOptionalDouble(record, "h2_t2"),
            HK2 = ReadRequiredDouble(record, "hK2"),
            HK2_U2 = ReadRequiredDouble(record, "hK2_u2"),
            HK2_T2 = ReadRequiredDouble(record, "hK2_t2"),
            HK2_D2 = ReadRequiredDouble(record, "hK2_d2"),
            HK2_MS = ReadRequiredDouble(record, "hK2_ms"),
            RT2 = ReadRequiredDouble(record, "rT2"),
            RT2_U2 = ReadRequiredDouble(record, "rT2_u2"),
            RT2_T2 = ReadRequiredDouble(record, "rT2_t2"),
            RT2_MS = ReadRequiredDouble(record, "rT2_ms"),
            RT2_RE = ReadRequiredDouble(record, "rT2_re")
        };
    }

    private static BoundaryLayerSystemAssembler.PrimaryStationState ReadPrimaryState(ParityTraceRecord record)
    {
        return new BoundaryLayerSystemAssembler.PrimaryStationState
        {
            U = ReadRequiredDouble(record, "u2"),
            T = ReadRequiredDouble(record, "t2"),
            D = ReadRequiredDouble(record, "d2")
        };
    }

    private static Station15CarryOverrides LoadStation15CarryOverrides()
    {
        FortranReferenceCase definition = FortranReferenceCases.Get(CaseId);
        AnalysisSettings settings = new(
            panelCount: definition.PanelCount,
            reynoldsNumber: definition.ReynoldsNumber,
            machNumber: 0.0,
            inviscidSolverType: InviscidSolverType.LinearVortex,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: definition.MaxViscousIterations,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: definition.CriticalAmplificationFactor,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyWakeSourceKernelPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);
        double alphaRadians = definition.AlphaDegrees * Math.PI / 180.0;

        var geometry = new NacaAirfoilGenerator().Generate4DigitClassic(
            definition.AirfoilCode,
            ClassicXFoilNacaPointCount);
        var x = new double[geometry.Points.Count];
        var y = new double[geometry.Points.Count];
        for (int i = 0; i < geometry.Points.Count; i++)
        {
            x[i] = geometry.Points[i].X;
            y[i] = geometry.Points[i].Y;
        }

        ViscousSolverEngine.PreNewtonSetupContext context = ViscousSolverEngine.PrepareLegacyPreNewtonContext((x, y), settings, alphaRadians);
        BoundaryLayerSystemAssembler.KinematicResult? station1Kinematic = context.BoundaryLayerState.LegacyKinematic[13, 0];
        BoundaryLayerSystemAssembler.SecondaryStationResult? station1Secondary = context.BoundaryLayerState.LegacySecondary[13, 0];

        Assert.NotNull(station1Kinematic);
        Assert.NotNull(station1Secondary);

        return new Station15CarryOverrides(
            station1Kinematic!.Clone(),
            station1Secondary!.Clone());
    }

    private static string DescribePrimaryStationCandidates(IReadOnlyList<ParityTraceRecord> records, int station)
    {
        return string.Join(
            ";",
            records
                .Where(record => record.Kind == "bldif_primary_station" &&
                                 HasExactDataInt(record, "ityp", 2) &&
                                 HasExactDataInt(record, "station", station))
                .Select(record =>
                    $"seq={record.Sequence},u={ToHex((float)ReadRequiredDouble(record, "u"))},t={ToHex((float)ReadRequiredDouble(record, "t"))},d={ToHex((float)ReadRequiredDouble(record, "d"))},hk={ToHex((float)ReadRequiredDouble(record, "hk"))}"));
    }

    private static void AssertIntField(ParityTraceRecord expected, ParityTraceRecord actual, string field, string context)
    {
        int expectedValue = ReadRequiredInt(expected, field);
        int actualValue = ReadRequiredInt(actual, field);
        Assert.True(expectedValue == actualValue, $"{context} field={field} expected={expectedValue} actual={actualValue}");
    }

    private static void AssertHex(string expected, string actual, string context)
    {
        Assert.True(
            string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase),
            $"{context} expected={expected} actual={actual}");
    }

    private static void AssertFloatFieldHex(ParityTraceRecord expected, ParityTraceRecord actual, string field, string context)
    {
        string expectedHex = GetFloatFieldHex(expected, field);
        string actualHex = GetFloatFieldHex(actual, field);
        AssertHex(expectedHex, actualHex, $"{context} field={field}");
    }

    private static string GetFloatFieldHex(ParityTraceRecord record, string field)
    {
        if (record.TryGetDataBits(field, out IReadOnlyDictionary<string, string>? bits) &&
            bits is not null &&
            bits.TryGetValue("f32", out string? existingHex) &&
            !string.IsNullOrWhiteSpace(existingHex))
        {
            return existingHex!;
        }

        if (record.TryGetDataField($"{field}Bits", out var bitValue) &&
            bitValue.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            string? hex = bitValue.GetString();
            Assert.False(string.IsNullOrWhiteSpace(hex), $"Empty traced bits for '{field}' in {record.Kind}.");
            return hex!;
        }

        if (record.TryGetDataField($"{field}Bits", out bitValue) &&
            bitValue.ValueKind == System.Text.Json.JsonValueKind.Number &&
            bitValue.TryGetInt32(out int numericBits))
        {
            return $"0x{unchecked((uint)numericBits):X8}";
        }

        return ToHex((float)ReadRequiredDouble(record, field));
    }

    private sealed record DirectSeedStation15Vector(
        int Iteration,
        bool Wake,
        bool Turb,
        bool Tran,
        bool Simi,
        double X1,
        double X2,
        double U1,
        double U2,
        double T1,
        double T2,
        double D1,
        double D2,
        double S1,
        double S2,
        double Dw1,
        double Dw2,
        double Ampl1,
        double Ampl2,
        string ExpectedHk2Hex,
        string ExpectedHk2T2Hex,
        string ExpectedHk2D2Hex,
        string ExpectedResidual1Hex,
        string ExpectedResidual2Hex,
        string ExpectedResidual3Hex,
        string ExpectedRow11Hex,
        string ExpectedRow12Hex,
        string ExpectedRow13Hex,
        string ExpectedRow14Hex,
        string ExpectedRow21Hex,
        string ExpectedRow22Hex,
        string ExpectedRow23Hex,
        string ExpectedRow24Hex,
        string ExpectedRow31Hex,
        string ExpectedRow32Hex,
        string ExpectedRow33Hex,
        string ExpectedRow34Hex);

    private sealed record Station15CarryOverrides(
        BoundaryLayerSystemAssembler.KinematicResult Station1Kinematic,
        BoundaryLayerSystemAssembler.SecondaryStationResult Station1Secondary);

    private sealed record Iteration7TransitionWindowRecords(
        IReadOnlyList<ParityTraceRecord> ReferencePointIterations,
        ParityTraceRecord ReferenceFinalSensitivities,
        ParityTraceRecord ReferenceTransitionInputs,
        IReadOnlyList<ParityTraceRecord> ManagedPointIterations,
        ParityTraceRecord ManagedFinalSensitivities,
        ParityTraceRecord ManagedTransitionInputs);
}
