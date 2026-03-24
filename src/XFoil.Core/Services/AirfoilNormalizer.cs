using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgeom.f :: NORM
// Secondary legacy source: f_xfoil/src/xgeom.f :: LEFIND, f_xfoil/src/spline.f :: SCALC
// Role in port: Normalizes discrete airfoil coordinates to unit chord in a managed preprocessing step.
// Differences: The managed implementation uses a direct point-cloud rigid transform based on discrete leading/trailing edge estimates, while legacy XFoil normalizes spline geometry and derivative arrays in place.
// Decision: Keep the simpler managed normalization helper because it is sufficient for file preprocessing and does not need a parity-only branch.
namespace XFoil.Core.Services;

public sealed class AirfoilNormalizer
{
    // Legacy mapping: f_xfoil/src/xgeom.f :: NORM.
    // Difference from legacy: This routine estimates the normalization frame from discrete points and rotates/translates the point set directly instead of updating spline state and derived geometry arrays.
    // Decision: Keep the managed simplification because normalization here is infrastructure for imports and tests, not a solver-fidelity path.
    public AirfoilGeometry Normalize(AirfoilGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var points = geometry.Points;
        var trailingEdge = new AirfoilPoint(
            0.5 * (points[0].X + points[^1].X),
            0.5 * (points[0].Y + points[^1].Y));

        var leadingEdge = points
            .OrderBy(point => point.X)
            .ThenBy(point => Math.Abs(point.Y))
            .First();

        var dx = trailingEdge.X - leadingEdge.X;
        var dy = trailingEdge.Y - leadingEdge.Y;
        var chord = Math.Sqrt((dx * dx) + (dy * dy));
        if (chord <= double.Epsilon)
        {
            throw new InvalidOperationException("The airfoil chord length is zero and cannot be normalized.");
        }

        var angle = Math.Atan2(dy, dx);
        var cosine = Math.Cos(-angle);
        var sine = Math.Sin(-angle);

        // Legacy block: NORM-style rigid-body normalization.
        // Difference: Legacy XFoil transforms coordinate and derivative arrays in place after exact leading-edge localization; this port applies the same conceptual transform only to discrete points.
        // Decision: Keep the discrete transform because the .NET preprocessing layer does not carry spline derivatives here.
        var normalized = points
            .Select(point =>
            {
                var translatedX = point.X - leadingEdge.X;
                var translatedY = point.Y - leadingEdge.Y;
                var rotatedX = (translatedX * cosine) - (translatedY * sine);
                var rotatedY = (translatedX * sine) + (translatedY * cosine);
                return new AirfoilPoint(rotatedX / chord, rotatedY / chord);
            })
            .ToArray();

        return new AirfoilGeometry(
            geometry.Name,
            normalized,
            geometry.Format,
            geometry.DomainParameters);
    }
}
