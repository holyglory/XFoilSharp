---
phase: 03-viscous-solver-parity-and-polar-validation
plan: 07
subsystem: solver
tags: [polar-sweep, viscous, newton, warm-start, mrcl, airfoil-analysis]

# Dependency graph
requires:
  - phase: 03-06
    provides: "DragCalculator, PostStallExtrapolator, ViscousSolverEngine drag wiring"
provides:
  - "PolarSweepRunner with Type 1 (alpha), Type 2 (CL), Type 3 (Re) sweeps"
  - "AirfoilAnalysisService viscous API: AnalyzeViscous, SweepViscousAlpha, SweepViscousCL, SweepViscousRe"
  - "Surrogate pipeline deletion (7 files removed)"
  - "CLI commands updated to use Newton solver API"
affects: [03-08-parity-validation, phase-4-randomized-test-bench]

# Tech tracking
tech-stack:
  added: []
  patterns: [multi-seed-warm-start, mrcl-coupling, tdd-red-green-refactor]

key-files:
  created:
    - src/XFoil.Solver/Services/PolarSweepRunner.cs
    - tests/XFoil.Core.Tests/PolarSweepRunnerTests.cs
  modified:
    - src/XFoil.Solver/Services/AirfoilAnalysisService.cs
    - src/XFoil.Solver/Models/ViscousAnalysisResult.cs
    - src/XFoil.Solver/Services/ViscousStateEstimator.cs
    - src/XFoil.Solver/Services/ViscousLaminarCorrector.cs
    - src/XFoil.IO/Services/AnalysisSessionRunner.cs
    - src/XFoil.Cli/Program.cs

key-decisions:
  - "Simplified BLSnapshot to AlphaRadians-only for warm-start tracking (ViscousSolverEngine reinitializes BL from scratch each call)"
  - "Inlined LaminarAmplificationModel into ViscousStateEstimator rather than keeping a separate file"
  - "CLI surrogate-specific diagnostic commands replaced with deprecation notices rather than full rewrites"
  - "Old surrogate methods kept as Obsolete stubs (throwing NotSupportedException) for API documentation"

patterns-established:
  - "Polar sweep warm-start: previous converged -> nearby converged -> cold-start"
  - "MRCL coupling: Type 1 fixed M/Re, Type 2 M may depend on CL, Type 3 Re = REINF1 * CL^RETYP"

requirements-completed: [POL-01, POL-02, POL-03]

# Metrics
duration: 25min
completed: 2026-03-11
---

# Phase 3 Plan 7: Polar Sweep Runner and AirfoilAnalysisService Integration Summary

**PolarSweepRunner with all 3 polar sweep types (alpha/CL/Re), multi-seed warm-start, MRCL coupling, and full surrogate pipeline deletion**

## Performance

- **Duration:** 25 min
- **Started:** 2026-03-11T01:00:00Z
- **Completed:** 2026-03-11T01:25:00Z
- **Tasks:** 2
- **Files modified:** 23

## Accomplishments
- Implemented PolarSweepRunner with SweepAlpha (Type 1), SweepCL (Type 2), SweepRe (Type 3) and multi-seed warm-start strategy
- Wired Newton-coupled viscous solver into AirfoilAnalysisService with clean public API (AnalyzeViscous, SweepViscousAlpha, SweepViscousCL, SweepViscousRe)
- Deleted 7 surrogate pipeline files (-951 lines), updated all dependents (tests, CLI, IO)
- All 258 tests pass including 11 new PolarSweepRunner integration tests covering NACA 0012 alpha sweep and NACA 2412 CL sweep

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement PolarSweepRunner** - `f41ec29` (feat)
2. **Task 2: Wire viscous solver, delete surrogates, integration tests** - TDD workflow:
   - `9a0d833` (test: TDD RED - 11 failing tests)
   - `4b746c9` (feat: TDD GREEN - implementation + surrogate deletion)
   - `b2e22bf` (fix: CLI commands updated to new API)

**Plan metadata:** [pending final commit]

## Files Created/Modified

**Created:**
- `src/XFoil.Solver/Services/PolarSweepRunner.cs` - Static class with SweepAlpha, SweepCL, SweepRe, MRCL coupling, warm-start
- `tests/XFoil.Core.Tests/PolarSweepRunnerTests.cs` - 11 integration tests for polar sweeps and AirfoilAnalysisService wiring

**Modified:**
- `src/XFoil.Solver/Services/AirfoilAnalysisService.cs` - Added AnalyzeViscous, SweepViscousAlpha, SweepViscousCL, SweepViscousRe; marked old methods Obsolete
- `src/XFoil.Solver/Models/ViscousAnalysisResult.cs` - Added AngleOfAttackDegrees property
- `src/XFoil.Solver/Services/ViscousStateEstimator.cs` - Inlined LaminarAmplificationModel as private method
- `src/XFoil.Solver/Services/ViscousLaminarCorrector.cs` - Gutted to throw NotSupportedException
- `src/XFoil.IO/Services/AnalysisSessionRunner.cs` - Viscous sweeps use new SweepViscousAlpha/SweepViscousCL API
- `src/XFoil.Cli/Program.cs` - 5 functions rewritten to use Newton API, 5 replaced with deprecation notices
- `tests/XFoil.Core.Tests/AirfoilAnalysisServiceTests.cs` - Removed 3 surrogate-specific tests
- `tests/XFoil.Core.Tests/TransitionModelTests.cs` - Removed 1 surrogate-specific test
- `tests/XFoil.Core.Tests/AnalysisSessionRunnerTests.cs` - Updated CSV format assertion

**Deleted:**
- `src/XFoil.Solver/Services/ViscousLaminarSolver.cs`
- `src/XFoil.Solver/Services/ViscousInteractionCoupler.cs`
- `src/XFoil.Solver/Services/EdgeVelocityFeedbackBuilder.cs`
- `src/XFoil.Solver/Services/LaminarAmplificationModel.cs`
- `src/XFoil.Solver/Services/ViscousForceEstimator.cs`
- `src/XFoil.Solver/Services/DisplacementSurfaceGeometryBuilder.cs`
- `src/XFoil.Solver/Services/ViscousIntervalSystemBuilder.cs`
- `tests/XFoil.Core.Tests/DisplacementCoupledViscousTests.cs`
- `tests/XFoil.Core.Tests/ViscousInteractionTests.cs`
- `tests/XFoil.Core.Tests/ViscousIntervalSystemTests.cs`
- `tests/XFoil.Core.Tests/ViscousLaminarCorrectionTests.cs`
- `tests/XFoil.Core.Tests/ViscousLaminarSolveTests.cs`

## Decisions Made
- **BLSnapshot simplified to AlphaRadians-only:** ViscousSolverEngine reinitializes BL state from scratch on each call, so full BL state snapshots are unnecessary. Warm-start tracking uses alpha proximity instead.
- **LaminarAmplificationModel inlined:** Rather than keeping a separate file with a single method after surrogate deletion, the amplification logic was inlined as a private static method in ViscousStateEstimator.
- **CLI commands use deprecation notices:** 5 surrogate-specific CLI diagnostic commands (interval system, correction, solve, interaction, displacement-coupled) print deprecation messages rather than being fully rewritten, since they are development diagnostics with no production users.
- **Old methods kept as Obsolete stubs:** AirfoilAnalysisService's old surrogate methods marked with `[Obsolete]` throwing `NotSupportedException` to document the API change for any external callers.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] ViscousStateEstimator referenced deleted LaminarAmplificationModel**
- **Found during:** Task 2 (surrogate deletion)
- **Issue:** After deleting LaminarAmplificationModel.cs, ViscousStateEstimator had unresolved references
- **Fix:** Inlined the AdvanceAmplification logic as a private static method using BoundaryLayerCorrelationConstants
- **Files modified:** src/XFoil.Solver/Services/ViscousStateEstimator.cs
- **Verification:** Build succeeds, transition tests pass
- **Committed in:** 4b746c9

**2. [Rule 3 - Blocking] ViscousLaminarCorrector referenced deleted types**
- **Found during:** Task 2 (surrogate deletion)
- **Issue:** ViscousLaminarCorrector referenced ViscousIntervalSystemBuilder and LaminarAmplificationModel
- **Fix:** Rewrote to throw NotSupportedException with migration message
- **Files modified:** src/XFoil.Solver/Services/ViscousLaminarCorrector.cs
- **Verification:** Build succeeds
- **Committed in:** 4b746c9

**3. [Rule 3 - Blocking] AnalysisSessionRunner called obsolete sweep methods**
- **Found during:** Task 2 (wiring new API)
- **Issue:** ExportViscousAlphaSweep and ExportViscousLiftSweep called SweepDisplacementCoupledAlpha/LiftCoefficient
- **Fix:** Rewrote both methods to use SweepViscousAlpha/SweepViscousCL with simple CSV output
- **Files modified:** src/XFoil.IO/Services/AnalysisSessionRunner.cs
- **Verification:** Build succeeds, session runner test passes
- **Committed in:** 4b746c9

**4. [Rule 3 - Blocking] Test files referenced Obsolete methods (CS0618 errors)**
- **Found during:** Task 2 (test compilation)
- **Issue:** 7 test files called deprecated surrogate methods, treated as errors by warnings-as-errors
- **Fix:** Deleted 5 surrogate-specific test files, removed 3 surrogate tests from AirfoilAnalysisServiceTests, removed 1 from TransitionModelTests, updated AnalysisSessionRunnerTests assertion
- **Files modified:** 8 test files (5 deleted, 3 modified)
- **Verification:** All 258 tests pass
- **Committed in:** 4b746c9

**5. [Rule 3 - Blocking] CLI Program.cs called 10 obsolete methods**
- **Found during:** Task 2 (full solution build)
- **Issue:** CLI had 10 calls to deprecated surrogate methods across 10 functions
- **Fix:** Rewrote 5 functions to use new Newton API, replaced 5 surrogate-specific functions with deprecation notices, removed unused ComputeTransitionResidual
- **Files modified:** src/XFoil.Cli/Program.cs
- **Verification:** Full solution builds with zero warnings and zero errors
- **Committed in:** b2e22bf

---

**Total deviations:** 5 auto-fixed (all Rule 3 - Blocking)
**Impact on plan:** All auto-fixes were necessary cascading consequences of the planned surrogate deletion. No scope creep.

## Issues Encountered
- CS0649 warnings-as-errors on BLSnapshot: Initial implementation had unused BL state fields. Resolved by simplifying BLSnapshot to only track AlphaRadians since ViscousSolverEngine reinitializes BL from scratch.
- CS8603/CS8600 nullable reference warnings: FindBestWarmStart return type needed to be nullable BLSnapshot?. Fixed with proper nullable annotations.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All 3 polar sweep types implemented and tested
- AirfoilAnalysisService has clean viscous API ready for Phase 3 Plan 8 (parity validation)
- Surrogate pipeline fully removed per CONTEXT.md locked decision
- 258 tests all passing, zero warnings, zero errors across full solution
- Ready for 03-08 parity validation testing against XFoil reference

---
*Phase: 03-viscous-solver-parity-and-polar-validation*
*Completed: 2026-03-11*
