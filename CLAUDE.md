# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A managed C# port of XFoil 6.97 (MIT Drela's airfoil analysis tool). The goal is a modern, testable, headless-first implementation while keeping discrepancies with original XFoil visible and documented.

## Build & Test Commands

```bash
# Build everything
dotnet build src/XFoil.CSharp.slnx

# Run all tests
dotnet test src/XFoil.CSharp.slnx

# Run a single test by name
dotnet test src/XFoil.CSharp.slnx --filter "FullyQualifiedName~SomeTestClassName.SomeMethodName"

# Run tests in a specific category/class
dotnet test src/XFoil.CSharp.slnx --filter "FullyQualifiedName~ViscousParityTests"

# Run parity matrix harness (requires Fortran reference build)
python3 tools/fortran-debug/run_micro_rig_matrix.py

# Run Phase 2 doubled-tree (native double precision) viscous polar
dotnet run --project src/XFoil.Cli -c Release -- viscous-polar-naca-double 0012 0 8 2

# Phase 2 doubled-tree vs float-parity sweep with envelope guard + worst-case reporter
dotnet build tools/fortran-debug/ParallelPolarCompare/ParallelPolarCompare.csproj -c Release
XFOIL_DISABLE_FMA=1 dotnet run --project tools/fortran-debug/ParallelPolarCompare -c Release --no-build -- \
    --double-sweep tools/fortran-debug/reference/selig_passing.txt --sample 5000
```

No separate lint command — warnings are treated as errors via `Directory.Build.props` (`TreatWarningsAsErrors`). The build itself enforces lint.

## Target Framework

.NET 10.0, C# 12, nullable reference types enabled, implicit usings. All projects inherit settings from the root `Directory.Build.props`.

## Project Dependency Graph

```
XFoil.Core          (no dependencies — domain models, geometry, numerics)
├── XFoil.Solver    (inviscid/viscous solvers, BL coupling, polar sweeps — Fortran-parity)
├── XFoil.ThesisClosureSolver (clean-room MSES-class closure; parallel to XFoil.Solver)
├── XFoil.Design    (geometry transforms, inverse design, MAPGEN)
├── XFoil.IO        (file exporters/importers, session runner)
└── XFoil.Cli       (headless CLI — depends on all above)
```

Single test project: `tests/XFoil.Core.Tests/` covers all managed projects. Test framework is xUnit.

## MSES path — when to use

`XFoil.ThesisClosureSolver` is a parallel viscous analyzer implementing
`IAirfoilAnalysisService`. It uses Drela's MSES-class closure (1986
thesis §4–§6), which is robust through stall where XFoil's lag-
dissipation closure diverges. Default config is fully thesis-exact
(implicit-Newton laminar + implicit-Newton turbulent + wake marcher
with Squire-Young far-field CD).

Pick a solver:

| Need                                  | Solver                        |
|---------------------------------------|-------------------------------|
| Bit-exact Fortran XFoil 6.97 parity   | `XFoil.Solver` (Double tree)  |
| Modern-tree viscous, non-parity       | `XFoil.Solver.Modern`         |
| MSES-class closure, stall-robust      | `XFoil.ThesisClosureSolver`            |

See `agents/architecture/MsesSolverReadme.md` for the user guide,
`MsesValidation.md` for the pinned acceptance numbers, and
`MsesClosurePlan.md` for the phase plan. Known limitation: CL
comes from the inviscid path; full two-way viscous-inviscid coupling
is deferred ("Phase 5 proper").

## Architecture Notes

- **Single inviscid path**: linear-vortex (Newton-coupled), the same algorithm as Fortran XFoil. The Hess-Smith alternative was removed in Phase 3 cleanup along with the surrogate viscous pipeline.
- **Viscous solver**: `ViscousSolverEngine` + `PolarSweepRunner` implement Newton-coupled boundary-layer analysis. `AirfoilAnalysisService` is the main solver facade.
- **Explicit service composition**: no hidden DI container. Services are wired manually.
- **Array-heavy internals** isolated behind typed models at public boundaries.
- **Legacy surrogate methods** in `AirfoilAnalysisService` throw `NotSupportedException` — they exist as API placeholders only.

## Parity Testing

Fortran-vs-managed parity is tracked rigorously:
- Parity tests live in `tests/XFoil.Core.Tests/FortranParity/`
- Micro-rig registry at `tools/fortran-debug/micro_rig_registry.json` drives the parity matrix harness
- Reference Fortran source is in `f_xfoil/` (built via CMake)
- Discrepancies are documented in `agents/architecture/ParityAndTodos.md`

Do not silently claim parity. Record approximations, surrogates, and gaps explicitly.

### Parity Debugging Workflow (MANDATORY)

**Single-case-first cycle** — never run broad vector sweeps while a known disparity exists:

1. **Pick ONE failing case** (e.g. NACA 4415 Re=1e6 α=6 nCrit=12).
2. **Debug it with GDB hex comparison** until the C# output is bit-exact with Fortran for that case. Use `gdb -batch` on the Fortran binary and `XFOIL_SETBL_HEX=1` traces on the C# binary to compare intermediate values line by line.
3. **Only after single-case parity is reached**, run the sweep with `--bfp` (break-at-first-unparity):
   ```bash
   XFOIL_DISABLE_FMA=1 dotnet run --project tools/fortran-debug/ParallelPolarCompare -c Release -- \
       tools/fortran-debug/reference/clean_fortran_polar_vectors_4875.txt --bfp
   ```
4. **Stop at the FIRST unparity** (any non-zero CD or CL ULP). That case becomes the new single case for step 1.
5. **Repeat** until the full sweep passes with 0 unparities.

**ALWAYS use `--bfp` mode** when running sweeps during parity debugging. Full sweeps without `--bfp` are only for final verification after all known unparities are fixed. The sweep reports bit-exact count (0 ULP in both CD and CL) and ULP distribution — the target is 100% bit-exact.

**Do NOT** run broad sweeps to "see how many pass" while debugging — this wastes compute and gives no actionable signal. The only useful test during debugging is the single failing case.

### Parity Fix Policy (MANDATORY — NEVER VIOLATE)

1. **Never revert a fix that was verified correct through GDB hex comparison.** If a sweep pass rate changes after a verified fix, investigate WHY — do not revert. Sweep statistics are noise during active single-case debugging. The fix is correct if the GDB hex comparison shows the C# value matches Fortran for the specific case being debugged.

2. **Never guess or speculate about what a fix should be.** Every fix MUST be based on a concrete GDB hex comparison showing the exact divergent value and the exact Fortran reference value. Read the Fortran code to understand what expression produces the reference value, then match it in C#.

3. **Never run programs "to see what happens" without a specific hex comparison target.** Every run must either: (a) capture specific hex values for comparison via GDB or `XFOIL_SETBL_HEX=1` traces, or (b) verify a specific fix at a specific station.

4. **Never optimize for aggregate pass rates.** The target is 0 ULP at every single vector. A fix that makes one case bit-exact but changes another case's CD is still correct — the other case has its own bug that needs separate debugging.

5. **Always verify fixes at the specific station/case that motivated them** before considering the fix complete. A fix is verified when the GDB hex value matches between C# and Fortran at the exact location where the divergence was traced.

## Documentation Tree

The `agents/` directory is a living documentation tree that **must be updated** alongside code changes. Key entry points:
- `agents/README.md` — reading order
- `agents/architecture/Overview.md` — system architecture
- `agents/architecture/ParityAndTodos.md` — parity gap map and prioritized TODOs
- `agents/architecture/FortranMappingAudit.md` — file-level C#-to-Fortran mapping ledger
- `agents/architecture/MsesSolverReadme.md` — user guide for the MSES solver
- `agents/architecture/MsesValidation.md` — MSES validation snapshot
- `agents/architecture/MsesClosurePlan.md` — MSES phase plan and status

A code change is not complete until relevant `agents/` docs are updated. See `AGENTS.md` for the full documentation policy.

## Working Conventions

- Keep changes small, testable, and scriptable.
- Prefer extending existing services over adding logic to `Program.cs`.
- When fixing a parity/precision bug, search for the same pattern across the codebase and fix the family.
- Keep CLI behavior headless and deterministic; prefer structured outputs over console-only behavior.
- Document approximations and their intended replacement path.
