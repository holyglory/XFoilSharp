using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLVAR turbulent wall DI contribution (CF2T + DI2 before DFAC/DD/DDL)
// Secondary legacy source: tools/fortran-debug/di_wall_parity_driver.f90
// Role in port: Verifies the extracted turbulent wall DI contribution helper against a standalone Fortran micro-driver instead of rediscovering CF2T/DI wall drift through the full turbulent DI path.
// Differences: The harness is managed-only infrastructure, but it compares raw IEEE-754 words for the CF2T chain and the wall-only DI partials/finals.
// Decision: Keep the micro-driver because this block sits directly on the turbulent DI producer path and now has explicit `M2_U2`/`M2_MS` propagation that needs proof.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class TurbulentWallDiFortranParityTests
{
    private static readonly string[] CfFields =
    {
        "cf2t", "cf2tHk", "cf2tRt", "cf2tM", "cf2tT", "cf2tD", "cf2tU", "cf2tMs"
    };

    private static readonly string[] DiFields =
    {
        "di", "diHs", "diUs", "diCf2t", "diT", "diD", "diU", "diMs"
    };

    [Fact]
    public void TurbulentWallDiBatch_BitwiseMatchesFortranDriver()
    {
        IReadOnlyList<FortranDiWallCase> cases = BuildCases();
        FortranDiWallResult fortran = FortranDiWallDriver.RunBatch(cases);
        ManagedDiWallResult managed = RunManagedBatch(cases);

        Assert.Equal(fortran.CfTerms.Count, managed.CfTerms.Count);
        for (int caseIndex = 0; caseIndex < fortran.CfTerms.Count; caseIndex++)
        {
            CompareRecord("CF", CfFields, cases[caseIndex], fortran.CfTerms[caseIndex], managed.CfTerms[caseIndex]);
            CompareRecord("DI", DiFields, cases[caseIndex], fortran.DiTerms[caseIndex], managed.DiTerms[caseIndex]);
        }
    }

    private static IReadOnlyList<FortranDiWallCase> BuildCases()
    {
        var cases = new List<FortranDiWallCase>
        {
            new(1.3886719f, 2.40625f, 0.4189453f, 4200.0f, 0.1875f, 0.15625f, -0.28125f, 0.09375f, 0.03125f, -0.125f, 0.21875f, 0.0625f, -0.015625f, 0.0078125f, -0.00390625f, 0.015625f, 0.001953125f, 22.0f, -10.0f, 7.0f, 0.0625f, -0.015625f),
            new(1.85007f, 2.71875f, 0.5625f, 7873.0073f, 0.30803722f, 0.5f, -0.75f, 0.375f, -0.125f, 0.1875f, -0.3125f, 0.125f, 0.046875f, -0.0625f, 0.09375f, 0.03125f, -0.015625f, 48.0f, -24.0f, 17.0f, 0.09375f, -0.03125f),
            new(3.1226902f, 2.1875f, 0.703125f, 3900.4304f, 0.5048134f, -0.28125f, 0.40625f, -0.15625f, 0.0625f, 0.125f, -0.1875f, 0.078125f, -0.03125f, 0.046875f, -0.0625f, 0.0234375f, 0.01171875f, -19.0f, 9.0f, -6.0f, 0.046875f, 0.015625f)
        };

        var random = new Random(20260317);
        for (int i = 0; i < 1024; i++)
        {
            cases.Add(new FortranDiWallCase(
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

    private static ManagedDiWallResult RunManagedBatch(IReadOnlyList<FortranDiWallCase> cases)
    {
        MethodInfo method = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeTurbulentWallDiContributionLegacy",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeTurbulentWallDiContributionLegacy method not found.");

        var cfTerms = new List<DiWallHexRecord>(cases.Count);
        var diTerms = new List<DiWallHexRecord>(cases.Count);

        foreach (FortranDiWallCase @case in cases)
        {
            object?[] args =
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

            method.Invoke(null, args);

            cfTerms.Add(new DiWallHexRecord(new[]
            {
                ToHex((float)args[22]!),
                ToHex((float)args[23]!),
                ToHex((float)args[24]!),
                ToHex((float)args[25]!),
                ToHex((float)args[26]!),
                ToHex((float)args[27]!),
                ToHex((float)args[28]!),
                ToHex((float)args[29]!)
            }));

            diTerms.Add(new DiWallHexRecord(new[]
            {
                ToHex((float)args[30]!),
                ToHex((float)args[31]!),
                ToHex((float)args[32]!),
                ToHex((float)args[33]!),
                ToHex((float)args[34]!),
                ToHex((float)args[35]!),
                ToHex((float)args[36]!),
                ToHex((float)args[37]!)
            }));
        }

        return new ManagedDiWallResult(cfTerms, diTerms);
    }

    private static void CompareRecord(
        string kind,
        IReadOnlyList<string> fieldNames,
        FortranDiWallCase @case,
        DiWallHexRecord expected,
        DiWallHexRecord actual)
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

    private sealed record ManagedDiWallResult(
        IReadOnlyList<DiWallHexRecord> CfTerms,
        IReadOnlyList<DiWallHexRecord> DiTerms);
}
