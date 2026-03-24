using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using XFoil.Core.Services;
using XFoil.Solver.Numerics;
using Xunit;

// Legacy audit:
// Primary legacy source: f_xfoil/src/spline.f :: SPLIND/SEVAL/DEVAL
// Secondary legacy source: tools/fortran-debug/spline_parity_driver.f standalone batch harness
// Role in port: Exercises the managed ParametricSpline parity path directly against the legacy spline.f routines without running the full XFoil solver.
// Differences: The test is managed-only infrastructure, but it compares thousands of standalone spline fit/eval points bitwise against a dedicated Fortran driver instead of relying on broad solver traces.
// Decision: Keep the micro-driver parity fixture because it isolates spline arithmetic and shortens the patch-feedback loop materially.
namespace XFoil.Core.Tests.FortranParity;

[Trait("Category", "FortranReference")]
[Trait("Category", "ParityBlock")]
public sealed class ParametricSplineFortranParityTests
{
    [Fact]
    public void FloatSplineBatch_BitwiseMatchesFortranSplineDriver()
    {
        IReadOnlyList<FortranSplineCase> cases = BuildCases();
        string tracePath = Path.Combine(
            Path.GetTempPath(),
            $"xfoilsharp-spline-parity-trace-{Guid.NewGuid():N}.log");
        bool completed = false;

        try
        {
            IReadOnlyList<FortranSplineCaseResult> fortranResults =
                FortranSplineDriver.RunBatch(cases, tracePath);

            Assert.Equal(cases.Count, fortranResults.Count);

            for (int caseIndex = 0; caseIndex < cases.Count; caseIndex++)
            {
                FortranSplineCase @case = cases[caseIndex];
                FortranSplineCaseResult fortran = fortranResults[caseIndex];

                var managedDerivatives = new float[@case.Parameters.Length];
                ParametricSpline.FitWithBoundaryConditions(
                    @case.Values,
                    managedDerivatives,
                    @case.Parameters,
                    @case.Parameters.Length,
                    ToManagedBoundary(@case.StartBoundaryValue),
                    ToManagedBoundary(@case.EndBoundaryValue));

                for (int i = 0; i < managedDerivatives.Length; i++)
                {
                    AssertBitwiseEqual(
                        fortran.Derivatives[i],
                        managedDerivatives[i],
                        $"derivative case={caseIndex} node={i} bc=({DescribeBoundary(@case.StartBoundaryValue)},{DescribeBoundary(@case.EndBoundaryValue)})");
                }

                for (int evaluationIndex = 0; evaluationIndex < @case.EvaluationParameters.Length; evaluationIndex++)
                {
                    float parameter = @case.EvaluationParameters[evaluationIndex];
                    float managedValue = ParametricSpline.Evaluate(
                        parameter,
                        @case.Values,
                        managedDerivatives,
                        @case.Parameters,
                        @case.Parameters.Length);
                    float managedDerivative = ParametricSpline.EvaluateDerivative(
                        parameter,
                        @case.Values,
                        managedDerivatives,
                        @case.Parameters,
                        @case.Parameters.Length);

                    AssertBitwiseEqual(
                        fortran.Evaluations[evaluationIndex],
                        managedValue,
                        $"SEVAL case={caseIndex} eval={evaluationIndex} s={FormatFloat(parameter)} bc=({DescribeBoundary(@case.StartBoundaryValue)},{DescribeBoundary(@case.EndBoundaryValue)})");
                    AssertBitwiseEqual(
                        fortran.EvaluationDerivatives[evaluationIndex],
                        managedDerivative,
                        $"DEVAL case={caseIndex} eval={evaluationIndex} s={FormatFloat(parameter)} bc=({DescribeBoundary(@case.StartBoundaryValue)},{DescribeBoundary(@case.EndBoundaryValue)})");
                }
            }

            completed = true;
        }
        catch (Exception ex) when (ex is Xunit.Sdk.XunitException or InvalidOperationException)
        {
            throw new Xunit.Sdk.XunitException(
                $"{ex.Message}{Environment.NewLine}Fortran spline trace: {tracePath}");
        }
        finally
        {
            if (completed && File.Exists(tracePath))
            {
                File.Delete(tracePath);
            }
        }
    }

    [Fact]
    public void SegmentedClassicNacaContours_BitwiseMatchFortranSeGsplDriver()
    {
        IReadOnlyList<FortranSplineCase> cases = BuildClassicNacaSegmentedCases();
        Assert.All(cases, @case => Assert.True(@case.EvaluationParameters.Length >= 1500));
        IReadOnlyList<FortranSplineCaseResult> fortranResults = FortranSegmentedSplineDriver.RunBatch(cases);

        Assert.Equal(cases.Count, fortranResults.Count);

        for (int caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            FortranSplineCase @case = cases[caseIndex];
            FortranSplineCaseResult fortran = fortranResults[caseIndex];

            var managedDerivatives = new float[@case.Parameters.Length];
            ParametricSpline.FitSegmented(
                @case.Values,
                managedDerivatives,
                @case.Parameters,
                @case.Parameters.Length);

            for (int i = 0; i < managedDerivatives.Length; i++)
            {
                AssertBitwiseEqual(
                    fortran.Derivatives[i],
                    managedDerivatives[i],
                    $"SEGSPL derivative case={caseIndex} node={i}");
            }

            for (int evaluationIndex = 0; evaluationIndex < @case.EvaluationParameters.Length; evaluationIndex++)
            {
                float parameter = @case.EvaluationParameters[evaluationIndex];
                float managedValue = ParametricSpline.Evaluate(
                    parameter,
                    @case.Values,
                    managedDerivatives,
                    @case.Parameters,
                    @case.Parameters.Length);
                float managedDerivative = ParametricSpline.EvaluateDerivative(
                    parameter,
                    @case.Values,
                    managedDerivatives,
                    @case.Parameters,
                    @case.Parameters.Length);

                AssertBitwiseEqual(
                    fortran.Evaluations[evaluationIndex],
                    managedValue,
                    $"SEGSPL case={caseIndex} eval={evaluationIndex} s={FormatFloat(parameter)}");
                AssertBitwiseEqual(
                    fortran.EvaluationDerivatives[evaluationIndex],
                    managedDerivative,
                    $"SEGSPL derivative case={caseIndex} eval={evaluationIndex} s={FormatFloat(parameter)}");
            }
        }
    }

    private static IReadOnlyList<FortranSplineCase> BuildCases()
    {
        const int randomCaseCount = 256;
        var cases = new List<FortranSplineCase>(randomCaseCount + 6)
        {
            BuildExplicitTwoPointCase(999.0f, 999.0f),
            BuildExplicitTwoPointCase(-999.0f, -999.0f),
            BuildExplicitTwoPointCase(0.25f, -0.5f),
            BuildExplicitEdgeCase(999.0f, -999.0f),
            BuildExplicitEdgeCase(-999.0f, 999.0f),
            BuildExplicitEdgeCase(0.75f, -1.25f)
        };

        var random = new Random(20260317);
        for (int caseIndex = 0; caseIndex < randomCaseCount; caseIndex++)
        {
            int nodeCount = 4 + random.Next(0, 9);
            float[] parameters = BuildParameters(random, nodeCount);
            float[] values = BuildValues(random, caseIndex, parameters);
            (float startBoundary, float endBoundary) = BuildBoundaryPair(random, caseIndex);
            float[] evaluations = BuildEvaluationPoints(random, parameters);

            cases.Add(new FortranSplineCase(parameters, values, startBoundary, endBoundary, evaluations));
        }

        return cases;
    }

    private static IReadOnlyList<FortranSplineCase> BuildClassicNacaSegmentedCases()
    {
        var generator = new NacaAirfoilGenerator();
        var cases = new List<FortranSplineCase>();

        foreach (string designation in new[] { "0006", "0012", "2412", "4412", "4415", "6409" })
        {
            foreach (int pointCount in new[] { 41, 61, 81 })
            {
                AirfoilCurveData curve = BuildClassicNacaCurve(generator, designation, pointCount);
                cases.Add(new FortranSplineCase(
                    curve.Parameters,
                    curve.XValues,
                    StartBoundaryValue: -999.0f,
                    EndBoundaryValue: -999.0f,
                    BuildCurveEvaluationPoints(curve.Parameters)));
                cases.Add(new FortranSplineCase(
                    curve.Parameters,
                    curve.YValues,
                    StartBoundaryValue: -999.0f,
                    EndBoundaryValue: -999.0f,
                    BuildCurveEvaluationPoints(curve.Parameters)));
            }
        }

        return cases;
    }

    private static FortranSplineCase BuildExplicitTwoPointCase(float startBoundary, float endBoundary)
    {
        float[] parameters = { 0.0f, 1.25f };
        float[] values = { -0.75f, 0.5f };
        float[] evaluations = { 0.0f, 0.000125f, 0.625f, 1.249875f, 1.25f };
        return new FortranSplineCase(parameters, values, startBoundary, endBoundary, evaluations);
    }

    private static FortranSplineCase BuildExplicitEdgeCase(float startBoundary, float endBoundary)
    {
        float[] parameters = { 0.0f, 0.03125f, 0.5f, 1.75f, 1.7509766f, 3.0f };
        float[] values = { 0.0f, 0.015625f, -0.125f, 0.875f, 0.87597656f, -0.25f };
        float[] evaluations =
        {
            0.0f,
            0.03125f,
            0.031250313f,
            0.265625f,
            1.75f,
            1.7505f,
            2.375f,
            3.0f
        };

        return new FortranSplineCase(parameters, values, startBoundary, endBoundary, evaluations);
    }

    private static float[] BuildParameters(Random random, int nodeCount)
    {
        var parameters = new float[nodeCount];
        float current = 0.0f;
        for (int i = 0; i < nodeCount; i++)
        {
            current = i == 0
                ? 0.0f
                : current + (0.03125f + (NextSingle(random) * 1.5f));
            parameters[i] = current;
        }

        return parameters;
    }

    private static float[] BuildValues(Random random, int caseIndex, IReadOnlyList<float> parameters)
    {
        var values = new float[parameters.Count];
        float a = (NextSingle(random) * 1.8f) - 0.9f;
        float b = (NextSingle(random) * 1.4f) - 0.7f;
        float c = (NextSingle(random) * 0.5f) - 0.25f;
        float d = (NextSingle(random) * 0.15f) - 0.075f;
        float e = (NextSingle(random) * 0.35f) - 0.175f;
        float freq1 = 0.35f + (0.2f * (caseIndex % 5));
        float freq2 = 0.55f + (0.15f * (caseIndex % 7));

        for (int i = 0; i < parameters.Count; i++)
        {
            float s = parameters[i];
            values[i] = caseIndex % 4 switch
            {
                0 => ((a * s) + b) + (c * s * s) + (d * s * s * s),
                1 => (float)(a + (b * Math.Sin(freq1 * s)) + (c * Math.Cos(freq2 * s))),
                2 => (float)((a * s) + (b * Math.Sin(freq1 * s)) + (c * Math.Cos(freq2 * s)) + d),
                _ => (float)(a + (b * Math.Sin(freq1 * s)) + (c * s * s) + (d * Math.Cos(freq2 * s)) + (e * s))
            };
        }

        return values;
    }

    private static (float startBoundary, float endBoundary) BuildBoundaryPair(Random random, int caseIndex)
    {
        float startBoundary = caseIndex % 3 switch
        {
            0 => 999.0f,
            1 => -999.0f,
            _ => (NextSingle(random) * 3.0f) - 1.5f
        };

        float endBoundary = (caseIndex / 3) % 3 switch
        {
            0 => 999.0f,
            1 => -999.0f,
            _ => (NextSingle(random) * 3.0f) - 1.5f
        };

        return (startBoundary, endBoundary);
    }

    private static float[] BuildEvaluationPoints(Random random, IReadOnlyList<float> parameters)
    {
        var evaluations = new List<float>(16)
        {
            parameters[0],
            parameters[^1]
        };

        for (int interval = 0; interval < parameters.Count - 1 && evaluations.Count < 16; interval++)
        {
            float low = parameters[interval];
            float high = parameters[interval + 1];
            float step = high - low;
            float epsilon = step * 1.0e-4f;

            evaluations.Add(low + (0.5f * step));
            if (epsilon > 0.0f)
            {
                evaluations.Add(low + epsilon);
            }

            if (evaluations.Count >= 16)
            {
                break;
            }

            if (epsilon > 0.0f)
            {
                evaluations.Add(high - epsilon);
            }
        }

        while (evaluations.Count < 16)
        {
            int interval = random.Next(0, parameters.Count - 1);
            float low = parameters[interval];
            float high = parameters[interval + 1];
            float t = NextSingle(random);
            evaluations.Add(low + ((high - low) * t));
        }

        return evaluations.ToArray();
    }

    private static float[] BuildCurveEvaluationPoints(IReadOnlyList<float> parameters)
    {
        float start = parameters[0];
        float end = parameters[^1];
        float span = end - start;

        ReadOnlySpan<float> intervalFractions =
        [
            0.0f,
            1.0f / 64.0f,
            1.0f / 32.0f,
            1.0f / 16.0f,
            1.0f / 8.0f,
            3.0f / 16.0f,
            1.0f / 4.0f,
            3.0f / 8.0f,
            1.0f / 2.0f,
            5.0f / 8.0f,
            3.0f / 4.0f,
            13.0f / 16.0f,
            7.0f / 8.0f,
            15.0f / 16.0f,
            31.0f / 32.0f,
            63.0f / 64.0f,
            1.0f
        ];

        var evaluations = new List<float>((parameters.Count * (intervalFractions.Length + 2)) + 1025);

        for (int index = 0; index <= 1024; index++)
        {
            float t = index / 1024.0f;
            evaluations.Add(start + (span * t));
        }

        for (int interval = 0; interval < parameters.Count - 1; interval++)
        {
            float low = parameters[interval];
            float high = parameters[interval + 1];
            float step = high - low;
            float epsilon = step * 1.0e-4f;

            foreach (float fraction in intervalFractions)
            {
                evaluations.Add(low + (fraction * step));
            }

            if (epsilon > 0.0f)
            {
                evaluations.Add(low + epsilon);
                evaluations.Add(high - epsilon);
            }
        }

        return evaluations
            .Where(value => value >= start && value <= end)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
    }

    private static AirfoilCurveData BuildClassicNacaCurve(
        NacaAirfoilGenerator generator,
        string designation,
        int pointCount)
    {
        var geometry = generator.Generate4DigitClassic(designation, pointCount, useLegacyPrecision: true);
        float[] xValues = geometry.Points.Select(point => (float)point.X).ToArray();
        float[] yValues = geometry.Points.Select(point => (float)point.Y).ToArray();
        var parameters = new float[geometry.Points.Count];
        ParametricSpline.ComputeArcLength(xValues, yValues, parameters, parameters.Length);
        return new AirfoilCurveData(parameters, xValues, yValues);
    }

    private static SplineBoundaryCondition ToManagedBoundary(float boundaryValue)
    {
        if (boundaryValue == 999.0f)
        {
            return SplineBoundaryCondition.ZeroSecondDerivative;
        }

        if (boundaryValue == -999.0f)
        {
            return SplineBoundaryCondition.ZeroThirdDerivative;
        }

        return SplineBoundaryCondition.SpecifiedDerivative(boundaryValue);
    }

    private static string DescribeBoundary(float boundaryValue)
    {
        if (boundaryValue == 999.0f)
        {
            return "ZeroSecondDerivative";
        }

        if (boundaryValue == -999.0f)
        {
            return "ZeroThirdDerivative";
        }

        return $"Specified({FormatFloat(boundaryValue)})";
    }

    private static void AssertBitwiseEqual(float expected, float actual, string context)
    {
        int expectedBits = BitConverter.SingleToInt32Bits(expected);
        int actualBits = BitConverter.SingleToInt32Bits(actual);
        Assert.True(
            expectedBits == actualBits,
            $"{context}: Fortran={FormatFloat(expected)} [{FormatBits(expectedBits)}] Managed={FormatFloat(actual)} [{FormatBits(actualBits)}]");
    }

    private static string FormatFloat(float value)
        => value.ToString("G9", CultureInfo.InvariantCulture);

    private static string FormatBits(int bits)
        => $"0x{bits:X8}";

    private static float NextSingle(Random random)
        => (float)random.NextDouble();

    private sealed record AirfoilCurveData(
        float[] Parameters,
        float[] XValues,
        float[] YValues);
}
