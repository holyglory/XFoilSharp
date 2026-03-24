using XFoil.Core.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xgeom.f :: LEFIND/NORM/GEOPAR
// Secondary legacy source: f_xfoil/src/spline.f :: SCALC/SINVRT
// Role in port: Supplies reusable geometry-frame, leading-edge, and spline helpers shared by the managed geometry-edit services.
// Differences: The managed path exposes legacy geometry operations as composable utility methods and nested spline helpers instead of funneling them through command-oriented common blocks.
// Decision: Keep the managed refactor because it factors out geometry primitives that multiple design services reuse while preserving legacy geometric meaning.
namespace XFoil.Design.Services;

internal static class GeometryTransformUtilities
{
    // Legacy mapping: f_xfoil/src/xgeom.f :: LEFIND seed search lineage.
    // Difference from legacy: The helper chooses the minimum-x seed directly before the refined leading-edge solve, rather than embedding that scan inside a monolithic geometry routine.
    // Decision: Keep the managed refactor because it isolates a reusable seed-selection step.
    internal static int FindLeadingEdgeIndex(IReadOnlyList<AirfoilPoint> points)
    {
        var bestIndex = 0;
        var bestX = points[0].X;

        // Legacy block: LEFIND coarse leading-edge seed scan.
        // Difference: The managed helper performs the same minimum-x scan in a standalone utility.
        // Decision: Keep the equivalent loop for clarity.
        for (var index = 1; index < points.Count; index++)
        {
            if (points[index].X < bestX)
            {
                bestIndex = index;
                bestX = points[index].X;
            }
        }

        return bestIndex;
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: NORM/LEFIND and f_xfoil/src/spline.f :: SCALC.
    // Difference from legacy: The port builds a reusable chord frame through explicit spline and leading-edge helpers instead of through in-place normalization of global arrays.
    // Decision: Keep the managed refactor because multiple geometry services consume the same normalized frame.
    internal static ChordFrame BuildChordFrame(IReadOnlyList<AirfoilPoint> points)
    {
        var leadingEdgeIndex = FindLeadingEdgeIndex(points);
        var trailingEdge = new AirfoilPoint(
            0.5d * (points[0].X + points[^1].X),
            0.5d * (points[0].Y + points[^1].Y));
        var curve = SplineCurve2D.Create(points);
        var leadingEdgeArcLength = FindLeadingEdgeArcLength(curve, trailingEdge, curve.ArcLengths[leadingEdgeIndex]);
        var leadingEdge = curve.Evaluate(leadingEdgeArcLength);
        var chordVectorX = trailingEdge.X - leadingEdge.X;
        var chordVectorY = trailingEdge.Y - leadingEdge.Y;
        var chordLength = Math.Sqrt((chordVectorX * chordVectorX) + (chordVectorY * chordVectorY));
        if (chordLength <= 1e-12d)
        {
            throw new InvalidOperationException("Airfoil chord is degenerate.");
        }

        return new ChordFrame(
            leadingEdgeIndex,
            leadingEdgeArcLength,
            leadingEdge,
            trailingEdge,
            chordVectorX / chordLength,
            chordVectorY / chordLength,
            chordLength);
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: NORM coordinate projection.
    // Difference from legacy: The projection is exposed as an explicit helper instead of being folded into command-specific geometry edits.
    // Decision: Keep the equivalent helper for reuse.
    internal static AirfoilPoint ToChordFrame(AirfoilPoint point, ChordFrame frame)
    {
        var dx = point.X - frame.LeadingEdge.X;
        var dy = point.Y - frame.LeadingEdge.Y;
        return new AirfoilPoint(
            (dx * frame.ChordDirectionX) + (dy * frame.ChordDirectionY),
            (dy * frame.ChordDirectionX) - (dx * frame.ChordDirectionY));
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: inverse normalization transform.
    // Difference from legacy: The inverse projection is expressed as a standalone helper for immutable point reconstruction.
    // Decision: Keep the equivalent helper because design services need the inverse map explicitly.
    internal static AirfoilPoint FromChordFrame(AirfoilPoint point, ChordFrame frame)
    {
        return new AirfoilPoint(
            frame.LeadingEdge.X + (point.X * frame.ChordDirectionX) - (point.Y * frame.ChordDirectionY),
            frame.LeadingEdge.Y + (point.Y * frame.ChordDirectionX) + (point.X * frame.ChordDirectionY));
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: LEFIND.
    // Difference from legacy: The managed helper returns only the leading-edge point by delegating to the shared chord-frame builder.
    // Decision: Keep the managed refactor because it avoids duplicating the leading-edge solve.
    internal static AirfoilPoint EstimateLeadingEdgePoint(IReadOnlyList<AirfoilPoint> points)
    {
        return BuildChordFrame(points).LeadingEdge;
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: GEOPAR.
    // Difference from legacy: The radius estimate is reconstructed from spline derivatives in local helpers instead of from command-scope derivative arrays.
    // Decision: Keep the managed refactor because it preserves the same curvature formula with clearer data flow.
    internal static double EstimateLeadingEdgeRadius(AirfoilGeometry geometry)
    {
        var curve = SplineCurve2D.Create(geometry.Points);
        var frame = BuildChordFrame(geometry.Points);
        var firstDerivative = curve.EvaluateDerivative(frame.LeadingEdgeArcLength);
        var secondDerivative = curve.EvaluateSecondDerivative(frame.LeadingEdgeArcLength);
        var numerator = Math.Pow((firstDerivative.X * firstDerivative.X) + (firstDerivative.Y * firstDerivative.Y), 1.5d);
        var denominator = Math.Abs((firstDerivative.X * secondDerivative.Y) - (firstDerivative.Y * secondDerivative.X));
        if (denominator <= 1e-12d)
        {
            return double.PositiveInfinity;
        }

        return numerator / denominator;
    }

    // Legacy mapping: f_xfoil/src/xgdes.f :: LERAD opposite-surface sampling, with f_xfoil/src/spline.f :: SINVRT lineage.
    // Difference from legacy: The port performs an explicit bracket-and-Newton solve on the chord-frame spline instead of relying on the original shared spline inversion routines.
    // Decision: Keep the managed refactor because the opposite-surface query is a reusable geometry primitive.
    internal static AirfoilPoint FindOppositePointAtSameChordX(
        IReadOnlyList<AirfoilPoint> surfacePoints,
        double targetChordX,
        ChordFrame frame)
    {
        var curve = SplineCurve2D.Create(surfacePoints);
        var seedIndex = 0;
        var bestDistance = double.PositiveInfinity;
        for (var index = 0; index < surfacePoints.Count; index++)
        {
            var chordPoint = ToChordFrame(surfacePoints[index], frame);
            var distance = Math.Abs(chordPoint.X - targetChordX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                seedIndex = index;
            }
        }

        var arcLength = curve.ArcLengths[seedIndex];
        // Legacy block: SINVRT-style refinement to find the point whose chordwise x matches the requested coordinate.
        // Difference: The managed helper iterates on a reusable spline curve abstraction instead of on raw geometry arrays.
        // Decision: Keep the managed refactor because the solve remains explicit and reusable.
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var point = curve.Evaluate(arcLength);
            var chordPoint = ToChordFrame(point, frame);
            var derivative = curve.EvaluateDerivative(arcLength);
            var chordDerivativeX =
                (derivative.X * frame.ChordDirectionX)
                + (derivative.Y * frame.ChordDirectionY);
            var residual = chordPoint.X - targetChordX;
            if (Math.Abs(residual) <= 1e-10d)
            {
                return point;
            }

            if (Math.Abs(chordDerivativeX) <= 1e-12d)
            {
                break;
            }

            var delta = Math.Clamp(-residual / chordDerivativeX, -0.05d * curve.TotalArcLength, 0.05d * curve.TotalArcLength);
            arcLength = Math.Clamp(arcLength + delta, 0d, curve.TotalArcLength);
        }

        return curve.Evaluate(arcLength);
    }

    // Legacy mapping: f_xfoil/src/xgdes.f :: local linear segment interpolation helpers used by several edit commands.
    // Difference from legacy: The interpolation is made explicit as a reusable helper rather than duplicated inline in each command handler.
    // Decision: Keep the equivalent helper for clarity.
    internal static double InterpolateY(IReadOnlyList<AirfoilPoint> points, double x)
    {
        if (x <= points[0].X)
        {
            return points[0].Y;
        }

        if (x >= points[^1].X)
        {
            return points[^1].Y;
        }

        for (var index = 1; index < points.Count; index++)
        {
            var left = points[index - 1];
            var right = points[index];
            if (x > right.X)
            {
                continue;
            }

            var deltaX = right.X - left.X;
            if (Math.Abs(deltaX) < 1e-12d)
            {
                return 0.5d * (left.Y + right.Y);
            }

            var t = (x - left.X) / deltaX;
            return left.Y + (t * (right.Y - left.Y));
        }

        return points[^1].Y;
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: local curvature-radius geometry.
    // Difference from legacy: The circumcircle formula is factored into a dedicated helper rather than embedded in larger geometry routines.
    // Decision: Keep the equivalent helper for local reuse.
    private static double CircumcircleRadius(AirfoilPoint first, AirfoilPoint second, AirfoilPoint third)
    {
        var sideA = Distance(second, third);
        var sideB = Distance(first, third);
        var sideC = Distance(first, second);
        var twiceArea =
            Math.Abs(
                (first.X * (second.Y - third.Y))
                + (second.X * (third.Y - first.Y))
                + (third.X * (first.Y - second.Y)));
        if (twiceArea <= 1e-12d)
        {
            return double.PositiveInfinity;
        }

        return (sideA * sideB * sideC) / (2d * twiceArea);
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: local distance algebra used throughout the geometry routines.
    // Difference from legacy: The algebra is unchanged; it is factored into a managed helper.
    // Decision: Keep the equivalent helper.
    private static double Distance(AirfoilPoint first, AirfoilPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // Legacy mapping: f_xfoil/src/xgeom.f :: LEFIND Newton refinement.
    // Difference from legacy: The managed port evaluates the residual against the chord-to-trailing-edge orthogonality condition through the shared spline abstraction instead of direct derivative arrays.
    // Decision: Keep the managed refactor because it preserves the same geometric condition with clearer intermediates.
    private static double FindLeadingEdgeArcLength(SplineCurve2D curve, AirfoilPoint trailingEdge, double seedArcLength)
    {
        var arcLength = Math.Clamp(seedArcLength, 0d, curve.TotalArcLength);
        var epsilon = Math.Max(1e-5d, curve.TotalArcLength * 1e-5d);

        // Legacy block: LEFIND root solve for the leading-edge station.
        // Difference: The managed loop keeps the residual and derivative terms explicit rather than folding them into the Fortran command routine.
        // Decision: Keep the managed refactor because it makes the leading-edge criterion auditable.
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var point = curve.Evaluate(arcLength);
            var firstDerivative = curve.EvaluateDerivative(arcLength);
            var secondDerivative = curve.EvaluateSecondDerivative(arcLength);

            var chordX = point.X - trailingEdge.X;
            var chordY = point.Y - trailingEdge.Y;
            var residual = (chordX * firstDerivative.X) + (chordY * firstDerivative.Y);
            var residualDerivative =
                (firstDerivative.X * firstDerivative.X)
                + (firstDerivative.Y * firstDerivative.Y)
                + (chordX * secondDerivative.X)
                + (chordY * secondDerivative.Y);

            if (Math.Abs(residual) <= epsilon)
            {
                break;
            }

            if (Math.Abs(residualDerivative) <= 1e-12d)
            {
                break;
            }

            var delta = Math.Clamp(-residual / residualDerivative, -0.02d * curve.TotalArcLength, 0.02d * curve.TotalArcLength);
            arcLength = Math.Clamp(arcLength + delta, 0d, curve.TotalArcLength);
            if (Math.Abs(delta) <= epsilon)
            {
                break;
            }
        }

        return arcLength;
    }

    internal readonly record struct ChordFrame(
        int LeadingEdgeIndex,
        double LeadingEdgeArcLength,
        AirfoilPoint LeadingEdge,
        AirfoilPoint TrailingEdge,
        double ChordDirectionX,
        double ChordDirectionY,
        double ChordLength);

    internal sealed class SplineCurve2D
    {
        private readonly NaturalCubicSpline xSpline;
        private readonly NaturalCubicSpline ySpline;

        // Legacy mapping: f_xfoil/src/spline.f :: SCALC/SPLIND setup supporting xgeom/xgdes operations.
        // Difference from legacy: The managed helper stores the arc-length-parametrized splines as an object instead of as parallel buffers.
        // Decision: Keep the managed refactor because it groups the curve state coherently.
        private SplineCurve2D(double[] arcLengths, AirfoilPoint[] points)
        {
            ArcLengths = arcLengths;
            Points = points;
            xSpline = new NaturalCubicSpline(arcLengths, points.Select(point => point.X).ToArray());
            ySpline = new NaturalCubicSpline(arcLengths, points.Select(point => point.Y).ToArray());
            TotalArcLength = arcLengths[^1];
        }

        public IReadOnlyList<double> ArcLengths { get; }

        public IReadOnlyList<AirfoilPoint> Points { get; }

        public double TotalArcLength { get; }

        // Legacy mapping: f_xfoil/src/spline.f :: SCALC.
        // Difference from legacy: The arc-length build is exposed as a constructor-style factory instead of being hidden inside each geometry command.
        // Decision: Keep the managed refactor because many services share the same setup.
        public static SplineCurve2D Create(IReadOnlyList<AirfoilPoint> points)
        {
            var arcLengths = new double[points.Count];
            // Legacy block: SCALC cumulative arc-length construction for the full contour.
            // Difference: The managed code computes the same cumulative distances into a local array owned by the helper object.
            // Decision: Keep the equivalent loop.
            for (var index = 1; index < points.Count; index++)
            {
                arcLengths[index] = arcLengths[index - 1] + Distance(points[index - 1], points[index]);
            }

            return new SplineCurve2D(arcLengths, points.ToArray());
        }

        // Legacy mapping: f_xfoil/src/spline.f :: SEVAL.
        // Difference from legacy: The evaluator reads from object-owned splines instead of procedural arrays.
        // Decision: Keep the equivalent helper for clarity.
        public AirfoilPoint Evaluate(double arcLength)
        {
            return new AirfoilPoint(
                xSpline.Evaluate(arcLength),
                ySpline.Evaluate(arcLength));
        }

        // Legacy mapping: f_xfoil/src/spline.f :: first-derivative evaluation used by xgeom routines.
        // Difference from legacy: The design path estimates the derivative numerically from repeated evaluations rather than carrying a precomputed derivative array.
        // Decision: Keep the managed improvement because it simplifies state management.
        public AirfoilPoint EvaluateDerivative(double arcLength)
        {
            return new AirfoilPoint(
                EvaluateFirstDerivative(xSpline, arcLength),
                EvaluateFirstDerivative(ySpline, arcLength));
        }

        // Legacy mapping: f_xfoil/src/spline.f :: second-derivative support used by GEOPAR/LEFIND lineage.
        // Difference from legacy: The design path reconstructs the second derivative numerically from repeated spline evaluation.
        // Decision: Keep the managed improvement because it is sufficient for the geometry tools.
        public AirfoilPoint EvaluateSecondDerivative(double arcLength)
        {
            return new AirfoilPoint(
                EvaluateSecondDerivative(xSpline, arcLength),
                EvaluateSecondDerivative(ySpline, arcLength));
        }

        // Legacy mapping: f_xfoil/src/spline.f :: derivative evaluation helper.
        // Difference from legacy: This helper uses centered finite differences instead of the original derivative arrays.
        // Decision: Keep the managed improvement because it keeps the spline wrapper compact.
        private static double EvaluateFirstDerivative(NaturalCubicSpline spline, double parameter)
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

        // Legacy mapping: f_xfoil/src/spline.f :: second-derivative helper.
        // Difference from legacy: The C# port reconstructs the second derivative numerically instead of reading an analytic cubic-coefficient table.
        // Decision: Keep the managed improvement for the design-tool path.
        private static double EvaluateSecondDerivative(NaturalCubicSpline spline, double parameter)
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

    internal sealed class NaturalCubicSpline
    {
        private readonly double[] parameters;
        private readonly double[] values;
        private readonly double[] slopes;

        // Legacy mapping: f_xfoil/src/spline.f :: SPLIND/SPLINA setup.
        // Difference from legacy: The managed constructor delegates to the generalized endpoint-derivative overload instead of relying on separate spline entry points.
        // Decision: Keep the managed refactor because it unifies the spline construction variants.
        public NaturalCubicSpline(IReadOnlyList<double> parameters, IReadOnlyList<double> values)
            : this(parameters, values, null, null)
        {
        }

        // Legacy mapping: f_xfoil/src/spline.f :: SPLIND and endpoint-slope variants used by LEFIND/ZLEFIND lineage.
        // Difference from legacy: The port captures optional endpoint derivatives directly in the constructor rather than via sentinel values threaded through procedural calls.
        // Decision: Keep the managed refactor because it makes the boundary-condition choice explicit.
        public NaturalCubicSpline(
            IReadOnlyList<double> parameters,
            IReadOnlyList<double> values,
            double? startDerivative,
            double? endDerivative)
        {
            this.parameters = parameters.ToArray();
            this.values = values.ToArray();
            slopes = ComputeNaturalSlopes(this.parameters, this.values, startDerivative, endDerivative);
        }

        public IReadOnlyList<double> Parameters => parameters;

        // Legacy mapping: f_xfoil/src/spline.f :: SEVAL.
        // Difference from legacy: The cubic Hermite evaluation is written inline in C# instead of dispatched through the Fortran spline package.
        // Decision: Keep the managed refactor because it preserves the interpolation law with less hidden state.
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

        // Legacy mapping: f_xfoil/src/spline.f :: interval lookup before spline evaluation.
        // Difference from legacy: The managed port isolates the binary search in a reusable helper instead of coupling it to each evaluation site.
        // Decision: Keep the managed refactor for clarity.
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

        // Legacy mapping: f_xfoil/src/spline.f :: SPLIND slope-system assembly.
        // Difference from legacy: The managed implementation supports optional endpoint derivatives directly and keeps the tridiagonal coefficients local to the helper.
        // Decision: Keep the managed refactor because it preserves the spline construction with clearer boundary-condition handling.
        private static double[] ComputeNaturalSlopes(
            IReadOnlyList<double> parameters,
            IReadOnlyList<double> values,
            double? startDerivative,
            double? endDerivative)
        {
            var count = parameters.Count;
            var a = new double[count];
            var b = new double[count];
            var c = new double[count];
            var d = new double[count];

            // Legacy block: SPLIND tridiagonal system assembly for cubic-spline slopes.
            // Difference: The coefficient assembly is structurally the same, but the managed code makes the endpoint-condition branches explicit instead of encoding them through sentinel arguments.
            // Decision: Keep the managed refactor because the spline boundary conditions are easier to audit.
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

            if (startDerivative.HasValue)
            {
                a[0] = 1d;
                c[0] = 0d;
                d[0] = startDerivative.Value;
            }
            else
            {
                a[0] = 2d;
                c[0] = 1d;
                d[0] = 3d * (values[1] - values[0]) / (parameters[1] - parameters[0]);
            }

            if (endDerivative.HasValue)
            {
                b[count - 1] = 0d;
                a[count - 1] = 1d;
                d[count - 1] = endDerivative.Value;
            }
            else
            {
                b[count - 1] = 1d;
                a[count - 1] = 2d;
                d[count - 1] = 3d * (values[count - 1] - values[count - 2]) / (parameters[count - 1] - parameters[count - 2]);
            }

            return SolveTriDiagonal(a, b, c, d);
        }

        // Legacy mapping: f_xfoil/src/xqdes.f :: TRISOL and spline backsolve lineage.
        // Difference from legacy: The Thomas sweep is kept as a local helper instead of a shared Fortran utility call.
        // Decision: Keep the equivalent managed solver because the algorithm is unchanged.
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
