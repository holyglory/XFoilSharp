# Roadmap: XFoilSharp

## Overview

XFoilSharp needs to close the gap between its current surrogate-based solvers and exact Fortran XFoil parity. The work progresses from foundation cleanup (docs, framework) through inviscid kernel porting, then viscous solver replacement with polar validation, and finally a randomized test bench that provides continuous confidence across thousands of real-world airfoils.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Foundation Cleanup** - Fix stale docs and upgrade to .NET 10
- [ ] **Phase 2: Inviscid Kernel Parity** - Clean C# implementation of XFoil's linear-vorticity inviscid solver alongside existing Hess-Smith
- [ ] **Phase 3: Viscous Solver Parity and Polar Validation** - Replace surrogates with true viscous system, validate all sweep types
- [ ] **Phase 4: Randomized Test Bench** - Automated random-case testing against reference XFoil binary

## Phase Details

### Phase 1: Foundation Cleanup
**Goal**: Codebase references are accurate and the project builds on .NET 10 with no warnings
**Depends on**: Nothing (first phase)
**Requirements**: DOC-01, DOC-02, FW-01, FW-02
**Success Criteria** (what must be TRUE):
  1. All markdown files under agents/ reference `src/` paths -- no `src-cs/` references remain anywhere
  2. ParityAndTodos.md accurately reflects the current solver state (which routines are surrogate vs ported)
  3. `dotnet build` succeeds targeting net10.0 with zero warnings across all projects in the solution
**Plans**: 2 plans

Plans:
- [x] 01-01-PLAN.md -- Fix stale src-cs/ path references and verify ParityAndTodos.md accuracy
- [x] 01-02-PLAN.md -- Upgrade to .NET 10, centralize TFM, update test packages, verify clean build

### Phase 2: Inviscid Kernel Parity
**Goal**: Clean C# linear-vorticity inviscid solver produces aerodynamically correct CL and CM, selectable alongside existing Hess-Smith solver
**Depends on**: Phase 1
**Requirements**: INV-01, INV-02, INV-03, INV-04
**Success Criteria** (what must be TRUE):
  1. Streamfunction influence coefficients are implemented using the linear-vorticity formulation
  2. System assembly builds and solves the influence matrix with two basis solutions
  3. Pressure integration recovers CL and CM with Karman-Tsien correction and second-order moment correction
  4. Inviscid CL and CM are aerodynamically correct for multiple test airfoils; exact parity refined in Phase 4
**Plans**: 5 plans

Plans:
- [x] 02-01-PLAN.md -- Foundation numerics: state models, parametric spline, scaled-pivot LU solver
- [x] 02-02-PLAN.md -- Geometry pipeline (normals, angles, TE gap) and streamfunction influence computation
- [x] 02-03-PLAN.md -- Cosine-clustering panel distribution generator
- [x] 02-04-PLAN.md -- Solver assembly, solver selection mechanism, and end-to-end aerodynamic tests
- [x] 02-05-PLAN.md -- Gap closure: wire solver dispatch in AirfoilAnalysisService for LinearVortex selection

### Phase 3: Viscous Solver Parity and Polar Validation
**Goal**: Full viscous solver produces CL, CD, CM within 0.001% of original XFoil across all polar sweep types
**Depends on**: Phase 2
**Requirements**: VISC-01, VISC-02, VISC-03, VISC-04, VISC-05, POL-01, POL-02, POL-03
**Success Criteria** (what must be TRUE):
  1. Newton-coupled viscous/inviscid system from xbl.f + xblsys.f replaces the surrogate -- viscous CL, CD, CM match within 0.001%
  2. Full e^n transition model replaces the laminar amplification surrogate
  3. Drag decomposition (form, friction, pressure) matches original XFoil values
  4. Alpha sweep (Type 1), CL sweep (Type 2), and Re sweep (Type 3) polars all match original XFoil within 0.001%
**Plans**: 14 plans

Plans:
- [x] 03-01-PLAN.md -- State models, AnalysisSettings extension, and BL correlation functions (xblsys.f)
- [x] 03-02-PLAN.md -- Full e^N transition model (DAMPL, DAMPL2, AXSET, TRCHEK2)
- [x] 03-03-PLAN.md -- BL equation system assembly (BLSYS, BLDIF, TRDIF, BLMID, TESYS, BLPRV, BLVAR)
- [x] 03-04-PLAN.md -- Coupling infrastructure: DIJ matrix (QDCALC), block-tridiagonal solver (BLSOLV), stagnation point tracker, edge velocity utilities
- [x] 03-05-PLAN.md -- Newton iteration core: SETBL, UPDATE, VISCAL outer loop
- [x] 03-06-PLAN.md -- Drag decomposition (CDCALC) and ViscousSolverEngine drag wiring
- [x] 03-07-PLAN.md -- Polar sweep runner (Type 1/2/3) and AirfoilAnalysisService integration
- [x] 03-08-PLAN.md -- Parity validation tests (single-point and polar sweep against XFoil reference)
- [x] 03-09-PLAN.md -- Gap closure: fix Newton system indexing (IV-based), DIJ coupling, wire TransitionModel
- [x] 03-10-PLAN.md -- Gap closure: rewire ViscousSolverEngine to Newton loop, tighten parity tolerances to 0.001%
- [ ] 03-11-PLAN.md -- Gap closure: instrument Fortran XFoil with debug logging, build and run reference case
- [ ] 03-12-PLAN.md -- Gap closure: add diagnostic logging to C# viscous solver, run reference case dump
- [ ] 03-13-PLAN.md -- Gap closure: compare Fortran vs C# intermediate values, fix Newton Jacobian divergence
- [ ] 03-14-PLAN.md -- Gap closure: tighten parity test tolerances to 0.001% after Newton convergence achieved

### Phase 4: Randomized Test Bench
**Goal**: Automated test bench continuously validates parity using random real-world airfoils against a reference XFoil binary
**Depends on**: Phase 3
**Requirements**: TEST-01, TEST-02, TEST-03, TEST-04, TEST-05, TEST-06, TEST-07, TEST-08, TEST-09
**Success Criteria** (what must be TRUE):
  1. Original XFoil builds from the f_xfoil submodule and runs as a reference binary
  2. Test bench selects random profiles from the Selig database with random Re and panel counts (>160)
  3. Test bench runs identical cases on both C# and Fortran, comparing CL/CD/CM at 0.001% tolerance
  4. On match, test bench continues with new random cases; on mismatch, it writes a detailed log and exits
  5. A 30-minute continuous passing run completes without mismatch
**Plans**: TBD

Plans:
- [ ] 04-01: TBD
- [ ] 04-02: TBD
- [ ] 04-03: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation Cleanup | 2/2 | Complete | 2026-03-10 |
| 2. Inviscid Kernel Parity | 5/5 | Complete | 2026-03-10 |
| 3. Viscous Solver Parity and Polar Validation | 10/14 | Gap closure | - |
| 4. Randomized Test Bench | 0/? | Not started | - |
