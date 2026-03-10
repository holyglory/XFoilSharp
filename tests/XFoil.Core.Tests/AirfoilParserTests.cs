using XFoil.Core.Models;
using XFoil.Core.Services;

namespace XFoil.Core.Tests;

public sealed class AirfoilParserTests
{
    [Fact]
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
