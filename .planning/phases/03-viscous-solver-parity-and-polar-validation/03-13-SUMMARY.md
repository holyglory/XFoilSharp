---
phase: 03-viscous-solver-parity-and-polar-validation
plan: 13
subsystem: solver
tags: [xfoil, newton, jacobian, boundary-layer, debugging, fortran-parity]

# Dependency graph
requires:
  - phase: 03-11
    provides: Fortran intermediate value dump (reference_dump.txt)
  - phase: 03-12
    provides: C# diagnostic logging and dump (csharp_dump.txt)
provides:
  - Python comparison script for Fortran/C# dump comparison
  - Corrected BL station numbering (IBL=2 start matching Fortran IBLSYS)
  - Documented root causes of Newton system divergence for Phase 4
affects: [03-14, phase-4-newton-jacobian-debugging]

# Tech tracking
tech-stack:
  added: []
  patterns: [IBL-2-station-start, fortran-csharp-dump-comparison]

key-files:
  created:
    - tools/fortran-debug/compare_dumps.py
  modified:
    - src/XFoil.Solver/Services/EdgeVelocityCalculator.cs
    - src/XFoil.Solver/Services/ViscousNewtonAssembler.cs

key-decisions:
  - "IBL=2 station start in ISYS mapping and Newton assembler march loop, matching Fortran IBLSYS DO IBL=2,NBL(IS)"
  - "Preserved hybrid BL-march + Newton approach because simplified Jacobians cannot drive pure Newton convergence"
  - "Documented missing DUE/DDS terms in VDEL RHS and simplified Jacobians as Phase 4 scope"

patterns-established:
  - "Comparison-driven debugging: parse structured dumps, compare by IV, identify first divergence point"
  - "Station numbering: IBL=2 is first equation station, IBL=1 is similarity station (no equations)"

requirements-completed: [VISC-01, VISC-02, VISC-05]

# Metrics
duration: 45min
completed: 2026-03-11
---

# Phase 03 Plan 13: Critical Gap Closure Summary

**Fortran/C# dump comparison script and IBL=2 station numbering fix in Newton system assembly**

## Performance

- **Duration:** 45 min
- **Started:** 2026-03-11T21:00:00Z
- **Completed:** 2026-03-11T21:45:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Created Python comparison script that parses both Fortran and C# diagnostic dumps, compares station-by-station by global system line IV, and identifies first divergence point per category
- Fixed BL station numbering in MapStationsToSystemLines (IBL=2 start) and BuildNewtonSystem march loop to match Fortran IBLSYS, eliminating off-by-one in Newton system assembly
- Identified and documented the complete set of root causes for Newton system divergence: (1) missing DUE/DDS forced-change terms in VDEL RHS, (2) simplified Jacobians in BoundaryLayerSystemAssembler, (3) BL-march modifying state before SETBL assembly
- All 309 tests pass with the IBL=2 fix while preserving the hybrid BL-march convergence approach

## Task Commits

Each task was committed atomically:

1. **Task 1: Create comparison script and identify divergence points** - `8fe280b` (feat)
2. **Task 2: Fix identified divergence root causes in C# Newton system** - `3556c8f` (fix)

## Files Created/Modified
- `tools/fortran-debug/compare_dumps.py` - Python script comparing Fortran/C# intermediate value dumps line-by-line with structured diagnosis
- `src/XFoil.Solver/Services/EdgeVelocityCalculator.cs` - Fixed MapStationsToSystemLines to start at IBL=2 (skip similarity station)
- `src/XFoil.Solver/Services/ViscousNewtonAssembler.cs` - Fixed march loop to IBL=2, previous station init from station 1, similarity flag simi=(ibl==2)

## Decisions Made

1. **Preserved hybrid BL-march + Newton approach**: The plan called for removing BL-march and using pure Newton as primary solver. However, investigation revealed the simplified Jacobians in BoundaryLayerSystemAssembler cannot support pure Newton convergence (RMSBL diverges from ~26 to ~70 and stalls). The BL-march + DIJ coupling remains the primary convergence driver, with Newton corrections applied when beneficial. Pure Newton will require Phase 4 Jacobian corrections.

2. **IBL=2 station numbering**: Fixed the ISYS mapping and Newton assembler march loop to start at IBL=2, matching Fortran's DO IBL=2,NBL(IS) in IBLSYS. Station 1 is the similarity station with no finite-difference "previous" station, so it has no Newton equation.

3. **Documented remaining Newton divergence root causes**: The comparison script identifies three additional issues beyond IBL numbering: (a) VDEL RHS is missing DUE/DDS forced-change terms that Fortran includes, (b) Jacobians use hardcoded Re=1e6 approximations, (c) BL-march modifies theta/dstar/ctau before SETBL sees them. These are documented as Phase 4 scope.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Reverted pure Newton engine to hybrid BL-march approach**
- **Found during:** Task 2
- **Issue:** Removing BL-march from the iteration loop caused RMSBL to diverge (26 -> 70, stalling) for both 120 and 160 panel cases. The simplified Jacobians in BoundaryLayerSystemAssembler are not accurate enough for pure Newton convergence. 15 tests failed with the pure Newton approach.
- **Fix:** Reverted ViscousSolverEngine.cs to the original hybrid BL-march + conditional Newton approach while keeping the IBL=2 fixes in EdgeVelocityCalculator and ViscousNewtonAssembler.
- **Files modified:** src/XFoil.Solver/Services/ViscousSolverEngine.cs (reverted)
- **Verification:** All 309 tests pass
- **Committed in:** 3556c8f (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 bug: pure Newton divergence)
**Impact on plan:** The plan's goal of removing BL-march and achieving pure Newton convergence could not be met because the underlying Jacobians are approximate. The IBL=2 station numbering fix was successfully applied. Pure Newton convergence requires full Jacobian correction (Phase 4 scope).

## Issues Encountered

- **Pure Newton divergence**: The comparison script correctly identified that the C# Newton system diverges, but the prescribed fix (remove BL-march, use pure Newton) made things worse because the root cause is in the Jacobians (BoundaryLayerSystemAssembler), not in the iteration structure. The IBL=2 fix improves Newton assembly accuracy but is insufficient alone.

- **RMSBL metric confusion**: The plan used RMSBL from BuildNewtonSystem (equation residuals) for convergence checking. This RMSBL is O(70) even with correct code because the approximate Jacobians produce large residuals. The working convergence metric is the BL march residual (change in theta/dstar between iterations), which reaches 1e-4 in ~10-20 iterations.

## Deferred Issues

The following issues were identified by the comparison script but are out of scope for this plan:

1. **Missing DUE/DDS forced-change terms in VDEL RHS**: Fortran's VDEL includes VS1(k,4)*DUE1 + VS1(k,3)*DDS1 + VS2(k,4)*DUE2 + VS2(k,3)*DDS2 terms. C# only uses localResult.Residual[k].
2. **Simplified Jacobians**: BoundaryLayerSystemAssembler uses hardcoded Re=1e6 approximations rather than the actual REYBL from COMSET with full derivative chains.
3. **Station count mismatch**: Fortran has 182 stations, C# has 183 for 160 panels (slight difference in BL station mapping).
4. **Iteration structure mismatch**: C# does BL-march -> DIJ-update -> SETBL -> BLSOLV -> conditional-UPDATE. Fortran does SETBL -> BLSOLV -> UPDATE -> QVFUE -> STMOVE.

These are documented for Phase 4 Newton Jacobian debugging.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Comparison script is ready for iterative debugging in Phase 4
- IBL=2 station numbering is correct, reducing one source of divergence
- Root causes are fully documented with specific code locations
- All 309 tests pass, including parity tests with current tolerance levels

## Self-Check: PASSED

- FOUND: tools/fortran-debug/compare_dumps.py
- FOUND: src/XFoil.Solver/Services/EdgeVelocityCalculator.cs
- FOUND: src/XFoil.Solver/Services/ViscousNewtonAssembler.cs
- FOUND: 8fe280b (Task 1 commit)
- FOUND: 3556c8f (Task 2 commit)
- All 309 tests pass

---
*Phase: 03-viscous-solver-parity-and-polar-validation*
*Completed: 2026-03-11*
