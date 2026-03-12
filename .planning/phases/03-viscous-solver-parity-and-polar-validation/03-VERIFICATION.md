---
phase: 03-viscous-solver-parity-and-polar-validation
verified: 2026-03-12T14:55:00Z
status: gaps_found
score: 7/9 must-haves verified
re_verification:
  previous_status: gaps_found
  previous_score: 7/9
  gaps_closed:
    - "Full BLDIF chain-rule Jacobians implemented in ComputeFiniteDifferences (~65 lines -> ~500 lines) -- commit a9d9507"
    - "VS1[2,2] and VS2[2,2] shape parameter D* sensitivity are now nonzero -- were hardcoded to 0 in round 4"
    - "UPW upwinding derivatives propagate through HK chains to T/D/U -- commit a9d9507"
    - "SIMI handling order fixed to apply before U_UEI chain transform -- commit 3faeef8"
    - "RESIDUAL GAP language removed from all test file docstrings -- commit 58a9a4a, 36fe6ed"
    - "Old round-4 tolerance constants (CL=0.22/0.48, CD=0.90) no longer present in test files"
  gaps_remaining:
    - "Viscous CL, CD, CM do NOT match XFoil within 0.001%. Post-03-17 accuracy is WORSE: CL has wrong sign O(1) error (ref 0.56, actual -2.12); CD ~50x error (ref 0.006, actual 0.29 for NACA 4415). ViscousParityTests: CL_RelativeTolerance=12.0 (1200%), CD_RelativeTolerance=100.0 (10000%). 5 orders of magnitude from 1e-5 target."
    - "All three polar sweep types have the same accuracy degradation. PolarParityTests: CL_RelativeTolerance=32.0 (3200%), CD_RelativeTolerance=100.0 (10000%). Newton solver does not achieve Converged=true at any iteration count."
  regressions:
    - "CL accuracy degraded from ~22% error (round 4) to O(1) wrong-sign error (round 5). The 03-17 full chain-rule Jacobian rewrite made accuracy significantly worse, not better. NACA 0012 a=5: CL went from ~-2.12 (22% error) in round 4 to CL=-2.12 still wrong-sign. NACA 0012 a=0: CL~-3.09 (was 0.0); sign inverted."
    - "Newton solver now does NOT achieve Converged=true at any iteration count. In round 4, solver at least terminated with some bounded state. With full chain-rule Jacobians the solver diverges to a non-physical fixed point at every iteration count tested (up to 200)."
    - "maxViscousIterations kept at 200 (plan 03-18 requested reduction to 50; executor could not reduce it because solver fails entirely at 50 iterations)."
    - "Convergence assertions remain as 'Converged || Iterations >= 10' (plan 03-18 requested strict Converged==true; executor could not achieve this because solver never converges)."
gaps:
  - truth: "Newton-coupled viscous/inviscid system replaces surrogate; CL, CD, CM match XFoil within 0.001%"
    status: failed
    reason: >
      Five rounds of gap closure (plans 03-09 through 03-17) have not achieved 0.001% parity.
      Round 5 (plan 03-17) rewrote ComputeFiniteDifferences with full BLDIF chain-rule Jacobians
      (~500 lines, matching Fortran xblsys.f:1551-1975). VS1[2,2]/VS2[2,2] are now nonzero.
      UPW derivatives propagate through HK chains. SIMI ordering fixed.
      Despite these structurally correct Jacobians, the Newton solver now converges to a
      DIFFERENT spurious fixed point that is physically wrong: CL has wrong sign (e.g.,
      NACA 0012 a=5 produces CL=-2.12 vs ref 0.56), CD is ~50x off (NACA 4415 CD=0.29 vs ref 0.006),
      CM is O(1) off (~2.3 absolute vs ref ~0). Solver does not achieve Converged=true at any
      iteration count up to 200. Accuracy was better before plan 03-17 (CL ~22% vs now O(1)).
      The chain-rule Jacobians are structurally correct but drive the Newton iteration to a
      worse fixed point, indicating the problem is in the overall VISCAL iteration loop,
      STMOVE state management, or QVFUE edge velocity update -- not the Jacobians themselves.
    artifacts:
      - path: "tests/XFoil.Core.Tests/ViscousParityTests.cs"
        issue: "CL_RelativeTolerance=12.0 (1200%), CD_RelativeTolerance=100.0 (10000%), CM_AbsoluteTolerance=5.0. Convergence assertions relaxed to 'Converged || Iterations >= 10'. maxViscousIterations=200. Tolerances are 1200000x and 10000000x wider than the 1e-5 requirement. Accuracy is worse than round 4 (22% CL then vs O(1) now)."
      - path: "src/XFoil.Solver/Services/ViscousSolverEngine.cs"
        issue: "Iteration order (SETBL->BLSOLV->UPDATE->QVFUE->STMOVE) is correct. Full chain-rule Jacobians from 03-17 are being used. But Newton system converges to non-physical fixed point. Possible causes: VISCAL outer loop state management, STMOVE stagnation tracking, or QVFUE DIJ coupling causing divergence to spurious solution with new Jacobians."
      - path: "src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs"
        issue: "ComputeFiniteDifferences is now 500 lines with full chain-rule Jacobians (verified). VS1[2,2]/VS2[2,2] nonzero (verified). UPW derivatives present (24 occurrences). The Jacobians are structurally correct per Fortran BLDIF. The regression introduced by using these Jacobians suggests they interact badly with the current state management or solver loop."
    missing:
      - "Diagnostic run with post-03-17 C# solver using compare_dumps.py -- dump the ViscousSolverEngine state at each Newton iteration and compare VA/VB/VDEL against Fortran reference_dump.txt to find where the spurious fixed point originates."
      - "Determine whether the regression from ~22% to O(1) CL error is caused by: (a) an incorrect chain in the new Jacobians -- compare specific VS1/VS2 values against Fortran debug dump; or (b) the new Jacobians exposing a previously-masked issue in the Newton loop (STMOVE, QVFUE, UPDATE) that was hidden when Jacobians were wrong."
      - "Consider whether to revert ComputeFiniteDifferences to the round-4 state (which, while wrong, gave ~22% accuracy) while debugging the Newton loop, then re-apply correct Jacobians after loop is fixed."
      - "After Newton fixed-point debugging, tighten ViscousParityTests tolerances to 1e-5 and PolarParityTests similarly."

  - truth: "Alpha sweep (Type 1), CL sweep (Type 2), and Re sweep (Type 3) polars match original XFoil within 0.001%"
    status: failed
    reason: "All three sweep types structurally correct and produce correct point counts. Accuracy is the same spurious-fixed-point problem as single-point: CL 3200% error at worst case (alpha=-2: CL=-3.47 vs ref -0.21), CD 10000% tolerance. PolarParityTests: CL_RelativeTolerance=32.0 (3200%), CD_RelativeTolerance=100.0. Tests pass only because tolerances are calibrated to 2x measured worst-case error, not the 0.001% target."
    artifacts:
      - path: "tests/XFoil.Core.Tests/PolarParityTests.cs"
        issue: "CL_RelativeTolerance=32.0 (3200%), CD_RelativeTolerance=100.0 (10000%), CM_AbsoluteTolerance=5.0. Symmetry checks widened to 15.0 (measured -6.27). Near-zero CL tolerance widened to 5.0. CL is antisymmetric with wrong sign across the alpha sweep."
    missing:
      - "Fix Newton solver accuracy (same as truth 1 above)"
      - "Tighten PolarParityTests tolerances to 1e-5 after Newton convergence to correct fixed point is confirmed"

human_verification: []
---

# Phase 3: Viscous Solver Parity and Polar Validation -- Re-Verification Report (Round 5)

**Phase Goal:** Full viscous solver produces CL, CD, CM within 0.001% of original XFoil across all polar sweep types
**Verified:** 2026-03-12T14:55:00Z
**Status:** gaps_found
**Re-verification:** Yes -- after gap closure plans 03-17 and 03-18 (round 5 of gap closure)

## Re-Verification Context

The previous verification (2026-03-12T00:00:00Z, score 7/9, round 4) found that all three previously-identified Newton defects were fixed but the solver still converged to a different solution than Fortran, with ~22% CL / ~90% CD accuracy. Plans 03-17 and 03-18 implemented the next targeted fix: full chain-rule BLDIF Jacobians.

**Plan 03-17 (commits a9d9507, 3faeef8):**
- Rewrote ComputeFiniteDifferences from ~65 simplified lines to ~500 lines of full chain-rule Jacobians matching Fortran BLDIF (xblsys.f:1551-1975)
- Fixed VS1[2,2] and VS2[2,2] shape parameter D* sensitivity -- were hardcoded to 0
- Added UPW upwinding derivatives propagating through HK chains
- Fixed SIMI handling order (before vs after U_UEI chain transform)

**Plan 03-18 (commits 58a9a4a, 36fe6ed):**
- Measured post-03-17 accuracy: CL O(1) wrong-sign error, CD ~50x error, CM O(1) absolute error
- Set tolerances to 2x measured worst-case (CL=12.0, CD=100.0, CM=5.0 absolute)
- Removed RESIDUAL GAP language from docstrings
- All 309 tests pass

The executor confirmed that plan 03-17 made solver accuracy **significantly worse**, not better.

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Newton-coupled viscous/inviscid system replaces surrogate; CL, CD, CM within 0.001% | FAILED | ViscousParityTests.cs line 77: CL_RelativeTolerance=12.0 (1200%), CD_RelativeTolerance=100.0. Measured CL for NACA 0012 a=5: -2.12 vs ref 0.56 (wrong sign). Solver does not achieve Converged=true at any iteration count. |
| 2 | Full e^N transition model replaces laminar amplification surrogate | VERIFIED | TransitionModel.CheckTransition called from ViscousNewtonAssembler (IBL=2, simi check). Inline Arnal constants eliminated in prior rounds. Unchanged. |
| 3 | Drag decomposition (form, friction, pressure) matches original XFoil | PARTIAL | DragCalculator wired post-convergence. CDF+CDP=CD invariant holds. But absolute accuracy ceiling is limited by the spurious Newton fixed point. |
| 4 | Alpha sweep (Type 1), CL sweep (Type 2), Re sweep (Type 3) all within 0.001% | FAILED | All 3 sweep types structurally correct, produce 7/5/4 points respectively. PolarParityTests.cs line 92: CL_RelativeTolerance=32.0 (3200%). Alpha sweep CL has wrong sign across the range. |
| 5 | BL correlation functions ported with Jacobian sensitivities | VERIFIED | BoundaryLayerCorrelations.cs (454 lines), 28+ tests pass. Unchanged. |
| 6 | Full e^N transition model (DAMPL/DAMPL2/AXSET/TRCHEK2) ported | VERIFIED | TransitionModel.cs (560 lines), 20 tests pass. Wired. |
| 7 | BL equation system assembly (BLSYS/BLDIF etc.) ported | VERIFIED | BoundaryLayerSystemAssembler.cs is now 1355 lines. ComputeFiniteDifferences has full chain-rule Jacobians (verified: upw_t/d/u patterns present 24 times). VS1[2,2] and VS2[2,2] are nonzero (lines 852, 857, 863, 866). SIMI handling order fixed. Structurally matches Fortran BLDIF. However the resulting Jacobians drive Newton to a spurious fixed point -- worse accuracy than the pre-03-17 simplified Jacobians. |
| 8 | Viscous/inviscid coupling infrastructure (DIJ, BLSOLV, stagnation, edge velocity) ported | VERIFIED | DUE/DDS forced-change terms in VDEL RHS (from round 4). Iteration order matches VISCAL (SETBL->BLSOLV->UPDATE->QVFUE->STMOVE). All components wired. The coupling infrastructure is correct; the spurious fixed point likely originates in STMOVE or QVFUE interaction with the new Jacobians. |
| 9 | All 3 polar sweep types with warm-start wired into AirfoilAnalysisService | VERIFIED | PolarSweepRunner, AirfoilAnalysisService unchanged. 11+ integration tests pass (within 3200%/10000% tolerances). |

**Score:** 7/9 truths verified (unchanged from rounds 1-4)

### What Changed in Round 5 vs Round 4

**Improvements (genuine fixes):**
- VS1[2,2]/VS2[2,2] are now nonzero: shape parameter D* sensitivity was structurally missing before
- UPW derivative chains are now propagated
- SIMI ordering corrected
- RESIDUAL GAP docstring language removed

**Regressions (things that got worse):**
- CL accuracy degraded from ~22% error (round 4) to O(1) wrong-sign error (round 5)
- NACA 4415 CD degraded from ~90% tolerance needed to ~50x (5000%) tolerance needed
- Newton solver no longer achieves Converged=true at any iteration count (in round 4 it at least terminated with some bounded state using `Iterations >= 50`)
- Test file CL tolerance widened from 0.22 (22%) to 12.0 (1200%); polar CL tolerance from 0.48 (48%) to 32.0 (3200%)
- maxViscousIterations could not be reduced (remained at 200)
- Convergence assertions could not be made strict

**Root cause per 03-18 summary:** "Full chain-rule Jacobians are structurally correct (matching Fortran BLDIF) but the Newton system converges to a different (non-physical) fixed point. This indicates remaining differences in the overall VISCAL iteration loop, STMOVE state management, or QVFUE edge velocity update."

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs` | Full BLDIF chain-rule Jacobians | VERIFIED | 1355 lines. ComputeFiniteDifferences has full chain-rule derivatives. VS1[2,2]/VS2[2,2] nonzero at lines 852/857/863/866. UPW derivatives present (24 occurrences of upw_t/d/u patterns). SIMI ordering fixed. Commits a9d9507, 3faeef8. |
| `tests/XFoil.Core.Tests/ViscousParityTests.cs` | 0.001% tolerances | FAILED | CL_RelativeTolerance=12.0 (1200%), CD_RelativeTolerance=100.0 (10000%), CM_AbsoluteTolerance=5.0. Convergence: `Converged || Iterations >= 10`. maxViscousIterations=200. |
| `tests/XFoil.Core.Tests/PolarParityTests.cs` | 0.001% tolerances | FAILED | CL_RelativeTolerance=32.0 (3200%), CD_RelativeTolerance=100.0 (10000%), CM_AbsoluteTolerance=5.0. |
| `tools/fortran-debug/compare_dumps.py` | Comparison script | VERIFIED | 554 lines. Available for re-running with post-03-17 C# dump. |
| `tools/fortran-debug/reference_dump.txt` | Fortran reference | VERIFIED | 14362 lines. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `ViscousSolverEngine.cs` | `BoundaryLayerSystemAssembler.ComputeFiniteDifferences` | AssembleStationSystem -> ComputeFiniteDifferences | WIRED | Full chain-rule Jacobians now reach Newton assembler via this path. |
| `ViscousSolverEngine.cs` | `ViscousNewtonAssembler.BuildNewtonSystem` | SETBL step, passes uedgPrev/massPrev | WIRED | Verified in round 4. Unchanged. |
| `ViscousSolverEngine.cs` | `BlockTridiagonalSolver.Solve` | BLSOLV step | WIRED | Verified in round 4. Unchanged. |
| `ViscousSolverEngine.cs` | `ViscousNewtonUpdater.ApplyNewtonUpdate` | UPDATE step | WIRED | Verified in round 4. Unchanged. |
| `ViscousSolverEngine.cs` | `UpdateEdgeVelocityDIJCoupling` | QVFUE step | WIRED | Verified in round 4. Unchanged. |
| `ViscousSolverEngine.cs` | `StagnationPointTracker.MoveStagnationPoint` | STMOVE step | WIRED | Verified in round 4. Unchanged. |
| `BoundaryLayerCorrelations` | `ComputeFiniteDifferences` | Correlation derivatives used as chain-rule building blocks | WIRED | CF, HS, HC, DI derivatives from BoundaryLayerCorrelations are chained through HK/RT/M2 primaries to T/D/U Newton variables. Grep confirms cf1_hk/hs1_hk/di1_hk usage patterns present. |

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|----------------|-------------|--------|----------|
| VISC-01 | 03-01..03-17 | Port full Newton-coupled viscous/inviscid system from xbl.f + xblsys.f | PARTIAL | Newton loop structurally correct with VISCAL order. Full chain-rule Jacobians now structurally match Fortran BLDIF. But Newton does not converge (no Converged=true at any iteration count) and accuracy is O(1) wrong-sign CL. "Full Newton-coupled" requires convergence to Fortran's solution, not a spurious fixed point. |
| VISC-02 | 03-04..03-17 | Replace surrogate displacement coupling with true source/displacement in inviscid matrix | PARTIAL | DIJ built and applied. DUE/DDS coupling in VDEL RHS (round 4). But viscous/inviscid coupling accuracy limited by spurious Newton fixed point. |
| VISC-03 | 03-02, 03-09..10 | Port full e^N transition model replacing laminar amplification surrogate | SATISFIED | TransitionModel.CheckTransition wired from both MarchBoundaryLayer and ViscousNewtonAssembler. Unchanged from round 4. |
| VISC-04 | 03-06 | Port full drag decomposition (form, friction, pressure) from original | SATISFIED | DragCalculator with Squire-Young, CDF, CDP, TE base drag. Wired post-convergence. CDF+CDP=CD invariant holds even at spurious fixed point. |
| VISC-05 | 03-05..10, 03-14..18 | Viscous CL, CD, CM match original XFoil within 0.001% | BLOCKED | Current tolerance: CL 1200%, CD 10000%. REQUIREMENTS.md marks "Complete" but ViscousParityTests.cs line 77 shows CL_RelativeTolerance=12.0 and comments document "Newton solver converges to different (non-physical) fixed point." |
| POL-01 | 03-07..10, 03-14..18 | Alpha sweep (Type 1) produces results within 0.001% of original XFoil | BLOCKED | Alpha sweep functional (7 points). CL wrong-sign across range. 3200% CL / 10000% CD tolerance. |
| POL-02 | 03-07..10, 03-14..18 | CL sweep (Type 2) produces results within 0.001% of original XFoil | BLOCKED | CL sweep functional (5 points). Same accuracy problem. |
| POL-03 | 03-07..10, 03-14..18 | Re sweep (Type 3) produces results within 0.001% of original XFoil | BLOCKED | Re sweep functional (4 points). Same accuracy problem. |

**REQUIREMENTS.md contradiction persists (and has worsened):** All VISC-01 through POL-03 remain marked "Complete" in REQUIREMENTS.md. The code contradicts this for VISC-01, VISC-02, VISC-05, POL-01, POL-02, POL-03. The actual accuracy is now worse than when they were first marked "Complete" (O(1) CL error vs ~22% error in round 4).

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `tests/XFoil.Core.Tests/ViscousParityTests.cs` | 77-79 | `CL_RelativeTolerance = 12.0` (1200%), `CD_RelativeTolerance = 100.0` (10000%), `CM_AbsoluteTolerance = 5.0`. Worsened from round 4 (was CL=0.22/CD=0.90). | BLOCKER | Phase goal not verified. Tests pass but tolerance is 1200000x/10000000x above 0.001% requirement. |
| `tests/XFoil.Core.Tests/ViscousParityTests.cs` | 100, 176, 249, 318, 392 | `Assert.True(result.Converged \|\| result.Iterations >= 10, ...)` -- convergence not strict. Solver never achieves Converged=true. | BLOCKER | Newton solver is non-convergent. Plan 03-18 could not restore strict convergence because full chain-rule Jacobians made solver diverge entirely. |
| `tests/XFoil.Core.Tests/ViscousParityTests.cs` | 479 | `maxViscousIterations: 200`. Plan 03-18 intended to reduce this to 50 but could not. | WARNING | Solver runs 200 iterations before producing usable output but never truly converges. |
| `tests/XFoil.Core.Tests/PolarParityTests.cs` | 92-94 | `CL_RelativeTolerance = 32.0` (3200%), `CD_RelativeTolerance = 100.0` (10000%), `CM_AbsoluteTolerance = 5.0`. Worsened from round 4 (was CL=0.48/CD=0.90). | BLOCKER | Same problem for polar sweeps. Symmetry checks widened to 15.0 absolute (measured ~6.27). |
| `tests/XFoil.Core.Tests/ViscousSolverEngineTests.cs` | multiple | `Assert.True(result.Converged \|\| result.Iterations >= 10, ...)`. maxIterations not reduced to 50. | WARNING | Unit tests accept non-convergence. |
| `tests/XFoil.Core.Tests/PolarSweepRunnerTests.cs` | multiple | NaN CD accepted at high alpha; extreme CD tolerated in CL sweep. | WARNING | Post-03-17, solver produces NaN CD at alpha >= 8 in 120-panel sweep -- a physically invalid result treated as acceptable. |

### Human Verification Required

None -- all remaining issues are programmatically identifiable via the diagnostic dump infrastructure and test tolerances.

### Gaps Summary

Both failing truths (1 and 4) share the same root cause: the Newton solver converges to a spurious, non-physical fixed point after the full chain-rule Jacobian rewrite in plan 03-17.

**Key finding from round 5:** The full chain-rule Jacobians from plan 03-17 are structurally correct and match Fortran BLDIF. But using them makes accuracy worse (CL goes from ~22% error with simplified Jacobians to O(1) wrong-sign error with correct Jacobians). This is a strong indicator that the problem is NOT in the Jacobians -- the problem is elsewhere in the Newton iteration: VISCAL outer loop state management, STMOVE stagnation point tracking, or QVFUE edge velocity update. The correct Jacobians expose this underlying problem more severely than the incorrect ones did.

**Recommended diagnostic approach:**
1. Run compare_dumps.py on a new C# diagnostic dump generated with the post-03-17 solver to find where VA/VB/VDEL first diverge from Fortran in the very first Newton iteration. This will identify whether the problem is in the Jacobian assembly output or in the state passed into the first Newton step (initialization).
2. Specifically examine SETBL (initial BL state setup) -- if the initial state is wrong, even perfect Jacobians will find the wrong fixed point.
3. Check QVFUE and STMOVE -- the edge velocity update and stagnation point movement might be sending the Newton iteration into a wrong basin of attraction with the new Jacobians.
4. Consider temporarily testing with the simplified Jacobians but a diagnostic-instrumented loop to isolate whether the loop or the Jacobians are the primary failure mode.

---

_Verified: 2026-03-12T14:55:00Z_
_Verifier: Claude (gsd-verifier)_
_Re-verification: Yes -- after gap closure plans 03-17 and 03-18 (round 5 of gap closure)_
