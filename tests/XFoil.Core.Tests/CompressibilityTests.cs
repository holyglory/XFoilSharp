using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class CompressibilityTests
{
    [Fact]
    public void MachIncrease_ChangesCorrectedPressureDiagnostics()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);

        var incompressible = service.AnalyzeInviscid(geometry, 4d, new AnalysisSettings(panelCount: 120, machNumber: 0d));
        var compressible = service.AnalyzeInviscid(geometry, 4d, new AnalysisSettings(panelCount: 120, machNumber: 0.3d));

        Assert.NotEqual(incompressible.CorrectedPressureIntegratedLiftCoefficient, compressible.CorrectedPressureIntegratedLiftCoefficient);
        Assert.Equal(incompressible.PressureIntegratedLiftCoefficient, incompressible.CorrectedPressureIntegratedLiftCoefficient);
    }
}
