using System;
using Xunit;
using XFoil.Solver.Models;
using XFoil.Solver.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xfoil.f drag accumulation and Squire-Young output
// Secondary legacy source: f_xfoil/src/xbl.f wake/drag support and post-stall extensions added in the port
// Role in port: Verifies the managed drag calculator and post-stall extrapolator layered over legacy-derived viscous state.
// Differences: The port exposes a structured drag decomposition and a managed post-stall extrapolator rather than only legacy scalar reporting.
// Decision: Keep the managed decomposition and post-stall extension while preserving legacy-derived drag formulas in the default viscous path.
namespace XFoil.Core.Tests;

/// <summary>
/// Tests for DragCalculator and PostStallExtrapolator.
/// Validates Squire-Young drag, skin friction drag, pressure drag,
/// surface cross-check, TE base drag, and Viterna-Corrigan post-stall.
/// </summary>
public class DragCalculatorTests
{
    // ================================================================
    // Squire-Young CD tests
    // ================================================================

    /// <summary>
    /// Squire-Young CD must be positive for any physically valid BL state.
    /// </summary>
    [Fact]
    // Legacy mapping: legacy Squire-Young total drag output.
    // Difference from legacy: Total CD is asserted on a managed drag-decomposition object instead of printed scalar output. Decision: Keep the managed regression because it directly protects the ported drag accumulation.
    public void ComputeDrag_ConvergedBLState_CDIsPositive()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();
        double mach = 0.0;
        double teGap = 0.001;

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, mach, teGap,
            useExtendedWake: false, useLockWaveDrag: false);

        Assert.True(result.CD > 0,
            $"CD should be positive, got {result.CD}");
    }

    /// <summary>
    /// CD should be in a physically reasonable range for NACA 0012 at Re=1e6.
    /// Expected: 0.003 < CD < 0.03.
    /// </summary>
    [Fact]
    // Legacy mapping: legacy total-drag magnitude for a converged viscous state.
    // Difference from legacy: The test uses a managed synthetic/ported state rather than a legacy runtime session. Decision: Keep the managed bounded-range check because it constrains the same physical output.
    public void ComputeDrag_ConvergedBLState_CDPhysicallyReasonable()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();
        double mach = 0.0;
        double teGap = 0.001;

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, mach, teGap,
            useExtendedWake: false, useLockWaveDrag: false);

        Assert.True(result.CD > 0.003 && result.CD < 0.03,
            $"CD should be in [0.003, 0.03], got {result.CD}");
    }

    // ================================================================
    // CDF (skin friction drag) tests
    // ================================================================

    /// <summary>
    /// Skin friction drag must be positive.
    /// </summary>
    [Fact]
    // Legacy mapping: skin-friction drag contribution.
    // Difference from legacy: The port exposes CDF explicitly as part of a structured result. Decision: Keep the managed decomposition test because it documents this public API improvement.
    public void ComputeDrag_ConvergedBLState_CDFIsPositive()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();
        double mach = 0.0;
        double teGap = 0.001;

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, mach, teGap,
            useExtendedWake: false, useLockWaveDrag: false);

        Assert.True(result.CDF > 0,
            $"CDF (skin friction drag) should be positive, got {result.CDF}");
    }

    /// <summary>
    /// CDF should be less than total CD (friction drag is a component).
    /// </summary>
    [Fact]
    // Legacy mapping: friction-drag contribution relative to total drag.
    // Difference from legacy: The decomposition relationship is checked directly on the managed result object. Decision: Keep the managed regression because it protects decomposition consistency.
    public void ComputeDrag_ConvergedBLState_CDFLessThanCD()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();
        double mach = 0.0;
        double teGap = 0.001;

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, mach, teGap,
            useExtendedWake: false, useLockWaveDrag: false);

        Assert.True(result.CDF <= result.CD,
            $"CDF ({result.CDF}) should be <= CD ({result.CD})");
    }

    // ================================================================
    // CDP (pressure drag) tests
    // ================================================================

    /// <summary>
    /// CDP = CD - CDF by definition (exact identity).
    /// </summary>
    [Fact]
    // Legacy mapping: CDP derived from total drag minus friction drag.
    // Difference from legacy: The identity is asserted directly on the managed drag decomposition instead of inferred from printed columns. Decision: Keep the managed test because it documents the explicit decomposition contract.
    public void ComputeDrag_ConvergedBLState_CDPEqualsSubtraction()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();
        double mach = 0.0;
        double teGap = 0.001;

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, mach, teGap,
            useExtendedWake: false, useLockWaveDrag: false);

        double expected = result.CD - result.CDF;
        Assert.True(Math.Abs(result.CDP - expected) < 1e-10,
            $"CDP ({result.CDP}) should equal CD - CDF ({expected})");
    }

    // ================================================================
    // Surface cross-check tests
    // ================================================================

    /// <summary>
    /// Surface cross-check (TE-based Squire-Young) is computed and produces a
    /// finite discrepancy metric. With synthetic BL data the TE-to-wake extrapolation
    /// gap is larger than with real converged data. Verify the metric is finite and
    /// non-negative. Real integration tests (ViscousSolverEngineTests) validate tighter bounds.
    /// </summary>
    [Fact]
    // Legacy mapping: surface drag cross-check.
    // Difference from legacy: The managed code exposes a direct surface cross-check discrepancy metric. Decision: Keep the managed regression because it provides extra observability over the same drag lineage.
    public void ComputeDrag_ConvergedBLState_SurfaceCrossCheckProducesFiniteDiscrepancy()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();
        double mach = 0.0;
        double teGap = 0.001;

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, mach, teGap,
            useExtendedWake: false, useLockWaveDrag: false);

        // Discrepancy metric should be finite, non-negative, and not NaN
        Assert.True(result.DiscrepancyMetric >= 0,
            $"Discrepancy metric should be >= 0, got {result.DiscrepancyMetric}");
        Assert.False(double.IsNaN(result.DiscrepancyMetric),
            "Discrepancy metric should not be NaN");
        Assert.True(result.CDSurfaceCrossCheck > 0,
            $"Surface cross-check CD should be positive, got {result.CDSurfaceCrossCheck}");
    }

    // ================================================================
    // TE base drag tests
    // ================================================================

    /// <summary>
    /// TE base drag should be non-negative and small relative to total CD.
    /// </summary>
    [Fact]
    // Legacy mapping: legacy trailing-edge base-drag contribution.
    // Difference from legacy: TE base drag is surfaced explicitly inside the managed decomposition. Decision: Keep the managed regression because it documents this component clearly.
    public void ComputeDrag_ConvergedBLState_TEBaseDragSmall()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();
        double mach = 0.0;
        double teGap = 0.01; // Moderate TE gap

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, mach, teGap,
            useExtendedWake: false, useLockWaveDrag: false);

        Assert.True(result.TEBaseDrag >= 0,
            $"TE base drag should be >= 0, got {result.TEBaseDrag}");
        Assert.True(result.TEBaseDrag < 0.01,
            $"TE base drag should be small (< 0.01), got {result.TEBaseDrag}");
    }

    // ================================================================
    // Wave drag tests
    // ================================================================

    /// <summary>
    /// For M < 0.7, WaveDrag should be null.
    /// </summary>
    [Fact]
    // Legacy mapping: subsonic drag with no wave-drag contribution.
    // Difference from legacy: The port exposes wave drag as an explicit field rather than folding it into a single scalar. Decision: Keep the managed decomposition test because it clarifies the output contract.
    public void ComputeDrag_SubsonicMach_WaveDragIsNull()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, machNumber: 0.3, teGap: 0.001,
            useExtendedWake: false, useLockWaveDrag: true);

        Assert.Null(result.WaveDrag);
    }

    /// <summary>
    /// For M > 0.7 with Lock wave drag enabled, WaveDrag should be non-null and positive.
    /// </summary>
    [Fact]
    // Legacy mapping: transonic/compressibility drag contribution.
    // Difference from legacy: Positive wave drag is checked directly on the managed result object. Decision: Keep the managed regression because it protects the added decomposition visibility.
    public void ComputeDrag_TransonicMach_WaveDragIsPositive()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, machNumber: 0.75, teGap: 0.001,
            useExtendedWake: false, useLockWaveDrag: true);

        Assert.NotNull(result.WaveDrag);
        Assert.True(result.WaveDrag > 0,
            $"Wave drag should be positive at M=0.75, got {result.WaveDrag}");
    }

    // ================================================================
    // Extended wake marching tests
    // ================================================================

    /// <summary>
    /// Extended wake should produce a valid (positive) CD.
    /// </summary>
    [Fact]
    // Legacy mapping: extended wake drag accumulation.
    // Difference from legacy: The managed test toggles an explicit useExtendedWake option rather than implicit runtime behavior. Decision: Keep the managed regression because it documents the extended-wake branch clearly.
    public void ComputeDrag_ExtendedWake_CDIsPositive()
    {
        var (blState, panel, qinf, alfa) = BuildConvergedBLState();

        var result = DragCalculator.ComputeDrag(
            blState, panel, qinf, alfa, machNumber: 0.0, teGap: 0.001,
            useExtendedWake: true, useLockWaveDrag: false);

        Assert.True(result.CD > 0,
            $"CD with extended wake should be positive, got {result.CD}");
    }

    // ================================================================
    // Viterna-Corrigan post-stall tests
    // ================================================================

    /// <summary>
    /// Post-stall CL should be less than CL at the last converged alpha (CL decreases after stall).
    /// </summary>
    [Fact]
    // Legacy mapping: none.
    // Difference from legacy: Post-stall extrapolation uses the managed Viterna-Corrigan extension, which has no direct Fortran XFoil analogue. Decision: Keep this managed-only test because the extension is intentional functionality in the port.
    public void ExtrapolatePostStall_HighAlpha_CLDecreases()
    {
        double alpha = 20.0 * Math.PI / 180.0;
        double lastAlpha = 12.0 * Math.PI / 180.0;
        double lastCL = 1.2;
        double lastCD = 0.02;
        double ar = 2.0 * Math.PI; // 2D effective AR

        var (cl, cd) = PostStallExtrapolator.ExtrapolatePostStall(
            alpha, lastAlpha, lastCL, lastCD, ar);

        Assert.True(cl < lastCL,
            $"Post-stall CL ({cl}) should be less than last converged CL ({lastCL})");
    }

    /// <summary>
    /// Post-stall CD should be positive and larger than last converged CD.
    /// </summary>
    [Fact]
    // Legacy mapping: none.
    // Difference from legacy: This validates the managed-only post-stall drag extension rather than a legacy solver path. Decision: Keep the managed test because the extrapolator is a deliberate addition.
    public void ExtrapolatePostStall_HighAlpha_CDPositiveAndLarger()
    {
        double alpha = 20.0 * Math.PI / 180.0;
        double lastAlpha = 12.0 * Math.PI / 180.0;
        double lastCL = 1.2;
        double lastCD = 0.02;
        double ar = 2.0 * Math.PI;

        var (cl, cd) = PostStallExtrapolator.ExtrapolatePostStall(
            alpha, lastAlpha, lastCL, lastCD, ar);

        Assert.True(cd > 0,
            $"Post-stall CD should be positive, got {cd}");
        Assert.True(cd > lastCD,
            $"Post-stall CD ({cd}) should be larger than last converged CD ({lastCD})");
    }

    /// <summary>
    /// At 90 degrees, CL should be near zero (flat plate broadside has zero lift).
    /// </summary>
    [Fact]
    // Legacy mapping: none.
    // Difference from legacy: Ninety-degree post-stall behavior is specific to the managed extrapolation layer. Decision: Keep the managed-only regression because it defines the extension's expected output.
    public void ExtrapolatePostStall_NinetyDegrees_CLNearZero()
    {
        double alpha = 90.0 * Math.PI / 180.0;
        double lastAlpha = 12.0 * Math.PI / 180.0;
        double lastCL = 1.2;
        double lastCD = 0.02;
        double ar = 2.0 * Math.PI;

        var (cl, cd) = PostStallExtrapolator.ExtrapolatePostStall(
            alpha, lastAlpha, lastCL, lastCD, ar);

        Assert.True(Math.Abs(cl) < 0.3,
            $"CL at 90 degrees should be near zero, got {cl}");
    }

    /// <summary>
    /// CL/CD values should not be NaN or Infinity.
    /// </summary>
    [Fact]
    // Legacy mapping: none.
    // Difference from legacy: Finite-value robustness for the post-stall extrapolator is a managed-only contract. Decision: Keep the managed test because the extension must remain numerically safe.
    public void ExtrapolatePostStall_HighAlpha_NoNaNOrInfinity()
    {
        double alpha = 45.0 * Math.PI / 180.0;
        double lastAlpha = 12.0 * Math.PI / 180.0;
        double lastCL = 1.2;
        double lastCD = 0.02;
        double ar = 2.0 * Math.PI;

        var (cl, cd) = PostStallExtrapolator.ExtrapolatePostStall(
            alpha, lastAlpha, lastCL, lastCD, ar);

        Assert.False(double.IsNaN(cl), "CL should not be NaN");
        Assert.False(double.IsInfinity(cl), "CL should not be Infinity");
        Assert.False(double.IsNaN(cd), "CD should not be NaN");
        Assert.False(double.IsInfinity(cd), "CD should not be Infinity");
    }

    // ================================================================
    // Helper: Build a converged BL state (realistic NACA 0012 at Re=1e6, alpha=0)
    // ================================================================

    /// <summary>
    /// Creates a BL state that mimics a converged NACA 0012 at Re=1e6, alpha=0.
    /// This sets up the arrays with physically plausible theta, delta*, Ue, Cf profiles
    /// so that drag calculation can be tested in isolation.
    /// </summary>
    private static (BoundaryLayerSystemState blState, LinearVortexPanelState panel,
        double qinf, double alfa)
        BuildConvergedBLState()
    {
        double qinf = 1.0;
        double alfa = 0.0;
        double reinf = 1_000_000.0;

        int nSurface = 60; // stations per surface
        int nWake = 10;
        int maxStations = nSurface + nWake + 5;

        var blState = new BoundaryLayerSystemState(maxStations, nWake);

        // Side 0 (upper): nSurface stations, TE at station nSurface-1
        blState.IBLTE[0] = nSurface - 1;
        blState.NBL[0] = nSurface;
        blState.ITRAN[0] = 20; // Transition at station 20 (~x/c=0.35)

        // Side 1 (lower): nSurface surface stations + nWake wake stations
        blState.IBLTE[1] = nSurface - 1;
        blState.NBL[1] = nSurface + nWake;
        blState.ITRAN[1] = 20;

        // Build physically plausible BL profiles for both sides
        for (int side = 0; side < 2; side++)
        {
            int iblte = blState.IBLTE[side];
            int itran = blState.ITRAN[side];

            for (int ibl = 0; ibl < blState.NBL[side]; ibl++)
            {
                bool isWake = (ibl > iblte);
                bool isTurb = (ibl >= itran) || isWake;

                // Arc-length coordinate
                double xsi;
                if (!isWake)
                    xsi = (double)ibl / (double)iblte * 2.0; // ~2.0 chord surface length
                else
                    xsi = 2.0 + 0.02 * (ibl - iblte); // Wake extension

                blState.XSSI[ibl, side] = xsi;

                // Edge velocity (normalized by qinf = 1)
                double xc = (double)ibl / (double)iblte; // x/c-like parameter
                if (isWake)
                {
                    // Wake: Ue recovers toward freestream
                    double ueTE = 0.9;
                    int iw = ibl - iblte;
                    blState.UEDG[ibl, side] = ueTE + 0.01 * iw;
                }
                else
                {
                    // Surface: typical NACA 0012 Ue distribution
                    blState.UEDG[ibl, side] = Math.Max(0.05,
                        1.0 + 0.3 * Math.Sin(Math.PI * xc)); // Peak ~1.3 at mid-chord
                }

                // Momentum thickness
                if (ibl == 0)
                {
                    blState.THET[ibl, side] = 1e-6; // Near stagnation
                }
                else if (!isTurb)
                {
                    // Laminar: theta grows as ~sqrt(x/Re)
                    blState.THET[ibl, side] = 0.664 * Math.Sqrt(xsi / reinf);
                }
                else if (!isWake)
                {
                    // Turbulent: theta grows faster
                    double thetaTran = 0.664 * Math.Sqrt(blState.XSSI[itran, side] / reinf);
                    double dxTurb = xsi - blState.XSSI[itran, side];
                    blState.THET[ibl, side] = thetaTran + 0.036 * Math.Pow(dxTurb, 0.8)
                        / Math.Pow(reinf, 0.2);
                }
                else
                {
                    // Wake: theta approximately constant or slowly growing
                    double thetaTE = blState.THET[iblte, side];
                    int iw = ibl - iblte;
                    blState.THET[ibl, side] = thetaTE * (1.0 + 0.01 * iw);
                }

                // Shape factor and displacement thickness
                double hk;
                if (!isTurb)
                    hk = 2.6; // Laminar Blasius-like
                else if (!isWake)
                    hk = 1.4; // Turbulent
                else
                    hk = 1.0 + 0.4 * Math.Exp(-0.1 * (ibl - iblte));

                blState.DSTR[ibl, side] = hk * blState.THET[ibl, side];

                // Ctau
                if (!isTurb)
                    blState.CTAU[ibl, side] = 0.0;
                else
                    blState.CTAU[ibl, side] = 0.03;

                // Mass defect
                blState.MASS[ibl, side] = blState.DSTR[ibl, side] * blState.UEDG[ibl, side];
            }
        }

        // Build a minimal panel state for geometry info
        int nodeCount = 2 * nSurface;
        var panel = new LinearVortexPanelState(nodeCount + 5);
        panel.Resize(nodeCount);

        // Set panel coordinates to approximate NACA 0012
        for (int i = 0; i < nodeCount; i++)
        {
            double t = (double)i / (nodeCount - 1);
            double xc = 0.5 * (1.0 + Math.Cos(Math.PI * t));
            double yt = 0.6 * (0.2969 * Math.Sqrt(xc) - 0.126 * xc
                - 0.3516 * xc * xc + 0.2843 * xc * xc * xc - 0.1036 * xc * xc * xc * xc);
            panel.X[i] = xc;
            panel.Y[i] = (i < nodeCount / 2) ? yt : -yt;
        }
        panel.Chord = 1.0;

        return (blState, panel, qinf, alfa);
    }
}
