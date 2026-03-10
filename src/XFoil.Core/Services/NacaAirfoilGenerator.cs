using System.Globalization;
using XFoil.Core.Models;

namespace XFoil.Core.Services;

public sealed class NacaAirfoilGenerator
{
    public AirfoilGeometry Generate4Digit(string designation, int pointCount = 161)
    {
        if (string.IsNullOrWhiteSpace(designation))
        {
            throw new ArgumentException("A NACA designation is required.", nameof(designation));
        }

        if (designation.Length != 4 || !designation.All(char.IsDigit))
        {
            throw new ArgumentException("Only NACA 4-digit airfoils are supported in the initial managed port.", nameof(designation));
        }

        if (pointCount < 21 || pointCount % 2 == 0)
        {
            throw new ArgumentException("Point count must be an odd number greater than or equal to 21.", nameof(pointCount));
        }

        var maxCamber = (designation[0] - '0') / 100.0;
        var maxCamberPosition = (designation[1] - '0') / 10.0;
        var thickness = int.Parse(designation[2..], CultureInfo.InvariantCulture) / 100.0;
        var surfacePointCount = (pointCount + 1) / 2;

        var upper = new List<AirfoilPoint>(surfacePointCount);
        var lower = new List<AirfoilPoint>(surfacePointCount);

        for (var index = 0; index < surfacePointCount; index++)
        {
            var beta = Math.PI * index / (surfacePointCount - 1);
            var x = 0.5 * (1.0 - Math.Cos(beta));

            var yt = 5.0 * thickness *
                     (0.2969 * Math.Sqrt(x)
                      - 0.1260 * x
                      - 0.3516 * x * x
                      + 0.2843 * x * x * x
                      - 0.1036 * x * x * x * x);

            var (yc, dycDx) = MeanCamber(maxCamber, maxCamberPosition, x);
            var theta = Math.Atan(dycDx);

            upper.Add(new AirfoilPoint(
                x - (yt * Math.Sin(theta)),
                yc + (yt * Math.Cos(theta))));

            lower.Add(new AirfoilPoint(
                x + (yt * Math.Sin(theta)),
                yc - (yt * Math.Cos(theta))));
        }

        var points = upper
            .AsEnumerable()
            .Reverse()
            .Concat(lower.Skip(1))
            .ToArray();

        return new AirfoilGeometry($"NACA {designation}", points, AirfoilFormat.PlainCoordinates);
    }

    private static (double Camber, double Slope) MeanCamber(double m, double p, double x)
    {
        if (m <= 0 || p <= 0)
        {
            return (0d, 0d);
        }

        if (x < p)
        {
            var camber = (m / (p * p)) * ((2 * p * x) - (x * x));
            var slope = (2 * m / (p * p)) * (p - x);
            return (camber, slope);
        }

        var denominator = Math.Pow(1 - p, 2);
        var aftCamber = (m / denominator) * ((1 - (2 * p)) + (2 * p * x) - (x * x));
        var aftSlope = (2 * m / denominator) * (p - x);
        return (aftCamber, aftSlope);
    }
}
