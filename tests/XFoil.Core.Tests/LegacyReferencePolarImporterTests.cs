using XFoil.IO.Models;
using XFoil.IO.Services;

namespace XFoil.Core.Tests;

public sealed class LegacyReferencePolarImporterTests
{
    [Fact]
    public void Import_ParsesReferencePolarFixture()
    {
        var importer = new LegacyReferencePolarImporter();

        var polar = importer.Import(GetFixturePath("polref_100.387"));

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

    private static string GetFixturePath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "runs", fileName));
    }
}
