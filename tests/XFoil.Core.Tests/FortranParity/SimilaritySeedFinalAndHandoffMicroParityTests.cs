using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: MRCHUE similarity-station update/final-state carry
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_seedhandoff_ref1/reference_trace*.jsonl and tools/fortran-debug/reference/alpha10_p80_similarity_seed_s1st2_ref/reference_trace*.jsonl
// Role in port: Replays the accepted similarity-station updates and the first downstream handoff from captured local vectors so station-to-station seed parity can be checked without reopening the full viscous march.
// Differences: The harness is managed-only infrastructure, but it drives the exact managed local-system, dense-solve, step-limiter, and accepted-state update sequence against authoritative Fortran trace words.
// Decision: Keep the micro-engine because it closes the gap between the local seed-system rigs and the next-station consumer using only raw IEEE754 comparisons.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class SimilaritySeedFinalAndHandoffMicroParityTests
{
    private const double Amcrit = 9.0;
    private const double Hklim = 1.02;

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
    public void Alpha10_P80_SimilaritySeedFinal_BitwiseMatchesFortranTraceVectors()
    {
        SeedChainData data = LoadSeedChainData();
        ReplayedSeedState finalState = ReplayStation2SeedChain(data, assertIntermediateStates: true);

        AssertHex(GetSingleHex(data.FinalRecord, "theta"), ToHex((float)finalState.Theta), "final theta");
        AssertHex(GetSingleHex(data.FinalRecord, "dstar"), ToHex((float)finalState.Dstar), "final dstar");
        AssertHex(GetSingleHex(data.FinalRecord, "ampl"), ToHex((float)finalState.Ampl), "final ampl");
        AssertHex(GetSingleHex(data.FinalRecord, "mass"), ToHex((float)finalState.Mass), "final mass");
    }

    [Fact]
    public void Alpha10_P80_SimilaritySeedHandoff_BitwiseMatchesNextStationInputs()
    {
        SeedChainData data = LoadSeedChainData();
        ReplayedSeedState finalState = ReplayStation2SeedChain(data, assertIntermediateStates: false);

        AssertHex(GetSingleHex(data.FirstStation3Input, "x1"), ToHex((float)finalState.X), "station3 handoff x1");
        AssertHex(GetSingleHex(data.FirstStation3Input, "u1"), ToHex((float)finalState.Uei), "station3 handoff u1");
        AssertHex(GetSingleHex(data.FirstStation3Input, "t1"), ToHex((float)finalState.Theta), "station3 handoff t1");
        AssertHex(GetSingleHex(data.FirstStation3Input, "d1"), ToHex((float)finalState.Dstar), "station3 handoff d1");
        AssertHex(GetSingleHex(data.FirstStation3Input, "ampl1"), ToHex((float)finalState.Ampl), "station3 handoff ampl1");
    }

    private static ReplayedSeedState ReplayStation2SeedChain(SeedChainData data, bool assertIntermediateStates)
    {
        Assert.Equal(4, data.Station2Inputs.Count);
        Assert.Equal(4, data.Station2StepRecords.Count);

        double x = FromSingleHex(GetSingleHex(data.Station2Inputs[0], "x1"));
        double uei = FromSingleHex(GetSingleHex(data.Station2Inputs[0], "u1"));
        double theta = FromSingleHex(GetSingleHex(data.Station2Inputs[0], "t1"));
        double dstar = FromSingleHex(GetSingleHex(data.Station2Inputs[0], "d1"));
        double ampl = FromSingleHex(GetSingleHex(data.Station2Inputs[0], "ampl1"));

        var solver = new DenseLinearSystemSolver();

        for (int iterationIndex = 0; iterationIndex < data.Station2StepRecords.Count; iterationIndex++)
        {
            ParityTraceRecord inputRecord = data.Station2Inputs[iterationIndex];
            ParityTraceRecord stepRecord = data.Station2StepRecords[iterationIndex];

            AssertHex(GetSingleHex(inputRecord, "u1"), ToHex((float)uei), $"iter={iterationIndex + 1} input u1");
            AssertHex(GetSingleHex(inputRecord, "t1"), ToHex((float)theta), $"iter={iterationIndex + 1} input t1");
            AssertHex(GetSingleHex(inputRecord, "d1"), ToHex((float)dstar), $"iter={iterationIndex + 1} input d1");
            AssertHex(GetSingleHex(inputRecord, "ampl1"), ToHex((float)ampl), $"iter={iterationIndex + 1} input ampl1");

            BoundaryLayerSystemAssembler.BlsysResult result = BoundaryLayerSystemAssembler.AssembleStationSystem(
                isWake: false,
                isTurbOrTran: false,
                isTran: false,
                isSimi: true,
                x1: x,
                x2: x,
                uei1: uei,
                uei2: uei,
                t1: theta,
                t2: theta,
                d1: dstar,
                d2: dstar,
                s1: ViscousSolverEngine.LegacyLaminarShearSeedValue,
                s2: ViscousSolverEngine.LegacyLaminarShearSeedValue,
                dw1: 0.0,
                dw2: 0.0,
                ampl1: ampl,
                ampl2: ampl,
                amcrit: Amcrit,
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
                useLegacyPrecision: true);

            var matrix = new double[4, 4];
            var rhs = new double[4];
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    matrix[row, col] = result.VS2[row, col];
                }

                rhs[row] = result.Residual[row];
            }

            matrix[3, 3] = 1.0;
            rhs[3] = 0.0;

            double[] delta = (double[])SolveSeedLinearSystemMethod.Invoke(null, new object?[] { solver, matrix, rhs, true })!;
            object stepMetrics = ComputeSeedStepMetricsMethod.Invoke(null, new object?[]
            {
                delta,
                theta,
                dstar,
                10.0,
                uei,
                1,
                2,
                iterationIndex + 1,
                "direct",
                false,
                true
            })!;

            double dmax = (double)GetProperty(stepMetrics, "Dmax");
            double rlx = (double)ComputeLegacySeedRelaxationMethod.Invoke(null, new object?[] { dmax, true })!;

            AssertHex(GetSingleHex(stepRecord, "deltaTheta"), ToHex((float)(double)GetProperty(stepMetrics, "DeltaTheta")), $"iter={iterationIndex + 1} deltaTheta");
            AssertHex(GetSingleHex(stepRecord, "deltaDstar"), ToHex((float)(double)GetProperty(stepMetrics, "DeltaDstar")), $"iter={iterationIndex + 1} deltaDstar");
            AssertHex(GetSingleHex(stepRecord, "rlx"), ToHex((float)rlx), $"iter={iterationIndex + 1} rlx");

            theta = Math.Max(LegacyPrecisionMath.AddScaled(theta, rlx, delta[1], useLegacyPrecision: true), 1.0e-10);
            dstar = Math.Max(LegacyPrecisionMath.AddScaled(dstar, rlx, delta[2], useLegacyPrecision: true), 1.0e-10);
            dstar = (double)ApplySeedDslimMethod.Invoke(null, new object?[] { dstar, theta, 0.0, Hklim, true })!;

            uei = LegacyPrecisionMath.RoundToSingle(uei);
            theta = LegacyPrecisionMath.RoundToSingle(theta);
            dstar = LegacyPrecisionMath.RoundToSingle(dstar);
            ampl = LegacyPrecisionMath.RoundToSingle(ampl);

            if (!assertIntermediateStates)
            {
                continue;
            }

            if (iterationIndex + 1 < data.Station2Inputs.Count)
            {
                ParityTraceRecord nextInputRecord = data.Station2Inputs[iterationIndex + 1];
                AssertHex(GetSingleHex(nextInputRecord, "u1"), ToHex((float)uei), $"iter={iterationIndex + 1} next u1");
                AssertHex(GetSingleHex(nextInputRecord, "t1"), ToHex((float)theta), $"iter={iterationIndex + 1} next t1");
                AssertHex(GetSingleHex(nextInputRecord, "d1"), ToHex((float)dstar), $"iter={iterationIndex + 1} next d1");
                AssertHex(GetSingleHex(nextInputRecord, "ampl1"), ToHex((float)ampl), $"iter={iterationIndex + 1} next ampl1");
            }
        }

        double mass = LegacyPrecisionMath.Multiply(dstar, uei, useLegacyPrecision: true);
        return new ReplayedSeedState(x, uei, theta, dstar, ampl, mass);
    }

    private static SeedChainData LoadSeedChainData()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string handoffPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_seedhandoff_ref1"));
        string similarityPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_similarity_seed_s1st2_ref"));

        IReadOnlyList<ParityTraceRecord> station2Inputs = ParityTraceLoader.ReadMatching(
                handoffPath,
                static record => record.Kind == "blsys_interval_inputs" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 2))
            .OrderBy(record => record.Sequence)
            .ToArray();

        IReadOnlyList<ParityTraceRecord> station2Steps = ParityTraceLoader.ReadMatching(
                similarityPath,
                static record => record.Kind == "laminar_seed_step" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 2))
            .OrderBy(record => ReadRequiredDouble(record, "iteration"))
            .ToArray();

        ParityTraceRecord finalRecord = ParityTraceLoader.ReadMatching(
                handoffPath,
                static record => record.Kind == "laminar_seed_final" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 2))
            .Single();

        ParityTraceRecord firstStation3Input = ParityTraceLoader.ReadMatching(
                handoffPath,
                static record => record.Kind == "blsys_interval_inputs" &&
                                 HasExactDataInt(record, "side", 1) &&
                                 HasExactDataInt(record, "station", 3))
            .OrderBy(record => record.Sequence)
            .First();

        return new SeedChainData(station2Inputs, station2Steps, finalRecord, firstStation3Input);
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

    private static double ReadRequiredDouble(ParityTraceRecord record, string field)
    {
        Assert.True(record.TryGetDataField(field, out var value), $"Missing data field '{field}' in {record.Kind}.");
        return value.GetDouble();
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

    private static object GetProperty(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {instance.GetType().FullName}.");
        return property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' returned null on {instance.GetType().FullName}.");
    }

    private static string ToHex(float value)
        => $"0x{BitConverter.SingleToInt32Bits(value):X8}";

    private static void AssertHex(string expected, string actual, string context)
    {
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{context} expected={expected} actual={actual}");
    }

    private sealed record SeedChainData(
        IReadOnlyList<ParityTraceRecord> Station2Inputs,
        IReadOnlyList<ParityTraceRecord> Station2StepRecords,
        ParityTraceRecord FinalRecord,
        ParityTraceRecord FirstStation3Input);

    private sealed record ReplayedSeedState(
        double X,
        double Uei,
        double Theta,
        double Dstar,
        double Ampl,
        double Mass);
}
