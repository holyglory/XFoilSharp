using XFoil.Core.Services;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f :: CPCALC / Karman-Tsien correction path
// Secondary legacy source: f_xfoil/src/xpanel.f
// Role in port: Verifies that managed inviscid analysis exposes compressibility-corrected diagnostics consistent with the legacy subsonic correction workflow.
// Differences: The test compares managed result fields directly instead of reading legacy printed diagnostics.
// Decision: Keep the managed diagnostic test because the public API surfaces corrected and uncorrected coefficients separately.
namespace XFoil.Core.Tests;

public sealed class CompressibilityTests
{
    [Fact]
    // Legacy mapping: f_xfoil/src/xfoil.f compressibility correction path.
    // Difference from legacy: The managed test asserts field-level diagnostic changes instead of inspecting legacy command output.
    // Decision: Keep the managed regression because it directly protects the ported diagnostic contract.
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
