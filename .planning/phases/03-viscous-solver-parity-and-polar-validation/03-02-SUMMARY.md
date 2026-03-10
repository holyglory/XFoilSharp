---
phase: 03-viscous-solver-parity-and-polar-validation
plan: 02
subsystem: solver
tags: [transition, e-n-method, amplification, dampl, trchek2, jacobian, xblsys]

# Dependency graph
requires:
  - phase: 03-viscous-solver-parity-and-polar-validation
    plan: 01
    provides: BoundaryLayerCorrelations (KinematicShapeParameter), BoundaryLayerSystemState, TransitionInfo/TransitionType models
provides:
  - TransitionModel static class with DAMPL, DAMPL2, AXSET, CheckTransition (TRCHEK2)
  - AxsetResult readonly record struct for combined amplification sensitivities
  - TransitionCheckResult readonly record struct with convergence diagnostics
  - TransitionResultType enum (None/Free/Forced)
affects: [03-03, 03-04, 03-05, 03-06, 03-07, 03-08]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Newton iteration with C2SAV save/restore pattern for transition location"
    - "Cubic ramp onset (DGR=0.08) for smooth amplification activation"
    - "RMS averaging of station amplification rates (better on coarse grids)"

key-files:
  created:
    - src/XFoil.Solver/Services/TransitionModel.cs
    - tests/XFoil.Core.Tests/TransitionModelPortTests.cs
  modified: []

key-decisions:
  - "DAMPL2 includes exp(-20*HMI) term in AF that DAMPL lacks -- they differ slightly at low Hk (< 0.1% relative)"
  - "CheckTransition uses simplified interface (station BL variables) rather than full BoundaryLayerSystemState to allow unit testing without full state setup"
  - "Test Rt values set well above critical Re_theta to ensure non-zero amplification rates in all TRCHEK2 tests"

patterns-established:
  - "C2SAV save/restore: save station 2 variables before Newton iteration, restore after convergence"
  - "Transition Newton: linear interpolation of BL variables at trial location, AXSET for amplification, Newton correction with interval clamping"
  - "Forced vs free transition: forced upstream of natural takes precedence, forced downstream ignored"

requirements-completed: [VISC-03]

# Metrics
duration: 19min
completed: 2026-03-11
---

# Phase 3 Plan 02: Transition Model Summary

**Full e^N transition model ported from xblsys.f with DAMPL/DAMPL2 amplification rates, AXSET RMS-averaged dispatch, and TRCHEK2 Newton iteration for exact transition location -- all with Jacobian sensitivities for Newton system coupling**

## Performance

- **Duration:** 19 min
- **Started:** 2026-03-10T23:05:08Z
- **Completed:** 2026-03-10T23:24:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Ported DAMPL (Falkner-Skan envelope amplification) with critical Re correlation, smooth cubic onset ramp, m(H) spatial correlation, and full Jacobian sensitivities (AX_HK, AX_TH, AX_RT)
- Ported DAMPL2 (modified envelope for separated profiles) with Orr-Sommerfeld max-ai correction blending between Hk=3.5 and Hk=4.0
- Ported AXSET (combined dispatch) with RMS averaging of station rates and near-Ncrit dN/dx > 0 correction term
- Ported TRCHEK2 (Newton iteration) for exact transition location within BL interval straddling N_crit, with C2SAV save/restore, amplitude clamping, and forced transition override logic
- All 20 new tests pass; all 236 total tests pass with zero regressions

## Task Commits

Each task was committed atomically (TDD RED-GREEN):

1. **Task 1 RED: Add failing tests for DAMPL/DAMPL2/AXSET** - `4db66f3` (test)
2. **Task 1 GREEN: Port DAMPL, DAMPL2, AXSET** - `98033bf` (feat)
3. **Task 2 RED: Add failing tests for TRCHEK2** - `ab2dccf` (test)
4. **Task 2 GREEN: Port TRCHEK2 transition Newton iteration** - `d7e07f4` (feat)

## Files Created/Modified
- `src/XFoil.Solver/Services/TransitionModel.cs` - Full e^N transition model: ComputeAmplificationRate (DAMPL), ComputeAmplificationRateHighHk (DAMPL2), ComputeTransitionSensitivities (AXSET), CheckTransition (TRCHEK2)
- `tests/XFoil.Core.Tests/TransitionModelPortTests.cs` - 20 tests: amplification rates, Jacobian central-difference verification, onset ramp, Hk blending, Newton convergence, forced/free transition

## Decisions Made
- **DAMPL2 AF term difference:** DAMPL2 adds `0.1*exp(-20*HMI)` to the AF conversion factor that DAMPL lacks. This causes < 0.1% relative difference at low Hk. Both functions are exact ports of their respective Fortran subroutines.
- **Simplified CheckTransition interface:** Used flat parameters (hk1, th1, rt1, etc.) rather than requiring a full BoundaryLayerSystemState. This allows unit testing without constructing full BL state. The full SETBL integration (Plan 03-05+) will wrap this with BoundaryLayerSystemState access.
- **Test parameter selection:** TRCHEK2 tests use Rt=5000-10000 (well above critical Re for Hk=2.4-2.8) and intervals dx=0.30 to ensure AX*dx can bridge the N gap. Smaller intervals or lower Rt produce zero amplification rates.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Pre-existing untracked files from parallel plan executions (BoundaryLayerSystemAssemblerTests.cs, BlockTridiagonalSolverTests.cs, BoundaryLayerSystemAssembler.cs) caused build errors. Temporarily excluded during testing, restored afterward. Logged as out-of-scope.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- TransitionModel ready for integration into BL system assembler (TRDIF in Plan 03-03)
- AXSET available for laminar amplification equation in BLDIF
- CheckTransition ready for TRCHEK2 calls from SETBL (Plan 03-05+)
- Forced transition support available via AnalysisSettings.ForcedTransitionUpper/Lower

## Self-Check: PASSED

All 2 created files verified present. All 4 task commits verified in git log.

---
*Phase: 03-viscous-solver-parity-and-polar-validation*
*Completed: 2026-03-11*
