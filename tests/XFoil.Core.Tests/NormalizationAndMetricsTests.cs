using XFoil.Core.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f :: NORM
// Secondary legacy source: f_xfoil/src/geom.f geometry metrics lineage
// Role in port: Verifies the managed normalization and metrics services derived from legacy geometry normalization workflows.
// Differences: The managed port splits normalization and metric extraction into dedicated services instead of using the legacy editor state.
// Decision: Keep the managed service split because it preserves the same geometric outcome with a cleaner API.
namespace XFoil.Core.Tests;

public sealed class NormalizationAndMetricsTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xgdes.f :: NORM with geometry metrics follow-up.
    // Difference from legacy: The test asserts normalized metrics through dedicated managed services instead of reading back mutable legacy geometry state.
    // Decision: Keep the managed composition because it is the intended refactor of the same normalization behavior.
    public void Normalize_And_CalculateMetrics_ProducesUnitChordForNaca0012()
    {
        var generator = new NacaAirfoilGenerator();
        var normalizer = new AirfoilNormalizer();
        var metricsCalculator = new AirfoilMetricsCalculator();

        var geometry = generator.Generate4Digit("0012", 161);
        var normalized = normalizer.Normalize(geometry);
        var metrics = metricsCalculator.Calculate(normalized);

        Assert.InRange(metrics.Chord, 0.999999, 1.000001);
        Assert.InRange(metrics.MaxThickness, 0.11, 0.13);
    }
}
