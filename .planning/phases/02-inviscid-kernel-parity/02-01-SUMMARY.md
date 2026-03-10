---
phase: 02-inviscid-kernel-parity
plan: 01
subsystem: numerics
tags: [spline, lu-decomposition, tridiagonal, panel-method, xfoil-port]

# Dependency graph
requires:
  - phase: 01-foundation-cleanup
    provides: Clean .NET 10 build with zero warnings
provides:
  - LinearVortexPanelState, InviscidSolverState, LinearVortexInviscidResult state models
  - ParametricSpline with 3 BC modes, segmented fitting, evaluation, derivatives, arc-length, inversion
  - TridiagonalSolver (Thomas algorithm)
  - ScaledPivotLuSolver (Crout's method with scaled partial pivoting)
affects: [02-02, 02-03, 02-04, inviscid-solver-assembly, geometry-pipeline]

# Tech tracking
tech-stack:
  added: []
  patterns: [static-utility-classes-for-numerics, raw-array-apis-matching-fortran-calling-convention, 0-based-indexing-throughout]

key-files:
  created:
    - src/XFoil.Solver/Models/LinearVortexPanelState.cs
    - src/XFoil.Solver/Models/InviscidSolverState.cs
    - src/XFoil.Solver/Models/LinearVortexInviscidResult.cs
    - src/XFoil.Solver/Numerics/TridiagonalSolver.cs
    - src/XFoil.Solver/Numerics/ParametricSpline.cs
    - src/XFoil.Solver/Numerics/ScaledPivotLuSolver.cs
    - tests/XFoil.Core.Tests/ParametricSplineTests.cs
    - tests/XFoil.Core.Tests/ScaledPivotLuSolverTests.cs
  modified: []

key-decisions:
  - "Used static utility classes with raw array parameters for numerics, matching XFoil's Fortran calling convention"
  - "SplineBoundaryCondition as readonly struct with static factory methods for clean BC mode API"
  - "Segmented spline uses duplicate arc-length values to mark segment breaks (matching Fortran SEGSPL convention)"

patterns-established:
  - "Static numerics utilities: no instance state, raw arrays, in-place mutation"
  - "State models: mutable containers with MaxNodes capacity and runtime Resize/InitializeForNodeCount"
  - "Result models: immutable sealed classes with constructor-set properties"

requirements-completed: [INV-01, INV-02, INV-03]

# Metrics
duration: 9min
completed: 2026-03-10
---

# Phase 2 Plan 1: Foundation Numerics Summary

**Node-based state models, parametric spline with 3 BC modes (port of SPLINE/SPLIND/SEGSPL), and scaled-pivot LU solver (port of LUDCMP/BAKSUB) for linear-vorticity inviscid kernel**

## Performance

- **Duration:** 9 min
- **Started:** 2026-03-10T14:11:40Z
- **Completed:** 2026-03-10T14:20:58Z
- **Tasks:** 3
- **Files created:** 8 (3 models + 3 numerics + 2 test files)

## Accomplishments
- Three state models (LinearVortexPanelState, InviscidSolverState, LinearVortexInviscidResult) providing the data layout for the node-based linear-vorticity formulation
- ParametricSpline with zero-2nd-derivative, zero-3rd-derivative, and specified-derivative BCs, plus segmented spline, evaluation, derivative evaluation, arc-length, and Newton inversion
- TridiagonalSolver implementing Thomas algorithm (port of TRISOL)
- ScaledPivotLuSolver with Crout's method and scaled partial pivoting (port of LUDCMP/BAKSUB)
- 13 new tests (8 spline + 5 LU) all passing; 110 existing tests still pass (123 total, 0 regressions)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create state models** - `dd3b19e` (feat)
2. **Task 2: Parametric spline** - `c805ad4` (test: RED), `7dd391f` (feat: GREEN)
3. **Task 3: Scaled-pivot LU solver** - `e0f2c58` (test: RED), `0c98b2f` (feat: GREEN)
4. **Documentation** - `454a9a0` (docs: agent docs for new files)

## Files Created/Modified

### Created
- `src/XFoil.Solver/Models/LinearVortexPanelState.cs` - Node-based panel geometry container
- `src/XFoil.Solver/Models/InviscidSolverState.cs` - Inviscid solver workspace with all arrays
- `src/XFoil.Solver/Models/LinearVortexInviscidResult.cs` - Immutable result with CL, CM, CDP, Cp
- `src/XFoil.Solver/Numerics/TridiagonalSolver.cs` - Thomas algorithm for tridiagonal systems
- `src/XFoil.Solver/Numerics/ParametricSpline.cs` - Full spline system with SplineBoundaryCondition struct
- `src/XFoil.Solver/Numerics/ScaledPivotLuSolver.cs` - LU decomposition with scaled partial pivoting
- `tests/XFoil.Core.Tests/ParametricSplineTests.cs` - 8 tests for spline and tridiagonal solver
- `tests/XFoil.Core.Tests/ScaledPivotLuSolverTests.cs` - 5 tests for LU solver
- `agents/projects/XFoil.Solver/numerics/parametric-spline.md` - Agent doc
- `agents/projects/XFoil.Solver/numerics/tridiagonal-solver.md` - Agent doc
- `agents/projects/XFoil.Solver/numerics/scaled-pivot-lu-solver.md` - Agent doc
- `agents/projects/XFoil.Solver/models/linear-vortex-state.md` - Agent doc

### Modified
- `agents/projects/XFoil.Solver/00-index.md` - Added new children entries
- `agents/architecture/ParityAndTodos.md` - Updated inviscid solver parity status

## Decisions Made
- Used static utility classes with raw array parameters for numerics, matching XFoil's Fortran calling convention for straightforward porting of downstream routines
- SplineBoundaryCondition implemented as readonly struct with static factory methods rather than enum, to support the SpecifiedDerivative(value) case cleanly
- Segmented spline detects corners via duplicate arc-length values (same convention as Fortran SEGSPL) rather than the spacing-ratio approach mentioned in the plan description

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Adjusted arc-length test point count from 201 to 1001**
- **Found during:** Task 2 (parametric spline TDD GREEN)
- **Issue:** With 201 points on a unit circle quadrant, piecewise-linear arc-length sum has O(h^2) error of ~4e-6, exceeding the 1e-6 test tolerance
- **Fix:** Increased point count to 1001 (h^2 error drops to ~1.6e-7)
- **Files modified:** tests/XFoil.Core.Tests/ParametricSplineTests.cs
- **Verification:** Test passes with 1001 points
- **Committed in:** 7dd391f (part of Task 2 GREEN commit)

**2. [Rule 3 - Blocking] Segmented spline uses duplicate-parameter convention instead of spacing ratio**
- **Found during:** Task 2 (parametric spline implementation)
- **Issue:** Plan described segment detection via "arc-length spacing ratio exceeding 3x" but the actual Fortran SEGSPL uses duplicate consecutive S values (S(i) == S(i+1)) to mark segment joints
- **Fix:** Implemented the Fortran convention (duplicate S values) as this is what the actual codebase uses and will produce when handling corners
- **Files modified:** src/XFoil.Solver/Numerics/ParametricSpline.cs, tests/XFoil.Core.Tests/ParametricSplineTests.cs
- **Verification:** Segmented spline test passes with discontinuous derivatives at corner
- **Committed in:** 7dd391f (part of Task 2 GREEN commit)

---

**Total deviations:** 2 auto-fixed (1 test tolerance bug, 1 blocking algorithm correction)
**Impact on plan:** Both fixes necessary for correctness. Matching Fortran's actual segment detection convention is critical for downstream parity.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- State models are ready for the geometry pipeline (02-02) to populate LinearVortexPanelState
- ParametricSpline and TridiagonalSolver are ready for NCALC/APCALC normal and angle computation
- ScaledPivotLuSolver is ready for GGCALC influence matrix factoring
- All numerics use the same static-utility pattern, making downstream integration straightforward

## Self-Check: PASSED

All 8 created files verified present. All 6 commit hashes verified in git log.

---
*Phase: 02-inviscid-kernel-parity*
*Completed: 2026-03-10*
