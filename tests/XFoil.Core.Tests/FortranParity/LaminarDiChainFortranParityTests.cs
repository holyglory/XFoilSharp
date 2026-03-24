using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: DIL plus BLVAR laminar DI chain
// Secondary legacy source: tools/fortran-debug/dil_parity_driver.f90
// Role in port: Verifies the managed laminar dissipation path against a standalone Fortran micro-driver instead of rediscovering DIL drift through laminar seed and transition rows.
// Differences: The harness is managed-only infrastructure, but it compares raw IEEE-754 words for both the DIL partials and the chained T/D/U/MS outputs produced by ComputeDiChains(..., ityp=1).
// Decision: Keep the micro-driver because the laminar DI chain is small, parity-sensitive, and directly upstream of later BLVAR/BLDIF consumers.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class LaminarDiChainFortranParityTests
{
    private static readonly string[] TermsFields =
    {
        "di", "diHk", "diRt"
    };

    private static readonly string[] FinalFields =
    {
        "di", "diT", "diD", "diU", "diMs"
    };

    [Fact]
    public void LaminarDiChainBatch_BitwiseMatchesFortranDriver()
    {
        IReadOnlyList<FortranDilCase> cases = BuildCases();
        FortranDilResult fortran = FortranDilDriver.RunBatch(cases);
        ManagedDilResult managed = RunManagedBatch(cases);

        Assert.Equal(fortran.Terms.Count, managed.Terms.Count);
        for (int caseIndex = 0; caseIndex < fortran.Terms.Count; caseIndex++)
        {
            DilHexRecord expected = fortran.Terms[caseIndex];
            DilHexRecord actual = managed.Terms[caseIndex];

            for (int fieldIndex = 0; fieldIndex < expected.Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected.Values[fieldIndex], actual.Values[fieldIndex], StringComparison.Ordinal),
                    $"TERMS case={caseIndex} hk={cases[caseIndex].Hk.ToString("R", CultureInfo.InvariantCulture)} rt={cases[caseIndex].Rt.ToString("R", CultureInfo.InvariantCulture)} field={TermsFields[fieldIndex]} expected={expected.Values[fieldIndex]} actual={actual.Values[fieldIndex]}");
            }
        }

        Assert.Equal(fortran.Finals.Count, managed.Finals.Count);
        for (int caseIndex = 0; caseIndex < fortran.Finals.Count; caseIndex++)
        {
            DilHexRecord expected = fortran.Finals[caseIndex];
            DilHexRecord actual = managed.Finals[caseIndex];

            for (int fieldIndex = 0; fieldIndex < expected.Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected.Values[fieldIndex], actual.Values[fieldIndex], StringComparison.Ordinal),
                    $"FINAL case={caseIndex} hk={cases[caseIndex].Hk.ToString("R", CultureInfo.InvariantCulture)} rt={cases[caseIndex].Rt.ToString("R", CultureInfo.InvariantCulture)} field={FinalFields[fieldIndex]} expected={expected.Values[fieldIndex]} actual={actual.Values[fieldIndex]}");
            }
        }
    }

    private static IReadOnlyList<FortranDilCase> BuildCases()
    {
        var cases = new List<FortranDilCase>
        {
            new(1.8125f, 620.0f, 0.75f, -1.25f, 0.5f, 0.1875f, 82.0f, -38.0f, 24.0f),
            new(3.984375f, 410.0f, -0.3125f, 0.625f, -0.25f, 0.09375f, -17.0f, 8.0f, -5.0f),
            new(4.125f, 217.0f, 0.40625f, -0.53125f, 0.21875f, -0.078125f, 33.0f, -15.0f, 9.0f),
            new(6.875f, 1450.0f, -0.21875f, 0.34375f, -0.15625f, 0.046875f, -22.0f, 11.0f, -7.0f)
        };

        var random = new Random(20260317);
        for (int i = 0; i < 1024; i++)
        {
            float hk = 1.02f + ((float)random.NextDouble() * 7.0f);
            float rt = 40.0f + ((float)random.NextDouble() * 9000.0f);

            cases.Add(new FortranDilCase(
                hk,
                rt,
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 140.0f),
                RandomSigned(random, 140.0f),
                RandomSigned(random, 140.0f)));
        }

        return cases;
    }

    private static float RandomSigned(Random random, float amplitude)
        => (((float)random.NextDouble() * 2.0f) - 1.0f) * amplitude;

    private static ManagedDilResult RunManagedBatch(IReadOnlyList<FortranDilCase> cases)
    {
        MethodInfo method = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeDiChains",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeDiChains method not found.");

        var terms = new List<DilHexRecord>(cases.Count);
        var finals = new List<DilHexRecord>(cases.Count);

        foreach (FortranDilCase @case in cases)
        {
            var (dil, dilHk, dilRt) = BoundaryLayerCorrelations.LaminarDissipation(@case.Hk, @case.Rt, useLegacyPrecision: true);

            object?[] args =
            {
                1,
                (double)@case.Hk,
                1.75,
                0.35,
                1.5,
                (double)@case.Rt,
                0.02,
                0.1,
                (double)@case.HkT,
                (double)@case.HkD,
                (double)@case.HkU,
                (double)@case.HkMs,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                (double)@case.RtT,
                (double)@case.RtU,
                (double)@case.RtMs,
                0.0,
                0.0,
                null,
                null,
                null,
                null,
                null,
                null,
                true,
                0
            };

            method.Invoke(null, args);

            terms.Add(new DilHexRecord(
                new[]
                {
                    ToHex((float)dil),
                    ToHex((float)dilHk),
                    ToHex((float)dilRt)
                }));

            finals.Add(new DilHexRecord(
                new[]
                {
                    ToHex((float)(double)args[26]!),
                    ToHex((float)(double)args[28]!),
                    ToHex((float)(double)args[29]!),
                    ToHex((float)(double)args[30]!),
                    ToHex((float)(double)args[31]!)
                }));
        }

        return new ManagedDilResult(terms, finals);
    }

    private static string ToHex(float value)
        => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);

    private sealed record ManagedDilResult(
        IReadOnlyList<DilHexRecord> Terms,
        IReadOnlyList<DilHexRecord> Finals);
}
