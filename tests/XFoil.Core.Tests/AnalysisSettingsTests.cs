using System;
using Xunit;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

public sealed class AnalysisSettingsTests
{
    [Fact]
    public void Constructor_AllowsTwelvePanelParityCases()
    {
        var settings = new AnalysisSettings(panelCount: 12);

        Assert.Equal(12, settings.PanelCount);
    }

    [Fact]
    public void Constructor_RejectsCountsBelowMinimumSupportedPanelCount()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnalysisSettings(panelCount: AnalysisSettings.MinimumSupportedPanelCount - 1));

        Assert.Contains(
            AnalysisSettings.MinimumSupportedPanelCount.ToString(),
            exception.Message,
            StringComparison.Ordinal);
    }
}
