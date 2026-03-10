using XFoil.Core.Services;

namespace XFoil.Core.Tests;

public sealed class NacaAirfoilGeneratorTests
{
    [Fact]
    public void Generate4Digit_CreatesExpectedPointCountAndName()
    {
        var generator = new NacaAirfoilGenerator();
        var geometry = generator.Generate4Digit("2412", 161);

        Assert.Equal("NACA 2412", geometry.Name);
        Assert.Equal(161, geometry.Points.Count);
    }
}
