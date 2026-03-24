using XFoil.IO.Models;
using XFoil.IO.Services;

// Legacy audit:
// Primary legacy source: saved XFOIL polar file format
// Secondary legacy source: f_xfoil/src/polfit.f-style polar output conventions
// Role in port: Verifies the managed importer for legacy saved polar files used by compatibility workflows.
// Differences: The managed importer returns structured records and metadata instead of reading the file into the legacy runtime directly.
// Decision: Keep the managed importer and validate it against preserved legacy fixtures.
namespace XFoil.Core.Tests;

public sealed class LegacyPolarImporterTests
{
    [Fact]
    // Legacy mapping: legacy saved polar file parsing.
    // Difference from legacy: The test asserts parsed metadata and record fields through managed DTOs instead of through the legacy plotting and reporting pipeline.
    // Decision: Keep the managed compatibility test because it protects interoperability with historical polar outputs.
    public void Import_ParsesLegacySavedPolarFixture()
    {
        var importer = new LegacyPolarImporter();

        var polar = importer.Import(TestDataPaths.GetRunsFixturePath("e387_09.100"));

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
}
