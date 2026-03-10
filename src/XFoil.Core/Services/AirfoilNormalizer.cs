using XFoil.Core.Models;

namespace XFoil.Core.Services;

public sealed class AirfoilNormalizer
{
    public AirfoilGeometry Normalize(AirfoilGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var points = geometry.Points;
        var trailingEdge = new AirfoilPoint(
            0.5 * (points[0].X + points[^1].X),
            0.5 * (points[0].Y + points[^1].Y));

        var leadingEdge = points
            .OrderBy(point => point.X)
            .ThenBy(point => Math.Abs(point.Y))
            .First();

        var dx = trailingEdge.X - leadingEdge.X;
        var dy = trailingEdge.Y - leadingEdge.Y;
        var chord = Math.Sqrt((dx * dx) + (dy * dy));
        if (chord <= double.Epsilon)
        {
            throw new InvalidOperationException("The airfoil chord length is zero and cannot be normalized.");
        }

        var angle = Math.Atan2(dy, dx);
        var cosine = Math.Cos(-angle);
        var sine = Math.Sin(-angle);

        var normalized = points
            .Select(point =>
            {
                var translatedX = point.X - leadingEdge.X;
                var translatedY = point.Y - leadingEdge.Y;
                var rotatedX = (translatedX * cosine) - (translatedY * sine);
                var rotatedY = (translatedX * sine) + (translatedY * cosine);
                return new AirfoilPoint(rotatedX / chord, rotatedY / chord);
            })
            .ToArray();

        return new AirfoilGeometry(
            geometry.Name,
            normalized,
            geometry.Format,
            geometry.DomainParameters);
    }
}
