using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using XFoil.Core.Services;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Models;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;
using Xunit;
using Xunit.Sdk;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: UESET/SETBL pre-Newton USAV reconstruction
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_setbl_vdel_s1_i2_ref/reference_dump.*.txt authoritative focused dump
// Role in port: Proves the live pre-first-Newton USAV wake reconstruction against the authoritative focused Fortran dump so wake-influence bugs can be isolated without rerunning the full viscous solve.
// Differences: Classic XFoil had no managed micro-engine for this boundary; the test reuses the managed pre-Newton setup helper and compares its station-2 wake split to the recorded legacy dump.
// Decision: Keep this micro-test because it turns a whole-run divergence into a fast, deterministic oracle on the exact pre-Newton wake term that currently diverges.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class PreNewtonWakeUsavParityTests
{
    private const string CaseId = "n0012_re1e6_a10_p80";
    private const int ClassicXFoilNacaPointCount = 239;
    private static readonly string ReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_setbl_vdel_s1_i2_ref");
    private static readonly string PredictedEdgeVelocityReferenceDirectory = Path.Combine(
        FortranReferenceCases.GetFortranDebugDirectory(),
        "reference",
        "alpha10_p80_ueset_s1st2_ref4");

    private static readonly Regex UsavSplitRegex = new(
        @"USAV_SPLIT IS=\s*1 IBL=\s*2 UINV=\s*(?<uinv>[-+0-9.E]+)(?:\s*\[(?<uinvBits>[0-9A-F]{8})\])? AIR=\s*(?<air>[-+0-9.E]+)(?:\s*\[(?<airBits>[0-9A-F]{8})\])? WAKE=\s*(?<wake>[-+0-9.E]+)(?:\s*\[(?<wakeBits>[0-9A-F]{8})\])? USAV=\s*(?<usav>[-+0-9.E]+)(?:\s*\[(?<usavBits>[0-9A-F]{8})\])?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UsavWakeTermRegex = new(
        @"USAV_WAKE_TERM IS=\s*1 IBL=\s*2 JS=\s*2 JBL=\s*35 J(?:PAN)?=\s*(?<j>\d+) UE_M=\s*(?<ueM>[-+0-9.E]+)(?:\s*\[(?<ueMBits>[0-9A-F]{8})\])? MASS=\s*(?<mass>[-+0-9.E]+)(?:\s*\[(?<massBits>[0-9A-F]{8})\])? CONTR=\s*(?<contr>[-+0-9.E]+)(?:\s*\[(?<contrBits>[0-9A-F]{8})\])?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlStateRegex = new(
        @"BL_STATE x=\s*(?<x>[-+0-9.E]+)(?:\s*\[(?<xBits>[0-9A-F]{8})\])? Ue=\s*(?<ue>[-+0-9.E]+)(?:\s*\[(?<ueBits>[0-9A-F]{8})\])? th=\s*(?<th>[-+0-9.E]+)(?:\s*\[(?<thBits>[0-9A-F]{8})\])? ds=\s*(?<ds>[-+0-9.E]+)(?:\s*\[(?<dsBits>[0-9A-F]{8})\])? m=\s*(?<mass>[-+0-9.E]+)(?:\s*\[(?<massBits>[0-9A-F]{8})\])?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Alpha10_P80_PreNewtonUpperStation2_State_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 1, station: 2);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        string firstMismatch = DescribeFirstBoundaryLayerMismatch(blState);

        AssertExactSingleFromDump("upper station-2 Ue", reference.Ue, blState.UEDG[1, 0], firstMismatch);
        AssertExactSingleFromDump("upper station-2 theta", reference.Theta, blState.THET[1, 0], firstMismatch);
        AssertExactSingleFromDump("upper station-2 dstar", reference.Dstar, blState.DSTR[1, 0], firstMismatch);
        AssertExactSingleFromDump("upper station-2 mass", reference.Mass, blState.MASS[1, 0], firstMismatch);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonUpperTeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 1, station: 48);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[0];
        string firstMismatch = DescribeFirstBoundaryLayerMismatch(blState);

        AssertExactSingleFromDump("upper TE Ue", reference.Ue, blState.UEDG[ibl, 0], firstMismatch);
        AssertExactSingleFromDump("upper TE theta", reference.Theta, blState.THET[ibl, 0], firstMismatch);
        AssertExactSingleFromDump("upper TE dstar", reference.Dstar, blState.DSTR[ibl, 0], firstMismatch);
        AssertExactSingleFromDump("upper TE mass", reference.Mass, blState.MASS[ibl, 0], firstMismatch);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonLowerTeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 34);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[1];

        AssertExactSingleFromDump("lower TE Ue", reference.Ue, blState.UEDG[ibl, 1]);
        AssertExactSingleFromDump("lower TE theta", reference.Theta, blState.THET[ibl, 1]);
        AssertExactSingleFromDump("lower TE dstar", reference.Dstar, blState.DSTR[ibl, 1]);
        AssertExactSingleFromDump("lower TE mass", reference.Mass, blState.MASS[ibl, 1]);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonFirstLowerWakeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 35);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[1] + 1;

        AssertExactSingleFromDump("first lower wake Ue", reference.Ue, blState.UEDG[ibl, 1]);
        AssertExactSingleFromDump("first lower wake theta", reference.Theta, blState.THET[ibl, 1]);
        AssertExactSingleFromDump("first lower wake dstar", reference.Dstar, blState.DSTR[ibl, 1]);
        AssertExactSingleFromDump("first lower wake mass", reference.Mass, blState.MASS[ibl, 1]);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonSecondLowerWakeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 36);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[1] + 2;

        AssertExactSingleFromDump("second lower wake Ue", reference.Ue, blState.UEDG[ibl, 1]);
        AssertExactSingleFromDump("second lower wake theta", reference.Theta, blState.THET[ibl, 1]);
        AssertExactSingleFromDump("second lower wake dstar", reference.Dstar, blState.DSTR[ibl, 1]);
        AssertExactSingleFromDump("second lower wake mass", reference.Mass, blState.MASS[ibl, 1]);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonSecondLowerWakeRemarchFinalTrace_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 36);
        ParityTraceRecord record = RunManagedSetBlTrace("legacy_seed_final")
            .Single(trace =>
                trace.Kind == "legacy_seed_final" &&
                HasExactDataInt(trace, "side", 2) &&
                HasExactDataInt(trace, "station", 36));

        AssertExactSingleFromDump("second lower wake remarch Ue", reference.Ue, ReadTraceSingle(record, "uei"));
        AssertExactSingleFromDump("second lower wake remarch theta", reference.Theta, ReadTraceSingle(record, "theta"));
        AssertExactSingleFromDump("second lower wake remarch dstar", reference.Dstar, ReadTraceSingle(record, "dstar"));
        AssertExactSingleFromDump("second lower wake remarch mass", reference.Mass, ReadTraceSingle(record, "mass"));
    }

    [Fact]
    public void Alpha10_P80_PreNewtonThirdLowerWakeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 37);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[1] + 3;

        AssertExactSingleFromDump("third lower wake Ue", reference.Ue, blState.UEDG[ibl, 1]);
        AssertExactSingleFromDump("third lower wake theta", reference.Theta, blState.THET[ibl, 1]);
        AssertExactSingleFromDump("third lower wake dstar", reference.Dstar, blState.DSTR[ibl, 1]);
        AssertExactSingleFromDump("third lower wake mass", reference.Mass, blState.MASS[ibl, 1]);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonFourthLowerWakeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 38);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[1] + 4;

        AssertExactSingleFromDump("fourth lower wake Ue", reference.Ue, blState.UEDG[ibl, 1]);
        AssertExactSingleFromDump("fourth lower wake theta", reference.Theta, blState.THET[ibl, 1]);
        AssertExactSingleFromDump("fourth lower wake dstar", reference.Dstar, blState.DSTR[ibl, 1]);
        AssertExactSingleFromDump("fourth lower wake mass", reference.Mass, blState.MASS[ibl, 1]);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonFourthLowerWakeRemarchFinalTrace_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 38);
        ParityTraceRecord record = RunManagedSetBlTrace("legacy_seed_final")
            .Single(trace =>
                trace.Kind == "legacy_seed_final" &&
                HasExactDataInt(trace, "side", 2) &&
                HasExactDataInt(trace, "station", 38));

        AssertExactSingleFromDump("fourth lower wake remarch Ue", reference.Ue, ReadTraceSingle(record, "uei"));
        AssertExactSingleFromDump("fourth lower wake remarch theta", reference.Theta, ReadTraceSingle(record, "theta"));
        AssertExactSingleFromDump("fourth lower wake remarch dstar", reference.Dstar, ReadTraceSingle(record, "dstar"));
        AssertExactSingleFromDump("fourth lower wake remarch mass", reference.Mass, ReadTraceSingle(record, "mass"));
    }

    [Fact]
    public void Alpha10_P80_PreNewtonFifthLowerWakeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 39);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[1] + 5;

        AssertExactSingleFromDump("fifth lower wake Ue", reference.Ue, blState.UEDG[ibl, 1]);
        AssertExactSingleFromDump("fifth lower wake theta", reference.Theta, blState.THET[ibl, 1]);
        AssertExactSingleFromDump("fifth lower wake dstar", reference.Dstar, blState.DSTR[ibl, 1]);
        AssertExactSingleFromDump("fifth lower wake mass", reference.Mass, blState.MASS[ibl, 1]);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonFifthLowerWakeRemarchFinalTrace_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 39);
        ParityTraceRecord record = RunManagedSetBlTrace("legacy_seed_final")
            .Single(trace =>
                trace.Kind == "legacy_seed_final" &&
                HasExactDataInt(trace, "side", 2) &&
                HasExactDataInt(trace, "station", 39));

        AssertExactSingleFromDump("fifth lower wake remarch Ue", reference.Ue, ReadTraceSingle(record, "uei"));
        AssertExactSingleFromDump("fifth lower wake remarch theta", reference.Theta, ReadTraceSingle(record, "theta"));
        AssertExactSingleFromDump("fifth lower wake remarch dstar", reference.Dstar, ReadTraceSingle(record, "dstar"));
        AssertExactSingleFromDump("fifth lower wake remarch mass", reference.Mass, ReadTraceSingle(record, "mass"));
    }

    [Fact]
    public void Alpha10_P80_PreNewtonTenthLowerWakeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 44);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[1] + 10;
        AssertStationStateMatchesReference(
            "tenth lower wake state",
            reference,
            blState.UEDG[ibl, 1],
            blState.THET[ibl, 1],
            blState.DSTR[ibl, 1],
            blState.MASS[ibl, 1]);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonTenthLowerWakeRemarchFinalTrace_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 44);
        ParityTraceRecord record = RunManagedSetBlTrace("legacy_seed_final")
            .Single(trace =>
                trace.Kind == "legacy_seed_final" &&
                HasExactDataInt(trace, "side", 2) &&
                HasExactDataInt(trace, "station", 44));
        AssertStationStateMatchesReference(
            "tenth lower wake remarch",
            reference,
            ReadTraceSingle(record, "uei"),
            ReadTraceSingle(record, "theta"),
            ReadTraceSingle(record, "dstar"),
            ReadTraceSingle(record, "mass"));
    }

    [Fact]
    public void Alpha10_P80_PreNewtonTwelfthLowerWakeState_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 46);
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;
        int ibl = blState.IBLTE[1] + 12;
        AssertStationStateMatchesReference(
            "twelfth lower wake state",
            reference,
            blState.UEDG[ibl, 1],
            blState.THET[ibl, 1],
            blState.DSTR[ibl, 1],
            blState.MASS[ibl, 1]);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonTwelfthLowerWakeRemarchFinalTrace_MatchesReferenceDump()
    {
        ReferenceStationState reference = ReadReferenceStationState(surface: 2, station: 46);
        ParityTraceRecord record = RunManagedSetBlTrace("legacy_seed_final")
            .Single(trace =>
                trace.Kind == "legacy_seed_final" &&
                HasExactDataInt(trace, "side", 2) &&
                HasExactDataInt(trace, "station", 46));
        AssertStationStateMatchesReference(
            "twelfth lower wake remarch",
            reference,
            ReadTraceSingle(record, "uei"),
            ReadTraceSingle(record, "theta"),
            ReadTraceSingle(record, "dstar"),
            ReadTraceSingle(record, "mass"));
    }

    [Fact]
    public void Alpha10_P80_PreNewtonUpperStation2_UsavSplit_MatchesReferenceDump()
    {
        ReferenceUsavSplit reference = ReadReferenceUsavSplit();
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        (double uinv, double air, double wake, double usav) managed = ComputeUsavSplit(
            context.BoundaryLayerState,
            context.Dij,
            context.UeInv,
            side: 0,
            ibl: 1);

        AssertExactSingleFromDump("station-2 UINV", reference.Uinv, managed.uinv);
        AssertExactSingleFromDump("station-2 air contribution", reference.Air, managed.air);
        AssertExactSingleFromDump("station-2 wake contribution", reference.Wake, managed.wake);
        AssertExactSingleFromDump("station-2 USAV", reference.Usav, managed.usav);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonUpperStation2_FirstWakeTerm_MatchesReferenceDump()
    {
        ReferenceUsavWakeTerm reference = ReadReferenceUsavWakeTerm();
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        BoundaryLayerSystemState blState = context.BoundaryLayerState;

        int ibl = 1;
        int side = 0;
        int jbl = blState.IBLTE[1] + 1;
        int iPan = blState.IPAN[ibl, side];
        int jPan = blState.IPAN[jbl, 1];

        Assert.Equal(reference.J, jPan + 1);

        double ueM = -blState.VTI[ibl, side] * blState.VTI[jbl, 1] * context.Dij[iPan, jPan];
        double contribution = ueM * blState.MASS[jbl, 1];

        AssertExactSingleFromDump("station-2 first wake UE_M", reference.UeM, ueM);
        AssertExactSingleFromDump("station-2 first wake MASS", reference.Mass, blState.MASS[jbl, 1]);
        AssertExactSingleFromDump("station-2 first wake contribution", reference.Contribution, contribution);
    }

    [Fact]
    public void Alpha10_P80_PreNewtonUpperStation2_AirContributionRunningSum_MatchesReferenceTraceAndDump()
    {
        ReferenceUsavSplit referenceSplit = ReadReferenceUsavSplit();
        ParityTraceRecord[] referenceAirTerms = ReadReferencePredictedEdgeVelocityTerms()
            .Where(record => !ReadBooleanish(record, "isWakeSource"))
            .ToArray();
        ParityTraceRecord[] managedAirTerms = RunManagedPredictedEdgeVelocityTerms()
            .Where(record => !ReadBooleanish(record, "isWakeSource"))
            .ToArray();

        Assert.Equal(referenceAirTerms.Length, managedAirTerms.Length);

        float referenceRunning = 0.0f;
        float managedRunning = 0.0f;
        for (int index = 0; index < referenceAirTerms.Length; index++)
        {
            AssertPredictedEdgeVelocityTermIdentity(referenceAirTerms[index], managedAirTerms[index], $"air term[{index}]");

            float referenceContribution = ReadTraceSingle(referenceAirTerms[index], "contribution");
            float managedContribution = ReadTraceSingle(managedAirTerms[index], "contribution");
            AssertExactSingleFromDump($"air term[{index}] contribution", referenceContribution, managedContribution);

            referenceRunning = AccumulateLegacyFloat(referenceRunning, referenceContribution);
            managedRunning = AccumulateLegacyFloat(managedRunning, managedContribution);
            AssertExactSingleFromDump($"air running sum[{index}]", referenceRunning, managedRunning);
        }

        AssertExactSingleFromDump("reference air subtotal from terms", referenceSplit.Air, referenceRunning);
        AssertExactSingleFromDump("managed air subtotal from terms", referenceSplit.Air, managedRunning);
    }

    private static ViscousSolverEngine.PreNewtonSetupContext BuildContext()
    {
        FortranReferenceCase definition = FortranReferenceCases.Get(CaseId);
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

        return ViscousSolverEngine.PrepareLegacySetBlContext((x, y), settings, alphaRadians);
    }

    private static AnalysisSettings BuildAnalysisSettings(FortranReferenceCase definition)
    {
        return new AnalysisSettings(
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
    }

    private static (double uinv, double air, double wake, double usav) ComputeUsavSplit(
        BoundaryLayerSystemState blState,
        double[,] dij,
        double[,] ueInv,
        int side,
        int ibl)
    {
        double dui = 0.0;
        double airfoilContribution = 0.0;
        double wakeContribution = 0.0;
        int iPan = blState.IPAN[ibl, side];
        double vtiI = blState.VTI[ibl, side];
        const bool UseLegacyPrecision = true;

        for (int jSide = 0; jSide < 2; jSide++)
        {
            for (int jbl = 1; jbl < blState.NBL[jSide]; jbl++)
            {
                int jPan = blState.IPAN[jbl, jSide];
                if (iPan < 0 || iPan >= dij.GetLength(0) || jPan < 0 || jPan >= dij.GetLength(1))
                {
                    continue;
                }

                double vtiJ = blState.VTI[jbl, jSide];
                double ueM = -LegacyPrecisionMath.Multiply(vtiI, vtiJ, dij[iPan, jPan], UseLegacyPrecision);
                double contribution = LegacyPrecisionMath.Multiply(ueM, blState.MASS[jbl, jSide], UseLegacyPrecision);
                dui = LegacyPrecisionMath.Add(dui, contribution, UseLegacyPrecision);

                if (jbl > blState.IBLTE[jSide])
                {
                    wakeContribution = LegacyPrecisionMath.Add(wakeContribution, contribution, UseLegacyPrecision);
                }
                else
                {
                    airfoilContribution = LegacyPrecisionMath.Add(airfoilContribution, contribution, UseLegacyPrecision);
                }
            }
        }

        double predicted = LegacyPrecisionMath.Add(ueInv[ibl, side], dui, UseLegacyPrecision);
        return (ueInv[ibl, side], airfoilContribution, wakeContribution, predicted);
    }

    private static IReadOnlyList<ParityTraceRecord> ReadReferencePredictedEdgeVelocityTerms()
    {
        string referencePath = FortranParityArtifactLocator.GetLatestReferenceTracePath(PredictedEdgeVelocityReferenceDirectory);
        return ParityTraceLoader.ReadMatching(
                referencePath,
                static record => record.Kind == "predicted_edge_velocity_term" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 2))
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedPredictedEdgeVelocityTerms()
    {
        MethodInfo method = typeof(ViscousNewtonAssembler).GetMethod(
            "ComputePredictedEdgeVelocities",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ViscousNewtonAssembler.ComputePredictedEdgeVelocities was not found.");
        ViscousSolverEngine.PreNewtonSetupContext context = BuildContext();
        var lines = new List<string>();

        using (var traceWriter = new JsonlTraceWriter(TextWriter.Null, runtime: "csharp", session: new { caseName = "pre-newton-wake-usav-air-sum" }, serializedRecordObserver: lines.Add))
        {
            using var traceScope = SolverTrace.Begin(traceWriter);
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

            _ = method.Invoke(null, args)
                ?? throw new InvalidOperationException("ComputePredictedEdgeVelocities returned null.");
        }

        return ParseObservedRecords(lines)
            .Where(static record => record.Kind == "predicted_edge_velocity_term" &&
                                    HasExactDataInt(record, "side", 1) &&
                                    HasExactDataInt(record, "station", 2))
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static IReadOnlyList<ParityTraceRecord> RunManagedSetBlTrace(params string[] kinds)
    {
        var lines = new List<string>();
        using (var traceWriter = new JsonlTraceWriter(
            TextWriter.Null,
            runtime: "csharp",
            session: new { caseName = "pre-newton-setbl-micro" },
            serializedRecordObserver: lines.Add))
        {
            using var traceScope = SolverTrace.Begin(traceWriter);
            _ = BuildContext();
        }

        HashSet<string> requiredKinds = new(kinds, StringComparer.Ordinal);
        return ParseObservedRecords(lines)
            .Where(record => requiredKinds.Count == 0 || requiredKinds.Contains(record.Kind))
            .OrderBy(static record => record.Sequence)
            .ToArray();
    }

    private static ReferenceUsavSplit ReadReferenceUsavSplit()
    {
        string line = File.ReadLines(GetLatestReferenceDumpPath())
            .FirstOrDefault(value => value.Contains("USAV_SPLIT IS= 1 IBL=   2", StringComparison.Ordinal))
            ?? throw new XunitException("USAV_SPLIT line for upper station-2 was not found in the focused reference dump.");

        Match match = UsavSplitRegex.Match(line);
        if (!match.Success)
        {
            throw new XunitException($"Unable to parse USAV_SPLIT reference line: {line}");
        }

        return new ReferenceUsavSplit(
            ParseSingle(match, "uinv"),
            ParseSingle(match, "air"),
            ParseSingle(match, "wake"),
            ParseSingle(match, "usav"));
    }

    private static ReferenceUsavWakeTerm ReadReferenceUsavWakeTerm()
    {
        string line = File.ReadLines(GetLatestReferenceDumpPath())
            .FirstOrDefault(value => value.Contains("USAV_WAKE_TERM IS= 1 IBL=   2 JS= 2 JBL=  35", StringComparison.Ordinal))
            ?? throw new XunitException("USAV_WAKE_TERM line for the first lower wake contributor was not found in the focused reference dump.");

        Match match = UsavWakeTermRegex.Match(line);
        if (!match.Success)
        {
            throw new XunitException($"Unable to parse USAV_WAKE_TERM reference line: {line}");
        }

        return new ReferenceUsavWakeTerm(
            int.Parse(match.Groups["j"].Value, CultureInfo.InvariantCulture),
            ParseSingle(match, "ueM"),
            ParseSingle(match, "mass"),
            ParseSingle(match, "contr"));
    }

    private static ReferenceStationState ReadReferenceStationState(int surface, int station)
    {
        string[] lines = File.ReadAllLines(GetLatestReferenceDumpPath());
        string marker = string.Format(
            CultureInfo.InvariantCulture,
            "STATION IS= {0} IBL= {1,3}",
            surface,
            station);

        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (!lines[i].Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            Match match = BlStateRegex.Match(lines[i + 1]);
            if (!match.Success)
            {
                throw new XunitException($"Unable to parse BL_STATE line after {marker}: {lines[i + 1]}");
            }

            return new ReferenceStationState(
                ParseSingle(match, "x"),
                ParseSingle(match, "ue"),
                ParseSingle(match, "th"),
                ParseSingle(match, "ds"),
                ParseSingle(match, "mass"));
        }

        throw new XunitException($"Station marker not found in focused reference dump: {marker}");
    }

    private static string GetLatestReferenceDumpPath()
    {
        return FortranParityArtifactLocator.GetLatestReferenceDumpPath(ReferenceDirectory);
    }

    private static IReadOnlyList<ParityTraceRecord> ParseObservedRecords(IEnumerable<string> lines)
    {
        return lines
            .Select(ParityTraceLoader.DeserializeLine)
            .Where(static record => record is not null)
            .Select(static record => record!)
            .ToArray();
    }

    private static float ParseSingle(Match match, string groupName)
    {
        Group bitsGroup = match.Groups[groupName + "Bits"];
        if (bitsGroup.Success && bitsGroup.Length > 0)
        {
            int bits = int.Parse(bitsGroup.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return BitConverter.Int32BitsToSingle(bits);
        }

        return float.Parse(match.Groups[groupName].Value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static float ReadTraceSingle(ParityTraceRecord record, string fieldName)
    {
        if (record.TryGetDataBits(fieldName, out IReadOnlyDictionary<string, string>? bits) &&
            bits is not null &&
            bits.TryGetValue("f32", out string? singleHex))
        {
            int rawBits = int.Parse(singleHex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return BitConverter.Int32BitsToSingle(rawBits);
        }

        Assert.True(record.TryGetDataField(fieldName, out JsonElement value), $"Missing data field '{fieldName}' in {record.Kind}.");
        Assert.True(value.ValueKind == JsonValueKind.Number, $"Field '{fieldName}' in {record.Kind} was not numeric.");
        return (float)value.GetDouble();
    }

    private static bool ReadBooleanish(ParityTraceRecord record, string fieldName)
    {
        Assert.True(record.TryGetDataField(fieldName, out JsonElement value), $"Missing data field '{fieldName}' in {record.Kind}.");
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDouble() != 0.0,
            _ => throw new InvalidOperationException($"Field '{fieldName}' in {record.Kind} was not boolean-compatible.")
        };
    }

    private static bool HasExactDataInt(ParityTraceRecord record, string fieldName, int expected)
    {
        if (!record.TryGetDataField(fieldName, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return value.TryGetInt32(out int actual) && actual == expected;
    }

    private static void AssertPredictedEdgeVelocityTermIdentity(ParityTraceRecord expected, ParityTraceRecord actual, string context)
    {
        Assert.True(HasExactDataInt(actual, "side", (int)ReadTraceSingle(expected, "side")), $"{context} side mismatch.");
        Assert.True(HasExactDataInt(actual, "station", (int)ReadTraceSingle(expected, "station")), $"{context} station mismatch.");
        Assert.True(HasExactDataInt(actual, "sourceSide", (int)ReadTraceSingle(expected, "sourceSide")), $"{context} sourceSide mismatch.");
        Assert.True(HasExactDataInt(actual, "sourceStation", (int)ReadTraceSingle(expected, "sourceStation")), $"{context} sourceStation mismatch.");
        Assert.True(HasExactDataInt(actual, "iPan", (int)ReadTraceSingle(expected, "iPan")), $"{context} iPan mismatch.");
        Assert.True(HasExactDataInt(actual, "jPan", (int)ReadTraceSingle(expected, "jPan")), $"{context} jPan mismatch.");
        Assert.Equal(ReadBooleanish(expected, "isWakeSource"), ReadBooleanish(actual, "isWakeSource"));
    }

    private static float AccumulateLegacyFloat(float accumulator, float contribution)
    {
        float sum = (float)((double)accumulator + contribution);
        return LegacyPrecisionMath.RoundBarrier(sum);
    }

    private static void AssertExactSingleFromDump(string label, float expected, double actual, string? diagnostic = null)
    {
        float actualSingle = (float)actual;
        int expectedBits = BitConverter.SingleToInt32Bits(expected);
        int actualBits = BitConverter.SingleToInt32Bits(actualSingle);
        if (expectedBits != actualBits)
        {
            throw new XunitException(
                $"{label} mismatch. " +
                $"Fortran={expected.ToString("R", CultureInfo.InvariantCulture)} [{expectedBits:X8}] " +
                $"ManagedDouble={actual.ToString("G17", CultureInfo.InvariantCulture)} " +
                $"ManagedSingle={actualSingle.ToString("R", CultureInfo.InvariantCulture)} [{actualBits:X8}]" +
                (string.IsNullOrWhiteSpace(diagnostic) ? string.Empty : $" {diagnostic}"));
        }
    }

    private static void AssertStationStateMatchesReference(
        string label,
        ReferenceStationState expected,
        double actualUe,
        double actualTheta,
        double actualDstar,
        double actualMass)
    {
        string[] mismatches = new string?[]
        {
            DescribeStateFieldMismatch("Ue", expected.Ue, actualUe),
            DescribeStateFieldMismatch("theta", expected.Theta, actualTheta),
            DescribeStateFieldMismatch("dstar", expected.Dstar, actualDstar),
            DescribeStateFieldMismatch("mass", expected.Mass, actualMass)
        }
        .Where(static message => message is not null)
        .Cast<string>()
        .ToArray();

        if (mismatches.Length > 0)
        {
            throw new XunitException($"{label} mismatch: {string.Join("; ", mismatches)}");
        }
    }

    private static string DescribeFirstBoundaryLayerMismatch(BoundaryLayerSystemState blState)
    {
        for (int surface = 1; surface <= 2; surface++)
        {
            int side = surface - 1;
            for (int station = 2; station <= blState.IBLTE[side] + 1; station++)
            {
                ReferenceStationState reference = ReadReferenceStationState(surface, station);
                int ibl = station - 1;

                string? mismatch = DescribeSingleMismatch("Ue", surface, station, reference.Ue, blState.UEDG[ibl, side]);
                if (mismatch is not null)
                {
                    return mismatch;
                }

                mismatch = DescribeSingleMismatch("theta", surface, station, reference.Theta, blState.THET[ibl, side]);
                if (mismatch is not null)
                {
                    return mismatch;
                }

                mismatch = DescribeSingleMismatch("dstar", surface, station, reference.Dstar, blState.DSTR[ibl, side]);
                if (mismatch is not null)
                {
                    return mismatch;
                }

                mismatch = DescribeSingleMismatch("mass", surface, station, reference.Mass, blState.MASS[ibl, side]);
                if (mismatch is not null)
                {
                    return mismatch;
                }
            }
        }

        return "first-bl-mismatch=none";
    }

    private static string? DescribeSingleMismatch(string field, int surface, int station, float expected, double actual)
    {
        float actualSingle = (float)actual;
        int expectedBits = BitConverter.SingleToInt32Bits(expected);
        int actualBits = BitConverter.SingleToInt32Bits(actualSingle);
        if (expectedBits == actualBits)
        {
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "first-bl-mismatch=surface{0} station{1} {2} expected={3:R}[{4:X8}] actual={5:R}[{6:X8}]",
            surface,
            station,
            field,
            expected,
            expectedBits,
            actualSingle,
            actualBits);
    }

    private static string? DescribeStateFieldMismatch(string field, float expected, double actual)
    {
        float actualSingle = (float)actual;
        int expectedBits = BitConverter.SingleToInt32Bits(expected);
        int actualBits = BitConverter.SingleToInt32Bits(actualSingle);
        if (expectedBits == actualBits)
        {
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} expected={1:R}[{2:X8}] actual={3:R}[{4:X8}]",
            field,
            expected,
            expectedBits,
            actualSingle,
            actualBits);
    }

    private readonly record struct ReferenceUsavSplit(float Uinv, float Air, float Wake, float Usav);

    private readonly record struct ReferenceUsavWakeTerm(int J, float UeM, float Mass, float Contribution);

    private readonly record struct ReferenceStationState(float X, float Ue, float Theta, float Dstar, float Mass);
}
