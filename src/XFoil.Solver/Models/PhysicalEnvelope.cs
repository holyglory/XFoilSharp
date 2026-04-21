namespace XFoil.Solver.Models;

// Phase 2 iter 44: shared physicality envelope for ViscousAnalysisResult.
//
// The viscous Newton iteration can satisfy its rmsbl<tolerance criterion
// without producing a physical result — the BL state can wander into a
// non-physical attractor (CD up to 1e105 observed at extreme α/Re). 2D
// airfoils realistically peak around CL≈1.5–1.8, so |CL|>5 or CD outside
// [0,1] indicates the converged result is not engineering data.
//
// Centralized here so the diagnostic harness, the tests, and the CLI all
// use the same envelope. Engine semantics are unchanged — this is a
// post-hoc classification, not a Newton-iteration gate.
public static class PhysicalEnvelope
{
    public const double MaxAbsoluteLiftCoefficient = 5.0;
    public const double MaxDragCoefficient = 1.0;

    // Post-stall Viterna-Corrigan extrapolation matches an analytic
    // CL model to a pre-stall anchor, so realistic 2D post-stall CL
    // stays bounded below the airfoil's CL_max (~1.8 for conventional
    // sections). This tighter cap rejects non-physical Newton
    // attractors that slip through the generic envelope (|CL|≤5) and
    // get misreported as post-stall Viterna outputs.
    public const double MaxAbsoluteLiftCoefficientPostStall = 2.2;

    public static bool IsAirfoilResultPhysical(ViscousAnalysisResult? result)
        => result is { Converged: true }
           && double.IsFinite(result.LiftCoefficient)
           && double.IsFinite(result.DragDecomposition.CD)
           && System.Math.Abs(result.LiftCoefficient) <= MaxAbsoluteLiftCoefficient
           && result.DragDecomposition.CD >= 0d
           && result.DragDecomposition.CD <= MaxDragCoefficient;

    // Tier B3 v1 — relaxed post-stall envelope check.
    //
    // The ViscousSolverEngine's Viterna-Corrigan fill-in keeps
    // Converged=false because the Newton iteration itself never
    // succeeded — the CL/CD values come from an analytic post-stall
    // model, not from the BL solve. We still want to accept those
    // results as physical when the caller explicitly opted into
    // post-stall extrapolation, so this variant drops the Converged
    // requirement but keeps the same CL/CD envelope bounds.
    public static bool IsAirfoilResultPhysicalPostStall(ViscousAnalysisResult? result)
        => result is not null
           && double.IsFinite(result.LiftCoefficient)
           && double.IsFinite(result.DragDecomposition.CD)
           && System.Math.Abs(result.LiftCoefficient) <= MaxAbsoluteLiftCoefficientPostStall
           && result.DragDecomposition.CD >= 0d
           && result.DragDecomposition.CD <= MaxDragCoefficient;
}
