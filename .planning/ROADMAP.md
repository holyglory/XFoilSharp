# Roadmap: XFoilSharp

## Overview

XFoilSharp needs to close the gap between its current surrogate-based solvers and exact Fortran XFoil parity. The work progresses from foundation cleanup (docs, framework) through inviscid kernel porting, then viscous solver replacement with polar validation, and finally a randomized test bench that provides continuous confidence across thousands of real-world airfoils.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Foundation Cleanup** - Fix stale docs and upgrade to .NET 10
- [ ] **Phase 2: Inviscid Kernel Parity** - Faithful port of PSILIN, GGCALC, CLCALC from Fortran
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
- [ ] 01-02-PLAN.md -- Upgrade to .NET 10, centralize TFM, update test packages, verify clean build

### Phase 2: Inviscid Kernel Parity
**Goal**: Inviscid solver produces CL and CM values within 0.001% of original XFoil for any valid airfoil
**Depends on**: Phase 1
**Requirements**: INV-01, INV-02, INV-03, INV-04
**Success Criteria** (what must be TRUE):
  1. PSILIN (streamfunction influence coefficients) is ported from xpanel.f and produces identical intermediate values to Fortran
  2. GGCALC (gamma/sigma solution) is ported from xsolve.f and produces identical intermediate values to Fortran
  3. CLCALC (lift/moment recovery) is ported from xfoil.f and produces identical intermediate values to Fortran
  4. Inviscid-only CL and CM for any test airfoil match original XFoil output within 0.001%
**Plans**: TBD

Plans:
- [ ] 02-01: TBD
- [ ] 02-02: TBD
- [ ] 02-03: TBD

### Phase 3: Viscous Solver Parity and Polar Validation
**Goal**: Full viscous solver produces CL, CD, CM within 0.001% of original XFoil across all polar sweep types
**Depends on**: Phase 2
**Requirements**: VISC-01, VISC-02, VISC-03, VISC-04, VISC-05, POL-01, POL-02, POL-03
**Success Criteria** (what must be TRUE):
  1. Newton-coupled viscous/inviscid system from xbl.f + xblsys.f replaces the surrogate -- viscous CL, CD, CM match within 0.001%
  2. Full e^n transition model replaces the laminar amplification surrogate
  3. Drag decomposition (form, friction, pressure) matches original XFoil values
  4. Alpha sweep (Type 1), CL sweep (Type 2), and Re sweep (Type 3) polars all match original XFoil within 0.001%
**Plans**: TBD

Plans:
- [ ] 03-01: TBD
- [ ] 03-02: TBD
- [ ] 03-03: TBD

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
| 1. Foundation Cleanup | 1/2 | In Progress | - |
| 2. Inviscid Kernel Parity | 0/? | Not started | - |
| 3. Viscous Solver Parity and Polar Validation | 0/? | Not started | - |
| 4. Randomized Test Bench | 0/? | Not started | - |
