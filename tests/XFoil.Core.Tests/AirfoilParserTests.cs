using XFoil.Core.Models;
using XFoil.Core.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f :: LOAD
// Secondary legacy source: f_xfoil/src/userio.f
// Role in port: Verifies managed parsing of airfoil coordinate files derived from the legacy geometry-loading workflows.
// Differences: The parser consumes in-memory string collections instead of legacy terminal/file prompts, but it preserves the same format recognition rules.
// Decision: Keep the managed parsing surface and document its legacy file-format lineage through tests.
namespace XFoil.Core.Tests;

public sealed class AirfoilParserTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xfoil.f :: LOAD labeled-coordinate parsing path.
    // Difference from legacy: The test drives the parser directly with managed input lines instead of the legacy interactive loader.
    // Decision: Keep the managed direct-input test because it isolates the format-recognition behavior more cleanly.
    public void ParseLines_RecognizesLabeledCoordinateFormat()
    {
        var parser = new AirfoilParser();
        var geometry = parser.ParseLines(
            new[]
            {
                "NACA 0012",
                "1.0 0.0",
                "0.5 0.1",
                "0.0 0.0",
                "0.5 -0.1",
                "1.0 0.0"
            });

        Assert.Equal("NACA 0012", geometry.Name);
        Assert.Equal(AirfoilFormat.LabeledCoordinates, geometry.Format);
        Assert.Equal(5, geometry.Points.Count);
    }

    [Fact]
    // Legacy mapping: f_xfoil/src/xfoil.f :: LOAD ISES-domain parsing path.
    // Difference from legacy: The test uses a managed in-memory fixture instead of reading through the legacy file command path.
    // Decision: Keep the managed fixture because it precisely verifies the preserved ISES header recognition rule.
    public void ParseLines_RecognizesIsesDomainHeader()
    {
        var parser = new AirfoilParser();
        var geometry = parser.ParseLines(
            new[]
            {
                "TEST AIRFOIL",
                "-2.0 3.0 -2.5 3.0",
                "1.0 0.0",
                "0.0 0.0",
                "1.0 0.0"
            });

        Assert.Equal(AirfoilFormat.Ises, geometry.Format);
        Assert.Equal(4, geometry.DomainParameters.Count);
    }
}
