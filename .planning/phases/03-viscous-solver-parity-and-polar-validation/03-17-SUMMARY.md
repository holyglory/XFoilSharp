---
phase: 03-viscous-solver-parity-and-polar-validation
plan: 17
subsystem: solver
tags: [boundary-layer, jacobian, chain-rule, newton-solver, xfoil-port]

# Dependency graph
requires:
  - phase: 03-viscous-solver-parity-and-polar-validation
    provides: "BoundaryLayerCorrelations with _hk/_rt/_msq derivatives, ComputeFiniteDifferences stub"
provides:
  - "Full BLDIF chain-rule Jacobians for 3 equations x 5 columns x 2 stations"
  - "BLVAR-style primary derivative chains (HK, RT, M2 -> T, D, U)"
  - "ComputeCqChains and ComputeDiChains helpers for chain derivatives"
  - "Nonzero VS1[2,2]/VS2[2,2] shape parameter D* sensitivity"
  - "BLSYS U_UEI chain transformation verified in AssembleStationSystem"
affects: [viscous-solver-parity, newton-convergence, polar-validation]

# Tech tracking
tech-stack:
  added: []
  patterns: ["Chain-rule Jacobian propagation matching Fortran BLVAR/BLDIF/BLSYS"]

key-files:
  created: []
  modified:
    - "src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs"

key-decisions:
  - "ComputeFiniteDifferences rewritten with full chain-rule Jacobians inline (not calling BLVAR separately) for single-file clarity"
  - "ComputeCqChains and ComputeDiChains as private static helpers to encapsulate equilibrium Ctau and dissipation chain derivatives"
  - "BLSYS SIMI handling fixed to apply before U_UEI chain transform matching Fortran execution order"
  - "Incompressible path (M=0): hk_u=0, m_u=0 simplification preserved; compressibility chains ready for future MSQ>0 path"

patterns-established:
  - "Chain-rule Jacobians: correlation derivatives (_hk, _rt, _msq) chained through primary derivatives (hk_t, hk_d, rt_t, rt_u) to Newton variables (T, D, U, X)"

requirements-completed: [VISC-01, VISC-02, VISC-05]

# Metrics
duration: 7min
completed: 2026-03-12
---

# Phase 3 Plan 17: BLDIF Chain-Rule Jacobians and BLSYS Transforms Summary

**Full Fortran BLDIF chain-rule Jacobians with BLVAR-style derivative chains through HK/RT/M2 primaries to T/D/U Newton variables, fixing zero VS1[2,2]/VS2[2,2] shape parameter sensitivity**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-12T14:11:04Z
- **Completed:** 2026-03-12T14:18:46Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments
- Rewrote ComputeFiniteDifferences from ~65 simplified lines to ~500 lines of full chain-rule Jacobians matching Fortran BLDIF (xblsys.f:1551-1975)
- Fixed critical VS1[2,2] and VS2[2,2] shape parameter D* sensitivity (was hardcoded to 0, now contains HS, CF, DI, HC, HA, UPW chain terms)
- Added UPW upwinding derivatives propagating through HK chains to T/D/U variables
- Added ComputeCqChains and ComputeDiChains helpers for full equilibrium Ctau and dissipation derivative chains
- Verified BLSYS chain transformations (U_UEI factor) in AssembleStationSystem

## Task Commits

Each task was committed atomically:

1. **Task 1: Compute BLVAR-style primary derivatives and rewrite BLDIF Jacobians** - `a9d9507` (feat)
2. **Task 2: Add BLSYS chain transformations to AssembleStationSystem** - `3faeef8` (feat)

## Files Created/Modified
- `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs` - Full chain-rule Jacobians in ComputeFiniteDifferences, ComputeCqChains/ComputeDiChains helpers, BLSYS documentation

## Decisions Made
- Inlined BLVAR-style derivative computation directly in ComputeFiniteDifferences rather than calling a separate BLVAR method -- keeps all Jacobian logic in one method for clarity
- Added ComputeCqChains helper to compute equilibrium Ctau (CQ) and its T/D/U/MS chain derivatives, matching Fortran BLVAR lines 853-895
- Added ComputeDiChains helper to compute dissipation (DI) with full derivative chains including DFAC correction, outer layer, and laminar override, matching Fortran BLVAR lines 929-1097
- Preserved incompressible simplification (hk_u=0, m_u=0) while structuring code to support future compressible path

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed SIMI handling order in AssembleStationSystem**
- **Found during:** Task 2
- **Issue:** SIMI station combining (VS2 = VS1 + VS2, VS1 = 0) was applied after Ue chain transform, but Fortran BLSYS applies it before (lines 636-644 vs 647-658)
- **Fix:** Moved SIMI handling to operate on bldif arrays before copying to result with U_UEI transform
- **Files modified:** src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs
- **Verification:** All 42 BoundaryLayer tests pass
- **Committed in:** 3faeef8

---

**Total deviations:** 1 auto-fixed (1 bug fix)
**Impact on plan:** SIMI ordering fix is necessary for correctness at the leading edge similarity station. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Full chain-rule Jacobians now available for Newton solver convergence debugging
- VS1[2,2]/VS2[2,2] nonzero enables proper D* sensitivity in shape parameter equation
- UPW derivative propagation enables correct upwinding near transition
- Next steps: Newton system debugging to close 3-5 order accuracy gap

## Self-Check: PASSED

- FOUND: src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs
- FOUND: .planning/phases/03-viscous-solver-parity-and-polar-validation/03-17-SUMMARY.md
- FOUND: a9d9507 (Task 1 commit)
- FOUND: 3faeef8 (Task 2 commit)

---
*Phase: 03-viscous-solver-parity-and-polar-validation*
*Completed: 2026-03-12*
