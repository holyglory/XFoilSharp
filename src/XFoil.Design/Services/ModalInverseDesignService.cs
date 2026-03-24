using XFoil.Core.Models;
using XFoil.Design.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xmdes.f :: MDES/PERT
// Secondary legacy source: f_xfoil/src/xmdes.f :: CNCALC, f_xfoil/src/xqdes.f :: QDES
// Role in port: Provides a managed modal inverse-design workflow built on Qspec data.
// Differences: The managed implementation replaces the legacy conformal-map modal system with a simplified sine-basis spectrum and direct normal-displacement reconstruction.
// Decision: Keep the managed improvement because it is an intentional higher-level design API rather than a parity target for the legacy interactive MDES session.
namespace XFoil.Design.Services;

public sealed class ModalInverseDesignService
{
    // Legacy mapping: f_xfoil/src/xmdes.f :: MDES coefficient-view lineage.
    // Difference from legacy: The managed port computes a sine-basis spectrum directly from Qspec values instead of exposing the legacy `Cn` conformal-map coefficients.
    // Decision: Keep the managed improvement because the modal API is intentionally simpler for library use.
    public ModalSpectrum CreateSpectrum(
        string name,
        QSpecProfile profile,
        int modeCount,
        double filterStrength = 0d)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var values = profile.Points.Select(point => point.SpeedRatio).ToArray();
        return BuildSpectrum(name, profile.Points, values, modeCount, filterStrength);
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: MDES.
    // Difference from legacy: The managed implementation derives the modal target from direct Qspec differences and then executes a simplified geometry reconstruction instead of running the legacy conformal inverse design session.
    // Decision: Keep the managed improvement because the service intentionally exposes a lighter-weight modal workflow.
    public ModalInverseExecutionResult Execute(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        QSpecProfile targetProfile,
        int modeCount = 12,
        double filterStrength = 0.15d,
        double maxDisplacementFraction = 0.02d)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (baselineProfile is null)
        {
            throw new ArgumentNullException(nameof(baselineProfile));
        }

        if (targetProfile is null)
        {
            throw new ArgumentNullException(nameof(targetProfile));
        }

        EnsureCompatibleProfiles(baselineProfile, targetProfile);

        var deltaValues = targetProfile.Points
            .Select((point, index) => point.SpeedRatio - baselineProfile.Points[index].SpeedRatio)
            .ToArray();
        var spectrum = BuildSpectrum($"{geometry.Name} mdes", baselineProfile.Points, deltaValues, modeCount, filterStrength);
        return ExecuteFromSpectrum(geometry, baselineProfile, spectrum, maxDisplacementFraction);
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: PERT.
    // Difference from legacy: The managed port perturbs one sine mode in the simplified modal basis instead of perturbing one conformal-map coefficient in the legacy `Cn` system.
    // Decision: Keep the managed improvement because this API is deliberately basis-agnostic to the old interactive implementation.
    public ModalInverseExecutionResult PerturbMode(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        int modeIndex,
        double coefficientDelta,
        int modeCount = 12,
        double filterStrength = 0.15d,
        double maxDisplacementFraction = 0.02d)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (baselineProfile is null)
        {
            throw new ArgumentNullException(nameof(baselineProfile));
        }

        if (modeIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(modeIndex), "Mode index must be positive.");
        }

        if (modeCount < modeIndex)
        {
            modeCount = modeIndex;
        }

        var coefficients = new ModalCoefficient[modeCount];
        for (var mode = 1; mode <= modeCount; mode++)
        {
            var coefficient = mode == modeIndex ? coefficientDelta : 0d;
            coefficients[mode - 1] = new ModalCoefficient(mode, coefficient, ApplyFilter(mode, coefficient, filterStrength));
        }

        var spectrum = new ModalSpectrum($"{geometry.Name} pert m{modeIndex}", coefficients);
        return ExecuteFromSpectrum(geometry, baselineProfile, spectrum, maxDisplacementFraction);
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: MDES/PERT geometry-update lineage.
    // Difference from legacy: The managed implementation reconstructs a displacement field from the simplified modal spectrum and moves points along estimated normals instead of invoking MAPGEN/CNCALC.
    // Decision: Keep the managed improvement because the design API intentionally favors a direct geometric edit.
    public ModalInverseExecutionResult ExecuteFromSpectrum(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        ModalSpectrum spectrum,
        double maxDisplacementFraction = 0.02d)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (baselineProfile is null)
        {
            throw new ArgumentNullException(nameof(baselineProfile));
        }

        if (spectrum is null)
        {
            throw new ArgumentNullException(nameof(spectrum));
        }

        if (!double.IsFinite(maxDisplacementFraction) || maxDisplacementFraction < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDisplacementFraction), "Maximum displacement fraction must be finite and non-negative.");
        }

        var points = baselineProfile.Points.ToArray();
        var chordFrame = GeometryTransformUtilities.BuildChordFrame(geometry.Points);
        var displacementLimit = maxDisplacementFraction * chordFrame.ChordLength;
        var rawDisplacements = ReconstructField(points, spectrum.Coefficients);
        var maxRaw = rawDisplacements.Length == 0 ? 0d : rawDisplacements.Max(value => Math.Abs(value));
        var scale = maxRaw <= 1e-12d || displacementLimit <= 0d ? 0d : displacementLimit / maxRaw;
        var centroid = ComputeCentroid(points.Select(point => point.Location).ToArray());

        var displacedPoints = new AirfoilPoint[points.Length];
        var maxNormalDisplacement = 0d;
        var sumSquares = 0d;
        // Legacy block: Managed-only geometry reconstruction from the simplified modal field.
        // Difference: The legacy MDES path updates conformal-map coefficients and regenerates geometry, while this managed service displaces the discrete profile directly.
        // Decision: Keep the managed-only formulation because it is the intended library behavior.
        for (var index = 0; index < points.Length; index++)
        {
            var displacement = rawDisplacements[index] * scale;
            var normal = ComputeOutwardNormal(points, centroid, index);
            displacedPoints[index] = new AirfoilPoint(
                points[index].Location.X + (normal.X * displacement),
                points[index].Location.Y + (normal.Y * displacement));
            maxNormalDisplacement = Math.Max(maxNormalDisplacement, Math.Abs(displacement));
            sumSquares += displacement * displacement;
        }

        if (displacedPoints.Length > 0)
        {
            displacedPoints[0] = points[0].Location;
            displacedPoints[^1] = points[^1].Location;
        }

        var displacedGeometry = new AirfoilGeometry(
            $"{geometry.Name} mdes",
            displacedPoints,
            geometry.Format,
            geometry.DomainParameters);

        return new ModalInverseExecutionResult(
            displacedGeometry,
            spectrum,
            maxNormalDisplacement,
            displacedPoints.Length == 0 ? 0d : Math.Sqrt(sumSquares / displacedPoints.Length));
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: MDES spectrum-building lineage.
    // Difference from legacy: The managed code builds a sine-series spectrum rather than the original conformal-map coefficient set.
    // Decision: Keep the managed improvement because the higher-level modal interface is intentional.
    private static ModalSpectrum BuildSpectrum(
        string name,
        IReadOnlyList<QSpecPoint> points,
        IReadOnlyList<double> values,
        int modeCount,
        double filterStrength)
    {
        if (modeCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(modeCount), "Mode count must be positive.");
        }

        var coefficients = new ModalCoefficient[modeCount];
        // Legacy block: MDES-like per-mode coefficient extraction.
        // Difference: The coefficients come from a sine-basis projection instead of from the legacy mapping coefficients.
        // Decision: Keep the managed-only spectrum construction.
        for (var mode = 1; mode <= modeCount; mode++)
        {
            var coefficient = ComputeSineCoefficient(points, values, mode);
            coefficients[mode - 1] = new ModalCoefficient(mode, coefficient, ApplyFilter(mode, coefficient, filterStrength));
        }

        return new ModalSpectrum(name, coefficients);
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: MDES modal projection lineage.
    // Difference from legacy: The managed code projects onto sine modes over surface coordinate rather than onto the conformal-map basis used by the original tool.
    // Decision: Keep the managed-only approximation because it is the chosen modal basis for the .NET API.
    private static double ComputeSineCoefficient(IReadOnlyList<QSpecPoint> points, IReadOnlyList<double> values, int modeIndex)
    {
        var integral = 0d;
        // Legacy block: Managed-only trapezoidal projection of the Qspec field onto one sine mode.
        // Difference: This numerical integral has no exact Fortran twin because the managed service uses a different modal basis.
        // Decision: Keep the managed-only projection helper.
        for (var index = 1; index < points.Count; index++)
        {
            var s0 = points[index - 1].SurfaceCoordinate;
            var s1 = points[index].SurfaceCoordinate;
            var basis0 = Math.Sin(modeIndex * Math.PI * s0);
            var basis1 = Math.Sin(modeIndex * Math.PI * s1);
            integral += 0.5d * ((values[index - 1] * basis0) + (values[index] * basis1)) * (s1 - s0);
        }

        return 2d * integral;
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN filtering lineage.
    // Difference from legacy: The managed service attenuates the simplified modal coefficients with an exponential filter rather than the legacy map-space filter controls.
    // Decision: Keep the managed-only filter because it is tuned to the simplified spectrum.
    private static double ApplyFilter(int modeIndex, double coefficient, double filterStrength)
    {
        if (!double.IsFinite(filterStrength) || filterStrength < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(filterStrength), "Filter strength must be finite and non-negative.");
        }

        var attenuation = Math.Exp(-filterStrength * (modeIndex - 1) * (modeIndex - 1));
        return coefficient * attenuation;
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: PERT/MDES reconstruction lineage.
    // Difference from legacy: The field is reconstructed from the simplified sine coefficients rather than from conformal-map coefficients.
    // Decision: Keep the managed-only reconstruction because it matches the chosen basis.
    private static double[] ReconstructField(IReadOnlyList<QSpecPoint> points, IReadOnlyList<ModalCoefficient> coefficients)
    {
        var values = new double[points.Count];
        // Legacy block: Managed-only modal field synthesis over the Qspec stations.
        // Difference: This sum operates in the simplified sine basis and has no exact one-to-one Fortran counterpart.
        // Decision: Keep the managed-only synthesis loop.
        for (var index = 0; index < points.Count; index++)
        {
            var s = points[index].SurfaceCoordinate;
            var value = 0d;
            foreach (var coefficient in coefficients)
            {
                value += coefficient.FilteredCoefficient * Math.Sin(coefficient.ModeIndex * Math.PI * s);
            }

            values[index] = value;
        }

        if (values.Length > 0)
        {
            values[0] = 0d;
            values[^1] = 0d;
        }

        return values;
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: profile-compatibility checks around MDES/PERT.
    // Difference from legacy: The managed API validates profile compatibility upfront instead of relying on session-state assumptions.
    // Decision: Keep the managed refactor because invalid inputs fail earlier and more clearly.
    private static void EnsureCompatibleProfiles(QSpecProfile baselineProfile, QSpecProfile targetProfile)
    {
        if (baselineProfile.Points.Count != targetProfile.Points.Count)
        {
            throw new InvalidOperationException("Baseline and target Qspec profiles must contain the same number of points.");
        }
    }

    // Legacy mapping: none; managed-only geometry helper for the simplified modal execution path.
    // Difference from legacy: The centroid is used only by the direct point-normal reconstruction.
    // Decision: Keep the managed-only helper.
    private static AirfoilPoint ComputeCentroid(IReadOnlyList<AirfoilPoint> points)
    {
        return new AirfoilPoint(
            points.Average(point => point.X),
            points.Average(point => point.Y));
    }

    // Legacy mapping: none; managed-only normal estimator for ExecuteFromSpectrum.
    // Difference from legacy: The original MDES path regenerates geometry through conformal mapping rather than through direct discrete normals.
    // Decision: Keep the managed-only helper because it is intrinsic to the simplified modal workflow.
    private static AirfoilPoint ComputeOutwardNormal(IReadOnlyList<QSpecPoint> points, AirfoilPoint centroid, int index)
    {
        var previous = points[Math.Max(0, index - 1)].Location;
        var next = points[Math.Min(points.Count - 1, index + 1)].Location;
        var tangentX = next.X - previous.X;
        var tangentY = next.Y - previous.Y;
        var length = Math.Sqrt((tangentX * tangentX) + (tangentY * tangentY));
        if (length <= 1e-12d)
        {
            return new AirfoilPoint(0d, 0d);
        }

        var normalX = -tangentY / length;
        var normalY = tangentX / length;
        var point = points[index].Location;
        var centroidVectorX = point.X - centroid.X;
        var centroidVectorY = point.Y - centroid.Y;
        if ((normalX * centroidVectorX) + (normalY * centroidVectorY) < 0d)
        {
            normalX = -normalX;
            normalY = -normalY;
        }

        return new AirfoilPoint(normalX, normalY);
    }
}
