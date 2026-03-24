using XFoil.Core.Services;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f inviscid operating-point workflow
// Secondary legacy source: f_xfoil/src/xpanel.f inviscid force integration
// Role in port: Verifies high-level inviscid analysis behavior reproduced by the managed analysis service.
// Differences: The managed tests exercise service APIs and typed results instead of the legacy command interpreter.
// Decision: Keep the managed façade tests because they preserve the same physical trends at the public API boundary.
namespace XFoil.Core.Tests;

public sealed class InviscidSolverTests
{
    [Fact]
    // Legacy mapping: legacy inviscid solve for a symmetric section at zero incidence.
    // Difference from legacy: Near-zero lift is asserted directly on the managed result instead of through printed coefficients.
    // Decision: Keep the managed invariant because it is the clearest regression for this physical behavior.
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
    // Legacy mapping: legacy inviscid positive-incidence solve.
    // Difference from legacy: Positive lift is checked through the managed result object rather than the legacy operating-point display.
    // Decision: Keep the managed trend test because it captures the same aerodynamic expectation.
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
    // Legacy mapping: legacy inviscid solve for cambered geometry at zero incidence.
    // Difference from legacy: The port exposes the result as structured data rather than an interactive state snapshot.
    // Decision: Keep the managed regression because it documents the preserved camber-induced lift behavior.
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
