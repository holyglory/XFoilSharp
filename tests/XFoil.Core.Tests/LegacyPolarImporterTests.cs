using XFoil.IO.Models;
using XFoil.IO.Services;

namespace XFoil.Core.Tests;

public sealed class LegacyPolarImporterTests
{
    [Fact]
    public void Import_ParsesLegacySavedPolarFixture()
    {
        var importer = new LegacyPolarImporter();

        var polar = importer.Import(GetFixturePath("e387_09.100"));

        Assert.Equal("XFOIL", polar.SourceCode);
        Assert.Equal(6.90d, polar.Version.GetValueOrDefault(), 2);
        Assert.Equal("Eppler 387", polar.AirfoilName);
        Assert.Equal(1, polar.ElementCount);
        Assert.Equal(LegacyReynoldsVariationType.Fixed, polar.ReynoldsVariationType);
        Assert.Equal(LegacyMachVariationType.Fixed, polar.MachVariationType);
        Assert.Equal(0d, polar.ReferenceMachNumber.GetValueOrDefault(), 6);
        Assert.Equal(100_000d, polar.ReferenceReynoldsNumber.GetValueOrDefault(), 3);
        Assert.Equal(9d, polar.CriticalAmplificationFactor.GetValueOrDefault(), 6);
        Assert.Contains(polar.Columns, column => column.Key == "alpha");
        Assert.Contains(polar.Columns, column => column.Key == "CL");
        Assert.Contains(polar.Columns, column => column.Key == "Top_Xtr");
        Assert.Contains(polar.Columns, column => column.Key == "Bot_Xtr");
        Assert.True(polar.Records.Count > 50);

        var first = polar.Records[0].Values;
        Assert.Equal(-3.000d, first["alpha"], 3);
        Assert.Equal(0.1048d, first["CL"], 4);
        Assert.Equal(0.01946d, first["CD"], 5);
        Assert.Equal(0.01072d, first["CDp"], 5);
        Assert.Equal(-0.1011d, first["CM"], 4);
        Assert.Equal(0.9086d, first["Top_Xtr"], 4);
        Assert.Equal(0.0922d, first["Bot_Xtr"], 4);
    }

    private static string GetFixturePath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "runs", fileName));
    }
}
