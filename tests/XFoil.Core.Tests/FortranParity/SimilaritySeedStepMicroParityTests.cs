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
// Primary legacy source: f_xfoil/src/xbl.f :: MRCHUE similarity-station dense solve and seed-step limiter
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_seedhandoff_ref1/reference_trace.*.jsonl and tools/fortran-debug/reference/alpha10_p80_similarity_seed_s1st2_ref/reference_trace.jsonl
// Role in port: Replays the similarity-station Newton step from captured local vectors so dense-solve and seed-step parity can be verified without rerunning the full viscous march.
// Differences: The harness is managed-only infrastructure, but it drives the exact managed seed helper sequence against authoritative Fortran step traces using raw IEEE-754 comparisons.
// Decision: Keep the micro-engine because it sits immediately downstream of the similarity BLSYS block and catches seed-step bugs in milliseconds.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class SimilaritySeedStepMicroParityTests
{
    [Fact]
    public void Alpha10_P80_SimilaritySeedStep_BitwiseMatchesFortranTraceVectors()
    {
        IReadOnlyList<SimilaritySeedStepVector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        MethodInfo solveMethod = typeof(ViscousSolverEngine).GetMethod(
            "SolveSeedLinearSystem",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SolveSeedLinearSystem method not found.");
        MethodInfo stepMetricsMethod = typeof(ViscousSolverEngine).GetMethod(
            "ComputeSeedStepMetrics",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeSeedStepMetrics method not found.");
        MethodInfo relaxationMethod = typeof(ViscousSolverEngine).GetMethod(
            "ComputeLegacySeedRelaxation",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeLegacySeedRelaxation method not found.");

        var solver = new DenseLinearSystemSolver();

        foreach (SimilaritySeedStepVector vector in vectors)
        {
            BlsysResult result = BoundaryLayerSystemAssembler.AssembleStationSystem(
                isWake: false,
                isTurbOrTran: false,
                isTran: false,
                isSimi: true,
                x1: vector.X,
                x2: vector.X,
                uei1: vector.Uei,
                uei2: vector.Uei,
                t1: vector.Theta,
                t2: vector.Theta,
                d1: vector.Dstar,
                d2: vector.Dstar,
                s1: ViscousSolverEngine.LegacyLaminarShearSeedValue,
                s2: ViscousSolverEngine.LegacyLaminarShearSeedValue,
                dw1: 0.0,
                dw2: 0.0,
                ampl1: vector.Ampl,
                ampl2: vector.Ampl,
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

            double[] delta = (double[])solveMethod.Invoke(null, new object?[] { solver, matrix, rhs, true })!;
            object stepMetrics = stepMetricsMethod.Invoke(null, new object?[]
            {
                delta,
                (double)vector.Theta,
                (double)vector.Dstar,
                10.0,
                (double)vector.Uei,
                false,
                true
            })!;

            double dmax = (double)GetProperty(stepMetrics, "Dmax");
            double rlx = (double)relaxationMethod.Invoke(null, new object?[] { dmax, true })!;

            AssertHex(vector.ExpectedDeltaShearHex, ToHex((float)(double)GetProperty(stepMetrics, "DeltaShear")), $"iter={vector.Iteration} deltaShear");
            AssertHex(vector.ExpectedDeltaThetaHex, ToHex((float)(double)GetProperty(stepMetrics, "DeltaTheta")), $"iter={vector.Iteration} deltaTheta");
            AssertHex(vector.ExpectedDeltaDstarHex, ToHex((float)(double)GetProperty(stepMetrics, "DeltaDstar")), $"iter={vector.Iteration} deltaDstar");
            AssertHex(vector.ExpectedDeltaUeHex, ToHex((float)(double)GetProperty(stepMetrics, "DeltaUe")), $"iter={vector.Iteration} deltaUe");
            AssertHex(vector.ExpectedRatioShearHex, ToHex((float)(double)GetProperty(stepMetrics, "RatioShear")), $"iter={vector.Iteration} ratioShear");
            AssertHex(vector.ExpectedRatioThetaHex, ToHex((float)(double)GetProperty(stepMetrics, "RatioTheta")), $"iter={vector.Iteration} ratioTheta");
            AssertHex(vector.ExpectedRatioDstarHex, ToHex((float)(double)GetProperty(stepMetrics, "RatioDstar")), $"iter={vector.Iteration} ratioDstar");
            AssertHex(vector.ExpectedRatioUeHex, ToHex((float)(double)GetProperty(stepMetrics, "RatioUe")), $"iter={vector.Iteration} ratioUe");
            AssertHex(vector.ExpectedDmaxHex, ToHex((float)dmax), $"iter={vector.Iteration} dmax");
            AssertHex(vector.ExpectedRelaxationHex, ToHex((float)rlx), $"iter={vector.Iteration} rlx");
            AssertHex(vector.ExpectedResidualNormHex, ToHex((float)(double)GetProperty(stepMetrics, "ResidualNorm")), $"iter={vector.Iteration} residualNorm");
        }
    }

    private static IReadOnlyList<SimilaritySeedStepVector> LoadVectors()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string inputsPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_seedhandoff_ref1"));
        string stepsPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_similarity_seed_s1st2_ref"));

        IReadOnlyList<ParityTraceRecord> inputRecords = ParityTraceLoader.ReadMatching(
            inputsPath,
            static record => record.Kind == "blsys_interval_inputs" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2));
        IReadOnlyList<ParityTraceRecord> stepRecords = ParityTraceLoader.ReadMatching(
            stepsPath,
            static record => record.Kind == "laminar_seed_step" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2));

        Assert.Equal(inputRecords.Count, stepRecords.Count);

        return inputRecords
            .Zip(stepRecords, static (input, step) => BuildVector(input, step))
            .ToArray();
    }

    private static SimilaritySeedStepVector BuildVector(ParityTraceRecord input, ParityTraceRecord step)
    {
        string inputUHex = GetSingleHex(input, "u1");
        string inputTHex = GetSingleHex(input, "t1");
        string inputDHex = GetSingleHex(input, "d1");
        string inputAmplHex = GetSingleHex(input, "ampl1");

        Assert.Equal(inputUHex, GetSingleHex(step, "uei"));
        Assert.Equal(inputTHex, GetSingleHex(step, "theta"));
        Assert.Equal(inputDHex, GetSingleHex(step, "dstar"));
        Assert.Equal(inputAmplHex, GetSingleHex(step, "ampl"));

        return new SimilaritySeedStepVector(
            Iteration: (int)ReadRequiredDouble(step, "iteration"),
            X: FromSingleHex(GetSingleHex(input, "x1")),
            Uei: FromSingleHex(inputUHex),
            Theta: FromSingleHex(inputTHex),
            Dstar: FromSingleHex(inputDHex),
            Ampl: FromSingleHex(inputAmplHex),
            ExpectedDeltaShearHex: GetSingleHex(step, "deltaShear"),
            ExpectedDeltaThetaHex: GetSingleHex(step, "deltaTheta"),
            ExpectedDeltaDstarHex: GetSingleHex(step, "deltaDstar"),
            ExpectedDeltaUeHex: GetSingleHex(step, "deltaUe"),
            ExpectedRatioShearHex: GetSingleHex(step, "ratioShear"),
            ExpectedRatioThetaHex: GetSingleHex(step, "ratioTheta"),
            ExpectedRatioDstarHex: GetSingleHex(step, "ratioDstar"),
            ExpectedRatioUeHex: GetSingleHex(step, "ratioUe"),
            ExpectedDmaxHex: GetSingleHex(step, "dmax"),
            ExpectedRelaxationHex: GetSingleHex(step, "rlx"),
            ExpectedResidualNormHex: GetSingleHex(step, "residualNorm"));
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

    private sealed record SimilaritySeedStepVector(
        int Iteration,
        float X,
        float Uei,
        float Theta,
        float Dstar,
        float Ampl,
        string ExpectedDeltaShearHex,
        string ExpectedDeltaThetaHex,
        string ExpectedDeltaDstarHex,
        string ExpectedDeltaUeHex,
        string ExpectedRatioShearHex,
        string ExpectedRatioThetaHex,
        string ExpectedRatioDstarHex,
        string ExpectedRatioUeHex,
        string ExpectedDmaxHex,
        string ExpectedRelaxationHex,
        string ExpectedResidualNormHex);
}
