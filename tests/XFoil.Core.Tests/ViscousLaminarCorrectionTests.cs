using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class ViscousLaminarCorrectionTests
{
    [Fact]
    public void ViscousLaminarCorrection_ReducesSurfaceMomentumResidualMagnitude()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("2412", 161);
        var settings = new AnalysisSettings(panelCount: 120, reynoldsNumber: 1_000_000d);

        var correction = service.AnalyzeViscousLaminarCorrection(geometry, 2d, settings, iterations: 3);

        var initialResidual = AverageAbsoluteMomentumResidual(correction.InitialSystem.UpperSurfaceIntervals)
                            + AverageAbsoluteMomentumResidual(correction.InitialSystem.LowerSurfaceIntervals);
        var correctedResidual = AverageAbsoluteMomentumResidual(correction.CorrectedSystem.UpperSurfaceIntervals)
                              + AverageAbsoluteMomentumResidual(correction.CorrectedSystem.LowerSurfaceIntervals);

        Assert.True(correctedResidual < initialResidual);
    }

    [Fact]
    public void ViscousLaminarCorrection_PreservesPositiveThicknessAndShapeFactor()
    {
        var generator = new NacaAirfoilGenerator();
        var service = new AirfoilAnalysisService();
        var geometry = generator.Generate4Digit("0012", 161);
        var settings = new AnalysisSettings(panelCount: 120, machNumber: 0.2d, reynoldsNumber: 500_000d);

        var correction = service.AnalyzeViscousLaminarCorrection(geometry, 4d, settings, iterations: 2);

        Assert.All(correction.CorrectedSystem.State.UpperSurface.Stations, AssertStation);
        Assert.All(correction.CorrectedSystem.State.LowerSurface.Stations, AssertStation);
    }

    private static double AverageAbsoluteMomentumResidual(IReadOnlyList<ViscousIntervalState> intervals)
    {
        return intervals.Average(interval => Math.Abs(interval.MomentumResidual));
    }

    private static void AssertStation(ViscousStationState station)
    {
        Assert.True(station.MomentumThickness > 0d);
        Assert.True(station.DisplacementThickness >= station.MomentumThickness);
        Assert.True(station.ShapeFactor > 1d);
        Assert.True(station.ReynoldsTheta > 0d);
    }
}
