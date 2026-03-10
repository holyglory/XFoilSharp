using System.Numerics;
using XFoil.Core.Models;
using XFoil.Design.Models;

namespace XFoil.Design.Services;

public sealed class ConformalMapgenService
{
    public ConformalMapgenResult Execute(
        AirfoilGeometry geometry,
        QSpecProfile targetProfile,
        int circlePointCount = 129,
        int maxNewtonIterations = 10,
        double convergenceTolerance = 5e-5d,
        AirfoilPoint? targetTrailingEdgeGap = null,
        double? targetTrailingEdgeAngleDegrees = null,
        double filterExponent = 0d)
    {
        return Execute(
            geometry,
            targetProfile,
            targetProfile,
            circlePointCount,
            maxNewtonIterations,
            convergenceTolerance,
            targetTrailingEdgeGap,
            targetTrailingEdgeAngleDegrees,
            filterExponent);
    }

    public ConformalMapgenResult Execute(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        QSpecProfile targetProfile,
        int circlePointCount = 129,
        int maxNewtonIterations = 10,
        double convergenceTolerance = 5e-5d,
        AirfoilPoint? targetTrailingEdgeGap = null,
        double? targetTrailingEdgeAngleDegrees = null,
        double filterExponent = 0d)
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

        if (circlePointCount < 9 || (circlePointCount % 2) == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(circlePointCount), "Circle-point count must be odd and at least 9.");
        }

        if (maxNewtonIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNewtonIterations), "At least one Newton iteration is required.");
        }

        if (!targetTrailingEdgeAngleDegrees.HasValue)
        {
            var directResult = SolveForProfileTarget(
                geometry,
                baselineProfile,
                targetProfile,
                circlePointCount,
                maxNewtonIterations,
                convergenceTolerance,
                targetTrailingEdgeGap,
                null,
                filterExponent);
            var geometryTrailingEdgeAngle = ComputeTrailingEdgeAngleDegrees(geometry.Points);
            if (Math.Abs(directResult.AchievedTrailingEdgeAngleDegrees - geometryTrailingEdgeAngle) <= DefaultTrailingEdgeAngleRetentionToleranceDegrees)
            {
                return RebindReportedTargets(directResult, targetTrailingEdgeGap, geometryTrailingEdgeAngle);
            }

            return RebindReportedTargets(
                SolveForTrailingEdgeAngleTarget(
                    geometry,
                    baselineProfile,
                    targetProfile,
                    circlePointCount,
                    maxNewtonIterations,
                    convergenceTolerance,
                    targetTrailingEdgeGap,
                    geometryTrailingEdgeAngle,
                    filterExponent),
                targetTrailingEdgeGap,
                geometryTrailingEdgeAngle);
        }

        return RebindReportedTargets(
            SolveForTrailingEdgeAngleTarget(
                geometry,
                baselineProfile,
                targetProfile,
                circlePointCount,
                maxNewtonIterations,
                convergenceTolerance,
                targetTrailingEdgeGap,
                targetTrailingEdgeAngleDegrees.Value,
                filterExponent),
            targetTrailingEdgeGap,
            targetTrailingEdgeAngleDegrees.Value);
    }

    private static ConformalMapgenResult SolveForTrailingEdgeAngleTarget(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        QSpecProfile targetProfile,
        int circlePointCount,
        int maxNewtonIterations,
        double convergenceTolerance,
        AirfoilPoint? targetTrailingEdgeGap,
        double requestedAngleDegrees,
        double filterExponent)
    {
        var firstTrialAngle = ClampTrailingEdgeAngle(requestedAngleDegrees);
        var firstResult = SolveForProfileTarget(
            geometry,
            baselineProfile,
            targetProfile,
            circlePointCount,
            maxNewtonIterations,
            convergenceTolerance,
            targetTrailingEdgeGap,
            firstTrialAngle,
            filterExponent);
        var firstResidual = firstResult.AchievedTrailingEdgeAngleDegrees - requestedAngleDegrees;
        if (Math.Abs(firstResidual) <= TrailingEdgeAngleToleranceDegrees)
        {
            return firstResult;
        }

        var secondTrialAngle = ClampTrailingEdgeAngle(firstTrialAngle - Math.CopySign(Math.Max(0.75d, Math.Abs(firstResidual)), firstResidual));
        if (Math.Abs(secondTrialAngle - firstTrialAngle) <= 1e-9d)
        {
            secondTrialAngle = ClampTrailingEdgeAngle(firstTrialAngle + (firstResidual >= 0d ? -1d : 1d));
        }

        var secondResult = SolveForProfileTarget(
            geometry,
            baselineProfile,
            targetProfile,
            circlePointCount,
            maxNewtonIterations,
            convergenceTolerance,
            targetTrailingEdgeGap,
            secondTrialAngle,
            filterExponent);
        var secondResidual = secondResult.AchievedTrailingEdgeAngleDegrees - requestedAngleDegrees;

        var bestResult = Math.Abs(secondResidual) < Math.Abs(firstResidual) ? secondResult : firstResult;
        var bestResidual = Math.Abs(secondResidual) < Math.Abs(firstResidual) ? secondResidual : firstResidual;
        var lowerTrial = firstTrialAngle;
        var lowerResidual = firstResidual;
        var lowerResult = firstResult;
        var upperTrial = secondTrialAngle;
        var upperResidual = secondResidual;
        var upperResult = secondResult;

        NormalizeBracket(
            ref lowerTrial,
            ref lowerResidual,
            ref lowerResult,
            ref upperTrial,
            ref upperResidual,
            ref upperResult);

        for (var outerIteration = 0; outerIteration < 8; outerIteration++)
        {
            var nextTrial = TryFalsePosition(lowerTrial, lowerResidual, upperTrial, upperResidual)
                ?? TrySecant(lowerTrial, lowerResidual, upperTrial, upperResidual)
                ?? (0.5d * (lowerTrial + upperTrial));
            nextTrial = ClampTrailingEdgeAngle(nextTrial);

            if (Math.Abs(nextTrial - lowerTrial) <= 1e-9d || Math.Abs(nextTrial - upperTrial) <= 1e-9d)
            {
                nextTrial = ClampTrailingEdgeAngle(0.5d * (lowerTrial + upperTrial));
            }

            var nextResult = SolveForProfileTarget(
                geometry,
                baselineProfile,
                targetProfile,
                circlePointCount,
                maxNewtonIterations,
                convergenceTolerance,
                targetTrailingEdgeGap,
                nextTrial,
                filterExponent);
            var nextResidual = nextResult.AchievedTrailingEdgeAngleDegrees - requestedAngleDegrees;
            if (Math.Abs(nextResidual) < Math.Abs(bestResidual))
            {
                bestResidual = nextResidual;
                bestResult = nextResult;
            }

            if (Math.Abs(nextResidual) <= TrailingEdgeAngleToleranceDegrees)
            {
                return nextResult;
            }

            if (Math.Sign(nextResidual) == Math.Sign(lowerResidual))
            {
                lowerTrial = nextTrial;
                lowerResidual = nextResidual;
                lowerResult = nextResult;
            }
            else
            {
                upperTrial = nextTrial;
                upperResidual = nextResidual;
                upperResult = nextResult;
            }

            if (Math.Sign(lowerResidual) == Math.Sign(upperResidual))
            {
                if (Math.Abs(lowerResidual) <= Math.Abs(upperResidual))
                {
                    upperTrial = nextTrial;
                    upperResidual = nextResidual;
                    upperResult = nextResult;
                }
                else
                {
                    lowerTrial = nextTrial;
                    lowerResidual = nextResidual;
                    lowerResult = nextResult;
                }

                NormalizeBracket(
                    ref lowerTrial,
                    ref lowerResidual,
                    ref lowerResult,
                    ref upperTrial,
                    ref upperResidual,
                    ref upperResult);
            }
        }

        return bestResult;
    }

    private static ConformalMapgenResult SolveForProfileTarget(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        QSpecProfile targetProfile,
        int circlePointCount,
        int maxNewtonIterations,
        double convergenceTolerance,
        AirfoilPoint? targetTrailingEdgeGap,
        double? solverTrailingEdgeAngleDegrees,
        double filterExponent)
    {
        var directResult = ExecuteCore(
            geometry,
            baselineProfile,
            targetProfile,
            circlePointCount,
            maxNewtonIterations,
            convergenceTolerance,
            targetTrailingEdgeGap,
            solverTrailingEdgeAngleDegrees,
            filterExponent);
        if (ShouldAcceptDirectResult(directResult, convergenceTolerance))
        {
            return directResult;
        }

        return ExecuteWithContinuation(
            geometry,
            baselineProfile,
            targetProfile,
            circlePointCount,
            maxNewtonIterations,
            convergenceTolerance,
            targetTrailingEdgeGap,
            solverTrailingEdgeAngleDegrees,
            filterExponent,
            directResult);
    }

    private static ConformalMapgenResult ExecuteWithContinuation(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        QSpecProfile targetProfile,
        int circlePointCount,
        int maxNewtonIterations,
        double convergenceTolerance,
        AirfoilPoint? targetTrailingEdgeGap,
        double? solverTrailingEdgeAngleDegrees,
        double filterExponent,
        ConformalMapgenResult directResult)
    {
        var stageCount = DetermineContinuationStageCount(baselineProfile, targetProfile);
        if (stageCount <= 1)
        {
            return directResult;
        }

        var stageGeometry = geometry;
        var stageBaselineProfile = baselineProfile;
        var bestResult = directResult;
        var initialFilterExponent = DetermineInitialContinuationFilter(filterExponent);
        for (var stageIndex = 1; stageIndex <= stageCount; stageIndex++)
        {
            var fraction = (double)stageIndex / stageCount;
            var stageTargetProfile = BlendProfiles(baselineProfile, targetProfile, fraction);
            var stageFilterExponent = filterExponent + ((initialFilterExponent - filterExponent) * (1d - fraction));
            var stageResult = ExecuteCore(
                stageGeometry,
                stageBaselineProfile,
                stageTargetProfile,
                circlePointCount,
                maxNewtonIterations,
                convergenceTolerance,
                targetTrailingEdgeGap,
                solverTrailingEdgeAngleDegrees,
                stageFilterExponent);

            if (IsBetterResult(stageResult, bestResult))
            {
                bestResult = stageResult;
            }

            if (!AreFinite(stageResult.Geometry.Points.Select(point => new Complex(point.X, point.Y)).ToArray()))
            {
                break;
            }

            stageGeometry = stageResult.Geometry;
            stageBaselineProfile = stageTargetProfile;
        }

        return bestResult;
    }

    private static ConformalMapgenResult ExecuteCore(
        AirfoilGeometry geometry,
        QSpecProfile baselineProfile,
        QSpecProfile targetProfile,
        int circlePointCount,
        int maxNewtonIterations,
        double convergenceTolerance,
        AirfoilPoint? targetTrailingEdgeGap,
        double? solverTrailingEdgeAngleDegrees,
        double filterExponent)
    {
        var baselineResampled = ResampleProfile(baselineProfile, circlePointCount);
        var targetResampled = ResampleProfile(targetProfile, circlePointCount);
        var state = CirclePlaneState.Create(geometry, baselineResampled, targetTrailingEdgeGap, solverTrailingEdgeAngleDegrees);
        BuildCnFromQSpec(state, baselineResampled, false);
        BuildCnFromQSpec(state, targetResampled, true);
        ApplyHanningFilter(state.Cn, filterExponent);

        var tangent = geometry.Points[1];
        var tangentDx = tangent.X - geometry.Points[0].X;
        var tangentDy = tangent.Y - geometry.Points[0].Y;
        var qim0 = Math.Atan2(tangentDx, -tangentDy) + (0.5d * Math.PI * (1d + state.Agte));
        var qimoff = qim0 - state.Cn[0].Imaginary;
        state.Cn[0] += Complex.ImaginaryOne * qimoff;

        PiqSum(state);
        ZcCalc(state);
        ZcNorm(state);
        state.Cn[0] = new Complex(0d, state.Cn[0].Imaginary);
        var lastFiniteZc = AreFinite(state.Zc)
            ? state.Zc.ToArray()
            : baselineResampled.Points.Select(point => new Complex(point.Location.X, point.Location.Y)).ToArray();
        var initialResidualMagnitude = Complex.Abs(state.Zc[0] - state.Zc[state.Nc - 1] - state.Dzte);
        var finalResidualMagnitude = initialResidualMagnitude;

        var converged = false;
        var iterationCount = 0;
        var maxCorrection = 0d;
        var lastIndex = state.Nc - 1;
        for (var iteration = 0; iteration < maxNewtonIterations; iteration++)
        {
            var residual = state.Zc[0] - state.Zc[lastIndex] - state.Dzte;
            var jacobian = state.ZcCn[0, 1] - state.ZcCn[lastIndex, 1];
            if (!IsFinite(residual) || !IsFinite(jacobian) || Complex.Abs(jacobian) <= 1e-12d)
            {
                break;
            }

            var correction = residual / jacobian;
            if (!IsFinite(correction))
            {
                break;
            }

            var baseResidualMagnitude = Complex.Abs(residual);
            var baselineState = CaptureState(state);
            var accepted = false;
            var acceptedCorrection = Complex.Zero;
            foreach (var relaxation in DampingSchedule)
            {
                RestoreState(state, baselineState);
                var trialCorrection = correction * relaxation;
                state.Cn[1] -= trialCorrection;

                PiqSum(state);
                ZcCalc(state);
                ZcNorm(state);
                if (!AreFinite(state.Zc))
                {
                    continue;
                }

                var trialResidual = state.Zc[0] - state.Zc[lastIndex] - state.Dzte;
                if (!IsFinite(trialResidual))
                {
                    continue;
                }

                var trialResidualMagnitude = Complex.Abs(trialResidual);
                if (trialResidualMagnitude > baseResidualMagnitude && relaxation > DampingSchedule[^1])
                {
                    continue;
                }

                accepted = true;
                acceptedCorrection = trialCorrection;
                finalResidualMagnitude = trialResidualMagnitude;
                Array.Copy(state.Zc, lastFiniteZc, state.Zc.Length);
                break;
            }

            if (!accepted)
            {
                RestoreState(state, baselineState);
                Array.Copy(lastFiniteZc, state.Zc, lastFiniteZc.Length);
                break;
            }

            maxCorrection = Math.Max(maxCorrection, Complex.Abs(acceptedCorrection));
            iterationCount = iteration + 1;

            if (Complex.Abs(acceptedCorrection) <= convergenceTolerance)
            {
                converged = true;
                break;
            }
        }

        var points = state.Zc
            .Select(value => new AirfoilPoint(value.Real, value.Imaginary))
            .ToArray();
        var coefficients = state.Cn
            .Select((value, index) => new ConformalMappingCoefficient(index, value.Real, value.Imaginary))
            .ToArray();
        var outputGeometry = new AirfoilGeometry($"{geometry.Name} mapgen", points, geometry.Format, geometry.DomainParameters);
        var achievedTrailingEdgeGap = new AirfoilPoint(points[0].X - points[^1].X, points[0].Y - points[^1].Y);
        var targetGap = new AirfoilPoint(state.Dzte.Real, state.Dzte.Imaginary);
        var achievedTrailingEdgeAngleDegrees = ComputeTrailingEdgeAngleDegrees(points);
        return new ConformalMapgenResult(
            outputGeometry,
            coefficients,
            state.Nc,
            iterationCount,
            converged,
            maxCorrection,
            initialResidualMagnitude,
            finalResidualMagnitude,
            targetGap,
            achievedTrailingEdgeGap,
            state.Agte * 180d,
            achievedTrailingEdgeAngleDegrees);
    }

    private static ConformalMapgenResult RebindReportedTargets(
        ConformalMapgenResult result,
        AirfoilPoint? targetTrailingEdgeGap,
        double targetTrailingEdgeAngleDegrees)
    {
        return new ConformalMapgenResult(
            result.Geometry,
            result.Coefficients,
            result.CirclePointCount,
            result.IterationCount,
            result.Converged,
            result.MaxCoefficientCorrection,
            result.InitialTrailingEdgeResidual,
            result.FinalTrailingEdgeResidual,
            targetTrailingEdgeGap ?? result.TargetTrailingEdgeGap,
            result.AchievedTrailingEdgeGap,
            targetTrailingEdgeAngleDegrees,
            result.AchievedTrailingEdgeAngleDegrees);
    }

    private static bool ShouldAcceptDirectResult(ConformalMapgenResult result, double convergenceTolerance)
    {
        return result.Converged || result.FinalTrailingEdgeResidual <= Math.Max(10d * convergenceTolerance, 1e-3d);
    }

    private static bool IsBetterResult(ConformalMapgenResult candidate, ConformalMapgenResult baseline)
    {
        if (candidate.Converged != baseline.Converged)
        {
            return candidate.Converged;
        }

        return candidate.FinalTrailingEdgeResidual < baseline.FinalTrailingEdgeResidual;
    }

    private static int DetermineContinuationStageCount(QSpecProfile baselineProfile, QSpecProfile targetProfile)
    {
        var maxSpeedRatioDelta = 0d;
        for (var index = 0; index < baselineProfile.Points.Count; index++)
        {
            maxSpeedRatioDelta = Math.Max(
                maxSpeedRatioDelta,
                Math.Abs(targetProfile.Points[index].SpeedRatio - baselineProfile.Points[index].SpeedRatio));
        }

        if (maxSpeedRatioDelta <= 0.05d)
        {
            return 1;
        }

        return Math.Clamp((int)Math.Ceiling(maxSpeedRatioDelta / 0.10d), 2, 6);
    }

    private static double DetermineInitialContinuationFilter(double requestedFilterExponent)
    {
        return requestedFilterExponent > 0d
            ? Math.Max(requestedFilterExponent, 1.5d)
            : 1.5d;
    }

    private static QSpecProfile BlendProfiles(QSpecProfile baselineProfile, QSpecProfile targetProfile, double fraction)
    {
        var clampedFraction = Math.Clamp(fraction, 0d, 1d);
        var points = new QSpecPoint[baselineProfile.Points.Count];
        for (var index = 0; index < points.Length; index++)
        {
            var baselinePoint = baselineProfile.Points[index];
            var targetPoint = targetProfile.Points[index];
            points[index] = new QSpecPoint(
                baselinePoint.Index,
                baselinePoint.SurfaceCoordinate,
                baselinePoint.PlotCoordinate,
                baselinePoint.Location,
                baselinePoint.SpeedRatio + ((targetPoint.SpeedRatio - baselinePoint.SpeedRatio) * clampedFraction),
                baselinePoint.PressureCoefficient + ((targetPoint.PressureCoefficient - baselinePoint.PressureCoefficient) * clampedFraction),
                baselinePoint.CorrectedPressureCoefficient + ((targetPoint.CorrectedPressureCoefficient - baselinePoint.CorrectedPressureCoefficient) * clampedFraction));
        }

        return new QSpecProfile(
            $"{targetProfile.Name} blend {clampedFraction:F2}",
            baselineProfile.AngleOfAttackDegrees + ((targetProfile.AngleOfAttackDegrees - baselineProfile.AngleOfAttackDegrees) * clampedFraction),
            baselineProfile.MachNumber + ((targetProfile.MachNumber - baselineProfile.MachNumber) * clampedFraction),
            points);
    }

    private static void NormalizeBracket(
        ref double lowerTrial,
        ref double lowerResidual,
        ref ConformalMapgenResult lowerResult,
        ref double upperTrial,
        ref double upperResidual,
        ref ConformalMapgenResult upperResult)
    {
        if (lowerTrial <= upperTrial)
        {
            return;
        }

        (lowerTrial, upperTrial) = (upperTrial, lowerTrial);
        (lowerResidual, upperResidual) = (upperResidual, lowerResidual);
        (lowerResult, upperResult) = (upperResult, lowerResult);
    }

    private static double? TryFalsePosition(double lowerTrial, double lowerResidual, double upperTrial, double upperResidual)
    {
        if (Math.Sign(lowerResidual) == Math.Sign(upperResidual))
        {
            return null;
        }

        var denominator = upperResidual - lowerResidual;
        if (Math.Abs(denominator) <= 1e-12d)
        {
            return null;
        }

        return lowerTrial - (lowerResidual * (upperTrial - lowerTrial) / denominator);
    }

    private static double? TrySecant(double firstTrial, double firstResidual, double secondTrial, double secondResidual)
    {
        var denominator = secondResidual - firstResidual;
        if (Math.Abs(denominator) <= 1e-12d)
        {
            return null;
        }

        return secondTrial - (secondResidual * (secondTrial - firstTrial) / denominator);
    }

    private static void BuildCnFromQSpec(CirclePlaneState state, QSpecProfile profile, bool preserveCurrentImaginaryOffset)
    {
        var qc = profile.Points.Select(point => point.SpeedRatio).ToArray();
        var qcSpline = new GeometryTransformUtilities.NaturalCubicSpline(state.Wc, qc);
        var wcLe = Math.PI;
        for (var index = 1; index < qc.Length; index++)
        {
            if (Math.Sign(qc[index - 1]) == Math.Sign(qc[index]))
            {
                continue;
            }

            wcLe = state.Wc[index];
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var value = qcSpline.Evaluate(wcLe);
                var derivative = EstimateSplineDerivative(qcSpline, wcLe);
                if (Math.Abs(derivative) <= 1e-12d)
                {
                    break;
                }

                var delta = Math.Clamp(-value / derivative, -0.2d, 0.2d);
                wcLe = Math.Clamp(wcLe + delta, 0d, 2d * Math.PI);
                if (Math.Abs(delta) <= 1e-8d)
                {
                    break;
                }
            }

            break;
        }

        var alphaCir = 0.5d * (wcLe - Math.PI);
        for (var index = 1; index < state.Nc - 1; index++)
        {
            var cosw = 2d * Math.Cos((0.5d * state.Wc[index]) - alphaCir);
            var sinw = 2d * Math.Sin(0.5d * state.Wc[index]);
            var sinwe = Math.Pow(Math.Max(sinw, 1e-8d), state.Agte);
            var qcValue = qc[index];
            var pfun = Math.Abs(cosw) < 1e-4d
                ? Math.Abs(sinwe / qcSpline.Evaluate(state.Wc[index]))
                : Math.Abs((cosw * sinwe) / Math.Max(Math.Abs(qcValue), 1e-8d));
            pfun = Math.Clamp(pfun, 1e-12d, 1e12d);
            state.Piq[index] = new Complex(Math.Log(pfun), 0d);
        }

        state.Piq[0] = (3d * state.Piq[1]) - (3d * state.Piq[2]) + state.Piq[3];
        state.Piq[state.Nc - 1] = (3d * state.Piq[state.Nc - 2]) - (3d * state.Piq[state.Nc - 3]) + state.Piq[state.Nc - 4];

        FourierTransform(state);
        if (!preserveCurrentImaginaryOffset)
        {
            state.QimOld = state.Cn[0].Imaginary;
        }

        state.Cn[0] = new Complex(0d, state.QimOld);
        PiqSum(state);
    }

    private static void FourierTransform(CirclePlaneState state)
    {
        for (var mode = 0; mode <= state.Mc; mode++)
        {
            var zsum = Complex.Zero;
            for (var index = 1; index < state.Nc - 1; index++)
            {
                zsum += state.Piq[index] * state.Eiw[index, mode];
            }

            state.Cn[mode] = ((0.5d * ((state.Piq[0] * state.Eiw[0, mode]) + (state.Piq[state.Nc - 1] * state.Eiw[state.Nc - 1, mode]))) + zsum) * state.Dwc / Math.PI;
        }

        state.Cn[0] *= 0.5d;
    }

    private static void PiqSum(CirclePlaneState state)
    {
        for (var index = 0; index < state.Nc; index++)
        {
            var zsum = Complex.Zero;
            for (var mode = 0; mode <= state.Mc; mode++)
            {
                zsum += state.Cn[mode] * Complex.Conjugate(state.Eiw[index, mode]);
            }

            state.Piq[index] = zsum;
        }
    }

    private static void ZcCalc(CirclePlaneState state)
    {
        var sensitivityCount = state.Mct;
        state.Zc[0] = new Complex(4d, 0d);
        for (var mode = 1; mode <= sensitivityCount; mode++)
        {
            state.ZcCn[0, mode] = Complex.Zero;
        }

        var sinw = 2d * Math.Sin(0.5d * state.Wc[0]);
        var sinwe = sinw > 0d ? Math.Pow(sinw, 1d - state.Agte) : 0d;
        var hwc = (0.5d * (state.Wc[0] - Math.PI) * (1d + state.Agte)) - (0.5d * Math.PI);
        var dzdw1 = sinwe * SafeExp(state.Piq[0], hwc);
        for (var index = 1; index < state.Nc; index++)
        {
            sinw = 2d * Math.Sin(0.5d * state.Wc[index]);
            sinwe = sinw > 0d ? Math.Pow(sinw, 1d - state.Agte) : 0d;
            hwc = (0.5d * (state.Wc[index] - Math.PI) * (1d + state.Agte)) - (0.5d * Math.PI);
            var dzdw2 = sinwe * SafeExp(state.Piq[index], hwc);

            state.Zc[index] = (0.5d * (dzdw1 + dzdw2) * state.Dwc) + state.Zc[index - 1];
            var dzPiq1 = 0.5d * dzdw1 * state.Dwc;
            var dzPiq2 = 0.5d * dzdw2 * state.Dwc;
            for (var mode = 1; mode <= sensitivityCount; mode++)
            {
                state.ZcCn[index, mode] =
                    (dzPiq1 * Complex.Conjugate(state.Eiw[index - 1, mode]))
                    + (dzPiq2 * Complex.Conjugate(state.Eiw[index, mode]))
                    + state.ZcCn[index - 1, mode];
            }

            dzdw1 = dzdw2;
        }
    }

    private static void ZcNorm(CirclePlaneState state)
    {
        var zle = FindLeadingEdge(state);
        if (!IsFinite(zle))
        {
            zle = state.ZleOld;
        }

        for (var index = 0; index < state.Nc; index++)
        {
            state.Zc[index] -= zle;
        }

        var lastIndex = state.Nc - 1;
        var zte = 0.5d * (state.Zc[0] + state.Zc[lastIndex]);
        if (!IsFinite(zte) || Complex.Abs(zte) <= 1e-10d)
        {
            zte = state.ChordZ;
        }

        var zteCn = new Complex[state.Mct + 1];
        for (var mode = 1; mode <= state.Mct; mode++)
        {
            zteCn[mode] = 0.5d * (state.ZcCn[0, mode] + state.ZcCn[lastIndex, mode]);
        }

        for (var index = 0; index < state.Nc; index++)
        {
            var zcNew = state.ChordZ * state.Zc[index] / zte;
            var zcZte = -zcNew / zte;
            state.Zc[index] = zcNew;
            for (var mode = 1; mode <= state.Mct; mode++)
            {
                state.ZcCn[index, mode] = (state.ChordZ * state.ZcCn[index, mode] / zte) + (zcZte * zteCn[mode]);
            }
        }

        var ratio = state.ChordZ / zte;
        var qimoff = IsFinite(ratio) ? -Complex.Log(ratio).Imaginary : 0d;
        state.Cn[0] -= Complex.ImaginaryOne * qimoff;
        for (var index = 0; index < state.Nc; index++)
        {
            state.Zc[index] += state.ZleOld;
        }
    }

    private static Complex FindLeadingEdge(CirclePlaneState state)
    {
        var zte = 0.5d * (state.Zc[0] + state.Zc[state.Nc - 1]);
        var leIndex = 0;
        var maxDistance = 0d;
        for (var index = 0; index < state.Nc; index++)
        {
            var distance = Complex.Abs(state.Zc[index] - zte);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                leIndex = index;
            }
        }

        const int localCount = 33;
        var start = Math.Max(leIndex - ((localCount - 1) / 2), 0);
        var end = Math.Min(leIndex + ((localCount - 1) / 2), state.Nc - 1);
        var count = end - start + 1;
        var wc = new double[count];
        var xc = new double[count];
        var yc = new double[count];
        for (var index = start; index <= end; index++)
        {
            var local = index - start;
            wc[local] = state.Wc[index];
            xc[local] = state.Zc[index].Real;
            yc[local] = state.Zc[index].Imaginary;
        }

        var dzdwStart = EvaluateDzDw(state, start);
        var dzdwEnd = EvaluateDzDw(state, end);
        var xSpline = new GeometryTransformUtilities.NaturalCubicSpline(wc, xc, dzdwStart.Real, dzdwEnd.Real);
        var ySpline = new GeometryTransformUtilities.NaturalCubicSpline(wc, yc, dzdwStart.Imaginary, dzdwEnd.Imaginary);
        var wcLe = state.Wc[leIndex];
        var xte = zte.Real;
        var yte = zte.Imaginary;
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var xle = xSpline.Evaluate(wcLe);
            var yle = ySpline.Evaluate(wcLe);
            var dxdw = EstimateSplineDerivative(xSpline, wcLe);
            var dydw = EstimateSplineDerivative(ySpline, wcLe);
            var dxdd = EstimateSplineSecondDerivative(xSpline, wcLe);
            var dydd = EstimateSplineSecondDerivative(ySpline, wcLe);
            var xChord = xle - xte;
            var yChord = yle - yte;
            var residual = (xChord * dxdw) + (yChord * dydw);
            var residualW = (dxdw * dxdw) + (dydw * dydw) + (xChord * dxdd) + (yChord * dydd);
            if (Math.Abs(residualW) <= 1e-12d)
            {
                break;
            }

            var delta = -residual / residualW;
            wcLe = Math.Clamp(wcLe + delta, wc[0], wc[^1]);
            if (Math.Abs(delta) <= 1e-5d)
            {
                break;
            }
        }

        return new Complex(xSpline.Evaluate(wcLe), ySpline.Evaluate(wcLe));
    }

    private static Complex EvaluateDzDw(CirclePlaneState state, int index)
    {
        var sinw = 2d * Math.Sin(0.5d * state.Wc[index]);
        var sinwe = sinw > 0d ? Math.Pow(sinw, 1d - state.Agte) : 0d;
        var hwc = (0.5d * (state.Wc[index] - Math.PI) * (1d + state.Agte)) - (0.5d * Math.PI);
        return sinwe * SafeExp(state.Piq[index], hwc);
    }

    private static Complex SafeExp(Complex piq, double hwc)
    {
        var clampedReal = Math.Clamp(piq.Real, -50d, 50d);
        var imag = IsFinite(piq) ? piq.Imaginary + hwc : hwc;
        return Complex.Exp(new Complex(clampedReal, imag));
    }

    private static bool IsFinite(Complex value)
    {
        return double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
    }

    private static bool AreFinite(IReadOnlyList<Complex> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (!IsFinite(values[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static CirclePlaneStateSnapshot CaptureState(CirclePlaneState state)
    {
        return new CirclePlaneStateSnapshot(
            state.Cn.ToArray(),
            state.Piq.ToArray(),
            state.Zc.ToArray(),
            (Complex[,])state.ZcCn.Clone());
    }

    private static void RestoreState(CirclePlaneState state, CirclePlaneStateSnapshot snapshot)
    {
        Array.Copy(snapshot.Cn, state.Cn, snapshot.Cn.Length);
        Array.Copy(snapshot.Piq, state.Piq, snapshot.Piq.Length);
        Array.Copy(snapshot.Zc, state.Zc, snapshot.Zc.Length);
        Array.Copy(snapshot.ZcCn, state.ZcCn, snapshot.ZcCn.Length);
    }

    private static double EstimateSplineDerivative(GeometryTransformUtilities.NaturalCubicSpline spline, double parameter)
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

    private static double EstimateSplineSecondDerivative(GeometryTransformUtilities.NaturalCubicSpline spline, double parameter)
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
        var halfWidth = 0.5d * (upper - lower);
        return (left - (2d * center) + right) / (halfWidth * halfWidth);
    }

    private static QSpecProfile ResampleProfile(QSpecProfile profile, int pointCount)
    {
        var parameters = profile.Points.Select(point => point.SurfaceCoordinate).ToArray();
        var xSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.Location.X).ToArray());
        var ySpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.Location.Y).ToArray());
        var qSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.SpeedRatio).ToArray());
        var cpSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.PressureCoefficient).ToArray());
        var cpCorrectedSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.CorrectedPressureCoefficient).ToArray());

        var points = new QSpecPoint[pointCount];
        for (var index = 0; index < pointCount; index++)
        {
            var s = (double)index / (pointCount - 1);
            points[index] = new QSpecPoint(
                index,
                s,
                1d - s,
                new AirfoilPoint(xSpline.Evaluate(s), ySpline.Evaluate(s)),
                qSpline.Evaluate(s),
                cpSpline.Evaluate(s),
                cpCorrectedSpline.Evaluate(s));
        }

        return new QSpecProfile(profile.Name, profile.AngleOfAttackDegrees, profile.MachNumber, points);
    }

    private static double ComputeTrailingEdgeAngleDegrees(IReadOnlyList<AirfoilPoint> points)
    {
        var firstDerivative = new AirfoilPoint(
            points[1].X - points[0].X,
            points[1].Y - points[0].Y);
        var lastDerivative = new AirfoilPoint(
            points[^1].X - points[^2].X,
            points[^1].Y - points[^2].Y);
        return
            ((Math.Atan2(lastDerivative.X, -lastDerivative.Y) - Math.Atan2(firstDerivative.X, -firstDerivative.Y)) / Math.PI - 1d)
            * 180d;
    }

    private static void ApplyHanningFilter(IList<Complex> coefficients, double filterExponent)
    {
        if (filterExponent == 0d || coefficients.Count <= 1)
        {
            return;
        }

        var maxMode = coefficients.Count - 1;
        for (var mode = 0; mode <= maxMode; mode++)
        {
            var frequency = (double)mode / maxMode;
            var weight = 0.5d * (1d + Math.Cos(Math.PI * frequency));
            if (filterExponent > 0d)
            {
                weight = Math.Pow(weight, filterExponent);
            }

            coefficients[mode] *= weight;
        }
    }

    private static double ClampTrailingEdgeAngle(double angleDegrees)
    {
        return Math.Clamp(angleDegrees, -25d, 25d);
    }

    private sealed class CirclePlaneState
    {
        private CirclePlaneState(
            int nc,
            int mc,
            int mct,
            double dwc,
            double[] wc,
            Complex[,] eiw,
            Complex[] piq,
            Complex[] cn,
            Complex[] zc,
            Complex[,] zcCn,
            double agte,
            Complex dzte,
            Complex chordZ,
            Complex zleOld,
            double qimOld)
        {
            Nc = nc;
            Mc = mc;
            Mct = mct;
            Dwc = dwc;
            Wc = wc;
            Eiw = eiw;
            Piq = piq;
            Cn = cn;
            Zc = zc;
            ZcCn = zcCn;
            Agte = agte;
            Dzte = dzte;
            ChordZ = chordZ;
            ZleOld = zleOld;
            QimOld = qimOld;
        }

        public int Nc { get; }

        public int Mc { get; }

        public int Mct { get; }

        public double Dwc { get; }

        public double[] Wc { get; }

        public Complex[,] Eiw { get; }

        public Complex[] Piq { get; }

        public Complex[] Cn { get; }

        public Complex[] Zc { get; }

        public Complex[,] ZcCn { get; }

        public double Agte { get; }

        public Complex Dzte { get; }

        public Complex ChordZ { get; }

        public Complex ZleOld { get; }

        public double QimOld { get; set; }

        public static CirclePlaneState Create(
            AirfoilGeometry geometry,
            QSpecProfile profile,
            AirfoilPoint? targetTrailingEdgeGap,
            double? targetTrailingEdgeAngleDegrees)
        {
            var frame = GeometryTransformUtilities.BuildChordFrame(geometry.Points);
            var leadingEdge = frame.LeadingEdge;
            var trailingEdge = frame.TrailingEdge;
            var dzte = targetTrailingEdgeGap is null
                ? new Complex(
                    geometry.Points[0].X - geometry.Points[^1].X,
                    geometry.Points[0].Y - geometry.Points[^1].Y)
                : new Complex(targetTrailingEdgeGap.Value.X, targetTrailingEdgeGap.Value.Y);
            var chordZ = new Complex(trailingEdge.X - leadingEdge.X, trailingEdge.Y - leadingEdge.Y);

            var firstDerivative = new AirfoilPoint(
                geometry.Points[1].X - geometry.Points[0].X,
                geometry.Points[1].Y - geometry.Points[0].Y);
            var lastDerivative = new AirfoilPoint(
                geometry.Points[^1].X - geometry.Points[^2].X,
                geometry.Points[^1].Y - geometry.Points[^2].Y);
            var agte = targetTrailingEdgeAngleDegrees.HasValue
                ? targetTrailingEdgeAngleDegrees.Value / 180d
                : (Math.Atan2(lastDerivative.X, -lastDerivative.Y)
                    - Math.Atan2(firstDerivative.X, -firstDerivative.Y))
                    / Math.PI - 1d;

            var nc = profile.Points.Count;
            var mc = nc / 4;
            var mct = Math.Max(1, nc / 16);
            var dwc = (2d * Math.PI) / (nc - 1);
            var wc = new double[nc];
            var eiw = new Complex[nc, mc + 1];
            for (var index = 0; index < nc; index++)
            {
                wc[index] = dwc * index;
                eiw[index, 0] = Complex.One;
            }

            if (mc >= 1)
            {
                for (var index = 0; index < nc; index++)
                {
                    eiw[index, 1] = Complex.Exp(Complex.ImaginaryOne * wc[index]);
                }

                for (var mode = 2; mode <= mc; mode++)
                {
                    for (var index = 0; index < nc; index++)
                    {
                        var mappedIndex = ((mode * index) % (nc - 1));
                        eiw[index, mode] = eiw[mappedIndex, 1];
                    }
                }
            }

            return new CirclePlaneState(
                nc,
                mc,
                mct,
                dwc,
                wc,
                eiw,
                new Complex[nc],
                new Complex[mc + 1],
                new Complex[nc],
                new Complex[nc, mct + 1],
                agte,
                dzte,
                chordZ,
                new Complex(leadingEdge.X, leadingEdge.Y),
                0d);
        }
    }

    private sealed class CirclePlaneStateSnapshot
    {
        public CirclePlaneStateSnapshot(Complex[] cn, Complex[] piq, Complex[] zc, Complex[,] zcCn)
        {
            Cn = cn;
            Piq = piq;
            Zc = zc;
            ZcCn = zcCn;
        }

        public Complex[] Cn { get; }

        public Complex[] Piq { get; }

        public Complex[] Zc { get; }

        public Complex[,] ZcCn { get; }
    }

    private const double TrailingEdgeAngleToleranceDegrees = 0.25d;
    private const double DefaultTrailingEdgeAngleRetentionToleranceDegrees = 0.75d;

    private static readonly double[] DampingSchedule = new[] { 1d, 0.5d, 0.25d, 0.125d, 0.0625d, 0.03125d };
}
