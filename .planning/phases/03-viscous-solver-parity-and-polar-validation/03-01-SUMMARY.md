---
phase: 03-viscous-solver-parity-and-polar-validation
plan: 01
subsystem: solver
tags: [boundary-layer, correlations, jacobian, xblsys, viscous-models, newton-system]

# Dependency graph
requires:
  - phase: 02-inviscid-kernel-parity
    provides: AnalysisSettings, InviscidSolverState, Phase 2 patterns (static utility classes, readonly record structs, mutable workspaces)
provides:
  - 10 viscous state model types (BoundaryLayerProfile, BoundaryLayerSystemState, ViscousNewtonSystem, ViscousConvergenceInfo, ViscousAnalysisResult, DragDecomposition, TransitionInfo, PolarSweepMode, ViscousSolverMode)
  - Extended AnalysisSettings with 10 new viscous parameters (NCritUpper, NCritLower, UsePostStallExtrapolation, ViscousSolverMode, ForcedTransition, etc.)
  - BoundaryLayerCorrelations static class with 10 correlation functions (HKIN, HSL, HST, CFL, CFT, DIL, DILW, HCT, DIT, EquilibriumShearCoefficient)
  - Each correlation returns value + all Jacobian sensitivities for Newton system
affects: [03-02, 03-03, 03-04, 03-05, 03-06, 03-07, 03-08]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Jacobian-returning correlations: (value, dValue/dParam1, ...) tuples"
    - "Mutable workspace classes for Newton system arrays"
    - "Readonly record structs for immutable BL profile output"

key-files:
  created:
    - src/XFoil.Solver/Models/BoundaryLayerProfile.cs
    - src/XFoil.Solver/Models/BoundaryLayerSystemState.cs
    - src/XFoil.Solver/Models/ViscousNewtonSystem.cs
    - src/XFoil.Solver/Models/ViscousConvergenceInfo.cs
    - src/XFoil.Solver/Models/ViscousAnalysisResult.cs
    - src/XFoil.Solver/Models/DragDecomposition.cs
    - src/XFoil.Solver/Models/TransitionInfo.cs
    - src/XFoil.Solver/Models/PolarSweepMode.cs
    - src/XFoil.Solver/Models/ViscousSolverMode.cs
    - src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs
    - tests/XFoil.Core.Tests/BoundaryLayerCorrelationsTests.cs
  modified:
    - src/XFoil.Solver/Models/AnalysisSettings.cs

key-decisions:
  - "Fixed Fortran sign error in DILW RCD_HK derivative (xblsys.f:2315) -- used mathematically correct d/dHk[(Hk-1)^2/Hk^3]"
  - "Ported HCT as DensityThicknessShapeParameter (matching Fortran semantics) and added standalone EquilibriumShearCoefficient from BLVAR CQ2 formula"
  - "EquilibriumShearCoefficient takes fully resolved intermediates (hk, hs, us, h) with default CTCON from BoundaryLayerCorrelationConstants"

patterns-established:
  - "Jacobian tuple return: each correlation returns (value, d/dParam1, d/dParam2, ...) matching Fortran CALL pattern"
  - "Central-difference Jacobian verification in tests: (f(x+eps)-f(x-eps))/(2*eps) vs analytical derivative"
  - "Fortran comment references in XML docs: source file and line number for traceability"

requirements-completed: [VISC-01]

# Metrics
duration: 12min
completed: 2026-03-10
---

# Phase 3 Plan 01: BL State Models and Correlations Summary

**10 viscous state models, extended AnalysisSettings with per-surface NCrit and post-stall flag, and 10 BL correlation functions ported from xblsys.f with full Jacobian sensitivities verified by central-difference cross-check**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-10T22:47:42Z
- **Completed:** 2026-03-10T23:00:12Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments
- Created all viscous solver state models following Phase 2 mutable workspace + readonly record struct patterns
- Extended AnalysisSettings with 10 new viscous parameters while preserving full backward compatibility (all 153 existing tests pass unchanged)
- Ported 10 BL correlation functions from xblsys.f with complete Jacobian sensitivity chains
- All 28 new correlation tests pass, including central-difference Jacobian verification within 1e-6 tolerance

## Task Commits

Each task was committed atomically:

1. **Task 1: Create viscous state models and extend AnalysisSettings** - `cb6b8c0` (feat)
2. **Task 2 RED: Add failing tests for BL correlations** - `8dbe4e6` (test)
3. **Task 2 GREEN: Port BL correlations with Jacobian sensitivities** - `0c0ccdf` (feat)

## Files Created/Modified
- `src/XFoil.Solver/Models/BoundaryLayerProfile.cs` - Per-station BL state (theta, delta*, Ctau, Hk, cf, Re_theta, N, xi)
- `src/XFoil.Solver/Models/BoundaryLayerSystemState.cs` - Full BL state for both sides + wake (UEDG, THET, DSTR, CTAU, MASS arrays)
- `src/XFoil.Solver/Models/ViscousNewtonSystem.cs` - Block-tridiagonal Newton system arrays (VA, VB, VM, VDEL, VZ, ISYS)
- `src/XFoil.Solver/Models/ViscousConvergenceInfo.cs` - Per-iteration diagnostics (RMS, max residual, CL/CD/CM)
- `src/XFoil.Solver/Models/ViscousAnalysisResult.cs` - Full viscous result with BL profiles and drag decomposition
- `src/XFoil.Solver/Models/DragDecomposition.cs` - CD, CDF, CDP, cross-check, discrepancy, TE base drag, wave drag
- `src/XFoil.Solver/Models/TransitionInfo.cs` - Transition location, type (Free/Forced), N at transition, convergence
- `src/XFoil.Solver/Models/PolarSweepMode.cs` - Enum: AlphaSweep, CLSweep, ReSweep (Type 1/2/3)
- `src/XFoil.Solver/Models/ViscousSolverMode.cs` - Enum: TrustRegion, XFoilRelaxation
- `src/XFoil.Solver/Models/AnalysisSettings.cs` - Extended with ViscousSolverMode, NCritUpper/Lower, ForcedTransition, PostStall, etc.
- `src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs` - 10 correlation functions: HKIN, HSL, HST, CFL, CFT, DIL, DILW, HCT, DIT, EquilibriumShearCoefficient
- `tests/XFoil.Core.Tests/BoundaryLayerCorrelationsTests.cs` - 28 tests with value + Jacobian verification

## Decisions Made
- **Fixed Fortran DILW sign error:** The Fortran xblsys.f:2315 has a sign error in the RCD_HK derivative for wake dissipation. The correct derivative of (Hk-1)^2/Hk^3 is (Hk-1)(3-Hk)/Hk^4, not the negative form in the original. Used mathematically correct version, verified by central-difference test.
- **HCT mapped to DensityThicknessShapeParameter:** The Fortran HCT subroutine computes the density thickness shape parameter (from Whitfield), not an equilibrium shear coefficient. Named accordingly. Added a separate EquilibriumShearCoefficient function based on the CQ2 computation in BLVAR.
- **EquilibriumShearCoefficient signature:** Takes fully resolved intermediates (hk, hs, us, h) rather than raw BL variables, since the equilibrium Ctau formula depends on quantities computed by multiple upstream correlations.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed Fortran sign error in DILW RCD_HK derivative**
- **Found during:** Task 2 (BL correlation port)
- **Issue:** Fortran xblsys.f:2315 has incorrect sign on the first term of RCD_HK. Central-difference verification exposed the discrepancy.
- **Fix:** Used mathematically correct derivative: 1.10*(Hk-1)*(3-Hk)/Hk^4
- **Files modified:** src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs
- **Verification:** Central-difference Jacobian test passes within 1e-6
- **Committed in:** 0c0ccdf

**2. [Rule 1 - Bug] Fixed HCT naming mismatch in plan**
- **Found during:** Task 2 (BL correlation port)
- **Issue:** Plan mapped HCT to "EquilibriumShearCoefficient" but Fortran HCT computes density thickness shape parameter. Plan behavior section referenced nonexistent line 2614 (file is 2522 lines).
- **Fix:** Named HCT port as DensityThicknessShapeParameter (matching exports list) and added separate EquilibriumShearCoefficient from BLVAR CQ2 formula.
- **Files modified:** src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs, tests/XFoil.Core.Tests/BoundaryLayerCorrelationsTests.cs
- **Verification:** Both functions pass value and Jacobian tests
- **Committed in:** 0c0ccdf

---

**Total deviations:** 2 auto-fixed (2 bug fixes)
**Impact on plan:** Both auto-fixes necessary for correctness. No scope creep.

## Issues Encountered
None beyond the deviations documented above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All viscous state models in place for subsequent plans (03-02 through 03-08)
- BoundaryLayerCorrelations ready for use by BL system assembler (03-02: BLSYS/BLVAR/BLDIF)
- AnalysisSettings extended for viscous solver configuration
- EquilibriumShearCoefficient available for turbulent BL equation (Ctau equilibrium in BLSYS)

## Self-Check: PASSED

All 12 created/modified files verified present. All 3 task commits verified in git log.

---
*Phase: 03-viscous-solver-parity-and-polar-validation*
*Completed: 2026-03-10*
