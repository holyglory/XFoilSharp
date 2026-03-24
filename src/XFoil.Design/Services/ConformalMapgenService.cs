using System.Numerics;
using XFoil.Core.Models;
using XFoil.Design.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xmdes.f :: MAPGEN/CNCALC/PIQSUM/ZCCALC/ZCNORM/ZLEFIND
// Secondary legacy source: f_xfoil/src/xgeom.f :: LEFIND, f_xfoil/src/spline.f :: SPLIND/SEVAL
// Role in port: Replays the conformal-map inverse-design workflow behind the legacy MAPGEN command with managed data structures and convergence guards.
// Differences: The managed path breaks the monolithic MDES/MAPGEN flow into explicit state objects, wrapper heuristics, and finite-value guards while keeping the core conformal-map algebra close to the legacy routines.
// Decision: Keep the managed refactor around the core map algebra because it improves diagnosability; preserve the legacy formulas inside the conformal-map kernels.
namespace XFoil.Design.Services;

public sealed class ConformalMapgenService
{
    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN entry flow.
    // Difference from legacy: This overload is a managed convenience wrapper that reuses the same profile as both baseline and target instead of relying on session-state defaults.
    // Decision: Keep the managed-only overload because it simplifies library use without changing the core algorithm.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN command orchestration.
    // Difference from legacy: The managed entry point makes the trailing-edge-angle retention and target-selection logic explicit instead of mixing it into the interactive MDES command loop.
    // Decision: Keep the managed refactor because the control flow is clearer and the core MAPGEN algebra stays below in dedicated helpers.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN trailing-edge constraint iteration lineage.
    // Difference from legacy: The managed implementation wraps MAPGEN in an explicit bracketed solve for the requested trailing-edge angle, while the original workflow handled such adjustments through the interactive command path.
    // Decision: Keep the managed improvement because it gives the library API a deterministic target-angle solve.
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

        // Legacy block: Managed-only outer solve that wraps MAPGEN in a bracketed trailing-edge-angle target search.
        // Difference: The legacy command path did not expose this reusable library-style secant/false-position wrapper.
        // Decision: Keep the managed improvement because it isolates the target-angle logic from the conformal-map core.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN.
    // Difference from legacy: The managed port first tries the direct solve and then falls back to an explicit continuation wrapper rather than relying on command-session retries.
    // Decision: Keep the managed refactor because the fallback policy is explicit and testable.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN convergence-management lineage.
    // Difference from legacy: This continuation wrapper is a managed enhancement that stages the target profile and filter strength to improve robustness.
    // Decision: Keep the managed improvement because it is clearly outside the legacy core and improves solver usability.
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
        // Legacy block: Managed-only continuation stages around repeated MAPGEN calls.
        // Difference: The original MDES/MAPGEN workflow did not expose this stepped blend loop as a reusable abstraction.
        // Decision: Keep the managed improvement because it reduces hard failures on large profile deltas.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN core solve.
    // Difference from legacy: The core conformal-map equations follow the legacy routine closely, but the managed port wraps them in explicit state objects, residual tracking, and finite-value guards.
    // Decision: Keep the managed refactor around the legacy formulas because it makes the Newton loop diagnosable without changing the core math.
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
        // Legacy block: MAPGEN Newton iteration on the geometric trailing-edge constraint.
        // Difference: The managed port keeps the same residual/correction structure but adds snapshot-based damping retries and explicit finite-value checks.
        // Decision: Keep the managed refactor because it preserves the legacy solve while making failure modes explicit.
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
            // Legacy block: Managed-only damping schedule around one MAPGEN correction step.
            // Difference: The original routine applied the correction directly, while the port retries relaxed steps to avoid blowing up the conformal map.
            // Decision: Keep the managed improvement because it increases robustness without changing accepted full steps.
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

    // Legacy mapping: none; managed-only result-wrapping helper around MAPGEN outputs.
    // Difference from legacy: The interactive code reported targets implicitly through session state, while the managed API normalizes the reported target values in a dedicated helper.
    // Decision: Keep the managed-only wrapper because it keeps result-object construction centralized.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN convergence acceptance lineage.
    // Difference from legacy: The managed port makes the acceptance threshold explicit instead of relying on routine-local stop conditions only.
    // Decision: Keep the managed refactor because it clarifies why continuation is or is not invoked.
    private static bool ShouldAcceptDirectResult(ConformalMapgenResult result, double convergenceTolerance)
    {
        return result.Converged || result.FinalTrailingEdgeResidual <= Math.Max(10d * convergenceTolerance, 1e-3d);
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN retry-selection lineage.
    // Difference from legacy: The managed wrapper compares result quality explicitly across retries instead of relying on the last session state.
    // Decision: Keep the managed refactor because it makes retry selection deterministic.
    private static bool IsBetterResult(ConformalMapgenResult candidate, ConformalMapgenResult baseline)
    {
        if (candidate.Converged != baseline.Converged)
        {
            return candidate.Converged;
        }

        return candidate.FinalTrailingEdgeResidual < baseline.FinalTrailingEdgeResidual;
    }

    // Legacy mapping: none directly; managed-only continuation heuristic around MAPGEN.
    // Difference from legacy: Stage count is inferred from the requested Qspec delta rather than from user-driven retries.
    // Decision: Keep the managed-only heuristic because it exists purely to stage the library workflow.
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

    // Legacy mapping: none directly; managed-only continuation heuristic.
    // Difference from legacy: The initial filter strength is chosen automatically for staged MAPGEN retries instead of via the interactive filter controls alone.
    // Decision: Keep the managed-only heuristic because it improves automated robustness.
    private static double DetermineInitialContinuationFilter(double requestedFilterExponent)
    {
        return requestedFilterExponent > 0d
            ? Math.Max(requestedFilterExponent, 1.5d)
            : 1.5d;
    }

    // Legacy mapping: none directly; managed-only continuation helper around MAPGEN.
    // Difference from legacy: The blended profile is an explicit interpolant between baseline and target Qspec data, which the original session workflow did not materialize as a first-class object.
    // Decision: Keep the managed-only helper because it enables deterministic continuation stages.
    private static QSpecProfile BlendProfiles(QSpecProfile baselineProfile, QSpecProfile targetProfile, double fraction)
    {
        var clampedFraction = Math.Clamp(fraction, 0d, 1d);
        var points = new QSpecPoint[baselineProfile.Points.Count];
        // Legacy block: Managed-only profile interpolation used to stage MAPGEN continuation calls.
        // Difference: This interpolation object does not exist as a standalone artifact in the legacy command flow.
        // Decision: Keep the managed-only loop because it supports the continuation wrapper.
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

    // Legacy mapping: none; managed-only bracket housekeeping for the target-angle wrapper.
    // Difference from legacy: The interactive code did not expose this helper because the bracketed solve itself is a managed addition.
    // Decision: Keep the managed-only helper.
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

    // Legacy mapping: none directly; managed-only root-finding helper around MAPGEN retries.
    // Difference from legacy: The false-position step is part of the managed trailing-edge-angle wrapper, not a standalone Fortran routine.
    // Decision: Keep the managed-only helper.
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

    // Legacy mapping: none directly; managed-only root-finding helper around MAPGEN retries.
    // Difference from legacy: The secant step is part of the managed target-angle wrapper rather than the original interactive workflow.
    // Decision: Keep the managed-only helper.
    private static double? TrySecant(double firstTrial, double firstResidual, double secondTrial, double secondResidual)
    {
        var denominator = secondResidual - firstResidual;
        if (Math.Abs(denominator) <= 1e-12d)
        {
            return null;
        }

        return secondTrial - (secondResidual * (secondTrial - firstTrial) / denominator);
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: CNCALC.
    // Difference from legacy: The managed port expresses the Qspec-to-conformal-coefficient conversion through explicit spline helpers and state-object fields instead of through COMMON arrays.
    // Decision: Keep the managed refactor because the conformal-map algebra remains the same while the data flow is clearer.
    private static void BuildCnFromQSpec(CirclePlaneState state, QSpecProfile profile, bool preserveCurrentImaginaryOffset)
    {
        var qc = profile.Points.Select(point => point.SpeedRatio).ToArray();
        var qcSpline = new GeometryTransformUtilities.NaturalCubicSpline(state.Wc, qc);
        var wcLe = Math.PI;
        // Legacy block: CNCALC leading-edge parameter search from the sign change in Qspec.
        // Difference: The managed helper performs the same search/refinement through a local spline wrapper instead of through the original work arrays.
        // Decision: Keep the managed refactor because the search logic is explicit.
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
        // Legacy block: CNCALC assembly of the logarithmic `PIQ` field from the requested surface-speed distribution.
        // Difference: The port names the intermediate trigonometric factors explicitly and stores them in the managed state object.
        // Decision: Keep the managed refactor because the legacy formula is preserved.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: CNCALC Fourier coefficient accumulation.
    // Difference from legacy: The managed helper names the transform explicitly as a method instead of leaving it embedded in CNCALC.
    // Decision: Keep the managed refactor because it isolates one legacy step cleanly.
    private static void FourierTransform(CirclePlaneState state)
    {
        // Legacy block: CNCALC modal accumulation from the `PIQ` samples.
        // Difference: The summation is unchanged, but the managed code isolates it as a dedicated transform helper.
        // Decision: Keep the equivalent managed loop.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: PIQSUM.
    // Difference from legacy: The inverse summation is expressed as a dedicated method on the managed state object instead of through shared arrays and COMMON blocks.
    // Decision: Keep the managed refactor because the algebra is unchanged and the state ownership is clearer.
    private static void PiqSum(CirclePlaneState state)
    {
        // Legacy block: PIQSUM inverse Fourier reconstruction of `PIQ` from `Cn`.
        // Difference: The summation is unchanged; the port simply scopes it inside the state object.
        // Decision: Keep the equivalent managed loop.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: ZCCALC.
    // Difference from legacy: The port preserves the conformal-map marching formulas but isolates them behind a state object and explicit sensitivity count.
    // Decision: Keep the managed refactor because the geometric integration remains true to the legacy routine.
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
        // Legacy block: ZCCALC marching integration of the complex geometry and its sensitivities.
        // Difference: The formulas are preserved, but the managed code stores the running geometry in an object-owned state instead of shared arrays.
        // Decision: Keep the managed refactor because it keeps the legacy map integration intact.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: ZCNORM.
    // Difference from legacy: The normalization algebra is preserved, but finite-value fallbacks are made explicit in the managed wrapper.
    // Decision: Keep the managed refactor because it preserves the legacy normalization while making failure handling visible.
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
        // Legacy block: ZCNORM restoration of the stored leading-edge origin after chord normalization.
        // Difference: The port keeps the same final recentering step but does it through the managed state object.
        // Decision: Keep the equivalent step.
        for (var index = 0; index < state.Nc; index++)
        {
            state.Zc[index] += state.ZleOld;
        }
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: ZLEFIND.
    // Difference from legacy: The managed port keeps the same local-spline refinement idea but expresses it through reusable spline helpers and explicit derivative estimates.
    // Decision: Keep the managed refactor because the leading-edge solve remains close to the legacy routine while being easier to inspect.
    private static Complex FindLeadingEdge(CirclePlaneState state)
    {
        var zte = 0.5d * (state.Zc[0] + state.Zc[state.Nc - 1]);
        var leIndex = 0;
        var maxDistance = 0d;
        // Legacy block: ZLEFIND coarse search for the furthest point from the trailing-edge midpoint.
        // Difference: The same scan is written directly against the managed state arrays.
        // Decision: Keep the equivalent loop.
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
        // Legacy block: ZLEFIND Newton refinement of the leading-edge parameter on the local spline patch.
        // Difference: The residual terms are named explicitly and the spline helpers are shared utilities instead of local Fortran arrays.
        // Decision: Keep the managed refactor because the same geometric condition is solved more transparently.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: ZCCALC differential kernel.
    // Difference from legacy: The algebra is unchanged; it is isolated in a helper for reuse by ZCCALC and ZLEFIND.
    // Decision: Keep the equivalent helper.
    private static Complex EvaluateDzDw(CirclePlaneState state, int index)
    {
        var sinw = 2d * Math.Sin(0.5d * state.Wc[index]);
        var sinwe = sinw > 0d ? Math.Pow(sinw, 1d - state.Agte) : 0d;
        var hwc = (0.5d * (state.Wc[index] - Math.PI) * (1d + state.Agte)) - (0.5d * Math.PI);
        return sinwe * SafeExp(state.Piq[index], hwc);
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: complex exponential inside MAPGEN/CNCALC.
    // Difference from legacy: The managed helper clamps the exponential real part to avoid overflow, which is a defensive addition not present in the original code.
    // Decision: Keep the managed improvement because it only changes pathological overflow behavior.
    private static Complex SafeExp(Complex piq, double hwc)
    {
        var clampedReal = Math.Clamp(piq.Real, -50d, 50d);
        var imag = IsFinite(piq) ? piq.Imaginary + hwc : hwc;
        return Complex.Exp(new Complex(clampedReal, imag));
    }

    // Legacy mapping: none; managed-only numeric guard supporting MAPGEN.
    // Difference from legacy: The original code assumed finite floating-point state, while the port guards intermediate complex values explicitly.
    // Decision: Keep the managed-only helper because it makes failure handling explicit.
    private static bool IsFinite(Complex value)
    {
        return double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
    }

    // Legacy mapping: none; managed-only numeric guard supporting MAPGEN.
    // Difference from legacy: The helper scans collections for invalid complex values, which was not factored out in the original routine.
    // Decision: Keep the managed-only helper.
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

    // Legacy mapping: none; managed-only Newton-step bookkeeping around MAPGEN.
    // Difference from legacy: The original routine updated the conformal-map state in place without snapshot rollback helpers.
    // Decision: Keep the managed-only helper because it supports the damping retries cleanly.
    private static CirclePlaneStateSnapshot CaptureState(CirclePlaneState state)
    {
        return new CirclePlaneStateSnapshot(
            state.Cn.ToArray(),
            state.Piq.ToArray(),
            state.Zc.ToArray(),
            (Complex[,])state.ZcCn.Clone());
    }

    // Legacy mapping: none; managed-only Newton-step bookkeeping around MAPGEN.
    // Difference from legacy: This helper restores the captured state when a damped step is rejected, which has no direct standalone Fortran analogue.
    // Decision: Keep the managed-only helper because it is required by the managed damping wrapper.
    private static void RestoreState(CirclePlaneState state, CirclePlaneStateSnapshot snapshot)
    {
        Array.Copy(snapshot.Cn, state.Cn, snapshot.Cn.Length);
        Array.Copy(snapshot.Piq, state.Piq, snapshot.Piq.Length);
        Array.Copy(snapshot.Zc, state.Zc, snapshot.Zc.Length);
        Array.Copy(snapshot.ZcCn, state.ZcCn, snapshot.ZcCn.Length);
    }

    // Legacy mapping: f_xfoil/src/xmdes.f :: ZLEFIND spline-derivative support.
    // Difference from legacy: The managed implementation estimates derivatives numerically from the shared spline utility instead of using routine-local derivative arrays.
    // Decision: Keep the managed improvement because it avoids additional mutable spline state.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: ZLEFIND spline-second-derivative support.
    // Difference from legacy: The second derivative is reconstructed numerically from the shared spline helper rather than through dedicated cubic-coefficient storage.
    // Decision: Keep the managed improvement because it is sufficient for the leading-edge refinement.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: CNCALC/MAPGEN profile resampling lineage.
    // Difference from legacy: The managed helper materializes a uniformly parameterized Qspec profile object instead of rebuilding several parallel arrays.
    // Decision: Keep the managed refactor because the resampled state is easier to reuse.
    private static QSpecProfile ResampleProfile(QSpecProfile profile, int pointCount)
    {
        var parameters = profile.Points.Select(point => point.SurfaceCoordinate).ToArray();
        var xSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.Location.X).ToArray());
        var ySpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.Location.Y).ToArray());
        var qSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.SpeedRatio).ToArray());
        var cpSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.PressureCoefficient).ToArray());
        var cpCorrectedSpline = new GeometryTransformUtilities.NaturalCubicSpline(parameters, profile.Points.Select(point => point.CorrectedPressureCoefficient).ToArray());

        var points = new QSpecPoint[pointCount];
        // Legacy block: MAPGEN/CNCALC-style uniform resampling of the profile data.
        // Difference: The managed port emits immutable `QSpecPoint` objects instead of parallel resampled arrays.
        // Decision: Keep the managed refactor because the resampled data is explicit.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN trailing-edge angle measurement.
    // Difference from legacy: The angle extraction is factored into a reusable helper instead of being embedded in result reporting.
    // Decision: Keep the equivalent helper.
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

    // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN filter control (`FFILT`) lineage.
    // Difference from legacy: The managed port applies an explicit Hanning weight to the coefficients in a dedicated helper rather than inside the command routine.
    // Decision: Keep the managed refactor because it isolates one optional conditioning step.
    private static void ApplyHanningFilter(IList<Complex> coefficients, double filterExponent)
    {
        if (filterExponent == 0d || coefficients.Count <= 1)
        {
            return;
        }

        var maxMode = coefficients.Count - 1;
        // Legacy block: MAPGEN modal filtering prior to geometry regeneration.
        // Difference: The port names the Hanning weighting explicitly instead of letting the filter logic remain implicit in the routine body.
        // Decision: Keep the managed refactor because the filter intent is clearer.
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

    // Legacy mapping: none directly; managed-only input guard for the target-angle wrapper.
    // Difference from legacy: The original command workflow accepted user inputs interactively, while the library API clamps them to a safe range in one place.
    // Decision: Keep the managed-only helper because it protects the automated solver entry point.
    private static double ClampTrailingEdgeAngle(double angleDegrees)
    {
        return Math.Clamp(angleDegrees, -25d, 25d);
    }

    private sealed class CirclePlaneState
    {
        // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN/CNCALC state allocation.
        // Difference from legacy: The managed port groups the conformal-map state into one object instead of spreading it across COMMON arrays and local workspaces.
        // Decision: Keep the managed refactor because it makes ownership of the conformal-map state explicit.
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

        // Legacy mapping: f_xfoil/src/xmdes.f :: MAPGEN/CNCALC initialization.
        // Difference from legacy: The same initial state is assembled into a managed object with explicit geometry-derived fields instead of through the interactive MDES session variables.
        // Decision: Keep the managed refactor because the conformal-map initialization is easier to inspect.
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
            // Legacy block: MAPGEN/CNCALC setup of the circle-plane parameter grid and modal basis.
            // Difference: The managed code names the basis arrays explicitly and owns them inside the state object.
            // Decision: Keep the equivalent initialization loop.
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
        // Legacy mapping: none; managed-only rollback snapshot for damped MAPGEN steps.
        // Difference from legacy: The Fortran code updated state in place without a dedicated snapshot object.
        // Decision: Keep the managed-only helper because it supports the damping wrapper cleanly.
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
