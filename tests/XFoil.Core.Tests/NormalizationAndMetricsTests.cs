using XFoil.Core.Services;

namespace XFoil.Core.Tests;

public sealed class NormalizationAndMetricsTests
{
    [Fact]
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
