using XFoil.IO.Models;
using XFoil.IO.Services;

// Legacy audit:
// Primary legacy source: legacy reference polar file format
// Secondary legacy source: reference-polar plotting workflows
// Role in port: Verifies the managed importer for legacy reference polar fixtures.
// Differences: The managed port converts the legacy file into typed blocks and points instead of feeding it into the original plotting environment.
// Decision: Keep the managed compatibility importer because it is a .NET-facing wrapper over the preserved legacy data layout.
namespace XFoil.Core.Tests;

public sealed class LegacyReferencePolarImporterTests
{
    [Fact]
    // Legacy mapping: legacy reference polar file parsing.
    // Difference from legacy: The test reads the managed block model directly instead of checking the legacy plotting behavior that consumed the file.
    // Decision: Keep the managed parser test because it is the most direct compatibility check for the reference format.
    public void Import_ParsesReferencePolarFixture()
    {
        var importer = new LegacyReferencePolarImporter();

        var polar = importer.Import(TestDataPaths.GetRunsFixturePath("polref_100.387"));

        Assert.Equal("Langley LTPT", polar.Label);
        Assert.Equal(4, polar.Blocks.Count);
        Assert.Equal(LegacyReferencePolarBlockKind.DragVersusLift, polar.Blocks[0].Kind);
        Assert.Equal(LegacyReferencePolarBlockKind.AlphaVersusLift, polar.Blocks[1].Kind);
        Assert.Equal(LegacyReferencePolarBlockKind.AlphaVersusMoment, polar.Blocks[2].Kind);
        Assert.Equal(LegacyReferencePolarBlockKind.TransitionVersusLift, polar.Blocks[3].Kind);
        Assert.True(polar.Blocks[0].Points.Count > 10);
        Assert.True(polar.Blocks[1].Points.Count > 10);
        Assert.True(polar.Blocks[2].Points.Count > 10);
        Assert.Empty(polar.Blocks[3].Points);

        Assert.Equal(0.0201d, polar.Blocks[0].Points[0].X, 4);
        Assert.Equal(0.098d, polar.Blocks[0].Points[0].Y, 3);
        Assert.Equal(-2.97d, polar.Blocks[1].Points[0].X, 2);
        Assert.Equal(0.098d, polar.Blocks[1].Points[0].Y, 3);
    }
}
