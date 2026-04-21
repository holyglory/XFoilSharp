using System.Collections.Generic;
using XFoil.Core.Models;
using XFoil.Solver.Models;

// Legacy audit:
// Primary legacy source: f_xfoil/src/xoper.f :: SPECAL/SPECCL operating-point façade
// Secondary legacy source: f_xfoil/src/xfoil.f :: MRCL; f_xfoil/src/xpanel.f :: GGCALC/PSILIN lineages via delegated services
// Role in port: Provides the high-level managed analysis façade for inviscid, diagnostic viscous-prep, and Newton-coupled viscous workflows.
// Differences: The file is a managed orchestration layer with no single direct Fortran analogue; it composes multiple solver services, exposes obsolete compatibility shims, and packages results into explicit .NET objects rather than relying on the interactive XFoil command state.
// Decision: Keep the façade because it defines the supported public API surface, while documenting which paths are current, diagnostic-only, or compatibility-only.
using XFoil.Solver.Services;
namespace XFoil.Solver.Double.Services;

/// <summary>
/// Primary entry point for airfoil analysis. Supports inviscid analysis (Hess-Smith
/// and linear-vortex solvers) and viscous analysis via the Newton-coupled BL solver.
///
/// The surrogate viscous pipeline (ViscousLaminarSolver, ViscousInteractionCoupler,
/// EdgeVelocityFeedbackBuilder, etc.) has been replaced by the full Newton solver
/// (ViscousSolverEngine + PolarSweepRunner). The old surrogate-pipeline methods
/// now throw NotSupportedException directing callers to the Newton path.
/// </summary>
public class AirfoilAnalysisService : XFoil.Solver.Services.IAirfoilAnalysisService
{
    public AirfoilAnalysisService()
    {
    }

    // ================================================================
    // Inviscid analysis (unchanged)
    // ================================================================

    // Legacy mapping: managed façade over f_xfoil/src/xoper.f :: SPECAL/SPECCL lineage plus the alternative linear-vortex path.
    // Difference from legacy: The managed API can dispatch between Hess-Smith and linear-vortex inviscid implementations through settings instead of a single monolithic OPER path.
    // Decision: Keep the dispatching façade because it exposes the supported inviscid runtime choices clearly.
    public virtual InviscidAnalysisResult AnalyzeInviscid(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();
        return AnalyzeInviscidLinearVortex(geometry, angleOfAttackDegrees, settings);
    }

    // ================================================================
    // Viscous analysis via Newton solver (new API)
    // ================================================================

    /// <summary>
    /// Runs a single-point viscous analysis using the Newton-coupled BL solver
    /// (ViscousSolverEngine). This is the primary viscous analysis entry point.
    /// </summary>
    /// <param name="geometry">Airfoil geometry.</param>
    /// <param name="angleOfAttackDegrees">Angle of attack in degrees.</param>
    /// <param name="settings">Analysis settings (Re, Mach, NCrit, etc.).</param>
    /// <returns>Full viscous analysis result with CL, CD, CM, drag decomposition, BL profiles.</returns>
    // Legacy mapping: f_xfoil/src/xoper.f :: operating-point viscous solve lineage through VISCAL.
    // Difference from legacy: The façade extracts coordinates and delegates to ViscousSolverEngine instead of embedding the full operating-point loop itself.
    // Decision: Keep the thin wrapper because it defines the supported single-point viscous API cleanly.
    public virtual ViscousAnalysisResult AnalyzeViscous(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        var coords = ExtractCoordinates(geometry);
        double alphaRadians;
        if (settings.UseLegacyBoundaryLayerInitialization)
        {
            // Fortran: ALFA = ADEG * DTOR where DTOR = ACOS(-1.0)/180.0 in REAL.
            // Use double DTOR to match Fortran's double-precision alpha.
            double dtor = Math.Acos(-1.0d) / 180.0d;
            alphaRadians = (double)angleOfAttackDegrees * dtor;
        }
        else
        {
            alphaRadians = angleOfAttackDegrees * Math.PI / 180.0;
        }

        return ViscousSolverEngine.SolveViscous(coords, settings, alphaRadians);
    }

    /// <summary>
    /// Performs an alpha sweep using the Newton-coupled viscous solver (Type 1 polar).
    /// Delegates to PolarSweepRunner.SweepAlpha with warm-start between points.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xoper.f :: Type-1 polar sweep lineage.
    // Difference from legacy: The actual sweep logic lives in PolarSweepRunner and returns managed result objects.
    // Decision: Keep the wrapper because it preserves a small public façade.
    public virtual List<ViscousAnalysisResult> SweepViscousAlpha(
        AirfoilGeometry geometry,
        double alphaStartDegrees,
        double alphaEndDegrees,
        double alphaStepDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        var coords = ExtractCoordinates(geometry);
        return PolarSweepRunner.SweepAlpha(
            coords, settings, alphaStartDegrees, alphaEndDegrees, alphaStepDegrees);
    }

    /// <summary>
    /// Performs a CL sweep using the Newton-coupled viscous solver (Type 2 polar).
    /// Delegates to PolarSweepRunner.SweepCL.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xoper.f :: Type-2 polar sweep lineage.
    // Difference from legacy: Delegates to the managed sweep runner instead of owning the loop locally.
    // Decision: Keep the wrapper because it exposes the public CL-sweep API cleanly.
    public virtual List<ViscousAnalysisResult> SweepViscousCL(
        AirfoilGeometry geometry,
        double clStart,
        double clEnd,
        double clStep,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        var coords = ExtractCoordinates(geometry);
        return PolarSweepRunner.SweepCL(coords, settings, clStart, clEnd, clStep);
    }

    /// <summary>
    /// Performs a Reynolds number sweep at fixed CL (Type 3 polar).
    /// Delegates to PolarSweepRunner.SweepRe.
    /// </summary>
    // Legacy mapping: f_xfoil/src/xoper.f :: Type-3 polar sweep lineage with f_xfoil/src/xfoil.f :: MRCL semantics.
    // Difference from legacy: The actual loop and reduced MRCL handling live in PolarSweepRunner.
    // Decision: Keep the wrapper because it keeps the public API small.
    public virtual List<ViscousAnalysisResult> SweepViscousRe(
        AirfoilGeometry geometry,
        double fixedCL,
        double reStart,
        double reEnd,
        double reStep,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        settings ??= new AnalysisSettings();

        var coords = ExtractCoordinates(geometry);
        return PolarSweepRunner.SweepRe(coords, settings, fixedCL, reStart, reEnd, reStep);
    }

    // ================================================================
    // Inviscid sweep methods (unchanged)
    // ================================================================

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECAL repeated over an alpha range.
    // Difference from legacy: The sweep uses the prepared Hess-Smith system and stores managed polar points explicitly.
    // Decision: Keep the managed sweep because it cleanly exposes the current public inviscid alpha-polar API.
    public virtual PolarSweepResult SweepInviscidAlpha(
        AirfoilGeometry geometry,
        double alphaStartDegrees,
        double alphaEndDegrees,
        double alphaStepDegrees,
        AnalysisSettings? settings = null)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (Math.Abs(alphaStepDegrees) < 1e-12)
        {
            throw new ArgumentException("Alpha step must be non-zero.", nameof(alphaStepDegrees));
        }

        settings ??= new AnalysisSettings();
        var step = NormalizeStep(alphaStartDegrees, alphaEndDegrees, alphaStepDegrees);
        var points = new List<PolarPoint>();

        for (var alpha = alphaStartDegrees; ShouldContinue(alpha, alphaEndDegrees, step); alpha += step)
        {
            var result = AnalyzeInviscidLinearVortex(geometry, alpha, settings);
            points.Add(ToPolarPoint(result));
        }

        return new PolarSweepResult(geometry, settings, points);
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECCL repeated over a CL range.
    // Difference from legacy: The sweep iterates through the prepared managed system and packages the results as explicit target-lift records.
    // Decision: Keep the managed implementation because it fits the public API surface cleanly.
    public virtual InviscidLiftSweepResult SweepInviscidLiftCoefficient(
        AirfoilGeometry geometry,
        double liftStart,
        double liftEnd,
        double liftStep,
        AnalysisSettings? settings = null,
        double initialAlphaDegrees = 0d)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (Math.Abs(liftStep) < 1e-12)
        {
            throw new ArgumentException("Lift step must be non-zero.", nameof(liftStep));
        }

        settings ??= new AnalysisSettings();
        var step = NormalizeStep(liftStart, liftEnd, liftStep);
        var points = new List<InviscidTargetLiftResult>();
        var alphaGuess = initialAlphaDegrees;

        var caps = settings;
        Func<double, InviscidAnalysisResult> evaluate = alpha => AnalyzeInviscidLinearVortex(geometry, alpha, caps);
        for (var targetLift = liftStart; ShouldContinue(targetLift, liftEnd, step); targetLift += step)
        {
            var result = AnalyzeInviscidForLiftCoefficient(evaluate, targetLift, alphaGuess, liftTolerance: 0.01d, maxIterations: 20);
            alphaGuess = result.AngleOfAttackDegrees;
            points.Add(new InviscidTargetLiftResult(targetLift, result));
        }

        return new InviscidLiftSweepResult(geometry, settings, points);
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECCL.
    // Difference from legacy: The managed API exposes this as a direct public query returning an InviscidAnalysisResult instead of mutating the global operating state.
    // Decision: Keep the public helper because it is a useful capability boundary.
    public virtual InviscidAnalysisResult AnalyzeInviscidForLiftCoefficient(
        AirfoilGeometry geometry,
        double targetLiftCoefficient,
        AnalysisSettings? settings = null,
        double initialAlphaDegrees = 0d,
        double liftTolerance = 0.01d,
        int maxIterations = 20)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (liftTolerance <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(liftTolerance), "Lift tolerance must be positive.");
        }

        if (maxIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxIterations), "Iteration count must be positive.");
        }

        settings ??= new AnalysisSettings();
        var caps = settings;
        return AnalyzeInviscidForLiftCoefficient(
            alpha => AnalyzeInviscidLinearVortex(geometry, alpha, caps),
            targetLiftCoefficient, initialAlphaDegrees, liftTolerance, maxIterations);
    }

    // ================================================================
    // Private helpers
    // ================================================================

    // Legacy mapping: managed-only geometry extraction helper with no direct Fortran analogue.
    // Difference from legacy: The port converts immutable point objects into raw coordinate arrays before calling the low-level solvers.
    // Decision: Keep the helper because it is the natural bridge between API objects and solver arrays.
    private static (double[] x, double[] y) ExtractCoordinates(AirfoilGeometry geometry)
    {
        var points = geometry.Points;
        var inputX = new double[points.Count];
        var inputY = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            inputX[i] = points[i].X;
            inputY[i] = points[i].Y;
        }
        return (inputX, inputY);
    }

    // Legacy mapping: f_xfoil/src/xoper.f :: SPECCL-style target-CL solve, managed-derived for the Hess-Smith path.
    // Difference from legacy: The root find is a simple secant-style managed loop around the prepared Hess-Smith solver instead of the original XFoil inviscid state machine.
    // Solver-agnostic Newton/secant alpha search: takes an evaluator that
    // returns the inviscid result for a given alpha, then iterates to hit
    // the target CL.
    private static InviscidAnalysisResult AnalyzeInviscidForLiftCoefficient(
        Func<double, InviscidAnalysisResult> evaluate,
        double targetLiftCoefficient,
        double initialAlphaDegrees,
        double liftTolerance,
        int maxIterations)
    {
        var alpha1 = initialAlphaDegrees;
        var result1 = evaluate(alpha1);
        var error1 = targetLiftCoefficient - result1.LiftCoefficient;
        if (Math.Abs(error1) <= liftTolerance)
        {
            return result1;
        }

        double seedStep = Math.Sign(error1);
        if (seedStep == 0d)
        {
            seedStep = 1d;
        }

        var alpha2 = alpha1 + (2d * seedStep);
        var result2 = evaluate(alpha2);
        var error2 = targetLiftCoefficient - result2.LiftCoefficient;
        if (Math.Abs(error2) <= liftTolerance)
        {
            return result2;
        }

        var bestResult = Math.Abs(error2) < Math.Abs(error1) ? result2 : result1;
        var bestError = Math.Min(Math.Abs(error1), Math.Abs(error2));

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var denominator = result2.LiftCoefficient - result1.LiftCoefficient;
            double nextAlpha;

            if (Math.Abs(denominator) < 1e-9)
            {
                nextAlpha = alpha2 + (0.5d * Math.Sign(error2 == 0d ? 1d : error2));
            }
            else
            {
                nextAlpha = alpha2 + ((targetLiftCoefficient - result2.LiftCoefficient) * (alpha2 - alpha1) / denominator);
            }

            nextAlpha = Math.Clamp(nextAlpha, -20d, 20d);
            if (Math.Abs(nextAlpha - alpha2) < 1e-6)
            {
                nextAlpha = alpha2 + (0.25d * Math.Sign(error2 == 0d ? 1d : error2));
            }

            var nextResult = evaluate(nextAlpha);
            var nextError = targetLiftCoefficient - nextResult.LiftCoefficient;
            if (Math.Abs(nextError) < bestError)
            {
                bestError = Math.Abs(nextError);
                bestResult = nextResult;
            }

            if (Math.Abs(nextError) <= liftTolerance)
            {
                return nextResult;
            }

            alpha1 = alpha2;
            result1 = result2;
            error1 = error2;

            alpha2 = nextAlpha;
            result2 = nextResult;
            error2 = nextError;
        }

        return bestResult;
    }

    // Legacy mapping: f_xfoil/src/xpanel.f :: GGCALC/SPECAL/SPECCL lineage through LinearVortexInviscidSolver.
    // Difference from legacy: The method adapts a LinearVortexInviscidResult back into the public InviscidAnalysisResult shape and fills several Hess-Smith-centric fields with placeholders.
    // Decision: Keep the adapter until the public result model is unified across inviscid back ends.
    private InviscidAnalysisResult AnalyzeInviscidLinearVortex(
        AirfoilGeometry geometry,
        double angleOfAttackDegrees,
        AnalysisSettings settings)
    {
        var points = geometry.Points;
        var inputX = new double[points.Count];
        var inputY = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            inputX[i] = points[i].X;
            inputY[i] = points[i].Y;
        }

        var lvResult = LinearVortexInviscidSolver.AnalyzeInviscid(
            inputX, inputY, points.Count,
            angleOfAttackDegrees,
            settings.PanelCount,
            settings.MachNumber,
            settings.UseLegacyPanelingPrecision);

        // Cleanup Step 0.A: replicate LinearVortexInviscidSolver.AnalyzeInviscid's
        // panel distribution locally so we get control points that ACTUALLY
        // correspond to the panels the inviscid solver used. Previously this
        // method called panelMeshGenerator.Generate(), which used a DIFFERENT
        // distributor (managed approximation, not PANGEN port) — Cp values
        // were silently mapped to control points from a different mesh.
        int maxNodes = settings.PanelCount + 40;
        var lvPanel = new LinearVortexPanelState(maxNodes);
        CurvatureAdaptivePanelDistributor.Distribute(
            inputX, inputY, points.Count,
            lvPanel, settings.PanelCount,
            useLegacyPrecision: settings.UseLegacyPanelingPrecision);

        int sampleCount = Math.Min(lvResult.PressureCoefficients.Count, lvPanel.NodeCount - 1);
        var lvSamples = new PressureCoefficientSample[sampleCount];
        for (int si = 0; si < sampleCount; si++)
        {
            double midX = 0.5 * (lvPanel.X[si] + lvPanel.X[si + 1]);
            double midY = 0.5 * (lvPanel.Y[si] + lvPanel.Y[si + 1]);
            lvSamples[si] = new PressureCoefficientSample(
                new AirfoilPoint(midX, midY),
                tangentialVelocity: 0d,
                pressureCoefficient: lvResult.PressureCoefficients[si],
                correctedPressureCoefficient: lvResult.PressureCoefficients[si]);
        }

        return new InviscidAnalysisResult(
            panelCount: lvPanel.NodeCount - 1,
            angleOfAttackDegrees,
            settings.MachNumber,
            circulation: 0.0,
            liftCoefficient: lvResult.LiftCoefficient,
            dragCoefficient: lvResult.PressureDragCoefficient,
            correctedPressureIntegratedLiftCoefficient: lvResult.LiftCoefficient,
            correctedPressureIntegratedDragCoefficient: lvResult.PressureDragCoefficient,
            pressureIntegratedLiftCoefficient: lvResult.LiftCoefficient,
            pressureIntegratedDragCoefficient: lvResult.PressureDragCoefficient,
            momentCoefficientQuarterChord: lvResult.MomentCoefficient,
            sourceStrengths: Array.Empty<double>(),
            vortexStrength: 0.0,
            pressureSamples: lvSamples,
            wake: new WakeGeometry(Array.Empty<WakePoint>()));
    }

    // Legacy mapping: managed-only result adapter with no direct Fortran analogue.
    // Difference from legacy: The helper converts a detailed inviscid result object into the compact polar-point record used by managed sweeps.
    // Decision: Keep the adapter because it matches the managed reporting model.
    private static PolarPoint ToPolarPoint(InviscidAnalysisResult result)
    {
        return new PolarPoint(
            result.AngleOfAttackDegrees,
            result.LiftCoefficient,
            result.DragCoefficient,
            result.CorrectedPressureIntegratedLiftCoefficient,
            result.CorrectedPressureIntegratedDragCoefficient,
            result.MomentCoefficientQuarterChord,
            result.Circulation,
            result.PressureIntegratedLiftCoefficient,
            result.PressureIntegratedDragCoefficient);
    }

    // Legacy mapping: managed-only sweep utility mirroring legacy inclusive loop direction handling.
    // Difference from legacy: The helper centralizes step normalization instead of distributing it across interactive loops.
    // Decision: Keep the helper because it simplifies the sweep façade code.
    private static double NormalizeStep(double start, double end, double step)
    {
        if (end >= start)
        {
            return Math.Abs(step);
        }

        return -Math.Abs(step);
    }

    // Legacy mapping: managed-only sweep-bound helper mirroring legacy loop intent.
    // Difference from legacy: The original code relies on command-state control flow rather than a standalone predicate.
    // Decision: Keep the helper because it makes the managed sweep boundaries explicit.
    private static bool ShouldContinue(double current, double end, double step)
    {
        const double tolerance = 1e-9;
        if (step > 0d)
        {
            return current <= end + tolerance;
        }

        return current >= end - tolerance;
    }
}
