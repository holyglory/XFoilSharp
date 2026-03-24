using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLVAR DD/DDL outer-layer DI add-ons
// Secondary legacy source: tools/fortran-debug/di_outer_parity_driver.f90
// Role in port: Verifies the managed BLVAR outer-layer dissipation add-ons against a standalone Fortran micro-driver instead of finding DD/DDL drift through full DI or BLDIF traces.
// Differences: The harness is managed-only infrastructure, but it compares raw IEEE-754 words for the DD and DDL terms and their chained derivatives.
// Decision: Keep the micro-driver because it isolates the next turbulent DI producer block after the proven wall-only helper.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class TurbulentOuterDiFortranParityTests
{
    private static readonly string[] DdFields =
    {
        "dd", "ddHs", "ddUs", "ddS", "ddT", "ddD", "ddU", "ddMs"
    };

    private static readonly string[] DdlFields =
    {
        "ddl", "ddlHs", "ddlUs", "ddlRt", "ddlT", "ddlD", "ddlU", "ddlMs"
    };

    [Fact]
    public void TurbulentOuterDiBatch_BitwiseMatchesFortranDriver()
    {
        IReadOnlyList<FortranDiOuterCase> cases = BuildCases();
        FortranDiOuterResult fortran = FortranDiOuterDriver.RunBatch(cases);
        ManagedOuterDiResult managed = RunManagedBatch(cases);

        Assert.Equal(fortran.DdTerms.Count, managed.DdTerms.Count);
        for (int caseIndex = 0; caseIndex < fortran.DdTerms.Count; caseIndex++)
        {
            CompareRecord("DD", DdFields, cases[caseIndex], fortran.DdTerms[caseIndex], managed.DdTerms[caseIndex]);
            CompareRecord("DDL", DdlFields, cases[caseIndex], fortran.DdlTerms[caseIndex], managed.DdlTerms[caseIndex]);
        }
    }

    private static IReadOnlyList<FortranDiOuterCase> BuildCases()
    {
        var cases = new List<FortranDiOuterCase>
        {
            new(0.15625f, 2.40625f, 0.4189453f, 4200.0f, 0.0625f, -0.015625f, 0.0078125f, -0.00390625f, 0.015625f, 0.001953125f, -0.03125f, 0.01171875f, 22.0f, -10.0f, 7.0f),
            new(0.53125f, 2.71875f, 0.5625f, 7873.0073f, 0.125f, 0.046875f, -0.0625f, 0.09375f, 0.03125f, -0.015625f, 0.0625f, -0.046875f, 48.0f, -24.0f, 17.0f),
            new(0.8125f, 2.1875f, 0.703125f, 3900.4304f, -0.078125f, -0.03125f, 0.046875f, -0.0625f, 0.0234375f, 0.01171875f, -0.015625f, 0.03125f, -19.0f, 9.0f, -6.0f)
        };

        var random = new Random(20260317);
        for (int i = 0; i < 1024; i++)
        {
            cases.Add(new FortranDiOuterCase(
                S: 0.01f + ((float)random.NextDouble() * 1.35f),
                Hs: 1.3f + ((float)random.NextDouble() * 4.0f),
                Us: (float)random.NextDouble() * 0.94f,
                Rt: 18.0f + ((float)random.NextDouble() * 9000.0f),
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
                RtMs: RandomSigned(random, 140.0f)));
        }

        return cases;
    }

    private static float RandomSigned(Random random, float amplitude)
        => (((float)random.NextDouble() * 2.0f) - 1.0f) * amplitude;

    private static ManagedOuterDiResult RunManagedBatch(IReadOnlyList<FortranDiOuterCase> cases)
    {
        MethodInfo method = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeOuterLayerDiContributionLegacy",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeOuterLayerDiContributionLegacy method not found.");

        var ddTerms = new List<DiOuterHexRecord>(cases.Count);
        var ddlTerms = new List<DiOuterHexRecord>(cases.Count);

        foreach (FortranDiOuterCase @case in cases)
        {
            object?[] args =
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

            method.Invoke(null, args);

            ddTerms.Add(new DiOuterHexRecord(new[]
            {
                ToHex((float)args[15]!),
                ToHex((float)args[16]!),
                ToHex((float)args[17]!),
                ToHex((float)args[18]!),
                ToHex((float)args[19]!),
                ToHex((float)args[20]!),
                ToHex((float)args[21]!),
                ToHex((float)args[22]!)
            }));

            ddlTerms.Add(new DiOuterHexRecord(new[]
            {
                ToHex((float)args[23]!),
                ToHex((float)args[24]!),
                ToHex((float)args[25]!),
                ToHex((float)args[26]!),
                ToHex((float)args[27]!),
                ToHex((float)args[28]!),
                ToHex((float)args[29]!),
                ToHex((float)args[30]!)
            }));
        }

        return new ManagedOuterDiResult(ddTerms, ddlTerms);
    }

    private static void CompareRecord(
        string kind,
        IReadOnlyList<string> fieldNames,
        FortranDiOuterCase @case,
        DiOuterHexRecord expected,
        DiOuterHexRecord actual)
    {
        for (int fieldIndex = 0; fieldIndex < expected.Values.Count; fieldIndex++)
        {
            Assert.True(
                string.Equals(expected.Values[fieldIndex], actual.Values[fieldIndex], StringComparison.Ordinal),
                $"{kind} s={@case.S.ToString("R", CultureInfo.InvariantCulture)} hs={@case.Hs.ToString("R", CultureInfo.InvariantCulture)} us={@case.Us.ToString("R", CultureInfo.InvariantCulture)} rt={@case.Rt.ToString("R", CultureInfo.InvariantCulture)} field={fieldNames[fieldIndex]} expected={expected.Values[fieldIndex]} actual={actual.Values[fieldIndex]}");
        }
    }

    private static string ToHex(float value)
        => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);

    private sealed record ManagedOuterDiResult(
        IReadOnlyList<DiOuterHexRecord> DdTerms,
        IReadOnlyList<DiOuterHexRecord> DdlTerms);
}
