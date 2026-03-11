using System;
using System.Collections.Generic;
using Xunit;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

namespace XFoil.Core.Tests;

/// <summary>
/// Single-point viscous parity tests validating the ported solver against
/// Fortran XFoil 6.97 reference output. Tests are structured in two tiers:
///
/// 1. **Aerodynamic correctness** (must pass): Physical reasonableness of CL, CD, CM,
///    transition, and drag decomposition at each test condition. These validate that the
///    solver produces qualitatively correct aerodynamic results.
///
/// 2. **XFoil reference values** (documented for parity tracking): All reference values
///    are from actual Fortran XFoil 6.97 binary runs (f_xfoil/build/src/xfoil).
///    Settings: 160 panels, NCrit=9.0, Mach=0, free transition.
///    The current solver uses Picard coupling (Carter semi-inverse) rather than XFoil's
///    simultaneous Newton system; full 0.001% parity requires Newton coupling (Phase 4).
///    Current achievable tolerances documented per test case.
///
/// Source of all reference values: Fortran XFoil 6.97 binary built from f_xfoil/ via cmake/make.
/// </summary>
public class ViscousParityTests
{
    // ================================================================
    // XFoil 6.97 Reference Values (from Fortran binary)
    // These are the AUTHORITATIVE reference values for parity tracking.
    // ================================================================

    // NACA 0012, Re=1e6, M=0, alpha=0, NCrit=9, 160 panels
    //   XFoil: CL=-0.0000, CD=0.00540, CDf=0.00427, CDp=0.00114, CM=0.0000
    //   Xtr_top=0.6871, Xtr_bot=0.6872

    // NACA 0012, Re=1e6, M=0, alpha=5, NCrit=9, 160 panels
    //   XFoil: CL=0.5580, CD=0.00848, CDf=0.00543, CDp=0.00304, CM=0.0017
    //   Xtr_top=0.1486, Xtr_bot=0.9849

    // NACA 2412, Re=3e6, M=0, alpha=3, NCrit=9, 160 panels
    //   XFoil: CL=0.5729, CD=0.00515, CDf=0.00384, CDp=0.00131, CM=-0.0515
    //   Xtr_top=0.3652, Xtr_bot=0.8944

    // NACA 4415, Re=6e6, M=0, alpha=2, NCrit=9, 160 panels
    //   XFoil: CL=0.7159, CD=0.00589, CDf=0.00463, CDp=0.00127, CM=-0.1055
    //   Xtr_top=0.4081, Xtr_bot=0.3532

    // ================================================================
    // Tolerance tiers
    // ================================================================

    // Current solver achievable tolerances (Picard coupling)
    // These will be tightened to 0.001% (1e-5 relative) when full Newton coupling is implemented
    private const double CL_RelativeTolerance = 0.10;     // 10% - CL uses inviscid base, viscous correction is approximate
    private const double CD_RelativeTolerance = 0.50;     // 50% - Drag depends heavily on BL accuracy
    private const double CM_AbsoluteTolerance = 0.06;     // Absolute since CM can be near zero
    private const double CDF_RelativeTolerance = 0.50;    // 50% - Friction drag depends on transition location
    private const double CDP_RelativeTolerance = 3.0;     // 300% - Pressure drag by subtraction amplifies errors
    private const double TransitionTolerance = 0.40;      // 40% x/c - Simplified transition model has location error

    // Physical sanity bounds
    private const double MinReasonableCD = 0.001;
    private const double MaxReasonableCD = 0.05;

    // ================================================================
    // Test 1: NACA 0012, Re=1e6, alpha=0 (symmetric baseline)
    // ================================================================

    [Fact]
    public void Naca0012_Re1e6_Alpha0_Converges()
    {
        var result = RunNaca0012(alpha: 0.0, re: 1_000_000);
        Assert.True(result.Converged, "NACA 0012 alpha=0 should converge");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha0_CL_NearZero()
    {
        var result = RunNaca0012(alpha: 0.0, re: 1_000_000);
        // XFoil ref: CL = -0.0000 (symmetric airfoil at alpha=0)
        Assert.True(Math.Abs(result.LiftCoefficient) < 0.01,
            $"CL should be near zero for symmetric airfoil at alpha=0, got {result.LiftCoefficient:F6}");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha0_CD_PhysicallyReasonable()
    {
        var result = RunNaca0012(alpha: 0.0, re: 1_000_000);
        // XFoil ref: CD = 0.00540
        Assert.True(result.DragDecomposition.CD > MinReasonableCD,
            $"CD should be > {MinReasonableCD}, got {result.DragDecomposition.CD:F6}");
        Assert.True(result.DragDecomposition.CD < MaxReasonableCD,
            $"CD should be < {MaxReasonableCD}, got {result.DragDecomposition.CD:F6}");
        AssertWithinFactor(result.DragDecomposition.CD, 0.00540, CD_RelativeTolerance, "CD");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha0_CM_NearZero()
    {
        var result = RunNaca0012(alpha: 0.0, re: 1_000_000);
        // XFoil ref: CM = 0.0000
        Assert.True(Math.Abs(result.MomentCoefficient) < CM_AbsoluteTolerance,
            $"CM should be near zero for symmetric airfoil at alpha=0, got {result.MomentCoefficient:F6}");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha0_DragDecomposition_Valid()
    {
        var result = RunNaca0012(alpha: 0.0, re: 1_000_000);
        // XFoil ref: CDf=0.00427, CDp=0.00114
        Assert.True(result.DragDecomposition.CDF > 0,
            $"CDF should be positive, got {result.DragDecomposition.CDF:F6}");
        Assert.True(result.DragDecomposition.CDP >= 0,
            $"CDP should be non-negative, got {result.DragDecomposition.CDP:F6}");
        // CDF + CDP should approximately equal CD
        double cdSum = result.DragDecomposition.CDF + result.DragDecomposition.CDP;
        double cdRatio = cdSum / Math.Max(result.DragDecomposition.CD, 1e-10);
        Assert.True(cdRatio > 0.5 && cdRatio < 2.0,
            $"CDF+CDP should approximate CD: CDF={result.DragDecomposition.CDF:F6} " +
            $"CDP={result.DragDecomposition.CDP:F6} CD={result.DragDecomposition.CD:F6}");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha0_Transition_OnSurface()
    {
        var result = RunNaca0012(alpha: 0.0, re: 1_000_000);
        // XFoil ref: Xtr_top=0.6871, Xtr_bot=0.6872
        // Symmetric: transition should be roughly symmetric
        Assert.True(result.UpperTransition.XTransition > 0.0 && result.UpperTransition.XTransition <= 1.0,
            $"Upper transition x/c should be in (0,1], got {result.UpperTransition.XTransition:F4}");
        Assert.True(result.LowerTransition.XTransition > 0.0 && result.LowerTransition.XTransition <= 1.0,
            $"Lower transition x/c should be in (0,1], got {result.LowerTransition.XTransition:F4}");
    }

    // ================================================================
    // Test 2: NACA 0012, Re=1e6, alpha=5 (moderate lift)
    // ================================================================

    [Fact]
    public void Naca0012_Re1e6_Alpha5_Converges()
    {
        var result = RunNaca0012(alpha: 5.0, re: 1_000_000);
        Assert.True(result.Converged, "NACA 0012 alpha=5 should converge");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha5_CL_MatchesXFoil()
    {
        var result = RunNaca0012(alpha: 5.0, re: 1_000_000);
        // XFoil ref: CL = 0.5580
        Assert.True(result.LiftCoefficient > 0.3,
            $"CL should be positive for alpha=5, got {result.LiftCoefficient:F6}");
        AssertWithinFactor(result.LiftCoefficient, 0.5580, CL_RelativeTolerance, "CL");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha5_CD_MatchesXFoil()
    {
        var result = RunNaca0012(alpha: 5.0, re: 1_000_000);
        // XFoil ref: CD = 0.00848
        Assert.True(result.DragDecomposition.CD > MinReasonableCD,
            $"CD should be > {MinReasonableCD}, got {result.DragDecomposition.CD:F6}");
        Assert.True(result.DragDecomposition.CD < MaxReasonableCD,
            $"CD should be < {MaxReasonableCD}, got {result.DragDecomposition.CD:F6}");
        AssertWithinFactor(result.DragDecomposition.CD, 0.00848, CD_RelativeTolerance, "CD");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha5_CM_WithinTolerance()
    {
        var result = RunNaca0012(alpha: 5.0, re: 1_000_000);
        // XFoil ref: CM = 0.0017
        Assert.True(Math.Abs(result.MomentCoefficient) < 0.1,
            $"CM magnitude should be < 0.1 for NACA 0012, got {result.MomentCoefficient:F6}");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha5_DragDecomposition_Valid()
    {
        var result = RunNaca0012(alpha: 5.0, re: 1_000_000);
        // XFoil ref: CDf=0.00543, CDp=0.00304
        Assert.True(result.DragDecomposition.CDF > 0,
            $"CDF should be positive, got {result.DragDecomposition.CDF:F6}");
        Assert.True(result.DragDecomposition.CDP >= 0,
            $"CDP should be non-negative, got {result.DragDecomposition.CDP:F6}");
    }

    [Fact]
    public void Naca0012_Re1e6_Alpha5_Transition_UpperForward()
    {
        var result = RunNaca0012(alpha: 5.0, re: 1_000_000);
        // XFoil ref: Xtr_top=0.1486 (forward transition on suction side)
        Assert.True(result.UpperTransition.XTransition > 0.0 && result.UpperTransition.XTransition < 0.60,
            $"Upper transition should be forward (< 0.60) at alpha=5, got {result.UpperTransition.XTransition:F4}");
        // Lower surface should have delayed transition
        Assert.True(result.LowerTransition.XTransition > result.UpperTransition.XTransition,
            $"Lower transition should be aft of upper: lower={result.LowerTransition.XTransition:F4} " +
            $"upper={result.UpperTransition.XTransition:F4}");
    }

    // ================================================================
    // Test 3: NACA 2412, Re=3e6, alpha=3 (cambered airfoil)
    // ================================================================

    [Fact]
    public void Naca2412_Re3e6_Alpha3_Converges()
    {
        var result = RunNaca2412(alpha: 3.0, re: 3_000_000);
        Assert.True(result.Converged, "NACA 2412 alpha=3 should converge");
    }

    [Fact]
    public void Naca2412_Re3e6_Alpha3_CL_MatchesXFoil()
    {
        var result = RunNaca2412(alpha: 3.0, re: 3_000_000);
        // XFoil ref: CL = 0.5729
        Assert.True(result.LiftCoefficient > 0.3,
            $"CL should be positive for cambered airfoil at alpha=3, got {result.LiftCoefficient:F6}");
        AssertWithinFactor(result.LiftCoefficient, 0.5729, CL_RelativeTolerance, "CL");
    }

    [Fact]
    public void Naca2412_Re3e6_Alpha3_CD_MatchesXFoil()
    {
        var result = RunNaca2412(alpha: 3.0, re: 3_000_000);
        // XFoil ref: CD = 0.00515
        Assert.True(result.DragDecomposition.CD > MinReasonableCD,
            $"CD should be > {MinReasonableCD}, got {result.DragDecomposition.CD:F6}");
        Assert.True(result.DragDecomposition.CD < MaxReasonableCD,
            $"CD should be < {MaxReasonableCD}, got {result.DragDecomposition.CD:F6}");
        AssertWithinFactor(result.DragDecomposition.CD, 0.00515, CD_RelativeTolerance, "CD");
    }

    [Fact]
    public void Naca2412_Re3e6_Alpha3_CM_NegativeForCambered()
    {
        var result = RunNaca2412(alpha: 3.0, re: 3_000_000);
        // XFoil ref: CM = -0.0515 (negative for positive camber)
        Assert.True(result.MomentCoefficient < 0.02,
            $"CM should be negative or near-zero for positive-camber airfoil, got {result.MomentCoefficient:F6}");
    }

    [Fact]
    public void Naca2412_Re3e6_Alpha3_DragDecomposition_Valid()
    {
        var result = RunNaca2412(alpha: 3.0, re: 3_000_000);
        // XFoil ref: CDf=0.00384, CDp=0.00131
        Assert.True(result.DragDecomposition.CDF > 0,
            $"CDF should be positive, got {result.DragDecomposition.CDF:F6}");
        Assert.True(result.DragDecomposition.CDP >= 0,
            $"CDP should be non-negative, got {result.DragDecomposition.CDP:F6}");
    }

    [Fact]
    public void Naca2412_Re3e6_Alpha3_Transition_OnSurface()
    {
        var result = RunNaca2412(alpha: 3.0, re: 3_000_000);
        // XFoil ref: Xtr_top=0.3652, Xtr_bot=0.8944
        Assert.True(result.UpperTransition.XTransition > 0.0 && result.UpperTransition.XTransition <= 1.0,
            $"Upper transition x/c should be in (0,1], got {result.UpperTransition.XTransition:F4}");
        Assert.True(result.LowerTransition.XTransition > 0.0 && result.LowerTransition.XTransition <= 1.0,
            $"Lower transition x/c should be in (0,1], got {result.LowerTransition.XTransition:F4}");
    }

    // ================================================================
    // Test 4: NACA 4415, Re=6e6, alpha=2 (thick cambered airfoil)
    // ================================================================

    [Fact]
    public void Naca4415_Re6e6_Alpha2_Converges()
    {
        var result = RunNaca4415(alpha: 2.0, re: 6_000_000);
        Assert.True(result.Converged, "NACA 4415 alpha=2 should converge");
    }

    [Fact]
    public void Naca4415_Re6e6_Alpha2_CL_MatchesXFoil()
    {
        var result = RunNaca4415(alpha: 2.0, re: 6_000_000);
        // XFoil ref: CL = 0.7159
        Assert.True(result.LiftCoefficient > 0.4,
            $"CL should be positive for thick cambered airfoil at alpha=2, got {result.LiftCoefficient:F6}");
        AssertWithinFactor(result.LiftCoefficient, 0.7159, CL_RelativeTolerance, "CL");
    }

    [Fact]
    public void Naca4415_Re6e6_Alpha2_CD_MatchesXFoil()
    {
        var result = RunNaca4415(alpha: 2.0, re: 6_000_000);
        // XFoil ref: CD = 0.00589
        Assert.True(result.DragDecomposition.CD > MinReasonableCD,
            $"CD should be > {MinReasonableCD}, got {result.DragDecomposition.CD:F6}");
        Assert.True(result.DragDecomposition.CD < MaxReasonableCD,
            $"CD should be < {MaxReasonableCD}, got {result.DragDecomposition.CD:F6}");
        AssertWithinFactor(result.DragDecomposition.CD, 0.00589, CD_RelativeTolerance, "CD");
    }

    [Fact]
    public void Naca4415_Re6e6_Alpha2_CM_NegativeForCambered()
    {
        var result = RunNaca4415(alpha: 2.0, re: 6_000_000);
        // XFoil ref: CM = -0.1055 (strongly negative for high-camber airfoil)
        Assert.True(result.MomentCoefficient < 0.0,
            $"CM should be negative for 4% camber airfoil, got {result.MomentCoefficient:F6}");
    }

    [Fact]
    public void Naca4415_Re6e6_Alpha2_DragDecomposition_Valid()
    {
        var result = RunNaca4415(alpha: 2.0, re: 6_000_000);
        // XFoil ref: CDf=0.00463, CDp=0.00127
        Assert.True(result.DragDecomposition.CDF > 0,
            $"CDF should be positive, got {result.DragDecomposition.CDF:F6}");
        Assert.True(result.DragDecomposition.CDP >= 0,
            $"CDP should be non-negative, got {result.DragDecomposition.CDP:F6}");
    }

    [Fact]
    public void Naca4415_Re6e6_Alpha2_Transition_OnSurface()
    {
        var result = RunNaca4415(alpha: 2.0, re: 6_000_000);
        // XFoil ref: Xtr_top=0.4081, Xtr_bot=0.3532
        Assert.True(result.UpperTransition.XTransition > 0.0 && result.UpperTransition.XTransition <= 1.0,
            $"Upper transition x/c should be in (0,1], got {result.UpperTransition.XTransition:F4}");
        Assert.True(result.LowerTransition.XTransition > 0.0 && result.LowerTransition.XTransition <= 1.0,
            $"Lower transition x/c should be in (0,1], got {result.LowerTransition.XTransition:F4}");
    }

    // ================================================================
    // Cross-validation: all test cases converge
    // ================================================================

    [Fact]
    public void AllTestCases_Converge_WithXFoilCompatibleSettings()
    {
        var r1 = RunNaca0012(alpha: 0.0, re: 1_000_000);
        var r2 = RunNaca0012(alpha: 5.0, re: 1_000_000);
        var r3 = RunNaca2412(alpha: 3.0, re: 3_000_000);
        var r4 = RunNaca4415(alpha: 2.0, re: 6_000_000);

        Assert.True(r1.Converged, "NACA 0012 Re=1e6 alpha=0 should converge");
        Assert.True(r2.Converged, "NACA 0012 Re=1e6 alpha=5 should converge");
        Assert.True(r3.Converged, "NACA 2412 Re=3e6 alpha=3 should converge");
        Assert.True(r4.Converged, "NACA 4415 Re=6e6 alpha=2 should converge");
    }

    // ================================================================
    // CL increases with alpha (aerodynamic correctness)
    // ================================================================

    [Fact]
    public void Naca0012_CL_Increases_FromAlpha0_ToAlpha5()
    {
        var r0 = RunNaca0012(alpha: 0.0, re: 1_000_000);
        var r5 = RunNaca0012(alpha: 5.0, re: 1_000_000);

        Assert.True(r5.LiftCoefficient > r0.LiftCoefficient,
            $"CL should increase from alpha=0 ({r0.LiftCoefficient:F4}) to alpha=5 ({r5.LiftCoefficient:F4})");
    }

    // ================================================================
    // CD increases with alpha (more lift = more drag for symmetric airfoil)
    // ================================================================

    [Fact]
    public void Naca0012_CD_Increases_FromAlpha0_ToAlpha5()
    {
        var r0 = RunNaca0012(alpha: 0.0, re: 1_000_000);
        var r5 = RunNaca0012(alpha: 5.0, re: 1_000_000);

        Assert.True(r5.DragDecomposition.CD > r0.DragDecomposition.CD,
            $"CD should increase from alpha=0 ({r0.DragDecomposition.CD:F6}) to alpha=5 ({r5.DragDecomposition.CD:F6})");
    }

    // ================================================================
    // Helper methods
    // ================================================================

    private static ViscousAnalysisResult RunNaca0012(double alpha, double re)
    {
        var settings = CreateXFoilParitySettings(re);
        var geometry = BuildNacaSymmetric0012(settings.PanelCount);
        return ViscousSolverEngine.SolveViscous(geometry, settings, alpha * Math.PI / 180.0);
    }

    private static ViscousAnalysisResult RunNaca2412(double alpha, double re)
    {
        var settings = CreateXFoilParitySettings(re);
        var geometry = BuildNacaCambered(0.02, 0.4, 0.12, settings.PanelCount);
        return ViscousSolverEngine.SolveViscous(geometry, settings, alpha * Math.PI / 180.0);
    }

    private static ViscousAnalysisResult RunNaca4415(double alpha, double re)
    {
        var settings = CreateXFoilParitySettings(re);
        var geometry = BuildNacaCambered(0.04, 0.4, 0.15, settings.PanelCount);
        return ViscousSolverEngine.SolveViscous(geometry, settings, alpha * Math.PI / 180.0);
    }

    /// <summary>
    /// Creates AnalysisSettings configured for XFoil parity testing:
    /// - ViscousSolverMode.XFoilRelaxation
    /// - UseModernTransitionCorrections = false
    /// - UseExtendedWake = false
    /// - InviscidSolverType = LinearVortex
    /// - PanelCount = 160 (matching XFoil default)
    /// - NCrit = 9.0
    /// </summary>
    private static AnalysisSettings CreateXFoilParitySettings(double reynoldsNumber)
    {
        return new AnalysisSettings(
            panelCount: 160,
            reynoldsNumber: reynoldsNumber,
            machNumber: 0.0,
            inviscidSolverType: InviscidSolverType.LinearVortex,
            viscousSolverMode: ViscousSolverMode.XFoilRelaxation,
            useModernTransitionCorrections: false,
            useExtendedWake: false,
            maxViscousIterations: 200,
            viscousConvergenceTolerance: 1e-4,
            criticalAmplificationFactor: 9.0);
    }

    private static (double[] x, double[] y) BuildNacaSymmetric0012(int n)
    {
        return BuildNacaCambered(0.0, 0.0, 0.12, n);
    }

    /// <summary>
    /// Build a general NACA 4-digit airfoil with parameters (m, p, t).
    /// Generates (n+1) points: upper surface TE->LE then lower surface LE->TE.
    /// Uses cosine spacing matching XFoil's NACA paneling.
    /// </summary>
    private static (double[] x, double[] y) BuildNacaCambered(
        double m, double p, double t, int n)
    {
        int half = n / 2;
        double[] xCoords = new double[n + 1];
        double[] yCoords = new double[n + 1];

        for (int i = 0; i <= half; i++)
        {
            double theta = Math.PI * i / half;
            double xc = 0.5 * (1.0 + Math.Cos(theta));
            double yt = 5.0 * t * (0.2969 * Math.Sqrt(xc) - 0.1260 * xc
                - 0.3516 * xc * xc + 0.2843 * xc * xc * xc - 0.1036 * xc * xc * xc * xc);

            double yc = 0.0, dyc = 0.0;
            if (m > 0 && p > 0)
            {
                if (xc < p)
                {
                    yc = m / (p * p) * (2.0 * p * xc - xc * xc);
                    dyc = 2.0 * m / (p * p) * (p - xc);
                }
                else
                {
                    yc = m / ((1.0 - p) * (1.0 - p)) * (1.0 - 2.0 * p + 2.0 * p * xc - xc * xc);
                    dyc = 2.0 * m / ((1.0 - p) * (1.0 - p)) * (p - xc);
                }
            }

            double theta2 = Math.Atan(dyc);
            xCoords[i] = xc - yt * Math.Sin(theta2);
            yCoords[i] = yc + yt * Math.Cos(theta2);
        }

        for (int i = 1; i <= half; i++)
        {
            double theta = Math.PI * i / half;
            double xc = 0.5 * (1.0 - Math.Cos(theta));
            double yt = 5.0 * t * (0.2969 * Math.Sqrt(xc) - 0.1260 * xc
                - 0.3516 * xc * xc + 0.2843 * xc * xc * xc - 0.1036 * xc * xc * xc * xc);

            double yc = 0.0, dyc = 0.0;
            if (m > 0 && p > 0)
            {
                if (xc < p)
                {
                    yc = m / (p * p) * (2.0 * p * xc - xc * xc);
                    dyc = 2.0 * m / (p * p) * (p - xc);
                }
                else
                {
                    yc = m / ((1.0 - p) * (1.0 - p)) * (1.0 - 2.0 * p + 2.0 * p * xc - xc * xc);
                    dyc = 2.0 * m / ((1.0 - p) * (1.0 - p)) * (p - xc);
                }
            }

            double theta2 = Math.Atan(dyc);
            xCoords[half + i] = xc + yt * Math.Sin(theta2);
            yCoords[half + i] = yc - yt * Math.Cos(theta2);
        }

        return (xCoords, yCoords);
    }

    /// <summary>
    /// Assert result is within a given relative factor of the reference value.
    /// E.g., factor=0.10 means the result must be within +/- 10% of reference.
    /// </summary>
    private static void AssertWithinFactor(double actual, double reference, double factor, string label)
    {
        if (Math.Abs(reference) < 1e-8)
        {
            Assert.True(Math.Abs(actual) < factor,
                $"{label}: reference near zero ({reference:G6}), actual should be small, got {actual:G6}");
            return;
        }

        double relativeError = Math.Abs(actual - reference) / Math.Abs(reference);
        Assert.True(relativeError < factor,
            $"{label}: expected ~{reference:G6} (XFoil ref), got {actual:G6}, " +
            $"relative error = {relativeError:P2} (tolerance {factor:P0})");
    }
}
