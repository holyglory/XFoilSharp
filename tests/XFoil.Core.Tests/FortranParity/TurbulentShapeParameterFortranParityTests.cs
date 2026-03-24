using System;
using System.Collections.Generic;
using System.Linq;
using XFoil.Solver.Services;
using Xunit;

namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class TurbulentShapeParameterFortranParityTests
{
    [Fact]
    public void LegacyPrecisionBatch_BitwiseMatchesStandaloneFortranHstDriver()
    {
        IReadOnlyList<FortranHstCase> cases = BuildCases();
        Assert.True(cases.Count >= 1000);

        IReadOnlyList<FortranHstCaseResult> fortranResults = FortranHstDriver.RunBatch(cases);
        Assert.Equal(cases.Count, fortranResults.Count);

        for (int i = 0; i < cases.Count; i++)
        {
            FortranHstCase @case = cases[i];
            FortranHstCaseResult fortran = fortranResults[i];
            var managed = BoundaryLayerCorrelations.TurbulentShapeParameter(
                @case.Hk,
                @case.Rt,
                @case.Msq,
                useLegacyPrecision: true);

            AssertBitwiseEqual(fortran.Hs, (float)managed.Hs, $"case={i} hs hk={Format(@case.Hk)} rt={Format(@case.Rt)} msq={Format(@case.Msq)}");
            AssertBitwiseEqual(fortran.HsHk, (float)managed.Hs_Hk, $"case={i} hs_hk hk={Format(@case.Hk)} rt={Format(@case.Rt)} msq={Format(@case.Msq)}");
            AssertBitwiseEqual(fortran.HsRt, (float)managed.Hs_Rt, $"case={i} hs_rt hk={Format(@case.Hk)} rt={Format(@case.Rt)} msq={Format(@case.Msq)}");
            AssertBitwiseEqual(fortran.HsMsq, (float)managed.Hs_Msq, $"case={i} hs_msq hk={Format(@case.Hk)} rt={Format(@case.Rt)} msq={Format(@case.Msq)}");
        }
    }

    private static IReadOnlyList<FortranHstCase> BuildCases()
    {
        var cases = new List<FortranHstCase>
        {
            new(BitConverter.Int32BitsToSingle(unchecked((int)0x40CCCF16)), BitConverter.Int32BitsToSingle(unchecked((int)0x4363294F)), 0.0f),
            new(3.9995f, 199.5f, 0.0f),
            new(4.0005f, 199.5f, 0.0f),
            new(3.9995f, 200.5f, 0.0f),
            new(4.0005f, 200.5f, 0.0f),
            new(3.5f, 399.5f, 0.0f),
            new(4.5f, 399.5f, 0.0f),
            new(3.5f, 400.5f, 0.0f),
            new(4.5f, 400.5f, 0.0f),
            new(6.400279f, 227.16136f, 0.05f),
            new(6.400279f, 227.16136f, 0.15f),
            new(1.05f, 50.0f, 0.0f),
            new(1.2f, 1000.0f, 0.25f)
        };

        var random = new Random(20260318);
        while (cases.Count < 1152)
        {
            float rt = Lerp(30.0f, 5000.0f, (float)random.NextDouble());
            float ho = rt > 400.0f ? 3.0f + (400.0f / rt) : 4.0f;
            float hk;
            switch (cases.Count % 6)
            {
                case 0:
                    hk = Lerp(1.05f, MathF.Max(1.15f, ho - 0.02f), (float)random.NextDouble());
                    break;
                case 1:
                    hk = ho + Lerp(-0.005f, 0.005f, (float)random.NextDouble());
                    break;
                case 2:
                    hk = ho + Lerp(0.02f, 0.5f, (float)random.NextDouble());
                    break;
                case 3:
                    hk = Lerp(1.05f, 8.0f, (float)random.NextDouble());
                    break;
                case 4:
                    rt = Lerp(180.0f, 220.0f, (float)random.NextDouble());
                    ho = rt > 400.0f ? 3.0f + (400.0f / rt) : 4.0f;
                    hk = ho + Lerp(-0.05f, 0.8f, (float)random.NextDouble());
                    break;
                default:
                    rt = Lerp(380.0f, 420.0f, (float)random.NextDouble());
                    ho = rt > 400.0f ? 3.0f + (400.0f / rt) : 4.0f;
                    hk = ho + Lerp(-0.05f, 1.0f, (float)random.NextDouble());
                    break;
            }

            hk = MathF.Max(1.05f, hk);
            float msq = Lerp(0.0f, 0.35f, (float)random.NextDouble());
            cases.Add(new FortranHstCase(hk, rt, msq));
        }

        return cases;
    }

    private static void AssertBitwiseEqual(float expected, float actual, string context)
    {
        int expectedBits = BitConverter.SingleToInt32Bits(expected);
        int actualBits = BitConverter.SingleToInt32Bits(actual);
        Assert.True(
            expectedBits == actualBits,
            $"{context} expected=0x{unchecked((uint)expectedBits):X8} actual=0x{unchecked((uint)actualBits):X8}");
    }

    private static float Lerp(float min, float max, float t)
        => min + ((max - min) * t);

    private static string Format(float value)
        => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
