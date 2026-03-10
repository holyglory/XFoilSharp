namespace XFoil.Core.Models;

public sealed class AirfoilGeometry
{
    public AirfoilGeometry(
        string name,
        IReadOnlyList<AirfoilPoint> points,
        AirfoilFormat format,
        IReadOnlyList<double>? domainParameters = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An airfoil name is required.", nameof(name));
        }

        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        if (points.Count < 3)
        {
            throw new ArgumentException("At least three points are required to describe an airfoil.", nameof(points));
        }

        Name = name;
        Points = points.ToArray();
        Format = format;
        DomainParameters = domainParameters?.ToArray() ?? Array.Empty<double>();
    }

    public string Name { get; }

    public IReadOnlyList<AirfoilPoint> Points { get; }

    public AirfoilFormat Format { get; }

    public IReadOnlyList<double> DomainParameters { get; }
}
