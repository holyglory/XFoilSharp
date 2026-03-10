using XFoil.Core.Models;
using XFoil.Design.Models;

namespace XFoil.Design.Services;

public sealed class FlapDeflectionService
{
    private const double SurfaceBreakSpacingFraction = 0.33333d;
    private const double MicroSegmentTolerance = 0.2d;

    public FlapDeflectionResult DeflectTrailingEdge(
        AirfoilGeometry geometry,
        AirfoilPoint hingePoint,
        double deflectionDegrees)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (!double.IsFinite(deflectionDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(deflectionDegrees), "Deflection must be finite.");
        }

        if (Math.Abs(deflectionDegrees) < 1e-12d)
        {
            return new FlapDeflectionResult(geometry, hingePoint, deflectionDegrees, 0, 0, 0);
        }

        var rotationRadians = deflectionDegrees * Math.PI / 180d;
        var sourcePoints = geometry.Points.ToArray();
        var frame = GeometryTransformUtilities.BuildChordFrame(sourcePoints);
        var leadingEdgeIndex = frame.LeadingEdgeIndex;
        var upperSurface = sourcePoints[..(leadingEdgeIndex + 1)].Reverse().ToArray();
        var lowerSurface = sourcePoints[leadingEdgeIndex..].ToArray();
        var upperData = SurfaceData.Create(upperSurface, frame);
        var lowerData = SurfaceData.Create(lowerSurface, frame);
        var hingeChord = GeometryTransformUtilities.ToChordFrame(hingePoint, frame);

        var hingeInside = IsInside(sourcePoints, hingePoint);
        var topAtHinge = upperData.SampleByX(hingeChord.X);
        var bottomAtHinge = lowerData.SampleByX(hingeChord.X);

        var topAngle = 0d;
        var bottomAngle = 0d;
        if (hingeInside)
        {
            topAngle = Math.Max(0d, -rotationRadians);
            bottomAngle = Math.Max(0d, rotationRadians);
        }
        else
        {
            var chordX = bottomAtHinge.Point.X - topAtHinge.Point.X;
            var chordY = bottomAtHinge.Point.Y - topAtHinge.Point.Y;
            var midpointX = 0.5d * (bottomAtHinge.Point.X + topAtHinge.Point.X);
            var midpointY = 0.5d * (bottomAtHinge.Point.Y + topAtHinge.Point.Y);
            var cross = chordX * (hingePoint.Y - midpointY) - chordY * (hingePoint.X - midpointX);
            if (cross > 0d)
            {
                topAngle = Math.Max(0d, rotationRadians);
                bottomAngle = Math.Max(0d, rotationRadians);
            }
            else
            {
                topAngle = Math.Max(0d, -rotationRadians);
                bottomAngle = Math.Max(0d, -rotationRadians);
            }
        }

        var editedUpper = EditSurface(upperData, hingePoint, hingeChord.X, rotationRadians, topAngle);
        var editedLower = EditSurface(lowerData, hingePoint, hingeChord.X, rotationRadians, bottomAngle);

        var combinedPoints = editedUpper.Points
            .Reverse<AirfoilPoint>()
            .Concat(editedLower.Points.Skip(1))
            .ToList();
        RemoveMicroSegments(combinedPoints, MicroSegmentTolerance);

        var renamedGeometry = new AirfoilGeometry(
            $"{geometry.Name} flap {deflectionDegrees:+0.###;-0.###;0}deg",
            combinedPoints,
            geometry.Format,
            geometry.DomainParameters);

        return new FlapDeflectionResult(
            renamedGeometry,
            hingePoint,
            deflectionDegrees,
            editedUpper.AffectedPointCount + editedLower.AffectedPointCount,
            Math.Max(0, renamedGeometry.Points.Count - geometry.Points.Count),
            Math.Max(0, geometry.Points.Count - renamedGeometry.Points.Count));
    }

    private static SurfaceEditResult EditSurface(
        SurfaceData surface,
        AirfoilPoint hingePoint,
        double hingeChordX,
        double rotationRadians,
        double closingAngle)
    {
        var hingeSample = surface.SampleByX(hingeChordX);
        var averageSpacing = surface.TotalArcLength / Math.Max(1, surface.Points.Count - 1);
        var (fixedBreak, flapBreak) = SolveSurfaceBreaks(surface, hingePoint, hingeSample, closingAngle);
        var hingeRadius = Distance(hingeSample.Point, hingePoint);

        var result = new List<AirfoilPoint>();
        var beforePoints = surface.Points
            .Where((_, index) => surface.ArcLengths[index] < fixedBreak.ArcLength - 1e-9d)
            .ToArray();
        result.AddRange(beforePoints);

        if (closingAngle <= 1e-12d)
        {
            result.Add(hingeSample.Point);

            var rotatedBreak = RotateAroundHinge(hingeSample.Point, hingePoint, rotationRadians);
            var arcLength = Math.Abs(rotationRadians) * hingeRadius;
            var arcPointCount = Math.Max(1, (int)Math.Floor((1.5d * arcLength / Math.Max(averageSpacing, 1e-6d)) + 1d));
            for (var pointIndex = 1; pointIndex <= arcPointCount; pointIndex++)
            {
                var fraction = pointIndex / (double)(arcPointCount + 1);
                result.Add(RotateAroundHinge(hingeSample.Point, hingePoint, rotationRadians * fraction));
            }

            result.Add(rotatedBreak);
        }
        else
        {
            var fixedCleanup = surface.SampleByArcLength(
                Math.Min(hingeSample.ArcLength, fixedBreak.ArcLength + (SurfaceBreakSpacingFraction * Math.Max(averageSpacing, hingeSample.ArcLength - fixedBreak.ArcLength))));
            if (Distance(fixedCleanup.Point, fixedBreak.Point) > 1e-9d)
            {
                result.Add(fixedCleanup.Point);
            }

            result.Add(fixedBreak.Point);

            var rotatedBreak = RotateAroundHinge(flapBreak.Point, hingePoint, rotationRadians);
            result.Add(rotatedBreak);

            var flapCleanup = surface.SampleByArcLength(
                Math.Max(hingeSample.ArcLength, flapBreak.ArcLength - (SurfaceBreakSpacingFraction * Math.Max(averageSpacing, flapBreak.ArcLength - hingeSample.ArcLength))));
            var rotatedCleanup = RotateAroundHinge(flapCleanup.Point, hingePoint, rotationRadians);
            if (Distance(rotatedCleanup, rotatedBreak) > 1e-9d)
            {
                result.Add(rotatedCleanup);
            }
        }

        var afterPoints = surface.Points
            .Where((_, index) => surface.ArcLengths[index] > flapBreak.ArcLength + 1e-9d)
            .Select(point => RotateAroundHinge(point, hingePoint, rotationRadians))
            .ToArray();
        result.AddRange(afterPoints);

        return new SurfaceEditResult(result, afterPoints.Length + (closingAngle <= 1e-12d ? 1 : 2));
    }

    private static (SurfaceSample FixedBreak, SurfaceSample FlapBreak) SolveSurfaceBreaks(
        SurfaceData surface,
        AirfoilPoint hingePoint,
        SurfaceSample hingeSample,
        double closingAngle)
    {
        if (closingAngle <= 1e-12d)
        {
            var breakPoint = SolvePerpendicularBreak(surface, hingePoint, hingeSample.ArcLength);
            return (breakPoint, breakPoint);
        }

        var hingeRadius = Distance(hingeSample.Point, hingePoint);
        if (hingeRadius <= 1e-12d)
        {
            return (hingeSample, hingeSample);
        }

        var sinHalfAngle = Math.Sin(0.5d * closingAngle);
        var initialOffset = Math.Min(
            0.2d * surface.TotalArcLength,
            Math.Max(1e-5d, sinHalfAngle * hingeRadius));
        var s1 = Math.Max(0d, hingeSample.ArcLength - initialOffset);
        var s2 = Math.Min(surface.TotalArcLength, hingeSample.ArcLength + initialOffset);
        var epsilon = Math.Max(1e-5d, surface.TotalArcLength * 1e-5d);

        for (var iteration = 0; iteration < 12; iteration++)
        {
            var residual = ComputeClosingBreakResiduals(surface, hingePoint, s1, s2, sinHalfAngle);
            if ((Math.Abs(residual.RadiusDifference) + Math.Abs(residual.AngleResidual)) < epsilon)
            {
                break;
            }

            var jacobianStep = Math.Max(1e-4d, surface.TotalArcLength * 1e-4d);

            var residualS1Forward = ComputeClosingBreakResiduals(
                surface,
                hingePoint,
                Math.Clamp(s1 + jacobianStep, 0d, surface.TotalArcLength),
                s2,
                sinHalfAngle);
            var residualS1Backward = ComputeClosingBreakResiduals(
                surface,
                hingePoint,
                Math.Clamp(s1 - jacobianStep, 0d, surface.TotalArcLength),
                s2,
                sinHalfAngle);
            var residualS2Forward = ComputeClosingBreakResiduals(
                surface,
                hingePoint,
                s1,
                Math.Clamp(s2 + jacobianStep, 0d, surface.TotalArcLength),
                sinHalfAngle);
            var residualS2Backward = ComputeClosingBreakResiduals(
                surface,
                hingePoint,
                s1,
                Math.Clamp(s2 - jacobianStep, 0d, surface.TotalArcLength),
                sinHalfAngle);

            var a11 = (residualS1Forward.RadiusDifference - residualS1Backward.RadiusDifference) / (2d * jacobianStep);
            var a12 = (residualS2Forward.RadiusDifference - residualS2Backward.RadiusDifference) / (2d * jacobianStep);
            var a21 = (residualS1Forward.AngleResidual - residualS1Backward.AngleResidual) / (2d * jacobianStep);
            var a22 = (residualS2Forward.AngleResidual - residualS2Backward.AngleResidual) / (2d * jacobianStep);
            var determinant = (a11 * a22) - (a12 * a21);
            if (Math.Abs(determinant) <= 1e-12d)
            {
                break;
            }

            var deltaS1 = ((-residual.RadiusDifference * a22) + (a12 * residual.AngleResidual)) / determinant;
            var deltaS2 = ((a21 * residual.RadiusDifference) - (a11 * residual.AngleResidual)) / determinant;
            var maxStep = 0.05d * surface.TotalArcLength;
            deltaS1 = Math.Clamp(deltaS1, -maxStep, maxStep);
            deltaS2 = Math.Clamp(deltaS2, -maxStep, maxStep);

            s1 = Math.Clamp(s1 + deltaS1, 0d, hingeSample.ArcLength);
            s2 = Math.Clamp(s2 + deltaS2, hingeSample.ArcLength, surface.TotalArcLength);
        }

        var fixedBreak = surface.SampleByArcLength(s1);
        var flapBreak = surface.SampleByArcLength(s2);
        return (fixedBreak, flapBreak);
    }

    private static SurfaceSample SolvePerpendicularBreak(
        SurfaceData surface,
        AirfoilPoint hingePoint,
        double initialArcLength)
    {
        var arcLength = Math.Clamp(initialArcLength, 0d, surface.TotalArcLength);
        var epsilon = Math.Max(1e-5d, surface.TotalArcLength * 1e-5d);

        for (var iteration = 0; iteration < 12; iteration++)
        {
            var sample = surface.SampleByArcLength(arcLength);
            var tangent = surface.EstimateTangent(arcLength);
            var residual =
                ((sample.Point.X - hingePoint.X) * tangent.X)
                + ((sample.Point.Y - hingePoint.Y) * tangent.Y);
            if (Math.Abs(residual) < epsilon)
            {
                return sample;
            }

            var step = Math.Max(1e-4d, surface.TotalArcLength * 1e-4d);
            var forwardSample = surface.SampleByArcLength(Math.Clamp(arcLength + step, 0d, surface.TotalArcLength));
            var forwardTangent = surface.EstimateTangent(Math.Clamp(arcLength + step, 0d, surface.TotalArcLength));
            var backwardSample = surface.SampleByArcLength(Math.Clamp(arcLength - step, 0d, surface.TotalArcLength));
            var backwardTangent = surface.EstimateTangent(Math.Clamp(arcLength - step, 0d, surface.TotalArcLength));
            var forwardResidual =
                ((forwardSample.Point.X - hingePoint.X) * forwardTangent.X)
                + ((forwardSample.Point.Y - hingePoint.Y) * forwardTangent.Y);
            var backwardResidual =
                ((backwardSample.Point.X - hingePoint.X) * backwardTangent.X)
                + ((backwardSample.Point.Y - hingePoint.Y) * backwardTangent.Y);
            var derivative = (forwardResidual - backwardResidual) / (2d * step);
            if (Math.Abs(derivative) <= 1e-12d)
            {
                break;
            }

            var delta = Math.Clamp(-residual / derivative, -0.05d * surface.TotalArcLength, 0.05d * surface.TotalArcLength);
            arcLength = Math.Clamp(arcLength + delta, 0d, surface.TotalArcLength);
        }

        return surface.SampleByArcLength(arcLength);
    }

    private static ClosingBreakResidual ComputeClosingBreakResiduals(
        SurfaceData surface,
        AirfoilPoint hingePoint,
        double fixedBreakArcLength,
        double flapBreakArcLength,
        double sinHalfAngle)
    {
        var fixedBreak = surface.SampleByArcLength(fixedBreakArcLength);
        var flapBreak = surface.SampleByArcLength(flapBreakArcLength);
        var fixedRadius = Distance(fixedBreak.Point, hingePoint);
        var flapRadius = Distance(flapBreak.Point, hingePoint);
        var chordDistance = Distance(fixedBreak.Point, flapBreak.Point);
        return new ClosingBreakResidual(
            fixedRadius - flapRadius,
            chordDistance - ((fixedRadius + flapRadius) * sinHalfAngle));
    }

    private static AirfoilPoint RotateAroundHinge(AirfoilPoint point, AirfoilPoint hingePoint, double rotationRadians)
    {
        var cosine = Math.Cos(rotationRadians);
        var sine = Math.Sin(rotationRadians);
        var xRelative = point.X - hingePoint.X;
        var yRelative = point.Y - hingePoint.Y;
        return new AirfoilPoint(
            hingePoint.X + (xRelative * cosine) + (yRelative * sine),
            hingePoint.Y - (xRelative * sine) + (yRelative * cosine));
    }

    private static bool IsInside(IReadOnlyList<AirfoilPoint> points, AirfoilPoint hingePoint)
    {
        var angleSum = 0d;
        for (var index = 0; index < points.Count; index++)
        {
            var nextIndex = index == points.Count - 1 ? 0 : index + 1;
            var x1 = points[index].X - hingePoint.X;
            var y1 = points[index].Y - hingePoint.Y;
            var x2 = points[nextIndex].X - hingePoint.X;
            var y2 = points[nextIndex].Y - hingePoint.Y;
            var denominator = Math.Sqrt(((x1 * x1) + (y1 * y1)) * ((x2 * x2) + (y2 * y2)));
            if (denominator <= 1e-12d)
            {
                continue;
            }

            angleSum += ((x1 * y2) - (y1 * x2)) / denominator;
        }

        return Math.Abs(angleSum) > 1d;
    }

    private static void RemoveMicroSegments(List<AirfoilPoint> points, double tolerance)
    {
        if (points.Count < 4)
        {
            return;
        }

        var changed = true;
        while (changed && points.Count >= 4)
        {
            changed = false;
            for (var index = 1; index < points.Count - 2; index++)
            {
                var dsm1 = Distance(points[index - 1], points[index]);
                var dsp1 = Distance(points[index], points[index + 1]);
                var dsp2 = Distance(points[index + 1], points[index + 2]);
                if (dsp1 <= 1e-12d)
                {
                    continue;
                }

                if (dsp1 < tolerance * dsm1 || dsp1 < tolerance * dsp2)
                {
                    points[index] = new AirfoilPoint(
                        0.5d * (points[index].X + points[index + 1].X),
                        0.5d * (points[index].Y + points[index + 1].Y));
                    points.RemoveAt(index + 1);
                    changed = true;
                    break;
                }
            }
        }
    }

    private static double Distance(AirfoilPoint first, AirfoilPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private sealed record SurfaceEditResult(IReadOnlyList<AirfoilPoint> Points, int AffectedPointCount);

    private readonly record struct ClosingBreakResidual(double RadiusDifference, double AngleResidual);

    private sealed class SurfaceData
    {
        private readonly NaturalCubicSpline xSpline;
        private readonly NaturalCubicSpline ySpline;

        private SurfaceData(
            IReadOnlyList<AirfoilPoint> points,
            IReadOnlyList<AirfoilPoint> chordPoints,
            IReadOnlyList<double> arcLengths,
            NaturalCubicSpline xSpline,
            NaturalCubicSpline ySpline)
        {
            Points = points;
            ChordPoints = chordPoints;
            ArcLengths = arcLengths;
            this.xSpline = xSpline;
            this.ySpline = ySpline;
            TotalArcLength = arcLengths[^1];
        }

        public IReadOnlyList<AirfoilPoint> Points { get; }

        public IReadOnlyList<AirfoilPoint> ChordPoints { get; }

        public IReadOnlyList<double> ArcLengths { get; }

        public double TotalArcLength { get; }

        public static SurfaceData Create(IReadOnlyList<AirfoilPoint> points, GeometryTransformUtilities.ChordFrame frame)
        {
            var chordPoints = points
                .Select(point => GeometryTransformUtilities.ToChordFrame(point, frame))
                .ToArray();
            var arcLengths = new double[points.Count];
            for (var index = 1; index < points.Count; index++)
            {
                arcLengths[index] = arcLengths[index - 1] + Distance(points[index - 1], points[index]);
            }

            return new SurfaceData(
                points.ToArray(),
                chordPoints,
                arcLengths,
                new NaturalCubicSpline(arcLengths, points.Select(point => point.X).ToArray()),
                new NaturalCubicSpline(arcLengths, points.Select(point => point.Y).ToArray()));
        }

        public SurfaceSample SampleByX(double x)
        {
            if (x <= ChordPoints[0].X)
            {
                return new SurfaceSample(Points[0], ChordPoints[0], ArcLengths[0]);
            }

            if (x >= ChordPoints[^1].X)
            {
                return new SurfaceSample(Points[^1], ChordPoints[^1], ArcLengths[^1]);
            }

            var seedArcLength = ArcLengths[^1];
            for (var index = 1; index < ChordPoints.Count; index++)
            {
                var left = ChordPoints[index - 1];
                var right = ChordPoints[index];
                if (x > right.X)
                {
                    continue;
                }

                var deltaX = right.X - left.X;
                var t = Math.Abs(deltaX) <= 1e-12d ? 0d : (x - left.X) / deltaX;
                seedArcLength = ArcLengths[index - 1] + (t * (ArcLengths[index] - ArcLengths[index - 1]));
                break;
            }

            var arcLength = Math.Clamp(seedArcLength, 0d, TotalArcLength);
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var chordPoint = EvaluateChordPoint(arcLength);
                var derivative = EvaluateChordDerivative(arcLength);
                var residual = chordPoint.X - x;
                if (Math.Abs(residual) <= 1e-10d)
                {
                    return new SurfaceSample(EvaluateWorldPoint(arcLength), chordPoint, arcLength);
                }

                if (Math.Abs(derivative.X) <= 1e-12d)
                {
                    break;
                }

                var delta = Math.Clamp(-residual / derivative.X, -0.05d * TotalArcLength, 0.05d * TotalArcLength);
                arcLength = Math.Clamp(arcLength + delta, 0d, TotalArcLength);
            }

            return new SurfaceSample(EvaluateWorldPoint(arcLength), EvaluateChordPoint(arcLength), arcLength);
        }

        public SurfaceSample SampleByArcLength(double arcLength)
        {
            if (arcLength <= 0d)
            {
                return new SurfaceSample(Points[0], ChordPoints[0], ArcLengths[0]);
            }

            if (arcLength >= TotalArcLength)
            {
                return new SurfaceSample(Points[^1], ChordPoints[^1], ArcLengths[^1]);
            }

            return new SurfaceSample(EvaluateWorldPoint(arcLength), EvaluateChordPoint(arcLength), arcLength);
        }

        public AirfoilPoint EstimateTangent(double arcLength)
        {
            var derivative = EvaluateWorldDerivative(arcLength);
            var dx = derivative.X;
            var dy = derivative.Y;
            var magnitude = Math.Sqrt((dx * dx) + (dy * dy));
            if (magnitude <= 1e-12d)
            {
                return new AirfoilPoint(1d, 0d);
            }

            return new AirfoilPoint(dx / magnitude, dy / magnitude);
        }

        public AirfoilPoint EstimateCurvatureDerivative(double arcLength)
        {
            return new AirfoilPoint(
                EvaluateWorldSecondDerivativeComponent(xSpline, arcLength),
                EvaluateWorldSecondDerivativeComponent(ySpline, arcLength));
        }

        private AirfoilPoint EvaluateWorldPoint(double arcLength)
        {
            return new AirfoilPoint(
                xSpline.Evaluate(arcLength),
                ySpline.Evaluate(arcLength));
        }

        private AirfoilPoint EvaluateWorldDerivative(double arcLength)
        {
            return new AirfoilPoint(
                EvaluateFirstDerivativeComponent(xSpline, arcLength),
                EvaluateFirstDerivativeComponent(ySpline, arcLength));
        }

        private AirfoilPoint EvaluateChordPoint(double arcLength)
        {
            var worldPoint = EvaluateWorldPoint(arcLength);
            var seedIndex = FindNearestIndex(arcLength);
            var chordLeft = ChordPoints[Math.Max(0, seedIndex - 1)];
            var chordRight = ChordPoints[Math.Min(ChordPoints.Count - 1, seedIndex + 1)];
            var worldLeft = Points[Math.Max(0, seedIndex - 1)];
            var worldRight = Points[Math.Min(Points.Count - 1, seedIndex + 1)];
            var dx = worldRight.X - worldLeft.X;
            var dy = worldRight.Y - worldLeft.Y;
            var t = ((Math.Abs(dx) + Math.Abs(dy)) <= 1e-12d)
                ? 0d
                : (((worldPoint.X - worldLeft.X) * dx) + ((worldPoint.Y - worldLeft.Y) * dy))
                    / Math.Max(1e-12d, (dx * dx) + (dy * dy));
            return new AirfoilPoint(
                chordLeft.X + (t * (chordRight.X - chordLeft.X)),
                chordLeft.Y + (t * (chordRight.Y - chordLeft.Y)));
        }

        private AirfoilPoint EvaluateChordDerivative(double arcLength)
        {
            var step = Math.Max(1e-5d, TotalArcLength * 1e-4d);
            var left = EvaluateChordPoint(Math.Max(0d, arcLength - step));
            var right = EvaluateChordPoint(Math.Min(TotalArcLength, arcLength + step));
            return new AirfoilPoint((right.X - left.X) / (2d * step), (right.Y - left.Y) / (2d * step));
        }

        private int FindNearestIndex(double arcLength)
        {
            for (var index = 1; index < ArcLengths.Count; index++)
            {
                if (arcLength <= ArcLengths[index])
                {
                    return index;
                }
            }

            return ArcLengths.Count - 1;
        }

        private static double EvaluateFirstDerivativeComponent(NaturalCubicSpline spline, double parameter)
        {
            var step = Math.Max(1e-6d, (spline.Parameters[^1] - spline.Parameters[0]) * 1e-5d);
            var lower = Math.Max(spline.Parameters[0], parameter - step);
            var upper = Math.Min(spline.Parameters[^1], parameter + step);
            if (upper <= lower)
            {
                return 0d;
            }

            return (spline.Evaluate(upper) - spline.Evaluate(lower)) / (upper - lower);
        }

        private static double EvaluateWorldSecondDerivativeComponent(NaturalCubicSpline spline, double parameter)
        {
            var step = Math.Max(1e-5d, (spline.Parameters[^1] - spline.Parameters[0]) * 1e-4d);
            var lower = Math.Max(spline.Parameters[0], parameter - step);
            var upper = Math.Min(spline.Parameters[^1], parameter + step);
            if (upper <= lower)
            {
                return 0d;
            }

            var center = spline.Evaluate(parameter);
            var left = spline.Evaluate(lower);
            var right = spline.Evaluate(upper);
            var effectiveStep = 0.5d * (upper - lower);
            if (effectiveStep <= 1e-12d)
            {
                return 0d;
            }

            return (right - (2d * center) + left) / (effectiveStep * effectiveStep);
        }
    }

    private readonly record struct SurfaceSample(AirfoilPoint Point, AirfoilPoint ChordPoint, double ArcLength);

    private sealed class NaturalCubicSpline
    {
        private readonly double[] parameters;
        private readonly double[] values;
        private readonly double[] slopes;

        public NaturalCubicSpline(IReadOnlyList<double> parameters, IReadOnlyList<double> values)
        {
            if (parameters.Count != values.Count)
            {
                throw new ArgumentException("Spline arrays must have matching lengths.");
            }

            if (parameters.Count < 2)
            {
                throw new ArgumentException("At least two spline points are required.");
            }

            this.parameters = parameters.ToArray();
            this.values = values.ToArray();
            slopes = ComputeNaturalSlopes(this.parameters, this.values);
        }

        public IReadOnlyList<double> Parameters => parameters;

        public double Evaluate(double parameter)
        {
            if (parameter <= parameters[0])
            {
                return values[0];
            }

            if (parameter >= parameters[^1])
            {
                return values[^1];
            }

            var upperIndex = FindUpperIndex(parameter);
            var lowerIndex = upperIndex - 1;
            var interval = parameters[upperIndex] - parameters[lowerIndex];
            var t = (parameter - parameters[lowerIndex]) / interval;
            var cx1 = (interval * slopes[lowerIndex]) - values[upperIndex] + values[lowerIndex];
            var cx2 = (interval * slopes[upperIndex]) - values[upperIndex] + values[lowerIndex];

            return (t * values[upperIndex])
                 + ((1d - t) * values[lowerIndex])
                 + ((t - (t * t)) * (((1d - t) * cx1) - (t * cx2)));
        }

        private int FindUpperIndex(double parameter)
        {
            var lower = 0;
            var upper = parameters.Length - 1;

            while (upper - lower > 1)
            {
                var middle = (upper + lower) / 2;
                if (parameter < parameters[middle])
                {
                    upper = middle;
                }
                else
                {
                    lower = middle;
                }
            }

            return upper;
        }

        private static double[] ComputeNaturalSlopes(IReadOnlyList<double> parameters, IReadOnlyList<double> values)
        {
            var count = parameters.Count;
            var a = new double[count];
            var b = new double[count];
            var c = new double[count];
            var d = new double[count];

            for (var index = 1; index < count - 1; index++)
            {
                var deltaMinus = parameters[index] - parameters[index - 1];
                var deltaPlus = parameters[index + 1] - parameters[index];
                b[index] = deltaPlus;
                a[index] = 2d * (deltaMinus + deltaPlus);
                c[index] = deltaMinus;
                d[index] = 3d * (((values[index + 1] - values[index]) * deltaMinus / deltaPlus)
                               + ((values[index] - values[index - 1]) * deltaPlus / deltaMinus));
            }

            a[0] = 2d;
            c[0] = 1d;
            d[0] = 3d * (values[1] - values[0]) / (parameters[1] - parameters[0]);
            b[count - 1] = 1d;
            a[count - 1] = 2d;
            d[count - 1] = 3d * (values[count - 1] - values[count - 2]) / (parameters[count - 1] - parameters[count - 2]);

            return SolveTriDiagonal(a, b, c, d);
        }

        private static double[] SolveTriDiagonal(double[] a, double[] b, double[] c, double[] d)
        {
            var size = a.Length;
            var diagonal = (double[])a.Clone();
            var upper = (double[])c.Clone();
            var solution = (double[])d.Clone();

            for (var index = 1; index < size; index++)
            {
                var previous = index - 1;
                upper[previous] /= diagonal[previous];
                solution[previous] /= diagonal[previous];
                diagonal[index] -= b[index] * upper[previous];
                solution[index] -= b[index] * solution[previous];
            }

            solution[size - 1] /= diagonal[size - 1];
            for (var index = size - 2; index >= 0; index--)
            {
                solution[index] -= upper[index] * solution[index + 1];
            }

            return solution;
        }
    }
}
