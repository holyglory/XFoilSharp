using System;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xpanel.f :: XYWAKE wake-gap profile block
// Secondary legacy source: f_xfoil/src/XFOIL.INC :: WGAP wake-thickness conventions
// Role in port: Encapsulates the dead-air wake-gap profile used when building managed wake seeds.
// Differences: The cubic profile is directly derived from the legacy WGAP formula, but the port isolates it into a tiny reusable helper instead of leaving it embedded inside XYWAKE/SETBL bookkeeping.
// Decision: Keep the isolated helper because it makes the wake-gap law reusable while preserving the same legacy cubic when the parity path needs it.
namespace XFoil.Solver.Services;

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

        double cubicA = 3d + (TrailingEdgeGapRatio * wakeGapDerivative);
        double cubicB = -2d - (TrailingEdgeGapRatio * wakeGapDerivative);
        double normalizedDistance = 1d - (distanceFromTrailingEdge / (TrailingEdgeGapRatio * normalGap));
        if (normalizedDistance < 0d)
        {
            return 0d;
        }

        return normalGap * (cubicA + (cubicB * normalizedDistance)) * normalizedDistance * normalizedDistance;
    }
}
