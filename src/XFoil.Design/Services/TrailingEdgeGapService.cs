using XFoil.Core.Models;
using XFoil.Design.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f :: TGAP
// Secondary legacy source: f_xfoil/src/xgeom.f :: LEFIND/GEOPAR
// Role in port: Adjusts the trailing-edge gap by displacing the upper and lower surfaces along a resolved gap direction.
// Differences: The managed implementation keeps the same TGAP intent but exposes the operation as a direct immutable service call with explicit helpers for the blend and direction logic.
// Decision: Keep the managed refactor because it preserves the legacy geometry edit purpose while fitting the .NET service layer.
namespace XFoil.Design.Services;

public sealed class TrailingEdgeGapService
{
    // Legacy mapping: f_xfoil/src/xgdes.f :: TGAP.
    // Difference from legacy: The managed implementation computes the chord frame and applies the gap displacement directly to copied points instead of mutating buffer geometry arrays in the GDES command path.
    // Decision: Keep the managed refactor because it preserves the TGAP behavior while making the operation reusable and testable.
    public TrailingEdgeGapEditResult SetTrailingEdgeGap(
        AirfoilGeometry geometry,
        double targetGap,
        double blendDistanceChordFraction = 1d)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (!double.IsFinite(targetGap) || targetGap < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(targetGap), "Target gap must be finite and non-negative.");
        }

        if (!double.IsFinite(blendDistanceChordFraction))
        {
            throw new ArgumentOutOfRangeException(nameof(blendDistanceChordFraction), "Blend distance must be finite.");
        }

        var points = geometry.Points;
        if (points.Count < 4)
        {
            throw new ArgumentException("At least four points are required to adjust the trailing-edge gap.", nameof(geometry));
        }

        var leadingEdgeIndex = FindLeadingEdgeIndex(points);
        var leadingEdge = points[leadingEdgeIndex];
        var trailingEdge = new AirfoilPoint(
            0.5d * (points[0].X + points[^1].X),
            0.5d * (points[0].Y + points[^1].Y));
        var chordVectorX = trailingEdge.X - leadingEdge.X;
        var chordVectorY = trailingEdge.Y - leadingEdge.Y;
        var chordLengthSquared = (chordVectorX * chordVectorX) + (chordVectorY * chordVectorY);
        if (chordLengthSquared <= 1e-16d)
        {
            throw new InvalidOperationException("Airfoil chord is degenerate.");
        }

        var originalGapVectorX = points[0].X - points[^1].X;
        var originalGapVectorY = points[0].Y - points[^1].Y;
        var originalGap = Math.Sqrt((originalGapVectorX * originalGapVectorX) + (originalGapVectorY * originalGapVectorY));

        var (gapDirectionX, gapDirectionY) = ResolveGapDirection(points, originalGapVectorX, originalGapVectorY, originalGap);
        var clampedBlendDistance = Math.Clamp(blendDistanceChordFraction, 0d, 1d);
        var deltaGap = targetGap - originalGap;
        var transformedPoints = new AirfoilPoint[points.Count];

        // Legacy block: TGAP per-point displacement sweep.
        // Difference: The managed port evaluates the blend factor and upper/lower direction explicitly for each point instead of using the monolithic GDES workspace update.
        // Decision: Keep the explicit sweep because it exposes the actual geometry change clearly.
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var xOverChord =
                (((point.X - leadingEdge.X) * chordVectorX)
                + ((point.Y - leadingEdge.Y) * chordVectorY))
                / chordLengthSquared;

            double thicknessFactor;
            if (clampedBlendDistance == 0d)
            {
                thicknessFactor = index == 0 || index == points.Count - 1 ? 1d : 0d;
            }
            else
            {
                var argument = Math.Min((1d - xOverChord) * ((1d / clampedBlendDistance) - 1d), 15d);
                thicknessFactor = Math.Exp(-argument);
            }

            var displacement = 0.5d * deltaGap * xOverChord * thicknessFactor;
            var direction = index <= leadingEdgeIndex ? 1d : -1d;
            transformedPoints[index] = new AirfoilPoint(
                point.X + (direction * displacement * gapDirectionX),
                point.Y + (direction * displacement * gapDirectionY));
        }

        var editedGeometry = new AirfoilGeometry(
            $"{geometry.Name} te-gap {targetGap:0.######}",
            transformedPoints,
            geometry.Format,
            geometry.DomainParameters);

        var finalGap = ComputeEndpointGap(transformedPoints);
        return new TrailingEdgeGapEditResult(
            editedGeometry,
            originalGap,
            finalGap,
            targetGap,
            clampedBlendDistance);
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: XLFIND/LEFIND first-guess intent.
    // Difference from legacy: This helper uses a discrete minimum-x scan rather than a spline-refined leading-edge solve.
    // Decision: Keep the simpler scan because this service operates directly on supplied points.
    private static int FindLeadingEdgeIndex(IReadOnlyList<AirfoilPoint> points)
    {
        var bestIndex = 0;
        var bestX = points[0].X;

        for (var index = 1; index < points.Count; index++)
        {
            if (points[index].X < bestX)
            {
                bestIndex = index;
                bestX = points[index].X;
            }
        }

        return bestIndex;
    }

    // Legacy mapping: none; this is a managed helper for resolving the displacement direction when the existing TE gap is zero.
    // Difference from legacy: The port falls back to local tangent geometry explicitly instead of relying on implicit workspace state.
    // Decision: Keep the helper because it makes the zero-gap case deterministic.
    private static (double X, double Y) ResolveGapDirection(
        IReadOnlyList<AirfoilPoint> points,
        double gapVectorX,
        double gapVectorY,
        double gap)
    {
        if (gap > 1e-12d)
        {
            return (gapVectorX / gap, gapVectorY / gap);
        }

        var firstTangent = Normalize(points[1].X - points[0].X, points[1].Y - points[0].Y);
        var lastTangent = Normalize(points[^1].X - points[^2].X, points[^1].Y - points[^2].Y);
        var directionX = -0.5d * (lastTangent.Y - firstTangent.Y);
        var directionY = 0.5d * (lastTangent.X - firstTangent.X);
        var magnitude = Math.Sqrt((directionX * directionX) + (directionY * directionY));
        if (magnitude > 1e-12d)
        {
            return (directionX / magnitude, directionY / magnitude);
        }

        return (0d, 1d);
    }

    // Legacy mapping: none; this is a managed vector-normalization helper.
    // Difference from legacy: The helper isolates a small geometric primitive that would be inlined in the legacy command code.
    // Decision: Keep the helper because it improves readability.
    private static (double X, double Y) Normalize(double x, double y)
    {
        var magnitude = Math.Sqrt((x * x) + (y * y));
        if (magnitude <= 1e-12d)
        {
            return (0d, 0d);
        }

        return (x / magnitude, y / magnitude);
    }

    // Legacy mapping: f_xfoil/src/xgdes.f :: TGAP endpoint-gap measure.
    // Difference from legacy: The helper computes the final endpoint distance explicitly for the managed result object.
    // Decision: Keep the helper because it supports the immutable summary returned by the service.
    private static double ComputeEndpointGap(IReadOnlyList<AirfoilPoint> points)
    {
        var dx = points[0].X - points[^1].X;
        var dy = points[0].Y - points[^1].Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
