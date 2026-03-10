using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class QSpecPoint
{
    public QSpecPoint(
        int index,
        double surfaceCoordinate,
        double plotCoordinate,
        AirfoilPoint location,
        double speedRatio,
        double pressureCoefficient,
        double correctedPressureCoefficient)
    {
        Index = index;
        SurfaceCoordinate = surfaceCoordinate;
        PlotCoordinate = plotCoordinate;
        Location = location;
        SpeedRatio = speedRatio;
        PressureCoefficient = pressureCoefficient;
        CorrectedPressureCoefficient = correctedPressureCoefficient;
    }

    public int Index { get; }

    public double SurfaceCoordinate { get; }

    public double PlotCoordinate { get; }

    public AirfoilPoint Location { get; }

    public double SpeedRatio { get; }

    public double PressureCoefficient { get; }

    public double CorrectedPressureCoefficient { get; }
}
