using XFoil.Core.Models;

namespace XFoil.Design.Models;

public sealed class GeometryScalingResult
{
    public GeometryScalingResult(
        AirfoilGeometry geometry,
        GeometryScaleOrigin originKind,
        AirfoilPoint originPoint,
        double scaleFactor)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        OriginKind = originKind;
        OriginPoint = originPoint;
        ScaleFactor = scaleFactor;
    }

    public AirfoilGeometry Geometry { get; }

    public GeometryScaleOrigin OriginKind { get; }

    public AirfoilPoint OriginPoint { get; }

    public double ScaleFactor { get; }
}
