using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLVAR CQ2 chain
// Secondary legacy source: tools/fortran-debug/cq_parity_driver.f90
// Role in port: Verifies the managed CQ chain replay against a standalone Fortran micro-driver instead of rediscovering CQ drift through BLDIF rows.
// Differences: The harness is managed-only infrastructure, but it compares raw IEEE-754 words for the CQ terms, derivative terms, and final chained outputs.
// Decision: Keep the micro-driver because CQ feeds the current alpha-10 producer boundary (`cq1D1` / `cq2D2`) directly.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class CqChainFortranParityTests
{
    private static readonly string[] TermsFields =
    {
        "hkc", "hkb", "usb", "num", "den", "ratio", "cq"
    };

    private static readonly string[] DerivativeFields =
    {
        "cqHs", "cqUs", "cqHk", "cqH", "cqRt",
        "cqHkTerm1", "cqHkTerm2", "cqHkTerm3",
        "cqTermHsT", "cqTermUsT", "cqTermHkT", "cqTermHT", "cqTermRtT",
        "cqTermHsD", "cqTermUsD", "cqTermHkD", "cqTermHD",
        "cqTermHsU", "cqTermUsU", "cqTermHkU", "cqTermRtU",
        "cqTermHsMs", "cqTermUsMs", "cqTermHkMs", "cqTermRtMs",
        "cqT", "cqD"
    };

    private static readonly string[] FinalFields =
    {
        "cq", "cqT", "cqD", "cqU", "cqMs"
    };

    [Fact]
    public void CqChainBatch_BitwiseMatchesFortranDriver()
    {
        IReadOnlyList<FortranCqCase> cases = BuildCases();
        FortranCqResult fortran = FortranCqDriver.RunBatch(cases);
        ManagedCqResult managed = RunManagedBatch(cases);

        AssertRecordsEqual(fortran.Terms, managed.Terms, "TERMS", TermsFields);
        AssertRecordsEqual(fortran.DerivativeTerms, managed.DerivativeTerms, "DTERM", DerivativeFields);
        AssertRecordsEqual(fortran.Finals, managed.Finals, "FINAL", FinalFields);
    }

    private static IReadOnlyList<FortranCqCase> BuildCases()
    {
        var cases = new List<FortranCqCase>
        {
            new(
                FlowType: 2,
                Hk: 1.7324219f,
                Hs: 1.9951172f,
                Us: 0.4189453f,
                H: 1.4619141f,
                Rt: 642.5f,
                HkT: 0.8125f,
                HkD: -1.4375f,
                HkU: 0.59375f,
                HkMs: 0.21875f,
                HsT: 0.53125f,
                HsD: -0.40625f,
                HsU: 0.28125f,
                HsMs: 0.171875f,
                UsT: -0.0625f,
                UsD: 0.125f,
                UsU: 0.34375f,
                UsMs: -0.046875f,
                HT: 0.6875f,
                HD: -0.5625f,
                RtT: 91.0f,
                RtU: -44.0f,
                RtMs: 38.0f),
            new(
                FlowType: 3,
                Hk: 1.0249023f,
                Hs: 2.40625f,
                Us: 0.9921875f,
                H: 1.8222656f,
                Rt: 3800.0f,
                HkT: 0.0625f,
                HkD: 0.09375f,
                HkU: -0.03125f,
                HkMs: 0.015625f,
                HsT: -0.125f,
                HsD: 0.21875f,
                HsU: 0.0625f,
                HsMs: -0.015625f,
                UsT: 0.0078125f,
                UsD: -0.00390625f,
                UsU: 0.015625f,
                UsMs: 0.001953125f,
                HT: -0.09375f,
                HD: 0.15625f,
                RtT: 12.0f,
                RtU: -6.0f,
                RtMs: 4.0f)
        };

        var random = new Random(20260317);
        for (int i = 0; i < 1024; i++)
        {
            int flowType = (i % 3) switch
            {
                0 => 1,
                1 => 2,
                _ => 3
            };

            float hk = 1.02f + ((float)random.NextDouble() * 5.0f);
            float hs = 1.2f + ((float)random.NextDouble() * 4.0f);
            float us = flowType == 3
                ? 0.70f + ((float)random.NextDouble() * 0.299f)
                : (float)random.NextDouble() * 0.94f;
            float h = 1.1f + ((float)random.NextDouble() * 3.5f);
            float rt = 80.0f + ((float)random.NextDouble() * 8000.0f);

            cases.Add(new FortranCqCase(
                flowType,
                hk,
                hs,
                us,
                h,
                rt,
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 1.5f),
                RandomSigned(random, 1.5f),
                RandomSigned(random, 1.5f),
                RandomSigned(random, 1.5f),
                RandomSigned(random, 2.0f),
                RandomSigned(random, 2.0f),
                RandomSigned(random, 120.0f),
                RandomSigned(random, 120.0f),
                RandomSigned(random, 120.0f)));
        }

        return cases;
    }

    private static float RandomSigned(Random random, float amplitude)
        => (((float)random.NextDouble() * 2.0f) - 1.0f) * amplitude;

    private static ManagedCqResult RunManagedBatch(IReadOnlyList<FortranCqCase> cases)
    {
        MethodInfo method = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeCqChains",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeCqChains method not found.");

        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-cq-trace-{Guid.NewGuid():N}.jsonl");

        try
        {
            using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "cq-micro" });
            using var traceScope = SolverTrace.Begin(traceWriter);

            var finals = new List<CqHexRecord>(cases.Count);
            foreach (FortranCqCase @case in cases)
            {
                object?[] args =
                {
                    (double)@case.Hk, (double)@case.Hs, (double)@case.Us, (double)@case.H, (double)@case.Rt, @case.FlowType,
                    (double)@case.HkT, (double)@case.HkD, (double)@case.HkU, (double)@case.HkMs,
                    (double)@case.HsT, (double)@case.HsD, (double)@case.HsU, (double)@case.HsMs,
                    (double)@case.UsT, (double)@case.UsD, (double)@case.UsU, (double)@case.UsMs,
                    (double)@case.HT, (double)@case.HD,
                    (double)@case.RtT, (double)@case.RtU, (double)@case.RtMs,
                    null, null, null, null, null,
                    true
                };

                method.Invoke(null, args);

                finals.Add(new CqHexRecord(
                    "FINAL",
                    @case.FlowType,
                    new[]
                    {
                        ToHex((float)(double)args[23]!),
                        ToHex((float)(double)args[24]!),
                        ToHex((float)(double)args[25]!),
                        ToHex((float)(double)args[26]!),
                        ToHex((float)(double)args[27]!)
                    }));
            }

            IReadOnlyList<ParityTraceRecord> records = ParityTraceLoader.ReadAll(tracePath);

            IReadOnlyList<CqHexRecord> terms = records
                .Where(record => record.Kind == "cq_terms")
                .Select(record => new CqHexRecord(
                    "TERMS",
                    record.Data.GetProperty("ityp").GetInt32(),
                    TermsFields.Select(field => ReadTraceBits(record, field)).ToArray()))
                .ToArray();

            IReadOnlyList<CqHexRecord> derivativeTerms = records
                .Where(record => record.Kind == "cq_derivative_terms")
                .Select(record => new CqHexRecord(
                    "DTERM",
                    record.Data.GetProperty("ityp").GetInt32(),
                    DerivativeFields.Select(field => ReadTraceBits(record, field)).ToArray()))
                .ToArray();

            return new ManagedCqResult(terms, derivativeTerms, finals);
        }
        finally
        {
            if (File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    private static string ReadTraceBits(ParityTraceRecord record, string fieldName)
    {
        if (record.TryGetDataBits(fieldName, out IReadOnlyDictionary<string, string>? bits) && bits is not null)
        {
            if (bits.TryGetValue("f32", out string? single))
            {
                return single[2..];
            }

            if (bits.TryGetValue("f64", out string? dbl))
            {
                ulong doubleValue = ulong.Parse(dbl[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                double asDouble = BitConverter.Int64BitsToDouble(unchecked((long)doubleValue));
                return ToHex((float)asDouble);
            }
        }

        return ToHex((float)record.Data.GetProperty(fieldName).GetDouble());
    }

    private static void AssertRecordsEqual(
        IReadOnlyList<CqHexRecord> expected,
        IReadOnlyList<CqHexRecord> actual,
        string kind,
        IReadOnlyList<string> fieldNames)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int recordIndex = 0; recordIndex < expected.Count; recordIndex++)
        {
            Assert.Equal(expected[recordIndex].FlowType, actual[recordIndex].FlowType);
            for (int fieldIndex = 0; fieldIndex < expected[recordIndex].Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected[recordIndex].Values[fieldIndex], actual[recordIndex].Values[fieldIndex], StringComparison.Ordinal),
                    $"kind={kind} record={recordIndex} ityp={expected[recordIndex].FlowType} field={fieldNames[fieldIndex]} Fortran=0x{expected[recordIndex].Values[fieldIndex]} Managed=0x{actual[recordIndex].Values[fieldIndex]}");
            }
        }
    }

    private static string ToHex(float value)
        => $"{BitConverter.SingleToInt32Bits(value):X8}";

    private sealed record ManagedCqResult(
        IReadOnlyList<CqHexRecord> Terms,
        IReadOnlyList<CqHexRecord> DerivativeTerms,
        IReadOnlyList<CqHexRecord> Finals);
}
