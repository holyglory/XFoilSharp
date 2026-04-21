using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: UESET predicted edge-velocity reconstruction
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_ueset_s1st2_ref4/reference_trace.*.jsonl authoritative focused trace
// Role in port: Replays the upper-side station-2 UESET accumulation through the real managed implementation and compares every emitted term plus the final split against the focused Fortran trace.
// Differences: The harness is managed-only infrastructure, but it exercises the actual private assembler helper under an in-process JSON trace writer instead of re-implementing the arithmetic in the test.
// Decision: Keep this micro-engine because it turns the live UESET producer boundary into a fast raw-word oracle and avoids broad viscous reruns.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class PredictedEdgeVelocityMicroParityTests
{
    private const string Alpha0FullCaseId = "n0012_re1e6_a0_p12_n9_full";
    private const string CaseId = "n0012_re1e6_a10_p80";
    private const string LegacyDirectSeedCarryFocusReferenceCaseId = "alpha0_p12_legacy_direct_seed_carry_focus_ref";
    private const string LegacyDirectSeedCarryFocusManagedCaseId = "alpha0_p12_legacy_direct_seed_carry_focus_man";
    private const int ClassicXFoilNacaPointCount = 239;
    private static readonly string SimilarityReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_similarity_seed_s1st2_ref");
    private static readonly string RemarchReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_remarch_station16_ref");
    private static readonly string RemarchIterationReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_seediter_station16_ref");
    private static readonly string DirectSeedReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_directseed_station16_ref");
    private static readonly string DirectSeedEq1ResidualReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_eq1_residual_station16_ref");
    private static readonly string DirectSeedEq1DStation16ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_eq1_d_station16_ref");
    private static readonly string DirectSeedEq3ResidualReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_eq3_residual_station16_ref");
    private static readonly string DirectSeedEq1ResidualStation15ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_eq1_residual_station15_ref");
    private static readonly string DirectSeedEq1TStation15ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_eq1_t_withsys_ref");
    private static readonly string DirectSeedEq2TStation15ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_eq2_t2_station15_ref");
    private static readonly string DirectSeedEq3TStation5ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_eq3_t_station5_ref");
    private static readonly string DirectSeedRow33ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_row33_ref");
    private static readonly string DirectSeedRow34ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_row34_ref");
    private static readonly string DirectSeedStation5CfChainReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_station5_cfchain_ref");
    private static readonly string DirectSeedSecondaryStation1ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_secondary_withsys_ref");
    private static readonly string DirectSeedKinematicReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_kinematic_withsys_ref");
    private static readonly string DirectSeedCompressibleReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_compressible_withsys_ref");
    private static readonly string DirectSeedCarryChainReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_carrychain_withsys_ref");
    private static readonly string DirectSeedCarryBlkinReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_carryblkin_withsys_ref");
    private static readonly string DirectSeedTransitionIntervalInputsReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_iter1_transition_window_ref");
    private static readonly string DirectSeedBldifLogIter3ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_log_iter3_ref");
    private static readonly string Alpha0Station5DirectSeedReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha0_p12_station5_directseed_ref");
    private static readonly string LegacyDirectSeedCarryFocusedReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        LegacyDirectSeedCarryFocusReferenceCaseId);
    private static readonly string LegacyDirectSeedCarryFocusedManagedDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "csharp",
        LegacyDirectSeedCarryFocusManagedCaseId);
    private static readonly string DirectSeedIteration5TransitionWindowReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_iter5_transition_window_ref");
    private static readonly object LegacyDirectSeedCarryFocusedArtifactsLock = new();
    private static IReadOnlyList<ParityTraceRecord>? s_legacyDirectSeedCarryFocusedReferenceRecords;
    private static IReadOnlyList<ParityTraceRecord>? s_legacyDirectSeedCarryFocusedManagedRecords;
    private static readonly string[] FocusedTraceEnvironmentVariableNames =
    {
        "XFOIL_TRACE_KIND_ALLOW",
        "XFOIL_TRACE_SCOPE_ALLOW",
        "XFOIL_TRACE_NAME_ALLOW",
        "XFOIL_TRACE_DATA_MATCH",
        "XFOIL_TRACE_SIDE",
        "XFOIL_TRACE_STATION",
        "XFOIL_TRACE_ITERATION",
        "XFOIL_TRACE_ITER_MIN",
        "XFOIL_TRACE_ITER_MAX",
        "XFOIL_TRACE_MODE",
        "XFOIL_TRACE_TRIGGER_KIND",
        "XFOIL_TRACE_TRIGGER_SCOPE",
        "XFOIL_TRACE_TRIGGER_NAME_ALLOW",
        "XFOIL_TRACE_TRIGGER_DATA_MATCH",
        "XFOIL_TRACE_TRIGGER_SIDE",
        "XFOIL_TRACE_TRIGGER_STATION",
        "XFOIL_TRACE_TRIGGER_ITERATION",
        "XFOIL_TRACE_TRIGGER_ITER_MIN",
        "XFOIL_TRACE_TRIGGER_ITER_MAX",
        "XFOIL_TRACE_TRIGGER_MODE",
        "XFOIL_TRACE_TRIGGER_OCCURRENCE",
        "XFOIL_TRACE_RING_BUFFER",
        "XFOIL_TRACE_POST_LIMIT",
        "XFOIL_MAX_TRACE_MB"
    };
    private static readonly string DirectSeedIteration4TransitionWindowReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_iter4_transition_window_ref");
    private static readonly string DirectSeedBldifEq1STermsStation15Iter4ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_bldif_eq1_s_station15_iter4_ref");
    private static readonly string DirectSeedTransitionPointReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_transpoint_withsys_ref");
    private static readonly string DirectSeedStepStation7ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_seedstep_station7_ref");
    private static readonly string DirectSeedStepStation6ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_seedstep_station6_ref");
    private static readonly string DirectSeedStepStation5ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_seedstep_station5_ref");
    private static readonly string ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_ueset_s1st2_ref4");

    [Fact]
    public void Alpha0_P12_UpperStation2_PredictedEdgeVelocity_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        PredictedEdgeVelocityBlock referenceBlock = SelectFirstPredictedEdgeVelocityBlock(
            ParityTraceLoader.ReadMatching(
                referencePath,
                static record => (record.Kind == "predicted_edge_velocity_term" || record.Kind == "predicted_edge_velocity") &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 2)),
            side: 1,
            station: 2);

        Assert.NotEmpty(referenceBlock.Terms);

        ManagedPredictedEdgeVelocityTraceResult managed = RunManagedPredictedEdgeVelocityTrace(Alpha0FullCaseId, station: 2);
        AssertHex(
            GetFloatHex(referenceBlock.Terms[0], "mass"),
            managed.ContextStationMassHex,
            "managed alpha-0 context station-2 mass");

        Assert.Equal(referenceBlock.Terms.Count, managed.Terms.Count);
        for (int index = 0; index < referenceBlock.Terms.Count; index++)
        {
            AssertTermParity(referenceBlock.Terms[index], managed.Terms[index], index);
        }

        AssertFinalParity(referenceBlock.Final, managed.Final);
        AssertHex(
            GetFloatHex(referenceBlock.Final, "predicted"),
            ToHex((float)managed.Usav[1, 0]),
            "returned alpha-0 usav upper station-2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_LegacySeedFinal_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_final")
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 station-4 legacy seed final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 station-4 legacy seed final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 station-4 legacy seed final");
    }

    [Fact]
    public void Alpha0_P12_UpperStation3_LaminarSeedFinal_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 3))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedFinal = RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_final")
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 3))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 station-3 laminar seed final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 station-3 laminar seed final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 station-3 laminar seed final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 station-3 laminar seed final");
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_LaminarSeedFinal_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedFinal = RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_final")
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 station-5 laminar seed final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 station-5 laminar seed final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 station-5 laminar seed final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 station-5 laminar seed final");
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_LaminarSeedSystemDstar_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecords(referencePath, 5, "laminar_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_system"),
                5,
                "laminar_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();

        AssertOrderedFieldParity(referenceRecords, managedRecords, "dstar", "alpha-0 station-5 laminar seed system dstar scan");
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_LaminarSeedStepDeltaDstar_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecords(referencePath, 5, "laminar_seed_step")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_step"),
                5,
                "laminar_seed_step")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();

        AssertOrderedFieldParity(referenceRecords, managedRecords, "deltaDstar", "alpha-0 station-5 laminar seed step delta-dstar scan");
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_Iteration1LaminarSeedStepNormTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceTerms = GetOrderedStationRecords(referencePath, 5, "laminar_seed_step_norm_terms").First();
        ParityTraceRecord managedTerms = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_step_norm_terms"),
                5,
                "laminar_seed_step_norm_terms")
            .First();

        AssertFloatField(referenceTerms, managedTerms, "deltaShear", "alpha-0 station-5 iteration1 seed-step norm terms");
        AssertFloatField(referenceTerms, managedTerms, "deltaTheta", "alpha-0 station-5 iteration1 seed-step norm terms");
        AssertFloatField(referenceTerms, managedTerms, "deltaDstar", "alpha-0 station-5 iteration1 seed-step norm terms");
        AssertFloatField(referenceTerms, managedTerms, "squareShear", "alpha-0 station-5 iteration1 seed-step norm terms");
        AssertFloatField(referenceTerms, managedTerms, "squareTheta", "alpha-0 station-5 iteration1 seed-step norm terms");
        AssertFloatField(referenceTerms, managedTerms, "squareDstar", "alpha-0 station-5 iteration1 seed-step norm terms");
        AssertFloatField(referenceTerms, managedTerms, "sumSquares", "alpha-0 station-5 iteration1 seed-step norm terms");
        AssertFloatField(referenceTerms, managedTerms, "residualNorm", "alpha-0 station-5 iteration1 seed-step norm terms");
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_FirstLaminarSeedSystemAndStep_FromFullTrace_BitwiseMatchFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 5, "laminar_seed_system").First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 5, "laminar_seed_system").First();
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "alpha-0 station-5 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "alpha-0 station-5 first laminar seed system");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 5, "laminar_seed_step").First();
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 5, "laminar_seed_step").First();
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "alpha-0 station-5 first laminar seed step");
        AssertFloatField(referenceStep, managedStep, "ratioDstar", "alpha-0 station-5 first laminar seed step");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-5 first laminar seed step");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-5 first laminar seed step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-5 first laminar seed step");
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_Iteration1SystemResidual1OwnerEq1ResidualTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 1;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_system",
            "bldif_eq1_residual_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 5, "laminar_seed_system")
            .Where(static record => HasExactDataInt(record, "iteration", Iteration))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 5, "laminar_seed_system")
            .Where(static record => HasExactDataInt(record, "iteration", Iteration))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_residual_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "scc",
            "cqa",
            "upw",
            "oneMinusUpw",
            "ald",
            "cq1",
            "cq2",
            "cqaLeftTerm",
            "cqaRightTerm",
            "sa",
            "dxi",
            "dea",
            "slog",
            "uq",
            "ulog",
            "eq1Source",
            "eq1Production",
            "eq1LogLoss",
            "eq1Convection",
            "eq1DuxGain",
            "eq1SubStored",
            "rezcStoredTerms",
            "eq1SubInlineProduction",
            "rezcInlineProduction",
            "eq1SubInlineFull",
            "rezcInlineFull",
            "rezc"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceResidual, managedResidual, field, "alpha-0 station-5 iteration1 system residual1 owner eq1 residual");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_FirstDirectSeedSystem_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(Alpha0Station5DirectSeedReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_system",
            "laminar_seed_step",
            "bldif_eq1_residual_terms",
            "bldif_eq1_uq_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 5, "laminar_seed_system").First();

        string[] fields =
        [
            "uei",
            "theta",
            "dstar",
            "ampl",
            "ctau",
            "hk2",
            "hk2_T2",
            "hk2_D2",
            "hk2_U2",
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

        foreach (string field in fields)
        {
            AssertFloatField(referenceSystem, managedSystem, field, "alpha-0 station-5 focused direct-seed system");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_FirstDirectSeedEq1Residual_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(Alpha0Station5DirectSeedReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_system",
            "bldif_eq1_residual_terms",
            "bldif_eq1_uq_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 5, "laminar_seed_system").First();

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_residual_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 5) &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 5) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

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
            "x1",
            "x2",
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

        foreach (string field in fields)
        {
            AssertFloatField(referenceResidual, managedResidual, field, "alpha-0 station-5 focused direct-seed eq1 residual");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation3_LaminarAmplificationCarry_FromFullTrace_BitwiseMatchesAcceptedSeed()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 3))
            .OrderBy(static record => record.Sequence)
            .Last();
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext(Alpha0FullCaseId);

        AssertHex(
            GetFloatHex(referenceFinal, "ampl"),
            ToHex((float)context.BoundaryLayerState.LegacyAmplificationCarry[2, 0]),
            "alpha-0 station-3 laminar amplification carry");
    }

    [Fact]
    public void Alpha0_P12_UpperStation3_LaminarSeedSystemAmpl_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecords(referencePath, 3, "laminar_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_system"),
                3,
                "laminar_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();

        AssertOrderedFieldParity(referenceRecords, managedRecords, "ampl", "alpha-0 station-3 laminar seed system ampl scan");
    }

    [Fact]
    public void Alpha0_P12_UpperStation3_FirstLaminarSeedSystemAndStep_FromFullTrace_BitwiseMatchFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 3, "laminar_seed_system").First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 3, "laminar_seed_system").First();
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-3 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-3 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-3 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-3 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-3 first laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-3 first laminar seed system");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 3, "laminar_seed_step").First();
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 3, "laminar_seed_step").First();
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-3 first laminar seed step");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-3 first laminar seed step");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-3 first laminar seed step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-3 first laminar seed step");
    }

    [Fact]
    public void Alpha0_P12_UpperStation3_SecondLaminarSeedSystemAndStep_FromFullTrace_BitwiseMatchFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 3, "laminar_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", 2));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 3, "laminar_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", 2));
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-3 second laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-3 second laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-3 second laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-3 second laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-3 second laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-3 second laminar seed system");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 3, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", 2));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 3, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", 2));
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-3 second laminar seed step");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-3 second laminar seed step");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-3 second laminar seed step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-3 second laminar seed step");
    }

    [Fact]
    public void Alpha0_P12_UpperStation3_FinalReferenceLaminarSeedIteration_FromFullTrace_BitwiseMatchFortranTrace()
    {
        const int ReferenceFinalIteration = 11;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 3, "laminar_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", ReferenceFinalIteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 3, "laminar_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", ReferenceFinalIteration));
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-3 final reference laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-3 final reference laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-3 final reference laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-3 final reference laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-3 final reference laminar seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-3 final reference laminar seed system");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 3, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", ReferenceFinalIteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 3, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", ReferenceFinalIteration));
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-3 final reference laminar seed step");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-3 final reference laminar seed step");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-3 final reference laminar seed step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-3 final reference laminar seed step");
    }

    [Fact]
    public void Alpha0_P12_UpperStation3_FirstTransitionSensitivityInputs_BeforeLaminarSeedSystem_FromFullTrace_BitwiseMatchFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_sensitivity_inputs",
            "laminar_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 3, "laminar_seed_system").First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 3, "laminar_seed_system").First();

        ParityTraceRecord referenceInputs = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_sensitivity_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_sensitivity_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "a1", "alpha-0 station-3 first transition sensitivity inputs");
        AssertFloatField(referenceInputs, managedInputs, "a2", "alpha-0 station-3 first transition sensitivity inputs");
        AssertFloatField(referenceInputs, managedInputs, "acrit", "alpha-0 station-3 first transition sensitivity inputs");
        AssertFloatField(referenceInputs, managedInputs, "hk1", "alpha-0 station-3 first transition sensitivity inputs");
        AssertFloatField(referenceInputs, managedInputs, "hk2", "alpha-0 station-3 first transition sensitivity inputs");
        AssertFloatField(referenceInputs, managedInputs, "rt1", "alpha-0 station-3 first transition sensitivity inputs");
        AssertFloatField(referenceInputs, managedInputs, "rt2", "alpha-0 station-3 first transition sensitivity inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-3 first transition sensitivity inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-3 first transition sensitivity inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation3_FirstTransitionPointIteration_BeforeLaminarSeedSystem_FromFullTrace_BitwiseMatchFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "laminar_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 3, "laminar_seed_system").First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 3, "laminar_seed_system").First();

        ParityTraceRecord acceptedReferenceIteration = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem);
        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(acceptedReferenceIteration, "x1"),
            GetFloatHex(acceptedReferenceIteration, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(referenceIteration, "x1"),
            GetFloatHex(referenceIteration, "x2"));

        AssertFloatField(referenceIteration, managedIteration, "ampl1", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ax", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "alpha-0 station-3 first transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "alpha-0 station-3 first transition point iteration");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_FinalTransitionSeedSystem_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_system",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system").Last();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system").Last();

        AssertFloatField(referenceSystem, managedSystem, "uei", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_U2", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "htarg", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "alpha-0 station-4 final transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "alpha-0 station-4 final transition seed system");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_FinalTransitionSeedStep_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step").Last();
        ParityTraceRecord managedStep = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_step"),
                4,
                "laminar_seed_step")
            .Last();

        AssertFloatField(referenceStep, managedStep, "uei", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "theta", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "dstar", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "ratioTheta", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "ratioDstar", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 final transition seed step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-4 final transition seed step");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_FinalStepCtauCarryOwner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system").Last();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system").Last();
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step").Last();
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step").Last();

        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 final ctau carry owner");
        AssertFloatField(referenceStep, managedStep, "deltaShear", "alpha-0 station-4 final ctau carry owner");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 final ctau carry owner");

        float referenceCarry = AddFloat32(
            (float)GetFloatValue(referenceSystem, "ctau"),
            MultiplyFloat32(
                (float)GetFloatValue(referenceStep, "deltaShear"),
                (float)GetFloatValue(referenceStep, "rlx")));
        float managedCarry = AddFloat32(
            (float)GetFloatValue(managedSystem, "ctau"),
            MultiplyFloat32(
                (float)GetFloatValue(managedStep, "deltaShear"),
                (float)GetFloatValue(managedStep, "rlx")));

        Assert.Equal(ToHex(referenceCarry), ToHex(managedCarry));
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13TransitionSeedSystemAndStep_FromFullTrace_BitwiseMatchFortranTrace()
    {
        const int Iteration = 13;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        AssertFloatField(referenceSystem, managedSystem, "uei", "alpha-0 station-4 iteration13 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-4 iteration13 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-4 iteration13 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-4 iteration13 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 iteration13 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "alpha-0 station-4 iteration13 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-4 iteration13 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-4 iteration13 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-4 iteration13 transition seed system");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        AssertFloatField(referenceStep, managedStep, "uei", "alpha-0 station-4 iteration13 transition seed step");
        AssertFloatField(referenceStep, managedStep, "theta", "alpha-0 station-4 iteration13 transition seed step");
        AssertFloatField(referenceStep, managedStep, "dstar", "alpha-0 station-4 iteration13 transition seed step");
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-4 iteration13 transition seed step");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-4 iteration13 transition seed step");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 iteration13 transition seed step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-4 iteration13 transition seed step");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13TransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 13;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration13 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 iteration13 transition interval inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration12TransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 12;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration12 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 iteration12 transition interval inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13CurrentPrimaryStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 13;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "bldif_primary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referencePrimary = SelectPrimaryForInputs(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_primary_station"),
            referenceSystem,
            referenceInputs,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedPrimary = SelectPrimaryForInputs(
            managedRecords.Where(static record => record.Kind == "bldif_primary_station"),
            managedSystem,
            managedInputs,
            ityp: 2,
            station: 2);

        AssertFloatField(referencePrimary, managedPrimary, "x", "alpha-0 station-4 iteration13 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "t", "alpha-0 station-4 iteration13 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "u", "alpha-0 station-4 iteration13 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "d", "alpha-0 station-4 iteration13 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "s", "alpha-0 station-4 iteration13 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "h", "alpha-0 station-4 iteration13 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "hk", "alpha-0 station-4 iteration13 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "rt", "alpha-0 station-4 iteration13 current primary station2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration12CurrentPrimaryStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 12;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "bldif_primary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referencePrimary = SelectPrimaryForInputs(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_primary_station"),
            referenceSystem,
            referenceInputs,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedPrimary = SelectPrimaryForInputs(
            managedRecords.Where(static record => record.Kind == "bldif_primary_station"),
            managedSystem,
            managedInputs,
            ityp: 2,
            station: 2);

        AssertFloatField(referencePrimary, managedPrimary, "x", "alpha-0 station-4 iteration12 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "t", "alpha-0 station-4 iteration12 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "u", "alpha-0 station-4 iteration12 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "d", "alpha-0 station-4 iteration12 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "s", "alpha-0 station-4 iteration12 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "h", "alpha-0 station-4 iteration12 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "hk", "alpha-0 station-4 iteration12 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "rt", "alpha-0 station-4 iteration12 current primary station2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_CurrentPrimaryStation2T_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> referenceSystems = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "bldif_primary_station");
        IReadOnlyList<ParityTraceRecord> managedSystems = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();

        Assert.Equal(referenceSystems.Count, managedSystems.Count);

        for (int i = 0; i < referenceSystems.Count; i++)
        {
            ParityTraceRecord referenceSystem = referenceSystems[i];
            ParityTraceRecord managedSystem = managedSystems[i];
            int iteration = (int)GetFloatValue(referenceSystem, "iteration");

            ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
                ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
                referenceSystem);
            ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
                managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
                managedSystem);

            ParityTraceRecord referencePrimary = SelectPrimaryForInputs(
                ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_primary_station"),
                referenceSystem,
                referenceInputs,
                ityp: 2,
                station: 2);
            ParityTraceRecord managedPrimary = SelectPrimaryForInputs(
                managedRecords.Where(static record => record.Kind == "bldif_primary_station"),
                managedSystem,
                managedInputs,
                ityp: 2,
                station: 2);

            AssertFloatField(referencePrimary, managedPrimary, "t", $"alpha-0 station-4 current primary station2 theta scan iteration {iteration}");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13AcceptedTransitionPointIteration_BeforeSystem_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 13;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceIteration = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem);
        ParityTraceRecord managedIteration = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem);

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl1", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "amcrit", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ax", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "wf2", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual_A2", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "alpha-0 station-4 iteration13 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "relaxation", "alpha-0 station-4 iteration13 accepted transition point iteration");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration12AcceptedTransitionPointIteration_BeforeSystem_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 12;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceIteration = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem);
        ParityTraceRecord managedIteration = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem);

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl1", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "amcrit", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ax", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "wf2", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual_A2", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "alpha-0 station-4 iteration12 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "relaxation", "alpha-0 station-4 iteration12 accepted transition point iteration");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13MatchedTransitionIntervalInputs_FromAcceptedTransitionPoint_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 13;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "transition_point_iteration");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceIntervalCandidate, "x1Original"),
            GetFloatHex(referenceIntervalCandidate, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedIntervalCandidate, "x1Original"),
            GetFloatHex(managedIntervalCandidate, "x2"));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem,
            referenceIteration);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem,
            managedIteration);

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 iteration13 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 iteration13 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 iteration13 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 iteration13 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 iteration13 accepted transition point");

        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration13 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 iteration13 matched transition interval inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13AcceptedTransitionPointIterationLadder_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 13;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord acceptedReferenceIteration = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem);

        string x1Hex = GetFloatHex(acceptedReferenceIteration, "x1");
        string x2Hex = GetFloatHex(acceptedReferenceIteration, "x2");

        IReadOnlyList<ParityTraceRecord> referenceIterations = ParityTraceLoader.ReadMatching(
            referencePath,
            record => record.Kind == "transition_point_iteration" &&
                      record.Sequence < referenceSystem.Sequence &&
                      GetFloatHex(record, "x1") == x1Hex &&
                      GetFloatHex(record, "x2") == x2Hex)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        IReadOnlyList<ParityTraceRecord> managedIterations = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .Where(record => !TryReadInt(record, "station").HasValue || TryReadInt(record, "station") == 4)
            .Where(record => !TryReadInt(record, "stationIteration").HasValue || TryReadInt(record, "stationIteration") == Iteration)
            .Where(record => GetFloatHex(record, "x1") == x1Hex)
            .Where(record => GetFloatHex(record, "x2") == x2Hex)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceIterations.Count, managedIterations.Count);

        for (int i = 0; i < referenceIterations.Count; i++)
        {
            ParityTraceRecord referenceIteration = referenceIterations[i];
            ParityTraceRecord managedIteration = managedIterations[i];

            AssertIntField(referenceIteration, managedIteration, "iteration", $"alpha-0 station-4 iteration13 accepted transition ladder[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "ampl2", $"alpha-0 station-4 iteration13 accepted transition ladder[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "ax", $"alpha-0 station-4 iteration13 accepted transition ladder[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "wf2", $"alpha-0 station-4 iteration13 accepted transition ladder[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "xt", $"alpha-0 station-4 iteration13 accepted transition ladder[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "deltaA2", $"alpha-0 station-4 iteration13 accepted transition ladder[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "relaxation", $"alpha-0 station-4 iteration13 accepted transition ladder[{i}]");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13TransitionPointProbeAndSystemPhases_Agree()
    {
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration");

        ParityTraceRecord probe = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .Where(record =>
                TryReadInt(record, "station") == 4 &&
                TryReadInt(record, "stationIteration") == 13 &&
                TryReadInt(record, "iteration") == 2 &&
                string.Equals(ReadString(record, "phase"), "seed_probe", StringComparison.Ordinal))
            .OrderBy(static record => record.Sequence)
            .Last();

        ParityTraceRecord system = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .Where(record =>
                TryReadInt(record, "station") == 4 &&
                TryReadInt(record, "stationIteration") == 13 &&
                TryReadInt(record, "iteration") == 2 &&
                string.Equals(ReadString(record, "phase"), "transition_interval_system", StringComparison.Ordinal))
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(probe, system, "ampl2", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "ax", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "wf2", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "xt", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "tt", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "dt", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "ut", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "residual", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "residual_A2", "alpha-0 station-4 iteration13 transition point phase agreement");
        AssertFloatField(probe, system, "deltaA2", "alpha-0 station-4 iteration13 transition point phase agreement");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13SeedProbeTransitionPointIterations_FromFullTrace_BitwiseMatchFortranTrace()
    {
        const int Iteration = 13;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        IReadOnlyList<ParityTraceRecord> referenceIterations = ParityTraceLoader.ReadMatching(
            referencePath,
            record => record.Kind == "transition_point_iteration" &&
                      record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .TakeLast(2)
            .ToArray();
        string x1Hex = GetFloatHex(referenceIterations[0], "x1");
        string x2Hex = GetFloatHex(referenceIterations[0], "x2");

        IReadOnlyList<ParityTraceRecord> managedIterations = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .Where(record =>
                TryReadInt(record, "station") == 4 &&
                TryReadInt(record, "stationIteration") == Iteration &&
                string.Equals(ReadString(record, "phase"), "seed_probe", StringComparison.Ordinal) &&
                GetFloatHex(record, "x1") == x1Hex &&
                GetFloatHex(record, "x2") == x2Hex)
            .OrderBy(static record => record.Sequence)
            .TakeLast(2)
            .ToArray();

        Assert.Equal(2, referenceIterations.Count);
        Assert.Equal(2, managedIterations.Count);

        for (int i = 0; i < 2; i++)
        {
            ParityTraceRecord referenceIteration = referenceIterations[i];
            ParityTraceRecord managedIteration = managedIterations[i];

            AssertIntField(referenceIteration, managedIteration, "iteration", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "ampl2", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "wf2", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "xt", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "tt", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "dt", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "ut", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "ax", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "residual", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "residual_A2", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "deltaA2", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
            AssertFloatField(referenceIteration, managedIteration, "relaxation", $"alpha-0 station-4 iteration13 seed-probe transition iteration[{i}]");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration13SeedProbeTransitionSensitivityInputs_FromFullTrace_BitwiseMatchFortranTrace()
    {
        const int Iteration = 13;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_sensitivity_inputs",
            "transition_seed_system",
            "transition_point_iteration");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        IReadOnlyList<ParityTraceRecord> referenceIterations = ParityTraceLoader.ReadMatching(
            referencePath,
            record => record.Kind == "transition_point_iteration" &&
                      record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .TakeLast(2)
            .ToArray();
        string x1Hex = GetFloatHex(referenceIterations[0], "x1");
        string x2Hex = GetFloatHex(referenceIterations[0], "x2");

        IReadOnlyList<ParityTraceRecord> referenceInputs = ParityTraceLoader.ReadMatching(
            referencePath,
            record => record.Kind == "transition_sensitivity_inputs" &&
                      record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .TakeLast(2)
            .ToArray();

        IReadOnlyList<ParityTraceRecord> managedInputs = managedRecords
            .Where(static record => record.Kind == "transition_sensitivity_inputs")
            .Where(record => record.Sequence < GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
                .Single(static system => HasExactDataInt(system, "iteration", Iteration)).Sequence)
            .OrderBy(static record => record.Sequence)
            .TakeLast(2)
            .ToArray();

        IReadOnlyList<ParityTraceRecord> managedIterations = managedRecords
            .Where(static record => record.Kind == "transition_point_iteration")
            .Where(record =>
                TryReadInt(record, "station") == 4 &&
                TryReadInt(record, "stationIteration") == Iteration &&
                string.Equals(ReadString(record, "phase"), "seed_probe", StringComparison.Ordinal) &&
                GetFloatHex(record, "x1") == x1Hex &&
                GetFloatHex(record, "x2") == x2Hex)
            .OrderBy(static record => record.Sequence)
            .TakeLast(2)
            .ToArray();

        Assert.Equal(2, referenceInputs.Count);
        Assert.Equal(2, managedInputs.Count);
        Assert.Equal(2, managedIterations.Count);

        for (int i = 0; i < 2; i++)
        {
            ParityTraceRecord referenceInput = referenceInputs[i];
            ParityTraceRecord managedInput = managedInputs[i];

            AssertFloatField(referenceInput, managedInput, "a1", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
            AssertFloatField(referenceInput, managedInput, "a2", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
            AssertFloatField(referenceInput, managedInput, "acrit", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
            AssertFloatField(referenceInput, managedInput, "hk1", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
            AssertFloatField(referenceInput, managedInput, "hk2", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
            AssertFloatField(referenceInput, managedInput, "t1", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
            AssertFloatField(referenceInput, managedInput, "t2", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
            AssertFloatField(referenceInput, managedInput, "rt1", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
            AssertFloatField(referenceInput, managedInput, "rt2", $"alpha-0 station-4 iteration13 seed-probe transition sensitivity[{i}]");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration2TransitionSeedSystem_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 2;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "uei", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "alpha-0 station-4 iteration2 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "alpha-0 station-4 iteration2 transition seed system");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration2TransitionSeedStepThetaDeltaOwner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 2;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceStep, managedStep, "theta", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "dstar", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "uei", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "deltaUe", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 iteration2 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-4 iteration2 transition seed step owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration12TransitionSeedStepThetaDeltaOwner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 12;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceStep, managedStep, "theta", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "dstar", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "uei", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "deltaUe", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 iteration12 transition seed step owner");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-4 iteration12 transition seed step owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration11TransitionSeedStepCarryOwner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 11;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceStep, managedStep, "theta", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "dstar", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "uei", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "deltaUe", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 iteration11 transition seed step carry owner");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-4 iteration11 transition seed step carry owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration12TransitionSeedSystemOwner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 12;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "uei", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row11", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row12", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row13", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row14", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row21", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row22", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row23", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row24", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row31", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row32", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row33", "alpha-0 station-4 iteration12 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row34", "alpha-0 station-4 iteration12 transition seed system owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration11TransitionSeedSystemOwner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 11;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "uei", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row11", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row12", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row13", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row14", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row21", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row22", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row23", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row24", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row31", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row32", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row33", "alpha-0 station-4 iteration11 transition seed system owner");
        AssertFloatField(referenceSystem, managedSystem, "row34", "alpha-0 station-4 iteration11 transition seed system owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8TransitionSeedSystem_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "uei", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "alpha-0 station-4 iteration8 transition seed system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "alpha-0 station-4 iteration8 transition seed system");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8TransitionSeedStep_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceStep, managedStep, "uei", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "theta", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "dstar", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "ratioTheta", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "ratioDstar", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 iteration8 transition seed step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "alpha-0 station-4 iteration8 transition seed step");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8StepThetaCarryOwner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceNextSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration + 1));
        ParityTraceRecord managedNextSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration + 1));

        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 station-4 iteration8 theta carry owner");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "alpha-0 station-4 iteration8 theta carry owner");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 iteration8 theta carry owner");

        float referenceCarry = AddFloat32(
            (float)GetFloatValue(referenceSystem, "theta"),
            MultiplyFloat32(
                (float)GetFloatValue(referenceStep, "deltaTheta"),
                (float)GetFloatValue(referenceStep, "rlx")));
        float managedCarry = AddFloat32(
            (float)GetFloatValue(managedSystem, "theta"),
            MultiplyFloat32(
                (float)GetFloatValue(managedStep, "deltaTheta"),
                (float)GetFloatValue(managedStep, "rlx")));

        Assert.Equal(ToHex(referenceCarry), ToHex(managedCarry));
        Assert.Equal(ToHex(referenceCarry), GetFloatHex(referenceNextSystem, "theta"));
        Assert.Equal(ToHex(managedCarry), GetFloatHex(managedNextSystem, "theta"));
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7TransitionSeedSystemRow12_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "row12", "alpha-0 station-4 iteration7 transition seed system row12");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6TransitionSeedSystemRow22_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "row22", "alpha-0 station-4 iteration6 transition seed system row22");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7Eq1ThetaRowTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_t_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_t_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_t_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "upwT1Term", "alpha-0 station-4 iteration7 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "de1T1Term", "alpha-0 station-4 iteration7 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "us1T1Term", "alpha-0 station-4 iteration7 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row12Transport", "alpha-0 station-4 iteration7 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cq1T1Term", "alpha-0 station-4 iteration7 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf1T1Term", "alpha-0 station-4 iteration7 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "hk1T1Term", "alpha-0 station-4 iteration7 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row12", "alpha-0 station-4 iteration7 eq1 theta row");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8Eq1ThetaRowStation2Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_t_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_t_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_t_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "upwT2Term", "alpha-0 station-4 iteration8 eq1 theta row station2");
        AssertFloatField(referenceTerms, managedTerms, "de2T2Term", "alpha-0 station-4 iteration8 eq1 theta row station2");
        AssertFloatField(referenceTerms, managedTerms, "us2T2Term", "alpha-0 station-4 iteration8 eq1 theta row station2");
        AssertFloatField(referenceTerms, managedTerms, "row22Transport", "alpha-0 station-4 iteration8 eq1 theta row station2");
        AssertFloatField(referenceTerms, managedTerms, "cq2T2Term", "alpha-0 station-4 iteration8 eq1 theta row station2");
        AssertFloatField(referenceTerms, managedTerms, "cf2T2Term", "alpha-0 station-4 iteration8 eq1 theta row station2");
        AssertFloatField(referenceTerms, managedTerms, "hk2T2Term", "alpha-0 station-4 iteration8 eq1 theta row station2");
        AssertFloatField(referenceTerms, managedTerms, "row22", "alpha-0 station-4 iteration8 eq1 theta row station2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7Eq1ThetaUpwProducerChain_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_common",
            "bldif_upw_terms",
            "bldif_z_upw_terms",
            "bldif_eq1_t_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceCommon = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_common" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCommon = managedRecords
            .Where(static record => record.Kind == "bldif_common" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4) &&
                                    HasExactDataInt(record, "iteration", Iteration) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        ParityTraceRecord referenceUpw = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_upw_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedUpw = managedRecords
            .Where(static record => record.Kind == "bldif_upw_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4) &&
                                    HasExactDataInt(record, "iteration", Iteration) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        ParityTraceRecord referenceZUpw = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_z_upw_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedZUpw = managedRecords
            .Where(static record => record.Kind == "bldif_z_upw_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4) &&
                                    HasExactDataInt(record, "iteration", Iteration) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_t_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_t_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4) &&
                                    HasExactDataInt(record, "iteration", Iteration) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceCommon, managedCommon, "upw", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "hk1", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "hk2", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "hl", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "hlsq", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "ehh", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "upwHl", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "upwHd", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "upwHk1", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceUpw, managedUpw, "upwT1", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "zSa", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "sDelta", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "cqTerm", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "sTerm", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "cfTerm", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "hkTerm", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "sum12", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "sum123", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceZUpw, managedZUpw, "zUpw", "alpha-0 station-4 iteration7 eq1 theta upw chain");
        AssertFloatField(referenceTerms, managedTerms, "upwT1Term", "alpha-0 station-4 iteration7 eq1 theta upw chain");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7CurrentPrimaryStation1_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "bldif_primary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referencePrimary = SelectPrimaryForInputs(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_primary_station"),
            referenceSystem,
            referenceInputs,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedPrimary = SelectPrimaryForInputs(
            managedRecords.Where(static record => record.Kind == "bldif_primary_station"),
            managedSystem,
            managedInputs,
            ityp: 2,
            station: 1);

        AssertFloatField(referencePrimary, managedPrimary, "x", "alpha-0 station-4 iteration7 current primary station1");
        AssertFloatField(referencePrimary, managedPrimary, "u", "alpha-0 station-4 iteration7 current primary station1");
        AssertFloatField(referencePrimary, managedPrimary, "t", "alpha-0 station-4 iteration7 current primary station1");
        AssertFloatField(referencePrimary, managedPrimary, "d", "alpha-0 station-4 iteration7 current primary station1");
        AssertFloatField(referencePrimary, managedPrimary, "s", "alpha-0 station-4 iteration7 current primary station1");
        AssertFloatField(referencePrimary, managedPrimary, "h", "alpha-0 station-4 iteration7 current primary station1");
        AssertFloatField(referencePrimary, managedPrimary, "hk", "alpha-0 station-4 iteration7 current primary station1");
        AssertFloatField(referencePrimary, managedPrimary, "rt", "alpha-0 station-4 iteration7 current primary station1");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7CurrentPrimaryStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "bldif_primary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referencePrimary = SelectPrimaryForInputs(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_primary_station"),
            referenceSystem,
            referenceInputs,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedPrimary = SelectPrimaryForInputs(
            managedRecords.Where(static record => record.Kind == "bldif_primary_station"),
            managedSystem,
            managedInputs,
            ityp: 2,
            station: 2);

        AssertFloatField(referencePrimary, managedPrimary, "x", "alpha-0 station-4 iteration7 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "u", "alpha-0 station-4 iteration7 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "t", "alpha-0 station-4 iteration7 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "d", "alpha-0 station-4 iteration7 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "s", "alpha-0 station-4 iteration7 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "h", "alpha-0 station-4 iteration7 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "hk", "alpha-0 station-4 iteration7 current primary station2");
        AssertFloatField(referencePrimary, managedPrimary, "rt", "alpha-0 station-4 iteration7 current primary station2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7TransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceAcceptedIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceIntervalCandidate, "x1Original"),
            GetFloatHex(referenceIntervalCandidate, "x2"));
        ParityTraceRecord managedAcceptedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedIntervalCandidate, "x1Original"),
            GetFloatHex(managedIntervalCandidate, "x2"));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem,
            referenceAcceptedIteration);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem,
            managedAcceptedIteration);

        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration7 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 iteration7 transition interval inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7TransitionSeedSystemCtau_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 iteration7 transition seed system");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6StepCtauCarryOwner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "ctau", "alpha-0 station-4 iteration6 ctau carry owner");
        AssertFloatField(referenceStep, managedStep, "deltaShear", "alpha-0 station-4 iteration6 ctau carry owner");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 station-4 iteration6 ctau carry owner");

        float referenceCarry = AddFloat32(
            (float)GetFloatValue(referenceSystem, "ctau"),
            MultiplyFloat32(
                (float)GetFloatValue(referenceStep, "deltaShear"),
                (float)GetFloatValue(referenceStep, "rlx")));
        float managedCarry = AddFloat32(
            (float)GetFloatValue(managedSystem, "ctau"),
            MultiplyFloat32(
                (float)GetFloatValue(managedStep, "deltaShear"),
                (float)GetFloatValue(managedStep, "rlx")));

        Assert.Equal(ToHex(referenceCarry), ToHex(managedCarry));
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8TransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration8 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 iteration8 transition interval inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7MatchedTransitionIntervalInputs_FromAcceptedTransitionPoint_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "transition_point_iteration");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceIntervalCandidate, "x1Original"),
            GetFloatHex(referenceIntervalCandidate, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedIntervalCandidate, "x1Original"),
            GetFloatHex(managedIntervalCandidate, "x2"));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem,
            referenceIteration);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem,
            managedIteration);

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 iteration7 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 iteration7 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 iteration7 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-4 iteration7 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 iteration7 accepted transition point");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 iteration7 accepted transition point");

        AssertFloatField(referenceInputs, managedInputs, "x1Original", "alpha-0 station-4 iteration7 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "alpha-0 station-4 iteration7 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration7 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-4 iteration7 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "alpha-0 station-4 iteration7 matched transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration7 matched transition interval inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7MatchedTransitionIntervalInputsD1_ReblendsBeyondAcceptedDt_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "transition_point_iteration");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceIntervalCandidate, "x1Original"),
            GetFloatHex(referenceIntervalCandidate, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedIntervalCandidate, "x1Original"),
            GetFloatHex(managedIntervalCandidate, "x2"));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem,
            referenceIteration);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem,
            managedIteration);

        AssertHex(
            GetFloatHex(referenceIteration, "dt"),
            GetFloatHex(managedIteration, "dt"),
            "alpha-0 station-4 iteration7 accepted transition point dt");
        AssertHex(
            GetFloatHex(referenceInputs, "d1"),
            GetFloatHex(managedInputs, "d1"),
            "alpha-0 station-4 iteration7 matched transition interval d1");
        Assert.NotEqual(
            GetFloatHex(referenceIteration, "dt"),
            GetFloatHex(referenceInputs, "d1"));
        Assert.NotEqual(
            GetFloatHex(managedIteration, "dt"),
            GetFloatHex(managedInputs, "d1"));
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow12OwnerTransitionIntervalBt2Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_bt2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "row", 1) &&
                                 HasExactDataInt(record, "column", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "transition_interval_bt2_terms" &&
                                    HasExactDataInt(record, "row", 1) &&
                                    HasExactDataInt(record, "column", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceTerms, managedTerms, "baseBits", "alpha-0 station-4 iteration7 system-row12 owner transition bt2 row12");
        AssertIntField(referenceTerms, managedTerms, "stBits", "alpha-0 station-4 iteration7 system-row12 owner transition bt2 row12");
        AssertIntField(referenceTerms, managedTerms, "ttBits", "alpha-0 station-4 iteration7 system-row12 owner transition bt2 row12");
        AssertIntField(referenceTerms, managedTerms, "dtBits", "alpha-0 station-4 iteration7 system-row12 owner transition bt2 row12");
        AssertIntField(referenceTerms, managedTerms, "utBits", "alpha-0 station-4 iteration7 system-row12 owner transition bt2 row12");
        AssertIntField(referenceTerms, managedTerms, "xtBits", "alpha-0 station-4 iteration7 system-row12 owner transition bt2 row12");
        AssertIntField(referenceTerms, managedTerms, "finalBits", "alpha-0 station-4 iteration7 system-row12 owner transition bt2 row12");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration2SystemRow13OwnerEq1DRow23Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 2;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_d_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_d_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_d_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row23BaseTerm", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23UpwTerm", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23DeTerm", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23UsTerm", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23Transport", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23CqTerm", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23CfTerm", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23HkTerm", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23", "alpha-0 station-4 iteration2 system-row13 owner eq1 d row23");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration2SystemRow14OwnerEq1URow24Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 2;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_u_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_u_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_u_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row24BaseTerm", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
        AssertFloatField(referenceTerms, managedTerms, "row24UpwTerm", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
        AssertFloatField(referenceTerms, managedTerms, "row24DeTerm", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
        AssertFloatField(referenceTerms, managedTerms, "row24UsTerm", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
        AssertFloatField(referenceTerms, managedTerms, "row24Transport", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
        AssertFloatField(referenceTerms, managedTerms, "row24CqTerm", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
        AssertFloatField(referenceTerms, managedTerms, "row24CfTerm", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
        AssertFloatField(referenceTerms, managedTerms, "row24HkTerm", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
        AssertFloatField(referenceTerms, managedTerms, "row24", "alpha-0 station-4 iteration2 system-row14 owner eq1 u row24");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration2SystemRow14OwnerTransitionIntervalBt2Row14Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 2;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_bt2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "row", 1) &&
                                 HasExactDataInt(record, "column", 4))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "transition_interval_bt2_terms" &&
                                    HasExactDataInt(record, "row", 1) &&
                                    HasExactDataInt(record, "column", 4))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertIntField(referenceTerms, managedTerms, "baseBits", "alpha-0 station-4 iteration2 system-row14 owner transition bt2 row14");
        AssertIntField(referenceTerms, managedTerms, "stBits", "alpha-0 station-4 iteration2 system-row14 owner transition bt2 row14");
        AssertIntField(referenceTerms, managedTerms, "ttBits", "alpha-0 station-4 iteration2 system-row14 owner transition bt2 row14");
        AssertIntField(referenceTerms, managedTerms, "dtBits", "alpha-0 station-4 iteration2 system-row14 owner transition bt2 row14");
        AssertIntField(referenceTerms, managedTerms, "utBits", "alpha-0 station-4 iteration2 system-row14 owner transition bt2 row14");
        AssertIntField(referenceTerms, managedTerms, "xtBits", "alpha-0 station-4 iteration2 system-row14 owner transition bt2 row14");
        AssertIntField(referenceTerms, managedTerms, "finalBits", "alpha-0 station-4 iteration2 system-row14 owner transition bt2 row14");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_TransitionSeedSystemDstar_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();

        AssertOrderedFieldParity(referenceRecords, managedRecords, "dstar", "alpha-0 station-4 transition seed system dstar scan");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_TransitionSeedSystemTheta_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "transition_seed_system"),
                4,
                "transition_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();

        AssertOrderedFieldParity(referenceRecords, managedRecords, "theta", "alpha-0 station-4 transition seed system theta scan");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_TransitionSeedStepDeltaDstar_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecords(
                RunManagedPreparationTraceForCase(Alpha0FullCaseId, "laminar_seed_step"),
                4,
                "laminar_seed_step")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();

        AssertOrderedFieldParity(referenceRecords, managedRecords, "deltaDstar", "alpha-0 station-4 transition seed step delta-dstar scan");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration2StepDeltaDstarOwnerGaussBacksub_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 2;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        Assert.Equal("backsub", ReadStringField(referenceGauss, "phase"));
        Assert.Equal("backsub", ReadStringField(managedGauss, "phase"));
        AssertIntField(referenceGauss, managedGauss, "pivotIndex", "alpha-0 station-4 iteration2 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs1", "alpha-0 station-4 iteration2 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs2", "alpha-0 station-4 iteration2 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs3", "alpha-0 station-4 iteration2 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs4", "alpha-0 station-4 iteration2 gauss backsub owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration2GaussPhaseWindow_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 2;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        IReadOnlyList<ParityTraceRecord> referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > referenceSystem.Sequence && record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > managedSystem.Sequence && record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceGauss.Count, managedGauss.Count);

        string[] matrixFields =
        [
            "row11", "row12", "row13", "row14",
            "row21", "row22", "row23", "row24",
            "row31", "row32", "row33", "row34",
            "row41", "row42", "row43", "row44",
            "rhs1", "rhs2", "rhs3", "rhs4"
        ];

        for (int index = 0; index < referenceGauss.Count; index++)
        {
            ParityTraceRecord expected = referenceGauss[index];
            ParityTraceRecord actual = managedGauss[index];
            string phase = ReadStringField(expected, "phase");
            Assert.Equal(phase, ReadStringField(actual, "phase"));
            AssertIntField(expected, actual, "pivotIndex", $"alpha-0 station-4 iteration2 gauss phase[{index}]");

            foreach (string field in matrixFields)
            {
                AssertFloatField(expected, actual, field, $"alpha-0 station-4 iteration2 gauss {phase} snapshot[{index}]");
            }
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3StepDeltaDstarOwnerGaussBacksub_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        Assert.Equal("backsub", ReadStringField(referenceGauss, "phase"));
        Assert.Equal("backsub", ReadStringField(managedGauss, "phase"));
        AssertIntField(referenceGauss, managedGauss, "pivotIndex", "alpha-0 station-4 iteration3 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs1", "alpha-0 station-4 iteration3 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs2", "alpha-0 station-4 iteration3 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs3", "alpha-0 station-4 iteration3 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs4", "alpha-0 station-4 iteration3 gauss backsub owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration4StepDeltaDstarOwnerGaussBacksub_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 4;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        Assert.Equal("backsub", ReadStringField(referenceGauss, "phase"));
        Assert.Equal("backsub", ReadStringField(managedGauss, "phase"));
        AssertIntField(referenceGauss, managedGauss, "pivotIndex", "alpha-0 station-4 iteration4 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs1", "alpha-0 station-4 iteration4 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs2", "alpha-0 station-4 iteration4 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs3", "alpha-0 station-4 iteration4 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs4", "alpha-0 station-4 iteration4 gauss backsub owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5StepDeltaDstarOwnerGaussBacksub_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        Assert.Equal("backsub", ReadStringField(referenceGauss, "phase"));
        Assert.Equal("backsub", ReadStringField(managedGauss, "phase"));
        AssertIntField(referenceGauss, managedGauss, "pivotIndex", "alpha-0 station-4 iteration5 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs1", "alpha-0 station-4 iteration5 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs2", "alpha-0 station-4 iteration5 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs3", "alpha-0 station-4 iteration5 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs4", "alpha-0 station-4 iteration5 gauss backsub owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6StepDeltaShearOwnerGaussBacksub_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        Assert.Equal("backsub", ReadStringField(referenceGauss, "phase"));
        Assert.Equal("backsub", ReadStringField(managedGauss, "phase"));
        AssertIntField(referenceGauss, managedGauss, "pivotIndex", "alpha-0 station-4 iteration6 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs1", "alpha-0 station-4 iteration6 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs2", "alpha-0 station-4 iteration6 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs3", "alpha-0 station-4 iteration6 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs4", "alpha-0 station-4 iteration6 gauss backsub owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7StepDeltaDstarOwnerGaussBacksub_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        Assert.Equal("backsub", ReadStringField(referenceGauss, "phase"));
        Assert.Equal("backsub", ReadStringField(managedGauss, "phase"));
        AssertIntField(referenceGauss, managedGauss, "pivotIndex", "alpha-0 station-4 iteration7 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs1", "alpha-0 station-4 iteration7 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs2", "alpha-0 station-4 iteration7 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs3", "alpha-0 station-4 iteration7 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs4", "alpha-0 station-4 iteration7 gauss backsub owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8StepDeltaDstarOwnerGaussBacksub_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        Assert.Equal("backsub", ReadStringField(referenceGauss, "phase"));
        Assert.Equal("backsub", ReadStringField(managedGauss, "phase"));
        AssertIntField(referenceGauss, managedGauss, "pivotIndex", "alpha-0 station-4 iteration8 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs1", "alpha-0 station-4 iteration8 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs2", "alpha-0 station-4 iteration8 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs3", "alpha-0 station-4 iteration8 gauss backsub owner");
        AssertFloatField(referenceGauss, managedGauss, "rhs4", "alpha-0 station-4 iteration8 gauss backsub owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8GaussPhaseWindow_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        IReadOnlyList<ParityTraceRecord> referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > referenceSystem.Sequence && record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > managedSystem.Sequence && record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceGauss.Count, managedGauss.Count);

        string[] matrixFields =
        [
            "row11", "row12", "row13", "row14",
            "row21", "row22", "row23", "row24",
            "row31", "row32", "row33", "row34",
            "row41", "row42", "row43", "row44",
            "rhs1", "rhs2", "rhs3", "rhs4"
        ];

        for (int index = 0; index < referenceGauss.Count; index++)
        {
            ParityTraceRecord expected = referenceGauss[index];
            ParityTraceRecord actual = managedGauss[index];
            string phase = ReadStringField(expected, "phase");
            Assert.Equal(phase, ReadStringField(actual, "phase"));
            AssertIntField(expected, actual, "pivotIndex", $"alpha-0 station-4 iteration8 gauss phase[{index}]");

            foreach (string field in matrixFields)
            {
                AssertFloatField(expected, actual, field, $"alpha-0 station-4 iteration8 gauss {phase} snapshot[{index}]");
            }
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3TransitionSeedSystemRow34_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        AssertFloatField(referenceSystem, managedSystem, "row34", "alpha-0 station-4 iteration3 transition seed system row34");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3GaussPhaseWindow_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        IReadOnlyList<ParityTraceRecord> referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > referenceSystem.Sequence && record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > managedSystem.Sequence && record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceGauss.Count, managedGauss.Count);

        string[] matrixFields =
        [
            "row11", "row12", "row13", "row14",
            "row21", "row22", "row23", "row24",
            "row31", "row32", "row33", "row34",
            "row41", "row42", "row43", "row44",
            "rhs1", "rhs2", "rhs3", "rhs4"
        ];

        for (int index = 0; index < referenceGauss.Count; index++)
        {
            ParityTraceRecord expected = referenceGauss[index];
            ParityTraceRecord actual = managedGauss[index];
            string phase = ReadStringField(expected, "phase");
            Assert.Equal(phase, ReadStringField(actual, "phase"));
            AssertIntField(expected, actual, "pivotIndex", $"alpha-0 station-4 iteration3 gauss phase[{index}]");

            foreach (string field in matrixFields)
            {
                AssertFloatField(expected, actual, field, $"alpha-0 station-4 iteration3 gauss {phase} snapshot[{index}]");
            }
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration4GaussPhaseWindow_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 4;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        IReadOnlyList<ParityTraceRecord> referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > referenceSystem.Sequence && record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > managedSystem.Sequence && record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceGauss.Count, managedGauss.Count);

        string[] matrixFields =
        [
            "row11", "row12", "row13", "row14",
            "row21", "row22", "row23", "row24",
            "row31", "row32", "row33", "row34",
            "row41", "row42", "row43", "row44",
            "rhs1", "rhs2", "rhs3", "rhs4"
        ];

        for (int index = 0; index < referenceGauss.Count; index++)
        {
            ParityTraceRecord expected = referenceGauss[index];
            ParityTraceRecord actual = managedGauss[index];
            string phase = ReadStringField(expected, "phase");
            Assert.Equal(phase, ReadStringField(actual, "phase"));
            AssertIntField(expected, actual, "pivotIndex", $"alpha-0 station-4 iteration4 gauss phase[{index}]");

            foreach (string field in matrixFields)
            {
                AssertFloatField(expected, actual, field, $"alpha-0 station-4 iteration4 gauss {phase} snapshot[{index}]");
            }
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5GaussPhaseWindow_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        IReadOnlyList<ParityTraceRecord> referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > referenceSystem.Sequence && record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > managedSystem.Sequence && record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceGauss.Count, managedGauss.Count);

        string[] matrixFields =
        [
            "row11", "row12", "row13", "row14",
            "row21", "row22", "row23", "row24",
            "row31", "row32", "row33", "row34",
            "row41", "row42", "row43", "row44",
            "rhs1", "rhs2", "rhs3", "rhs4"
        ];

        for (int index = 0; index < referenceGauss.Count; index++)
        {
            ParityTraceRecord expected = referenceGauss[index];
            ParityTraceRecord actual = managedGauss[index];
            string phase = ReadStringField(expected, "phase");
            Assert.Equal(phase, ReadStringField(actual, "phase"));
            AssertIntField(expected, actual, "pivotIndex", $"alpha-0 station-4 iteration5 gauss phase[{index}]");

            foreach (string field in matrixFields)
            {
                AssertFloatField(expected, actual, field, $"alpha-0 station-4 iteration5 gauss {phase} snapshot[{index}]");
            }
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6GaussPhaseWindow_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        IReadOnlyList<ParityTraceRecord> referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > referenceSystem.Sequence && record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > managedSystem.Sequence && record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceGauss.Count, managedGauss.Count);

        string[] matrixFields =
        [
            "row11", "row12", "row13", "row14",
            "row21", "row22", "row23", "row24",
            "row31", "row32", "row33", "row34",
            "row41", "row42", "row43", "row44",
            "rhs1", "rhs2", "rhs3", "rhs4"
        ];

        for (int index = 0; index < referenceGauss.Count; index++)
        {
            ParityTraceRecord expected = referenceGauss[index];
            ParityTraceRecord actual = managedGauss[index];
            string phase = ReadStringField(expected, "phase");
            Assert.Equal(phase, ReadStringField(actual, "phase"));
            AssertIntField(expected, actual, "pivotIndex", $"alpha-0 station-4 iteration6 gauss phase[{index}]");

            foreach (string field in matrixFields)
            {
                AssertFloatField(expected, actual, field, $"alpha-0 station-4 iteration6 gauss {phase} snapshot[{index}]");
            }
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7GaussPhaseWindow_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "gauss_state",
            "laminar_seed_step");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        IReadOnlyList<ParityTraceRecord> referenceGauss = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > referenceSystem.Sequence && record.Sequence < referenceStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedGauss = managedRecords
            .Where(static record => record.Kind == "gauss_state")
            .Where(record => record.Sequence > managedSystem.Sequence && record.Sequence < managedStep.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceGauss.Count, managedGauss.Count);

        string[] matrixFields =
        [
            "row11", "row12", "row13", "row14",
            "row21", "row22", "row23", "row24",
            "row31", "row32", "row33", "row34",
            "row41", "row42", "row43", "row44",
            "rhs1", "rhs2", "rhs3", "rhs4"
        ];

        for (int index = 0; index < referenceGauss.Count; index++)
        {
            ParityTraceRecord expected = referenceGauss[index];
            ParityTraceRecord actual = managedGauss[index];
            string phase = ReadStringField(expected, "phase");
            Assert.Equal(phase, ReadStringField(actual, "phase"));
            AssertIntField(expected, actual, "pivotIndex", $"alpha-0 station-4 iteration7 gauss phase[{index}]");

            foreach (string field in matrixFields)
            {
                AssertFloatField(expected, actual, field, $"alpha-0 station-4 iteration7 gauss {phase} snapshot[{index}]");
            }
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow11OwnerEq1RowsRow21_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_rows");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceRows = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedRows = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceRows, managedRows, "row21", "alpha-0 station-4 iteration3 system-row11 owner eq1 rows row21");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow11OwnerEq1RowsRow21_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_rows");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceRows = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedRows = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceRows, managedRows, "row21", "alpha-0 station-4 iteration8 system-row11 owner eq1 rows row21");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22AndRow13OwnerEq1RowsRow13_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_rows");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceRows = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedRows = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceRows, managedRows, "row13", "alpha-0 station-4 iteration8 system-row22 and row13 owner eq1 rows row13");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerEq1RowsRow21_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_rows");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceRows = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedRows = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceRows, managedRows, "row21", "alpha-0 station-4 iteration6 system-row11 owner eq1 rows row21");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow11OwnerEq1RowsRow21_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_rows");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceRows = ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedRows = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_rows")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceRows, managedRows, "row21", "alpha-0 station-4 iteration7 system-row11 owner eq1 rows row21");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow11OwnerSecondaryStation1_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        string[] fields =
        [
            "hc", "hs", "hsHk", "hkD", "hsD", "hsT",
            "us", "usT", "hkU", "rtT", "rtU",
            "cq", "cf", "cfU", "cfT", "cfD", "cfMs",
            "cfmU", "cfmT", "cfmD", "cfmMs", "di", "diT", "de"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceSecondary, managedSecondary, field, "alpha-0 station-4 iteration3 system-row11 owner secondary station1");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow11OwnerSecondaryStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        string[] fields =
        [
            "hc", "hs", "hsHk", "hkD", "hsD", "hsT",
            "us", "usT", "hkU", "rtT", "rtU",
            "cq", "cf", "cfU", "cfT", "cfD", "cfMs",
            "cfmU", "cfmT", "cfmD", "cfmMs", "di", "diT", "de"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceSecondary, managedSecondary, field, "alpha-0 station-4 iteration3 system-row11 owner secondary station2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow11OwnerSecondaryStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        string[] fields =
        [
            "hc", "hs", "hsHk", "hkD", "hsD", "hsT",
            "us", "usT", "hkU", "rtT", "rtU",
            "cq", "cf", "cfU", "cfT", "cfD", "cfMs",
            "cfmU", "cfmT", "cfmD", "cfmMs", "di", "diT", "de"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceSecondary, managedSecondary, field, "alpha-0 station-4 iteration8 system-row11 owner secondary station2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerSecondaryStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        string[] fields =
        [
            "hc", "hs", "hsHk", "hkD", "hsD", "hsT",
            "us", "usT", "hkU", "rtT", "rtU",
            "cq", "cf", "cfU", "cfT", "cfD", "cfMs",
            "cfmU", "cfmT", "cfmD", "cfmMs", "di", "diT", "de"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceSecondary, managedSecondary, field, "alpha-0 station-4 iteration6 system-row11 owner secondary station2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow11OwnerSecondaryStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        string[] fields =
        [
            "hc", "hs", "hsHk", "hkD", "hsD", "hsT",
            "us", "usT", "hkU", "rtT", "rtU",
            "cq", "cf", "cfU", "cfT", "cfD", "cfMs",
            "cfmU", "cfmT", "cfmD", "cfmMs", "di", "diT", "de"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceSecondary, managedSecondary, field, "alpha-0 station-4 iteration7 system-row11 owner secondary station2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow11OwnerStation2TurbulentDiTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "blvar_turbulent_di_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceTerms = SelectTurbulentDiTermsForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blvar_turbulent_di_terms"),
            referenceSecondary);
        ParityTraceRecord managedTerms = SelectTurbulentDiTermsForSecondary(
            managedRecords.Where(static record => record.Kind == "blvar_turbulent_di_terms"),
            managedSecondary);

        string[] fields =
        [
            "cf2t", "cf2tHk", "cf2tRt", "cf2tM", "cf2tD",
            "diWallRaw", "diWallHs", "diWallUs", "diWallCf",
            "diWallDPreDfac", "grt", "hmin", "hmRt", "fl", "dfac", "dfHk", "dfRt",
            "dd", "ddHs", "ddUs", "ddD", "ddl", "ddlHs", "ddlUs", "ddlRt", "ddlD",
            "dil", "dilHk", "dilRt", "finalDi", "finalDiD"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceTerms, managedTerms, field, "alpha-0 station-4 iteration8 system-row11 owner station2 turbulent di");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow11OwnerStation2OuterDiTerms_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;
        const string ReferenceCaseId = "n0012_re1e6_a0_p12_n9_di_t_outer";

        string referencePath = FortranReferenceCases.GetReferenceTracePath(ReferenceCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            ReferenceCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "blvar_outer_di_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceTerms = SelectOuterDiTermsForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blvar_outer_di_terms"),
            referenceSecondary);
        ParityTraceRecord managedTerms = SelectOuterDiTermsForSecondary(
            managedRecords.Where(static record => record.Kind == "blvar_outer_di_terms"),
            managedSecondary);

        string[] fields =
        [
            "dd",
            "ddHs",
            "ddS",
            "ddT",
            "ddUs",
            "ddl",
            "ddlHs",
            "ddlRt",
            "ddlT",
            "ddlUs",
            "finalDiT",
            "hsT",
            "rtT",
            "usT"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceTerms, managedTerms, field, "alpha-0 station-4 iteration8 system-row11 owner station2 outer di");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerStation2TurbulentDiTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "blvar_turbulent_di_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceTerms = SelectTurbulentDiTermsForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blvar_turbulent_di_terms"),
            referenceSecondary);
        ParityTraceRecord managedTerms = SelectTurbulentDiTermsForSecondary(
            managedRecords.Where(static record => record.Kind == "blvar_turbulent_di_terms"),
            managedSecondary);

        string[] fields =
        [
            "cf2t", "cf2tHk", "cf2tRt", "cf2tM", "cf2tD",
            "diWallRaw", "diWallHs", "diWallUs", "diWallCf",
            "diWallDPreDfac", "grt", "hmin", "hmRt", "fl", "dfac", "dfHk", "dfRt",
            "dd", "ddHs", "ddUs", "ddD", "ddl", "ddlHs", "ddlUs", "ddlRt", "ddlD",
            "dil", "dilHk", "dilRt", "finalDi", "finalDiD"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceTerms, managedTerms, field, "alpha-0 station-4 iteration6 system-row11 owner station2 turbulent di");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerBlmidCfTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "blmid_cf_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "blmid_cf_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "blmid_cf_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "cfm", "alpha-0 station-4 iteration6 system-row11 owner blmid cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmHka", "alpha-0 station-4 iteration6 system-row11 owner blmid cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmRta", "alpha-0 station-4 iteration6 system-row11 owner blmid cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmMa", "alpha-0 station-4 iteration6 system-row11 owner blmid cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmU2", "alpha-0 station-4 iteration6 system-row11 owner blmid cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmT2", "alpha-0 station-4 iteration6 system-row11 owner blmid cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmD2", "alpha-0 station-4 iteration6 system-row11 owner blmid cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmMs", "alpha-0 station-4 iteration6 system-row11 owner blmid cf terms");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerBlmidCandidateCfTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "blmid_candidate_cf_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "blmid_candidate_cf_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "blmid_candidate_cf_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "hka", "alpha-0 station-4 iteration6 system-row11 owner blmid candidate cf terms");
        AssertFloatField(referenceTerms, managedTerms, "rta", "alpha-0 station-4 iteration6 system-row11 owner blmid candidate cf terms");
        AssertFloatField(referenceTerms, managedTerms, "ma", "alpha-0 station-4 iteration6 system-row11 owner blmid candidate cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmTurb", "alpha-0 station-4 iteration6 system-row11 owner blmid candidate cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfmLam", "alpha-0 station-4 iteration6 system-row11 owner blmid candidate cf terms");
        AssertIntField(referenceTerms, managedTerms, "usedLaminar", "alpha-0 station-4 iteration6 system-row11 owner blmid candidate cf terms");
        AssertFloatField(referenceTerms, managedTerms, "cfm", "alpha-0 station-4 iteration6 system-row11 owner blmid candidate cf terms");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7Eq1ThetaUpwOwnerStation2TurbulentDiTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "blvar_turbulent_di_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceTerms = SelectTurbulentDiTermsForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blvar_turbulent_di_terms"),
            referenceSecondary);
        ParityTraceRecord managedTerms = SelectTurbulentDiTermsForSecondary(
            managedRecords.Where(static record => record.Kind == "blvar_turbulent_di_terms"),
            managedSecondary);

        string[] fields =
        [
            "cf2t", "cf2tHk", "cf2tRt", "cf2tM", "cf2tD",
            "diWallRaw", "diWallHs", "diWallUs", "diWallCf",
            "diWallDPreDfac", "grt", "hmin", "hmRt", "fl", "dfac", "dfHk", "dfRt",
            "dd", "ddHs", "ddUs", "ddD", "ddl", "ddlHs", "ddlUs", "ddlRt", "ddlD",
            "dil", "dilHk", "dilRt", "finalDi", "finalDiD"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceTerms, managedTerms, field, "alpha-0 station-4 iteration7 eq1 theta upw owner station2 turbulent di");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow11OwnerStation2TurbulentDUpdateTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "blvar_turbulent_d_update_terms",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "blvar_turbulent_d_update_terms" &&
                                 HasExactDataInt(record, "station", 2))
            .Where(record => record.Sequence < referenceSecondary.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "blvar_turbulent_d_update_terms" &&
                                    HasExactDataInt(record, "station", 2))
            .Where(record => record.Sequence < managedSecondary.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "hsHk", "hkD", "hsD", "ddHs", "ddUs", "ddD", "ddlHs", "ddlUs", "ddlD", "finalDiD"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceTerms, managedTerms, field, "alpha-0 station-4 iteration7 system-row11 owner station2 turbulent d update terms");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow11OwnerStation2CfTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "kinematic_result",
            "blvar_cf_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceKinematic = SelectKinematicForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSystem);
        ParityTraceRecord managedKinematic = SelectKinematicForSystem(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSystem);

        ParityTraceRecord referenceCf = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "blvar_cf_terms" &&
                                 HasExactDataInt(record, "station", 2) &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence > referenceKinematic.Sequence && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCf = managedRecords
            .Where(static record => record.Kind == "blvar_cf_terms" &&
                                    HasExactDataInt(record, "station", 2) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence > managedKinematic.Sequence && record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "cf", "cfHk", "cfRt", "cfM", "cfU", "cfT", "cfD", "cfMs", "cfRe"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceCf, managedCf, field, "alpha-0 station-4 iteration7 system-row11 owner station2 cf terms");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7Eq1ThetaUpwOwnerSecondaryStation1_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        string[] fields =
        [
            "hc", "hs", "hsHk", "hkD", "hsD", "hsT",
            "us", "usT", "hkU", "rtT", "rtU",
            "cq", "cf", "cfU", "cfT", "cfD", "cfMs",
            "cfmU", "cfmT", "cfmD", "cfmMs", "di", "diT", "de"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceSecondary, managedSecondary, field, "alpha-0 station-4 iteration7 eq1 theta upw owner secondary station1");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7Eq1ThetaUpwOwnerSecondaryStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        string[] fields =
        [
            "hc", "hs", "hsHk", "hkD", "hsD", "hsT",
            "us", "usT", "hkU", "rtT", "rtU",
            "cq", "cf", "cfU", "cfT", "cfD", "cfMs",
            "cfmU", "cfmT", "cfmD", "cfmMs", "di", "diT", "de"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceSecondary, managedSecondary, field, "alpha-0 station-4 iteration7 eq1 theta upw owner secondary station2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow11OwnerKinematicStation1_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "v2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "v2_ms", "alpha-0 station-4 iteration3 system-row11 owner kinematic station1");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow11OwnerKinematicStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "v2", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "v2_ms", "alpha-0 station-4 iteration3 system-row11 owner kinematic station2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerKinematicStation1_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "v2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
        AssertFloatField(referenceKinematic, managedKinematic, "v2_ms", "alpha-0 station-4 iteration6 system-row11 owner kinematic station1");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerKinematicStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "v2", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "v2_ms", "alpha-0 station-4 iteration6 system-row11 owner kinematic station2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow11OwnerKinematicStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "v2", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
        AssertFloatField(referenceKinematic, managedKinematic, "v2_ms", "alpha-0 station-4 iteration7 system-row11 owner kinematic station2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow11OwnerBlkinInputsStation1_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result",
            "blkin_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        ParityTraceRecord referenceInputs = SelectBlkinInputsForKinematic(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blkin_inputs"),
            referenceKinematic);
        ParityTraceRecord managedInputs = SelectBlkinInputsForKinematic(
            managedRecords.Where(static record => record.Kind == "blkin_inputs"),
            managedKinematic);

        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "hstinv", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "hstinv_ms", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "gm1bl", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "rstbl", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "rstbl_ms", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "hvrat", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "reybl", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "reybl_re", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
        AssertFloatField(referenceInputs, managedInputs, "reybl_ms", "alpha-0 station-4 iteration3 system-row11 owner blkin inputs station1");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow11OwnerBlkinInputsStation2_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result",
            "blkin_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        ParityTraceRecord referenceInputs = SelectBlkinInputsForKinematic(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blkin_inputs"),
            referenceKinematic);
        ParityTraceRecord managedInputs = SelectBlkinInputsForKinematic(
            managedRecords.Where(static record => record.Kind == "blkin_inputs"),
            managedKinematic);

        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "hstinv", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "hstinv_ms", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "gm1bl", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "rstbl", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "rstbl_ms", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "hvrat", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "reybl", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "reybl_re", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
        AssertFloatField(referenceInputs, managedInputs, "reybl_ms", "alpha-0 station-4 iteration7 system-row11 owner blkin inputs station2");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow11OwnerTransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        string[] fields =
        [
            "x1", "x2", "xt",
            "x1Original", "t1Original", "d1Original", "u1Original",
            "t1", "t2", "d1", "d2", "u1", "u2"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceInputs, managedInputs, field, "alpha-0 station-4 iteration3 system-row11 owner transition interval inputs");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerTransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        string[] fields =
        [
            "x1", "x2", "xt",
            "x1Original", "t1Original", "d1Original", "u1Original",
            "t1", "t2", "d1", "d2", "u1", "u2"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceInputs, managedInputs, field, "alpha-0 station-4 iteration6 system-row11 owner transition interval inputs");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow11OwnerAcceptedTransitionPointIteration_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_interval_inputs",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceInputs, "x1Original"),
            GetFloatHex(referenceInputs, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedInputs, "x1Original"),
            GetFloatHex(managedInputs, "x2"));

        string[] fields =
        [
            "x1", "x2", "ampl1", "ampl2", "amcrit",
            "ax", "wf2", "xt", "tt", "dt", "ut",
            "residual", "residual_A2", "deltaA2", "relaxation"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceIteration, managedIteration, field, "alpha-0 station-4 iteration3 system-row11 owner accepted transition point iteration");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerAcceptedTransitionPointIteration_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceIteration = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem);
        ParityTraceRecord managedIteration = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem);

        string[] fields =
        [
            "x1", "x2", "wf2", "xt", "tt", "dt", "ut", "ampl1", "ampl2", "deltaA2", "relaxation"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceIteration, managedIteration, field, "alpha-0 station-4 iteration6 system-row11 owner accepted transition point iteration");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow11OwnerAcceptedTransitionPointIteration_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_interval_inputs",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceIntervalCandidate, "x1Original"),
            GetFloatHex(referenceIntervalCandidate, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedIntervalCandidate, "x1Original"),
            GetFloatHex(managedIntervalCandidate, "x2"));

        string[] fields =
        [
            "x1", "x2", "wf2", "xt", "tt", "dt", "ut", "ampl1", "ampl2", "deltaA2", "relaxation"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceIteration, managedIteration, field, "alpha-0 station-4 iteration7 system-row11 owner accepted transition point iteration");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7SystemRow11OwnerTransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "transition_point_iteration");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceIntervalCandidate, "x1Original"),
            GetFloatHex(referenceIntervalCandidate, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedIntervalCandidate, "x1Original"),
            GetFloatHex(managedIntervalCandidate, "x2"));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem,
            referenceIteration);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem,
            managedIteration);

        string[] fields =
        [
            "x1", "x2", "xt",
            "x1Original", "t1Original", "d1Original", "u1Original",
            "t1", "t2", "d1", "d2", "u1", "u2"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceInputs, managedInputs, field, "alpha-0 station-4 iteration7 system-row11 owner transition interval inputs");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow11OwnerTransitionFinalSensitivities_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_final_sensitivities",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinal = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinal = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        string[] fields =
        [
            "t1", "t2", "d1", "d2", "u1", "u2"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceFinal, managedFinal, field, "alpha-0 station-4 iteration6 system-row11 owner transition final sensitivities");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration1SystemRow13OwnerEq1DAccumulation_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 1;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_d_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_d_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_d_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row13BaseTerm", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
        AssertFloatField(referenceTerms, managedTerms, "row13UpwTerm", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
        AssertFloatField(referenceTerms, managedTerms, "row13DeTerm", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
        AssertFloatField(referenceTerms, managedTerms, "row13UsTerm", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
        AssertFloatField(referenceTerms, managedTerms, "row13Transport", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
        AssertFloatField(referenceTerms, managedTerms, "row13CqTerm", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
        AssertFloatField(referenceTerms, managedTerms, "row13CfTerm", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
        AssertFloatField(referenceTerms, managedTerms, "row13HkTerm", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
        AssertFloatField(referenceTerms, managedTerms, "row13", "alpha-0 station-4 iteration1 system-row13 owner eq1 d accumulation");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow13OwnerEq1DTransportTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_d_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_d_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_d_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row13BaseTerm", "alpha-0 station-4 iteration3 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13UpwTerm", "alpha-0 station-4 iteration3 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13DeTerm", "alpha-0 station-4 iteration3 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13UsTerm", "alpha-0 station-4 iteration3 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13Transport", "alpha-0 station-4 iteration3 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13CqTerm", "alpha-0 station-4 iteration3 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13CfTerm", "alpha-0 station-4 iteration3 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13HkTerm", "alpha-0 station-4 iteration3 system-row13 owner eq1 d");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow13OwnerEq1DTransportTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_d_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_d_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_d_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row13BaseTerm", "alpha-0 station-4 iteration8 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13UpwTerm", "alpha-0 station-4 iteration8 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13DeTerm", "alpha-0 station-4 iteration8 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13UsTerm", "alpha-0 station-4 iteration8 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13Transport", "alpha-0 station-4 iteration8 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13CqTerm", "alpha-0 station-4 iteration8 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13CfTerm", "alpha-0 station-4 iteration8 system-row13 owner eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13HkTerm", "alpha-0 station-4 iteration8 system-row13 owner eq1 d");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerEq1DRow23Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_d_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_d_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_d_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row23BaseTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23UpwTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23DeTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23UsTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23Transport", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23CqTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23CfTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23HkTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
        AssertFloatField(referenceTerms, managedTerms, "row23", "alpha-0 station-4 iteration5 system-row13 owner eq1 d row23");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerEq1UqDa_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_uq_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_uq_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_uq_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "uqDa", "alpha-0 station-4 iteration5 system-row13 owner eq1 uq");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerEq1UqHkaBlend_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_uq_terms",
            "bldif_eq1_residual_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceUqTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_uq_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedUqTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_uq_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_residual_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceResidual, managedResidual, "upw", "alpha-0 station-4 iteration5 system-row13 owner eq1 uq hka blend");
        AssertFloatField(referenceResidual, managedResidual, "oneMinusUpw", "alpha-0 station-4 iteration5 system-row13 owner eq1 uq hka blend");
        AssertFloatField(referenceUqTerms, managedUqTerms, "hka", "alpha-0 station-4 iteration5 system-row13 owner eq1 uq hka blend");
        AssertFloatField(referenceUqTerms, managedUqTerms, "uq", "alpha-0 station-4 iteration5 system-row13 owner eq1 uq hka blend");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerEq1ResidualInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_residual_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_residual_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceResidual, managedResidual, "dxi", "alpha-0 station-4 iteration5 system-row13 owner eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "dea", "alpha-0 station-4 iteration5 system-row13 owner eq1 residual");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerEq1ResidualConvection_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_residual_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_residual_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceResidual, managedResidual, "uq", "alpha-0 station-4 iteration5 system-row13 owner eq1 residual convection");
        AssertFloatField(referenceResidual, managedResidual, "ulog", "alpha-0 station-4 iteration5 system-row13 owner eq1 residual convection");
        AssertFloatField(referenceResidual, managedResidual, "eq1Convection", "alpha-0 station-4 iteration5 system-row13 owner eq1 residual convection");
        AssertFloatField(referenceResidual, managedResidual, "eq1Source", "alpha-0 station-4 iteration5 system-row13 owner eq1 residual convection");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerEq1SourceInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_residual_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_residual_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceResidual, managedResidual, "cqa", "alpha-0 station-4 iteration5 system-row13 owner eq1 source inputs");
        AssertFloatField(referenceResidual, managedResidual, "cqaLeftTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 source inputs");
        AssertFloatField(referenceResidual, managedResidual, "cqaRightTerm", "alpha-0 station-4 iteration5 system-row13 owner eq1 source inputs");
        AssertFloatField(referenceResidual, managedResidual, "sa", "alpha-0 station-4 iteration5 system-row13 owner eq1 source inputs");
        AssertFloatField(referenceResidual, managedResidual, "ald", "alpha-0 station-4 iteration5 system-row13 owner eq1 source inputs");
        AssertFloatField(referenceResidual, managedResidual, "eq1Source", "alpha-0 station-4 iteration5 system-row13 owner eq1 source inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerBldifCommonUlog_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_common");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceCommon = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_common" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedCommon = managedRecords
            .Where(static record => record.Kind == "bldif_common" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceCommon, managedCommon, "ulog", "alpha-0 station-4 iteration5 system-row13 owner bldif common ulog");
        AssertFloatField(referenceCommon, managedCommon, "upw", "alpha-0 station-4 iteration5 system-row13 owner bldif common ulog");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerBldifLogInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_log_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceLogInputs = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_log_inputs" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedLogInputs = managedRecords
            .Where(static record => record.Kind == "bldif_log_inputs" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceLogInputs, managedLogInputs, "u1", "alpha-0 station-4 iteration5 system-row13 owner bldif log inputs");
        AssertFloatField(referenceLogInputs, managedLogInputs, "u2", "alpha-0 station-4 iteration5 system-row13 owner bldif log inputs");
        AssertFloatField(referenceLogInputs, managedLogInputs, "x1", "alpha-0 station-4 iteration5 system-row13 owner bldif log inputs");
        AssertFloatField(referenceLogInputs, managedLogInputs, "x2", "alpha-0 station-4 iteration5 system-row13 owner bldif log inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerTransitionIntervalInputsUt_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_inputs")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedInputs = managedRecords
            .Where(static record => record.Kind == "transition_interval_inputs")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 iteration5 system-row13 owner transition interval ut");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration5 system-row13 owner transition interval ut");
        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 iteration5 system-row13 owner transition interval ut");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration5 system-row13 owner transition interval ut");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerAcceptedTransitionPointUt_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "transition_point_iteration");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceInputs, "x1Original"),
            GetFloatHex(referenceInputs, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedInputs, "x1Original"),
            GetFloatHex(managedInputs, "x2"));

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 iteration5 system-row13 owner accepted transition point ut");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 iteration5 system-row13 owner accepted transition point ut");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 iteration5 system-row13 owner accepted transition point ut");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-4 iteration5 system-row13 owner accepted transition point ut");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 iteration5 system-row13 owner accepted transition point ut");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 iteration5 system-row13 owner accepted transition point ut");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerMatchedTransitionIntervalInputsUt_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "transition_point_iteration");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedIntervalCandidate = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectAcceptedTransitionPointIterationForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem,
            GetFloatHex(referenceIntervalCandidate, "x1Original"),
            GetFloatHex(referenceIntervalCandidate, "x2"));
        ParityTraceRecord managedIteration = SelectAcceptedTransitionPointIterationForSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem,
            GetFloatHex(managedIntervalCandidate, "x1Original"),
            GetFloatHex(managedIntervalCandidate, "x2"));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem,
            referenceIteration);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForAcceptedIteration(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem,
            managedIteration);

        AssertHex(
            GetFloatHex(referenceIteration, "ut"),
            GetFloatHex(managedIteration, "ut"),
            "alpha-0 station-4 iteration5 accepted transition point ut");
        AssertHex(
            GetFloatHex(referenceInputs, "u1"),
            GetFloatHex(managedInputs, "u1"),
            "alpha-0 station-4 iteration5 matched transition interval u1");
        Assert.NotEqual(
            GetFloatHex(referenceIteration, "ut"),
            GetFloatHex(referenceInputs, "u1"));
        Assert.NotEqual(
            GetFloatHex(managedIteration, "ut"),
            GetFloatHex(managedInputs, "u1"));

        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 iteration5 system-row13 owner matched transition interval ut");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration5 system-row13 owner matched transition interval ut");
        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 iteration5 system-row13 owner matched transition interval ut");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration5 system-row13 owner matched transition interval ut");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerEq1DRow13Packet_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_d_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_d_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_d_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row13", "alpha-0 station-4 iteration3 system-row22 owner eq1 d row13 packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerEq1XPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_x_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_x_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_x_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "zDxi", "alpha-0 station-4 iteration3 system-row22 owner eq1 x packet");
        AssertFloatField(referenceTerms, managedTerms, "zX1", "alpha-0 station-4 iteration3 system-row22 owner eq1 x packet");
        AssertFloatField(referenceTerms, managedTerms, "zX2", "alpha-0 station-4 iteration3 system-row22 owner eq1 x packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerEq1XPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq1_x_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_x_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_x_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "zDxi", "alpha-0 station-4 iteration8 system-row22 owner eq1 x packet");
        AssertFloatField(referenceTerms, managedTerms, "zX1", "alpha-0 station-4 iteration8 system-row22 owner eq1 x packet");
        AssertFloatField(referenceTerms, managedTerms, "zX2", "alpha-0 station-4 iteration8 system-row22 owner eq1 x packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerTransitionIntervalSensitivityPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "dtT2", "alpha-0 station-4 iteration3 system-row22 owner transition interval sensitivity packet");
        AssertFloatField(referenceInputs, managedInputs, "xtT2", "alpha-0 station-4 iteration3 system-row22 owner transition interval sensitivity packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow22OwnerTransitionIntervalSensitivityPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "dtT2", "alpha-0 station-4 iteration6 system-row22 owner transition interval sensitivity packet");
        AssertFloatField(referenceInputs, managedInputs, "xtT2", "alpha-0 station-4 iteration6 system-row22 owner transition interval sensitivity packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerTransitionIntervalSensitivityPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "dtT2", "alpha-0 station-4 iteration8 system-row22 owner transition interval sensitivity packet");
        AssertFloatField(referenceInputs, managedInputs, "xtT2", "alpha-0 station-4 iteration8 system-row22 owner transition interval sensitivity packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerTransitionFinalSensitivitiesPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "zT2", "alpha-0 station-4 iteration3 system-row22 owner transition final sensitivities packet");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "xtT2", "alpha-0 station-4 iteration3 system-row22 owner transition final sensitivities packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow22OwnerTransitionFinalSensitivitiesPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "zT2", "alpha-0 station-4 iteration6 system-row22 owner transition final sensitivities packet");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "xtT2", "alpha-0 station-4 iteration6 system-row22 owner transition final sensitivities packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerTransitionFinalSensitivitiesAxT2Packet_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "ttCombo", "alpha-0 station-4 iteration3 system-row22 owner transition final sensitivities ax-t2 packet");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "axT2", "alpha-0 station-4 iteration3 system-row22 owner transition final sensitivities ax-t2 packet");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "zT2", "alpha-0 station-4 iteration3 system-row22 owner transition final sensitivities ax-t2 packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow22OwnerTransitionFinalSensitivitiesAxT2Packet_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "ttCombo", "alpha-0 station-4 iteration6 system-row22 owner transition final sensitivities ax-t2 packet");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "axT2", "alpha-0 station-4 iteration6 system-row22 owner transition final sensitivities ax-t2 packet");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "zT2", "alpha-0 station-4 iteration6 system-row22 owner transition final sensitivities ax-t2 packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerFinalTransitionSensitivitiesCoefficients_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_sensitivities",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        ParityTraceRecord referenceCoefficients = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_sensitivities"),
            referenceFinalSensitivities);
        ParityTraceRecord managedCoefficients = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_sensitivities"),
            managedFinalSensitivities);

        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_Hk2", "alpha-0 station-4 iteration3 system-row22 owner final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_T2", "alpha-0 station-4 iteration3 system-row22 owner final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_Rt2", "alpha-0 station-4 iteration3 system-row22 owner final transition sensitivities coefficients");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow22OwnerFinalTransitionSensitivitiesCoefficients_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_sensitivities",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        ParityTraceRecord referenceCoefficients = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_sensitivities"),
            referenceFinalSensitivities);
        ParityTraceRecord managedCoefficients = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_sensitivities"),
            managedFinalSensitivities);

        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_Hk2", "alpha-0 station-4 iteration6 system-row22 owner final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_T2", "alpha-0 station-4 iteration6 system-row22 owner final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_Rt2", "alpha-0 station-4 iteration6 system-row22 owner final transition sensitivities coefficients");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerFinalTransitionSensitivitiesCoefficients_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_sensitivities",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        ParityTraceRecord referenceCoefficients = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_sensitivities"),
            referenceFinalSensitivities);
        ParityTraceRecord managedCoefficients = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_sensitivities"),
            managedFinalSensitivities);

        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_Hk2", "alpha-0 station-4 iteration8 system-row22 owner final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_T2", "alpha-0 station-4 iteration8 system-row22 owner final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_Rt2", "alpha-0 station-4 iteration8 system-row22 owner final transition sensitivities coefficients");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerFinalTransitionKinematicPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "kinematic_result",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        ParityTraceRecord referenceKinematic = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceFinalSensitivities);
        ParityTraceRecord managedKinematic = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedFinalSensitivities);

        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration3 system-row22 owner final transition kinematic packet");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration3 system-row22 owner final transition kinematic packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow22OwnerFinalTransitionKinematicPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "kinematic_result",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        ParityTraceRecord referenceKinematic = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceFinalSensitivities);
        ParityTraceRecord managedKinematic = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedFinalSensitivities);

        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration6 system-row22 owner final transition kinematic packet");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration6 system-row22 owner final transition kinematic packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerFinalTransitionKinematicPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "kinematic_result",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        ParityTraceRecord referenceKinematic = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceFinalSensitivities);
        ParityTraceRecord managedKinematic = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedFinalSensitivities);

        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration8 system-row22 owner final transition kinematic packet");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration8 system-row22 owner final transition kinematic packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerTransitionIntervalStPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_st_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceStTerms = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_st_terms"),
            referenceSystem);
        ParityTraceRecord managedStTerms = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_st_terms"),
            managedSystem);

        AssertFloatField(referenceStTerms, managedStTerms, "stTt", "alpha-0 station-4 iteration3 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stDt", "alpha-0 station-4 iteration3 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stUt", "alpha-0 station-4 iteration3 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "ttT2", "alpha-0 station-4 iteration3 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "dtT2", "alpha-0 station-4 iteration3 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "utT2", "alpha-0 station-4 iteration3 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stT2", "alpha-0 station-4 iteration3 system-row22 owner transition interval st packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration6SystemRow22OwnerTransitionIntervalStPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 6;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_st_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceStTerms = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_st_terms"),
            referenceSystem);
        ParityTraceRecord managedStTerms = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_st_terms"),
            managedSystem);

        AssertFloatField(referenceStTerms, managedStTerms, "stTt", "alpha-0 station-4 iteration6 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stDt", "alpha-0 station-4 iteration6 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stUt", "alpha-0 station-4 iteration6 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "ttT2", "alpha-0 station-4 iteration6 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "dtT2", "alpha-0 station-4 iteration6 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "utT2", "alpha-0 station-4 iteration6 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stT2", "alpha-0 station-4 iteration6 system-row22 owner transition interval st packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerTransitionIntervalStPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_st_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceStTerms = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_st_terms"),
            referenceSystem);
        ParityTraceRecord managedStTerms = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_st_terms"),
            managedSystem);

        AssertFloatField(referenceStTerms, managedStTerms, "stTt", "alpha-0 station-4 iteration8 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stDt", "alpha-0 station-4 iteration8 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stUt", "alpha-0 station-4 iteration8 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "ttT2", "alpha-0 station-4 iteration8 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "dtT2", "alpha-0 station-4 iteration8 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "utT2", "alpha-0 station-4 iteration8 system-row22 owner transition interval st packet");
        AssertFloatField(referenceStTerms, managedStTerms, "stT2", "alpha-0 station-4 iteration8 system-row22 owner transition interval st packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8Eq2ThetaRowTerms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq2_t2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq2_t2_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq2_t2_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "zHaHalf", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zCfm", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zCf2", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zT2", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "h2T2", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cfmT2", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf2T2", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row22Ha", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row22Cfm", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row22Cf", "alpha-0 station-4 iteration8 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row22", "alpha-0 station-4 iteration8 eq2 theta row");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerEq2XPacket_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;
        const string ReferenceCaseId = "n0012_re1e6_a0_p12_n9_eq2_x";

        string referencePath = FortranReferenceCases.GetReferenceTracePath(ReferenceCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            ReferenceCaseId,
            "transition_seed_system",
            "bldif_eq2_x_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq2_x_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq2_x_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "zXl", "alpha-0 station-4 iteration8 system-row22 owner eq2 x packet");
        AssertFloatField(referenceTerms, managedTerms, "zCfx", "alpha-0 station-4 iteration8 system-row22 owner eq2 x packet");
        AssertFloatField(referenceTerms, managedTerms, "zX1", "alpha-0 station-4 iteration8 system-row22 owner eq2 x packet");
        AssertFloatField(referenceTerms, managedTerms, "zX2", "alpha-0 station-4 iteration8 system-row22 owner eq2 x packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerEq2XBreakdown_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;
        const string ReferenceCaseId = "n0012_re1e6_a0_p12_n9_eq2_x_breakdown";

        string referencePath = FortranReferenceCases.GetReferenceTracePath(ReferenceCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            ReferenceCaseId,
            "transition_seed_system",
            "bldif_eq2_x_breakdown");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq2_x_breakdown")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq2_x_breakdown")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "cfxX1", "alpha-0 station-4 iteration8 system-row22 owner eq2 x breakdown");
        AssertFloatField(referenceTerms, managedTerms, "xLogTerm", "alpha-0 station-4 iteration8 system-row22 owner eq2 x breakdown");
        AssertFloatField(referenceTerms, managedTerms, "cfxTerm", "alpha-0 station-4 iteration8 system-row22 owner eq2 x breakdown");
        AssertFloatField(referenceTerms, managedTerms, "zX1", "alpha-0 station-4 iteration8 system-row22 owner eq2 x breakdown");
        AssertFloatField(referenceTerms, managedTerms, "cfxX2", "alpha-0 station-4 iteration8 system-row22 owner eq2 x breakdown");
        AssertFloatField(referenceTerms, managedTerms, "x2LogTerm", "alpha-0 station-4 iteration8 system-row22 owner eq2 x breakdown");
        AssertFloatField(referenceTerms, managedTerms, "cfx2Term", "alpha-0 station-4 iteration8 system-row22 owner eq2 x breakdown");
        AssertFloatField(referenceTerms, managedTerms, "zX2", "alpha-0 station-4 iteration8 system-row22 owner eq2 x breakdown");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerTransitionIntervalBt2Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;
        const string ReferenceCaseId = "n0012_re1e6_a0_p12_n9_trdif_bt2_row22";

        string referencePath = FortranReferenceCases.GetReferenceTracePath(ReferenceCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            ReferenceCaseId,
            "transition_seed_system",
            "transition_interval_bt2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms")
            .Where(record => HasExactDataInt(record, "row", 2))
            .Where(record => HasExactDataInt(record, "column", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "transition_interval_bt2_terms")
            .Where(record => HasExactDataInt(record, "row", 2))
            .Where(record => HasExactDataInt(record, "column", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "baseVs2",
            "stTerm",
            "ttTerm",
            "dtTerm",
            "utTerm",
            "xtTerm",
            "final"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceTerms, managedTerms, field, "alpha-0 station-4 iteration8 system-row22 owner transition interval bt2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow22OwnerTransitionIntervalRows_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;
        const string ReferenceCaseId = "n0012_re1e6_a0_p12_n9_trdif_rows";

        string referencePath = FortranReferenceCases.GetReferenceTracePath(ReferenceCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            ReferenceCaseId,
            "transition_seed_system",
            "transition_interval_rows");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceRows = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_rows"),
            referenceSystem);
        ParityTraceRecord managedRows = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_rows"),
            managedSystem);

        AssertFloatField(referenceRows, managedRows, "laminarVs2_22", "alpha-0 station-4 iteration8 system-row22 owner transition interval rows");
        AssertFloatField(referenceRows, managedRows, "turbulentVs2_22", "alpha-0 station-4 iteration8 system-row22 owner transition interval rows");
        AssertFloatField(referenceRows, managedRows, "finalVs2_22", "alpha-0 station-4 iteration8 system-row22 owner transition interval rows");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow32OwnerEq3T1Packet_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;
        const string ReferenceCaseId = "n0012_re1e6_a0_p12_n9_eq3_row32";

        string referencePath = FortranReferenceCases.GetReferenceTracePath(ReferenceCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            ReferenceCaseId,
            "transition_seed_system",
            "bldif_eq3_t1_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceTerms = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq3_t1_terms" &&
                                 HasExactDataInt(record, "ityp", 2)),
            referenceSystem);
        ParityTraceRecord managedTerms = SelectLastRecordBeforeSystem(
            managedRecords.Where(
                static record => record.Kind == "bldif_eq3_t1_terms" &&
                                 HasExactDataInt(record, "ityp", 2)),
            managedSystem);

        string[] fields =
        [
            "x1",
            "x2",
            "t1",
            "t2",
            "u1",
            "u2",
            "upw",
            "xot1",
            "xot2",
            "cf1",
            "cf2",
            "di1",
            "di2",
            "cf1xot1",
            "cf2xot2",
            "di1xot1",
            "di2xot2",
            "zTermCf1",
            "zTermDi1",
            "zT1Body",
            "zT1Wake",
            "zHs1",
            "hs1T1",
            "zCf1",
            "cf1T1",
            "zDi1",
            "di1T1",
            "baseHs",
            "baseCf",
            "baseDi",
            "baseZT",
            "extraH",
            "zCfx",
            "zDix",
            "cfxUpw",
            "dixUpw",
            "zUpw",
            "upwT",
            "extraUpw",
            "baseStored32",
            "row32"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceTerms, managedTerms, field, "alpha-0 station-4 iteration8 system-row32 owner eq3 t1");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow13OwnerTransitionIntervalBt2Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_bt2_terms",
            "transition_interval_bt2_d_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceBt2 = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_d_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedBt2 = managedRecords
            .Where(static record => record.Kind == "transition_interval_bt2_d_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "baseVs2",
            "stTerm",
            "ttTerm",
            "dtTerm",
            "utTerm",
            "xtTerm",
            "row13"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceBt2, managedBt2, field, "alpha-0 station-4 iteration3 system-row13 owner transition interval bt2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow13OwnerTransitionIntervalBt2Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_bt2_terms",
            "transition_interval_bt2_d_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceBt2 = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_d_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedBt2 = managedRecords
            .Where(static record => record.Kind == "transition_interval_bt2_d_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "baseVs2",
            "stTerm",
            "ttTerm",
            "dtTerm",
            "utTerm",
            "xtTerm",
            "row13"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceBt2, managedBt2, field, "alpha-0 station-4 iteration8 system-row13 owner transition interval bt2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8SystemRow13OwnerTransitionIntervalInputsPacket_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "ttD2", "alpha-0 station-4 iteration8 system-row13 owner transition interval inputs packet");
        AssertFloatField(referenceInputs, managedInputs, "dtD2", "alpha-0 station-4 iteration8 system-row13 owner transition interval inputs packet");
        AssertFloatField(referenceInputs, managedInputs, "utD2", "alpha-0 station-4 iteration8 system-row13 owner transition interval inputs packet");
        AssertFloatField(referenceInputs, managedInputs, "stD2", "alpha-0 station-4 iteration8 system-row13 owner transition interval inputs packet");
        AssertFloatField(referenceInputs, managedInputs, "xtD2", "alpha-0 station-4 iteration8 system-row13 owner transition interval inputs packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration4SystemRow13OwnerTransitionIntervalBt2Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 4;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_bt2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceBt2 = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .Where(record => HasExactDataInt(record, "row", 1))
            .Where(record => HasExactDataInt(record, "column", 3))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedBt2 = managedRecords
            .Where(static record => record.Kind == "transition_interval_bt2_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .Where(record => HasExactDataInt(record, "row", 1))
            .Where(record => HasExactDataInt(record, "column", 3))
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "baseVs2",
            "stTerm",
            "ttTerm",
            "dtTerm",
            "utTerm",
            "xtTerm",
            "final"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceBt2, managedBt2, field, "alpha-0 station-4 iteration4 system-row13 owner transition interval bt2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration5SystemRow13OwnerTransitionIntervalBt2Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 5;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_bt2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceBt2 = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .Where(record => HasExactDataInt(record, "row", 1))
            .Where(record => HasExactDataInt(record, "column", 3))
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedBt2 = managedRecords
            .Where(static record => record.Kind == "transition_interval_bt2_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .Where(record => HasExactDataInt(record, "row", 1))
            .Where(record => HasExactDataInt(record, "column", 3))
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "baseVs2",
            "stTerm",
            "ttTerm",
            "dtTerm",
            "utTerm",
            "xtTerm",
            "final"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceBt2, managedBt2, field, "alpha-0 station-4 iteration5 system-row13 owner transition interval bt2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow22OwnerTransitionIntervalBt2Terms_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_bt2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceBt2 = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "transition_interval_bt2_terms" &&
                                 HasExactDataInt(record, "row", 1) &&
                                 HasExactDataInt(record, "column", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedBt2 = managedRecords
            .Where(static record => record.Kind == "transition_interval_bt2_terms" &&
                                    HasExactDataInt(record, "row", 1) &&
                                    HasExactDataInt(record, "column", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "baseVs2",
            "stTerm",
            "ttTerm",
            "dtTerm",
            "utTerm",
            "xtTerm",
            "final"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceBt2, managedBt2, field, "alpha-0 station-4 iteration3 system-row22 owner transition interval bt2");
        }
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow34OwnerEq3U2Packet_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "bldif_eq3_u2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceEq3 = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq3_u2_terms")
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedEq3 = managedRecords
            .Where(static record => record.Kind == "bldif_eq3_u2_terms")
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceEq3, managedEq3, "zU2", "alpha-0 station-4 iteration3 system-row34 owner eq3 u2 packet");
        AssertFloatField(referenceEq3, managedEq3, "zHcaHalf", "alpha-0 station-4 iteration3 system-row34 owner eq3 u2 packet");
        AssertFloatField(referenceEq3, managedEq3, "baseCf", "alpha-0 station-4 iteration3 system-row34 owner eq3 u2 packet");
        AssertFloatField(referenceEq3, managedEq3, "baseDi", "alpha-0 station-4 iteration3 system-row34 owner eq3 u2 packet");
        AssertFloatField(referenceEq3, managedEq3, "extraUpw", "alpha-0 station-4 iteration3 system-row34 owner eq3 u2 packet");
        AssertFloatField(referenceEq3, managedEq3, "row34", "alpha-0 station-4 iteration3 system-row34 owner eq3 u2 packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow34OwnerTransitionIntervalRows_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_rows");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceRows = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_rows"),
            referenceSystem);
        ParityTraceRecord managedRows = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_rows"),
            managedSystem);

        AssertFloatField(referenceRows, managedRows, "laminarVs2_34", "alpha-0 station-4 iteration3 system-row34 owner transition interval rows");
        AssertFloatField(referenceRows, managedRows, "turbulentVs2_34", "alpha-0 station-4 iteration3 system-row34 owner transition interval rows");
        AssertFloatField(referenceRows, managedRows, "finalVs2_34", "alpha-0 station-4 iteration3 system-row34 owner transition interval rows");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration3SystemRow34OwnerCompressibleVelocityPacket_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string referencePath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha0_p12_blprv_iter3_ref"));
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "laminar_seed_step",
            "compressible_velocity");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceNextSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration + 1));
        ParityTraceRecord managedNextSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration + 1));

        ParityTraceRecord referenceStep = GetOrderedStationRecords(referencePath, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedStep = GetOrderedStationRecords(managedRecords, 4, "laminar_seed_step")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceCompressible = SelectCompressibleForStepHandoff(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "compressible_velocity"),
            referenceStep,
            referenceNextSystem);
        ParityTraceRecord managedCompressible = SelectCompressibleForStepHandoff(
            managedRecords.Where(static record => record.Kind == "compressible_velocity"),
            managedStep,
            managedNextSystem);

        AssertIntField(referenceCompressible, managedCompressible, "ueiBits", "alpha-0 station-4 iteration3 system-row34 owner compressible velocity packet");
        AssertIntField(referenceCompressible, managedCompressible, "u2Bits", "alpha-0 station-4 iteration3 system-row34 owner compressible velocity packet");
        AssertIntField(referenceCompressible, managedCompressible, "u2UeiBits", "alpha-0 station-4 iteration3 system-row34 owner compressible velocity packet");
        AssertIntField(referenceCompressible, managedCompressible, "u2MsBits", "alpha-0 station-4 iteration3 system-row34 owner compressible velocity packet");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_FinalTransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtA1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtT1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtT2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtD1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtD2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtX1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtX2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2A1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2T1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2T2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2D1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2D2", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2X1", "alpha-0 station-4 final transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2X2", "alpha-0 station-4 final transition interval inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration14TransitionIntervalInputs_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 14;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "x1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtA1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtT1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtT2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtD1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtD2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtX1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtX2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2A1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2T1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2T2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2D1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2D2", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2X1", "alpha-0 station-4 iteration14 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2X2", "alpha-0 station-4 iteration14 transition interval inputs");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_AcceptedTransitionKinematic_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_interval_inputs",
            "kinematic_result");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        string referenceUHex = GetFloatHex(referenceInputs, "u1");
        string referenceTHex = GetFloatHex(referenceInputs, "t1");
        string referenceDHex = GetFloatHex(referenceInputs, "d1");
        string managedUHex = GetFloatHex(managedInputs, "u1");
        string managedTHex = GetFloatHex(managedInputs, "t1");
        string managedDHex = GetFloatHex(managedInputs, "d1");

        ParityTraceRecord referenceKinematic = SelectKinematicByPrimaryState(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceUHex,
            referenceTHex,
            referenceDHex);
        ParityTraceRecord managedKinematic = SelectKinematicByPrimaryState(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedUHex,
            managedTHex,
            managedDHex);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "alpha-0 station-4 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "alpha-0 station-4 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "alpha-0 station-4 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "alpha-0 station-4 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "alpha-0 station-4 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "alpha-0 station-4 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "alpha-0 station-4 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "alpha-0 station-4 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "alpha-0 station-4 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "alpha-0 station-4 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "alpha-0 station-4 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "alpha-0 station-4 accepted transition kinematic");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_FinalTransitionSensitivitiesXtX2Owner_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_seed_system",
            "transition_final_sensitivities");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "axA2", "alpha-0 station-4 iteration7 final transition sensitivities xtX2 owner");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "zA2", "alpha-0 station-4 iteration7 final transition sensitivities xtX2 owner");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "zX2", "alpha-0 station-4 iteration7 final transition sensitivities xtX2 owner");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "xtX2", "alpha-0 station-4 iteration7 final transition sensitivities xtX2 owner");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7FinalTransitionSensitivitiesCoefficients_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_sensitivities",
            "transition_final_sensitivities",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        ParityTraceRecord referenceCoefficients = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_sensitivities"),
            referenceFinalSensitivities);
        ParityTraceRecord managedCoefficients = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_sensitivities"),
            managedFinalSensitivities);

        AssertFloatField(referenceCoefficients, managedCoefficients, "ax", "alpha-0 station-4 iteration7 final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_Hk2", "alpha-0 station-4 iteration7 final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_T2", "alpha-0 station-4 iteration7 final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_Rt2", "alpha-0 station-4 iteration7 final transition sensitivities coefficients");
        AssertFloatField(referenceCoefficients, managedCoefficients, "ax_A2", "alpha-0 station-4 iteration7 final transition sensitivities coefficients");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7FinalTransitionKinematic_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "kinematic_result",
            "transition_final_sensitivities",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        ParityTraceRecord referenceKinematic = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceFinalSensitivities);
        ParityTraceRecord managedKinematic = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedFinalSensitivities);

        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "alpha-0 station-4 iteration7 final transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "alpha-0 station-4 iteration7 final transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "alpha-0 station-4 iteration7 final transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "alpha-0 station-4 iteration7 final transition kinematic");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration7FinalTransitionSensitivityCombos_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_final_sensitivities",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceFinalSensitivities = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_final_sensitivities"),
            referenceSystem);
        ParityTraceRecord managedFinalSensitivities = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_final_sensitivities"),
            managedSystem);

        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "ttCombo", "alpha-0 station-4 iteration7 final transition sensitivity combos");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "dtCombo", "alpha-0 station-4 iteration7 final transition sensitivity combos");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "ttA2", "alpha-0 station-4 iteration7 final transition sensitivity combos");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "dtA2", "alpha-0 station-4 iteration7 final transition sensitivity combos");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "axA2TTerm", "alpha-0 station-4 iteration7 final transition sensitivity combos");
        AssertFloatField(referenceFinalSensitivities, managedFinalSensitivities, "axA2DTerm", "alpha-0 station-4 iteration7 final transition sensitivity combos");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_FinalTransitionPointIteration_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        ParityTraceRecord referenceIteration = SelectTransitionPointIterationForInterval(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            GetFloatHex(referenceInputs, "x1Original"),
            GetFloatHex(referenceInputs, "x2"));
        ParityTraceRecord managedIteration = SelectTransitionPointIterationForInterval(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            GetFloatHex(managedInputs, "x1Original"),
            GetFloatHex(managedInputs, "x2"));

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl1", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "amcrit", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ax", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "wf2", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual_A2", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "alpha-0 station-4 final transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "relaxation", "alpha-0 station-4 final transition point iteration");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_AcceptedTransitionPointIteration_BeforeFinalSystem_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 7;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceIteration = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem);
        ParityTraceRecord managedIteration = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem);

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl1", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "amcrit", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ax", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "wf2", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual_A2", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "alpha-0 station-4 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "relaxation", "alpha-0 station-4 accepted transition point iteration");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration8AcceptedTransitionPointIteration_BeforeFinalSystem_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 8;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceIteration = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem);
        ParityTraceRecord managedIteration = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem);

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl1", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "amcrit", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ax", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "wf2", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual_A2", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "alpha-0 station-4 iteration8 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "relaxation", "alpha-0 station-4 iteration8 accepted transition point iteration");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_Iteration14AcceptedTransitionPointIteration_BeforeFinalSystem_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 14;

        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "transition_point_iteration",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 4, "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord referenceIteration = SelectLastRecordBeforeSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            referenceSystem);
        ParityTraceRecord managedIteration = SelectLastRecordBeforeSystem(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration"),
            managedSystem);

        AssertFloatField(referenceIteration, managedIteration, "x1", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl1", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "amcrit", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ax", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "wf2", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual_A2", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "alpha-0 station-4 iteration14 accepted transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "relaxation", "alpha-0 station-4 iteration14 accepted transition point iteration");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_PredictedEdgeVelocity_FromFullTrace_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        PredictedEdgeVelocityBlock referenceBlock = SelectFirstPredictedEdgeVelocityBlock(
            ParityTraceLoader.ReadMatching(
                referencePath,
                static record => (record.Kind == "predicted_edge_velocity_term" || record.Kind == "predicted_edge_velocity") &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 4)),
            side: 1,
            station: 4);

        Assert.NotEmpty(referenceBlock.Terms);

        ManagedPredictedEdgeVelocityTraceResult managed = RunManagedPredictedEdgeVelocityTrace(Alpha0FullCaseId, station: 4);
        ParityTraceRecord referenceSelfTerm = referenceBlock.Terms.Single(
            static record => HasExactDataInt(record, "sourceSide", 1) &&
                             HasExactDataInt(record, "sourceStation", 4));
        AssertHex(
            GetFloatHex(referenceSelfTerm, "mass"),
            managed.ContextStationMassHex,
            "managed alpha-0 context station-4 mass");

        Assert.Equal(referenceBlock.Terms.Count, managed.Terms.Count);
        for (int index = 0; index < referenceBlock.Terms.Count; index++)
        {
            AssertTermParity(referenceBlock.Terms[index], managed.Terms[index], index);
        }

        AssertFinalParity(referenceBlock.Final, managed.Final);
        AssertHex(
            GetFloatHex(referenceBlock.Final, "predicted"),
            ToHex((float)managed.Usav[3, 0]),
            "returned alpha-0 usav upper station-4");
    }

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceUpperStation2_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 1, 2, "alpha-0 upper station-4 source upper station-2");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceUpperStation3_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 1, 3, "alpha-0 upper station-4 source upper station-3");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceUpperStation4_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 1, 4, "alpha-0 upper station-4 source upper station-4");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceUpperStation5_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 1, 5, "alpha-0 upper station-4 source upper station-5");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceUpperStation6_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 1, 6, "alpha-0 upper station-4 source upper station-6");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceUpperStation7_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 1, 7, "alpha-0 upper station-4 source upper station-7");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation2_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 2, "alpha-0 upper station-4 source lower station-2");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation3_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 3, "alpha-0 upper station-4 source lower station-3");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation4_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 4, "alpha-0 upper station-4 source lower station-4");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation5_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 5, "alpha-0 upper station-4 source lower station-5");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation6_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 6, "alpha-0 upper station-4 source lower station-6");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation7_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 7, "alpha-0 upper station-4 source lower station-7");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation8_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 8, "alpha-0 upper station-4 source lower station-8");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation9_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 9, "alpha-0 upper station-4 source lower station-9");

    [Fact]
    public void Alpha0_P12_UpperStation4_SourceLowerStation10_PredictedEdgeVelocityTerm_FromFullTrace_BitwiseMatchesFortranTrace()
        => AssertPredictedEdgeVelocityContributorParity(Alpha0FullCaseId, 1, 4, 2, 10, "alpha-0 upper station-4 source lower station-10");

    [Fact]
    public void Alpha0_P12_UpperStation4_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-station4");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 4));
        AssertFloatField(referenceIteration, managedIteration, "uei", "alpha-0 station-4 legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "theta", "alpha-0 station-4 legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "dstar", "alpha-0 station-4 legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "ctau", "alpha-0 station-4 legacy remarch iteration");

        AssertFloatField(referenceSystem, managedSystem, "row22", "alpha-0 station-4 legacy remarch system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "alpha-0 station-4 legacy remarch system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "alpha-0 station-4 legacy remarch system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "alpha-0 station-4 legacy remarch system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "alpha-0 station-4 legacy remarch system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "alpha-0 station-4 legacy remarch system");
        AssertFloatField(referenceSystem, managedSystem, "rhs2", "alpha-0 station-4 legacy remarch system");
        AssertFloatField(referenceSystem, managedSystem, "rhs3", "alpha-0 station-4 legacy remarch system");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 station-4 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 station-4 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[3, 0]),
            "returned alpha-0 remarch context station-4 mass");
    }

    [Fact]
    public void Alpha0_P12_UpperStation5_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-station5");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 5));

        Console.WriteLine(
            $"alpha0-st5 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-st5 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-st5 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-st5 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-st5 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-st5 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-st5 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-st5 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 station-5 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 station-5 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[4, 0]),
            "returned alpha-0 remarch context station-5 mass");
    }

    [Fact]
    public void Alpha0_P12_UpperStation6_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-station6");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 6));

        Console.WriteLine(
            $"alpha0-st6 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-st6 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-st6 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-st6 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-st6 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-st6 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-st6 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-st6 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 station-6 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 station-6 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[5, 0]),
            "returned alpha-0 remarch context station-6 mass");
    }

    [Fact]
    public void Alpha0_P12_UpperStation7_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-station7");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 7));

        Console.WriteLine(
            $"alpha0-st7 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-st7 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-st7 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-st7 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-st7 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-st7 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-st7 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-st7 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 station-7 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 station-7 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[6, 0]),
            "returned alpha-0 remarch context station-7 mass");
    }

    [Fact]
    public void Alpha0_P12_LowerStation4_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-lower-station4");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 4))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 2) &&
                             HasExactDataInt(record, "station", 4));

        Console.WriteLine(
            $"alpha0-lower-st4 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st4 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st4 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st4 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st4 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st4 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st4 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-lower-st4 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 lower station-4 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 lower station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 lower station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 lower station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 lower station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 lower station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 lower station-4 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 lower station-4 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[3, 1]),
            "returned alpha-0 lower remarch context station-4 mass");
    }

    [Fact]
    public void Alpha0_P12_LowerStation5_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-lower-station5");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 5))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 2) &&
                             HasExactDataInt(record, "station", 5));

        Console.WriteLine(
            $"alpha0-lower-st5 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st5 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st5 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st5 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st5 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st5 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st5 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-lower-st5 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 lower station-5 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 lower station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 lower station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 lower station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 lower station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 lower station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 lower station-5 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 lower station-5 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[4, 1]),
            "returned alpha-0 lower remarch context station-5 mass");
    }

    [Fact]
    public void Alpha0_P12_LowerStation6_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-lower-station6");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 2) &&
                             HasExactDataInt(record, "station", 6));

        Console.WriteLine(
            $"alpha0-lower-st6 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st6 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st6 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st6 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st6 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st6 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st6 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-lower-st6 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 lower station-6 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 lower station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 lower station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 lower station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 lower station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 lower station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 lower station-6 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 lower station-6 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[5, 1]),
            "returned alpha-0 lower remarch context station-6 mass");
    }

    [Fact]
    public void Alpha0_P12_LowerStation7_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-lower-station7");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 2) &&
                             HasExactDataInt(record, "station", 7));

        Console.WriteLine(
            $"alpha0-lower-st7 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st7 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st7 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st7 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st7 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st7 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st7 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-lower-st7 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 lower station-7 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 lower station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 lower station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 lower station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 lower station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 lower station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 lower station-7 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 lower station-7 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[6, 1]),
            "returned alpha-0 lower remarch context station-7 mass");
    }

    [Fact]
    public void Alpha0_P12_LowerStation8_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-lower-station8");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 2) &&
                             HasExactDataInt(record, "station", 8));

        Console.WriteLine(
            $"alpha0-lower-st8 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st8 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st8 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st8 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st8 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st8 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st8 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-lower-st8 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 lower station-8 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 lower station-8 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 lower station-8 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 lower station-8 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 lower station-8 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 lower station-8 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 lower station-8 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 lower station-8 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[7, 1]),
            "returned alpha-0 lower remarch context station-8 mass");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_SystemWitness_LowerStation8_BitwiseMatchesFortranTrace()
    {
        var (referenceRecords, managedRecords) = GetLegacyDirectSeedCarryFocusedRecords();

        ParityTraceRecord referenceSystem = referenceRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "alpha-0 legacy direct-seed carry system lower station8 witness");
        AssertFloatField(referenceSystem, managedSystem, "theta", "alpha-0 legacy direct-seed carry system lower station8 witness");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "alpha-0 legacy direct-seed carry system lower station8 witness");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "alpha-0 legacy direct-seed carry system lower station8 witness");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "alpha-0 legacy direct-seed carry system lower station8 witness");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "alpha-0 legacy direct-seed carry system lower station8 witness");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "alpha-0 legacy direct-seed carry system lower station8 witness");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "alpha-0 legacy direct-seed carry system lower station8 witness");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "alpha-0 legacy direct-seed carry system lower station8 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_IntervalInputsWitness_LowerStation8_BitwiseMatchesFortranTrace()
    {
        var (referenceRecords, managedRecords) = GetLegacyDirectSeedCarryFocusedRecords();

        ParityTraceRecord referenceSystem = referenceRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceInputs = SelectLastRecordBefore(
            referenceRecords.Where(
                static record => record.Kind == "blsys_interval_inputs" &&
                                 HasExactDataInt(record, "ityp", 2)),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectLastRecordBefore(
            managedRecords.Where(
                static record => record.Kind == "blsys_interval_inputs" &&
                                 HasExactDataInt(record, "ityp", 2)),
            managedSystem);

        AssertRecordDataParity(
            referenceInputs,
            managedInputs,
            "alpha-0 legacy direct-seed carry interval inputs lower station8 witness",
            "phase");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_PrimaryStation2Witness_LowerStation8_BitwiseMatchesFortranTrace()
    {
        var (referenceRecords, managedRecords) = GetLegacyDirectSeedCarryFocusedRecords();

        ParityTraceRecord referenceSystem = referenceRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referencePrimary = SelectLastRecordBefore(
            referenceRecords.Where(
                static record => record.Kind == "bldif_primary_station" &&
                                 HasExactDataInt(record, "ityp", 2) &&
                                 HasExactDataInt(record, "station", 2)),
            referenceSystem);
        ParityTraceRecord managedPrimary = SelectLastRecordBefore(
            managedRecords.Where(
                static record => record.Kind == "bldif_primary_station" &&
                                 HasExactDataInt(record, "ityp", 2) &&
                                 HasExactDataInt(record, "station", 2)),
            managedSystem);

        AssertRecordDataParity(referencePrimary, managedPrimary, "alpha-0 legacy direct-seed carry primary station2 lower station8 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_SecondaryStation2Witness_LowerStation8_BitwiseMatchesFortranTrace()
    {
        var (referenceRecords, managedRecords) = GetLegacyDirectSeedCarryFocusedRecords();

        ParityTraceRecord referenceSystem = referenceRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceSecondary = SelectLastRecordBefore(
            referenceRecords.Where(
                static record => record.Kind == "bldif_secondary_station" &&
                                 HasExactDataInt(record, "ityp", 2) &&
                                 HasExactDataInt(record, "station", 2)),
            referenceSystem);
        ParityTraceRecord managedSecondary = SelectLastRecordBefore(
            managedRecords.Where(
                static record => record.Kind == "bldif_secondary_station" &&
                                 HasExactDataInt(record, "ityp", 2) &&
                                 HasExactDataInt(record, "station", 2)),
            managedSystem);

        AssertRecordDataParity(referenceSecondary, managedSecondary, "alpha-0 legacy direct-seed carry secondary station2 lower station8 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_Eq3D2TermsWitness_LowerStation8_BitwiseMatchesFortranTrace()
    {
        var (referenceRecords, managedRecords) = GetLegacyDirectSeedCarryFocusedRecords();

        ParityTraceRecord referenceSystem = referenceRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceTerms = SelectLastRecordBefore(
            referenceRecords.Where(
                static record => record.Kind == "bldif_eq3_d2_terms" &&
                                 HasExactDataInt(record, "ityp", 2)),
            referenceSystem);
        ParityTraceRecord managedTerms = SelectLastRecordBefore(
            managedRecords.Where(
                static record => record.Kind == "bldif_eq3_d2_terms" &&
                                 HasExactDataInt(record, "ityp", 2)),
            managedSystem);

        AssertRecordDataParity(referenceTerms, managedTerms, "alpha-0 legacy direct-seed carry eq3 d2 terms lower station8 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_ResidualWitness_LowerStation8_BitwiseMatchesFortranTrace()
    {
        var (referenceRecords, managedRecords) = GetLegacyDirectSeedCarryFocusedRecords();

        ParityTraceRecord referenceSystem = referenceRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceResidual = SelectLastRecordBefore(
            referenceRecords.Where(
                static record => record.Kind == "bldif_residual" &&
                                 HasExactDataInt(record, "ityp", 2)),
            referenceSystem);
        ParityTraceRecord managedResidual = SelectLastRecordBefore(
            managedRecords.Where(
                static record => record.Kind == "bldif_residual" &&
                                 HasExactDataInt(record, "ityp", 2)),
            managedSystem);

        AssertRecordDataParity(
            referenceResidual,
            managedResidual,
            "alpha-0 legacy direct-seed carry residual lower station8 witness",
            "phase");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_FinalWitness_LowerStation8_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 8))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 legacy direct-seed carry final lower station8 witness");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 legacy direct-seed carry final lower station8 witness");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 legacy direct-seed carry final lower station8 witness");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 legacy direct-seed carry final lower station8 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_LastStepWitness_LowerStation9_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_step",
            "laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceStep = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_step" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 9))
            .Where(record => record.Sequence < referenceFinal.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedStep = managedRecords
            .Where(static record => record.Kind == "laminar_seed_step" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 9))
            .Where(record => record.Sequence < managedFinal.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceStep, managedStep, "uei", "alpha-0 legacy direct-seed carry last-step lower station9 witness");
        AssertFloatField(referenceStep, managedStep, "theta", "alpha-0 legacy direct-seed carry last-step lower station9 witness");
        AssertFloatField(referenceStep, managedStep, "dstar", "alpha-0 legacy direct-seed carry last-step lower station9 witness");
        AssertFloatField(referenceStep, managedStep, "ampl", "alpha-0 legacy direct-seed carry last-step lower station9 witness");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "alpha-0 legacy direct-seed carry last-step lower station9 witness");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "alpha-0 legacy direct-seed carry last-step lower station9 witness");
        AssertFloatField(referenceStep, managedStep, "dmax", "alpha-0 legacy direct-seed carry last-step lower station9 witness");
        AssertFloatField(referenceStep, managedStep, "rlx", "alpha-0 legacy direct-seed carry last-step lower station9 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyDirectSeedCarry_FinalWitness_LowerStation9_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTraceForCase(
            Alpha0FullCaseId,
            "laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 legacy direct-seed carry final lower station9 witness");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 legacy direct-seed carry final lower station9 witness");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 legacy direct-seed carry final lower station9 witness");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 legacy direct-seed carry final lower station9 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyRemarchPreConstraintSystemPackets_PrimaryStation2Witness_BitwiseMatchesFortranTrace()
    {
        AssertLegacyRemarchPreConstraintPrimaryStationParity(
            station: 2,
            context: "alpha-0 legacy remarch pre-constraint system packets primary station2 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyRemarchPreConstraintSystemPackets_Eq3ThetaTermsWitness_BitwiseMatchesFortranTrace()
    {
        AssertLegacyRemarchPreConstraintRecordParity(
            "bldif_eq3_t2_terms",
            "alpha-0 legacy remarch pre-constraint system packets eq3 theta witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyRemarchPreConstraintSystemPackets_Eq3D1TermsWitness_BitwiseMatchesFortranTrace()
    {
        AssertLegacyRemarchPreConstraintRecordParity(
            "bldif_eq3_d1_terms",
            "alpha-0 legacy remarch pre-constraint system packets eq3 d1 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyRemarchPreConstraintSystemPackets_Eq3D2TermsWitness_BitwiseMatchesFortranTrace()
    {
        AssertLegacyRemarchPreConstraintRecordParity(
            "bldif_eq3_d2_terms",
            "alpha-0 legacy remarch pre-constraint system packets eq3 d2 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyRemarchPreConstraintSystemPackets_Eq3U2TermsWitness_BitwiseMatchesFortranTrace()
    {
        AssertLegacyRemarchPreConstraintRecordParity(
            "bldif_eq3_u2_terms",
            "alpha-0 legacy remarch pre-constraint system packets eq3 u2 witness");
    }

    [Fact]
    public void Alpha0_P12_LegacyRemarchPreConstraintSystemPackets_ResidualWitness_BitwiseMatchesFortranTrace()
    {
        AssertLegacyRemarchPreConstraintRecordParity(
            "bldif_residual",
            "alpha-0 legacy remarch pre-constraint system packets residual witness");
    }

    [Fact]
    public void Alpha0_P12_LowerStation9_LegacyRemarchConstraint_BitwiseMatchesFortranTrace()
    {
        LegacyRemarchStationBlock reference = LoadReferenceLegacyRemarchStationBlock(Alpha0FullCaseId, side: 2, station: 9);
        ManagedLegacyRemarchStationTraceResult managed = RunManagedLegacyRemarchStationTrace(
            Alpha0FullCaseId,
            side: 2,
            station: 9,
            sessionName: "legacy-remarch-alpha0-lower-station9-constraint");

        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch constraint ref currentU2={GetFloatHex(reference.Constraint, "currentU2")} currentU2Uei={GetFloatHex(reference.Constraint, "currentU2Uei")} hk2={GetFloatHex(reference.Constraint, "hk2")} hkref={GetFloatHex(reference.Constraint, "hkref")} ueref={GetFloatHex(reference.Constraint, "ueref")} sens={GetFloatHex(reference.Constraint, "sens")} senNew={GetFloatHex(reference.Constraint, "senNew")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch constraint man currentU2={GetFloatHex(managed.Block.Constraint, "currentU2")} currentU2Uei={GetFloatHex(managed.Block.Constraint, "currentU2Uei")} hk2={GetFloatHex(managed.Block.Constraint, "hk2")} hkref={GetFloatHex(managed.Block.Constraint, "hkref")} ueref={GetFloatHex(managed.Block.Constraint, "ueref")} sens={GetFloatHex(managed.Block.Constraint, "sens")} senNew={GetFloatHex(managed.Block.Constraint, "senNew")}");

        AssertIntField(reference.Constraint, managed.Block.Constraint, "side", "alpha-0 lower station-9 legacy remarch constraint");
        AssertIntField(reference.Constraint, managed.Block.Constraint, "station", "alpha-0 lower station-9 legacy remarch constraint");
        AssertIntField(reference.Constraint, managed.Block.Constraint, "iteration", "alpha-0 lower station-9 legacy remarch constraint");
        AssertStringField(reference.Constraint, managed.Block.Constraint, "mode", "alpha-0 lower station-9 legacy remarch constraint");
        AssertFloatField(reference.Constraint, managed.Block.Constraint, "currentU2", "alpha-0 lower station-9 legacy remarch constraint");
        AssertFloatField(reference.Constraint, managed.Block.Constraint, "currentU2Uei", "alpha-0 lower station-9 legacy remarch constraint");
        AssertFloatField(reference.Constraint, managed.Block.Constraint, "hk2", "alpha-0 lower station-9 legacy remarch constraint");
        AssertFloatField(reference.Constraint, managed.Block.Constraint, "hkref", "alpha-0 lower station-9 legacy remarch constraint");
        AssertFloatField(reference.Constraint, managed.Block.Constraint, "ueref", "alpha-0 lower station-9 legacy remarch constraint");
        AssertFloatField(reference.Constraint, managed.Block.Constraint, "sens", "alpha-0 lower station-9 legacy remarch constraint");
        AssertFloatField(reference.Constraint, managed.Block.Constraint, "senNew", "alpha-0 lower station-9 legacy remarch constraint");
    }

    [Fact]
    public void Alpha0_P12_LowerStation9_LegacyRemarchIteration_BitwiseMatchesFortranTrace()
    {
        LegacyRemarchStationBlock reference = LoadReferenceLegacyRemarchStationBlock(Alpha0FullCaseId, side: 2, station: 9);
        ManagedLegacyRemarchStationTraceResult managed = RunManagedLegacyRemarchStationTrace(
            Alpha0FullCaseId,
            side: 2,
            station: 9,
            sessionName: "legacy-remarch-alpha0-lower-station9-iteration");

        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch iter ref uei={GetFloatHex(reference.Iteration, "uei")} theta={GetFloatHex(reference.Iteration, "theta")} dstar={GetFloatHex(reference.Iteration, "dstar")} ctau={GetFloatHex(reference.Iteration, "ctau")} dmax={GetFloatHex(reference.Iteration, "dmax")} residualNorm={GetFloatHex(reference.Iteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch iter man uei={GetFloatHex(managed.Block.Iteration, "uei")} theta={GetFloatHex(managed.Block.Iteration, "theta")} dstar={GetFloatHex(managed.Block.Iteration, "dstar")} ctau={GetFloatHex(managed.Block.Iteration, "ctau")} dmax={GetFloatHex(managed.Block.Iteration, "dmax")} residualNorm={GetFloatHex(managed.Block.Iteration, "residualNorm")}");

        AssertIntField(reference.Iteration, managed.Block.Iteration, "side", "alpha-0 lower station-9 legacy remarch iteration");
        AssertIntField(reference.Iteration, managed.Block.Iteration, "station", "alpha-0 lower station-9 legacy remarch iteration");
        AssertFloatField(reference.Iteration, managed.Block.Iteration, "uei", "alpha-0 lower station-9 legacy remarch iteration");
        AssertFloatField(reference.Iteration, managed.Block.Iteration, "theta", "alpha-0 lower station-9 legacy remarch iteration");
        AssertFloatField(reference.Iteration, managed.Block.Iteration, "dstar", "alpha-0 lower station-9 legacy remarch iteration");
        AssertFloatField(reference.Iteration, managed.Block.Iteration, "ctau", "alpha-0 lower station-9 legacy remarch iteration");
        AssertFloatField(reference.Iteration, managed.Block.Iteration, "dmax", "alpha-0 lower station-9 legacy remarch iteration");
        AssertFloatField(reference.Iteration, managed.Block.Iteration, "residualNorm", "alpha-0 lower station-9 legacy remarch iteration");
    }

    [Fact]
    public void Alpha0_P12_LowerStation9_LegacyRemarchFinalSystem_BitwiseMatchesFortranTrace()
    {
        LegacyRemarchStationBlock reference = LoadReferenceLegacyRemarchStationBlock(Alpha0FullCaseId, side: 2, station: 9);
        ManagedLegacyRemarchStationTraceResult managed = RunManagedLegacyRemarchStationTrace(
            Alpha0FullCaseId,
            side: 2,
            station: 9,
            sessionName: "legacy-remarch-alpha0-lower-station9-system");

        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch system ref row22={GetFloatHex(reference.System, "row22")} row23={GetFloatHex(reference.System, "row23")} row24={GetFloatHex(reference.System, "row24")} row32={GetFloatHex(reference.System, "row32")} row33={GetFloatHex(reference.System, "row33")} row34={GetFloatHex(reference.System, "row34")} rhs2={GetFloatHex(reference.System, "rhs2")} rhs3={GetFloatHex(reference.System, "rhs3")} rhs4={GetFloatHex(reference.System, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch system man row22={GetFloatHex(managed.Block.System, "row22")} row23={GetFloatHex(managed.Block.System, "row23")} row24={GetFloatHex(managed.Block.System, "row24")} row32={GetFloatHex(managed.Block.System, "row32")} row33={GetFloatHex(managed.Block.System, "row33")} row34={GetFloatHex(managed.Block.System, "row34")} rhs2={GetFloatHex(managed.Block.System, "rhs2")} rhs3={GetFloatHex(managed.Block.System, "rhs3")} rhs4={GetFloatHex(managed.Block.System, "rhs4")}");

        AssertIntField(reference.System, managed.Block.System, "side", "alpha-0 lower station-9 legacy remarch final system");
        AssertIntField(reference.System, managed.Block.System, "station", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "row22", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "row23", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "row24", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "row32", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "row33", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "row34", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "rhs2", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "rhs3", "alpha-0 lower station-9 legacy remarch final system");
        AssertFloatField(reference.System, managed.Block.System, "rhs4", "alpha-0 lower station-9 legacy remarch final system");
    }

    [Fact]
    public void Alpha0_P12_LowerStation9_LegacyRemarchFinalOnly_BitwiseMatchesFortranTrace()
    {
        LegacyRemarchStationBlock reference = LoadReferenceLegacyRemarchStationBlock(Alpha0FullCaseId, side: 2, station: 9);
        ManagedLegacyRemarchStationTraceResult managed = RunManagedLegacyRemarchStationTrace(
            Alpha0FullCaseId,
            side: 2,
            station: 9,
            sessionName: "legacy-remarch-alpha0-lower-station9-final");

        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch final ref uei={GetFloatHex(reference.Final, "uei")} theta={GetFloatHex(reference.Final, "theta")} dstar={GetFloatHex(reference.Final, "dstar")} ctau={GetFloatHex(reference.Final, "ctau")} mass={GetFloatHex(reference.Final, "mass")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch final man uei={GetFloatHex(managed.Block.Final, "uei")} theta={GetFloatHex(managed.Block.Final, "theta")} dstar={GetFloatHex(managed.Block.Final, "dstar")} ctau={GetFloatHex(managed.Block.Final, "ctau")} mass={GetFloatHex(managed.Block.Final, "mass")}");

        AssertIntField(reference.Final, managed.Block.Final, "side", "alpha-0 lower station-9 legacy remarch final");
        AssertIntField(reference.Final, managed.Block.Final, "station", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(reference.Final, managed.Block.Final, "uei", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(reference.Final, managed.Block.Final, "theta", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(reference.Final, managed.Block.Final, "dstar", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(reference.Final, managed.Block.Final, "ctau", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(reference.Final, managed.Block.Final, "ampl", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(reference.Final, managed.Block.Final, "mass", "alpha-0 lower station-9 legacy remarch final");
        AssertHex(
            GetFloatHex(reference.Final, "mass"),
            managed.ContextStationMassHex,
            "returned alpha-0 lower remarch context station-9 mass");
    }

    [Fact]
    public void Alpha0_P12_LowerStation9_LegacyRemarchFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_system" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceDelta = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final_delta" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 2) &&
                                 HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceArtifacts trace = RunManagedTraceCore(Alpha0FullCaseId, "legacy-remarch-alpha0-lower-station9");
        ParityTraceRecord managedIteration = trace.Records
            .Where(static record => record.Kind == "legacy_seed_iteration" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_system" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedDelta = trace.Records
            .Where(static record => record.Kind == "legacy_seed_final_delta" &&
                                    HasExactDataInt(record, "side", 2) &&
                                    HasExactDataInt(record, "station", 9))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 2) &&
                             HasExactDataInt(record, "station", 9));

        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch iter ref uei={GetFloatHex(referenceIteration, "uei")} theta={GetFloatHex(referenceIteration, "theta")} dstar={GetFloatHex(referenceIteration, "dstar")} ctau={GetFloatHex(referenceIteration, "ctau")} dmax={GetFloatHex(referenceIteration, "dmax")} residualNorm={GetFloatHex(referenceIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch iter man uei={GetFloatHex(managedIteration, "uei")} theta={GetFloatHex(managedIteration, "theta")} dstar={GetFloatHex(managedIteration, "dstar")} ctau={GetFloatHex(managedIteration, "ctau")} dmax={GetFloatHex(managedIteration, "dmax")} residualNorm={GetFloatHex(managedIteration, "residualNorm")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch system ref row22={GetFloatHex(referenceSystem, "row22")} row23={GetFloatHex(referenceSystem, "row23")} row24={GetFloatHex(referenceSystem, "row24")} row32={GetFloatHex(referenceSystem, "row32")} row33={GetFloatHex(referenceSystem, "row33")} row34={GetFloatHex(referenceSystem, "row34")} rhs2={GetFloatHex(referenceSystem, "rhs2")} rhs3={GetFloatHex(referenceSystem, "rhs3")} rhs4={GetFloatHex(referenceSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch system man row22={GetFloatHex(managedSystem, "row22")} row23={GetFloatHex(managedSystem, "row23")} row24={GetFloatHex(managedSystem, "row24")} row32={GetFloatHex(managedSystem, "row32")} row33={GetFloatHex(managedSystem, "row33")} row34={GetFloatHex(managedSystem, "row34")} rhs2={GetFloatHex(managedSystem, "rhs2")} rhs3={GetFloatHex(managedSystem, "rhs3")} rhs4={GetFloatHex(managedSystem, "rhs4")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch delta ref d2={GetFloatHex(referenceDelta, "delta2")} d3={GetFloatHex(referenceDelta, "delta3")} d4={GetFloatHex(referenceDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch delta man d2={GetFloatHex(managedDelta, "delta2")} d3={GetFloatHex(managedDelta, "delta3")} d4={GetFloatHex(managedDelta, "delta4")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch final ref uei={GetFloatHex(referenceFinal, "uei")} theta={GetFloatHex(referenceFinal, "theta")} dstar={GetFloatHex(referenceFinal, "dstar")} ctau={GetFloatHex(referenceFinal, "ctau")} mass={GetFloatHex(referenceFinal, "mass")}");
        Console.WriteLine(
            $"alpha0-lower-st9 legacy remarch final man uei={GetFloatHex(managedFinal, "uei")} theta={GetFloatHex(managedFinal, "theta")} dstar={GetFloatHex(managedFinal, "dstar")} ctau={GetFloatHex(managedFinal, "ctau")} mass={GetFloatHex(managedFinal, "mass")}");

        AssertIntField(referenceFinal, managedFinal, "side", "alpha-0 lower station-9 legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "alpha-0 lower station-9 legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "alpha-0 lower station-9 legacy remarch final");
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[8, 1]),
            "returned alpha-0 lower remarch context station-9 mass");
    }

    [Fact]
    public void Alpha10_P80_PreNewtonPreparation_UpperStation2SeedFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(SimilarityReferenceDirectory);
        ParityTraceRecord referenceFinal = ParityTraceLoader.FindSingle(
            referencePath,
            static record => record.Kind == "laminar_seed_final" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2),
            "pre-Newton upper station-2 seed final");

        ParityTraceRecord managedFinal = RunManagedPreparationTrace("laminar_seed_final").Single(
            static record => record.Kind == "laminar_seed_final" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2));

        AssertIntField(referenceFinal, managedFinal, "side", "seed final");
        AssertIntField(referenceFinal, managedFinal, "station", "seed final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "seed final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "seed final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "seed final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "seed final");

        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        AssertHex(
            GetFloatHex(referenceFinal, "mass"),
            ToHex((float)context.BoundaryLayerState.MASS[1, 0]),
            "returned pre-newton context station-2 mass");
    }

    [Fact]
    public void Alpha10_P80_LegacyRemarch_UpperStation16Final_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(RemarchReferenceDirectory);
        string iterationReferencePath = GetLatestTracePath(RemarchIterationReferenceDirectory);
        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "legacy_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        ManagedTraceResult managed = RunManagedTrace();
        ParityTraceRecord referenceIteration = ParityTraceLoader.ReadMatching(
                iterationReferencePath,
                static record => record.Kind == "legacy_seed_iteration" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ViscousSolverEngine.PreNewtonSetupContext preRemarchContext = BuildContext();
        AssertHex(
            GetFloatHex(referenceIteration, "dstar"),
            ToHex((float)preRemarchContext.BoundaryLayerState.DSTR[15, 0]),
            "pre-remarch context station-16 dstar");
        ParityTraceRecord managedIteration = managed.LegacyRemarchStation16Iteration;
        ParityTraceRecord managedFinal = managed.LegacyRemarchStation16Final;

        AssertIntField(referenceIteration, managedIteration, "side", "legacy remarch iteration");
        AssertIntField(referenceIteration, managedIteration, "station", "legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "uei", "legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "theta", "legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "dstar", "legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "ctau", "legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl", "legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "dmax", "legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "rlx", "legacy remarch iteration");
        AssertFloatField(referenceIteration, managedIteration, "residualNorm", "legacy remarch iteration");

        AssertIntField(referenceFinal, managedFinal, "side", "legacy remarch final");
        AssertIntField(referenceFinal, managedFinal, "station", "legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "uei", "legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "theta", "legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "legacy remarch final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "legacy remarch final");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed system");

    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_Step_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(Path.Combine(
            FortranReferenceCases.GetFortranDebugDirectory(),
            "reference",
            "alpha10_p80_seed_step_ref"));
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_step");

        ParityTraceRecord referenceStep = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_step" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedStep = managedRecords
            .Where(static record => record.Kind == "laminar_seed_step" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceStep, managedStep, "uei", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "theta", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "dstar", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "ampl", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "ratioTheta", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "ratioDstar", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "dmax", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "rlx", "direct seed step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "direct seed step");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_Final_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceFinal, managedFinal, "theta", "direct seed final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "direct seed final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "direct seed final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "direct seed final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "direct seed final");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_SystemEq3Rows_BitwiseMatchFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed system eq3 rows");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed system eq3 rows");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed system eq3 rows");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed system eq3 rows");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_Row13Eq1DTerms_BitwiseMatchFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedEq1DStation16ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "bldif_eq1_d_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_d_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_d_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row13BaseTerm", "direct seed station16 row13 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13UpwTerm", "direct seed station16 row13 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13DeTerm", "direct seed station16 row13 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13UsTerm", "direct seed station16 row13 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13Transport", "direct seed station16 row13 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13CqTerm", "direct seed station16 row13 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13CfTerm", "direct seed station16 row13 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13HkTerm", "direct seed station16 row13 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row13", "direct seed station16 row13 eq1 d");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_Row23Eq1DTerms_BitwiseMatchFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedEq1DStation16ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "bldif_eq1_d_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_d_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_d_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "row23BaseTerm", "direct seed station16 row23 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row23UpwTerm", "direct seed station16 row23 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row23DeTerm", "direct seed station16 row23 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row23UsTerm", "direct seed station16 row23 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row23Transport", "direct seed station16 row23 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row23CqTerm", "direct seed station16 row23 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row23CfTerm", "direct seed station16 row23 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row23HkTerm", "direct seed station16 row23 eq1 d");
        AssertFloatField(referenceTerms, managedTerms, "row23", "direct seed station16 row23 eq1 d");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation13FinalCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 13))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 13))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceFinal, managedFinal, "theta", "direct seed station13 final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "direct seed station13 final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "direct seed station13 final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "direct seed station13 final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "direct seed station13 final");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15FinalCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceFinal, managedFinal, "theta", "direct seed station15 final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "direct seed station15 final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "direct seed station15 final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "direct seed station15 final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "direct seed station15 final");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15LastSystemBeforeFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system", "laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceSystem = GetOrderedStationRecordsBeforeFinal(referencePath, 15, "laminar_seed_system").Last();
        ParityTraceRecord managedSystem = GetOrderedStationRecordsBeforeFinal(managedRecords, 15, "laminar_seed_system").Last();

        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station15 final system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station15 final system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station15 final system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station15 final system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station15 final system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station15 final system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station15 final system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15Iteration5TransitionSeedSystem_BitwiseMatchesFortranTrace()
    {
        _ = DirectSeedIteration5TransitionWindowReferenceDirectory;
        new DirectSeedStation15SystemMicroParityTests()
            .Alpha10_P80_DirectSeedStation15_TransitionWindow_Iteration5_BitwiseMatchFortranTrace();
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16FirstSystemCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station16 first system carry");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station16 first system carry");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station16 first system carry");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation17FirstSystemCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 17))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 17))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station17 first system carry");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station17 first system carry");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station17 first system carry");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation18FirstSystemCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 18))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 18))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station18 first system carry");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station18 first system carry");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station18 first system carry");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation14FinalCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 14))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 14))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceFinal, managedFinal, "theta", "direct seed station14 final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "direct seed station14 final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "direct seed station14 final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "direct seed station14 final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "direct seed station14 final");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation13SystemCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 13))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 13))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station13 system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station13 system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15SystemCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "blsys_interval_inputs",
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 15, "laminar_seed_system").First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station15 system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station15 system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15SystemDstar_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "blsys_interval_inputs",
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecords(referencePath, 15, "laminar_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecords(
                RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system"),
                15,
                "laminar_seed_system",
                "transition_seed_system")
            .GroupBy(static record => (int)GetFloatValue(record, "iteration"))
            .OrderBy(static group => group.Key)
            .Select(static group => group.OrderBy(record => record.Sequence).Last())
            .ToArray();

        AssertOrderedFieldParity(referenceRecords, managedRecords, "dstar", "direct seed station15 system dstar iteration scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15Iteration6System_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "blsys_interval_inputs",
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 15, "laminar_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", 6));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", 6));

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station15 iteration6 system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station15 iteration6 system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15Iteration4System_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "blsys_interval_inputs",
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 15, "laminar_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", 4));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", 4));

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station15 iteration4 system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station15 iteration4 system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15Iteration5System_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "blsys_interval_inputs",
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 15, "laminar_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", 5));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", 5));

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station15 iteration5 system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station15 iteration5 system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation14SystemCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 14))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 14, "laminar_seed_system").First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station14 system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station14 system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation6LastSystemBeforeFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedStepStation6ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "laminar_seed_step",
            "laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 6))
            .Where(record => record.Sequence < referenceFinal.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 6))
            .Where(record => record.Sequence < managedFinal.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station6 final system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station6 final system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation6FirstSystemIteration_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedStepStation6ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "laminar_seed_step",
            "laminar_seed_final");

        ParityTraceRecord referenceSystem = GetOrderedStationRecordsBeforeFinal(
                referencePath,
                station: 6,
                "laminar_seed_system")
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecordsBeforeFinal(
                managedRecords,
                station: 6,
                "laminar_seed_system")
            .First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station6 first system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station6 first system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation5FirstSystemIteration_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedStepStation5ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "laminar_seed_step",
            "laminar_seed_final");

        ParityTraceRecord referenceSystem = GetOrderedStationRecordsBeforeFinal(
                referencePath,
                station: 5,
                "laminar_seed_system")
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecordsBeforeFinal(
                managedRecords,
                station: 5,
                "laminar_seed_system")
            .First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station5 first system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station5 first system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation6LastStepBeforeFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedStepStation6ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_step",
            "laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 6))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceStep = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_step" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 6))
            .Where(record => record.Sequence < referenceFinal.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedStep = managedRecords
            .Where(static record => record.Kind == "laminar_seed_step" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 6))
            .Where(record => record.Sequence < managedFinal.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceStep, managedStep, "uei", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "theta", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "dstar", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "ampl", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "deltaShear", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "deltaUe", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "ratioShear", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "ratioTheta", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "ratioDstar", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "ratioUe", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "dmax", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "rlx", "direct seed station6 final step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "direct seed station6 final step");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation6SystemTheta_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedStepStation6ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecordsBeforeFinal(
            referencePath,
            station: 6,
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecordsBeforeFinal(
            RunManagedPreparationTrace("laminar_seed_system", "laminar_seed_step", "laminar_seed_final"),
            station: 6,
            "laminar_seed_system");

        AssertOrderedFieldParity(referenceRecords, managedRecords, "theta", "direct seed station6 system theta iteration scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation6StepDeltaTheta_AcrossIterations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedStepStation6ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetOrderedStationRecordsBeforeFinal(
            referencePath,
            station: 6,
            "laminar_seed_step");
        IReadOnlyList<ParityTraceRecord> managedRecords = GetOrderedStationRecordsBeforeFinal(
            RunManagedPreparationTrace("laminar_seed_system", "laminar_seed_step", "laminar_seed_final"),
            station: 6,
            "laminar_seed_step");

        AssertOrderedFieldParity(referenceRecords, managedRecords, "deltaTheta", "direct seed station6 step delta-theta iteration scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation7System_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station7 system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station7 system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation12FinalCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 12))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 12))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceFinal, managedFinal, "theta", "direct seed station12 final");
        AssertFloatField(referenceFinal, managedFinal, "dstar", "direct seed station12 final");
        AssertFloatField(referenceFinal, managedFinal, "ampl", "direct seed station12 final");
        AssertFloatField(referenceFinal, managedFinal, "ctau", "direct seed station12 final");
        AssertFloatField(referenceFinal, managedFinal, "mass", "direct seed station12 final");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation12SystemCarry_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 12))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 12))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceSystem, managedSystem, "uei", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "theta", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "dstar", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "ampl", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "ctau", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_T2", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "hk2_D2", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "residual1", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "residual2", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "residual3", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "residual4", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row11", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row12", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row13", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row14", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row21", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row22", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row23", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row24", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row31", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row32", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row33", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row34", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row41", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row42", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row43", "direct seed station12 system");
        AssertFloatField(referenceSystem, managedSystem, "row44", "direct seed station12 system");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperLaminarSeedSystemTheta_AcrossStations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetFirstUpperSideRecordPerStation(
            referencePath,
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> managedRecords = GetFirstUpperSideRecordPerStation(
            RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system"),
            "laminar_seed_system",
            "transition_seed_system");

        AssertOrderedFieldParity(referenceRecords, managedRecords, "theta", "direct seed system theta scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperLaminarSeedSystemDstar_AcrossStations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetFirstUpperSideRecordPerStation(
            referencePath,
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> managedRecords = GetFirstUpperSideRecordPerStation(
            RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system"),
            "laminar_seed_system",
            "transition_seed_system");

        AssertOrderedFieldParity(referenceRecords, managedRecords, "dstar", "direct seed system dstar scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperLaminarSeedSystemCtau_AcrossStations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_system",
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetFirstUpperSideRecordPerStation(
            referencePath,
            "laminar_seed_system");
        IReadOnlyList<ParityTraceRecord> managedRecords = GetFirstUpperSideRecordPerStation(
            RunManagedPreparationTrace("laminar_seed_system", "transition_seed_system"),
            "laminar_seed_system",
            "transition_seed_system");

        AssertOrderedFieldParity(referenceRecords, managedRecords, "ctau", "direct seed system ctau scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperLaminarSeedFinalTheta_AcrossStations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetFirstUpperSideRecordPerStation(
            referencePath,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = GetFirstUpperSideRecordPerStation(
            RunManagedPreparationTrace("laminar_seed_final"),
            "laminar_seed_final");

        AssertOrderedFieldParity(referenceRecords, managedRecords, "theta", "direct seed final theta scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperLaminarSeedFinalDstar_AcrossStations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetFirstUpperSideRecordPerStation(
            referencePath,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = GetFirstUpperSideRecordPerStation(
            RunManagedPreparationTrace("laminar_seed_final"),
            "laminar_seed_final");

        AssertOrderedFieldParity(referenceRecords, managedRecords, "dstar", "direct seed final dstar scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperLaminarSeedFinalCtau_AcrossStations_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePathContainingKinds(
            DirectSeedReferenceDirectory,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> referenceRecords = GetFirstUpperSideRecordPerStation(
            referencePath,
            "laminar_seed_final");
        IReadOnlyList<ParityTraceRecord> managedRecords = GetFirstUpperSideRecordPerStation(
            RunManagedPreparationTrace("laminar_seed_final"),
            "laminar_seed_final");

        AssertOrderedFieldParity(referenceRecords, managedRecords, "ctau", "direct seed final ctau scan");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation7LastStepBeforeFinal_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedStepStation7ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_step",
            "laminar_seed_final");

        ParityTraceRecord referenceFinal = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedFinal = managedRecords
            .Where(static record => record.Kind == "laminar_seed_final" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 7))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceStep = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_step" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 7))
            .Where(record => record.Sequence < referenceFinal.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedStep = managedRecords
            .Where(static record => record.Kind == "laminar_seed_step" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 7))
            .Where(record => record.Sequence < managedFinal.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceStep, managedStep, "uei", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "theta", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "dstar", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "ampl", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "deltaShear", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "deltaTheta", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "deltaDstar", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "deltaUe", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "ratioShear", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "ratioTheta", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "ratioDstar", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "ratioUe", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "dmax", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "rlx", "direct seed station7 final step");
        AssertFloatField(referenceStep, managedStep, "residualNorm", "direct seed station7 final step");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_Eq1ResidualTerms_BitwiseMatchFortranTrace()
    {
        string systemReferencePath = GetLatestTracePath(DirectSeedReferenceDirectory);
        string residualReferencePath = GetLatestTracePath(DirectSeedEq1ResidualReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "bldif_eq1_residual_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                systemReferencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                residualReferencePath,
                static record => record.Kind == "bldif_eq1_residual_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16) &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16) &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        string[] fields =
        [
            "scc",
            "cqa",
            "upw",
            "oneMinusUpw",
            "s1",
            "s2",
            "saLeftTerm",
            "saRightTerm",
            "cq1",
            "cq2",
            "sa",
            "dxi",
            "dea",
            "slog",
            "uq",
            "ulog",
            "eq1Source",
            "eq1Production",
            "eq1LogLoss",
            "eq1Convection",
            "eq1DuxGain",
            "eq1SubStored",
            "rezcStoredTerms",
            "rezc"
        ];

        foreach (string field in fields)
        {
            AssertFloatField(referenceResidual, managedResidual, field, "direct seed eq1 residual");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_Eq3ResidualTerms_BitwiseMatchFortranTrace()
    {
        string residualReferencePath = GetLatestTracePath(DirectSeedEq3ResidualReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("laminar_seed_system", "bldif_eq3_residual_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                residualReferencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                residualReferencePath,
                static record => record.Kind == "bldif_eq3_residual_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq3_residual_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceResidual, managedResidual, "hlog", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "btmp", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "ulog", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "btmpUlog", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "xlog", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "cfx", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "halfCfx", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "dix", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "transport", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "xlogTransport", "direct seed eq3 residual");
        AssertFloatField(referenceResidual, managedResidual, "rezh", "direct seed eq3 residual");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_CarriedSecondaryStation1De_BitwiseMatchesFortranTrace()
    {
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "bldif_secondary_station",
            "bldif_eq1_residual_terms");

        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        Assert.True(
            ReadBooleanish(managedSecondary, "usedSecondaryOverride"),
            $"direct seed station16 carried secondary station1 expected override replay sequence={managedSecondary.Sequence}");
        AssertHex("0x3A4BEF3E", GetFloatHex(managedSecondary, "de"), "direct seed station16 carried secondary station1 de");
        AssertHex("0x3AEBC01E", GetFloatHex(managedSecondary, "cf"), "direct seed station16 carried secondary station1 cf");
        AssertHex("0xC1B8AE5E", GetFloatHex(managedSecondary, "cfD"), "direct seed station16 carried secondary station1 cfD");
        AssertHex("0x4227FECB", GetFloatHex(managedSecondary, "cfT"), "direct seed station16 carried secondary station1 cfT");
        AssertHex("0xC1428A66", GetFloatHex(managedSecondary, "cfmD"), "direct seed station16 carried secondary station1 cfmD");
        AssertHex("0x3BD1A8DC", GetFloatHex(managedSecondary, "di"), "direct seed station16 carried secondary station1 di");
        AssertHex("0xC45CD8DA", GetFloatHex(managedSecondary, "hsD"), "direct seed station16 carried secondary station1 hsD");
        AssertHex("0x45727CB4", GetFloatHex(managedSecondary, "usT"), "direct seed station16 carried secondary station1 usT");
    }

    [Fact(Skip = "No authoritative station16 current secondary station2 reference capture is available yet.")]
    public void Alpha10_P80_DirectSeed_UpperStation16_CurrentSecondaryStation2De_BitwiseMatchesFortranTrace()
    {
        throw new NotSupportedException();
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_Row33D2Terms_BitwiseMatchFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedRow33ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "blvar_turbulent_di_terms",
            "bldif_common",
            "bldif_upw_terms",
            "bldif_eq3_d2_terms");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "blvar_turbulent_di_terms" or "bldif_common" or "bldif_upw_terms" or "bldif_eq3_d2_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 16) &&
                             HasExactDataInt(record, "iteration", 1));
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16) &&
                                    HasExactDataInt(record, "iteration", 1))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceEq3 = referenceRecords
            .Where(record => record.Kind == "bldif_eq3_d2_terms" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedEq3 = managedRecords
            .Where(record => record.Kind == "bldif_eq3_d2_terms" && record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceEq3, managedEq3, "xot2", "direct seed station16 row33 d2");
        AssertFloatField(referenceEq3, managedEq3, "zDix", "direct seed station16 row33 d2");
        AssertFloatField(referenceEq3, managedEq3, "upwD", "direct seed station16 row33 d2");
        AssertFloatField(referenceEq3, managedEq3, "baseDi", "direct seed station16 row33 d2");
        AssertFloatField(referenceEq3, managedEq3, "extraH", "direct seed station16 row33 d2");
        AssertFloatField(referenceEq3, managedEq3, "extraUpw", "direct seed station16 row33 d2");
        AssertFloatField(referenceEq3, managedEq3, "row33", "direct seed station16 row33 d2");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation16_Row34U2Terms_BitwiseMatchFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedRow34ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "bldif_eq3_u2_terms");

        IReadOnlyList<ParityTraceRecord> referenceRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind is "laminar_seed_system" or "bldif_eq3_u2_terms")
            .OrderBy(static record => record.Sequence)
            .ToArray();

        ParityTraceRecord referenceSystem = referenceRecords.Single(
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 16) &&
                             HasExactDataInt(record, "iteration", 1));
        ParityTraceRecord managedSystem = managedRecords
            .Where(static record => record.Kind == "laminar_seed_system" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 16) &&
                                    HasExactDataInt(record, "iteration", 1))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceEq3 = referenceRecords
            .Where(record => record.Kind == "bldif_eq3_u2_terms" && record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedEq3 = managedRecords
            .Where(record => record.Kind == "bldif_eq3_u2_terms" && record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceEq3, managedEq3, "zU2", "direct seed station16 row34 u2");
        AssertFloatField(referenceEq3, managedEq3, "zHcaHalf", "direct seed station16 row34 u2");
        AssertFloatField(referenceEq3, managedEq3, "baseCf", "direct seed station16 row34 u2");
        AssertFloatField(referenceEq3, managedEq3, "baseDi", "direct seed station16 row34 u2");
        AssertFloatField(referenceEq3, managedEq3, "extraUpw", "direct seed station16 row34 u2");
        AssertFloatField(referenceEq3, managedEq3, "row34", "direct seed station16 row34 u2");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_CurrentSecondaryStation2De_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedCarryChainReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 2);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 2);

        AssertFloatField(referenceSecondary, managedSecondary, "de", "direct seed station15 current secondary station2");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Eq1ResidualTerms_BitwiseMatchFortranTrace()
    {
        string systemReferencePath = GetLatestTracePath(DirectSeedReferenceDirectory);
        string residualReferencePath = GetLatestTracePath(DirectSeedEq1ResidualStation15ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_eq1_residual_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                systemReferencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        ParityTraceRecord referenceResidual = ParityTraceLoader.ReadMatching(
                residualReferencePath,
                static record => record.Kind == "bldif_eq1_residual_terms" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15) &&
                                 HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedResidual = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_residual_terms" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 15) &&
                                    HasExactDataInt(record, "ityp", 2))
            .OrderBy(static record => record.Sequence)
            .First();

        AssertFloatField(referenceResidual, managedResidual, "scc", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "cqa", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "upw", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "oneMinusUpw", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "s1", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "s2", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "saLeftTerm", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "saRightTerm", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "cq1", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "cq2", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "sa", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "x1", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "x2", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "dxi", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "dea", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "slog", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "uq", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "ulog", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "eq1Source", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "eq1Production", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "eq1LogLoss", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "eq1Convection", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "eq1DuxGain", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "eq1SubStored", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "rezcStoredTerms", "direct seed station15 eq1 residual");
        AssertFloatField(referenceResidual, managedResidual, "rezc", "direct seed station15 eq1 residual");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Eq1ThetaRowTerms_BitwiseMatchFortranTrace()
    {
        string systemReferencePath = GetLatestTracePath(DirectSeedReferenceDirectory);
        string rowReferencePath = GetLatestTracePath(DirectSeedEq1TStation15ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_eq1_t_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                systemReferencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                rowReferencePath,
                static record => record.Kind == "bldif_eq1_t_terms" &&
                                 HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_t_terms" &&
                                    HasExactDataInt(record, "ityp", 2))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "zDe1", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "de1T1", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "upwT1Term", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "de1T1Term", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "us1T1Term", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row12Transport", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zCq1", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cq1T1", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cq1T1Term", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zCf1", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf1T1", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf1T1Term", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zHk1", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "hk1T1", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "hk1T1Term", "direct seed station15 eq1 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row12", "direct seed station15 eq1 theta row");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Eq2ThetaRowTerms_BitwiseMatchFortranTrace()
    {
        string systemReferencePath = GetLatestTracePath(DirectSeedReferenceDirectory);
        string rowReferencePath = GetLatestTracePath(DirectSeedEq2TStation15ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_eq2_t2_terms");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                systemReferencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord nextReferenceSystem = ParityTraceLoader.ReadMatching(
                systemReferencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .Where(record => record.Sequence > referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();
        ParityTraceRecord nextManagedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system")
            .Where(record => record.Sequence > managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                rowReferencePath,
                static record => record.Kind == "bldif_eq2_t2_terms")
            .Where(record => record.Sequence > referenceSystem.Sequence)
            .Where(record => record.Sequence < nextReferenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq2_t2_terms")
            .Where(record => record.Sequence > managedSystem.Sequence)
            .Where(record => record.Sequence < nextManagedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "zHaHalf", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zCfm", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zCf2", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zT2", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "h2T2", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cfmT2", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf2T2", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row22Ha", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row22Cfm", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row22Cf", "direct seed station15 eq2 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row22", "direct seed station15 eq2 theta row");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation5_Eq3ThetaRowTerms_BitwiseMatchFortranTrace()
    {
        string rowReferencePath = GetLatestTracePath(DirectSeedEq3TStation5ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "bldif_eq3_t2_terms");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(
                rowReferencePath,
                station: 5,
                "laminar_seed_system")
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(
                managedRecords,
                station: 5,
                "laminar_seed_system")
            .First();

        ParityTraceRecord referenceTerms = ParityTraceLoader.ReadMatching(
                rowReferencePath,
                static record => record.Kind == "bldif_eq3_t2_terms" &&
                                 HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq3_t2_terms" &&
                                    HasExactDataInt(record, "ityp", 1))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceTerms, managedTerms, "x1", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "x2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "t1", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "t2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "u1", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "u2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "upw", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "xot1", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "xot2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf1", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "di1", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "di2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf1xot1", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf2xot2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "di1xot1", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "di2xot2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zTermCf2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zTermDi2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zT2Body", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zT2Wake", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zHs2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "hs2T2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zCf2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cf2T2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zDi2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "di2T2", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "baseHs", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "baseCf", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "baseDi", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "baseZT", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "extraH", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zCfx", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zDix", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "cfxUpw", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "dixUpw", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "zUpw", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "upwT", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "extraUpw", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "baseStored32", "direct seed station5 eq3 theta row");
        AssertFloatField(referenceTerms, managedTerms, "row32", "direct seed station5 eq3 theta row");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation5_CfChain_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedStation5CfChainReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "kinematic_result",
            "blvar_cf_terms",
            "laminar_seed_system");

        ParityTraceRecord referenceSystem = GetOrderedStationRecords(referencePath, 5, "laminar_seed_system").First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 5, "laminar_seed_system").First();

        ParityTraceRecord referenceKinematic = SelectKinematicForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSystem);
        ParityTraceRecord managedKinematic = SelectKinematicForSystem(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSystem);

        ParityTraceRecord referenceCf = SelectCfTermsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blvar_cf_terms"),
            referenceKinematic,
            referenceSystem);
        ParityTraceRecord managedCf = SelectCfTermsForSystem(
            managedRecords.Where(static record => record.Kind == "blvar_cf_terms"),
            managedKinematic,
            managedSystem);

        AssertFloatField(referenceCf, managedCf, "cf", "direct seed station5 cf chain");
        AssertFloatField(referenceCf, managedCf, "cfHk", "direct seed station5 cf chain");
        AssertFloatField(referenceCf, managedCf, "cfRt", "direct seed station5 cf chain");
        AssertFloatField(referenceCf, managedCf, "cfT", "direct seed station5 cf chain");
        AssertFloatField(referenceCf, managedCf, "cfD", "direct seed station5 cf chain");
        AssertFloatField(referenceCf, managedCf, "cfU", "direct seed station5 cf chain");
        AssertFloatField(referenceCf, managedCf, "cfMs", "direct seed station5 cf chain");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_CarriedSecondaryStation1_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedSecondaryStation1ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_secondary_station");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        ParityTraceRecord referenceSecondary = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_secondary_station" &&
                                 HasExactDataInt(record, "ityp", 2) &&
                                 HasExactDataInt(record, "station", 1))
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
        ParityTraceRecord managedSecondary = managedRecords
            .Where(static record => record.Kind == "bldif_secondary_station" &&
                                    HasExactDataInt(record, "ityp", 2) &&
                                    HasExactDataInt(record, "station", 1))
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        AssertFloatField(referenceSecondary, managedSecondary, "hc", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "hs", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "hsHk", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "hkD", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "hsD", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "hsT", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "us", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "usT", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "hkU", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "rtT", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "rtU", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cq", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cf", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cfU", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cfT", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cfD", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cfMs", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cfmU", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cfmT", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cfmD", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "cfmMs", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "di", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "diT", "direct seed station15 carried secondary");
        AssertFloatField(referenceSecondary, managedSecondary, "de", "direct seed station15 carried secondary");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Station1Kinematic_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedKinematicReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "kinematic_result");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        IReadOnlyList<ParityTraceRecord> referenceKinematicCandidates = ParityTraceLoader.ReadMatching(
            referencePath,
            static record => record.Kind == "kinematic_result");
        ParityTraceRecord referenceKinematic = SelectKinematicForSystem(referenceKinematicCandidates, referenceSystem);

        ParityTraceRecord managedKinematic = SelectKinematicForSystem(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSystem);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "direct seed station15 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "direct seed station15 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "direct seed station15 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "direct seed station15 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "direct seed station15 station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "v2", "direct seed station15 station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "v2_ms", "direct seed station15 station1 kinematic");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Station1CompressibleVelocity_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedCompressibleReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "kinematic_result",
            "compressible_velocity");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        IReadOnlyList<ParityTraceRecord> referenceKinematicCandidates = ParityTraceLoader.ReadMatching(
            referencePath,
            static record => record.Kind == "kinematic_result");
        ParityTraceRecord referenceKinematic = SelectKinematicForSystem(referenceKinematicCandidates, referenceSystem);

        ParityTraceRecord managedKinematic = SelectKinematicForSystem(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSystem);

        IReadOnlyList<ParityTraceRecord> referenceCompressibleCandidates = ParityTraceLoader.ReadMatching(
            referencePath,
            static record => record.Kind == "compressible_velocity");
        ParityTraceRecord referenceCompressible = SelectCompressibleForKinematic(referenceCompressibleCandidates, referenceKinematic);

        ParityTraceRecord managedCompressible = SelectCompressibleForKinematic(
            managedRecords.Where(static record => record.Kind == "compressible_velocity"),
            managedKinematic);

        AssertIntField(referenceCompressible, managedCompressible, "ueiBits", "direct seed station15 station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "tkblBits", "direct seed station15 station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "qinfblBits", "direct seed station15 station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "tkblMsBits", "direct seed station15 station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "u2Bits", "direct seed station15 station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "u2UeiBits", "direct seed station15 station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "u2MsBits", "direct seed station15 station1 compressible");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_CarriedStation1Kinematic_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedCarryChainReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "direct seed station15 carried station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "direct seed station15 carried station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "direct seed station15 carried station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "direct seed station15 carried station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "direct seed station15 carried station1 kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "v2", "direct seed station15 carried station1 kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "v2_ms", "direct seed station15 carried station1 kinematic");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_CarriedStation1CompressibleVelocity_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedCarryChainReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "compressible_velocity");

        IReadOnlyList<ParityTraceRecord> referenceSystemRecords = GetOrderedStationRecords(
            referencePath,
            15,
            "laminar_seed_system",
            "transition_seed_system");
        IReadOnlyList<ParityTraceRecord> managedSystemRecords = GetOrderedStationRecords(
            managedRecords,
            15,
            "laminar_seed_system",
            "transition_seed_system");

        ParityTraceRecord referenceSystem = referenceSystemRecords.First();
        ParityTraceRecord managedSystem = managedSystemRecords.First();

        ParityTraceRecord referenceCompressible = SelectCompressibleForSystemHandoff(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "compressible_velocity"),
            referenceSystem,
            referenceSystemRecords);
        ParityTraceRecord managedCompressible = SelectCompressibleForSystemHandoff(
            managedRecords.Where(static record => record.Kind == "compressible_velocity"),
            managedSystem,
            managedSystemRecords);

        AssertIntField(referenceCompressible, managedCompressible, "ueiBits", "direct seed station15 carried station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "tkblBits", "direct seed station15 carried station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "qinfblBits", "direct seed station15 carried station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "tkblMsBits", "direct seed station15 carried station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "u2Bits", "direct seed station15 carried station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "u2UeiBits", "direct seed station15 carried station1 compressible");
        AssertIntField(referenceCompressible, managedCompressible, "u2MsBits", "direct seed station15 carried station1 compressible");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_CarriedStation1BlkinInputs_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedCarryBlkinReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result",
            "blkin_inputs");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        ParityTraceRecord referenceInputs = SelectBlkinInputsForKinematic(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blkin_inputs"),
            referenceKinematic);
        ParityTraceRecord managedInputs = SelectBlkinInputsForKinematic(
            managedRecords.Where(static record => record.Kind == "blkin_inputs"),
            managedKinematic);

        AssertIntField(referenceInputs, managedInputs, "u2Bits", "direct seed station15 carried station1 blkin inputs");
        AssertIntField(referenceInputs, managedInputs, "t2Bits", "direct seed station15 carried station1 blkin inputs");
        AssertIntField(referenceInputs, managedInputs, "d2Bits", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "hstinv", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "hstinv_ms", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "gm1bl", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "rstbl", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "rstbl_ms", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "hvrat", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "reybl", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "reybl_re", "direct seed station15 carried station1 blkin inputs");
        AssertFloatField(referenceInputs, managedInputs, "reybl_ms", "direct seed station15 carried station1 blkin inputs");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_CarriedStation1CfChain_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedCarryChainReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_secondary_station",
            "kinematic_result");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord managedSecondary = SelectSecondaryForSystem(
            managedRecords.Where(static record => record.Kind == "bldif_secondary_station"),
            managedSystem,
            ityp: 2,
            station: 1);

        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord managedKinematic = SelectKinematicForSecondary(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedSecondary);

        CfChainTraceResult referenceCf = InvokeCfChains(referenceKinematic, flowType: 2);
        CfChainTraceResult managedCf = InvokeCfChains(managedKinematic, flowType: 2);

        AssertHex(GetFloatHex(referenceSecondary, "cf"), referenceCf.CfHex, "direct seed station15 carried station1 reference cf helper");
        AssertHex(GetFloatHex(referenceSecondary, "cfU"), referenceCf.CfUHex, "direct seed station15 carried station1 reference cfU helper");
        Assert.True(
            string.Equals(GetFloatHex(referenceSecondary, "cfT"), referenceCf.CfTHex, StringComparison.Ordinal),
            $"direct seed station15 carried station1 reference cfT helper expected={GetFloatHex(referenceSecondary, "cfT")} actual={referenceCf.CfTHex} cf={referenceCf.CfHex} cfHk={referenceCf.CfHkHex} cfRt={referenceCf.CfRtHex} cfM={referenceCf.CfMHex} hkT={GetFloatHex(referenceKinematic, "hK2_t2")} rtT={GetFloatHex(referenceKinematic, "rT2_t2")} hkD={GetFloatHex(referenceKinematic, "hK2_d2")} rtU={GetFloatHex(referenceKinematic, "rT2_u2")} mU={GetFloatHex(referenceKinematic, "m2_u2")} cfDExpected={GetFloatHex(referenceSecondary, "cfD")} cfDActual={referenceCf.CfDHex} cfUExpected={GetFloatHex(referenceSecondary, "cfU")} cfUActual={referenceCf.CfUHex}");
        AssertHex(GetFloatHex(referenceSecondary, "cfD"), referenceCf.CfDHex, "direct seed station15 carried station1 reference cfD helper");
        AssertHex(GetFloatHex(referenceSecondary, "cfMs"), referenceCf.CfMsHex, "direct seed station15 carried station1 reference cfMs helper");

        AssertHex(GetFloatHex(managedSecondary, "cf"), managedCf.CfHex, "direct seed station15 carried station1 managed cf helper");
        AssertHex(GetFloatHex(managedSecondary, "cfU"), managedCf.CfUHex, "direct seed station15 carried station1 managed cfU helper");
        AssertHex(GetFloatHex(managedSecondary, "cfT"), managedCf.CfTHex, "direct seed station15 carried station1 managed cfT helper");
        AssertHex(GetFloatHex(managedSecondary, "cfD"), managedCf.CfDHex, "direct seed station15 carried station1 managed cfD helper");
        AssertHex(GetFloatHex(managedSecondary, "cfMs"), managedCf.CfMsHex, "direct seed station15 carried station1 managed cfMs helper");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_CarriedStation1KinematicFromBlkinInputs_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedCarryBlkinReferenceDirectory);

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();

        ParityTraceRecord referenceSecondary = SelectSecondaryForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "bldif_secondary_station"),
            referenceSystem,
            ityp: 2,
            station: 1);
        ParityTraceRecord referenceKinematic = SelectKinematicForSecondary(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "kinematic_result"),
            referenceSecondary);
        ParityTraceRecord referenceInputs = SelectBlkinInputsForKinematic(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "blkin_inputs"),
            referenceKinematic);

        KinematicTraceResult referenceReplay = InvokeKinematic(referenceInputs);

        AssertHex(GetIntHex(referenceKinematic, "u2Bits"), referenceReplay.U2BitsHex, "direct seed station15 reference kinematic helper u2Bits");
        AssertHex(GetIntHex(referenceKinematic, "t2Bits"), referenceReplay.T2BitsHex, "direct seed station15 reference kinematic helper t2Bits");
        AssertHex(GetIntHex(referenceKinematic, "d2Bits"), referenceReplay.D2BitsHex, "direct seed station15 reference kinematic helper d2Bits");
        AssertHex(GetFloatHex(referenceKinematic, "m2"), referenceReplay.M2Hex, "direct seed station15 reference kinematic helper m2");
        AssertHex(GetFloatHex(referenceKinematic, "r2"), referenceReplay.R2Hex, "direct seed station15 reference kinematic helper r2");
        AssertHex(GetIntHex(referenceKinematic, "h2Bits"), referenceReplay.H2BitsHex, "direct seed station15 reference kinematic helper h2Bits");
        AssertHex(GetIntHex(referenceKinematic, "hK2Bits"), referenceReplay.HK2BitsHex, "direct seed station15 reference kinematic helper hK2Bits");
        AssertHex(GetFloatHex(referenceKinematic, "hK2_t2"), referenceReplay.HK2T2Hex, "direct seed station15 reference kinematic helper hk2_t2");
        AssertHex(GetFloatHex(referenceKinematic, "hK2_d2"), referenceReplay.HK2D2Hex, "direct seed station15 reference kinematic helper hk2_d2");
        AssertHex(GetIntHex(referenceKinematic, "rT2Bits"), referenceReplay.RT2BitsHex, "direct seed station15 reference kinematic helper rt2Bits");
        AssertHex(GetFloatHex(referenceKinematic, "rT2_u2"), referenceReplay.RT2U2Hex, "direct seed station15 reference kinematic helper rt2_u2");
        AssertHex(GetFloatHex(referenceKinematic, "rT2_t2"), referenceReplay.RT2T2Hex, "direct seed station15 reference kinematic helper rt2_t2");
        AssertHex(GetFloatHex(referenceKinematic, "rT2_ms"), referenceReplay.RT2MsHex, "direct seed station15 reference kinematic helper rt2_ms");
        AssertHex(GetFloatHex(referenceKinematic, "rT2_re"), referenceReplay.RT2ReHex, "direct seed station15 reference kinematic helper rt2_re");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_TransitionIntervalInputs_BitwiseMatchFortranTrace()
    {
        string referencePath = GetLatestTracePath(DirectSeedTransitionIntervalInputsReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system").First();

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "x1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtA1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtT1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtT2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtD1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtD2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtX1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xtX2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2A1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2T1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2T2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2D1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2D2", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2X1", "direct seed station15 transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "wf2X2", "direct seed station15 transition interval inputs");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Iteration3OwnerBldifLogInputs_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = GetLatestTracePath(DirectSeedBldifLogIter3ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_log_inputs");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceSystems.Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord previousReferenceSystem = referenceSystems
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedSystems = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system");
        ParityTraceRecord managedSystem = managedSystems.Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord previousManagedSystem = managedSystems
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> referenceLogInputs = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_log_inputs")
            .Where(record => record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedLogInputs = managedRecords
            .Where(static record => record.Kind == "bldif_log_inputs")
            .Where(record => record.Sequence > previousManagedSystem.Sequence &&
                             record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceLogInputs.Count, managedLogInputs.Count);

        for (int index = 0; index < referenceLogInputs.Count; index++)
        {
            ParityTraceRecord referenceLogInput = referenceLogInputs[index];
            ParityTraceRecord managedLogInput = managedLogInputs[index];
            int? ityp = TryReadInt(referenceLogInput, "ityp");

            AssertIntField(referenceLogInput, managedLogInput, "ityp", $"direct seed station15 iter3 focused bldif log inputs record={index}");
            AssertFloatField(referenceLogInput, managedLogInput, "x1", $"direct seed station15 iter3 focused bldif log inputs record={index} ityp={ityp}");
            AssertFloatField(referenceLogInput, managedLogInput, "x2", $"direct seed station15 iter3 focused bldif log inputs record={index} ityp={ityp}");
            AssertFloatField(referenceLogInput, managedLogInput, "u1", $"direct seed station15 iter3 focused bldif log inputs record={index} ityp={ityp}");
            AssertFloatField(referenceLogInput, managedLogInput, "u2", $"direct seed station15 iter3 focused bldif log inputs record={index} ityp={ityp}");
            AssertFloatField(referenceLogInput, managedLogInput, "t1", $"direct seed station15 iter3 focused bldif log inputs record={index} ityp={ityp}");
            AssertFloatField(referenceLogInput, managedLogInput, "t2", $"direct seed station15 iter3 focused bldif log inputs record={index} ityp={ityp}");
            AssertFloatField(referenceLogInput, managedLogInput, "hs1", $"direct seed station15 iter3 focused bldif log inputs record={index} ityp={ityp}");
            AssertFloatField(referenceLogInput, managedLogInput, "hs2", $"direct seed station15 iter3 focused bldif log inputs record={index} ityp={ityp}");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Iteration3OwnerTransitionIntervalInputs_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = GetLatestTracePath(DirectSeedIteration4TransitionWindowReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "x1", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "direct seed station15 iter3 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "direct seed station15 iter3 focused transition interval inputs");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Iteration4OwnerTransitionIntervalInputs_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 4;

        string referencePath = GetLatestTracePath(DirectSeedIteration4TransitionWindowReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "transition_interval_inputs");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system")
            .Single(static record => HasExactDataInt(record, "iteration", Iteration));

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        AssertFloatField(referenceInputs, managedInputs, "x1", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x1Original", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "x2", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "xt", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u1Original", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "u2", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t1Original", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "t2", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d1Original", "direct seed station15 iter4 focused transition interval inputs");
        AssertFloatField(referenceInputs, managedInputs, "d2", "direct seed station15 iter4 focused transition interval inputs");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Iteration3OwnerBldifCommon_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 3;

        string referencePath = GetLatestTracePath(DirectSeedBldifLogIter3ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_common");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceSystems.Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord previousReferenceSystem = referenceSystems
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedSystems = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system");
        ParityTraceRecord managedSystem = managedSystems.Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord previousManagedSystem = managedSystems
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> referenceCommon = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_common")
            .Where(record => record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
        IReadOnlyList<ParityTraceRecord> managedCommon = managedRecords
            .Where(static record => record.Kind == "bldif_common")
            .Where(record => record.Sequence > previousManagedSystem.Sequence &&
                             record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.Equal(referenceCommon.Count, managedCommon.Count);

        for (int index = 0; index < referenceCommon.Count; index++)
        {
            ParityTraceRecord referenceCommonRecord = referenceCommon[index];
            ParityTraceRecord managedCommonRecord = managedCommon[index];
            int? ityp = TryReadInt(referenceCommonRecord, "ityp");

            AssertIntField(referenceCommonRecord, managedCommonRecord, "ityp", $"direct seed station15 iter3 focused bldif common record={index}");
            AssertFloatField(referenceCommonRecord, managedCommonRecord, "cfm", $"direct seed station15 iter3 focused bldif common record={index} ityp={ityp}");
            AssertFloatField(referenceCommonRecord, managedCommonRecord, "upw", $"direct seed station15 iter3 focused bldif common record={index} ityp={ityp}");
            AssertFloatField(referenceCommonRecord, managedCommonRecord, "xlog", $"direct seed station15 iter3 focused bldif common record={index} ityp={ityp}");
            AssertFloatField(referenceCommonRecord, managedCommonRecord, "ulog", $"direct seed station15 iter3 focused bldif common record={index} ityp={ityp}");
            AssertFloatField(referenceCommonRecord, managedCommonRecord, "tlog", $"direct seed station15 iter3 focused bldif common record={index} ityp={ityp}");
            AssertFloatField(referenceCommonRecord, managedCommonRecord, "hlog", $"direct seed station15 iter3 focused bldif common record={index} ityp={ityp}");
            AssertFloatField(referenceCommonRecord, managedCommonRecord, "ddlog", $"direct seed station15 iter3 focused bldif common record={index} ityp={ityp}");
        }
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_Iteration4OwnerBldifEq1STerms_FromFocusedTrace_BitwiseMatchesFortranTrace()
    {
        const int Iteration = 4;

        string referencePath = GetLatestTracePath(DirectSeedBldifEq1STermsStation15Iter4ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "bldif_eq1_s_terms");

        IReadOnlyList<ParityTraceRecord> referenceSystems = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .ToArray();
        ParityTraceRecord referenceSystem = referenceSystems.Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord previousReferenceSystem = referenceSystems
            .Where(record => record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        IReadOnlyList<ParityTraceRecord> managedSystems = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system", "transition_seed_system");
        ParityTraceRecord managedSystem = managedSystems.Single(static record => HasExactDataInt(record, "iteration", Iteration));
        ParityTraceRecord previousManagedSystem = managedSystems
            .Where(record => record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();

        ParityTraceRecord referenceSTerms = ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "bldif_eq1_s_terms")
            .Where(record => record.Sequence > previousReferenceSystem.Sequence &&
                             record.Sequence < referenceSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Single();
        ParityTraceRecord managedSTerms = managedRecords
            .Where(static record => record.Kind == "bldif_eq1_s_terms")
            .Where(record => record.Sequence > previousManagedSystem.Sequence &&
                             record.Sequence < managedSystem.Sequence)
            .OrderBy(static record => record.Sequence)
            .Single();

        AssertIntField(referenceSTerms, managedSTerms, "ityp", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "oneMinusUpw", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "upw", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "zSa", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "zSl", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "s1", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "s2", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "row11StoredTerm", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "row11LogTerm", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "row11", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "row21StoredTerm", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "row21LogTerm", "direct seed station15 iter4 focused eq1 s terms");
        AssertFloatField(referenceSTerms, managedSTerms, "row21", "direct seed station15 iter4 focused eq1 s terms");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation14_PrecedingTransitionPointIteration_BitwiseMatchesFortranTrace()
    {
        const string x1Hex = "0x3D77A070";
        const string x2Hex = "0x3D8937F0";

        string referencePath = GetLatestTracePath(DirectSeedTransitionPointReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("transition_point_iteration");

        ParityTraceRecord referenceIteration = SelectTransitionPointIterationForInterval(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            x1Hex,
            x2Hex);
        ParityTraceRecord managedIteration = SelectTransitionPointIterationForInterval(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration" &&
                                                 HasExactDataInt(record, "side", 1) &&
                                                 HasExactDataInt(record, "station", 14)),
            x1Hex,
            x2Hex);

        AssertFloatField(referenceIteration, managedIteration, "x1", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl1", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "amcrit", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "wf2", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual_A2", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "direct seed preceding transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "relaxation", "direct seed preceding transition point iteration");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_FreeTransitionPointIteration_BitwiseMatchesFortranTrace()
    {
        const string x1Hex = "0x3D8937F0";
        const string x2Hex = "0x3D997640";

        string referencePath = GetLatestTracePath(DirectSeedTransitionPointReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace("transition_point_iteration");

        ParityTraceRecord referenceIteration = SelectTransitionPointIterationForInterval(
            ParityTraceLoader.ReadMatching(referencePath, static record => record.Kind == "transition_point_iteration"),
            x1Hex,
            x2Hex);
        ParityTraceRecord managedIteration = SelectTransitionPointIterationForInterval(
            managedRecords.Where(static record => record.Kind == "transition_point_iteration" &&
                                                 HasExactDataInt(record, "side", 1) &&
                                                 HasExactDataInt(record, "station", 15)),
            x1Hex,
            x2Hex);

        AssertFloatField(referenceIteration, managedIteration, "x1", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "x2", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl1", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ampl2", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "amcrit", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "wf2", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "xt", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "tt", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "dt", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "ut", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "residual_A2", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "deltaA2", "direct seed free transition point iteration");
        AssertFloatField(referenceIteration, managedIteration, "relaxation", "direct seed free transition point iteration");
    }

    [Fact]
    public void Alpha10_P80_DirectSeed_UpperStation15_AcceptedTransitionKinematic_BitwiseMatchesFortranTrace()
    {
        string transitionReferencePath = GetLatestTracePath(DirectSeedTransitionIntervalInputsReferenceDirectory);
        string carryReferencePath = GetLatestTracePath(DirectSeedCarryChainReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> managedRecords = RunManagedPreparationTrace(
            "laminar_seed_system",
            "transition_seed_system",
            "transition_interval_inputs",
            "kinematic_result");

        ParityTraceRecord referenceSystem = ParityTraceLoader.ReadMatching(
                transitionReferencePath,
                static record => record.Kind == "laminar_seed_system" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 15))
            .OrderBy(static record => record.Sequence)
            .First();
        ParityTraceRecord managedSystem = GetOrderedStationRecords(managedRecords, 15, "laminar_seed_system").First();

        ParityTraceRecord referenceInputs = SelectTransitionIntervalInputsForSystem(
            ParityTraceLoader.ReadMatching(transitionReferencePath, static record => record.Kind == "transition_interval_inputs"),
            referenceSystem);
        ParityTraceRecord managedInputs = SelectTransitionIntervalInputsForSystem(
            managedRecords.Where(static record => record.Kind == "transition_interval_inputs"),
            managedSystem);

        string referenceUHex = GetFloatHex(referenceInputs, "u1");
        string referenceTHex = GetFloatHex(referenceInputs, "t1");
        string referenceDHex = GetFloatHex(referenceInputs, "d1");
        string managedUHex = GetFloatHex(managedInputs, "u1");
        string managedTHex = GetFloatHex(managedInputs, "t1");
        string managedDHex = GetFloatHex(managedInputs, "d1");

        ParityTraceRecord referenceKinematic = SelectKinematicByPrimaryState(
            ParityTraceLoader.ReadMatching(carryReferencePath, static record => record.Kind == "kinematic_result"),
            referenceUHex,
            referenceTHex,
            referenceDHex);
        ParityTraceRecord managedKinematic = SelectKinematicByPrimaryState(
            managedRecords.Where(static record => record.Kind == "kinematic_result"),
            managedUHex,
            managedTHex,
            managedDHex);

        AssertIntField(referenceKinematic, managedKinematic, "u2Bits", "direct seed station15 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "t2Bits", "direct seed station15 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "d2Bits", "direct seed station15 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "m2", "direct seed station15 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "r2", "direct seed station15 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "h2Bits", "direct seed station15 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "hK2Bits", "direct seed station15 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_t2", "direct seed station15 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "hK2_d2", "direct seed station15 accepted transition kinematic");
        AssertIntField(referenceKinematic, managedKinematic, "rT2Bits", "direct seed station15 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_u2", "direct seed station15 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_t2", "direct seed station15 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_ms", "direct seed station15 accepted transition kinematic");
        AssertFloatField(referenceKinematic, managedKinematic, "rT2_re", "direct seed station15 accepted transition kinematic");
    }

    [Fact]
    public void Alpha10_P80_UpperStation2_PredictedEdgeVelocity_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(ReferenceDirectory);
        IReadOnlyList<ParityTraceRecord> referenceTerms = ParityTraceLoader.ReadMatching(
            referencePath,
            static record => record.Kind == "predicted_edge_velocity_term" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2));
        ParityTraceRecord referenceFinal = ParityTraceLoader.FindSingle(
            referencePath,
            static record => record.Kind == "predicted_edge_velocity" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2),
            "UESET upper station-2 final predicted edge velocity");

        Assert.NotEmpty(referenceTerms);

        ManagedTraceResult managed = RunManagedTrace();
        AssertHex(GetFloatHex(referenceTerms[0], "mass"), managed.ContextStation2MassHex, "managed context station-2 mass");

        Assert.Equal(referenceTerms.Count, managed.Terms.Count);
        for (int index = 0; index < referenceTerms.Count; index++)
        {
            AssertTermParity(referenceTerms[index], managed.Terms[index], index);
        }

        AssertFinalParity(referenceFinal, managed.Final);
        AssertHex(
            GetFloatHex(referenceFinal, "predicted"),
            ToHex((float)managed.Usav[1, 0]),
            "returned usav upper station-2");
    }

    [Fact]
    public void Alpha10_P80_UpperStation2_SourceLowerStation36_PredictedEdgeVelocityTerm_BitwiseMatchesFortranTrace()
    {
        string referencePath = GetLatestTracePath(ReferenceDirectory);
        PredictedEdgeVelocityBlock referenceBlock = SelectFirstPredictedEdgeVelocityBlock(
            ParityTraceLoader.ReadMatching(
                referencePath,
                static record => (record.Kind == "predicted_edge_velocity_term" || record.Kind == "predicted_edge_velocity") &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 2)),
            side: 1,
            station: 2);
        ManagedTraceResult managed = RunManagedTrace();

        ParityTraceRecord referenceTerm = referenceBlock.Terms.Single(
            record => HasExactDataInt(record, "sourceSide", 2) &&
                      HasExactDataInt(record, "sourceStation", 36) &&
                      HasExactDataInt(record, "iPan", 47) &&
                      HasExactDataInt(record, "jPan", 82) &&
                      ReadBooleanish(record, "isWakeSource"));
        ParityTraceRecord managedTerm = managed.Terms.Single(
            record => HasExactDataInt(record, "sourceSide", 2) &&
                      HasExactDataInt(record, "sourceStation", 36) &&
                      HasExactDataInt(record, "iPan", 47) &&
                      HasExactDataInt(record, "jPan", 82) &&
                      ReadBooleanish(record, "isWakeSource"));

        AssertTermParity(referenceTerm, managedTerm, 81);
    }

    private static ManagedTraceResult RunManagedTrace()
    {
        ManagedTraceArtifacts trace = RunManagedTraceCore(CaseId, "ueset-micro");
        IReadOnlyList<ParityTraceRecord> terms = trace.Records
            .Where(static record => record.Kind == "predicted_edge_velocity_term" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 2))
            .ToArray();
        ParityTraceRecord final = trace.Records.Single(
            static record => record.Kind == "predicted_edge_velocity" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2));
        ParityTraceRecord legacyRemarchStation16Iteration = trace.Records.Single(
            static record => record.Kind == "legacy_seed_iteration" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 16));
        ParityTraceRecord legacyRemarchStation16Final = trace.Records.Single(
            static record => record.Kind == "legacy_seed_final" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 16));

        return new ManagedTraceResult(terms, final, trace.Usav, ToHex((float)trace.Context.BoundaryLayerState.MASS[1, 0]), legacyRemarchStation16Iteration, legacyRemarchStation16Final);
    }

    private static ManagedPredictedEdgeVelocityTraceResult RunManagedPredictedEdgeVelocityTrace(string caseId, int station)
    {
        return RunManagedPredictedEdgeVelocityTrace(caseId, side: 1, station: station);
    }

    private static ManagedPredictedEdgeVelocityTraceResult RunManagedPredictedEdgeVelocityTrace(string caseId, int side, int station)
    {
        ManagedTraceArtifacts trace = RunManagedTraceCore(caseId, "ueset-fulltrace-micro");
        PredictedEdgeVelocityBlock block = SelectFirstPredictedEdgeVelocityBlock(
            trace.Records,
            side: side,
            station: station);

        return new ManagedPredictedEdgeVelocityTraceResult(
            block.Terms,
            block.Final,
            trace.Usav,
            ToHex((float)trace.Context.BoundaryLayerState.MASS[station - 1, side - 1]));
    }

    private static ManagedLegacyRemarchStationTraceResult RunManagedLegacyRemarchStationTrace(
        string caseId,
        int side,
        int station,
        string sessionName)
    {
        ManagedTraceArtifacts trace = RunManagedTraceCore(caseId, sessionName);

        return new ManagedLegacyRemarchStationTraceResult(
            SelectLegacyRemarchStationBlock(trace.Records, side, station),
            ToHex((float)trace.Context.BoundaryLayerState.MASS[station - 1, side - 1]),
            trace.Records);
    }

    private static void AssertPredictedEdgeVelocityContributorParity(
        string caseId,
        int side,
        int station,
        int sourceSide,
        int sourceStation,
        string context)
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(caseId);
        PredictedEdgeVelocityBlock referenceBlock = SelectFirstPredictedEdgeVelocityBlock(
            ParityTraceLoader.ReadMatching(
                referencePath,
                record => (record.Kind == "predicted_edge_velocity_term" || record.Kind == "predicted_edge_velocity") &&
                          HasExactDataInt(record, "side", side) &&
                          HasExactDataInt(record, "station", station)),
            side,
            station);
        ManagedPredictedEdgeVelocityTraceResult managed = RunManagedPredictedEdgeVelocityTrace(caseId, side, station);

        ParityTraceRecord referenceTerm = referenceBlock.Terms.Single(
            record => HasExactDataInt(record, "sourceSide", sourceSide) &&
                      HasExactDataInt(record, "sourceStation", sourceStation));
        ParityTraceRecord managedTerm = managed.Terms.Single(
            record => HasExactDataInt(record, "sourceSide", sourceSide) &&
                      HasExactDataInt(record, "sourceStation", sourceStation));

        AssertTermParity(referenceTerm, managedTerm, 0);
        AssertHex(
            GetFloatHex(referenceTerm, "contribution"),
            GetFloatHex(managedTerm, "contribution"),
            $"{context} contribution");
    }

    private static void AssertLegacyRemarchPreConstraintRecordParity(string recordKind, string context)
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        LegacyRemarchStationBlock reference = LoadReferenceLegacyRemarchStationBlock(Alpha0FullCaseId, side: 2, station: 9);
        ManagedLegacyRemarchStationTraceResult managed = RunManagedLegacyRemarchStationTrace(
            Alpha0FullCaseId,
            side: 2,
            station: 9,
            sessionName: $"legacy-remarch-preconstraint-system-packets-{recordKind}-witness-st9");

        ParityTraceRecord referenceRecord = SelectLastRecordBefore(
            ParityTraceLoader.ReadMatching(referencePath, record => record.Kind == recordKind),
            reference.Constraint);
        ParityTraceRecord managedRecord = SelectLastRecordBefore(
            managed.Records.Where(record => record.Kind == recordKind),
            managed.Block.Constraint);

        AssertRecordDataParity(referenceRecord, managedRecord, context);
    }

    private static void AssertLegacyRemarchPreConstraintPrimaryStationParity(int station, string context)
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(Alpha0FullCaseId);
        LegacyRemarchStationBlock reference = LoadReferenceLegacyRemarchStationBlock(Alpha0FullCaseId, side: 2, station: 9);
        ManagedLegacyRemarchStationTraceResult managed = RunManagedLegacyRemarchStationTrace(
            Alpha0FullCaseId,
            side: 2,
            station: 9,
            sessionName: $"legacy-remarch-preconstraint-system-packets-primary-station{station}-witness-st9");

        ParityTraceRecord referenceRecord = SelectLastRecordBefore(
            ParityTraceLoader.ReadMatching(
                referencePath,
                record => record.Kind == "bldif_primary_station" &&
                          HasExactDataInt(record, "station", station)),
            reference.Constraint);
        ParityTraceRecord managedRecord = SelectLastRecordBefore(
            managed.Records.Where(
                record => record.Kind == "bldif_primary_station" &&
                          HasExactDataInt(record, "station", station)),
            managed.Block.Constraint);

        AssertRecordDataParity(referenceRecord, managedRecord, context);
    }

    private static LegacyRemarchStationBlock LoadReferenceLegacyRemarchStationBlock(string caseId, int side, int station)
    {
        string referencePath = FortranReferenceCases.GetReferenceTracePath(caseId);
        IReadOnlyList<ParityTraceRecord> stationRecords = ParityTraceLoader.ReadMatching(
                referencePath,
                record => IsLegacyRemarchStationRecord(record, side, station))
            .ToArray();

        return SelectLegacyRemarchStationBlock(stationRecords, side, station);
    }

    private static LegacyRemarchStationBlock SelectLegacyRemarchStationBlock(
        IEnumerable<ParityTraceRecord> records,
        int side,
        int station)
    {
        IReadOnlyList<ParityTraceRecord> stationRecords = records
            .Where(record => IsLegacyRemarchStationRecord(record, side, station))
            .ToArray();

        return new LegacyRemarchStationBlock(
            stationRecords
                .Where(static record => record.Kind == "legacy_seed_constraint")
                .OrderBy(static record => record.Sequence)
                .First(),
            stationRecords
                .Where(static record => record.Kind == "legacy_seed_iteration")
                .OrderBy(static record => record.Sequence)
                .First(),
            stationRecords
                .Where(static record => record.Kind == "legacy_seed_final_system")
                .OrderBy(static record => record.Sequence)
                .First(),
            stationRecords
                .Where(static record => record.Kind == "legacy_seed_final_delta")
                .OrderBy(static record => record.Sequence)
                .First(),
            stationRecords
                .Where(static record => record.Kind == "legacy_seed_final")
                .OrderBy(static record => record.Sequence)
                .First());
    }

    private static bool IsLegacyRemarchStationRecord(ParityTraceRecord record, int side, int station)
    {
        if (!HasExactDataInt(record, "side", side) || !HasExactDataInt(record, "station", station))
        {
            return false;
        }

        return record.Kind is "legacy_seed_constraint" or "legacy_seed_iteration" or "legacy_seed_final_system" or "legacy_seed_final_delta" or "legacy_seed_final";
    }

    private static ManagedTraceArtifacts RunManagedTraceCore(string caseId, string sessionName)
    {
        FortranReferenceCase definition = FortranReferenceCases.Get(caseId);
        AnalysisSettings settings = BuildAnalysisSettings(definition);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext(caseId);
        MethodInfo remarchMethod = typeof(ViscousSolverEngine).GetMethod(
            "RemarchBoundaryLayerLegacyDirect",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ViscousSolverEngine.RemarchBoundaryLayerLegacyDirect was not found.");
        MethodInfo method = typeof(ViscousNewtonAssembler).GetMethod(
            "ComputePredictedEdgeVelocities",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ViscousNewtonAssembler.ComputePredictedEdgeVelocities was not found.");
        var lines = new List<string>();

        double[,] usav;
        using (var traceWriter = new JsonlTraceWriter(TextWriter.Null, runtime: "csharp", session: new { caseName = sessionName }, serializedRecordObserver: lines.Add))
        {
            using var traceScope = SolverTrace.Begin(traceWriter);

            object?[] remarchArgs =
            {
                context.BoundaryLayerState,
                settings,
                context.InviscidState.TrailingEdgeGap,
                context.WakeSeed,
                context.Tkbl,
                context.QinfBl,
                context.TkblMs,
                context.HstInv,
                context.HstInvMs,
                context.RstBl,
                context.RstBlMs,
                context.ReyBl,
                context.ReyBlRe,
                context.ReyBlMs
            };
            _ = remarchMethod.Invoke(null, remarchArgs);

            object?[] args =
            {
                context.BoundaryLayerState,
                context.Dij,
                context.UeInv,
                context.Isp,
                context.NodeCount,
                null,
                true
            };

            usav = (double[,])(method.Invoke(null, args)
                ?? throw new InvalidOperationException("ComputePredictedEdgeVelocities returned null."));
        }

        return new ManagedTraceArtifacts(context, ParseObservedRecords(lines), usav);
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedPreparationTrace(params string[] kinds)
    {
        return RunManagedPreparationTraceForCase(CaseId, kinds);
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedPreparationTraceForCase(string caseId, params string[] kinds)
    {
        var lines = new List<string>();
        using (var traceWriter = new JsonlTraceWriter(TextWriter.Null, runtime: "csharp", session: new { caseName = $"{caseId}-pre-newton-prep-micro" }, serializedRecordObserver: lines.Add))
        {
            using var traceScope = SolverTrace.Begin(traceWriter);
            _ = BuildContext(caseId);
        }

        HashSet<string> requiredKinds = new(kinds, StringComparer.Ordinal);
        return ParseObservedRecords(lines)
            .Where(record => requiredKinds.Count == 0 || requiredKinds.Contains(record.Kind))
            .ToArray();
    }

    private static (IReadOnlyList<ParityTraceRecord> Reference, IReadOnlyList<ParityTraceRecord> Managed) GetLegacyDirectSeedCarryFocusedRecords()
    {
        lock (LegacyDirectSeedCarryFocusedArtifactsLock)
        {
            bool forceRefresh = string.Equals(
                Environment.GetEnvironmentVariable("XFOILSHARP_FORCE_PARITY_REFRESH"),
                "1",
                StringComparison.Ordinal);

            if (forceRefresh ||
                s_legacyDirectSeedCarryFocusedReferenceRecords is null ||
                s_legacyDirectSeedCarryFocusedManagedRecords is null)
            {
                EnsureLegacyDirectSeedCarryFocusedArtifacts();

                string referencePath = FortranParityArtifactLocator.GetLatestReferenceTracePath(LegacyDirectSeedCarryFocusedReferenceDirectory);
                string managedPath = FortranReferenceCases.GetManagedTracePath(LegacyDirectSeedCarryFocusManagedCaseId);
                s_legacyDirectSeedCarryFocusedReferenceRecords = ParityTraceLoader.ReadAll(referencePath);
                s_legacyDirectSeedCarryFocusedManagedRecords = ParityTraceLoader.ReadAll(managedPath);
            }

            return (s_legacyDirectSeedCarryFocusedReferenceRecords, s_legacyDirectSeedCarryFocusedManagedRecords);
        }
    }

    private static void EnsureLegacyDirectSeedCarryFocusedArtifacts()
    {
        string referenceTracePath = Path.Combine(LegacyDirectSeedCarryFocusedReferenceDirectory, "reference_trace.jsonl");
        string managedTracePath = Path.Combine(LegacyDirectSeedCarryFocusedManagedDirectory, "csharp_trace.jsonl");
        bool forceRefresh = string.Equals(
            Environment.GetEnvironmentVariable("XFOILSHARP_FORCE_PARITY_REFRESH"),
            "1",
            StringComparison.Ordinal);

        if (!forceRefresh && File.Exists(referenceTracePath) && File.Exists(managedTracePath))
        {
            return;
        }

        string repositoryRoot = FortranReferenceCases.FindRepositoryRoot();
        string referenceScriptPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "run_reference.sh");
        string managedScriptPath = Path.Combine(repositoryRoot, "tools", "fortran-debug", "run_managed_case.sh");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XFOIL_TRACE_KIND_ALLOW"] = "blsys_interval_inputs,bldif_primary_station,bldif_secondary_station,bldif_eq3_d2_terms,bldif_residual,laminar_seed_system",
            ["XFOIL_TRACE_TRIGGER_KIND"] = "laminar_seed_system",
            ["XFOIL_TRACE_TRIGGER_SIDE"] = "2",
            ["XFOIL_TRACE_TRIGGER_STATION"] = "8",
            ["XFOIL_TRACE_RING_BUFFER"] = "96",
            ["XFOIL_TRACE_POST_LIMIT"] = "8",
            ["XFOIL_MAX_TRACE_MB"] = "100"
        };

        RunShellScript(
            referenceScriptPath,
            [
                "--airfoil", "0012",
                "--re", "1000000",
                "--alpha", "0",
                "--panels", "12",
                "--ncrit", "9",
                "--iter", "80",
                "--output-dir", LegacyDirectSeedCarryFocusedReferenceDirectory
            ],
            environment);
        RunShellScript(
            managedScriptPath,
            [
                "--airfoil", "0012",
                "--re", "1000000",
                "--alpha", "0",
                "--panels", "12",
                "--ncrit", "9",
                "--iter", "80",
                "--output-dir", LegacyDirectSeedCarryFocusedManagedDirectory,
                "--reference-output-dir", LegacyDirectSeedCarryFocusedReferenceDirectory
            ],
            environment);
    }

    private static void RunShellScript(
        string scriptPath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            WorkingDirectory = FortranReferenceCases.FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (string variableName in FocusedTraceEnvironmentVariableNames)
        {
            _ = startInfo.Environment.Remove(variableName);
        }

        foreach ((string key, string? value) in environment)
        {
            if (value is null)
            {
                _ = startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start focused trace script: {scriptPath}");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Focused trace script failed with code {process.ExitCode}: {scriptPath}{Environment.NewLine}" +
                $"STDERR:{Environment.NewLine}{stderr}{Environment.NewLine}" +
                $"STDOUT:{Environment.NewLine}{stdout}");
        }
    }

    private static ViscousSolverEngine.PreNewtonSetupContext BuildContext()
    {
        return BuildContext(CaseId);
    }

    private static ViscousSolverEngine.PreNewtonSetupContext BuildContext(string caseId)
    {
        FortranReferenceCase definition = FortranReferenceCases.Get(caseId);
        AnalysisSettings settings = BuildAnalysisSettings(definition);
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

        return ViscousSolverEngine.PrepareLegacyPreNewtonContext((x, y), settings, alphaRadians);
    }

    private static AnalysisSettings BuildAnalysisSettings(FortranReferenceCase definition)
    {
        return new AnalysisSettings(
            panelCount: definition.PanelCount,
            reynoldsNumber: definition.ReynoldsNumber,
            machNumber: 0.0,

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
    }

    private static void AssertTermParity(ParityTraceRecord expected, ParityTraceRecord actual, int index)
    {
        string prefix = $"term[{index}]";

        AssertIntField(expected, actual, "side", prefix);
        AssertIntField(expected, actual, "station", prefix);
        AssertIntField(expected, actual, "sourceSide", prefix);
        AssertIntField(expected, actual, "sourceStation", prefix);
        AssertIntField(expected, actual, "iPan", prefix);
        AssertIntField(expected, actual, "jPan", prefix);
        AssertFloatField(expected, actual, "vtiI", prefix);
        AssertFloatField(expected, actual, "vtiJ", prefix);
        AssertFloatField(expected, actual, "dij", prefix);
        AssertFloatField(expected, actual, "mass", prefix);
        AssertFloatField(expected, actual, "ueM", prefix);
        AssertFloatField(expected, actual, "contribution", prefix);
        Assert.Equal(ReadBooleanish(expected, "isWakeSource"), ReadBooleanish(actual, "isWakeSource"));
    }

    private static void AssertFinalParity(ParityTraceRecord expected, ParityTraceRecord actual)
    {
        const string prefix = "final";

        AssertIntField(expected, actual, "side", prefix);
        AssertIntField(expected, actual, "station", prefix);
        AssertFloatField(expected, actual, "ueInv", prefix);
        AssertFloatField(expected, actual, "airfoilContribution", prefix);
        AssertFloatField(expected, actual, "wakeContribution", prefix);
        AssertFloatField(expected, actual, "predicted", prefix);
    }

    private static PredictedEdgeVelocityBlock SelectFirstPredictedEdgeVelocityBlock(
        IEnumerable<ParityTraceRecord> records,
        int side,
        int station)
    {
        List<ParityTraceRecord> relevant = records
            .Where(record => (record.Kind == "predicted_edge_velocity_term" || record.Kind == "predicted_edge_velocity") &&
                             HasExactDataInt(record, "side", side) &&
                             HasExactDataInt(record, "station", station))
            .OrderBy(record => record.Sequence)
            .ToList();
        ParityTraceRecord final = relevant
            .First(record => record.Kind == "predicted_edge_velocity");
        long previousFinalSequence = relevant
            .Where(record => record.Kind == "predicted_edge_velocity" && record.Sequence < final.Sequence)
            .Select(record => record.Sequence)
            .DefaultIfEmpty(long.MinValue)
            .Max();
        IReadOnlyList<ParityTraceRecord> terms = relevant
            .Where(record => record.Kind == "predicted_edge_velocity_term" &&
                             record.Sequence > previousFinalSequence &&
                             record.Sequence < final.Sequence)
            .ToArray();

        return new PredictedEdgeVelocityBlock(terms, final);
    }

    private static void AssertIntField(ParityTraceRecord expected, ParityTraceRecord actual, string field, string context)
    {
        AssertHex(GetIntHex(expected, field), GetIntHex(actual, field), $"{context} {field}");
    }

    private static void AssertFloatField(ParityTraceRecord expected, ParityTraceRecord actual, string field, string context)
    {
        AssertHex(GetFloatHex(expected, field), GetFloatHex(actual, field), $"{context} {field}");
    }

    private static void AssertStringField(ParityTraceRecord expected, ParityTraceRecord actual, string field, string context)
    {
        Assert.Equal(
            ReadString(expected, field),
            ReadString(actual, field));
    }

    private static string GetLatestTracePath(string directory)
    {
        return FortranParityArtifactLocator.GetLatestReferenceTracePath(directory);
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

    private static bool HasExactDataInt(ParityTraceRecord record, string field, int expected)
    {
        return record.TryGetDataField(field, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out int actual) &&
               actual == expected;
    }

    private static IReadOnlyList<ParityTraceRecord> ParseObservedRecords(IEnumerable<string> lines)
    {
        return lines
            .Select(ParityTraceLoader.DeserializeLine)
            .Where(static record => record is not null)
            .Select(static record => record!)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> GetOrderedUpperSideRecords(string tracePath, params string[] kinds)
    {
        HashSet<string> kindSet = new(kinds, StringComparer.Ordinal);
        return ParityTraceLoader.ReadMatching(
                tracePath,
                record => kindSet.Contains(record.Kind) &&
                          HasExactDataInt(record, "side", 1))
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> GetOrderedUpperSideRecords(
        IEnumerable<ParityTraceRecord> records,
        params string[] kinds)
    {
        HashSet<string> kindSet = new(kinds, StringComparer.Ordinal);
        return records
            .Where(record => kindSet.Contains(record.Kind) &&
                             HasExactDataInt(record, "side", 1))
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> GetFirstUpperSideRecordPerStation(
        string tracePath,
        params string[] kinds)
    {
        return GetFirstUpperSideRecordPerStation(
            ParityTraceLoader.ReadMatching(tracePath, static _ => true),
            kinds);
    }

    private static IReadOnlyList<ParityTraceRecord> GetFirstUpperSideRecordPerStation(
        IEnumerable<ParityTraceRecord> records,
        params string[] kinds)
    {
        HashSet<string> kindSet = new(kinds, StringComparer.Ordinal);
        return records
            .Where(record => kindSet.Contains(record.Kind) &&
                             HasExactDataInt(record, "side", 1))
            .GroupBy(ReadStation)
            .Select(group => group.OrderBy(static record => record.Sequence).First())
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> GetOrderedStationRecords(
        string tracePath,
        int station,
        params string[] kinds)
    {
        return GetOrderedStationRecords(
            ParityTraceLoader.ReadMatching(tracePath, static _ => true),
            station,
            kinds);
    }

    private static IReadOnlyList<ParityTraceRecord> GetOrderedStationRecords(
        IEnumerable<ParityTraceRecord> records,
        int station,
        params string[] kinds)
    {
        HashSet<string> kindSet = new(kinds, StringComparer.Ordinal);
        return records
            .Where(record => kindSet.Contains(record.Kind) &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", station))
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> GetOrderedStationRecordsBeforeFinal(
        string tracePath,
        int station,
        params string[] kinds)
    {
        return GetOrderedStationRecordsBeforeFinal(
            ParityTraceLoader.ReadMatching(tracePath, static _ => true),
            station,
            kinds);
    }

    private static IReadOnlyList<ParityTraceRecord> GetOrderedStationRecordsBeforeFinal(
        IEnumerable<ParityTraceRecord> records,
        int station,
        params string[] kinds)
    {
        ParityTraceRecord finalRecord = records
            .Where(record => record.Kind == "laminar_seed_final" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", station))
            .OrderBy(static record => record.Sequence)
            .First();

        HashSet<string> kindSet = new(kinds, StringComparer.Ordinal);
        return records
            .Where(record => kindSet.Contains(record.Kind) &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", station) &&
                             record.Sequence < finalRecord.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static void AssertOrderedFieldParity(
        IReadOnlyList<ParityTraceRecord> expectedRecords,
        IReadOnlyList<ParityTraceRecord> actualRecords,
        string field,
        string context)
    {
        Assert.Equal(expectedRecords.Count, actualRecords.Count);

        for (int i = 0; i < expectedRecords.Count; i++)
        {
            ParityTraceRecord expected = expectedRecords[i];
            ParityTraceRecord actual = actualRecords[i];
            int station = ReadStation(expected);
            string iterationSuffix = TryReadIntField(expected, "iteration", out int iteration)
                ? $" iteration {iteration}"
                : string.Empty;

            AssertIntField(expected, actual, "station", $"{context} index {i}");
            AssertFloatField(expected, actual, field, $"{context} station {station}{iterationSuffix}");
        }
    }

    private static int ReadStation(ParityTraceRecord record)
    {
        Assert.True(record.TryGetDataField("station", out JsonElement value), $"Missing station field in {record.Kind}.");
        Assert.True(value.ValueKind == JsonValueKind.Number, $"Station field in {record.Kind} was not numeric.");
        Assert.True(value.TryGetInt32(out int station), $"Station field in {record.Kind} was not an int.");
        return station;
    }

    private static bool TryReadIntField(ParityTraceRecord record, string field, out int value)
    {
        value = default;
        return record.TryGetDataField(field, out JsonElement element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out value);
    }

    private static string GetIntHex(ParityTraceRecord record, string field)
    {
        if (record.TryGetDataBits(field, out IReadOnlyDictionary<string, string>? bits) &&
            bits is not null &&
            bits.TryGetValue("i32", out string? hex))
        {
            return hex!;
        }

        Assert.True(record.TryGetDataField(field, out JsonElement value), $"Missing data field '{field}' in {record.Kind}.");
        Assert.True(value.ValueKind == JsonValueKind.Number, $"Field '{field}' in {record.Kind} was not numeric.");
        return $"0x{value.GetInt32():X8}";
    }

    private static string GetFloatHex(ParityTraceRecord record, string field)
    {
        if (record.TryGetDataBits(field, out IReadOnlyDictionary<string, string>? bits) &&
            bits is not null &&
            bits.TryGetValue("f32", out string? hex))
        {
            return hex!;
        }

        Assert.True(record.TryGetDataField(field, out JsonElement value), $"Missing data field '{field}' in {record.Kind}.");
        Assert.True(value.ValueKind == JsonValueKind.Number, $"Field '{field}' in {record.Kind} was not numeric.");
        return ToHex((float)value.GetDouble());
    }

    private static double GetFloatValue(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out JsonElement value), $"Missing data field '{field}' in {record.Kind}.");
        Assert.True(value.ValueKind == JsonValueKind.Number, $"Field '{field}' in {record.Kind} was not numeric.");
        return value.GetDouble();
    }

    private static bool ReadBooleanish(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out JsonElement value), $"Missing data field '{field}' in {record.Kind}.");
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDouble() != 0.0,
            _ => throw new InvalidOperationException($"Field '{field}' in {record.Kind} was not boolean-compatible.")
        };
    }

    private static string ReadStringField(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out JsonElement value), $"Missing data field '{field}' in {record.Kind}.");
        Assert.True(value.ValueKind == JsonValueKind.String, $"Field '{field}' in {record.Kind} was not a string.");
        return value.GetString() ?? string.Empty;
    }

    private static ParityTraceRecord SelectKinematicForSystem(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system)
    {
        string targetUeiHex = GetFloatHex(system, "uei");
        return records
            .Where(record => record.Sequence < system.Sequence)
            .Where(record => GetIntHex(record, "u2Bits") == targetUeiHex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectLastRecordBeforeSystem(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system)
    {
        int? side = TryReadInt(system, "side");
        int? station = TryReadInt(system, "station");

        return records
            .Where(record => record.Sequence < system.Sequence)
            .Where(record =>
                side is null ||
                !TryReadInt(record, "side").HasValue ||
                TryReadInt(record, "side") == side)
            .Where(record =>
                station is null ||
                (!TryReadInt(record, "station").HasValue && !TryReadInt(record, "intervalStation").HasValue) ||
                TryReadInt(record, "station") == station ||
                TryReadInt(record, "intervalStation") == station)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectCfTermsForSystem(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord kinematic,
        ParityTraceRecord system)
    {
        return records
            .Where(record => record.Sequence > kinematic.Sequence && record.Sequence < system.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectCompressibleForKinematic(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord kinematic)
    {
        string targetU2Hex = GetIntHex(kinematic, "u2Bits");
        return records
            .Where(record => record.Sequence < kinematic.Sequence)
            .Where(record => GetIntHex(record, "u2Bits") == targetU2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectCompressibleForStepHandoff(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord step,
        ParityTraceRecord nextSystem)
    {
        string targetU2Hex = GetFloatHex(nextSystem, "uei");
        return records
            .Where(record => record.Sequence > step.Sequence && record.Sequence < nextSystem.Sequence)
            .Where(record => GetIntHex(record, "u2Bits") == targetU2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectCompressibleForSystemHandoff(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system,
        IReadOnlyList<ParityTraceRecord> stationSystems)
    {
        string targetU2Hex = GetFloatHex(system, "uei");
        long nextSystemSequence = stationSystems
            .Where(record => record.Sequence > system.Sequence)
            .Select(static record => record.Sequence)
            .DefaultIfEmpty(long.MaxValue)
            .Min();

        return records
            .Where(record => record.Sequence > system.Sequence && record.Sequence < nextSystemSequence)
            .Where(record => GetIntHex(record, "u2Bits") == targetU2Hex)
            .OrderBy(static record => record.Sequence)
            .First();
    }

    private static ParityTraceRecord SelectSecondaryForSystem(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system,
        int ityp,
        int station)
    {
        return records
            .Where(record => record.Sequence < system.Sequence)
            .Where(record => HasExactDataInt(record, "ityp", ityp))
            .Where(record => HasExactDataInt(record, "station", station))
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectPrimaryForSystem(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system,
        int ityp,
        int station)
    {
        return records
            .Where(record => record.Sequence < system.Sequence)
            .Where(record => HasExactDataInt(record, "ityp", ityp))
            .Where(record => HasExactDataInt(record, "station", station))
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectPrimaryForInputs(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system,
        ParityTraceRecord inputs,
        int ityp,
        int station)
    {
        string targetX = GetFloatHex(inputs, station == 1 ? "x1" : "x2");
        string targetU = GetFloatHex(inputs, station == 1 ? "u1" : "u2");
        string targetT = GetFloatHex(inputs, station == 1 ? "t1" : "t2");
        string targetD = GetFloatHex(inputs, station == 1 ? "d1" : "d2");

        return records
            .Where(record => record.Sequence < system.Sequence)
            .Where(record => HasExactDataInt(record, "ityp", ityp))
            .Where(record => HasExactDataInt(record, "station", station))
            .Where(record => GetFloatHex(record, "x") == targetX)
            .Where(record => GetFloatHex(record, "u") == targetU)
            .Where(record => GetFloatHex(record, "t") == targetT)
            .Where(record => GetFloatHex(record, "d") == targetD)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectKinematicForSecondary(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord secondary)
    {
        string targetRtTHex = GetFloatHex(secondary, "rtT");
        return records
            .Where(record => record.Sequence < secondary.Sequence)
            .Where(record => GetFloatHex(record, "rT2_t2") == targetRtTHex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectLastRecordBefore(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord beforeRecord)
    {
        return records
            .Where(record => record.Sequence < beforeRecord.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static void AssertRecordDataParity(
        ParityTraceRecord reference,
        ParityTraceRecord managed,
        string context,
        params string[] ignoredFields)
    {
        HashSet<string> ignored = ignoredFields.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(ignoredFields, StringComparer.Ordinal);

        foreach (JsonProperty property in reference.Data.EnumerateObject())
        {
            string field = property.Name;
            if (ignored.Contains(field))
            {
                continue;
            }

            Assert.True(managed.TryGetDataField(field, out JsonElement managedValue), $"Missing data field '{field}' in managed {managed.Kind}.");

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (reference.TryGetDataBits(field, out IReadOnlyDictionary<string, string>? bits) &&
                        bits is not null &&
                        bits.ContainsKey("i32"))
                    {
                        AssertIntField(reference, managed, field, context);
                    }
                    else if (reference.TryGetDataBits(field, out bits) &&
                             bits is not null &&
                             bits.ContainsKey("f32"))
                    {
                        AssertFloatField(reference, managed, field, context);
                    }
                    else if (property.Value.TryGetInt32(out _) && managedValue.TryGetInt32(out _))
                    {
                        AssertIntField(reference, managed, field, context);
                    }
                    else
                    {
                        AssertFloatField(reference, managed, field, context);
                    }

                    break;

                case JsonValueKind.String:
                    AssertStringField(reference, managed, field, context);
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    Assert.Equal(property.Value.GetBoolean(), managedValue.GetBoolean());
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported parity field '{field}' kind '{property.Value.ValueKind}' in {reference.Kind}.");
            }
        }
    }

    private static ParityTraceRecord SelectKinematicForConstraint(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord constraint)
    {
        string targetU2Hex = GetFloatHex(constraint, "currentU2");
        IReadOnlyList<ParityTraceRecord> priorKinematics = records
            .Where(record => record.Sequence < constraint.Sequence)
            .OrderBy(static record => record.Sequence)
            .ToArray();

        Assert.NotEmpty(priorKinematics);

        ParityTraceRecord? exact = priorKinematics
            .Where(record => GetIntHex(record, "u2Bits") == targetU2Hex)
            .LastOrDefault();
        if (exact is not null)
        {
            return exact;
        }

        double targetU2 = GetFloatValue(constraint, "currentU2");
        return priorKinematics
            .OrderBy(record => Math.Abs(GetFloatValue(record, "u2") - targetU2))
            .ThenByDescending(record => record.Sequence)
            .First();
    }

    private static ParityTraceRecord SelectTurbulentDiTermsForSecondary(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord secondary)
    {
        string targetFinalDiHex = GetFloatHex(secondary, "di");
        return records
            .Where(record => record.Sequence < secondary.Sequence)
            .Where(record => GetFloatHex(record, "finalDi") == targetFinalDiHex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectOuterDiTermsForSecondary(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord secondary)
    {
        int? station = TryReadInt(secondary, "station");
        string targetHsTHex = GetFloatHex(secondary, "hsT");
        string targetUsTHex = GetFloatHex(secondary, "usT");
        string targetRtTHex = GetFloatHex(secondary, "rtT");

        return records
            .Where(record => record.Sequence < secondary.Sequence)
            .Where(static record => HasExactDataInt(record, "ityp", 2))
            .Where(record => station is null || HasExactDataInt(record, "station", station.Value))
            .Where(record => GetFloatHex(record, "hsT") == targetHsTHex)
            .Where(record => GetFloatHex(record, "usT") == targetUsTHex)
            .Where(record => GetFloatHex(record, "rtT") == targetRtTHex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectBlkinInputsForKinematic(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord kinematic)
    {
        return records
            .Where(record => record.Sequence < kinematic.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectTransitionIntervalInputsForSystem(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system)
    {
        int? station = TryReadInt(system, "station");
        int? iteration = TryReadInt(system, "iteration");

        return records
            .Where(record => station is null || !TryReadInt(record, "station").HasValue || TryReadInt(record, "station") == station)
            .Where(record => iteration is null || !TryReadInt(record, "iteration").HasValue || TryReadInt(record, "iteration") == iteration)
            .Where(record => record.Sequence < system.Sequence)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectTransitionPointIterationForInterval(
        IEnumerable<ParityTraceRecord> records,
        string x1Hex,
        string x2Hex)
    {
        return records
            .Where(record => GetFloatHex(record, "x1") == x1Hex)
            .Where(record => GetFloatHex(record, "x2") == x2Hex)
            .Where(static record =>
                !record.TryGetDataField("iteration", out JsonElement iterationValue) ||
                (iterationValue.ValueKind == JsonValueKind.Number &&
                 iterationValue.TryGetInt32(out int iteration) &&
                 iteration == 1))
            .OrderBy(static record => record.Sequence)
            .First();
    }

    private static ParityTraceRecord SelectAcceptedTransitionPointIterationForSystem(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system,
        string x1Hex,
        string x2Hex)
    {
        int? station = TryReadInt(system, "station");
        int? iteration = TryReadInt(system, "iteration");

        return records
            .Where(record => record.Sequence < system.Sequence)
            .Where(record => station is null || !TryReadInt(record, "station").HasValue || TryReadInt(record, "station") == station)
            .Where(record => iteration is null || !TryReadInt(record, "stationIteration").HasValue || TryReadInt(record, "stationIteration") == iteration)
            .Where(record => GetFloatHex(record, "x1") == x1Hex)
            .Where(record => GetFloatHex(record, "x2") == x2Hex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static ParityTraceRecord SelectTransitionIntervalInputsForAcceptedIteration(
        IEnumerable<ParityTraceRecord> records,
        ParityTraceRecord system,
        ParityTraceRecord acceptedIteration)
    {
        int? station = TryReadInt(system, "station");
        int? iteration = TryReadInt(system, "iteration");
        string x1OriginalHex = GetFloatHex(acceptedIteration, "x1");
        string x2Hex = GetFloatHex(acceptedIteration, "x2");
        string xtHex = GetFloatHex(acceptedIteration, "xt");

        return records
            .Where(record => record.Sequence < system.Sequence)
            .Where(record => station is null || !TryReadInt(record, "station").HasValue || TryReadInt(record, "station") == station)
            .Where(record => iteration is null || !TryReadInt(record, "iteration").HasValue || TryReadInt(record, "iteration") == iteration)
            .Where(record => GetFloatHex(record, "x1Original") == x1OriginalHex)
            .Where(record => GetFloatHex(record, "x2") == x2Hex)
            .Where(record => GetFloatHex(record, "xt") == xtHex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static int? TryReadInt(ParityTraceRecord record, string field)
    {
        if (!record.TryGetDataField(field, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int number))
        {
            return null;
        }

        return number;
    }

    private static string? ReadString(ParityTraceRecord record, string field)
    {
        if (!record.TryGetDataField(field, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static ParityTraceRecord SelectKinematicByPrimaryState(
        IEnumerable<ParityTraceRecord> records,
        string uHex,
        string tHex,
        string dHex)
    {
        return records
            .Where(record => GetIntHex(record, "u2Bits") == uHex)
            .Where(record => GetIntHex(record, "t2Bits") == tHex)
            .Where(record => GetIntHex(record, "d2Bits") == dHex)
            .OrderBy(static record => record.Sequence)
            .Last();
    }

    private static string ToHex(float value)
        => $"0x{BitConverter.SingleToInt32Bits(value):X8}";

    private static float MultiplyFloat32(float left, float right)
        => (float)((float)left * (float)right);

    private static float AddFloat32(float left, float right)
        => (float)((float)left + (float)right);

    private static CfChainTraceResult InvokeCfChains(ParityTraceRecord kinematic, int flowType)
    {
        MethodInfo method = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeCfChains",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BoundaryLayerSystemAssembler.ComputeCfChains was not found.");

        object?[] args =
        {
            flowType,
            GetFloatValue(kinematic, "hK2"),
            GetFloatValue(kinematic, "rT2"),
            GetFloatValue(kinematic, "m2"),
            GetFloatValue(kinematic, "hK2_t2"),
            GetFloatValue(kinematic, "hK2_d2"),
            GetFloatValue(kinematic, "hK2_u2"),
            GetFloatValue(kinematic, "hK2_ms"),
            GetFloatValue(kinematic, "rT2_t2"),
            GetFloatValue(kinematic, "rT2_u2"),
            GetFloatValue(kinematic, "rT2_ms"),
            GetFloatValue(kinematic, "m2_u2"),
            GetFloatValue(kinematic, "m2_ms"),
            GetFloatValue(kinematic, "rT2_re"),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            true
        };

        method.Invoke(null, args);

        return new CfChainTraceResult(
            (int)args[14]!,
            ToHex((float)(double)args[15]!),
            ToHex((float)(double)args[16]!),
            ToHex((float)(double)args[17]!),
            ToHex((float)(double)args[18]!),
            ToHex((float)(double)args[19]!),
            ToHex((float)(double)args[20]!),
            ToHex((float)(double)args[21]!),
            ToHex((float)(double)args[22]!),
            ToHex((float)(double)args[23]!));
    }

    private static KinematicTraceResult InvokeKinematic(ParityTraceRecord blkinInputs)
    {
        KinematicResult result = BoundaryLayerSystemAssembler.ComputeKinematicParameters(
            GetFloatValue(blkinInputs, "u2"),
            GetFloatValue(blkinInputs, "t2"),
            GetFloatValue(blkinInputs, "d2"),
            GetFloatValue(blkinInputs, "dw2"),
            GetFloatValue(blkinInputs, "hstinv"),
            GetFloatValue(blkinInputs, "hstinv_ms"),
            GetFloatValue(blkinInputs, "gm1bl"),
            GetFloatValue(blkinInputs, "rstbl"),
            GetFloatValue(blkinInputs, "rstbl_ms"),
            GetFloatValue(blkinInputs, "hvrat"),
            GetFloatValue(blkinInputs, "reybl"),
            GetFloatValue(blkinInputs, "reybl_re"),
            GetFloatValue(blkinInputs, "reybl_ms"),
            true);

        return new KinematicTraceResult(
            ToHex((float)GetFloatValue(blkinInputs, "u2")),
            ToHex((float)GetFloatValue(blkinInputs, "t2")),
            ToHex((float)GetFloatValue(blkinInputs, "d2")),
            ToHex((float)result.M2),
            ToHex((float)result.R2),
            ToHex((float)result.H2),
            ToHex((float)result.HK2),
            ToHex((float)result.HK2_T2),
            ToHex((float)result.HK2_D2),
            ToHex((float)result.RT2),
            ToHex((float)result.RT2_U2),
            ToHex((float)result.RT2_T2),
            ToHex((float)result.RT2_MS),
            ToHex((float)result.RT2_RE));
    }

    private static void AssertHex(string expected, string actual, string context)
    {
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{context} expected={expected} actual={actual}");
    }

    private sealed record CfChainTraceResult(
        int SelectedBranch,
        string CfHex,
        string CfHkHex,
        string CfRtHex,
        string CfMHex,
        string CfTHex,
        string CfDHex,
        string CfUHex,
        string CfMsHex,
        string CfReHex);

    private sealed record KinematicTraceResult(
        string U2BitsHex,
        string T2BitsHex,
        string D2BitsHex,
        string M2Hex,
        string R2Hex,
        string H2BitsHex,
        string HK2BitsHex,
        string HK2T2Hex,
        string HK2D2Hex,
        string RT2BitsHex,
        string RT2U2Hex,
        string RT2T2Hex,
        string RT2MsHex,
        string RT2ReHex);

    private sealed record ManagedTraceResult(
        IReadOnlyList<ParityTraceRecord> Terms,
        ParityTraceRecord Final,
        double[,] Usav,
        string ContextStation2MassHex,
        ParityTraceRecord LegacyRemarchStation16Iteration,
        ParityTraceRecord LegacyRemarchStation16Final);

    private sealed record ManagedPredictedEdgeVelocityTraceResult(
        IReadOnlyList<ParityTraceRecord> Terms,
        ParityTraceRecord Final,
        double[,] Usav,
        string ContextStationMassHex);

    private sealed record LegacyRemarchStationBlock(
        ParityTraceRecord Constraint,
        ParityTraceRecord Iteration,
        ParityTraceRecord System,
        ParityTraceRecord Delta,
        ParityTraceRecord Final);

    private sealed record ManagedLegacyRemarchStationTraceResult(
        LegacyRemarchStationBlock Block,
        string ContextStationMassHex,
        IReadOnlyList<ParityTraceRecord> Records);

    private sealed record PredictedEdgeVelocityBlock(
        IReadOnlyList<ParityTraceRecord> Terms,
        ParityTraceRecord Final);

    private sealed record ManagedTraceArtifacts(
        ViscousSolverEngine.PreNewtonSetupContext Context,
        IReadOnlyList<ParityTraceRecord> Records,
        double[,] Usav);
}
