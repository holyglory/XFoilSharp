using XFoil.MsesSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// F2.3 — sign validation for the source-distribution coupling
/// loop (F2.2).
///
/// IMPORTANT SCOPE NOTE: the coupling as implemented is a one-way
/// BL-side feedback — it perturbs the edge velocity the BL marcher
/// sees (Ue ← Ue₀ + α·ΔUe from the source distribution) but does
/// NOT re-solve the inviscid system. So:
///   - CL is unchanged (still from the uncoupled inviscid).
///   - CD and δ*/θ change because the BL responds to perturbed Ue.
///   - Deep-stall convergence may improve because the aft-surface
///     ΔUe > 0 relieves adverse gradient growth.
///
/// Full two-way coupling (source-distribution contribution baked
/// into the inviscid Jacobian) would also modify CL, but requires
/// touching XFoil.Solver's linear-vortex system — out of scope for
/// the F2 finalization pass (parity gate). These pins establish
/// what the one-way coupling does, for drift detection.
/// </summary>
public class MsesSourceCouplingSignTests
{
    private static ViscousAnalysisResult RunCoupled(
        string naca, double alphaDeg, double Re, bool coupled)
    {
        var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
        var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: 0.0,
            reynoldsNumber: Re, nCritUpper: 9.0, nCritLower: 9.0);
        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true,
            useWakeMarcher: true,
            useThesisExactLaminar: true,
            useSourceDistributionCoupling: coupled,
            sourceCouplingIterations: 10,
            sourceCouplingRelaxation: 0.4);
        return svc.AnalyzeViscous(geom, alphaDeg, settings);
    }

    [Fact]
    public void Naca4412_Alpha4_CL_Unchanged_CD_Changes()
    {
        // Coupled is one-way: inviscid CL stays the same; CD shifts.
        var un = RunCoupled("4412", 4.0, 3_000_000, coupled: false);
        var co = RunCoupled("4412", 4.0, 3_000_000, coupled: true);
        Assert.True(un.Converged && co.Converged);

        // CL delta bounded by inviscid numerical noise (no viscous CL
        // feedback in the one-way coupling).
        Assert.True(System.Math.Abs(un.LiftCoefficient - co.LiftCoefficient) < 1e-6,
            $"CL should be invariant under one-way source coupling: "
            + $"uncoupled={un.LiftCoefficient:F6}, coupled={co.LiftCoefficient:F6}");

        // CD should shift observably (otherwise the coupling had
        // no effect, meaning our σ integration is near-zero and
        // the loop is a no-op — a regression).
        double dCd = System.Math.Abs(un.DragDecomposition.CD - co.DragDecomposition.CD);
        Assert.True(dCd > 1e-5,
            $"CD should change under coupling: uncoupled CD={un.DragDecomposition.CD:F6}, "
            + $"coupled CD={co.DragDecomposition.CD:F6}, |ΔCD|={dCd:F6}");
    }

    [Fact]
    public void Naca0012_Alpha0_Symmetric_CouplingPreservesSymmetry()
    {
        // Symmetric geometry at α=0 should have Xtr_U == Xtr_L
        // within half a panel, coupled or not. The source
        // perturbation must be symmetric if both surfaces see
        // identical δ*.
        var co = RunCoupled("0012", 0.0, 3_000_000, coupled: true);
        Assert.True(co.Converged);
        double xU = co.UpperTransition.XTransition;
        double xL = co.LowerTransition.XTransition;
        if (xU > 0 && xL > 0)
        {
            Assert.True(System.Math.Abs(xU - xL) < 0.02,
                $"Symmetric α=0 coupled Xtr should be symmetric; got U={xU:F4} L={xL:F4}");
        }
    }

    [Fact]
    public void DeepStall_Showcase_CouplingDoesNotReduceConvergenceCount()
    {
        // Run the F1.2 deep-stall matrix twice: uncoupled then
        // coupled. The coupled count must be >= uncoupled count
        // (coupling can ONLY help or be neutral on this class of
        // cases; it should never break a case that was already
        // converging).
        var cases = new (string naca, double a, double m)[]
        {
            ("0012", 10.0, 0.0), ("0012", 14.0, 0.0), ("0012", 16.0, 0.0),
            ("4412", 12.0, 0.0), ("4412", 14.0, 0.0), ("4412", 16.0, 0.0),
            ("4412", 18.0, 0.0),
        };
        int uncoupledConv = 0, coupledConv = 0;
        foreach (var (naca, a, m) in cases)
        {
            var gen = new XFoil.Core.Services.NacaAirfoilGenerator();
            var geom = gen.Generate4DigitClassic(naca, pointCount: 161);
            var settings = new AnalysisSettings(
                panelCount: 161, freestreamVelocity: 1.0, machNumber: m,
                reynoldsNumber: 3_000_000, nCritUpper: 9.0, nCritLower: 9.0);
            var un = new MsesAnalysisService(
                useThesisExactTurbulent: true, useWakeMarcher: true,
                useThesisExactLaminar: true,
                useSourceDistributionCoupling: false);
            var co = new MsesAnalysisService(
                useThesisExactTurbulent: true, useWakeMarcher: true,
                useThesisExactLaminar: true,
                useSourceDistributionCoupling: true,
                sourceCouplingIterations: 8,
                sourceCouplingRelaxation: 0.3);
            if (un.AnalyzeViscous(geom, a, settings).Converged) uncoupledConv++;
            if (co.AnalyzeViscous(geom, a, settings).Converged) coupledConv++;
        }
        Assert.True(coupledConv >= uncoupledConv,
            $"Coupling must not reduce deep-stall convergence: "
            + $"uncoupled={uncoupledConv}/{cases.Length}, coupled={coupledConv}/{cases.Length}");
    }
}
