using XFoil.Core.Models;
using XFoil.Core.Services;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgdes.f :: GDES transform/edit workflow
// Secondary legacy source: f_xfoil/src/xgeom.f :: NORM/LEFIND, f_xfoil/src/spline.f :: SCALC
// Role in port: Provides managed rigid-body and simple scaling transforms for geometry-design workflows.
// Differences: Legacy XFoil exposes similar transforms through interactive geometry-design commands and shared buffers; this service offers direct immutable geometry transforms through a typed API.
// Decision: Keep the managed transform service because it is a deliberate API improvement; only the unit-chord normalization path reuses the legacy-derived normalization routine.
namespace XFoil.Design.Services;

public sealed class BasicGeometryTransformService
{
    private readonly AirfoilNormalizer normalizer = new();

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES rigid-rotation workflow.
    // Difference from legacy: This is a convenience wrapper that accepts degrees and delegates to the radian implementation.
    // Decision: Keep the wrapper because it is a straightforward managed API improvement.
    public AirfoilGeometry RotateDegrees(AirfoilGeometry geometry, double angleDegrees)
    {
        return RotateRadians(geometry, angleDegrees * Math.PI / 180d, $"{geometry.Name} adeg {angleDegrees:0.######}");
    }

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES rigid-rotation workflow.
    // Difference from legacy: The managed port returns a new immutable geometry object instead of mutating the active buffer geometry.
    // Decision: Keep the immutable transform because it is the right service-level contract.
    public AirfoilGeometry RotateRadians(AirfoilGeometry geometry, double angleRadians)
    {
        return RotateRadians(geometry, angleRadians, $"{geometry.Name} arad {angleRadians:0.######}");
    }

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES translation workflow.
    // Difference from legacy: The managed port applies translation directly to the point set and returns a fresh geometry object.
    // Decision: Keep the direct point transform because it is clearer than a command-driven mutation path.
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

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES transform family.
    // Difference from legacy: This exposes anisotropic scaling about the origin as an explicit API, which is more general than the legacy interactive workflows.
    // Decision: Keep the managed scaling helper because it is an intentional API expansion.
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

    // Legacy mapping: none; this is a managed-only linear thickness-scaling helper.
    // Difference from legacy: Legacy XFoil does not provide this exact x-varying y-scale transform as a standalone operation.
    // Decision: Keep the managed helper because it is a useful modern extension of the geometry toolset.
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
        // Legacy block: managed chord-frame interpolation transform.
        // Difference: The port computes the varying scale factor directly in chord coordinates rather than routing through a legacy command interpreter.
        // Decision: Keep the direct transform because this helper is intentionally managed-only.
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

    // Legacy mapping: f_xfoil/src/xgeom.f :: NORM chord-alignment intent.
    // Difference from legacy: The managed helper computes the current chord angle from a chord frame and then uses the rigid rotation helper to remove it.
    // Decision: Keep the helper because it reuses the managed transform pipeline cleanly.
    public AirfoilGeometry Derotate(AirfoilGeometry geometry)
    {
        ValidateGeometry(geometry);
        var frame = GeometryTransformUtilities.BuildChordFrame(geometry.Points);
        var angleRadians = Math.Atan2(
            frame.TrailingEdge.Y - frame.LeadingEdge.Y,
            frame.TrailingEdge.X - frame.LeadingEdge.X);
        return RotateRadians(geometry, angleRadians, $"{geometry.Name} dero");
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: NORM.
    // Difference from legacy: The service delegates to the dedicated managed normalizer and then only renames the returned geometry.
    // Decision: Keep the delegation because the normalization logic is already centralized and audited separately.
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

    // Legacy mapping: f_xfoil/src/xgdes.f :: GDES rigid-rotation workflow.
    // Difference from legacy: The managed helper applies the rotation directly to immutable point objects rather than mutating active arrays.
    // Decision: Keep the helper because it localizes the core rotation formula for both public rotation entry points.
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

    // Legacy mapping: none; this is a managed guard helper.
    // Difference from legacy: The port throws argument exceptions instead of relying on calling-code discipline.
    // Decision: Keep the explicit guard because it improves service robustness.
    private static void ValidateGeometry(AirfoilGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
    }
}
