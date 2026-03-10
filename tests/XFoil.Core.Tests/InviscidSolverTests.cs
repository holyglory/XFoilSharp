using XFoil.Core.Services;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

public sealed class InviscidSolverTests
{
    [Fact]
    public void SymmetricAirfoil_AtZeroAlpha_ProducesNearZeroLift()
    {
        var generator = new NacaAirfoilGenerator();
        var meshGenerator = new PanelMeshGenerator();
        var solver = new HessSmithInviscidSolver();

        var geometry = generator.Generate4Digit("0012", 161);
        var mesh = meshGenerator.Generate(geometry, 120);
        var result = solver.Analyze(mesh, 0d);

        Assert.InRange(result.LiftCoefficient, -0.12, 0.12);
    }

    [Fact]
    public void SymmetricAirfoil_AtPositiveAlpha_ProducesPositiveLift()
    {
        var generator = new NacaAirfoilGenerator();
        var meshGenerator = new PanelMeshGenerator();
        var solver = new HessSmithInviscidSolver();

        var geometry = generator.Generate4Digit("0012", 161);
        var mesh = meshGenerator.Generate(geometry, 120);
        var result = solver.Analyze(mesh, 5d);

        Assert.True(result.LiftCoefficient > 0.2d);
        Assert.True(double.IsFinite(result.PressureIntegratedLiftCoefficient));
        Assert.Equal(mesh.Panels.Count, result.PressureSamples.Count);
    }

    [Fact]
    public void CamberedAirfoil_AtZeroAlpha_ProducesPositiveLift()
    {
        var generator = new NacaAirfoilGenerator();
        var meshGenerator = new PanelMeshGenerator();
        var solver = new HessSmithInviscidSolver();

        var geometry = generator.Generate4Digit("2412", 161);
        var mesh = meshGenerator.Generate(geometry, 120);
        var result = solver.Analyze(mesh, 0d);

        Assert.True(result.LiftCoefficient > 0.05d);
    }
}
