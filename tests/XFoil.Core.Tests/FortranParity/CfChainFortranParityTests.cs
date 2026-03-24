using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using XFoil.Core.Numerics;
using XFoil.Solver.Diagnostics;
using XFoil.Solver.Numerics;
using XFoil.Solver.Services;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xblsys.f :: BLVAR CF2 chain plus CFL/CFT
// Secondary legacy source: tools/fortran-debug/cf_parity_driver.f90
// Role in port: Verifies the managed BLVAR skin-friction branch selection and chained derivatives against a standalone Fortran micro-driver instead of rediscovering Cf drift through BLDIF rows.
// Differences: The harness is managed-only infrastructure, but it compares raw IEEE-754 words for the selected branch, raw correlation partials, and chained derivative outputs.
// Decision: Keep the micro-driver because CF feeds the current viscous producer frontier directly and now has an isolated parity oracle.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class CfChainFortranParityTests
{
    private static readonly string[] TermsFields =
    {
        "cf", "cfHk", "cfRt", "cfM"
    };

    private static readonly string[] DetailFields =
    {
        "fcArg", "fc", "grt", "gex", "arg",
        "thkArg", "thk", "grtRatio",
        "thkSq", "oneMinusThkSq", "scaledThkDiff",
        "cfo", "cfHkTerm1", "cfHkTerm2", "cfHkTerm3",
        "cfNumerator", "cfMsqScale", "cfMsqLeadCore", "cfMsqTail"
    };

    private static readonly string[] FinalFields =
    {
        "cf", "cfT", "cfD", "cfU", "cfMs", "cfRe"
    };
    private static readonly FortranCfCase TurbulentCftDetailRegressionCase = new(
        FlowType: 2,
        Hk: 4.0568247f,
        Rt: 301.10233f,
        Msq: 0.6645372f,
        HkT: 0.0f,
        HkD: 0.0f,
        HkU: 0.0f,
        HkMs: 0.0f,
        RtT: 0.0f,
        RtU: 0.0f,
        RtMs: 0.0f,
        MU: 0.0f,
        MMs: 0.0f,
        RtRe: 0.0f);
    private static readonly FortranCfCase TurbulentAttachedRegressionCase = new(
        FlowType: 2,
        Hk: 1.3886719f,
        Rt: 4200.0f,
        Msq: 0.1875f,
        HkT: 0.0f,
        HkD: 0.0f,
        HkU: 0.0f,
        HkMs: 0.0f,
        RtT: 0.0f,
        RtU: 0.0f,
        RtMs: 0.0f,
        MU: 0.0f,
        MMs: 0.0f,
        RtRe: 0.0f);
    private static readonly FortranCfCase TurbulentAttachedMidRegressionCase = new(
        FlowType: 2,
        Hk: 1.7954354f,
        Rt: 3601.9326f,
        Msq: 0.83607733f,
        HkT: 0.0f,
        HkD: 0.0f,
        HkU: 0.0f,
        HkMs: 0.0f,
        RtT: 0.0f,
        RtU: 0.0f,
        RtMs: 0.0f,
        MU: 0.0f,
        MMs: 0.0f,
        RtRe: 0.0f);
    private static readonly FortranCfCase LaminarSeparatedRegressionCase = new(
        FlowType: 1,
        Hk: 6.125f,
        Rt: 217.0f,
        Msq: 0.0f,
        HkT: -0.40625f,
        HkD: 0.53125f,
        HkU: -0.21875f,
        HkMs: 0.078125f,
        RtT: -36.0f,
        RtU: 19.0f,
        RtMs: -11.0f,
        MU: 0.0f,
        MMs: 0.0f,
        RtRe: 0.0048828125f);
    private static readonly FortranCfCase TurbulentWallCfMRegressionCase = new(
        FlowType: 2,
        Hk: 3.1226902f,
        Rt: 3900.4304f,
        Msq: 0.5048134f,
        HkT: 0.0f,
        HkD: 0.0f,
        HkU: 0.0f,
        HkMs: 0.0f,
        RtT: 0.0f,
        RtU: 0.0f,
        RtMs: 0.0f,
        MU: 0.0f,
        MMs: 0.0f,
        RtRe: 0.0f);
    private static readonly FortranCfCase TurbulentStation15Iteration4RegressionCase = new(
        FlowType: 2,
        Hk: 7.0274982f,
        Rt: 212.8577f,
        Msq: 0.0f,
        HkT: 0.0f,
        HkD: 0.0f,
        HkU: 0.0f,
        HkMs: 0.0f,
        RtT: 0.0f,
        RtU: 0.0f,
        RtMs: 0.0f,
        MU: 0.0f,
        MMs: 0.0f,
        RtRe: 0.0f);

    [Fact]
    public void CfChainBatch_BitwiseMatchesFortranDriver()
    {
        IReadOnlyList<FortranCfCase> cases = BuildCases();
        IReadOnlyList<FortranCfCase> turbulentCases = cases.Where(@case => @case.FlowType == 2).ToArray();
        FortranCfResult fortran = FortranCfDriver.RunBatch(cases);
        ManagedCfResult managed = RunManagedBatch(cases);

        Assert.Equal(fortran.Details.Count, managed.Details.Count);
        for (int recordIndex = 0; recordIndex < fortran.Details.Count; recordIndex++)
        {
            CfHexDetail expected = fortran.Details[recordIndex];
            CfHexDetail actual = managed.Details[recordIndex];

            Assert.Equal(expected.FlowType, actual.FlowType);
            Assert.Equal(expected.Values.Count, actual.Values.Count);

            for (int fieldIndex = 0; fieldIndex < expected.Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected.Values[fieldIndex], actual.Values[fieldIndex], StringComparison.Ordinal),
                    $"DTERM record={recordIndex} fortranItyp={expected.FlowType} hk={turbulentCases[recordIndex].Hk.ToString("R", CultureInfo.InvariantCulture)} rt={turbulentCases[recordIndex].Rt.ToString("R", CultureInfo.InvariantCulture)} msq={turbulentCases[recordIndex].Msq.ToString("R", CultureInfo.InvariantCulture)} field={DetailFields[fieldIndex]} expected={expected.Values[fieldIndex]} actual={actual.Values[fieldIndex]}");
            }
        }

        Assert.Equal(fortran.Terms.Count, managed.Terms.Count);
        for (int caseIndex = 0; caseIndex < fortran.Terms.Count; caseIndex++)
        {
            CfHexTerms expected = fortran.Terms[caseIndex];
            CfHexTerms actual = managed.Terms[caseIndex];

            Assert.Equal(expected.FlowType, actual.FlowType);
            Assert.Equal(expected.SelectedBranch, actual.SelectedBranch);
            Assert.Equal(expected.Values.Count, actual.Values.Count);

            for (int fieldIndex = 0; fieldIndex < expected.Values.Count; fieldIndex++)
            {
                Assert.True(
                    string.Equals(expected.Values[fieldIndex], actual.Values[fieldIndex], StringComparison.Ordinal),
                    $"TERMS case={caseIndex} inputItyp={cases[caseIndex].FlowType} fortranItyp={expected.FlowType} branch={expected.SelectedBranch} hk={cases[caseIndex].Hk.ToString("R", CultureInfo.InvariantCulture)} rt={cases[caseIndex].Rt.ToString("R", CultureInfo.InvariantCulture)} msq={cases[caseIndex].Msq.ToString("R", CultureInfo.InvariantCulture)} field={TermsFields[fieldIndex]} expected={expected.Values[fieldIndex]} actual={actual.Values[fieldIndex]}");
            }
        }

        Assert.Equal(fortran.Finals.Count, managed.Finals.Count);
        for (int caseIndex = 0; caseIndex < fortran.Finals.Count; caseIndex++)
        {
            CfHexFinals expected = fortran.Finals[caseIndex];
            CfHexFinals actual = managed.Finals[caseIndex];

            Assert.Equal(expected.FlowType, actual.FlowType);
            Assert.Equal(expected.Values.Count, actual.Values.Count);

            for (int fieldIndex = 0; fieldIndex < expected.Values.Count; fieldIndex++)
            {
                string candidateSuffix = FinalFields[fieldIndex] == "cfT"
                    ? $" {DescribeCfTCandidates(managed.Terms[caseIndex], cases[caseIndex])}"
                    : string.Empty;
                Assert.True(
                    string.Equals(expected.Values[fieldIndex], actual.Values[fieldIndex], StringComparison.Ordinal),
                    $"FINAL case={caseIndex} inputItyp={cases[caseIndex].FlowType} fortranItyp={expected.FlowType} hk={cases[caseIndex].Hk.ToString("R", CultureInfo.InvariantCulture)} rt={cases[caseIndex].Rt.ToString("R", CultureInfo.InvariantCulture)} msq={cases[caseIndex].Msq.ToString("R", CultureInfo.InvariantCulture)} field={FinalFields[fieldIndex]} expected={expected.Values[fieldIndex]} actual={actual.Values[fieldIndex]}{candidateSuffix}");
            }
        }
    }

    [Fact]
    public void CfChain_TurbulentRegressionCase_DetailTraceMatchesFortranDriver()
        => AssertTurbulentRegressionCase(TurbulentCftDetailRegressionCase);

    [Fact]
    public void CfChain_TurbulentAttachedRegressionCase_DetailTraceMatchesFortranDriver()
        => AssertTurbulentRegressionCase(TurbulentAttachedRegressionCase);

    [Fact]
    public void CfChain_TurbulentAttachedMidRegressionCase_DetailTraceMatchesFortranDriver()
        => AssertTurbulentRegressionCase(TurbulentAttachedMidRegressionCase);

    [Fact]
    public void CfChain_LaminarSeparatedRegressionCase_FinalTraceMatchesFortranDriver()
    {
        IReadOnlyList<FortranCfCase> cases = new[] { LaminarSeparatedRegressionCase };
        FortranCfResult fortran = FortranCfDriver.RunBatch(cases);
        ManagedCfResult managed = RunManagedBatch(cases);

        CfHexTerms expectedTerms = fortran.Terms[0];
        CfHexTerms actualTerms = managed.Terms[0];
        Assert.Equal(expectedTerms.FlowType, actualTerms.FlowType);
        Assert.Equal(expectedTerms.SelectedBranch, actualTerms.SelectedBranch);
        for (int fieldIndex = 0; fieldIndex < expectedTerms.Values.Count; fieldIndex++)
        {
            Assert.True(
                string.Equals(expectedTerms.Values[fieldIndex], actualTerms.Values[fieldIndex], StringComparison.Ordinal),
                $"TERMS field={TermsFields[fieldIndex]} expected={expectedTerms.Values[fieldIndex]} actual={actualTerms.Values[fieldIndex]}");
        }

        CfHexFinals expectedFinal = fortran.Finals[0];
        CfHexFinals actualFinal = managed.Finals[0];
        Assert.Equal(expectedFinal.FlowType, actualFinal.FlowType);
        for (int fieldIndex = 0; fieldIndex < expectedFinal.Values.Count; fieldIndex++)
        {
            string candidateSuffix = FinalFields[fieldIndex] == "cfT"
                ? $" {DescribeCfTCandidates(actualTerms, LaminarSeparatedRegressionCase)}"
                : string.Empty;
            Assert.True(
                string.Equals(expectedFinal.Values[fieldIndex], actualFinal.Values[fieldIndex], StringComparison.Ordinal),
                $"FINAL field={FinalFields[fieldIndex]} expected={expectedFinal.Values[fieldIndex]} actual={actualFinal.Values[fieldIndex]}{candidateSuffix}");
        }
    }

    [Fact]
    public void CfChain_TurbulentWallCfMRegressionCase_DetailTraceMatchesFortranDriver()
        => AssertTurbulentRegressionCase(TurbulentWallCfMRegressionCase);

    [Fact]
    public void CfChain_TurbulentStation15Iteration4RegressionCase_DetailTraceMatchesFortranDriver()
        => AssertTurbulentRegressionCase(TurbulentStation15Iteration4RegressionCase);

    private static void AssertTurbulentRegressionCase(FortranCfCase @case)
    {
        IReadOnlyList<FortranCfCase> cases = new[] { @case };
        FortranCfResult fortran = FortranCfDriver.RunBatch(cases);
        ManagedCfResult managed = RunManagedBatch(cases);

        CfHexDetail expectedDetail = fortran.Details[0];
        CfHexDetail actualDetail = managed.Details[0];
        ParityTraceRecord actualDetailRecord = managed.DetailRecords[0];
        Assert.Equal(expectedDetail.FlowType, actualDetail.FlowType);
        for (int fieldIndex = 0; fieldIndex < expectedDetail.Values.Count; fieldIndex++)
        {
            Assert.True(
                string.Equals(expectedDetail.Values[fieldIndex], actualDetail.Values[fieldIndex], StringComparison.Ordinal),
                $"DTERM field={DetailFields[fieldIndex]} expected={expectedDetail.Values[fieldIndex]} actual={actualDetail.Values[fieldIndex]}");
        }

        CfHexTerms expectedTerms = fortran.Terms[0];
        CfHexTerms actualTerms = managed.Terms[0];
        Assert.Equal(expectedTerms.FlowType, actualTerms.FlowType);
        Assert.Equal(expectedTerms.SelectedBranch, actualTerms.SelectedBranch);
        for (int fieldIndex = 0; fieldIndex < expectedTerms.Values.Count; fieldIndex++)
        {
            string candidateSuffix = TermsFields[fieldIndex] switch
            {
                "cfHk" => $" {DescribeCfHkCandidates(actualDetailRecord)}",
                "cfM" => $" {DescribeCfMCandidates(actualDetailRecord)}",
                _ => string.Empty
            };
            Assert.True(
                string.Equals(expectedTerms.Values[fieldIndex], actualTerms.Values[fieldIndex], StringComparison.Ordinal),
                $"TERMS field={TermsFields[fieldIndex]} expected={expectedTerms.Values[fieldIndex]} actual={actualTerms.Values[fieldIndex]}{candidateSuffix}");
        }
    }

    private static IReadOnlyList<FortranCfCase> BuildCases()
    {
        var cases = new List<FortranCfCase>
        {
            new(
                FlowType: 3,
                Hk: 1.4375f,
                Rt: 520.0f,
                Msq: 0.0625f,
                HkT: 0.5f,
                HkD: -0.75f,
                HkU: 0.375f,
                HkMs: -0.125f,
                RtT: 48.0f,
                RtU: -24.0f,
                RtMs: 17.0f,
                MU: 0.28125f,
                MMs: -0.09375f,
                RtRe: 0.001953125f),
            new(
                FlowType: 1,
                Hk: 1.7324219f,
                Rt: 642.5f,
                Msq: 0.09375f,
                HkT: 0.8125f,
                HkD: -1.4375f,
                HkU: 0.59375f,
                HkMs: 0.21875f,
                RtT: 91.0f,
                RtU: -44.0f,
                RtMs: 38.0f,
                MU: 0.171875f,
                MMs: -0.0546875f,
                RtRe: 0.0012207031f),
            new(
                FlowType: 1,
                Hk: 6.125f,
                Rt: 217.0f,
                Msq: 0.0f,
                HkT: -0.40625f,
                HkD: 0.53125f,
                HkU: -0.21875f,
                HkMs: 0.078125f,
                RtT: -36.0f,
                RtU: 19.0f,
                RtMs: -11.0f,
                MU: 0.0f,
                MMs: 0.0f,
                RtRe: 0.0048828125f),
            new(
                FlowType: 2,
                Hk: 1.3886719f,
                Rt: 4200.0f,
                Msq: 0.1875f,
                HkT: 0.15625f,
                HkD: -0.28125f,
                HkU: 0.09375f,
                HkMs: 0.03125f,
                RtT: 22.0f,
                RtU: -10.0f,
                RtMs: 7.0f,
                MU: 0.0625f,
                MMs: -0.015625f,
                RtRe: 0.00024414062f),
            new(
                FlowType: 2,
                Hk: 1.296875f,
                Rt: 18.25f,
                Msq: 0.21875f,
                HkT: -0.21875f,
                HkD: 0.375f,
                HkU: -0.125f,
                HkMs: 0.046875f,
                RtT: -4.0f,
                RtU: 2.5f,
                RtMs: -1.25f,
                MU: 0.09375f,
                MMs: -0.03125f,
                RtRe: 0.0625f),
            new(
                FlowType: 2,
                Hk: 1.1015625f,
                Rt: 40.0f,
                Msq: 0.125f,
                HkT: 0.28125f,
                HkD: -0.46875f,
                HkU: 0.15625f,
                HkMs: -0.0625f,
                RtT: 5.0f,
                RtU: -3.0f,
                RtMs: 1.5f,
                MU: -0.046875f,
                MMs: 0.0234375f,
                RtRe: 0.03125f)
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

            float hk = 1.02f + ((float)random.NextDouble() * 5.7f);
            float rt = flowType == 2
                ? 18.0f + ((float)random.NextDouble() * 9000.0f)
                : 40.0f + ((float)random.NextDouble() * 9000.0f);
            float msq = (float)random.NextDouble() * 0.85f;

            cases.Add(new FortranCfCase(
                flowType,
                hk,
                rt,
                msq,
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 2.5f),
                RandomSigned(random, 140.0f),
                RandomSigned(random, 140.0f),
                RandomSigned(random, 140.0f),
                RandomSigned(random, 0.75f),
                RandomSigned(random, 0.75f),
                0.0001f + ((float)random.NextDouble() * 0.08f)));
        }

        return cases;
    }

    private static float RandomSigned(Random random, float amplitude)
        => (((float)random.NextDouble() * 2.0f) - 1.0f) * amplitude;

    private static ManagedCfResult RunManagedBatch(IReadOnlyList<FortranCfCase> cases)
    {
        MethodInfo method = typeof(BoundaryLayerSystemAssembler).GetMethod(
            "ComputeCfChains",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeCfChains method not found.");

        string tracePath = Path.Combine(Path.GetTempPath(), $"xfoilsharp-cf-trace-{Guid.NewGuid():N}.jsonl");

        try
        {
            using var traceWriter = new JsonlTraceWriter(tracePath, runtime: "csharp", session: new { caseName = "cf-micro" });
            using var traceScope = SolverTrace.Begin(traceWriter);

            var terms = new List<CfHexTerms>(cases.Count);
            var finals = new List<CfHexFinals>(cases.Count);

            foreach (FortranCfCase @case in cases)
            {
                object?[] args =
                {
                    @case.FlowType,
                    (double)@case.Hk,
                    (double)@case.Rt,
                    (double)@case.Msq,
                    (double)@case.HkT,
                    (double)@case.HkD,
                    (double)@case.HkU,
                    (double)@case.HkMs,
                    (double)@case.RtT,
                    (double)@case.RtU,
                    (double)@case.RtMs,
                    (double)@case.MU,
                    (double)@case.MMs,
                    (double)@case.RtRe,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    true
                };

                method.Invoke(null, args);

                terms.Add(new CfHexTerms(
                    @case.FlowType,
                    (int)args[14]!,
                    new[]
                    {
                        ToHex((float)(double)args[15]!),
                        ToHex((float)(double)args[16]!),
                        ToHex((float)(double)args[17]!),
                        ToHex((float)(double)args[18]!)
                    }));

                finals.Add(new CfHexFinals(
                    @case.FlowType,
                    new[]
                    {
                        ToHex((float)(double)args[15]!),
                        ToHex((float)(double)args[19]!),
                        ToHex((float)(double)args[20]!),
                        ToHex((float)(double)args[21]!),
                        ToHex((float)(double)args[22]!),
                        ToHex((float)(double)args[23]!)
                    }));
            }

            IReadOnlyList<ParityTraceRecord> records = ParityTraceLoader.ReadAll(tracePath);

            IReadOnlyList<ParityTraceRecord> detailRecords = records
                .Where(record => record.Kind == "cft_terms")
                .ToArray();

            IReadOnlyList<CfHexDetail> details = detailRecords
                .Select(record => new CfHexDetail(
                    FlowType: 2,
                    DetailFields.Select(field => ReadTraceBits(record, field)).ToArray()))
                .ToArray();

            return new ManagedCfResult(terms, details, finals, detailRecords);
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

    private static string ToHex(float value)
        => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);

    private static string DescribeCfHkCandidates(ParityTraceRecord detailRecord)
    {
        float fc = ReadTraceFloat(detailRecord, "fc");
        float term1 = ReadTraceFloat(detailRecord, "cfHkTerm1");
        float term2 = ReadTraceFloat(detailRecord, "cfHkTerm2");
        float term3 = ReadTraceFloat(detailRecord, "cfHkTerm3");

        float stagedRoundedTerms = (float)(((term1 + term2) + term3) / fc);
        float wideRoundedTerms = (float)(((double)term1 + term2 + term3) / fc);
        float fmaLead = MathF.FusedMultiplyAdd(1.0f, term1, term2);
        float fmaLeadDiv = (float)(((double)fmaLead + term3) / fc);

        return $"candidates[staged={ToHex(stagedRoundedTerms)} wideTerms={ToHex(wideRoundedTerms)} fmaLead={ToHex(fmaLeadDiv)}]";
    }

    private static string DescribeCfTCandidates(CfHexTerms terms, FortranCfCase @case)
    {
        float cfHk = FromHex(terms.Values[1]);
        float cfRt = FromHex(terms.Values[2]);
        float hkT = @case.HkT;
        float rtT = @case.RtT;

        float staged = (cfHk * hkT) + (cfRt * rtT);
        float wide = (float)(((double)cfHk * hkT) + ((double)cfRt * rtT));
        float fmaHk = MathF.FusedMultiplyAdd(cfHk, hkT, cfRt * rtT);
        float fmaRt = MathF.FusedMultiplyAdd(cfRt, rtT, cfHk * hkT);

        return $"candidates[staged={ToHex(staged)} wide={ToHex(wide)} fmaHk={ToHex(fmaHk)} fmaRt={ToHex(fmaRt)}]";
    }

    private static string DescribeCfMCandidates(ParityTraceRecord detailRecord)
    {
        float scale = ReadTraceFloat(detailRecord, "cfMsqScale");
        float leadCore = ReadTraceFloat(detailRecord, "cfMsqLeadCore");
        float tail = ReadTraceFloat(detailRecord, "cfMsqTail");

        float sourceOrder = (leadCore * (-scale)) - tail;
        float negFma = -MathF.FusedMultiplyAdd(scale, leadCore, tail);
        float fusedNegativeScale = MathF.FusedMultiplyAdd(leadCore, -scale, -tail);
        float wideSource = (float)(((double)leadCore * (-scale)) - tail);

        return $"candidates[source={ToHex(sourceOrder)} negFma={ToHex(negFma)} fusedNegScale={ToHex(fusedNegativeScale)} wideSource={ToHex(wideSource)}]";
    }

    private static float ReadTraceFloat(ParityTraceRecord record, string fieldName)
    {
        if (record.TryGetDataBits(fieldName, out IReadOnlyDictionary<string, string>? bits) &&
            bits is not null &&
            bits.TryGetValue("f32", out string? singleBits))
        {
            int intBits = int.Parse(singleBits[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return BitConverter.Int32BitsToSingle(intBits);
        }

        return (float)record.Data.GetProperty(fieldName).GetDouble();
    }

    private static float FromHex(string value)
    {
        int intBits = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return BitConverter.Int32BitsToSingle(intBits);
    }

    private sealed record ManagedCfResult(
        IReadOnlyList<CfHexTerms> Terms,
        IReadOnlyList<CfHexDetail> Details,
        IReadOnlyList<CfHexFinals> Finals,
        IReadOnlyList<ParityTraceRecord> DetailRecords);
}
