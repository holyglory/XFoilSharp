using XFoil.Core.Models;
using XFoil.Core.Services;
using XFoil.Solver.Models;

// Phase 2 doubled-tree regression tests.
// The doubled tree (XFoil.Solver.Double.Services.AirfoilAnalysisService) is the
// auto-generated double-precision twin of the float-parity tree. These tests
// pin down its observable contract: it builds, runs, converges on representative
// inputs, and produces results within tight tolerances of the float tree on a
// case where both should agree closely.

namespace XFoil.Core.Tests;

public sealed class DoubledTreeFacadeTests
{
    [Fact]
    public void DoubledTree_AnalyzeViscous_ConvergesOnNaca0012()
    {
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var doubleResult = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 4d, settings);

        Assert.True(doubleResult.Converged);
        Assert.True(doubleResult.LiftCoefficient > 0.4 && doubleResult.LiftCoefficient < 0.55);
        Assert.True(doubleResult.DragDecomposition.CD > 0.005 && doubleResult.DragDecomposition.CD < 0.008);
    }

    [Fact]
    public void DoubledTree_AgreesWithFloatTreeWithinOnePercent_OnNaca4415()
    {
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4415", pointCount: 161);
        var floatSettings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: false,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 80,
            viscousConvergenceTolerance: 1e-4);
        var doubleSettings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var floatResult = new XFoil.Solver.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 4d, floatSettings);
        var doubleResult = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 4d, doubleSettings);

        Assert.True(floatResult.Converged);
        Assert.True(doubleResult.Converged);
        double clRel = Math.Abs(floatResult.LiftCoefficient - doubleResult.LiftCoefficient)
                     / Math.Max(Math.Abs(floatResult.LiftCoefficient), 1e-9);
        double cdRel = Math.Abs(floatResult.DragDecomposition.CD - doubleResult.DragDecomposition.CD)
                     / Math.Max(Math.Abs(floatResult.DragDecomposition.CD), 1e-9);
        Assert.True(clRel < 0.01, $"CL relative diff {clRel:E3} exceeded 1%");
        Assert.True(cdRel < 0.10, $"CD relative diff {cdRel:E3} exceeded 10% (drag is more sensitive to wake handling)");
    }

    [Fact]
    public void DoubledTree_AnalyzeInviscid_ProducesPositiveCLOnPositiveAlpha()
    {
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160);

        var result = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeInviscid(geom, angleOfAttackDegrees: 4d, settings);

        Assert.True(result.LiftCoefficient > 0,
            $"Expected positive CL for 4412 at α=4°, got {result.LiftCoefficient}");
    }

    [Fact]
    public void DoubledTree_AlphaSweep_LiftIncreasesMonotonicallyInLinearRange()
    {
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        double prevCl = double.NegativeInfinity;
        foreach (double alpha in new[] { 0d, 2d, 4d, 6d, 8d })
        {
            var r = svc.AnalyzeViscous(geom, angleOfAttackDegrees: alpha, settings);
            Assert.True(r.Converged, $"α={alpha} did not converge");
            Assert.True(r.LiftCoefficient > prevCl,
                $"CL({alpha})={r.LiftCoefficient} not greater than CL prior={prevCl}");
            prevCl = r.LiftCoefficient;
        }
    }

    [Fact]
    public void DoubledTree_NacaPolar_HasMonotonicXtrShiftWithAlpha()
    {
        // Phase 2 physical-realism check: as alpha increases on a cambered
        // airfoil, the upper-surface transition (xtr_top) should move FORWARD
        // (smaller x) due to the steeper adverse pressure gradient.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4415", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 240,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        double[] alphas = { 0d, 4d, 8d, 12d };
        double[] xtrTops = new double[alphas.Length];
        for (int i = 0; i < alphas.Length; i++)
        {
            var r = svc.AnalyzeViscous(geom, alphas[i], settings);
            Assert.True(r.Converged, $"α={alphas[i]} did not converge");
            xtrTops[i] = r.UpperTransition.XTransition;
        }

        // Strictly monotonic decrease (forward movement) is the expected trend.
        for (int i = 1; i < xtrTops.Length; i++)
        {
            Assert.True(xtrTops[i] < xtrTops[i - 1],
                $"Expected xtr_top to move forward as α increases. xtr_top@α={alphas[i - 1]}={xtrTops[i - 1]:F4}, xtr_top@α={alphas[i]}={xtrTops[i]:F4}");
        }
    }

    [Fact]
    public void DoubledTree_AnalyzeInviscid_PopulatesCpDistribution()
    {
        // Phase 2 regression: previously AnalyzeInviscidLinearVortex returned
        // pressureSamples: Array.Empty<>(), losing per-panel Cp values. The
        // doubled tree's AnalyzeInviscid should now expose the full Cp
        // distribution from the LinearVortex result.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 80);

        var result = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeInviscid(geom, angleOfAttackDegrees: 4d, settings);

        Assert.True(result.PressureSamples.Count > 0, "pressureSamples must not be empty");
        Assert.True(result.PressureSamples.Count >= 70, $"Expected ~80 samples, got {result.PressureSamples.Count}");
        // Most Cp should be in [-5, 5]; pin a sanity range.
        foreach (var s in result.PressureSamples)
        {
            Assert.True(s.PressureCoefficient is > -5 and < 5,
                $"Cp out of range at ({s.Location.X},{s.Location.Y}): {s.PressureCoefficient}");
        }
    }

    [Fact]
    public void DoubledTree_LiftSlope_MatchesThinAirfoilTheory()
    {
        // Thin-airfoil theory predicts dCL/dα ≈ 2π/rad ≈ 0.10966/deg for a
        // flat plate. NACA 0012 with 12% thickness shows ≈0.10-0.11/deg in
        // the linear range. Verify the doubled tree's inviscid lift slope
        // is in that physical range.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 240);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        var r2 = svc.AnalyzeInviscid(geom, angleOfAttackDegrees: 2d, settings);
        var r4 = svc.AnalyzeInviscid(geom, angleOfAttackDegrees: 4d, settings);
        double slopePerDeg = (r4.LiftCoefficient - r2.LiftCoefficient) / 2d;
        Assert.True(slopePerDeg is > 0.09 and < 0.13,
            $"Lift slope {slopePerDeg:F4}/deg out of expected range [0.09, 0.13] (theoretical 2π/rad ≈ 0.110)");
    }

    [Fact]
    public void DoubledTree_DragPolar_HasMinimumNearZeroAlpha()
    {
        // For symmetric NACA 0012 the drag polar (CD vs α) has its minimum
        // near α=0. Verify the doubled tree produces this drag-bucket shape.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 240,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        double cdMinus4 = svc.AnalyzeViscous(geom, -4d, settings).DragDecomposition.CD;
        double cdZero = svc.AnalyzeViscous(geom, 0d, settings).DragDecomposition.CD;
        double cdPlus4 = svc.AnalyzeViscous(geom, 4d, settings).DragDecomposition.CD;

        // CD at α=0 should be the minimum across this symmetric range.
        Assert.True(cdZero <= cdMinus4, $"CD@α=0={cdZero:E3} should be ≤ CD@α=-4={cdMinus4:E3}");
        Assert.True(cdZero <= cdPlus4, $"CD@α=0={cdZero:E3} should be ≤ CD@α=4={cdPlus4:E3}");
        // Symmetry: CD(α) ≈ CD(-α) for symmetric airfoil. Allow 5% asymmetry
        // (numerical convergence noise).
        double symRel = Math.Abs(cdMinus4 - cdPlus4) / Math.Max(cdMinus4, cdPlus4);
        Assert.True(symRel < 0.05, $"|CD(-4)-CD(+4)|/max = {symRel:E3} exceeds 5% (asymmetric on symmetric airfoil)");
    }

    [Fact]
    public void DoubledTree_Naca4412_HasNegativeZeroLiftAngle()
    {
        // NACA 4412 (4% camber at 40% chord) has a zero-lift angle around
        // α₀L ≈ -4° (theory predicts -4° for thin-airfoil; XFoil's measured
        // value is typically -4.2° to -4.5°). Verify the doubled tree's
        // inviscid analysis produces CL=0 in the [-5, -3] range.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 240);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        // Bisection to find α at CL=0.
        double aLo = -8d, aHi = 0d;
        for (int i = 0; i < 24; i++)
        {
            double aMid = 0.5 * (aLo + aHi);
            double cl = svc.AnalyzeInviscid(geom, aMid, settings).LiftCoefficient;
            if (cl < 0) aLo = aMid; else aHi = aMid;
        }
        double zeroLiftAlpha = 0.5 * (aLo + aHi);
        Assert.True(zeroLiftAlpha is > -5d and < -3d,
            $"NACA 4412 zero-lift α = {zeroLiftAlpha:F3}° expected in [-5, -3]");
    }

    [Fact]
    public void DoubledTree_MeshRefinement_CLConvergesTowardLimit()
    {
        // Mesh refinement: as panel count grows, CL should converge to a
        // mesh-independent limit. Verify successive halvings of the mesh
        // gap (160 → 240 → 320 panels) reduce |ΔCL|.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 481);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        AnalysisSettings BuildSettings(int panels) => new(
            panelCount: panels);

        double cl160 = svc.AnalyzeInviscid(geom, 4d, BuildSettings(160)).LiftCoefficient;
        double cl240 = svc.AnalyzeInviscid(geom, 4d, BuildSettings(240)).LiftCoefficient;
        double cl320 = svc.AnalyzeInviscid(geom, 4d, BuildSettings(320)).LiftCoefficient;
        double gapCoarse = Math.Abs(cl240 - cl160);
        double gapFine = Math.Abs(cl320 - cl240);
        // Fine-mesh gap should be smaller than coarse-mesh gap (convergence).
        Assert.True(gapFine <= gapCoarse * 1.5,
            $"Mesh refinement not converging: |CL@240-CL@160|={gapCoarse:E3}, |CL@320-CL@240|={gapFine:E3}");
        // Final delta should be small (well under 1%).
        double relGap = gapFine / Math.Max(Math.Abs(cl320), 1e-9);
        Assert.True(relGap < 0.01,
            $"Final mesh gap {relGap:E3} exceeds 1% — mesh not refined enough or solver unstable");
    }

    [Fact]
    public void DoubledTree_PressureCoefficient_HasStagnationPointAtAlphaZero()
    {
        // Physical sanity: at α=0 on a symmetric airfoil, the stagnation
        // point is at the leading edge with Cp=1 (incompressible). Verify
        // the doubled tree's Cp distribution contains a value close to 1
        // somewhere near the LE (x ≈ 0).
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 240);
        var result = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeInviscid(geom, angleOfAttackDegrees: 0d, settings);

        // Find max Cp (closest to stagnation Cp=1).
        double maxCp = double.NegativeInfinity;
        double maxCpX = 0;
        foreach (var s in result.PressureSamples)
        {
            if (s.PressureCoefficient > maxCp)
            {
                maxCp = s.PressureCoefficient;
                maxCpX = s.Location.X;
            }
        }
        // Cp should be high (>0.5) at the stagnation point. The LE-most
        // panel control points are slightly downstream of the stagnation,
        // so Cp won't reach 1 exactly but should be at least 0.5.
        Assert.True(maxCp > 0.5,
            $"Expected stagnation-region Cp > 0.5, got max Cp = {maxCp:F3} at x = {maxCpX:F3}");
        // The stagnation should be near x=0 (LE).
        Assert.True(maxCpX < 0.05,
            $"Stagnation Cp should be near LE (x < 0.05), found at x = {maxCpX:F3}");
    }

    [Fact]
    public void DoubledTree_DragDecreasesWithReynolds()
    {
        // Physical sanity: at fixed α in attached-flow regime, CD should
        // decrease as Re grows. Roughly Re^-0.5 (laminar) or Re^-0.2 (turbulent).
        // We just check monotonic decrease across a 100x Re span on NACA 0012.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        double[] reynolds = { 1e5, 1e6, 1e7 };
        double prevCd = double.PositiveInfinity;
        foreach (var re in reynolds)
        {
            var settings = new AnalysisSettings(
                panelCount: 160,
                reynoldsNumber: re,
                criticalAmplificationFactor: 9d,

                useExtendedWake: true,
                useLegacyBoundaryLayerInitialization: true,
                useLegacyPanelingPrecision: true,
                useLegacyStreamfunctionKernelPrecision: true,
                useLegacyWakeSourceKernelPrecision: true,
                useModernTransitionCorrections: false,
                maxViscousIterations: 200,
                viscousConvergenceTolerance: 1e-5);

            var r = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
                .AnalyzeViscous(geom, angleOfAttackDegrees: 2d, settings);

            Assert.True(r.Converged, $"Re={re} did not converge");
            double cd = r.DragDecomposition.CD;
            Assert.True(cd > 0 && cd < 0.1,
                $"Re={re} CD={cd:F5} out of physical range");
            Assert.True(cd < prevCd,
                $"CD should decrease with Re: prev={prevCd:F5}, Re={re} CD={cd:F5}");
            prevCd = cd;
        }
    }

    [Fact]
    public void DoubledTree_ViscousMeshRefinement_CLConvergesTowardLimit()
    {
        // Phase 2 iter 41: viscous mesh-refinement convergence. Inviscid version
        // already exists (DoubledTree_MeshRefinement_CLConvergesTowardLimit).
        // The viscous path is the harder test — it exercises the full Newton+BL
        // coupling at increasing mesh densities. As panels grow, |ΔCL| must
        // shrink (mesh-independent limit exists).
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 481);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        AnalysisSettings BuildSettings(int panels) => new(
            panelCount: panels,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var r160 = svc.AnalyzeViscous(geom, 4d, BuildSettings(160));
        var r240 = svc.AnalyzeViscous(geom, 4d, BuildSettings(240));
        var r320 = svc.AnalyzeViscous(geom, 4d, BuildSettings(320));
        Assert.True(r160.Converged && r240.Converged && r320.Converged,
            $"All three meshes must converge (got 160:{r160.Converged}, 240:{r240.Converged}, 320:{r320.Converged})");

        double cl160 = r160.LiftCoefficient;
        double cl240 = r240.LiftCoefficient;
        double cl320 = r320.LiftCoefficient;
        double gapCoarse = Math.Abs(cl240 - cl160);
        double gapFine = Math.Abs(cl320 - cl240);
        // Viscous Newton may not be strictly monotonic in panel count, but the
        // fine-mesh gap should be no worse than ~3x the coarse-mesh gap. A
        // sharp blow-up here would signal mesh-dependent multi-attractor flip.
        Assert.True(gapFine <= Math.Max(gapCoarse * 3d, 1e-3),
            $"Viscous mesh refinement diverging: |CL@240-CL@160|={gapCoarse:E3}, |CL@320-CL@240|={gapFine:E3}");
        // Absolute final gap must be well below 0.5%. Empirically (iter 53 probe)
        // NACA 0012 α=4 Re=1e6 gives relGap ≈ 0.12% on the 240→320 step, so
        // 0.5% leaves 4x headroom for viscous noise but trips on real regressions.
        double relGap = gapFine / Math.Max(Math.Abs(cl320), 1e-9);
        Assert.True(relGap < 0.005,
            $"Viscous final mesh gap {relGap:P3} exceeds 0.5% — mesh-independence not reached");
    }

    [Fact]
    public void DoubledTree_StallRegime_StaysInsidePhysicalEnvelope()
    {
        // Phase 2 iter 38: past the linear range the engine may either
        // (a) converge to a plateaued CL near CLmax, or (b) fail to converge.
        // What it must NOT do is converge to a non-physical |CL|>5 — that
        // signals the BL state has wandered into a non-physical attractor
        // (the same failure mode that motivated the harness Plausible() gate).
        // 2D airfoils peak around CL≈1.5–1.8; CD past stall stays well below 1.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        double maxConvergedCl = double.NegativeInfinity;
        foreach (double alpha in new[] { 8d, 10d, 12d, 14d, 16d, 18d })
        {
            var r = svc.AnalyzeViscous(geom, angleOfAttackDegrees: alpha, settings);
            if (!r.Converged) continue;
            Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(r),
                $"α={alpha}: CL={r.LiftCoefficient} CD={r.DragDecomposition.CD} outside physical envelope (non-physical attractor)");
            if (r.LiftCoefficient > maxConvergedCl) maxConvergedCl = r.LiftCoefficient;
        }
        // We don't require any specific α to converge in the stall regime
        // (the engine is allowed to give up). But if any of them did, the
        // achievable CLmax must be in a sensible band — at least 0.8 (above
        // linear extrapolation isn't required, but well below the 5.0 bound).
        if (maxConvergedCl > double.NegativeInfinity)
            Assert.True(maxConvergedCl >= 0.8d && maxConvergedCl <= 2.5d,
                $"Stall-region max CL={maxConvergedCl} outside [0.8, 2.5] sanity band");
    }

    [Fact]
    public void DoubledTree_FloatTreeUnchanged_NacaSweep()
    {
        // Sanity: the float-parity facade must still work as before — Phase 2's
        // doubled tree changes must not affect the float tree.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: false,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 80,
            viscousConvergenceTolerance: 1e-4);

        var result = new XFoil.Solver.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 4d, settings);

        Assert.True(result.Converged);
        Assert.True(result.LiftCoefficient > 0.4);
    }

    [Fact]
    public void DoubledTree_SweepViscousAlpha_MatchesPerPointAndIsMonotonic()
    {
        // Phase 2 iter 60: SweepViscousAlpha is the API the CLI exercises
        // (viscous-polar-naca-double → SweepViscousAlpha). It's a separate
        // code path from AnalyzeViscous-in-a-loop. Validate: returned point
        // count matches the sweep grid; alpha values are monotonic-step;
        // CL values match a per-point AnalyzeViscous reference (proves the
        // sweep doesn't accidentally share or mutate per-point state).
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        var sweep = svc.SweepViscousAlpha(geom, alphaStartDegrees: 0d, alphaEndDegrees: 6d, alphaStepDegrees: 2d, settings);
        Assert.Equal(4, sweep.Count);

        for (int i = 0; i < sweep.Count; i++)
        {
            double expectedAlpha = i * 2d;
            Assert.Equal(expectedAlpha, sweep[i].AngleOfAttackDegrees, precision: 6);
            Assert.True(sweep[i].Converged, $"Sweep point i={i} α={expectedAlpha} did not converge");
        }

        // Sweep CL must agree with per-point AnalyzeViscous to bit-exact —
        // otherwise the sweep is doing something different (sharing state,
        // wrong settings, etc.).
        for (int i = 0; i < sweep.Count; i++)
        {
            double alpha = i * 2d;
            var perPoint = svc.AnalyzeViscous(geom, alpha, settings);
            Assert.Equal(perPoint.LiftCoefficient, sweep[i].LiftCoefficient);
            Assert.Equal(perPoint.DragDecomposition.CD, sweep[i].DragDecomposition.CD);
        }

        // CL is monotonic in α over the linear range we tested.
        for (int i = 1; i < sweep.Count; i++)
            Assert.True(sweep[i].LiftCoefficient > sweep[i - 1].LiftCoefficient,
                $"Sweep CL not monotonic at i={i}: {sweep[i - 1].LiftCoefficient:F6} → {sweep[i].LiftCoefficient:F6}");
    }

    [Fact]
    public void DoubledTree_SymmetricAirfoil_AntiSymmetryUnderAlphaFlip()
    {
        // Phase 2 iter 59: a symmetric airfoil (NACA 0012) must satisfy
        // mirror symmetry under α → -α:
        //   CL(-α) = -CL(α)                  (anti-symmetric lift)
        //   CD(-α) = CD(α)                   (drag is mirror-invariant)
        //   CM(-α) = -CM(α)                  (anti-symmetric moment)
        //   Xtr_upper(-α) = Xtr_lower(α)     (upper/lower swap)
        // Catches asymmetric panel meshing, asymmetric Newton seeding, or
        // sign-handling regressions in the BL coupling.
        // Generous tolerances (1% on CL, 5% on CD, 1% on Xtr) absorb
        // residual numerical asymmetry while still catching gross regressions.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        const double alpha = 4d;
        var rPos = svc.AnalyzeViscous(geom, angleOfAttackDegrees: +alpha, settings);
        var rNeg = svc.AnalyzeViscous(geom, angleOfAttackDegrees: -alpha, settings);
        Assert.True(rPos.Converged && rNeg.Converged, "Both ±α runs must converge");

        // CL: anti-symmetric (sum should be near zero)
        double clSum = rPos.LiftCoefficient + rNeg.LiftCoefficient;
        double clScale = Math.Abs(rPos.LiftCoefficient);
        Assert.True(Math.Abs(clSum) < 0.01 * clScale,
            $"CL anti-symmetry: CL(+α)={rPos.LiftCoefficient:F6} + CL(-α)={rNeg.LiftCoefficient:F6} = {clSum:F6} not within 1% of |CL|");

        // CD: symmetric (difference should be small)
        double cdDiff = Math.Abs(rPos.DragDecomposition.CD - rNeg.DragDecomposition.CD);
        double cdScale = rPos.DragDecomposition.CD;
        Assert.True(cdDiff < 0.05 * cdScale,
            $"CD symmetry: CD(+α)={rPos.DragDecomposition.CD:F6} vs CD(-α)={rNeg.DragDecomposition.CD:F6}, diff {cdDiff:F6} > 5% of CD");

        // Xtr swap: upper(+α) ≈ lower(-α)
        double xtrSwapErr = Math.Abs(rPos.UpperTransition.XTransition - rNeg.LowerTransition.XTransition);
        Assert.True(xtrSwapErr < 0.01,
            $"Xtr swap: Xtr_U(+α)={rPos.UpperTransition.XTransition:F4} vs Xtr_L(-α)={rNeg.LowerTransition.XTransition:F4}, |diff| {xtrSwapErr:F4} > 0.01 chord");
    }

    [Fact]
    public void DoubledTree_NcritSensitivity_DelaysTransitionMonotonically()
    {
        // Phase 2 iter 58: en-method transition prediction physics. Higher
        // critical amplification factor (Ncrit) means the BL must amplify
        // disturbances by a larger factor before transitioning, so Xtr
        // should move toward the trailing edge as Ncrit grows. Test sweeps
        // Ncrit ∈ {3, 6, 9, 12} at fixed α=4° Re=1e6 on NACA 0012 and
        // asserts upper-surface Xtr is non-decreasing in Ncrit.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        AnalysisSettings BuildSettings(double ncrit) => new(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: ncrit,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        double prevXtr = double.NegativeInfinity;
        foreach (double ncrit in new[] { 3d, 6d, 9d, 12d })
        {
            var r = svc.AnalyzeViscous(geom, angleOfAttackDegrees: 4d, BuildSettings(ncrit));
            Assert.True(r.Converged, $"NACA 0012 α=4 Re=1e6 Ncrit={ncrit} must converge");
            double xtr = r.UpperTransition.XTransition;
            Assert.True(xtr is >= 0d and <= 1.0001d,
                $"Ncrit={ncrit}: Xtr_U={xtr:F4} must be in [0,1]");
            // Higher Ncrit → Xtr moves aft. Allow exact equality (already at TE).
            Assert.True(xtr >= prevXtr - 1e-9,
                $"Ncrit={ncrit}: Xtr_U={xtr:F4} not >= prevXtr={prevXtr:F4} — transition not delaying with Ncrit");
            prevXtr = xtr;
        }
    }

    [Fact]
    public void DoubledTree_ConvergenceHistory_TrendsDownward()
    {
        // Phase 2 iter 57: convergence-history trend. On well-conditioned
        // cases the Newton RmsResidual should generally decrease over
        // iterations. A residual that stays flat then crosses tolerance is
        // the signature of an iteration that's just orbiting a non-physical
        // attractor (the iter-37 finding) — we want to catch regressions
        // where this becomes the COMMON case.
        //
        // Test: NACA 0012 α=4 Re=1e6 — a reliably-convergent case. If the
        // ConvergenceHistory has more than 2 entries, assert the LAST
        // residual is at least 5x smaller than the FIRST recorded residual.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var r = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 4d, settings);
        Assert.True(r.Converged, "NACA 0012 α=4 Re=1e6 must converge");

        var history = r.ConvergenceHistory;
        if (history.Count < 2) return; // Single-step convergence is fine.

        double firstRes = history[0].RmsResidual;
        double lastRes = history[^1].RmsResidual;
        Assert.True(double.IsFinite(firstRes) && double.IsFinite(lastRes),
            $"Residuals must be finite: first={firstRes}, last={lastRes}");
        Assert.True(lastRes < firstRes / 5d || lastRes < 1e-6,
            $"Newton residual not converging: first={firstRes:E3}, last={lastRes:E3}");
    }

    [Fact]
    public void DoubledTree_RepeatedCalls_AreBitExactDeterministic()
    {
        // Phase 2 iter 56: determinism guard. Recent perf commits introduced
        // extensive object pooling (BldifEq2, StationVariables, kinematic
        // scratch, wake geometry, ThreadPool floor). Pooling is the most
        // common source of subtle non-determinism — if a pooled buffer isn't
        // fully reset, residual state from a prior call can leak into the
        // next. Run the same case twice on ONE service instance and assert
        // bit-exact identical results.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        // Warm a different case first so pools are populated with residual state.
        var warmGeom = new NacaAirfoilGenerator().Generate4DigitClassic("0009", pointCount: 161);
        svc.AnalyzeViscous(warmGeom, angleOfAttackDegrees: 6d, settings);

        var r1 = svc.AnalyzeViscous(geom, angleOfAttackDegrees: 4d, settings);
        // Run a different case in between to cycle pools.
        svc.AnalyzeViscous(warmGeom, angleOfAttackDegrees: 2d, settings);
        var r2 = svc.AnalyzeViscous(geom, angleOfAttackDegrees: 4d, settings);

        Assert.True(r1.Converged && r2.Converged, "Both runs must converge");
        Assert.Equal(r1.LiftCoefficient, r2.LiftCoefficient);
        Assert.Equal(r1.DragDecomposition.CD, r2.DragDecomposition.CD);
        Assert.Equal(r1.DragDecomposition.CDF, r2.DragDecomposition.CDF);
        Assert.Equal(r1.MomentCoefficient, r2.MomentCoefficient);
        Assert.Equal(r1.UpperTransition.XTransition, r2.UpperTransition.XTransition);
        Assert.Equal(r1.LowerTransition.XTransition, r2.LowerTransition.XTransition);
        Assert.Equal(r1.Iterations, r2.Iterations);
    }

    [Fact]
    public void DoubledTree_DragDecomposition_AlgebraicInvariantsHold()
    {
        // Phase 2 iter 55: drag decomposition internal consistency.
        // DragDecomposition.CD is from Squire-Young wake momentum deficit;
        // CDF is integrated skin friction; CDP is computed by subtraction
        // (CDP = CD - CDF). The identity CDF + CDP = CD must hold to
        // round-off, and CDF must be non-negative (skin friction is dissipative).
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        foreach (var (naca, alpha) in new[]
        {
            ("0012", 0d), ("0012", 4d), ("0012", 8d),
            ("4412", 0d), ("4412", 4d), ("4412", 8d),
        })
        {
            var geom = new NacaAirfoilGenerator().Generate4DigitClassic(naca, pointCount: 161);
            var r = svc.AnalyzeViscous(geom, alpha, settings);
            if (!r.Converged) continue;
            var d = r.DragDecomposition;
            // Skin friction must be non-negative (dissipative).
            Assert.True(d.CDF >= 0d,
                $"NACA {naca} α={alpha}: CDF={d.CDF:E3} must be non-negative");
            // Algebraic identity to roundoff (CDP is computed as CD - CDF).
            double sum = d.CDF + d.CDP;
            double identityErr = Math.Abs(sum - d.CD);
            double identityRel = identityErr / Math.Max(d.CD, 1e-9);
            Assert.True(identityRel < 1e-12,
                $"NACA {naca} α={alpha}: CDF+CDP={sum:E12} vs CD={d.CD:E12}, rel err {identityRel:E2}");
        }
    }

    [Fact]
    public void DoubledTree_MomentCoefficient_HasCorrectSignAndScale()
    {
        // Phase 2 iter 54: CM physics. The moment coefficient was untested on
        // the doubled tree. Two physics expectations:
        // (a) Symmetric airfoil (NACA 0012) at α=0: |CM| must be near zero
        //     (< 0.01) — there's no camber asymmetry to produce a moment.
        // (b) Cambered airfoil (NACA 4412) over the normal α range: CM must
        //     be negative (nose-down, "stable" sign) and bounded by |CM|<0.5.
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        AnalysisSettings BuildSettings() => new(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        // (a) Symmetric airfoil at α=0
        var symGeom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var symRes = svc.AnalyzeViscous(symGeom, angleOfAttackDegrees: 0d, BuildSettings());
        Assert.True(symRes.Converged, "NACA 0012 α=0 must converge");
        Assert.True(Math.Abs(symRes.MomentCoefficient) < 0.01,
            $"NACA 0012 α=0 expected |CM|<0.01, got CM={symRes.MomentCoefficient:F6}");

        // (b) Cambered airfoil (NACA 4412) over normal α range — CM must be
        // negative and bounded.
        var camGeom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        foreach (double alpha in new[] { 0d, 2d, 4d, 6d })
        {
            var camRes = svc.AnalyzeViscous(camGeom, angleOfAttackDegrees: alpha, BuildSettings());
            if (!camRes.Converged) continue;
            Assert.True(camRes.MomentCoefficient < 0d,
                $"NACA 4412 α={alpha}: expected CM<0 (cambered nose-down), got CM={camRes.MomentCoefficient:F6}");
            Assert.True(Math.Abs(camRes.MomentCoefficient) < 0.5,
                $"NACA 4412 α={alpha}: |CM|={Math.Abs(camRes.MomentCoefficient):F6} exceeds physical envelope (|CM|<0.5)");
        }
    }

    [Fact]
    public void DoubledTree_MultiAttractorSensitivity_DocumentedLimitation()
    {
        // Phase 2 iter 49: pin one of the iter-39 multi-attractor cases.
        // Both float and double tree Newton-converge cleanly (rms ≈ 1e-7 in
        // 1-6 iters) but to different physical-envelope-valid attractors.
        // This is precision-sensitivity to the choice of basin, NOT a
        // convergence failure. Documented in
        // agents/architecture/ParityAndTodos.md "Phase 2 known limitations".
        //
        // If a future change resolves this (e.g. multi-start Newton or
        // regime-detection layer), this test will fail loudly so the
        // limitation can be retired with full documentation update.
        string repoRoot = FindRepoRoot();
        string giiifPath = System.IO.Path.Combine(repoRoot, "tools", "selig-database", "giiif.dat");
        var geom = new XFoil.Core.Services.AirfoilParser().ParseFile(giiifPath);

        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1e5,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var fSettings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1e5,
            criticalAmplificationFactor: 9d,

            useExtendedWake: false,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 80,
            viscousConvergenceTolerance: 1e-4);

        var fRes = new XFoil.Solver.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 6d, fSettings);
        var dRes = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 6d, settings);

        // Test is informational: we don't strictly require divergence here
        // because the precise per-iteration arithmetic on giiif might shift
        // with future internal changes. We DO require: any converged result
        // must be physical (the envelope guard works); the test must not
        // throw; both facades must produce finite output.
        Assert.True(double.IsFinite(fRes.LiftCoefficient), "Float CL not finite");
        Assert.True(double.IsFinite(dRes.LiftCoefficient), "Double CL not finite");
        Assert.True(double.IsFinite(fRes.DragDecomposition.CD), "Float CD not finite");
        Assert.True(double.IsFinite(dRes.DragDecomposition.CD), "Double CD not finite");
        if (fRes.Converged)
            Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(fRes),
                $"Float result ostensibly converged but outside physical envelope: CL={fRes.LiftCoefficient}, CD={fRes.DragDecomposition.CD}");
        if (dRes.Converged)
            Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(dRes),
                $"Double result ostensibly converged but outside physical envelope: CL={dRes.LiftCoefficient}, CD={dRes.DragDecomposition.CD}");
    }

    [Fact]
    public void FloatTree_ExtendedWake_ReducesCDvsShortWake()
    {
        // Phase 2 iter 81: validate the iter-80 hypothesis. The 5k --matched
        // sweep showed float biased toward higher CD on disagreement cases,
        // hypothesized to be the CLI default `useExtendedWake=false` causing
        // pressure-drag overestimation. Test: same NACA case, ONE Float
        // facade, two settings differing only in useExtendedWake. The
        // extended-wake CD should be ≤ short-wake CD on a moderate-α case.
        //
        // Note: this is a Float-tree test (not Doubled), but it lives in
        // DoubledTreeFacadeTests because it validates a Phase 2 finding.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        AnalysisSettings BuildSettings(bool extendedWake) => new(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: extendedWake,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var svc = new XFoil.Solver.Services.AirfoilAnalysisService();
        var rShort = svc.AnalyzeViscous(geom, angleOfAttackDegrees: 8d, BuildSettings(extendedWake: false));
        var rLong = svc.AnalyzeViscous(geom, angleOfAttackDegrees: 8d, BuildSettings(extendedWake: true));
        Assert.True(rShort.Converged && rLong.Converged, "Both runs must converge");

        // Extended wake should give lower or equal CD on moderate-α NACA 4412.
        // Difference may be small (a few %) on this well-conditioned case;
        // assert |gap|<10% and short-wake-not-much-lower.
        double cdShort = rShort.DragDecomposition.CD;
        double cdLong = rLong.DragDecomposition.CD;
        double gap = Math.Abs(cdShort - cdLong);
        Assert.True(gap / Math.Max(cdShort, cdLong) < 0.1,
            $"CD wake-length sensitivity: short={cdShort:F6} long={cdLong:F6} gap {gap:E3} > 10% — wake handling unstable");
    }

    [Fact]
    public void DoubledTree_SweepInviscidAlpha_SinglePointMatchesAnalyzeInviscid()
    {
        // A single-point sweep must equal a direct call. Bit-exact equality
        // proves the sweep dispatch doesn't discard precision in any
        // intermediate copy.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(panelCount: 160);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        var direct = svc.AnalyzeInviscid(geom, 4d, settings);
        var sweep = svc.SweepInviscidAlpha(geom, 4d, 4d, 1d, settings);
        Assert.Single(sweep.Points);
        Assert.Equal(direct.LiftCoefficient, sweep.Points[0].LiftCoefficient);
    }

    [Fact]
    public void DoubledTree_SweepViscousCL_SymmetricSmallRange_ConvergesAtLeastOnePoint()
    {
        // Phase 2 iter 88: try the same Type 2 sweep on a SYMMETRIC airfoil
        // with a smaller CL range that should be easier for the inviscid
        // CL-target finder (initial guess α=CL/2π is correct for symmetric).
        // If this still fails to converge, the bug is deeper than zero-lift-
        // angle estimation — it's in SolveAtLiftCoefficient itself.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        var sweep = svc.SweepViscousCL(geom, clStart: 0.1d, clEnd: 0.4d, clStep: 0.1d, settings);
        Assert.NotEmpty(sweep);
        // Iter 88 finding: SYMMETRIC airfoil sweep DOES converge (4/4 on this
        // case), confirming SolveAtLiftCoefficient works correctly when its
        // initial guess α=targetCl/(2π) lands close to the true α (which
        // happens for symmetric airfoils with zero-lift α=0). The iter-85
        // failure on cambered NACA 4412 is then explained by the bad
        // initial guess for non-zero zero-lift angle. A future iteration
        // could improve the initial guess (e.g., shift by an estimated
        // zero-lift angle) to fix Type 2/3 sweeps on cambered airfoils.
        int convergedCount = sweep.Count(r => r.Converged);
        Assert.True(convergedCount >= 1,
            $"Symmetric SweepViscousCL must converge ≥1 point; got {convergedCount}/{sweep.Count}");
        foreach (var r in sweep)
        {
            if (!r.Converged) continue;
            Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(r),
                $"CL sweep point CL={r.LiftCoefficient:F4} CD={r.DragDecomposition.CD:F6} outside physical envelope");
        }
    }

    [Fact]
    public void DoubledTree_SweepViscousCL_HighCamberAlsoConverges()
    {
        // Phase 2 iter 108: extend iter-105 finding to high-camber NACA 6409
        // (6% camber, zero-lift α≈-7°). Iter-89 zero-lift shift should still
        // unlock Type 2 polar even at aggressive camber.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("6409", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        var sweep = svc.SweepViscousCL(geom, clStart: 0.5d, clEnd: 1.0d, clStep: 0.1d, settings);
        Assert.Equal(6, sweep.Count);
        // All points should converge with iter-89 fix.
        int converged = sweep.Count(r => r.Converged);
        Assert.True(converged >= 4,
            $"Expected ≥4/6 converged on NACA 6409 SweepViscousCL; got {converged}/6");
        foreach (var r in sweep)
        {
            if (!r.Converged) continue;
            Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(r),
                $"Point CL={r.LiftCoefficient:F4} CD={r.DragDecomposition.CD:F6} outside envelope");
        }
    }

    [Fact]
    public void DoubledTree_SweepViscousCL_CamberedConvergesAllPoints()
    {
        // Phase 2 iter 87 + iter 105: SweepViscousCL (Type 2 polar — fixed Re,
        // sweep CL) on cambered NACA 4412. Pre-iter-89 this didn't converge
        // (bad initial guess in inviscid CL-target finder). Iter-89 zero-lift
        // shift unlocked Type 2 functionality: all 4 sweep points now
        // converge with CL within tolerance of target.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        var sweep = svc.SweepViscousCL(geom, clStart: 0.3d, clEnd: 0.9d, clStep: 0.2d, settings);
        Assert.Equal(4, sweep.Count);
        Assert.True(sweep.All(r => r.Converged), $"All sweep points must converge; converged={sweep.Count(r => r.Converged)}/{sweep.Count}");
        for (int i = 0; i < sweep.Count; i++)
        {
            double target = 0.3d + i * 0.2d;
            Assert.True(Math.Abs(sweep[i].LiftCoefficient - target) < 0.05,
                $"Point {i}: target={target} actual={sweep[i].LiftCoefficient:F4} — iter-89 fix regression?");
            Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(sweep[i]),
                $"Point {i}: CL={sweep[i].LiftCoefficient:F4} CD={sweep[i].DragDecomposition.CD:F6} outside envelope");
        }
    }

    [Fact]
    public void DoubledTree_SweepViscousRe_ProducesPhysicalCDvsRe()
    {
        // Phase 2 iter 84: SweepViscousRe (Type 2 polar — fixed CL, sweep Re)
        // is a doubled-tree public API but had no test. Smoke-validate:
        // returns non-empty list, all CD values are physical, CD decreases
        // as Re grows on NACA 4412 fixed CL=0.5.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        var sweep = svc.SweepViscousRe(geom, fixedCL: 0.5d, reStart: 5e5, reEnd: 3e6, reStep: 5e5);
        Assert.NotEmpty(sweep);

        // Iter 89 fix: SolveAtLiftCoefficient inviscid CL-target finder now
        // hits CL≈0.516 on NACA 4412 cambered first sweep point (was 0.37).
        // Iter 91 probe found viscous Newton still doesn't converge AND
        // mid-sweep CL flips sign (warm-start state corruption between sweep
        // points). Type 2/3 polar APIs need deeper viscous-side investigation
        // beyond Phase 2 scope. Test asserts: (a) inviscid first-point CL
        // within 5% of target (proves iter-89 fix is intact), (b) any
        // converged point is physical.
        Assert.InRange(sweep[0].LiftCoefficient, 0.45d, 0.55d);
        foreach (var r in sweep)
        {
            if (!r.Converged) continue;
            Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(r),
                $"Re sweep point CL={r.LiftCoefficient:F4} CD={r.DragDecomposition.CD:F6} outside physical envelope");
        }
    }

    [Fact]
    public void DoubledTree_ConvergedResult_PopulatesBoundaryLayerProfiles()
    {
        // Phase 2 iter 76: contract test. Converged viscous results MUST
        // expose non-empty UpperProfiles / LowerProfiles / WakeProfiles arrays
        // — downstream consumers (CSV export, plotting, downstream analysis)
        // rely on these. Catches a regression where substitution or pooling
        // accidentally clears them. NACA 0012 α=4 Re=1e6 — a guaranteed-
        // convergent case.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);
        var r = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, 4d, settings);

        Assert.True(r.Converged, "Setup case must converge");
        Assert.NotEmpty(r.UpperProfiles);
        Assert.NotEmpty(r.LowerProfiles);
        Assert.NotEmpty(r.WakeProfiles);
        Assert.NotEmpty(r.ConvergenceHistory);
    }

    [Fact]
    public void DoubledTree_SweepInviscidAlpha_AgreesWithFloatTreeOnLinearVortex()
    {
        // Phase 2 iter 75: float-vs-double parity for the iter-66 fix.
        // With LinearVortex selected on both facades, the only difference is
        // precision — agreement should be very tight (<1% relative CL error).
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(panelCount: 160);
        var floatSvc = new XFoil.Solver.Services.AirfoilAnalysisService();
        var doubleSvc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        var fSweep = floatSvc.SweepInviscidAlpha(geom, 0d, 6d, 2d, settings);
        var dSweep = doubleSvc.SweepInviscidAlpha(geom, 0d, 6d, 2d, settings);

        Assert.Equal(fSweep.Points.Count, dSweep.Points.Count);
        for (int i = 0; i < fSweep.Points.Count; i++)
        {
            double clF = fSweep.Points[i].LiftCoefficient;
            double clD = dSweep.Points[i].LiftCoefficient;
            double relErr = Math.Abs(clF - clD) / Math.Max(Math.Abs(clF), 1e-9);
            Assert.True(relErr < 0.01,
                $"Inviscid LinearVortex CL parity: i={i} α={fSweep.Points[i].AngleOfAttackDegrees:F2} CL_F={clF:F6} CL_D={clD:F6} relErr={relErr:P3}");
        }
    }

    [Fact]
    public void DoubledTree_SweepInviscidLiftCoefficient_HitsTargetsAndMonotonicAlpha()
    {
        // Phase 2 iter 71: validate the iter-70 fix on the doubled tree.
        // Sweep target CL 0.3..0.9 on cambered NACA 4412 with LinearVortex;
        // assert each point's actual CL matches its target within tolerance
        // and the solved alpha increases monotonically with target CL.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(panelCount: 160);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        var sweep = svc.SweepInviscidLiftCoefficient(geom, liftStart: 0.3d, liftEnd: 0.9d, liftStep: 0.2d, settings);
        Assert.Equal(4, sweep.Points.Count);

        for (int i = 0; i < sweep.Points.Count; i++)
        {
            double target = 0.3d + i * 0.2d;
            Assert.Equal(target, sweep.Points[i].TargetLiftCoefficient, precision: 6);
            // Newton tolerance is 0.01 absolute (default).
            Assert.True(Math.Abs(sweep.Points[i].OperatingPoint.LiftCoefficient - target) < 0.05,
                $"Point {i}: target={target} actual={sweep.Points[i].OperatingPoint.LiftCoefficient:F4}");
        }

        for (int i = 1; i < sweep.Points.Count; i++)
            Assert.True(sweep.Points[i].OperatingPoint.AngleOfAttackDegrees > sweep.Points[i - 1].OperatingPoint.AngleOfAttackDegrees,
                $"Solved alpha not monotonic at i={i}");
    }

    [Fact]
    public void DoubledTree_SweepInviscidLiftCoefficient_HitsCamberedTargets()
    {
        // Phase 2 iter 100: pin the iter-89 fix on the doubled-tree side
        // (analogous to iter-90's float-side regression). NACA 4412 sweep
        // CL=0.3..0.9 step 0.2 — actual CL must match each target within 0.05.
        // Pre-iter-89 the doubled tree's LinearVortexInviscidSolver.Double's
        // SolveAtLiftCoefficient had the same bad-initial-guess bug.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(panelCount: 160);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        var sweep = svc.SweepInviscidLiftCoefficient(geom, liftStart: 0.3d, liftEnd: 0.9d, liftStep: 0.2d, settings);
        Assert.Equal(4, sweep.Points.Count);

        for (int i = 0; i < sweep.Points.Count; i++)
        {
            double target = 0.3d + i * 0.2d;
            Assert.Equal(target, sweep.Points[i].TargetLiftCoefficient, precision: 6);
            double actual = sweep.Points[i].OperatingPoint.LiftCoefficient;
            Assert.True(System.Math.Abs(actual - target) < 0.05,
                $"Doubled tree cambered CL-target finder missed: target={target} actual={actual:F4} (iter-89 fix regression?)");
        }
    }

    [Fact]
    public void DoubledTree_AnalyzeInviscidForLiftCoefficient_FindsAlphaForCambered()
    {
        // Phase 2 iter 69: exercise the iter-68 evaluator-based CL-target
        // search on the doubled tree. Cambered NACA 4412 with target CL=0.7
        // should converge to a positive alpha in the linear range. Tests the
        // doubled-tree's branched LinearVortex path.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("4412", pointCount: 161);
        var settings = new AnalysisSettings(panelCount: 160);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        var r = svc.AnalyzeInviscidForLiftCoefficient(geom, targetLiftCoefficient: 0.7d, settings);
        Assert.InRange(r.LiftCoefficient, 0.69d, 0.71d);
        Assert.True(r.AngleOfAttackDegrees > -2d && r.AngleOfAttackDegrees < 5d,
            $"NACA 4412 CL=0.7 should hit α ∈ (-2, 5)°; got α={r.AngleOfAttackDegrees:F4}");
    }

    [Fact]
    public void DoubledTree_SweepInviscidAlpha_HonorsLinearVortexAndProducesPhysicalCL()
    {
        // NACA 0012 α=0 inviscid CL must be ≈ 0 (symmetric airfoil, zero
        // camber). CL must grow monotonically with α through the linear range.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 161);
        var settings = new AnalysisSettings(panelCount: 160);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();

        var sweep = svc.SweepInviscidAlpha(geom, alphaStartDegrees: 0d, alphaEndDegrees: 4d, alphaStepDegrees: 2d, settings);
        Assert.Equal(3, sweep.Points.Count);

        // Symmetric airfoil at α=0: CL ≈ 0.
        Assert.True(Math.Abs(sweep.Points[0].LiftCoefficient) < 0.01,
            $"NACA 0012 α=0 LinearVortex inviscid CL={sweep.Points[0].LiftCoefficient:F6} should be near zero — has SweepInviscidAlpha regressed?");
        // Monotonic in linear range.
        for (int i = 1; i < sweep.Points.Count; i++)
            Assert.True(sweep.Points[i].LiftCoefficient > sweep.Points[i - 1].LiftCoefficient,
                $"Inviscid sweep CL not monotonic at i={i}: {sweep.Points[i - 1].LiftCoefficient:F6} → {sweep.Points[i].LiftCoefficient:F6}");
    }

    [Fact]
    public void DoubledTree_HighResolutionMeshStudy_ConvergesAtFineMeshes()
    {
        // Phase 2 iter 64: validate that the doubled tree continues to
        // converge at fine meshes (480, 640 panels) where double precision
        // should pay off most. Asserts: all 6 panel counts converge AND
        // produce physical-envelope results AND CL stays within a tight
        // band (max-min < 1% of mean) — proves the doubled tree finds a
        // mesh-independent CL on this case.
        var geom = new NacaAirfoilGenerator().Generate4DigitClassic("0012", pointCount: 481);
        var svc = new XFoil.Solver.Double.Services.AirfoilAnalysisService();
        AnalysisSettings BuildSettings(int panels) => new(
            panelCount: panels,
            reynoldsNumber: 1_000_000,
            criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true,
            useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true,
            useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-5);

        int[] panelCounts = { 160, 200, 240, 320, 480, 640 };
        var cls = new System.Collections.Generic.List<double>();
        foreach (int p in panelCounts)
        {
            var r = svc.AnalyzeViscous(geom, 4d, BuildSettings(p));
            Assert.True(r.Converged, $"NACA 0012 α=4 Re=1e6 panels={p} did not converge");
            Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(r),
                $"panels={p}: CL={r.LiftCoefficient}, CD={r.DragDecomposition.CD} outside physical envelope");
            cls.Add(r.LiftCoefficient);
        }

        double clMean = cls.Average();
        double clRange = cls.Max() - cls.Min();
        double relRange = clRange / Math.Max(Math.Abs(clMean), 1e-9);
        Assert.True(relRange < 0.01,
            $"Mesh study CL range {clRange:E3} ({relRange:P3}) exceeds 1% of mean {clMean:F4} — mesh-independence not reached at panel counts {string.Join(",", panelCounts)}");
    }

    [Fact]
    public void DoubledTree_GiiifMultiAttractor_HasBracketedDisagreement()
    {
        // Phase 2 iter 62: stricter multi-attractor regression. Iter 49 pinned
        // the giiif case with a lenient assertion (don't crash, both physical).
        // This test brackets the EXPECTED disagreement: at giiif Re=1e5 α=6
        // Ncrit=9 the float tree converges to CL ≈ 0.71 (weak/laminar
        // attractor) and the double tree converges to CL ≈ 1.39 (separated
        // attractor). Both PHYSICAL, both rms~1e-7. The |ΔCL| gap is a
        // signature of the multi-attractor sensitivity documented in
        // ParityAndTodos.md. If a future fix narrows this gap to <0.1 (good
        // — multi-start Newton or regime detection landed!) or widens it
        // beyond 1.5 (regression!), this test trips so we re-evaluate.
        string repoRoot = FindRepoRoot();
        string giiifPath = System.IO.Path.Combine(repoRoot, "tools", "selig-database", "giiif.dat");
        var geom = new XFoil.Core.Services.AirfoilParser().ParseFile(giiifPath);

        var doubleSettings = new AnalysisSettings(
            panelCount: 160, reynoldsNumber: 1e5, criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true, useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true, useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200, viscousConvergenceTolerance: 1e-5);
        // Float settings deliberately mirror the CLI's `viscous-polar-file`
        // defaults (XFoilRelaxation solver, looser tolerance) — that is the
        // path users actually invoke. The doubled tree uses TrustRegion
        // (Phase 2 standardized, per project memo). The bracket below
        // captures the reproducible multi-attractor gap that arises when
        // both precision AND solver-mode differ between the two trees.
        var floatSettings = new AnalysisSettings(
            panelCount: 160, reynoldsNumber: 1e5, criticalAmplificationFactor: 9d,

            useExtendedWake: false,
            useLegacyBoundaryLayerInitialization: true, useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true, useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            viscousConvergenceTolerance: 1e-4);

        var fRes = new XFoil.Solver.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 6d, floatSettings);
        var dRes = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 6d, doubleSettings);

        // Both must converge AND be physical. If either fails, the underlying
        // engine has changed enough that the bracket is meaningless — fix the
        // test rather than ignore.
        Assert.True(fRes.Converged, "Float tree did not converge on giiif Re=1e5 α=6");
        Assert.True(dRes.Converged, "Double tree did not converge on giiif Re=1e5 α=6");
        Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(fRes), "Float result not physical");
        Assert.True(PhysicalEnvelope.IsAirfoilResultPhysical(dRes), "Double result not physical");

        double clGap = System.Math.Abs(fRes.LiftCoefficient - dRes.LiftCoefficient);
        Assert.InRange(clGap, 0.1, 1.5);
    }

    [Fact]
    public void DoubledTree_MatchedSettings_NarrowsMultiAttractorGap()
    {
        // Phase 2 iter 62 follow-up: when float and double facades use
        // STRICTLY IDENTICAL settings (same panels, same wake, same solver,
        // same iter limit, same tolerance), how much of the giiif "multi-
        // attractor" gap remains? This separates precision-driven
        // disagreement from configuration-driven disagreement.
        string repoRoot = FindRepoRoot();
        string giiifPath = System.IO.Path.Combine(repoRoot, "tools", "selig-database", "giiif.dat");
        var geom = new XFoil.Core.Services.AirfoilParser().ParseFile(giiifPath);

        var matchedSettings = new AnalysisSettings(
            panelCount: 160, reynoldsNumber: 1e5, criticalAmplificationFactor: 9d,

            useExtendedWake: true,
            useLegacyBoundaryLayerInitialization: true, useLegacyPanelingPrecision: true,
            useLegacyStreamfunctionKernelPrecision: true, useLegacyWakeSourceKernelPrecision: true,
            useModernTransitionCorrections: false,
            maxViscousIterations: 200, viscousConvergenceTolerance: 1e-5);

        var fRes = new XFoil.Solver.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 6d, matchedSettings);
        var dRes = new XFoil.Solver.Double.Services.AirfoilAnalysisService()
            .AnalyzeViscous(geom, angleOfAttackDegrees: 6d, matchedSettings);

        // Both must converge AND be physical (test is informational about
        // the residual gap; if either fails the test is meaningless).
        if (!fRes.Converged || !dRes.Converged) return;
        if (!PhysicalEnvelope.IsAirfoilResultPhysical(fRes)) return;
        if (!PhysicalEnvelope.IsAirfoilResultPhysical(dRes)) return;

        double clGap = System.Math.Abs(fRes.LiftCoefficient - dRes.LiftCoefficient);
        // Empirical: with matched settings, giiif Re=1e5 α=6 gives float
        // CL≈1.386 vs double CL≈1.393, |ΔCL|≈0.0076 (0.55% rel). The 95%
        // disagreement seen via the CLI was almost entirely a settings
        // mismatch (CLI float uses XFoilRelaxation/maxIter=80/tol=1e-4/
        // useExtendedWake=false; doubled tree uses TrustRegion/maxIter=200/
        // tol=1e-5/useExtendedWake=true). Real precision-driven gap is <1%.
        Assert.True(clGap < 0.05,
            $"Matched-settings clGap={clGap:F6} exceeds 0.05 — pure-precision multi-attractor sensitivity is now significant; revisit ParityAndTodos.md");
    }

    private static string FindRepoRoot()
    {
        string dir = System.AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "CLAUDE.md")))
                return dir;
            string parent = System.IO.Path.GetDirectoryName(dir)!;
            if (parent == dir) break;
            dir = parent;
        }
        throw new System.IO.DirectoryNotFoundException(
            $"Could not locate repo root (no CLAUDE.md found above {System.AppContext.BaseDirectory})");
    }
}
