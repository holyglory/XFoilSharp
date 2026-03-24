using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLVAR turbulent DI chain
// Secondary legacy source: tools/fortran-debug/di_turbulent_parity_driver.f90
// Role in port: Verifies the managed turbulent dissipation chain against a standalone Fortran micro-driver after the wall-only and outer-layer producer blocks have been isolated separately.
// Differences: The harness is managed-only infrastructure, but it compares raw IEEE-754 words for the final DI/S/T/D/U/MS outputs produced by ComputeDiChains(..., ityp=2).
// Decision: Keep the micro-driver because it pins the remaining turbulent DI composition to a fast oracle instead of long solver traces.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class TurbulentDiChainFortranParityTests
{
    private static readonly string[] StageFields =
    {
        "di", "diS", "diT", "diD", "diU", "diMs"
    };

    private static readonly string[] FinalFields =
    {
        "di", "diS", "diT", "diD", "diU", "diMs"
    };

    [Fact]
    public void TurbulentDiChainBatch_BitwiseMatchesFortranDriver()
    {
        IReadOnlyList<FortranDiTurbulentCase> cases = BuildCases();
        FortranDiTurbulentResult fortran = FortranDiTurbulentDriver.RunBatch(cases);
        ManagedDiTurbulentResult managed = RunManagedBatch(cases);

        AssertRecordsEqual("WALL", StageFields, cases, fortran.Walls, managed.Walls);
        AssertRecordsEqual("DFAC", StageFields, cases, fortran.Dfacs, managed.Dfacs);
        AssertRecordsEqual("DD", StageFields, cases, fortran.DdTerms, managed.DdTerms);
        AssertRecordsEqual("POSTDD", StageFields, cases, fortran.PostDds, managed.PostDds);
        AssertRecordsEqual("DDL", StageFields, cases, fortran.DdlTerms, managed.DdlTerms);
        AssertRecordsEqual("POSTDDL", StageFields, cases, fortran.PostDdls, managed.PostDdls);
        AssertRecordsEqual("DIL", StageFields, cases, fortran.Dils, managed.Dils);
        AssertRecordsEqual("FINAL", FinalFields, cases, fortran.Finals, managed.Finals);
    }

    private static void AssertRecordsEqual(
        string kind,
        IReadOnlyList<string> fieldNames,
        IReadOnlyList<FortranDiTurbulentCase> cases,
        IReadOnlyList<DiTurbulentHexRecord> expectedRecords,
        IReadOnlyList<DiTurbulentHexRecord> actualRecords)
    {
        Assert.Equal(expectedRecords.Count, actualRecords.Count);
        for (int caseIndex = 0; caseIndex < expectedRecords.Count; caseIndex++)
        {
            DiTurbulentHexRecord expected = expectedRecords[caseIndex];
            DiTurbulentHexRecord actual = actualRecords[caseIndex];

            for (int fieldIndex = 0; fieldIndex < expected.Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected.Values[fieldIndex], actual.Values[fieldIndex], StringComparison.Ordinal),
                    $"{kind} hk={cases[caseIndex].Hk.ToString("R", CultureInfo.InvariantCulture)} hs={cases[caseIndex].Hs.ToString("R", CultureInfo.InvariantCulture)} us={cases[caseIndex].Us.ToString("R", CultureInfo.InvariantCulture)} rt={cases[caseIndex].Rt.ToString("R", CultureInfo.InvariantCulture)} s={cases[caseIndex].S.ToString("R", CultureInfo.InvariantCulture)} msq={cases[caseIndex].Msq.ToString("R", CultureInfo.InvariantCulture)} field={fieldNames[fieldIndex]} expected={expected.Values[fieldIndex]} actual={actual.Values[fieldIndex]}");
            }
        }
    }

    [Fact]
    public void TurbulentDiChain_UsesMachSensitivitiesInDefaultAndParityPaths()
    {
        var baseline = new FortranDiTurbulentCase(
            Hk: 1.3886719f,
            Hs: 2.40625f,
            Us: 0.4189453f,
            Rt: 4200.0f,
            S: 0.15625f,
            Msq: 0.1875f,
            HkT: 0.0625f,
            HkD: -0.015625f,
            HkU: 0.0078125f,
            HkMs: -0.00390625f,
            HsT: 0.015625f,
            HsD: 0.001953125f,
            HsU: -0.03125f,
            HsMs: 0.01171875f,
            UsT: -0.0234375f,
            UsD: 0.009765625f,
            UsU: -0.015625f,
            UsMs: 0.0078125f,
            RtT: 22.0f,
            RtU: -10.0f,
            RtMs: 7.0f,
            MU: 0.0f,
            MMs: 0.0f);
        var withMachSensitivity = baseline with { MU = 0.25f, MMs = -0.125f };

        (double baselineDefaultU, double baselineDefaultMs) = InvokeManagedDiChain(baseline, useLegacyPrecision: false);
        (double shiftedDefaultU, double shiftedDefaultMs) = InvokeManagedDiChain(withMachSensitivity, useLegacyPrecision: false);
        (double baselineParityU, double baselineParityMs) = InvokeManagedDiChain(baseline, useLegacyPrecision: true);
        (double shiftedParityU, double shiftedParityMs) = InvokeManagedDiChain(withMachSensitivity, useLegacyPrecision: true);

        Assert.NotEqual(baselineDefaultU, shiftedDefaultU);
        Assert.NotEqual(baselineDefaultMs, shiftedDefaultMs);
        Assert.NotEqual(baselineParityU, shiftedParityU);
        Assert.NotEqual(baselineParityMs, shiftedParityMs);
    }

    private static IReadOnlyList<FortranDiTurbulentCase> BuildCases()
    {
        var cases = new List<FortranDiTurbulentCase>
        {
            new(1.3886719f, 2.40625f, 0.4189453f, 4200.0f, 0.15625f, 0.1875f, 0.0625f, -0.015625f, 0.0078125f, -0.00390625f, 0.015625f, 0.001953125f, -0.03125f, 0.01171875f, -0.0234375f, 0.009765625f, -0.015625f, 0.0078125f, 22.0f, -10.0f, 7.0f, 0.0625f, -0.015625f),
            new(1.85007f, 2.71875f, 0.5625f, 7873.0073f, 0.53125f, 0.30803722f, 0.5f, -0.75f, 0.375f, -0.125f, 0.1875f, -0.3125f, 0.125f, 0.046875f, -0.0625f, 0.09375f, 0.03125f, -0.015625f, 48.0f, -24.0f, 17.0f, 0.09375f, -0.03125f),
            new(3.1226902f, 2.1875f, 0.703125f, 3900.4304f, 0.8125f, 0.5048134f, -0.28125f, 0.40625f, -0.15625f, 0.0625f, 0.125f, -0.1875f, 0.078125f, -0.03125f, 0.046875f, -0.0625f, 0.0234375f, 0.01171875f, -19.0f, 9.0f, -6.0f, 0.046875f, 0.015625f)
        };

        var random = new Random(20260317);
        for (int i = 0; i < 1024; i++)
        {
            cases.Add(new FortranDiTurbulentCase(
                Hk: 1.02f + ((float)random.NextDouble() * 5.0f),
                Hs: 1.3f + ((float)random.NextDouble() * 4.0f),
                Us: (float)random.NextDouble() * 0.94f,
                Rt: 18.0f + ((float)random.NextDouble() * 9000.0f),
                S: 0.01f + ((float)random.NextDouble() * 1.35f),
                Msq: (float)random.NextDouble() * 0.85f,
                HkT: RandomSigned(random, 2.5f),
                HkD: RandomSigned(random, 2.5f),
                HkU: RandomSigned(random, 2.5f),
                HkMs: RandomSigned(random, 2.5f),
                HsT: RandomSigned(random, 2.5f),
                HsD: RandomSigned(random, 2.5f),
                HsU: RandomSigned(random, 2.5f),
                HsMs: RandomSigned(random, 2.5f),
                UsT: RandomSigned(random, 1.5f),
                UsD: RandomSigned(random, 1.5f),
                UsU: RandomSigned(random, 1.5f),
                UsMs: RandomSigned(random, 1.5f),
                RtT: RandomSigned(random, 140.0f),
                RtU: RandomSigned(random, 140.0f),
                RtMs: RandomSigned(random, 140.0f),
                MU: RandomSigned(random, 0.75f),
                MMs: RandomSigned(random, 0.75f)));
        }

        return cases;
    }

    private static float RandomSigned(Random random, float amplitude)
        => (((float)random.NextDouble() * 2.0f) - 1.0f) * amplitude;

    private static ManagedDiTurbulentResult RunManagedBatch(IReadOnlyList<FortranDiTurbulentCase> cases)
    {
        MethodInfo wallMethod = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeTurbulentWallDiContributionLegacy",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeTurbulentWallDiContributionLegacy method not found.");
        MethodInfo dfacMethod = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ApplyTurbulentDiDfacCorrectionLegacy",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ApplyTurbulentDiDfacCorrectionLegacy method not found.");
        MethodInfo outerMethod = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeOuterLayerDiContributionLegacy",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeOuterLayerDiContributionLegacy method not found.");

        var walls = new List<DiTurbulentHexRecord>(cases.Count);
        var dfacs = new List<DiTurbulentHexRecord>(cases.Count);
        var ddTerms = new List<DiTurbulentHexRecord>(cases.Count);
        var postDds = new List<DiTurbulentHexRecord>(cases.Count);
        var ddlTerms = new List<DiTurbulentHexRecord>(cases.Count);
        var postDdls = new List<DiTurbulentHexRecord>(cases.Count);
        var dils = new List<DiTurbulentHexRecord>(cases.Count);
        var finals = new List<DiTurbulentHexRecord>(cases.Count);

        foreach (FortranDiTurbulentCase @case in cases)
        {
            object?[] wallArgs =
            {
                @case.Hk,
                @case.Hs,
                @case.Us,
                @case.Rt,
                @case.Msq,
                @case.HkT,
                @case.HkD,
                @case.HkU,
                @case.HkMs,
                @case.HsT,
                @case.HsD,
                @case.HsU,
                @case.HsMs,
                @case.UsT,
                @case.UsD,
                @case.UsU,
                @case.UsMs,
                @case.RtT,
                @case.RtU,
                @case.RtMs,
                @case.MU,
                @case.MMs,
                null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null
            };
            wallMethod.Invoke(null, wallArgs);

            float wallDi = (float)wallArgs[30]!;
            float wallT = (float)wallArgs[34]!;
            float wallD = (float)wallArgs[35]!;
            float wallU = (float)wallArgs[36]!;
            float wallMs = (float)wallArgs[37]!;
            walls.Add(CreateRecord(wallDi, 0.0f, wallT, wallD, wallU, wallMs));

            object?[] dfacArgs =
            {
                @case.Hk,
                @case.Rt,
                @case.HkT,
                @case.HkD,
                @case.HkU,
                @case.HkMs,
                @case.RtT,
                @case.RtU,
                @case.RtMs,
                wallDi,
                0.0f,
                wallT,
                wallD,
                wallU,
                wallMs,
                null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null
            };
            dfacMethod.Invoke(null, dfacArgs);

            float di = (float)dfacArgs[15]!;
            float diS = (float)dfacArgs[16]!;
            float diT = (float)dfacArgs[17]!;
            float diD = (float)dfacArgs[18]!;
            float diU = (float)dfacArgs[19]!;
            float diMs = (float)dfacArgs[20]!;
            dfacs.Add(CreateRecord(di, diS, diT, diD, diU, diMs));

            object?[] outerArgs =
            {
                @case.S,
                @case.Hs,
                @case.Us,
                @case.Rt,
                @case.HsT,
                @case.HsD,
                @case.HsU,
                @case.HsMs,
                @case.UsT,
                @case.UsD,
                @case.UsU,
                @case.UsMs,
                @case.RtT,
                @case.RtU,
                @case.RtMs,
                null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null
            };
            outerMethod.Invoke(null, outerArgs);

            float dd = (float)outerArgs[15]!;
            float ddHs = (float)outerArgs[16]!;
            float ddUs = (float)outerArgs[17]!;
            float ddS = (float)outerArgs[18]!;
            float ddT = (float)outerArgs[19]!;
            float ddD = (float)outerArgs[20]!;
            float ddU = (float)outerArgs[21]!;
            float ddMs = (float)outerArgs[22]!;
            ddTerms.Add(CreateRecord(dd, ddS, ddT, ddD, ddU, ddMs));

            di += dd;
            diS = ddS;
            diU = (float)LegacyPrecisionMath.Add(diU, ddU, true);
            diT = (float)LegacyPrecisionMath.Add(diT, ddT, true);
            diD = (float)LegacyPrecisionMath.Add(diD, ddD, true);
            diMs = (float)LegacyPrecisionMath.Add(diMs, ddMs, true);
            postDds.Add(CreateRecord(di, diS, diT, diD, diU, diMs));

            float ddl = (float)outerArgs[23]!;
            float ddlHs = (float)outerArgs[24]!;
            float ddlUs = (float)outerArgs[25]!;
            float ddlRt = (float)outerArgs[26]!;
            float ddlT = (float)outerArgs[27]!;
            float ddlD = (float)outerArgs[28]!;
            float ddlU = (float)outerArgs[29]!;
            float ddlMs = (float)outerArgs[30]!;
            ddlTerms.Add(CreateRecord(ddl, 0.0f, ddlT, ddlD, ddlU, ddlMs));

            di += ddl;
            diU = (float)LegacyPrecisionMath.Add(diU, ddlU, true);
            diT = (float)LegacyPrecisionMath.Add(diT, ddlT, true);
            diD = (float)LegacyPrecisionMath.Add(diD, ddlD, true);
            diMs = (float)LegacyPrecisionMath.Add(diMs, ddlMs, true);
            postDdls.Add(CreateRecord(di, diS, diT, diD, diU, diMs));

            var (dilRaw, dilHkRaw, dilRtRaw) = BoundaryLayerCorrelations.LaminarDissipation(@case.Hk, @case.Rt, useLegacyPrecision: true);
            float dil = (float)dilRaw;
            float dilHk = (float)dilHkRaw;
            float dilRt = (float)dilRtRaw;
            float dilU = (float)LegacyPrecisionMath.MultiplyAdd(dilHk, @case.HkU, LegacyPrecisionMath.Multiply(dilRt, @case.RtU, true), true);
            float dilT = (float)LegacyPrecisionMath.MultiplyAdd(dilHk, @case.HkT, LegacyPrecisionMath.Multiply(dilRt, @case.RtT, true), true);
            float dilD = dilHk * @case.HkD;
            float dilMs = (float)LegacyPrecisionMath.MultiplyAdd(dilHk, @case.HkMs, LegacyPrecisionMath.Multiply(dilRt, @case.RtMs, true), true);
            dils.Add(CreateRecord(dil, 0.0f, dilT, dilD, dilU, dilMs));

            if (dil > di)
            {
                di = dil;
                diS = 0.0f;
                diT = dilT;
                diD = dilD;
                diU = dilU;
                diMs = dilMs;
            }

            finals.Add(CreateRecord(di, diS, diT, diD, diU, diMs));
        }

        return new ManagedDiTurbulentResult(walls, dfacs, ddTerms, postDds, ddlTerms, postDdls, dils, finals);
    }

    private static DiTurbulentHexRecord CreateRecord(float di, float diS, float diT, float diD, float diU, float diMs)
        => new(new[]
        {
            ToHex(di),
            ToHex(diS),
            ToHex(diT),
            ToHex(diD),
            ToHex(diU),
            ToHex(diMs)
        });

    private static (double DiU, double DiMs) InvokeManagedDiChain(FortranDiTurbulentCase @case, bool useLegacyPrecision)
    {
        var result = InvokeManagedDiChainRecord(@case, useLegacyPrecision);
        return (result.DiU, result.DiMs);
    }

    private static (double DiU, double DiMs, DiTurbulentHexRecord Final) InvokeManagedDiChainRecord(FortranDiTurbulentCase @case, bool useLegacyPrecision)
    {
        MethodInfo method = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeDiChains",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeDiChains method not found.");

        object?[] args =
        {
            2,
            (double)@case.Hk,
            (double)@case.Hs,
            (double)@case.Us,
            (double)@case.Hs,
            (double)@case.Rt,
            (double)@case.S,
            (double)@case.Msq,
            (double)@case.HkT,
            (double)@case.HkD,
            (double)@case.HkU,
            (double)@case.HkMs,
            (double)@case.HsT,
            (double)@case.HsD,
            (double)@case.HsU,
            (double)@case.HsMs,
            0.0,
            (double)@case.UsT,
            (double)@case.UsD,
            (double)@case.UsU,
            (double)@case.UsMs,
            (double)@case.RtT,
            (double)@case.RtU,
            (double)@case.RtMs,
            (double)@case.MU,
            (double)@case.MMs,
            null,
            null,
            null,
            null,
            null,
            null,
            useLegacyPrecision,
            0
        };

        method.Invoke(null, args);

        return (
            DiU: (double)args[30]!,
            DiMs: (double)args[31]!,
            Final: CreateRecord(
                (float)(double)args[26]!,
                (float)(double)args[27]!,
                (float)(double)args[28]!,
                (float)(double)args[29]!,
                (float)(double)args[30]!,
                (float)(double)args[31]!));
    }

    private static string ToHex(float value)
        => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);

    private sealed record ManagedDiTurbulentResult(
        IReadOnlyList<DiTurbulentHexRecord> Walls,
        IReadOnlyList<DiTurbulentHexRecord> Dfacs,
        IReadOnlyList<DiTurbulentHexRecord> DdTerms,
        IReadOnlyList<DiTurbulentHexRecord> PostDds,
        IReadOnlyList<DiTurbulentHexRecord> DdlTerms,
        IReadOnlyList<DiTurbulentHexRecord> PostDdls,
        IReadOnlyList<DiTurbulentHexRecord> Dils,
        IReadOnlyList<DiTurbulentHexRecord> Finals);
}
