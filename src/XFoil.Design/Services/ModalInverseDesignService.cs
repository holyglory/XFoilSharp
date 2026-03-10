using XFoil.Core.Models;
using XFoil.Design.Models;

namespace XFoil.Design.Services;

public sealed class ModalInverseDesignService
{
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
        for (var mode = 1; mode <= modeCount; mode++)
        {
            var coefficient = ComputeSineCoefficient(points, values, mode);
            coefficients[mode - 1] = new ModalCoefficient(mode, coefficient, ApplyFilter(mode, coefficient, filterStrength));
        }

        return new ModalSpectrum(name, coefficients);
    }

    private static double ComputeSineCoefficient(IReadOnlyList<QSpecPoint> points, IReadOnlyList<double> values, int modeIndex)
    {
        var integral = 0d;
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

    private static double ApplyFilter(int modeIndex, double coefficient, double filterStrength)
    {
        if (!double.IsFinite(filterStrength) || filterStrength < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(filterStrength), "Filter strength must be finite and non-negative.");
        }

        var attenuation = Math.Exp(-filterStrength * (modeIndex - 1) * (modeIndex - 1));
        return coefficient * attenuation;
    }

    private static double[] ReconstructField(IReadOnlyList<QSpecPoint> points, IReadOnlyList<ModalCoefficient> coefficients)
    {
        var values = new double[points.Count];
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

    private static void EnsureCompatibleProfiles(QSpecProfile baselineProfile, QSpecProfile targetProfile)
    {
        if (baselineProfile.Points.Count != targetProfile.Points.Count)
        {
            throw new InvalidOperationException("Baseline and target Qspec profiles must contain the same number of points.");
        }
    }

    private static AirfoilPoint ComputeCentroid(IReadOnlyList<AirfoilPoint> points)
    {
        return new AirfoilPoint(
            points.Average(point => point.X),
            points.Average(point => point.Y));
    }

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
