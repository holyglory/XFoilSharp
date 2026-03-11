---
phase: 03-viscous-solver-parity-and-polar-validation
plan: 09
subsystem: numerics
tags: [newton-system, block-tridiagonal, dij-coupling, transition-model, viscous-solver]

# Dependency graph
requires:
  - phase: 03-04
    provides: BlockTridiagonalSolver and BandMatrixSolver
  - phase: 03-05
    provides: ViscousNewtonAssembler, ViscousNewtonUpdater, ViscousNewtonSystem
provides:
  - ViscousNewtonSystem arrays indexed by global system line IV (no side collision)
  - SetupISYS method for bidirectional (ibl,side) <-> iv mapping
  - GetPanelIndex helper for correct DIJ panel-index mapping
  - TransitionModel.CheckTransition wired into Newton assembler BL march
  - Ue update from mass coupling via DIJ (was previously hardcoded to 0.0)
affects: [03-10-PLAN, viscous-solver-engine, newton-coupling-loop]

# Tech tracking
tech-stack:
  added: []
  patterns: [global-system-line-indexing, panel-index-mapping-via-isp]

key-files:
  created:
    - tests/XFoil.Core.Tests/NewtonSystemIndexingTests.cs
  modified:
    - src/XFoil.Solver/Models/ViscousNewtonSystem.cs
    - src/XFoil.Solver/Services/ViscousNewtonAssembler.cs
    - src/XFoil.Solver/Services/ViscousNewtonUpdater.cs
    - src/XFoil.Solver/Numerics/BlockTridiagonalSolver.cs
    - src/XFoil.Solver/Numerics/BandMatrixSolver.cs

key-decisions:
  - "Arrays sized by nsys (global system lines) not maxStations -- prevents side-0/side-1 ibl collision"
  - "SetupISYS copies mapping from EdgeVelocityCalculator into ViscousNewtonSystem"
  - "GetPanelIndex uses ISP-based mapping (ISP-ibl for upper, ISP+ibl for lower) for correct DIJ lookup"
  - "u2_uei chain factor added to VM assembly for incompressible-to-compressible velocity conversion"
  - "Ue update computed from full DIJ sum in ViscousNewtonUpdater (was hardcoded to 0.0)"
  - "TransitionModel.CheckTransition called from assembler BL march for natural transition detection"

patterns-established:
  - "Global system line IV indexing: all Newton system array accesses use iv (0..NSYS-1), never per-side ibl"
  - "Panel index mapping: GetPanelIndex(ibl, side, isp, nPanel, blState) for DIJ row/column lookup"

requirements-completed: [VISC-01, VISC-02, VISC-03]

# Metrics
duration: 7min
completed: 2026-03-11
---

# Phase 3 Plan 9: Newton System Indexing Fix Summary

**Fixed Newton system IV-based global indexing to prevent side collision, corrected DIJ panel mapping with u2_uei chain, and wired TransitionModel.CheckTransition into BL march**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-11T14:18:54Z
- **Completed:** 2026-03-11T14:26:00Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments
- ViscousNewtonSystem arrays re-indexed from per-side ibl to global system line IV, eliminating side-0/side-1 overwrite collision
- DIJ coupling fixed with correct panel-index mapping via GetPanelIndex and u2_uei compressibility chain factor
- TransitionModel.CheckTransition wired into ViscousNewtonAssembler's BL march for natural transition detection
- ViscousNewtonUpdater now computes Ue updates from mass coupling via full DIJ summation (was hardcoded to 0.0)
- All 308 existing tests pass plus 4 new NewtonSystemIndexingTests

## Task Commits

Each task was committed atomically:

1. **Task 1: Re-index ViscousNewtonSystem arrays** - `69b28de` (test: failing TDD RED) + `a814d28` (feat: GREEN implementation)
2. **Task 2: Fix DIJ coupling and wire TransitionModel** - `76940ec` (feat)

_Note: Task 1 followed TDD with RED/GREEN commits_

## Files Created/Modified
- `src/XFoil.Solver/Models/ViscousNewtonSystem.cs` - Arrays sized by nsys; added SetupISYS method
- `src/XFoil.Solver/Services/ViscousNewtonAssembler.cs` - IV-based indexing, GetPanelIndex, TransitionModel wiring
- `src/XFoil.Solver/Services/ViscousNewtonUpdater.cs` - IV-based VDEL reads, ComputeUeUpdates from DIJ
- `src/XFoil.Solver/Numerics/BlockTridiagonalSolver.cs` - Direct iv iteration (no ISYS lookup during solve)
- `src/XFoil.Solver/Numerics/BandMatrixSolver.cs` - Updated to match iv-based indexing
- `tests/XFoil.Core.Tests/NewtonSystemIndexingTests.cs` - 4 regression tests for indexing fix

## Decisions Made
- Arrays sized by nsys (global system lines) instead of maxStations to prevent side-0/side-1 collision when both sides have the same ibl value
- SetupISYS copies the ISYS mapping from EdgeVelocityCalculator for bidirectional (ibl,side) <-> iv lookup
- GetPanelIndex uses ISP-based panel mapping (ISP-ibl for upper, ISP+ibl for lower, N+(ibl-IBLTE[1]) for wake) with fallback to simplified linear offset when isp/nPanel not provided
- Added u2_uei chain factor to VM assembly so that DIJ sensitivity (incompressible Ue) is properly converted to compressible velocity sensitivity
- Ue update in ViscousNewtonUpdater now computed from full DIJ sum over all system lines (was previously hardcoded to 0.0, which was the second root cause of O(1e7) corrections)
- TransitionModel.CheckTransition called during BL march when !wake && !turb && ibl > 1 for natural transition detection

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Newton system infrastructure is now correct and ready for plan 10 to wire the full Newton coupling loop into ViscousSolverEngine
- ViscousNewtonAssembler.BuildNewtonSystem, BlockTridiagonalSolver.Solve, and ViscousNewtonUpdater.ApplyNewtonUpdate all use consistent IV-based global indexing
- TransitionModel.CheckTransition is integrated into the assembler, so plan 10 can remove inline transition logic from ViscousSolverEngine

## Self-Check: PASSED

All source files, test files, and commits verified present. 308 tests pass.

---
*Phase: 03-viscous-solver-parity-and-polar-validation*
*Completed: 2026-03-11*
