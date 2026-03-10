using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class QSpecExecutionResult
{
    public QSpecExecutionResult(
        AirfoilGeometry geometry,
        double maxNormalDisplacement,
        double rmsNormalDisplacement,
        double maxSpeedRatioDelta)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        MaxNormalDisplacement = maxNormalDisplacement;
        RmsNormalDisplacement = rmsNormalDisplacement;
        MaxSpeedRatioDelta = maxSpeedRatioDelta;
    }

    public AirfoilGeometry Geometry { get; }

    public double MaxNormalDisplacement { get; }

    public double RmsNormalDisplacement { get; }

    public double MaxSpeedRatioDelta { get; }
}
