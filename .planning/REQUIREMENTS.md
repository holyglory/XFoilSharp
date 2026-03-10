# Requirements: XFoilSharp

**Defined:** 2026-03-10
**Core Value:** Polar generation (CL, CD, CM) must produce results within 0.001% of original Fortran XFoil for any valid airfoil at any valid operating condition.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Documentation

- [ ] **DOC-01**: All agents/ markdown files reference correct `src/` paths (not stale `src-cs/`)
- [ ] **DOC-02**: Parity status in ParityAndTodos.md reflects current solver state after changes

### Framework

- [ ] **FW-01**: All projects target .NET 10 (net10.0)
- [ ] **FW-02**: Solution builds cleanly on .NET 10 with no warnings

### Inviscid Solver Parity

- [ ] **INV-01**: Port PSILIN (streamfunction influence coefficients) faithfully from xpanel.f
- [ ] **INV-02**: Port GGCALC (gamma/sigma solution) faithfully from xsolve.f
- [ ] **INV-03**: Port CLCALC (lift/moment coefficient recovery) faithfully from xfoil.f
- [ ] **INV-04**: Inviscid CL, CM match original XFoil within 0.001% for any valid airfoil

### Viscous Solver Parity

- [ ] **VISC-01**: Port full Newton-coupled viscous/inviscid system from xbl.f + xblsys.f
- [ ] **VISC-02**: Replace surrogate displacement coupling with true source/displacement in inviscid matrix
- [ ] **VISC-03**: Port full e^n transition model replacing current laminar amplification surrogate
- [ ] **VISC-04**: Port full drag decomposition (form, friction, pressure) from original
- [ ] **VISC-05**: Viscous CL, CD, CM match original XFoil within 0.001%

### Polar Generation

- [ ] **POL-01**: Alpha sweep (Type 1) produces results within 0.001% of original XFoil
- [ ] **POL-02**: CL sweep (Type 2) produces results within 0.001% of original XFoil
- [ ] **POL-03**: Re sweep (Type 3, fixed CL varying Re) produces results within 0.001% of original XFoil

### Test Bench

- [ ] **TEST-01**: Build original XFoil from f_xfoil submodule as reference binary
- [ ] **TEST-02**: Download and parse Selig airfoil database profiles
- [ ] **TEST-03**: Random profile selection from Selig database
- [ ] **TEST-04**: Random Re and panel count (>160) generation for each test case
- [ ] **TEST-05**: Run identical case on both C# XFoilSharp and Fortran XFoil
- [ ] **TEST-06**: Compare CL, CD, CM at 0.001% tolerance
- [ ] **TEST-07**: On match, repeat with new random case continuously
- [ ] **TEST-08**: On mismatch, write detailed log (profile, Re, panels, expected vs actual) and exit
- [ ] **TEST-09**: 30-minute timeout for continuous passing run

## v2 Requirements

### Advanced Features

- **ADV-01**: ORRS stability workflow port
- **ADV-02**: Managed plotting abstraction
- **ADV-03**: Interactive shell / menu system

## Out of Scope

| Feature | Reason |
|---------|--------|
| Interactive shell / menu system | Project is intentionally headless/batch |
| Plotting / graphical output | No managed plotting abstraction needed for parity |
| ORRS stability workflows | Deferred, not needed for polar parity |
| Cursor-driven interactive design | Batch-only design workflows sufficient |
| Mobile or web UI | CLI-only |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| DOC-01 | — | Pending |
| DOC-02 | — | Pending |
| FW-01 | — | Pending |
| FW-02 | — | Pending |
| INV-01 | — | Pending |
| INV-02 | — | Pending |
| INV-03 | — | Pending |
| INV-04 | — | Pending |
| VISC-01 | — | Pending |
| VISC-02 | — | Pending |
| VISC-03 | — | Pending |
| VISC-04 | — | Pending |
| VISC-05 | — | Pending |
| POL-01 | — | Pending |
| POL-02 | — | Pending |
| POL-03 | — | Pending |
| TEST-01 | — | Pending |
| TEST-02 | — | Pending |
| TEST-03 | — | Pending |
| TEST-04 | — | Pending |
| TEST-05 | — | Pending |
| TEST-06 | — | Pending |
| TEST-07 | — | Pending |
| TEST-08 | — | Pending |
| TEST-09 | — | Pending |

**Coverage:**
- v1 requirements: 25 total
- Mapped to phases: 0
- Unmapped: 25 ⚠️

---
*Requirements defined: 2026-03-10*
*Last updated: 2026-03-10 after initial definition*
