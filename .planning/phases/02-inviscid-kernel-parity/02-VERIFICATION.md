---
phase: 02-inviscid-kernel-parity
verified: 2026-03-10T00:00:00Z
status: gaps_found
score: 7/8 must-haves verified
re_verification: false
gaps:
  - truth: "Both Hess-Smith and linear-vorticity solvers are selectable through the analysis pipeline"
    status: failed
    reason: "AirfoilAnalysisService reads InviscidSolverType from AnalysisSettings (it is stored there) but never acts on it. The service hardwires HessSmithInviscidSolver unconditionally. No dispatch path to LinearVortexInviscidSolver exists inside the service. The test that was supposed to verify this wiring (per plan 02-04 Task 2 spec: 'A test that creates an InviscidSolverType.LinearVortex in AnalysisSettings and verifies AirfoilAnalysisService uses the new solver') was also not written."
    artifacts:
      - path: "src/XFoil.Solver/Services/AirfoilAnalysisService.cs"
        issue: "Stores AnalysisSettings.InviscidSolverType but never reads it. All analysis methods call this.inviscidSolver (always HessSmithInviscidSolver). No conditional dispatch on InviscidSolverType exists."
    missing:
      - "In AirfoilAnalysisService, read settings.InviscidSolverType and route to LinearVortexInviscidSolver.AnalyzeInviscid(...) when InviscidSolverType.LinearVortex is selected"
      - "A test in LinearVortexInviscidSolverTests.cs (or AirfoilAnalysisServiceTests.cs) that instantiates AnalysisSettings with InviscidSolverType.LinearVortex, calls AirfoilAnalysisService.AnalyzeInviscid, and verifies the result comes from the linear-vorticity solver"
---

# Phase 2: Inviscid Kernel Parity Verification Report

**Phase Goal:** Clean C# linear-vorticity inviscid solver produces aerodynamically correct CL and CM, selectable alongside existing Hess-Smith solver
**Verified:** 2026-03-10
**Status:** gaps_found
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Panel state container holds node-based arrays with descriptive C# naming | VERIFIED | `LinearVortexPanelState.cs` 127 lines, 0-based arrays: X, Y, ArcLength, XDerivative, YDerivative, NormalX, NormalY, PanelAngle, LE/TE fields |
| 2 | LU decomposition with scaled partial pivoting solves dense systems correctly | VERIFIED | `ScaledPivotLuSolver.cs` 154 lines, Crout's method + scaled pivoting; 5 tests pass |
| 3 | Parametric cubic spline supports 3 BC modes and segmented fitting | VERIFIED | `ParametricSpline.cs` 368 lines; 8 tests pass including arc-length, inversion, all 3 BC modes |
| 4 | Streamfunction influence coefficients computed with singularity handling | VERIFIED | `StreamfunctionInfluenceCalculator.cs` 488 lines; self-influence, TE panel, freestream all handled; 7 tests pass |
| 5 | Panel distribution uses cosine spacing with XFoil counterclockwise ordering | VERIFIED | `CosineClusteringPanelDistributor.cs` 670 lines; 8 tests pass for count, ordering, LE detection, symmetry |
| 6 | System assembly, LU factoring, and basis solution computation work end-to-end | VERIFIED | `LinearVortexInviscidSolver.cs` 575 lines; `AssembleAndFactorSystem` wires `StreamfunctionInfluenceCalculator`, `ScaledPivotLuSolver`, `PanelGeometryBuilder`; flags set correctly |
| 7 | Inviscid CL/CM are aerodynamically correct for multiple test airfoils | VERIFIED | 10 end-to-end tests pass: NACA 0012 CL=0 at alpha=0, CL within 5% of 2*pi*alpha at alpha=5, CL linearity verified, NACA 2412 positive CL and negative CM at alpha=0, panel independence within 5% |
| 8 | Both Hess-Smith and linear-vorticity solvers are selectable through the analysis pipeline | FAILED | `AirfoilAnalysisService` does not read `settings.InviscidSolverType`. The enum and property exist but the dispatch to `LinearVortexInviscidSolver` was never implemented in the service. |

**Score:** 7/8 truths verified

---

## Required Artifacts

| Artifact | Min Lines | Actual | Status | Details |
|----------|-----------|--------|--------|---------|
| `src/XFoil.Solver/Models/LinearVortexPanelState.cs` | 40 | 127 | VERIFIED | 0-based arrays, XML docs, Resize method |
| `src/XFoil.Solver/Models/InviscidSolverState.cs` | 50 | 213 | VERIFIED | All workspace arrays, InitializeForNodeCount |
| `src/XFoil.Solver/Models/LinearVortexInviscidResult.cs` | 15 | 71 | VERIFIED | Immutable with CL, CM, CDP, Cp, alpha derivatives |
| `src/XFoil.Solver/Numerics/ParametricSpline.cs` | 200 | 368 | VERIFIED | All 6 methods including InvertSpline |
| `src/XFoil.Solver/Numerics/ScaledPivotLuSolver.cs` | 80 | 154 | VERIFIED | Decompose + BackSubstitute |
| `src/XFoil.Solver/Services/PanelGeometryBuilder.cs` | 120 | 194 | VERIFIED | ComputeNormals, ComputePanelAngles, ComputeTrailingEdgeGeometry, ComputeCompressibilityParameters, ContinuousAtan2 |
| `src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs` | 200 | 488 | VERIFIED | Full PSILIN port with source terms, TE panel, freestream |
| `src/XFoil.Solver/Services/CosineClusteringPanelDistributor.cs` | 200 | 670 | VERIFIED | PANGEN algorithm with curvature clustering |
| `src/XFoil.Solver/Services/LinearVortexInviscidSolver.cs` | 300 | 575 | VERIFIED | All required methods present and substantive |
| `src/XFoil.Solver/Models/InviscidSolverType.cs` | 5 | 17 | VERIFIED | HessSmith, LinearVortex enum values |
| `tests/XFoil.Core.Tests/LinearVortexInviscidSolverTests.cs` | 100 | 334 | VERIFIED | 16 tests (6 unit + 10 aerodynamic correctness) |

---

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `ParametricSpline.cs` | `TridiagonalSolver.cs` | Spline fitting calls Thomas algorithm | WIRED | `TridiagonalSolver.Solve` called in FitWithBoundaryConditions |
| `InviscidSolverState.cs` | `LinearVortexPanelState.cs` | Solver state dimensions derive from panel | WIRED | Constructor allocates based on maxNodes; InitializeForNodeCount pattern |
| `PanelGeometryBuilder.cs` | `ParametricSpline.cs` | Normal computation uses FitSegmented | WIRED | `ParametricSpline.FitSegmented` called in ComputeNormals |
| `StreamfunctionInfluenceCalculator.cs` | `LinearVortexPanelState.cs` | Reads node coordinates and normals | WIRED | Uses panel.X, panel.Y, panel.NormalX, panel.NormalY throughout |
| `StreamfunctionInfluenceCalculator.cs` | `InviscidSolverState.cs` | Writes to sensitivity arrays | WIRED | Writes StreamfunctionVortexSensitivity, VelocityVortexSensitivity, etc. |
| `CosineClusteringPanelDistributor.cs` | `ParametricSpline.cs` | Uses spline fitting and evaluation | WIRED | FitWithBoundaryConditions, Evaluate, ComputeArcLength, InvertSpline all called |
| `CosineClusteringPanelDistributor.cs` | `LinearVortexPanelState.cs` | Populates panel state | WIRED | Calls panel.Resize, writes panel.X, panel.Y, panel.ArcLength, etc. |
| `LinearVortexInviscidSolver.cs` | `StreamfunctionInfluenceCalculator.cs` | System assembly calls ComputeInfluenceAt | WIRED | `StreamfunctionInfluenceCalculator.ComputeInfluenceAt` called in AssembleAndFactorSystem loop |
| `LinearVortexInviscidSolver.cs` | `ScaledPivotLuSolver.cs` | Factors and back-substitutes influence matrix | WIRED | `ScaledPivotLuSolver.Decompose` and `BackSubstitute` called |
| `LinearVortexInviscidSolver.cs` | `CosineClusteringPanelDistributor.cs` | Full pipeline via AnalyzeInviscid | WIRED | `CosineClusteringPanelDistributor.Distribute` called in AnalyzeInviscid |
| `LinearVortexInviscidSolver.cs` | `PanelGeometryBuilder.cs` | System assembly calls geometry builder | WIRED | ComputeNormals, ComputePanelAngles, ComputeTrailingEdgeGeometry, ComputeCompressibilityParameters all called |
| `AirfoilAnalysisService.cs` | `InviscidSolverType.cs` (via `AnalysisSettings`) | Service selects solver from settings | NOT WIRED | `AnalysisSettings.InviscidSolverType` property exists and is set, but `AirfoilAnalysisService` never reads it. The field `this.inviscidSolver` is always `HessSmithInviscidSolver`. No conditional dispatch exists. |

---

## Requirements Coverage

| Requirement | Source Plans | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| INV-01 | 02-01, 02-02 | Port PSILIN (streamfunction influence coefficients) faithfully from xpanel.f | SATISFIED | `StreamfunctionInfluenceCalculator.cs` implements PSILIN with linear vorticity integrals, self-influence protection, TE panel, source terms |
| INV-02 | 02-01, 02-04 | Port GGCALC (gamma/sigma solution) faithfully from xsolve.f | SATISFIED | `LinearVortexInviscidSolver.AssembleAndFactorSystem` implements GGCALC: (N+1)x(N+1) system, Kutta condition, sharp TE bisector, LU solve, basis solutions |
| INV-03 | 02-01, 02-04 | Port CLCALC (lift/moment coefficient recovery) faithfully from xfoil.f | SATISFIED | `LinearVortexInviscidSolver.IntegratePressureForces` implements CLCALC with Karman-Tsien correction and DG*DX/12 second-order CM correction term |
| INV-04 | 02-03, 02-04 | Inviscid CL, CM match original XFoil within 0.001% for any valid airfoil | PARTIALLY SATISFIED | For Phase 2, aerodynamic correctness verified (within 5% of thin-airfoil theory). Exact 0.001% parity deferred to Phase 4 per design. The solver selection gap means `AirfoilAnalysisService` cannot route to this solver, limiting its usability from the main analysis pipeline. |

---

## Anti-Patterns Found

No TODO, FIXME, placeholder, or stub anti-patterns were found in any phase 2 implementation files. All files contain substantive implementations.

---

## Human Verification Required

None — all critical behaviors are covered by automated tests that pass.

---

## Gaps Summary

One gap blocks complete goal achievement.

**Gap: Solver selectability through the analysis pipeline is unwired.**

The phase goal explicitly requires: "Clean C# linear-vorticity solver selectable alongside existing Hess-Smith solver." The mechanism was partially built — `InviscidSolverType` enum exists, `AnalysisSettings` stores the selection, and `LinearVortexInviscidSolver.AnalyzeInviscid` works correctly as a standalone entry point (used by all 16 tests). However, `AirfoilAnalysisService` — the main analysis pipeline — never consults `settings.InviscidSolverType`. It hardwires `HessSmithInviscidSolver` through its private `inviscidSolver` field. The task summary (02-04-SUMMARY.md) claims this was done; it was not.

The fix is localized: `AirfoilAnalysisService.AnalyzeInviscid` and related methods need to check `settings.InviscidSolverType` and call `LinearVortexInviscidSolver.AnalyzeInviscid(...)` when `LinearVortex` is selected. The companion test verifying this dispatch also needs to be added.

Note: the `LinearVortexInviscidSolver` itself is complete and correct. The aerodynamic correctness truths (INV-01 through INV-03 implementations, and the CL/CM correctness of INV-04) are all satisfied. This gap is purely the pipeline wiring, not a solver correctness issue.

---

_Verified: 2026-03-10_
_Verifier: Claude (gsd-verifier)_
