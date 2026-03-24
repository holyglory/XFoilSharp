using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: MRCHUE similarity-seed handoff into the first downstream laminar station
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_seedhandoff_ref1/reference_trace*.jsonl and tools/fortran-debug/reference/alpha10_p80_similarity_seed_s1st2_ref/reference_trace*.jsonl
// Role in port: Replays the real managed similarity-station helper followed by the real managed laminar-seed helper on a tiny in-memory BL state so the station-2 COM1 carry and the station-3 seed refinement can be checked without a whole-solver run.
// Differences: The harness is managed-only infrastructure, but it invokes the actual parity helpers and compares their accepted station state against authoritative Fortran trace words.
// Decision: Keep the micro-engine because station 3 depends on the carried COM1 snapshot from station 2, so this higher-level local rig is the smallest faithful oracle for that handoff.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class LaminarSeedStations3To5MethodMicroParityTests
{
    private const string SimilaritySeedReferenceDirectory = "alpha10_p80_similarity_seed_s1st2_ref";
    private const string Station3Iteration2Eq3ReferenceDirectory = "alpha10_p80_station3_iter2_eq3_ref";
    private const string Station3Iteration2BlmidReferenceDirectory = "alpha10_p80_station3_iter2_blmid_ref";
    private const string Station3Iteration2KinematicReferenceDirectory = "alpha10_p80_station3_iter2_kinematic_ref";
    private const string Station3Iteration2CommonReferenceDirectory = "alpha10_p80_station3_iter2_common_ref";
    private const string Station4Iteration1Eq3ReferenceDirectory = "alpha10_p80_station4_iter1_eq3_ref";

    private static readonly MethodInfo RefineSimilarityStationSeedMethod = typeof(ViscousSolverEngine).GetMethod(
        "RefineSimilarityStationSeed",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RefineSimilarityStationSeed method not found.");

    private static readonly MethodInfo RefineLaminarSeedStationMethod = typeof(ViscousSolverEngine).GetMethod(
        "RefineLaminarSeedStation",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RefineLaminarSeedStation method not found.");

    private static readonly MethodInfo ComputeCompressibilityParametersMethod = typeof(ViscousSolverEngine).GetMethod(
        "ComputeCompressibilityParameters",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ComputeCompressibilityParameters method not found.");

    private static readonly MethodInfo GetHvRatMethod = typeof(ViscousSolverEngine).GetMethod(
        "GetHvRat",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("GetHvRat method not found.");

    [Fact]
    public void Alpha10_P80_LaminarSeedStations3To5_FinalStates_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        BoundaryLayerSystemState blState = run.State;
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            SimilaritySeedReferenceDirectory));

        AssertHex(GetSingleHex(data.Station2FinalRecord, "theta"), ToHex((float)blState.THET[1, 0]), "station2 final theta");
        AssertHex(GetSingleHex(data.Station2FinalRecord, "dstar"), ToHex((float)blState.DSTR[1, 0]), "station2 final dstar");
        Assert.NotNull(blState.LegacyKinematic[1, 0]);
        Assert.NotNull(blState.LegacySecondary[1, 0]);

        foreach (DownstreamStationCase stationCase in data.DownstreamStations)
        {
            int ibl = stationCase.TraceStation - 1;
            ParityTraceRecord managedInput = GetFirstTraceRecord(run.Records, "blsys_interval_inputs", stationCase.TraceStation);
            IReadOnlyList<ParityTraceRecord> referenceSystems = GetTraceRecordsFromFile(referencePath, "laminar_seed_system", stationCase.TraceStation);
            IReadOnlyList<ParityTraceRecord> managedSystems = GetTraceRecords(run.Records, "laminar_seed_system", stationCase.TraceStation);
            IReadOnlyList<ParityTraceRecord> referenceSteps = GetTraceRecordsFromFile(referencePath, "laminar_seed_step", stationCase.TraceStation);
            IReadOnlyList<ParityTraceRecord> managedSteps = GetTraceRecords(run.Records, "laminar_seed_step", stationCase.TraceStation);

            Assert.Equal(referenceSystems.Count, managedSystems.Count);
            Assert.Equal(referenceSteps.Count, managedSteps.Count);

            AssertHex(GetSingleHex(stationCase.InitialInput, "x1"), GetSingleHex(managedInput, "x1"), $"station{stationCase.TraceStation} handoff x1");
            AssertHex(GetSingleHex(stationCase.InitialInput, "x2"), GetSingleHex(managedInput, "x2"), $"station{stationCase.TraceStation} handoff x2");
            AssertHex(GetSingleHex(stationCase.InitialInput, "u1"), GetSingleHex(managedInput, "u1"), $"station{stationCase.TraceStation} handoff u1");
            AssertHex(GetSingleHex(stationCase.InitialInput, "u2"), GetSingleHex(managedInput, "u2"), $"station{stationCase.TraceStation} handoff u2");
            AssertHex(GetSingleHex(stationCase.InitialInput, "t1"), GetSingleHex(managedInput, "t1"), $"station{stationCase.TraceStation} handoff t1");
            AssertHex(GetSingleHex(stationCase.InitialInput, "t2"), GetSingleHex(managedInput, "t2"), $"station{stationCase.TraceStation} handoff t2");
            AssertHex(GetSingleHex(stationCase.InitialInput, "d1"), GetSingleHex(managedInput, "d1"), $"station{stationCase.TraceStation} handoff d1");
            AssertHex(GetSingleHex(stationCase.InitialInput, "d2"), GetSingleHex(managedInput, "d2"), $"station{stationCase.TraceStation} handoff d2");
            AssertHex(GetSingleHex(stationCase.InitialInput, "ampl1"), GetSingleHex(managedInput, "ampl1"), $"station{stationCase.TraceStation} handoff ampl1");
            AssertHex(GetSingleHex(stationCase.InitialInput, "ampl2"), GetSingleHex(managedInput, "ampl2"), $"station{stationCase.TraceStation} handoff ampl2");

            for (int index = 0; index < referenceSystems.Count; index++)
            {
                int iteration = index + 1;
                ParityTraceRecord referenceSystem = referenceSystems[index];
                ParityTraceRecord managedSystem = managedSystems[index];
                AssertFloatField(referenceSystem, managedSystem, "uei", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "theta", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "dstar", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "ampl", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "residual1", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "residual2", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "residual3", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "row12", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "row22", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "row23", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "row24", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "row32", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "row33", $"station{stationCase.TraceStation} system iter{iteration}");
                AssertFloatField(referenceSystem, managedSystem, "row34", $"station{stationCase.TraceStation} system iter{iteration}");
            }

            for (int index = 0; index < referenceSteps.Count; index++)
            {
                int iteration = index + 1;
                ParityTraceRecord referenceStep = referenceSteps[index];
                ParityTraceRecord managedStep = managedSteps[index];
                AssertFloatField(referenceStep, managedStep, "uei", $"station{stationCase.TraceStation} step iter{iteration}");
                AssertFloatField(referenceStep, managedStep, "theta", $"station{stationCase.TraceStation} step iter{iteration}");
                AssertFloatField(referenceStep, managedStep, "dstar", $"station{stationCase.TraceStation} step iter{iteration}");
                AssertFloatField(referenceStep, managedStep, "ampl", $"station{stationCase.TraceStation} step iter{iteration}");
                AssertFloatField(referenceStep, managedStep, "dmax", $"station{stationCase.TraceStation} step iter{iteration}");
                AssertFloatField(referenceStep, managedStep, "rlx", $"station{stationCase.TraceStation} step iter{iteration}");
                AssertFloatField(referenceStep, managedStep, "residualNorm", $"station{stationCase.TraceStation} step iter{iteration}");
            }

            AssertHex(GetSingleHex(stationCase.FinalRecord, "theta"), ToHex((float)blState.THET[ibl, 0]), $"station{stationCase.TraceStation} final theta");
            AssertHex(GetSingleHex(stationCase.FinalRecord, "dstar"), ToHex((float)blState.DSTR[ibl, 0]), $"station{stationCase.TraceStation} final dstar");
            AssertHex(GetSingleHex(stationCase.FinalRecord, "ampl"), ToHex((float)blState.CTAU[ibl, 0]), $"station{stationCase.TraceStation} final ampl");
            AssertHex(GetSingleHex(stationCase.FinalRecord, "mass"), ToHex((float)blState.MASS[ibl, 0]), $"station{stationCase.TraceStation} final mass");
            Assert.NotNull(blState.LegacyKinematic[ibl, 0]);
            Assert.NotNull(blState.LegacySecondary[ibl, 0]);
        }
    }

    [Fact]
    public void Alpha10_P80_LaminarSeedStation3_FirstSystemAndStep_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            SimilaritySeedReferenceDirectory));

        ParityTraceRecord referenceSystem = GetTraceRecordFromFile(referencePath, "laminar_seed_system", station: 3, iteration: 1);
        ParityTraceRecord managedSystem = GetTraceRecord(run.Records, "laminar_seed_system", station: 3, iteration: 1);
        ParityTraceRecord? managedResidualTerms = TryGetLatestPrecedingTraceRecord(
            run.Records,
            managedSystem,
            "bldif_eq1_residual_terms");
        ParityTraceRecord? managedLaminarAxTerms = TryGetLatestPrecedingTraceRecord(
            run.Records,
            managedSystem,
            "laminar_ax_terms");
        AssertFloatField(referenceSystem, managedSystem, "uei", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_U2", "station3 first system");
        AssertHex(
            GetSingleHex(referenceSystem, "residual1"),
            GetSingleHex(managedSystem, "residual1"),
            $"station3 first system residual1 {DescribeLaminarSeedSystem(managedSystem)} {DescribeLaminarAxTerms(managedLaminarAxTerms)} {DescribeEq1ResidualCandidates(managedResidualTerms)} recent={DescribeRecentKinds(run.Records, managedSystem, 10)}");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "station3 first system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "station3 first system");

        ParityTraceRecord referenceStep = GetTraceRecordFromFile(referencePath, "laminar_seed_step", station: 3, iteration: 1);
        ParityTraceRecord managedStep = GetTraceRecord(run.Records, "laminar_seed_step", station: 3, iteration: 1);
        AssertFloatField(referenceStep, managedStep, "deltaShear", "station3 first step");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "station3 first step");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "station3 first step");
        AssertFloatField(referenceStep, managedStep, "ratioShear", "station3 first step");
        AssertFloatField(referenceStep, managedStep, "ratioTheta", "station3 first step");
        AssertFloatField(referenceStep, managedStep, "ratioDstar", "station3 first step");
        AssertFloatField(referenceStep, managedStep, "dmax", "station3 first step");
        AssertFloatField(referenceStep, managedStep, "rlx", "station3 first step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "station3 first step");
    }

    [Fact]
    public void Alpha10_P80_LaminarSeedStation3_Iteration2Eq3ResidualTerms_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            Station3Iteration2Eq3ReferenceDirectory));

        ParityTraceRecord referenceSystem = GetTraceRecordFromFile(referencePath, "laminar_seed_system", station: 3, iteration: 2);
        ParityTraceRecord managedSystem = GetTraceRecord(run.Records, "laminar_seed_system", station: 3, iteration: 2);

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq3_residual_terms" &&
                                 HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = run.Records
            .Where(static record => record.Kind == "bldif_eq3_residual_terms" &&
                                    HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();

        AssertFloatField(referenceResidual, managedResidual, "hlog", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "btmp", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "ulog", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "btmpUlog", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "xlog", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "cfx", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "halfCfx", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "dix", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "transport", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "xlogTransport", "station3 iter2 eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "rezh", "station3 iter2 eq3 residual");
    }

    [Fact]
    public void Alpha10_P80_LaminarSeedStation3_Iteration2Eq3SecondaryStations_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            Station3Iteration2Eq3ReferenceDirectory));

        ParityTraceRecord referenceSystem = GetTraceRecordFromFile(referencePath, "laminar_seed_system", station: 3, iteration: 2);
        ParityTraceRecord managedSystem = GetTraceRecord(run.Records, "laminar_seed_system", station: 3, iteration: 2);

        foreach (int localStation in new[] { 1, 2 })
        {
            ParityTraceRecord referenceSecondary = ParityTraceLoader.ReadMatching(
                    referencePath,
                    record => record.Kind == "bldif_secondary_station" &&
                              HasExactDataInt(record, "ityp", 1) &&
                              HasExactDataInt(record, "station", localStation))
                .Where(record => record.Sequence < referenceSystem.Sequence)
                .OrderBy(record => record.Sequence)
                .Last();
            ParityTraceRecord managedSecondary = run.Records
                .Where(record => record.Kind == "bldif_secondary_station" &&
                                 HasExactDataInt(record, "ityp", 1) &&
                                 HasExactDataInt(record, "station", localStation))
                .Where(record => record.Sequence < managedSystem.Sequence)
                .OrderBy(record => record.Sequence)
                .Last();

            string context = $"station3 iter2 eq3 secondary station{localStation}";
            AssertFloatField(referenceSecondary, managedSecondary, "hc", context);
            AssertFloatField(referenceSecondary, managedSecondary, "hs", context);
            AssertFloatField(referenceSecondary, managedSecondary, "hsHk", context);
            AssertFloatField(referenceSecondary, managedSecondary, "hkD", context);
            AssertFloatField(referenceSecondary, managedSecondary, "hsD", context);
            AssertFloatField(referenceSecondary, managedSecondary, "hsT", context);
            AssertFloatField(referenceSecondary, managedSecondary, "us", context);
            AssertFloatField(referenceSecondary, managedSecondary, "usT", context);
            AssertFloatField(referenceSecondary, managedSecondary, "hkU", context);
            AssertFloatField(referenceSecondary, managedSecondary, "rtT", context);
            AssertFloatField(referenceSecondary, managedSecondary, "rtU", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cq", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cf", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cfU", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cfT", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cfD", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cfMs", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cfmU", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cfmT", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cfmD", context);
            AssertFloatField(referenceSecondary, managedSecondary, "cfmMs", context);
            AssertFloatField(referenceSecondary, managedSecondary, "di", context);
            AssertFloatField(referenceSecondary, managedSecondary, "diT", context);
            AssertFloatField(referenceSecondary, managedSecondary, "de", context);
        }
    }

    [Fact]
    public void Alpha10_P80_LaminarSeedStation3_Iteration2BlmidCfTerms_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            Station3Iteration2BlmidReferenceDirectory));

        ParityTraceRecord referenceSystem = GetTraceRecordFromFile(referencePath, "laminar_seed_system", station: 3, iteration: 2);
        ParityTraceRecord managedSystem = GetTraceRecord(run.Records, "laminar_seed_system", station: 3, iteration: 2);

        ParityTraceRecord referenceBlmid = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "blmid_cf_terms" &&
                                 HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();
        ParityTraceRecord managedBlmid = run.Records
            .Where(static record => record.Kind == "blmid_cf_terms" &&
                                    HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();

        const string context = "station3 iter2 blmid cf terms";
        AssertFloatField(referenceBlmid, managedBlmid, "hk1Ms", context);
        AssertFloatField(referenceBlmid, managedBlmid, "rt1Ms", context);
        AssertFloatField(referenceBlmid, managedBlmid, "m1Ms", context);
        AssertFloatField(referenceBlmid, managedBlmid, "hk2Ms", context);
        AssertFloatField(referenceBlmid, managedBlmid, "rt2Ms", context);
        AssertFloatField(referenceBlmid, managedBlmid, "m2Ms", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfm", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmHka", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmRta", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmMa", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmU1", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmT1", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmD1", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmU2", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmT2", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmD2", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmMs", context);
        AssertFloatField(referenceBlmid, managedBlmid, "rt1Re", context);
        AssertFloatField(referenceBlmid, managedBlmid, "rt2Re", context);
        AssertFloatField(referenceBlmid, managedBlmid, "cfmRe", context);
    }

    [Fact]
    public void Alpha10_P80_LaminarSeedStation3_Iteration2KinematicResult_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            Station3Iteration2KinematicReferenceDirectory));

        ParityTraceRecord referenceSystem = GetTraceRecordFromFile(referencePath, "laminar_seed_system", station: 3, iteration: 2);
        ParityTraceRecord managedSystem = GetTraceRecord(run.Records, "laminar_seed_system", station: 3, iteration: 2);

        ParityTraceRecord referenceKinematic = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();
        ParityTraceRecord managedKinematic = run.Records
            .Where(static record => record.Kind == "kinematic_result")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();

        const string context = "station3 iter2 kinematic";
        AssertFloatField(referenceKinematic, managedKinematic, "hstinv_ms", context);
        AssertFloatField(referenceKinematic, managedKinematic, "m2_ms", context);
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_ms", context);
        AssertFloatField(referenceKinematic, managedKinematic, "r2_ms", context);
        AssertFloatField(referenceKinematic, managedKinematic, "rT2", context);
        AssertFloatField(referenceKinematic, managedKinematic, "v2MsReyblTerm", context);
        AssertFloatField(referenceKinematic, managedKinematic, "v2MsHeTerm", context);
        AssertFloatField(referenceKinematic, managedKinematic, "v2_ms", context);
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", context);
    }

    [Fact]
    public void Alpha10_P80_LaminarSeedStation3_Iteration2BlkinTerms_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            Station3Iteration2KinematicReferenceDirectory));

        ParityTraceRecord referenceSystem = GetTraceRecordFromFile(referencePath, "laminar_seed_system", station: 3, iteration: 2);
        ParityTraceRecord managedSystem = GetTraceRecord(run.Records, "laminar_seed_system", station: 3, iteration: 2);

        ParityTraceRecord referenceBlkin = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "blkin_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();
        ParityTraceRecord managedBlkin = run.Records
            .Where(static record => record.Kind == "blkin_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();

        const string context = "station3 iter2 blkin terms";
        AssertFloatField(referenceBlkin, managedBlkin, "m2Den", context);
        AssertFloatField(referenceBlkin, managedBlkin, "tr2", context);
        AssertFloatField(referenceBlkin, managedBlkin, "m2MsNum", context);
        AssertFloatField(referenceBlkin, managedBlkin, "m2Ms", context);
    }

    [Fact]
    public void Alpha10_P80_LaminarSeedStation3_Iteration2Eq3CommonInputs_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            Station3Iteration2CommonReferenceDirectory));

        ParityTraceRecord referenceSystem = GetTraceRecordFromFile(referencePath, "laminar_seed_system", station: 3, iteration: 2);
        ParityTraceRecord managedSystem = GetTraceRecord(run.Records, "laminar_seed_system", station: 3, iteration: 2);

        ParityTraceRecord referenceInputs = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_log_inputs" &&
                                 HasExactDataInt(record, "ityp", 1) &&
                                 HasExactDataInt(record, "station", 3))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();
        ParityTraceRecord managedInputs = run.Records
            .Where(static record => record.Kind == "bldif_log_inputs" &&
                                    HasExactDataInt(record, "ityp", 1) &&
                                    HasExactDataInt(record, "station", 3))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();

        const string inputsContext = "station3 iter2 eq3 log inputs";
        AssertFloatField(referenceInputs, managedInputs, "x1", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "x2", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "u1", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "u2", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "t1", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "t2", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "hs1", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "hs2", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "xRatio", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "uRatio", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "tRatio", inputsContext);
        AssertFloatField(referenceInputs, managedInputs, "hRatio", inputsContext);

        ParityTraceRecord referenceCommon = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_common" &&
                                 HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();
        ParityTraceRecord managedCommon = run.Records
            .Where(static record => record.Kind == "bldif_common" &&
                                    HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();

        const string commonContext = "station3 iter2 eq3 common";
        AssertFloatField(referenceCommon, managedCommon, "cfm", commonContext);
        AssertFloatField(referenceCommon, managedCommon, "upw", commonContext);
        AssertFloatField(referenceCommon, managedCommon, "xlog", commonContext);
        AssertFloatField(referenceCommon, managedCommon, "ulog", commonContext);
        AssertFloatField(referenceCommon, managedCommon, "tlog", commonContext);
        AssertFloatField(referenceCommon, managedCommon, "hlog", commonContext);
        AssertFloatField(referenceCommon, managedCommon, "ddlog", commonContext);
    }

    [Fact]
    public void Alpha10_P80_LaminarSeedStation4_Iteration1Eq3ResidualTerms_BitwiseMatchFortranTrace()
    {
        SeedMethodCaseData data = LoadCaseData();
        SeedSequenceRun run = RunSeedSequence(data);
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            Station4Iteration1Eq3ReferenceDirectory));

        ParityTraceRecord referenceSystem = GetTraceRecordFromFile(referencePath, "laminar_seed_system", station: 4, iteration: 1);
        ParityTraceRecord managedSystem = GetTraceRecord(run.Records, "laminar_seed_system", station: 4, iteration: 1);

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq3_residual_terms" &&
                                 HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = run.Records
            .Where(static record => record.Kind == "bldif_eq3_residual_terms" &&
                                    HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(record => record.Sequence)
            .Last();

        const string context = "station4 iter1 eq3 residual";
        AssertFloatField(referenceResidual, managedResidual, "hlog", context);
        AssertFloatField(referenceResidual, managedResidual, "btmp", context);
        AssertFloatField(referenceResidual, managedResidual, "ulog", context);
        AssertFloatField(referenceResidual, managedResidual, "btmpUlog", context);
        AssertFloatField(referenceResidual, managedResidual, "xlog", context);
        AssertFloatField(referenceResidual, managedResidual, "cfx", context);
        AssertFloatField(referenceResidual, managedResidual, "halfCfx", context);
        AssertFloatField(referenceResidual, managedResidual, "dix", context);
        AssertFloatField(referenceResidual, managedResidual, "transport", context);
        AssertFloatField(referenceResidual, managedResidual, "xlogTransport", context);
        AssertFloatField(referenceResidual, managedResidual, "rezh", context);
    }

    private static SeedMethodCaseData LoadCaseData()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string handoffPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_seedhandoff_ref1"));
        string allSidePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_blsys_all_side1_ref1"));
        string similarityPath = GetLatestTracePath(Path.Combine(debugDir, "reference", SimilaritySeedReferenceDirectory));

        ParityTraceRecord station2InitialInput = ParityTraceLoader.ReadMatching(
                handoffPath,
                static record => record.Kind == "blsys_interval_inputs" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 2))
            .OrderBy(record => record.Sequence)
            .First();

        ParityTraceRecord station2FinalRecord = ParityTraceLoader.ReadMatching(
                handoffPath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 2))
            .Single();

        IReadOnlyList<DownstreamStationCase> downstreamStations = Enumerable.Range(3, 3)
            .Select(traceStation =>
            {
                ParityTraceRecord initialInput = ParityTraceLoader.ReadMatching(
                        traceStation == 3 ? handoffPath : allSidePath,
                        record => record.Kind == "blsys_interval_inputs" &&
                                  HasExactDataInt(record, "side", 1) &&
                                  HasExactDataInt(record, "station", traceStation))
                    .OrderBy(record => record.Sequence)
                    .First();

                ParityTraceRecord finalRecord = ParityTraceLoader.ReadMatching(
                        similarityPath,
                        record => record.Kind == "laminar_seed_final" &&
                                  HasExactDataInt(record, "side", 1) &&
                                  HasExactDataInt(record, "station", traceStation))
                    .Single();

                return new DownstreamStationCase(traceStation, initialInput, finalRecord);
            })
            .ToArray();

        return new SeedMethodCaseData(
            Station2InitialInput: station2InitialInput,
            Station2FinalRecord: station2FinalRecord,
            DownstreamStations: downstreamStations);
    }

    private static BoundaryLayerSystemState CreateState(SeedMethodCaseData data)
    {
        var state = new BoundaryLayerSystemState(maxStations: 6, maxWakeStations: 0);
        state.InitializeForStationCounts(side1: 6, side2: 1, wake: 0);
        state.ITRAN[0] = state.IBLTE[0];
        state.ITRAN[1] = state.IBLTE[1];

        state.XSSI[1, 0] = FromSingleHex(GetSingleHex(data.Station2InitialInput, "x1"));
        state.UEDG[1, 0] = FromSingleHex(GetSingleHex(data.Station2InitialInput, "u1"));
        state.THET[1, 0] = FromSingleHex(GetSingleHex(data.Station2InitialInput, "t1"));
        state.DSTR[1, 0] = FromSingleHex(GetSingleHex(data.Station2InitialInput, "d1"));
        state.CTAU[1, 0] = FromSingleHex(GetSingleHex(data.Station2InitialInput, "ampl1"));

        return state;
    }

    private static SeedSequenceRun RunSeedSequence(SeedMethodCaseData data)
    {
        BoundaryLayerSystemState blState = CreateState(data);
        AnalysisSettings settings = CreateParitySettings();
        double hvrat = (double)(GetHvRatMethod.Invoke(null, new object?[] { true })
            ?? throw new InvalidOperationException("GetHvRat returned null."));
        object?[] compressibilityArgs =
        {
            settings.MachNumber,
            settings.FreestreamVelocity,
            settings.ReynoldsNumber,
            hvrat,
            0.0, // tkbl
            0.0, // qinfbl
            0.0, // tkbl_ms
            0.0, // hstinv
            0.0, // hstinv_ms
            0.0, // rstbl
            0.0, // rstbl_ms
            0.0, // reybl
            0.0, // reybl_re
            0.0, // reybl_ms
            true
        };
        ComputeCompressibilityParametersMethod.Invoke(null, compressibilityArgs);
        double tkbl = (double)compressibilityArgs[4]!;
        double qinfbl = (double)compressibilityArgs[5]!;
        double tkblMs = (double)compressibilityArgs[6]!;
        double hstinv = (double)compressibilityArgs[7]!;
        double hstinvMs = (double)compressibilityArgs[8]!;
        double rstbl = (double)compressibilityArgs[9]!;
        double rstblMs = (double)compressibilityArgs[10]!;
        double reybl = (double)compressibilityArgs[11]!;
        double reyblRe = (double)compressibilityArgs[12]!;
        double reyblMs = (double)compressibilityArgs[13]!;
        var lines = new List<string>();

        using (var traceWriter = new JsonlTraceWriter(
            TextWriter.Null,
            runtime: "csharp",
            session: new { caseName = "laminar-seed-stations-3-5-micro" },
            serializedRecordObserver: lines.Add))
        {
            using var traceScope = SolverTrace.Begin(traceWriter);

            RefineSimilarityStationSeedMethod.Invoke(null, new object?[]
            {
                blState,
                0,
                settings,
                tkbl,
                qinfbl,
                tkblMs,
                hstinv,
                hstinvMs,
                rstbl,
                rstblMs,
                reybl,
                reyblRe,
                reyblMs
            });

            foreach (DownstreamStationCase stationCase in data.DownstreamStations)
            {
                int ibl = stationCase.TraceStation - 1;

                blState.XSSI[ibl, 0] = FromSingleHex(GetSingleHex(stationCase.InitialInput, "x2"));
                blState.UEDG[ibl, 0] = FromSingleHex(GetSingleHex(stationCase.InitialInput, "u2"));
                blState.THET[ibl, 0] = FromSingleHex(GetSingleHex(stationCase.InitialInput, "t2"));
                blState.DSTR[ibl, 0] = FromSingleHex(GetSingleHex(stationCase.InitialInput, "d2"));
                blState.CTAU[ibl, 0] = FromSingleHex(GetSingleHex(stationCase.InitialInput, "ampl2"));

                RefineLaminarSeedStationMethod.Invoke(null, new object?[]
                {
                    blState,
                    0,
                    ibl,
                    settings,
                    tkbl,
                    qinfbl,
                    tkblMs,
                    hstinv,
                    hstinvMs,
                    rstbl,
                    rstblMs,
                    reybl,
                    reyblRe,
                    reyblMs
                });
            }
        }

        return new SeedSequenceRun(
            blState,
            lines
                .Select(ParityTraceLoader.DeserializeLine)
                .Where(static record => record is not null)
                .Select(static record => record!)
                .ToArray());
    }

    private static AnalysisSettings CreateParitySettings()
    {
        return new AnalysisSettings(
            panelCount: 80,
            reynoldsNumber: 1_000_000.0,
            machNumber: 0.0,
            criticalAmplificationFactor: 9.0,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyWakeSourceKernelPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyPanelingPrecision: true);
    }

    private static string GetLatestTracePath(string directory)
    {
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
    }

    private static bool HasExactDataInt(ParityTraceRecord record, string field, int expected)
    {
        return record.TryGetDataField(field, out var value) &&
               value.ValueKind == System.Text.Json.JsonValueKind.Number &&
               value.TryGetInt32(out int actual) &&
               actual == expected;
    }

    private static string GetSingleHex(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataBits(field, out IReadOnlyDictionary<string, string>? bits), $"Missing dataBits for '{field}' in {record.Kind}.");
        Assert.NotNull(bits);
        Assert.True(bits!.TryGetValue("f32", out string? hex), $"Missing f32 bits for '{field}' in {record.Kind}.");
        return hex!;
    }

    private static float FromSingleHex(string hex)
    {
        uint bits = uint.Parse(hex.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    private static string ToHex(float value)
        => $"0x{BitConverter.SingleToInt32Bits(value):X8}";

    private static ParityTraceRecord GetTraceRecordFromFile(string tracePath, string kind, int station, int iteration)
    {
        return ParityTraceLoader.ReadMatching(
                tracePath,
                record => record.Kind == kind &&
                          HasExactDataInt(record, "side", 1) &&
                          HasExactDataInt(record, "station", station) &&
                          HasExactDataInt(record, "iteration", iteration))
            .OrderBy(record => record.Sequence)
            .First();
    }

    private static ParityTraceRecord GetTraceRecord(IEnumerable<ParityTraceRecord> records, string kind, int station, int iteration)
    {
        return records
            .Where(record => record.Kind == kind &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", station) &&
                             HasExactDataInt(record, "iteration", iteration))
            .OrderBy(record => record.Sequence)
            .First();
    }

    private static ParityTraceRecord GetFirstTraceRecord(IEnumerable<ParityTraceRecord> records, string kind, int station)
    {
        return records
            .Where(record => record.Kind == kind &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", station))
            .OrderBy(record => record.Sequence)
            .First();
    }

    private static IReadOnlyList<ParityTraceRecord> GetTraceRecordsFromFile(string tracePath, string kind, int station)
    {
        return ParityTraceLoader.ReadMatching(
                tracePath,
                record => record.Kind == kind &&
                          HasExactDataInt(record, "side", 1) &&
                          HasExactDataInt(record, "station", station))
            .OrderBy(record => record.Sequence)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> GetTraceRecords(IEnumerable<ParityTraceRecord> records, string kind, int station)
    {
        return records
            .Where(record => record.Kind == kind &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", station))
            .OrderBy(record => record.Sequence)
            .ToArray();
    }

    private static ParityTraceRecord? TryGetLatestPrecedingTraceRecord(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord target,
        string kind)
    {
        return records
            .Where(record => record.Kind == kind && record.Sequence < target.Sequence)
            .OrderBy(record => record.Sequence)
            .LastOrDefault();
    }

    private static void AssertFloatField(ParityTraceRecord expected, ParityTraceRecord actual, string field, string context)
    {
        AssertHex(
            GetSingleHex(expected, field),
            GetSingleHex(actual, field),
            $"{context} {field}");
    }

    private static void AssertHex(string expected, string actual, string context)
    {
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{context} expected={expected} actual={actual}");
    }

    private static string DescribeEq1ResidualCandidates(ParityTraceRecord? record)
    {
        if (record is null)
        {
            return "bldif_eq1_residual_terms=absent";
        }

        string[] fields =
        [
            "eq1SubStored",
            "eq1SubInlineProduction",
            "eq1SubInlineFull",
            "eq1SubDirectFloatExpression",
            "eq1SubDirectFmaExpression",
            "rezcStoredTerms",
            "rezcRoundedLogLoss",
            "rezcInlineProduction",
            "rezcInlineFull",
            "rezcDirectFloatExpression",
            "rezcDirectFmaExpression",
            "rezcDirectFmaStoredDux",
            "rezcDirectFmaFull",
            "rezcWideLogLoss",
            "rezcWideEverything",
            "rezcExpressionWide",
            "rezc"
        ];

        return string.Join(
            ", ",
            fields.Select(field => $"{field}={TryGetSingleHex(record, field) ?? "missing"}"));
    }

    private static string DescribeLaminarSeedSystem(ParityTraceRecord record)
    {
        string[] fields =
        [
            "residual1",
            "residual2",
            "residual3",
            "row11",
            "row12",
            "row13",
            "row14",
            "row21",
            "row22",
            "row23",
            "row24",
            "row31",
            "row32",
            "row33",
            "row34"
        ];

        return string.Join(
            ", ",
            fields.Select(field => $"{field}={TryGetSingleHex(record, field) ?? "missing"}"));
    }

    private static string DescribeLaminarAxTerms(ParityTraceRecord? record)
    {
        if (record is null)
        {
            return "laminar_ax_terms=absent";
        }

        string[] fields =
        [
            "zAx",
            "ax",
            "axA1",
            "axA2",
            "vs1Row12Inner",
            "vs2Row12Inner"
        ];

        return string.Join(
            ", ",
            fields.Select(field => $"{field}={TryGetSingleHex(record, field) ?? "missing"}"));
    }

    private static string DescribeRecentKinds(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord target,
        int count)
    {
        return string.Join(
            " > ",
            records
                .Where(record => record.Sequence <= target.Sequence)
                .OrderBy(record => record.Sequence)
                .TakeLast(count)
                .Select(record => record.Kind));
    }

    private static string? TryGetSingleHex(ParityTraceRecord record, string field)
    {
        return record.TryGetDataBits(field, out IReadOnlyDictionary<string, string>? bits) &&
               bits is not null &&
               bits.TryGetValue("f32", out string? hex)
            ? hex
            : null;
    }

    private sealed record SeedMethodCaseData(
        ParityTraceRecord Station2InitialInput,
        ParityTraceRecord Station2FinalRecord,
        IReadOnlyList<DownstreamStationCase> DownstreamStations);

    private sealed record DownstreamStationCase(
        int TraceStation,
        ParityTraceRecord InitialInput,
        ParityTraceRecord FinalRecord);

    private sealed record SeedSequenceRun(
        BoundaryLayerSystemState State,
        IReadOnlyList<ParityTraceRecord> Records);
}
