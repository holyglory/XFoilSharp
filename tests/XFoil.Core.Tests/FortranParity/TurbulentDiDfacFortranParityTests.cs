using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLVAR DFAC correction block
// Secondary legacy source: tools/fortran-debug/di_dfac_parity_driver.f90
// Role in port: Verifies the managed low-Hk DFAC correction against a standalone Fortran micro-driver after the wall-only producer block has already been proven separately.
// Differences: The harness is managed-only infrastructure, but it compares raw IEEE-754 words for the DFAC trace scalars and the corrected DI/S/T/D/U/MS outputs.
// Decision: Keep the micro-driver because it isolates the remaining turbulent-DI wall correction without paying full-solver trace costs.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class TurbulentDiDfacFortranParityTests
{
    private static readonly string[] TermsFields =
    {
        "grt", "hmin", "hmRt", "fl", "dfac", "dfHk", "dfRt", "dfTermD"
    };

    private static readonly string[] FinalFields =
    {
        "di", "diS", "diT", "diD", "diU", "diMs"
    };

    [Fact]
    public void TurbulentDiDfacBatch_BitwiseMatchesFortranDriver()
    {
        IReadOnlyList<FortranDiDfacCase> cases = BuildCases();
        FortranDiDfacResult fortran = FortranDiDfacDriver.RunBatch(cases);
        ManagedDiDfacResult managed = RunManagedBatch(cases);

        Assert.Equal(fortran.Terms.Count, managed.Terms.Count);
        for (int caseIndex = 0; caseIndex < fortran.Terms.Count; caseIndex++)
        {
            CompareRecord("TERMS", TermsFields, cases[caseIndex], fortran.Terms[caseIndex], managed.Terms[caseIndex]);
            CompareRecord("FINAL", FinalFields, cases[caseIndex], fortran.Finals[caseIndex], managed.Finals[caseIndex]);
        }
    }

    private static IReadOnlyList<FortranDiDfacCase> BuildCases()
    {
        var cases = new List<FortranDiDfacCase>
        {
            new(1.3886719f, 2.40625f, 0.4189453f, 4200.0f, 0.1875f, 0.0625f, -0.015625f, 0.0078125f, -0.00390625f, 0.015625f, 0.001953125f, -0.03125f, 0.01171875f, -0.0234375f, 0.009765625f, -0.015625f, 0.0078125f, 22.0f, -10.0f, 7.0f, 0.0625f, -0.015625f),
            new(1.85007f, 2.71875f, 0.5625f, 7873.0073f, 0.30803722f, 0.5f, -0.75f, 0.375f, -0.125f, 0.1875f, -0.3125f, 0.125f, 0.046875f, -0.0625f, 0.09375f, 0.03125f, -0.015625f, 48.0f, -24.0f, 17.0f, 0.09375f, -0.03125f),
            new(3.1226902f, 2.1875f, 0.703125f, 3900.4304f, 0.5048134f, -0.28125f, 0.40625f, -0.15625f, 0.0625f, 0.125f, -0.1875f, 0.078125f, -0.03125f, 0.046875f, -0.0625f, 0.0234375f, 0.01171875f, -19.0f, 9.0f, -6.0f, 0.046875f, 0.015625f)
        };

        var random = new Random(20260317);
        for (int i = 0; i < 1024; i++)
        {
            cases.Add(new FortranDiDfacCase(
                Hk: 1.02f + ((float)random.NextDouble() * 5.0f),
                Hs: 1.3f + ((float)random.NextDouble() * 4.0f),
                Us: (float)random.NextDouble() * 0.94f,
                Rt: 18.0f + ((float)random.NextDouble() * 9000.0f),
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

    private static ManagedDiDfacResult RunManagedBatch(IReadOnlyList<FortranDiDfacCase> cases)
    {
        MethodInfo wallMethod = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeTurbulentWallDiContributionLegacy",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeTurbulentWallDiContributionLegacy method not found.");
        MethodInfo dfacMethod = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ApplyTurbulentDiDfacCorrectionLegacy",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ApplyTurbulentDiDfacCorrectionLegacy method not found.");

        var terms = new List<DiDfacHexRecord>(cases.Count);
        var finals = new List<DiDfacHexRecord>(cases.Count);

        foreach (FortranDiDfacCase @case in cases)
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
                (float)wallArgs[30]!,
                0.0f,
                (float)wallArgs[34]!,
                (float)wallArgs[35]!,
                (float)wallArgs[36]!,
                (float)wallArgs[37]!,
                null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null
            };
            dfacMethod.Invoke(null, dfacArgs);

            terms.Add(new DiDfacHexRecord(new[]
            {
                ToHex((float)dfacArgs[21]!),
                ToHex((float)dfacArgs[22]!),
                ToHex((float)dfacArgs[23]!),
                ToHex((float)dfacArgs[24]!),
                ToHex((float)dfacArgs[25]!),
                ToHex((float)dfacArgs[26]!),
                ToHex((float)dfacArgs[27]!),
                ToHex((float)dfacArgs[29]!)
            }));

            finals.Add(new DiDfacHexRecord(new[]
            {
                ToHex((float)dfacArgs[15]!),
                ToHex((float)dfacArgs[16]!),
                ToHex((float)dfacArgs[17]!),
                ToHex((float)dfacArgs[18]!),
                ToHex((float)dfacArgs[19]!),
                ToHex((float)dfacArgs[20]!)
            }));
        }

        return new ManagedDiDfacResult(terms, finals);
    }

    private static void CompareRecord(
        string kind,
        IReadOnlyList<string> fieldNames,
        FortranDiDfacCase @case,
        DiDfacHexRecord expected,
        DiDfacHexRecord actual)
    {
        for (int fieldIndex = 0; fieldIndex < expected.Values.Count; fieldIndex++)
        {
            Assert.True(
                string.Equals(expected.Values[fieldIndex], actual.Values[fieldIndex], StringComparison.Ordinal),
                $"{kind} hk={@case.Hk.ToString("R", CultureInfo.InvariantCulture)} hs={@case.Hs.ToString("R", CultureInfo.InvariantCulture)} us={@case.Us.ToString("R", CultureInfo.InvariantCulture)} rt={@case.Rt.ToString("R", CultureInfo.InvariantCulture)} msq={@case.Msq.ToString("R", CultureInfo.InvariantCulture)} field={fieldNames[fieldIndex]} expected={expected.Values[fieldIndex]} actual={actual.Values[fieldIndex]}");
        }
    }

    private static string ToHex(float value)
        => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);

    private sealed record ManagedDiDfacResult(
        IReadOnlyList<DiDfacHexRecord> Terms,
        IReadOnlyList<DiDfacHexRecord> Finals);
}
