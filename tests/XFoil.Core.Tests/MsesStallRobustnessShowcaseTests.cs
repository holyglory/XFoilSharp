using XFoil.MsesSolver.Services;
using XFoil.Solver.Models;

namespace XFoil.Core.Tests;

/// <summary>
/// Phase 4 — deep-stall showcase. Runs the fully-thesis-exact MSES
/// path (implicit-Newton laminar + implicit-Newton turbulent + wake
/// marcher) across the matrix where XFoil's lag-dissipation closure
/// historically diverged without facade rescue:
///
///   NACA 0012  α ∈ {10°, 12°, 14°, 16°, 18°}  M ∈ {0.0, 0.15, 0.3}
///   NACA 4412  α ∈ {12°, 14°, 16°, 18°, 20°}  M ∈ {0.0, 0.15, 0.3}
///
/// Acceptance:
///   1. Native-convergence rate ≥ 80 % across the 30-case matrix.
///   2. On every converged case, CD is in [0.01, 0.25] (no runaway
///      non-physical drag), CL is in [-0.5, 3.5] (sane magnitude —
///      note CL is inviscid and compressibility-boosted on M>0 cases
///      so the upper bound is generous).
///   3. Monotonic CD with α on the NACA 0012 M=0 subsweep — drag
///      must not drop in deep stall.
///
/// These pins are drift detectors, not accuracy gates. They confirm
/// that MSES's 2nd-order closure remains well-posed past separation
/// where the legacy closure diverges.
/// </summary>
public class MsesStallRobustnessShowcaseTests
{
    private static readonly (string naca, double alpha, double mach)[] ShowcaseMatrix = new[]
    {
        ("0012", 10.0, 0.0),  ("0012", 12.0, 0.0),  ("0012", 14.0, 0.0),
        ("0012", 16.0, 0.0),  ("0012", 18.0, 0.0),
        ("0012", 10.0, 0.15), ("0012", 12.0, 0.15), ("0012", 14.0, 0.15),
        ("0012", 16.0, 0.15), ("0012", 18.0, 0.15),
        ("0012", 10.0, 0.3),  ("0012", 12.0, 0.3),  ("0012", 14.0, 0.3),
        ("0012", 16.0, 0.3),  ("0012", 18.0, 0.3),

        ("4412", 12.0, 0.0),  ("4412", 14.0, 0.0),  ("4412", 16.0, 0.0),
        ("4412", 18.0, 0.0),  ("4412", 20.0, 0.0),
        ("4412", 12.0, 0.15), ("4412", 14.0, 0.15), ("4412", 16.0, 0.15),
        ("4412", 18.0, 0.15), ("4412", 20.0, 0.15),
        ("4412", 12.0, 0.3),  ("4412", 14.0, 0.3),  ("4412", 16.0, 0.3),
        ("4412", 18.0, 0.3),  ("4412", 20.0, 0.3),
    };

    private static ViscousAnalysisResult Run(string naca, double alpha, double mach)
    {
        var geom = new XFoil.Core.Services.NacaAirfoilGenerator()
            .Generate4DigitClassic(naca, pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 161, freestreamVelocity: 1.0, machNumber: mach,
            reynoldsNumber: 3_000_000);
        var svc = new MsesAnalysisService(
            useThesisExactTurbulent: true,
            useWakeMarcher: true,
            useThesisExactLaminar: true);
        return svc.AnalyzeViscous(geom, alpha, settings);
    }

    [Fact]
    public void Showcase_NativeConvergenceRate_AtLeast80Percent()
    {
        int converged = 0;
        var failures = new System.Collections.Generic.List<string>();
        foreach (var (naca, alpha, mach) in ShowcaseMatrix)
        {
            var r = Run(naca, alpha, mach);
            if (r.Converged) converged++;
            else failures.Add($"{naca} α={alpha}° M={mach}");
        }
        double rate = (double)converged / ShowcaseMatrix.Length;
        Assert.True(rate >= 0.80,
            $"Native convergence {converged}/{ShowcaseMatrix.Length} = {rate:P1}; "
            + $"target ≥ 80 %. Failures: {string.Join("; ", failures)}");
    }

    [Fact]
    public void Showcase_AllConvergedCases_HavePhysicalDragLiftMagnitudes()
    {
        foreach (var (naca, alpha, mach) in ShowcaseMatrix)
        {
            var r = Run(naca, alpha, mach);
            if (!r.Converged) continue;
            Assert.InRange(r.DragDecomposition.CD, 0.01, 0.25);
            Assert.InRange(r.LiftCoefficient, -0.5, 3.5);
        }
    }

    [Fact]
    public void Naca0012_M0_DeepStall_CdMonotonicWithAlpha()
    {
        double prevCd = 0.0;
        int comparisons = 0;
        foreach (var alpha in new[] { 10.0, 12.0, 14.0, 16.0, 18.0 })
        {
            var r = Run("0012", alpha, 0.0);
            if (!r.Converged) continue;
            if (prevCd > 0)
            {
                Assert.True(r.DragDecomposition.CD >= prevCd * 0.95,
                    $"CD(α={alpha}) = {r.DragDecomposition.CD:F4} "
                    + $"dropped > 5% below prev = {prevCd:F4}");
                comparisons++;
            }
            prevCd = r.DragDecomposition.CD;
        }
        Assert.True(comparisons >= 2,
            "Too few converged cases in the deep-stall subsweep to validate monotonicity");
    }
}
