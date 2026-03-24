using XFoil.Core.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/naca.f
// Secondary legacy source: f_xfoil/src/xfoil.f geometry-generation entry points
// Role in port: Verifies the managed NACA generator that reproduces the legacy analytical 4-digit airfoil construction.
// Differences: The managed test calls a direct generator service instead of the legacy geometry-generation command path.
// Decision: Keep the managed test because it validates the preserved analytical geometry contract through the port's public API.
namespace XFoil.Core.Tests;

public sealed class NacaAirfoilGeneratorTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/naca.f 4-digit section generation.
    // Difference from legacy: The test validates the managed return object directly instead of inspecting the active geometry buffer in the legacy program.
    // Decision: Keep the managed regression because it protects the same generated-name and point-count behavior.
    public void Generate4Digit_CreatesExpectedPointCountAndName()
    {
        var generator = new NacaAirfoilGenerator();
        var geometry = generator.Generate4Digit("2412", 161);

        Assert.Equal("NACA 2412", geometry.Name);
        Assert.Equal(161, geometry.Points.Count);
    }
}
