using XFoil.Core.Models;
using XFoil.Design.Models;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xqdes.f :: QDES/MODI/SMOOQ
// Secondary legacy source: f_xfoil/src/xmdes.f :: PERT/CNCALC, f_xfoil/src/spline.f :: SPLIND/TRISOL
// Role in port: Builds, edits, smooths, and executes managed Qspec profiles for inverse-design workflows.
// Differences: The managed path turns the interactive QDES command flow into explicit immutable profile transforms and uses local helpers for smoothing, slope matching, and displacement reconstruction.
// Decision: Keep the managed refactor because it preserves the legacy editing intent while exposing deterministic library APIs instead of command-session state.
namespace XFoil.Design.Services;

public sealed class QSpecDesignService
{
    // Legacy mapping: f_xfoil/src/xqdes.f :: QDES profile initialization lineage.
    // Difference from legacy: The managed port derives the Qspec profile directly from inviscid pressure samples instead of initializing it through the interactive QDES session buffers.
    // Decision: Keep the managed refactor because it produces the same design input in library form.
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
        // Legacy block: QDES-style construction of the surface-coordinate/Qspec arrays from the current analysis state.
        // Difference: The managed port rebuilds the immutable `QSpecPoint` array directly from solver samples instead of filling COMMON-backed arrays.
        // Decision: Keep the managed refactor because the resulting profile is explicit.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: MODI.
    // Difference from legacy: The managed implementation performs the Qspec graft through explicit control-point splines and immutable profile rebuilding instead of through interactive endpoint selection and in-place QSPEC updates.
    // Decision: Keep the managed refactor because it expresses the same graft operation with clearer inputs and outputs.
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
        // Legacy block: MODI replacement of the selected Qspec segment by the user-defined spline.
        // Difference: The managed port writes the modified segment into a fresh speed-ratio array rather than mutating the active QDES workspace in place.
        // Decision: Keep the managed refactor because the edit span is explicit and testable.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: SMOOQ.
    // Difference from legacy: The managed routine assembles the same smoothing tridiagonal system in explicit arrays and exposes the smoothing length as a library argument instead of interactive state.
    // Decision: Keep the managed refactor because it preserves the smoothing law while making the solve auditable.
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

        // Legacy block: SMOOQ tridiagonal system assembly for interior Qspec smoothing.
        // Difference: The coefficient arrays are named and local in C# rather than stored in the shared W1/W2/W3 work arrays.
        // Decision: Keep the managed refactor because it exposes the smoothing operator directly.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: SYMM branch inside QDES.
    // Difference from legacy: The managed implementation mirrors the same antisymmetric speed-ratio update in an immutable array instead of mutating the active QSPEC buffer.
    // Decision: Keep the equivalent managed refactor because the symmetry transform is easier to verify.
    public QSpecProfile ForceSymmetry(QSpecProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var points = profile.Points.ToArray();
        var symmetricSpeedRatio = points.Select(point => point.SpeedRatio).ToArray();
        // Legacy block: QDES symmetry forcing over mirrored surface stations.
        // Difference: The managed loop applies the same paired update to an immutable result buffer rather than editing the live command-state array.
        // Decision: Keep the equivalent managed loop.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: inverse-execution lineage, with conceptual overlap to f_xfoil/src/xmdes.f :: PERT.
    // Difference from legacy: The managed port applies a simplified normal-displacement reconstruction from Qspec deltas instead of invoking the legacy conformal-map inverse solve.
    // Decision: Keep the managed improvement because this API intentionally exposes a lighter-weight inverse edit; no parity branch is required in the design tool path.
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
        // Legacy block: QDES/PERT-style accumulation of the requested Qspec delta field.
        // Difference: The managed code derives a normalized displacement-driving field directly from paired profiles rather than through the legacy modal/conformal state.
        // Decision: Keep the managed improvement because it matches the library API shape.
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
        // Legacy block: Managed-only displacement scaling inspired by inverse-design magnitude limiting.
        // Difference: The original Fortran inverse path worked in conformal mapping coefficients; this managed API maps the Qspec delta directly to capped normal displacements.
        // Decision: Keep the managed-only formulation because it is the intended higher-level design tool behavior.
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
        // Legacy block: Managed-only geometry reconstruction from the limited normal-displacement field.
        // Difference: The port displaces profile samples directly along estimated outward normals instead of regenerating geometry through MAPGEN/CNCALC.
        // Decision: Keep the managed improvement because this service intentionally trades exact legacy fidelity for a direct API.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: SMOOQ endpoint-slope constraints.
    // Difference from legacy: The managed helper names the endpoint constraint coefficients explicitly instead of assembling them inline into W1/W2/W3.
    // Decision: Keep the managed refactor because it makes the constrained rows auditable.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: TRISOL.
    // Difference from legacy: The same Thomas sweep is implemented locally on explicit arrays rather than through a shared Fortran utility call.
    // Decision: Keep the equivalent managed solver for locality and readability.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: QSPEC array rebuild after an edit.
    // Difference from legacy: The managed implementation returns a new immutable profile object instead of modifying the active QDES arrays in place.
    // Decision: Keep the managed refactor because the API is intentionally immutable.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: target-index selection for Qspec edits.
    // Difference from legacy: The helper picks the nearest immutable profile index directly rather than relying on interactive cursor state.
    // Decision: Keep the managed refactor because the selection rule is explicit.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: endpoint slope estimation around MODI/SMOOQ.
    // Difference from legacy: The derivative is estimated from the neighboring immutable profile points instead of from an active spline derivative buffer.
    // Decision: Keep the managed refactor because it is sufficient for the edit endpoints.
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

    // Legacy mapping: f_xfoil/src/xqdes.f :: SSPEC/QSPEC arc-length-style parameter construction.
    // Difference from legacy: The managed implementation derives the cumulative surface distances from pressure-sample locations instead of reading the active geometry buffers.
    // Decision: Keep the managed refactor because it produces the same normalized coordinate input for library callers.
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

    // Legacy mapping: none directly; managed-only postprocessing for ExecuteInverse.
    // Difference from legacy: The original inverse-design path used conformal-map updates rather than explicit repeated averaging of normal displacements.
    // Decision: Keep the managed-only helper because it intentionally stabilizes the simplified execution path.
    private static void SmoothDisplacements(double[] displacements, int smoothingPasses)
    {
        if (displacements.Length <= 2)
        {
            return;
        }

        // Legacy block: Managed-only smoothing passes for the simplified displacement field.
        // Difference: This averaging loop is a library-specific stabilizer, not a direct translation of a single QDES routine.
        // Decision: Keep the managed-only helper because it reduces kinks in the displaced geometry.
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

    // Legacy mapping: none; managed-only geometry helper supporting the simplified inverse execution path.
    // Difference from legacy: The centroid is used only by the managed direct-normal reconstruction, which has no one-to-one Fortran analogue.
    // Decision: Keep the managed-only helper.
    private static AirfoilPoint ComputeCentroid(IReadOnlyList<AirfoilPoint> points)
    {
        return new AirfoilPoint(
            points.Average(point => point.X),
            points.Average(point => point.Y));
    }

    // Legacy mapping: none; managed-only normal-estimation helper for ExecuteInverse.
    // Difference from legacy: The original inverse-design routines work through conformal maps and geometry regeneration, not direct discrete outward normals.
    // Decision: Keep the managed-only helper because it is intrinsic to the simplified managed execution path.
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
