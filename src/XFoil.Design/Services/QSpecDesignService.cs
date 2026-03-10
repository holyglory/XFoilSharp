using XFoil.Core.Models;
using XFoil.Design.Models;
using XFoil.Solver.Models;

namespace XFoil.Design.Services;

public sealed class QSpecDesignService
{
    public QSpecProfile CreateFromInviscidAnalysis(
        string name,
        InviscidAnalysisResult analysis)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        var distances = BuildSampleDistances(analysis.PressureSamples);
        var totalDistance = distances[^1] <= 0d ? 1d : distances[^1];
        var points = new QSpecPoint[analysis.PressureSamples.Count];
        for (var index = 0; index < analysis.PressureSamples.Count; index++)
        {
            var sample = analysis.PressureSamples[index];
            var surfaceCoordinate = distances[index] / totalDistance;
            points[index] = new QSpecPoint(
                index,
                surfaceCoordinate,
                1d - surfaceCoordinate,
                sample.Location,
                sample.TangentialVelocity,
                sample.PressureCoefficient,
                sample.CorrectedPressureCoefficient);
        }

        return new QSpecProfile(name, analysis.AngleOfAttackDegrees, analysis.MachNumber, points);
    }

    public QSpecEditResult Modify(
        QSpecProfile profile,
        IReadOnlyList<AirfoilPoint> controlPoints,
        bool matchEndpointSlope = true)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (controlPoints is null)
        {
            throw new ArgumentNullException(nameof(controlPoints));
        }

        if (controlPoints.Count < 2)
        {
            throw new ArgumentException("At least two control points are required.", nameof(controlPoints));
        }

        var workingControls = controlPoints.ToArray();
        var points = profile.Points.ToArray();
        var modifiedStartIndex = FindClosestIndex(points, workingControls[0].X);
        var modifiedEndIndex = FindClosestIndex(points, workingControls[^1].X);
        if (modifiedStartIndex == modifiedEndIndex)
        {
            throw new InvalidOperationException("The graft endpoints must map to distinct Qspec points.");
        }

        if (modifiedStartIndex > modifiedEndIndex)
        {
            Array.Reverse(workingControls);
            (modifiedStartIndex, modifiedEndIndex) = (modifiedEndIndex, modifiedStartIndex);
        }

        var xValues = points.Select(point => point.SurfaceCoordinate).ToArray();
        var yValues = points.Select(point => point.SpeedRatio).ToArray();

        if (matchEndpointSlope || modifiedStartIndex != 0)
        {
            workingControls[0] = new AirfoilPoint(points[modifiedStartIndex].PlotCoordinate, yValues[modifiedStartIndex]);
        }

        if (matchEndpointSlope || modifiedEndIndex != points.Length - 1)
        {
            workingControls[^1] = new AirfoilPoint(points[modifiedEndIndex].PlotCoordinate, yValues[modifiedEndIndex]);
        }

        var parameterizedControls = workingControls
            .Select(point => new AirfoilPoint(1d - point.X, point.Y))
            .OrderBy(point => point.X)
            .ToArray();

        var startDerivative = matchEndpointSlope && modifiedStartIndex != 0
            ? EstimateDerivative(xValues, yValues, modifiedStartIndex)
            : (double?)null;
        var endDerivative = matchEndpointSlope && modifiedEndIndex != points.Length - 1
            ? EstimateDerivative(xValues, yValues, modifiedEndIndex)
            : (double?)null;

        var spline = new GeometryTransformUtilities.NaturalCubicSpline(
            parameterizedControls.Select(point => point.X).ToArray(),
            parameterizedControls.Select(point => point.Y).ToArray(),
            startDerivative,
            endDerivative);

        var editedSpeedRatio = yValues.ToArray();
        for (var index = modifiedStartIndex; index <= modifiedEndIndex; index++)
        {
            editedSpeedRatio[index] = spline.Evaluate(xValues[index]);
        }

        return new QSpecEditResult(
            RebuildProfile(profile, editedSpeedRatio, "modi"),
            modifiedStartIndex,
            modifiedEndIndex,
            matchEndpointSlope,
            0d);
    }

    public QSpecEditResult Smooth(
        QSpecProfile profile,
        double startPlotCoordinate,
        double endPlotCoordinate,
        bool matchEndpointSlope = true,
        double smoothingLengthFactor = 0.002d)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (!double.IsFinite(smoothingLengthFactor) || smoothingLengthFactor < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothingLengthFactor), "Smoothing length factor must be finite and non-negative.");
        }

        var points = profile.Points.ToArray();
        var plotCoordinates = points.Select(point => point.PlotCoordinate).ToArray();
        var surfaceCoordinates = points.Select(point => point.SurfaceCoordinate).ToArray();
        var values = points.Select(point => point.SpeedRatio).ToArray();
        var modifiedStartIndex = FindClosestIndex(points, startPlotCoordinate);
        var modifiedEndIndex = FindClosestIndex(points, endPlotCoordinate);
        if (modifiedStartIndex > modifiedEndIndex)
        {
            (modifiedStartIndex, modifiedEndIndex) = (modifiedEndIndex, modifiedStartIndex);
        }

        if (modifiedEndIndex - modifiedStartIndex < 2)
        {
            throw new InvalidOperationException("Qspec segment is too short for smoothing.");
        }

        var coordinateRange = Math.Abs(surfaceCoordinates[^1] - surfaceCoordinates[0]);
        var smoothingLength = smoothingLengthFactor * coordinateRange;
        var smoothingSquared = smoothingLength * smoothingLength;
        var lower = new double[values.Length];
        var diagonal = new double[values.Length];
        var upper = new double[values.Length];
        var rightHandSide = values.ToArray();

        for (var index = modifiedStartIndex + 1; index < modifiedEndIndex; index++)
        {
            var dsm = surfaceCoordinates[index] - surfaceCoordinates[index - 1];
            var dsp = surfaceCoordinates[index + 1] - surfaceCoordinates[index];
            var dso = 0.5d * (surfaceCoordinates[index + 1] - surfaceCoordinates[index - 1]);
            lower[index] = smoothingSquared * (-1d / dsm) / dso;
            diagonal[index] = smoothingSquared * ((1d / dsp) + (1d / dsm)) / dso + 1d;
            upper[index] = smoothingSquared * (-1d / dsp) / dso;
        }

        diagonal[modifiedStartIndex] = 1d;
        upper[modifiedStartIndex] = 0d;
        lower[modifiedEndIndex] = 0d;
        diagonal[modifiedEndIndex] = 1d;

        if (matchEndpointSlope)
        {
            ApplySlopeConstraint(surfaceCoordinates, values, lower, diagonal, upper, rightHandSide, modifiedStartIndex + 1, true);
            ApplySlopeConstraint(surfaceCoordinates, values, lower, diagonal, upper, rightHandSide, modifiedEndIndex - 1, false);
        }

        SolveTriDiagonal(lower, diagonal, upper, rightHandSide, modifiedStartIndex, modifiedEndIndex);

        return new QSpecEditResult(
            RebuildProfile(profile, rightHandSide, "smoo"),
            modifiedStartIndex,
            modifiedEndIndex,
            matchEndpointSlope,
            smoothingLength);
    }

    public QSpecProfile ForceSymmetry(QSpecProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var points = profile.Points.ToArray();
        var symmetricSpeedRatio = points.Select(point => point.SpeedRatio).ToArray();
        for (var index = 0; index < points.Length; index++)
        {
            var mirrorIndex = points.Length - index - 1;
            if (mirrorIndex < index)
            {
                break;
            }

            var symmetricValue = 0.5d * (points[index].SpeedRatio - points[mirrorIndex].SpeedRatio);
            symmetricSpeedRatio[index] = symmetricValue;
            symmetricSpeedRatio[mirrorIndex] = -symmetricValue;
        }

        return RebuildProfile(profile, symmetricSpeedRatio, "symm");
    }

    public QSpecExecutionResult ExecuteInverse(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        QSpecProfile targetProfile,
        double maxDisplacementFraction = 0.02d,
        int smoothingPasses = 2)
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

        if (baselineProfile.Points.Count != targetProfile.Points.Count)
        {
            throw new InvalidOperationException("Baseline and target Qspec profiles must contain the same number of points.");
        }

        if (!double.IsFinite(maxDisplacementFraction) || maxDisplacementFraction < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDisplacementFraction), "Maximum displacement fraction must be finite and non-negative.");
        }

        if (smoothingPasses < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothingPasses), "Smoothing passes must be non-negative.");
        }

        var chordFrame = GeometryTransformUtilities.BuildChordFrame(geometry.Points);
        var pointCount = baselineProfile.Points.Count;
        var displacementLimit = maxDisplacementFraction * chordFrame.ChordLength;
        var speedRatioDelta = new double[pointCount];
        var maxSpeedRatioDelta = 0d;
        for (var index = 0; index < pointCount; index++)
        {
            speedRatioDelta[index] = targetProfile.Points[index].SpeedRatio - baselineProfile.Points[index].SpeedRatio;
            maxSpeedRatioDelta = Math.Max(maxSpeedRatioDelta, Math.Abs(speedRatioDelta[index]));
        }

        if (maxSpeedRatioDelta <= 1e-12d || displacementLimit <= 0d)
        {
            return new QSpecExecutionResult(geometry, 0d, 0d, 0d);
        }

        var normalDisplacements = new double[pointCount];
        for (var index = 0; index < pointCount; index++)
        {
            normalDisplacements[index] = displacementLimit * (speedRatioDelta[index] / maxSpeedRatioDelta);
        }

        normalDisplacements[0] = 0d;
        normalDisplacements[^1] = 0d;
        SmoothDisplacements(normalDisplacements, smoothingPasses);

        var centroid = ComputeCentroid(baselineProfile.Points.Select(point => point.Location).ToArray());
        var displacedPoints = new AirfoilPoint[pointCount];
        var sumSquares = 0d;
        var maxNormalDisplacement = 0d;
        for (var index = 0; index < pointCount; index++)
        {
            var normal = ComputeOutwardNormal(baselineProfile.Points, centroid, index);
            var displacement = normalDisplacements[index];
            displacedPoints[index] = new AirfoilPoint(
                baselineProfile.Points[index].Location.X + (normal.X * displacement),
                baselineProfile.Points[index].Location.Y + (normal.Y * displacement));
            sumSquares += displacement * displacement;
            maxNormalDisplacement = Math.Max(maxNormalDisplacement, Math.Abs(displacement));
        }

        var displacedGeometry = new AirfoilGeometry(
            $"{geometry.Name} qexec",
            displacedPoints,
            geometry.Format,
            geometry.DomainParameters);

        return new QSpecExecutionResult(
            displacedGeometry,
            maxNormalDisplacement,
            Math.Sqrt(sumSquares / pointCount),
            maxSpeedRatioDelta);
    }

    private static void ApplySlopeConstraint(
        double[] coordinates,
        double[] values,
        double[] lower,
        double[] diagonal,
        double[] upper,
        double[] rightHandSide,
        int index,
        bool isStart)
    {
        var dsm = coordinates[index] - coordinates[index - 1];
        var dsp = coordinates[index + 1] - coordinates[index];
        var ds = coordinates[index + 1] - coordinates[index - 1];

        if (isStart)
        {
            lower[index] = -1d / dsm - (dsm / ds) / dsm;
            diagonal[index] = 1d / dsm + (dsm / ds) / dsm + (dsm / ds) / dsp;
            upper[index] = -(dsm / ds) / dsp;
        }
        else
        {
            lower[index] = (dsp / ds) / dsm;
            diagonal[index] = -1d / dsp - (dsp / ds) / dsp - (dsp / ds) / dsm;
            upper[index] = 1d / dsp + (dsp / ds) / dsp;
        }

        rightHandSide[index] =
            (lower[index] * values[index - 1])
            + (diagonal[index] * values[index])
            + (upper[index] * values[index + 1]);
    }

    private static void SolveTriDiagonal(
        double[] lower,
        double[] diagonal,
        double[] upper,
        double[] rightHandSide,
        int startIndex,
        int endIndex)
    {
        for (var index = startIndex + 1; index <= endIndex; index++)
        {
            var factor = lower[index] / diagonal[index - 1];
            diagonal[index] -= factor * upper[index - 1];
            rightHandSide[index] -= factor * rightHandSide[index - 1];
        }

        rightHandSide[endIndex] /= diagonal[endIndex];
        for (var index = endIndex - 1; index >= startIndex; index--)
        {
            rightHandSide[index] = (rightHandSide[index] - (upper[index] * rightHandSide[index + 1])) / diagonal[index];
        }
    }

    private static QSpecProfile RebuildProfile(QSpecProfile original, IReadOnlyList<double> editedSpeedRatio, string suffix)
    {
        var points = original.Points
            .Select((point, index) => new QSpecPoint(
                point.Index,
                point.SurfaceCoordinate,
                point.PlotCoordinate,
                point.Location,
                editedSpeedRatio[index],
                point.PressureCoefficient,
                point.CorrectedPressureCoefficient))
            .ToArray();

        return new QSpecProfile(
            $"{original.Name} {suffix}",
            original.AngleOfAttackDegrees,
            original.MachNumber,
            points);
    }

    private static int FindClosestIndex(IReadOnlyList<QSpecPoint> points, double plotCoordinate)
    {
        var bestIndex = 0;
        var bestDistance = Math.Abs(points[0].PlotCoordinate - plotCoordinate);
        for (var index = 1; index < points.Count; index++)
        {
            var distance = Math.Abs(points[index].PlotCoordinate - plotCoordinate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static double EstimateDerivative(double[] xValues, double[] yValues, int index)
    {
        var lowerIndex = Math.Max(0, index - 1);
        var upperIndex = Math.Min(xValues.Length - 1, index + 1);
        if (upperIndex == lowerIndex)
        {
            return 0d;
        }

        return (yValues[upperIndex] - yValues[lowerIndex]) / (xValues[upperIndex] - xValues[lowerIndex]);
    }

    private static double[] BuildSampleDistances(IReadOnlyList<PressureCoefficientSample> samples)
    {
        var distances = new double[samples.Count];
        for (var index = 1; index < samples.Count; index++)
        {
            var dx = samples[index].Location.X - samples[index - 1].Location.X;
            var dy = samples[index].Location.Y - samples[index - 1].Location.Y;
            distances[index] = distances[index - 1] + Math.Sqrt((dx * dx) + (dy * dy));
        }

        return distances;
    }

    private static void SmoothDisplacements(double[] displacements, int smoothingPasses)
    {
        if (displacements.Length <= 2)
        {
            return;
        }

        for (var pass = 0; pass < smoothingPasses; pass++)
        {
            var original = displacements.ToArray();
            for (var index = 1; index < displacements.Length - 1; index++)
            {
                displacements[index] =
                    (0.25d * original[index - 1])
                    + (0.5d * original[index])
                    + (0.25d * original[index + 1]);
            }

            displacements[0] = 0d;
            displacements[^1] = 0d;
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
