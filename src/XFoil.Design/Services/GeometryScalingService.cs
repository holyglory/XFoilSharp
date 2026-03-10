using XFoil.Core.Models;
using XFoil.Design.Models;

namespace XFoil.Design.Services;

public sealed class GeometryScalingService
{
    public GeometryScalingResult Scale(
        AirfoilGeometry geometry,
        double scaleFactor,
        GeometryScaleOrigin originKind,
        AirfoilPoint? originPoint = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (!double.IsFinite(scaleFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Scale factor must be finite.");
        }

        var points = geometry.Points;
        var frame = GeometryTransformUtilities.BuildChordFrame(points);
        var resolvedOrigin = originKind switch
        {
            GeometryScaleOrigin.LeadingEdge => frame.LeadingEdge,
            GeometryScaleOrigin.TrailingEdge => frame.TrailingEdge,
            GeometryScaleOrigin.Point => originPoint ?? throw new ArgumentException("A point origin is required when origin kind is Point.", nameof(originPoint)),
            _ => throw new ArgumentOutOfRangeException(nameof(originKind)),
        };

        var scaledPoints = points
            .Select(point => new AirfoilPoint(
                resolvedOrigin.X + (scaleFactor * (point.X - resolvedOrigin.X)),
                resolvedOrigin.Y + (scaleFactor * (point.Y - resolvedOrigin.Y))))
            .ToArray();

        var resultGeometry = new AirfoilGeometry(
            $"{geometry.Name} scaled {scaleFactor:0.######}",
            scaledPoints,
            geometry.Format,
            geometry.DomainParameters);

        return new GeometryScalingResult(resultGeometry, originKind, resolvedOrigin, scaleFactor);
    }
}
