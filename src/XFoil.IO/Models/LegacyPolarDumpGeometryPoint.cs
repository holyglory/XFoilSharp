namespace XFoil.IO.Models;

public sealed class LegacyPolarDumpGeometryPoint
{
    public LegacyPolarDumpGeometryPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}
