using System;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: XYWAKE wake-gap profile block
// Secondary legacy source: f_xfoil/src/XFOIL.INC :: WGAP wake-thickness conventions
// Role in port: Encapsulates the dead-air wake-gap profile used when building managed wake seeds.
// Differences: The cubic profile is directly derived from the legacy WGAP formula, but the port isolates it into a tiny reusable helper instead of leaving it embedded inside XYWAKE/SETBL bookkeeping.
// Decision: Keep the isolated helper because it makes the wake-gap law reusable while preserving the same legacy cubic when the parity path needs it.
using XFoil.Solver.Services;
namespace XFoil.Solver.Double.Services;

internal static class WakeGapProfile
{
    internal const double TrailingEdgeGapRatio = 2.5d;

    // Legacy mapping: f_xfoil/src/xpanel.f :: XYWAKE wake-gap derivative setup.
    // Difference from legacy: The managed port exposes the tangent-to-slope conversion as its own helper instead of keeping it inline inside the wake geometry assembly.
    // Decision: Keep the helper because it makes the wake-gap construction easier to reuse and audit.
    internal static double ComputeDerivativeFromTangentY(double tangentY)
    {
        double clampedY = Math.Clamp(tangentY, -0.999999d, 0.999999d);
        return clampedY / Math.Sqrt(Math.Max(1e-12d, 1d - (clampedY * clampedY)));
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: XYWAKE / WGAP cubic dead-air profile.
    // Difference from legacy: The polynomial is materially the same, but the managed code treats the sharp-TE and zero-gap exits as explicit guards rather than relying on surrounding wake-loop state.
    // Decision: Keep the explicit guards and preserve the legacy cubic profile for parity-sensitive wake seeding.
    internal static double Evaluate(
        double normalGap,
        double distanceFromTrailingEdge,
        double wakeGapDerivative,
        bool sharpTrailingEdge)
    {
        if (sharpTrailingEdge || normalGap <= 1e-9d)
        {
            return 0d;
        }

        // Fortran xpanel.f:2492-2506 — XYWAKE computes the wake-gap cubic in REAL*4.
        // The double intermediates are intentional: matching with double drifts 1 ULP
        // per wake station and shifts WGAP(IW) vs Fortran. The double tree (auto-
        // generated *.Double.cs twin via gen-double.py) replaces these floats with
        // doubles for the algorithmic-parity (modern) precision path.
        double anteF = (double)normalGap;
        double telratF = (double)TrailingEdgeGapRatio;
        double dwdxF = (double)wakeGapDerivative;
        double aaF = 3d + (telratF * dwdxF);
        double bbF = -2d - (telratF * dwdxF);
        double telrAnteF = telratF * anteF;
        double distF = (double)distanceFromTrailingEdge;
        double znF = 1d - (distF / telrAnteF);
        if (znF < 0d)
        {
            return 0d;
        }
        // Fortran: ANTE * (AA + BB*ZN) * ZN**2 — ZN**2 = ZN*ZN; expression parses as
        // ANTE * ((AA+BB*ZN) * ZN**2), NOT left-to-right ((X*ZN)*ZN). Compute znSquared
        // first to match Fortran's grouping.
        double znSqF = znF * znF;
        return anteF * (aaF + (bbF * znF)) * znSqF;
    }
}
