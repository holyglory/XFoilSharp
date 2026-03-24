using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: none
// Role in port: Managed result object for geometry-scaling operations.
// Differences: Legacy XFoil does not return a dedicated scaling result; any equivalent transform would be applied directly to geometry arrays.
// Decision: Keep the managed result object because it makes the scaling service output explicit.
namespace XFoil.Design.Models;

public sealed class GeometryScalingResult
{
    // Legacy mapping: none; this constructor packages a managed geometry-scaling operation.
    // Difference from legacy: The managed port records origin semantics and scale factor explicitly rather than relying on procedural state.
    // Decision: Keep the result object because it is the right service-level contract.
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
