using XFoil.Core.Models;
using XFoil.Core.Services;

namespace XFoil.Design.Services;

public sealed class BasicGeometryTransformService
{
    private readonly AirfoilNormalizer normalizer = new();

    public AirfoilGeometry RotateDegrees(AirfoilGeometry geometry, double angleDegrees)
    {
        return RotateRadians(geometry, angleDegrees * Math.PI / 180d, $"{geometry.Name} adeg {angleDegrees:0.######}");
    }

    public AirfoilGeometry RotateRadians(AirfoilGeometry geometry, double angleRadians)
    {
        return RotateRadians(geometry, angleRadians, $"{geometry.Name} arad {angleRadians:0.######}");
    }

    public AirfoilGeometry Translate(AirfoilGeometry geometry, double deltaX, double deltaY)
    {
        ValidateGeometry(geometry);
        var translated = geometry.Points
            .Select(point => new AirfoilPoint(point.X + deltaX, point.Y + deltaY))
            .ToArray();
        return new AirfoilGeometry(
            $"{geometry.Name} tran",
            translated,
            geometry.Format,
            geometry.DomainParameters);
    }

    public AirfoilGeometry ScaleAboutOrigin(AirfoilGeometry geometry, double xScaleFactor, double yScaleFactor)
    {
        ValidateGeometry(geometry);

        var scaled = geometry.Points
            .Select(point => new AirfoilPoint(point.X * xScaleFactor, point.Y * yScaleFactor))
            .ToArray();

        if ((xScaleFactor * yScaleFactor) < 0d)
        {
            Array.Reverse(scaled);
        }

        return new AirfoilGeometry(
            $"{geometry.Name} scal",
            scaled,
            geometry.Format,
            geometry.DomainParameters);
    }

    public AirfoilGeometry ScaleYLinearly(
        AirfoilGeometry geometry,
        double xOverChord1,
        double yScaleFactor1,
        double xOverChord2,
        double yScaleFactor2)
    {
        ValidateGeometry(geometry);

        if (Math.Abs(xOverChord1 - xOverChord2) < 1e-8d)
        {
            throw new ArgumentException("The two x/c locations must be different.");
        }

        var frame = GeometryTransformUtilities.BuildChordFrame(geometry.Points);
        var scaled = geometry.Points
            .Select(point =>
            {
                var chordPoint = GeometryTransformUtilities.ToChordFrame(point, frame);
                var fraction1 = (xOverChord2 - (chordPoint.X / frame.ChordLength)) / (xOverChord2 - xOverChord1);
                var fraction2 = ((chordPoint.X / frame.ChordLength) - xOverChord1) / (xOverChord2 - xOverChord1);
                var yScale = (fraction1 * yScaleFactor1) + (fraction2 * yScaleFactor2);
                return new AirfoilPoint(point.X, point.Y * yScale);
            })
            .ToArray();

        return new AirfoilGeometry(
            $"{geometry.Name} lins",
            scaled,
            geometry.Format,
            geometry.DomainParameters);
    }

    public AirfoilGeometry Derotate(AirfoilGeometry geometry)
    {
        ValidateGeometry(geometry);
        var frame = GeometryTransformUtilities.BuildChordFrame(geometry.Points);
        var angleRadians = Math.Atan2(
            frame.TrailingEdge.Y - frame.LeadingEdge.Y,
            frame.TrailingEdge.X - frame.LeadingEdge.X);
        return RotateRadians(geometry, angleRadians, $"{geometry.Name} dero");
    }

    public AirfoilGeometry NormalizeUnitChord(AirfoilGeometry geometry)
    {
        ValidateGeometry(geometry);
        var normalized = normalizer.Normalize(geometry);
        return new AirfoilGeometry(
            $"{geometry.Name} unit",
            normalized.Points,
            geometry.Format,
            geometry.DomainParameters);
    }

    private static AirfoilGeometry RotateRadians(AirfoilGeometry geometry, double angleRadians, string name)
    {
        ValidateGeometry(geometry);

        var sine = Math.Sin(angleRadians);
        var cosine = Math.Cos(angleRadians);
        var rotated = geometry.Points
            .Select(point => new AirfoilPoint(
                (cosine * point.X) + (sine * point.Y),
                (cosine * point.Y) - (sine * point.X)))
            .ToArray();

        return new AirfoilGeometry(
            name,
            rotated,
            geometry.Format,
            geometry.DomainParameters);
    }

    private static void ValidateGeometry(AirfoilGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
    }
}
