using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xbl.f :: MRCHUE similarity-station seed loop and f_xfoil/src/xblsys.f :: BLSYS(0)
// Secondary legacy source: tools/fortran-debug/reference/alpha10_p80_seedhandoff_ref1/reference_trace.*.jsonl and tools/fortran-debug/reference/alpha10_p80_seed_system_ref/reference_trace*.jsonl
// Role in port: Replays the alpha-10 panel-80 similarity-station seed matrix from trace-captured local vectors so parity debugging can stop rerunning the full viscous solve for this block.
// Differences: The harness is managed-only infrastructure, but it compares the local BLSYS output matrix and residuals against authoritative Fortran trace words using the same captured station-2 inputs.
// Decision: Keep the micro-engine because the similarity seed system is upstream of the larger MRCHUE/MRCHDU chain and is small enough to verify bitwise in isolation.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class SimilaritySeedSystemMicroParityTests
{
    [Fact]
    public void Alpha10_P80_SimilaritySeedSystem_BitwiseMatchesFortranTraceVectors()
    {
        IReadOnlyList<SimilaritySeedVector> vectors = LoadVectors();
        Assert.NotEmpty(vectors);

        foreach (SimilaritySeedVector vector in vectors)
        {
            BoundaryLayerSystemAssembler.BlsysResult result = BoundaryLayerSystemAssembler.AssembleStationSystem(
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

    private static IReadOnlyList<SimilaritySeedVector> LoadVectors()
    {
        string debugDir = FortranReferenceCases.GetFortranDebugDirectory();
        string inputsPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_seedhandoff_ref1"));
        string outputsPath = GetLatestTracePath(Path.Combine(debugDir, "reference", "alpha10_p80_seed_system_ref"));

        IReadOnlyList<ParityTraceRecord> inputRecords = ParityTraceLoader.ReadMatching(
            inputsPath,
            static record => record.Kind == "blsys_interval_inputs" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2));
        IReadOnlyList<ParityTraceRecord> outputRecords = ParityTraceLoader.ReadMatching(
            outputsPath,
            static record => record.Kind == "laminar_seed_system" &&
                             HasExactDataInt(record, "side", 1) &&
                             HasExactDataInt(record, "station", 2));

        Assert.Equal(inputRecords.Count, outputRecords.Count);

        return inputRecords
            .Zip(outputRecords, static (input, output) => BuildVector(input, output))
            .ToArray();
    }

    private static SimilaritySeedVector BuildVector(ParityTraceRecord input, ParityTraceRecord output)
    {
        string inputUHex = GetSingleHex(input, "u1");
        string inputTHex = GetSingleHex(input, "t1");
        string inputDHex = GetSingleHex(input, "d1");
        string inputAmplHex = GetSingleHex(input, "ampl1");

        Assert.Equal(inputUHex, GetSingleHex(output, "uei"));
        Assert.Equal(inputTHex, GetSingleHex(output, "theta"));
        Assert.Equal(inputDHex, GetSingleHex(output, "dstar"));
        Assert.Equal(inputAmplHex, GetSingleHex(output, "ampl"));

        return new SimilaritySeedVector(
            Iteration: (int)ReadRequiredDouble(output, "iteration"),
            X: FromSingleHex(GetSingleHex(input, "x1")),
            Uei: FromSingleHex(inputUHex),
            Theta: FromSingleHex(inputTHex),
            Dstar: FromSingleHex(inputDHex),
            Ampl: FromSingleHex(inputAmplHex),
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

    private static string ToHex(float value)
        => $"0x{BitConverter.SingleToInt32Bits(value):X8}";

    private static void AssertHex(string expected, string actual, string context)
    {
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{context} expected={expected} actual={actual}");
    }

    private sealed record SimilaritySeedVector(
        int Iteration,
        float X,
        float Uei,
        float Theta,
        float Dstar,
        float Ampl,
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
}
