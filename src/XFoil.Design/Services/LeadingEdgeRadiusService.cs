using XFoil.Core.Models;
using XFoil.Design.Models;

namespace XFoil.Design.Services;

public sealed class LeadingEdgeRadiusService
{
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
