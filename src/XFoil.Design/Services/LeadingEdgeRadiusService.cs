using XFoil.Core.Models;
using XFoil.Design.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f :: LERAD
// Secondary legacy source: f_xfoil/src/xgeom.f :: LEFIND/GEOPAR, f_xfoil/src/spline.f :: SCALC/SINVRT
// Role in port: Adjusts the leading-edge radius by scaling local thickness in chord coordinates.
// Differences: The managed implementation preserves the same geometric intent as `LERAD` but expresses it through explicit chord-frame helpers and immutable geometry output.
// Decision: Keep the decomposed managed implementation because it makes the radius edit auditable while preserving the legacy operation’s purpose.
namespace XFoil.Design.Services;

public sealed class LeadingEdgeRadiusService
{
    // Legacy mapping: f_xfoil/src/xgdes.f :: LERAD.
    // Difference from legacy: The managed implementation expresses the leading-edge-radius edit through explicit chord-frame geometry and opposite-surface queries instead of through monolithic buffer-array operations.
    // Decision: Keep the managed refactor because it is clearer and still follows the legacy geometric intent.
    public LeadingEdgeRadiusEditResult ScaleLeadingEdgeRadius(
        AirfoilGeometry geometry,
        double radiusScaleFactor,
        double blendDistanceChordFraction = 1d)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (!double.IsFinite(radiusScaleFactor) || Math.Abs(radiusScaleFactor) < 1e-12d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusScaleFactor), "Radius scale factor must be finite and non-zero.");
        }

        if (!double.IsFinite(blendDistanceChordFraction))
        {
            throw new ArgumentOutOfRangeException(nameof(blendDistanceChordFraction), "Blend distance must be finite.");
        }

        var points = geometry.Points.ToArray();
        var frame = GeometryTransformUtilities.BuildChordFrame(points);
        var clampedBlendDistance = Math.Max(0.001d, blendDistanceChordFraction);
        var thicknessScale = Math.Sqrt(Math.Abs(radiusScaleFactor));
        var transformedChordPoints = points
            .Select(point => GeometryTransformUtilities.ToChordFrame(point, frame))
            .ToArray();
        var upperSurfacePoints = points[..(frame.LeadingEdgeIndex + 1)]
            .Reverse()
            .ToArray();
        var lowerSurfacePoints = points[frame.LeadingEdgeIndex..]
            .ToArray();

        var editedPoints = new AirfoilPoint[points.Length];
        // Legacy block: LERAD thickness rescaling along the airfoil chord.
        // Difference: The managed port computes the local opposite-surface pair explicitly and applies the blend law in chord coordinates instead of mutating the legacy spline work arrays directly.
        // Decision: Keep the explicit loop because it makes the local thickness adjustment understandable and testable.
        for (var index = 0; index < transformedChordPoints.Length; index++)
        {
            var point = transformedChordPoints[index];
            var oppositePoint = index <= frame.LeadingEdgeIndex
                ? GeometryTransformUtilities.FindOppositePointAtSameChordX(lowerSurfacePoints, point.X, frame)
                : GeometryTransformUtilities.FindOppositePointAtSameChordX(upperSurfacePoints, point.X, frame);
            var oppositeY = GeometryTransformUtilities.ToChordFrame(oppositePoint, frame).Y;

            var xOverChord = point.X / frame.ChordLength;
            var argument = Math.Min(xOverChord / clampedBlendDistance, 15d);
            var thicknessFactor = 1d - ((1d - thicknessScale) * Math.Exp(-argument));
            var camberY = 0.5d * (point.Y + oppositeY);
            var adjustedY = camberY + (thicknessFactor * 0.5d * (point.Y - oppositeY));

            editedPoints[index] = GeometryTransformUtilities.FromChordFrame(
                new AirfoilPoint(point.X, adjustedY),
                frame);
        }

        var editedGeometry = new AirfoilGeometry(
            $"{geometry.Name} le-radius {radiusScaleFactor:0.######}",
            editedPoints,
            geometry.Format,
            geometry.DomainParameters);

        return new LeadingEdgeRadiusEditResult(
            editedGeometry,
            GeometryTransformUtilities.EstimateLeadingEdgeRadius(geometry),
            GeometryTransformUtilities.EstimateLeadingEdgeRadius(editedGeometry),
            radiusScaleFactor,
            clampedBlendDistance);
    }
}
