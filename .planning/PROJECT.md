# XFoilSharp — Exact Parity C# Port of XFoil

## What This Is

A C# .NET 10 rewrite of XFoil, the classic Fortran airfoil analysis program by Mark Drela. The port targets exact numerical parity (0.001% tolerance) with the original Fortran code for polar generation across all sweep types. The project is ~60-70% complete, with the remaining work focused on closing solver parity gaps, fixing stale documentation, and building an automated randomized test bench against original XFoil.

## Core Value

Polar generation (CL, CD, CM) must produce results within 0.001% of original Fortran XFoil for any valid airfoil at any valid operating condition.

## Requirements

### Validated

<!-- Shipped and confirmed valuable. -->

- ✓ Core geometry parsing, normalization, and NACA 4-digit generation — existing
- ✓ Panel mesh generation (Hess-Smith style) — existing
- ✓ Inviscid coefficient solve with Mach correction — existing (surrogate, needs parity work)
- ✓ Wake geometry generation — existing
- ✓ Boundary-layer topology and viscous state seeding — existing (surrogate, needs parity work)
- ✓ Laminar interval solver — existing (surrogate, needs parity work)
- ✓ Geometry design transforms (flap, TE-gap, LE-radius, contour edit) — existing
- ✓ Inverse design (QSpec, modal, direct MAPGEN) — existing
- ✓ Deterministic IO (CSV, DAT export; legacy polar import) — existing
- ✓ Manifest-driven batch session execution — existing
- ✓ Headless CLI with rich command set — existing
- ✓ Regression test suite (33 test classes) — existing

### Active

<!-- Current scope. Building toward these. -->

- [ ] Fix stale documentation paths (`src-cs/` → `src/`) across agents/ markdown tree
- [ ] Upgrade target framework from net8.0 to .NET 10
- [ ] Achieve exact inviscid kernel parity (port PSILIN/GGCALC/CLCALC faithfully)
- [ ] Achieve exact viscous coupling parity (replace surrogate with true Newton-coupled system from xbl.f/xblsys.f)
- [ ] Achieve exact transition model parity (replace surrogate with full e^n implementation)
- [ ] Achieve exact drag decomposition parity (form, friction, pressure)
- [ ] Alpha sweep polar parity (Type 1) within 0.001%
- [ ] CL sweep polar parity (Type 2) within 0.001%
- [ ] Re sweep polar parity (Type 3, fixed CL varying Re) within 0.001%
- [ ] Build XFoil from f_xfoil submodule as reference binary
- [ ] Build randomized test bench using Selig airfoil database
- [ ] Test bench: random profile selection from Selig database
- [ ] Test bench: random Re and panel count (>160) generation
- [ ] Test bench: run same case on both C# and Fortran XFoil
- [ ] Test bench: compare results at 0.001% tolerance
- [ ] Test bench: on match, repeat with new random case (30-minute timeout)
- [ ] Test bench: on mismatch, write detailed log and exit

### Out of Scope

<!-- Explicit boundaries. Includes reasoning to prevent re-adding. -->

- Interactive shell / menu system — project is intentionally headless/batch
- Plotting / graphical output — no managed plotting abstraction needed
- ORRS stability workflows — deferred, not needed for polar parity
- Cursor-driven interactive design sessions — batch-only design workflows sufficient
- Mobile or web UI — CLI-only

## Context

- Original XFoil Fortran source is in `f_xfoil/` git submodule
- Key Fortran source files for parity: `xfoil.f`, `xbl.f`, `xblsys.f`, `xoper.f`, `xsolve.f`, `xpanel.f`
- Include files with shared state: `XFOIL.INC`, `XBL.INC`, `BLPAR.INC`
- C# architecture uses explicit services and immutable models instead of Fortran COMMON blocks
- Existing agents/ documentation tree tracks parity status and architecture decisions
- Documentation paths reference old `src-cs/` directory structure, now moved to `src/`
- Selig airfoil database at https://m-selig.ae.illinois.edu/ads/archives/coord_seligFmt.zip provides thousands of real-world profiles for testing
- Current solver uses surrogate approaches for viscous coupling and transition — these must be replaced with faithful Fortran ports to achieve 0.001% parity

## Constraints

- **Parity tolerance**: 0.001% on CL, CD, CM values — effectively exact match
- **Tech stack**: C# .NET 10, xUnit for testing
- **Architecture**: Must preserve existing managed architecture (explicit services, explicit models, minimal hidden state) per AGENTS.md
- **Documentation**: All code changes must update corresponding agents/ markdown files per AGENTS.md documentation policy
- **Reference binary**: Must build from f_xfoil submodule, not external binary
- **Test bench panels**: Minimum 160 panels for test cases
- **Test bench timeout**: 30-minute continuous run on match

## Key Decisions

<!-- Decisions that constrain future work. Add throughout project lifecycle. -->

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Target 0.001% parity | Exact match required — surrogates insufficient | — Pending |
| Build reference XFoil from submodule | Reproducible, version-locked reference | — Pending |
| Use Selig database for test profiles | Large real-world dataset covers edge cases | — Pending |
| Upgrade to .NET 10 | User requirement for modern framework | — Pending |
| Keep headless/batch architecture | Consistent with existing design, no interactive shell | — Pending |

---
*Last updated: 2026-03-10 after initialization*
