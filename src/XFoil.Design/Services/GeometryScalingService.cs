using XFoil.Core.Models;
using XFoil.Design.Models;

// Legacy audit:
// Primary legacy source: none
// Secondary legacy source: f_xfoil/src/xgdes.f :: geometry-transform workflow
// Role in port: Provides a managed geometry-scaling helper with explicit origin semantics.
// Differences: Legacy XFoil does not expose this exact scaling operation as a dedicated immutable service call; the port generalizes the transform for programmatic use.
// Decision: Keep the managed scaling helper because it is an intentional API improvement and not a parity-sensitive legacy replay path.
namespace XFoil.Design.Services;

public sealed class GeometryScalingService
{
    // Legacy mapping: none; this is a managed-only scaling convenience built on the shared chord-frame utilities.
    // Difference from legacy: The port exposes origin selection and immutable output directly instead of routing through an interactive geometry-edit command.
    // Decision: Keep the service method because it is a useful managed extension of the design toolset.
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
