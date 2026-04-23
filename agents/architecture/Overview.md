# Architecture Overview

## System intent

The managed codebase is a layered rewrite of XFoil that separates domain data, aerodynamic solution logic, design and inverse workflows, persistence, and command dispatch. The solver layer has a single linear-vortex inviscid path coupled to a Newton-iterated viscous boundary-layer stack, faithfully porting Fortran XFoil's algorithm. The Hess-Smith alternative path and the surrogate viscous pipeline were removed in Phase 3 cleanup; only the parity-validated linear-vortex + Newton-coupled stack remains.

The Solver also has two precision twins:

- The **float-parity tree** (default `XFoil.Solver.Services.AirfoilAnalysisService`) — `float`-based, byte-for-byte equivalent to the Fortran reference (4455/4455 = 100% bit-exact on the NACA polar gate).
- The **doubled tree** (`XFoil.Solver.Double.Services.AirfoilAnalysisService`) — auto-generated `double`-based twin produced by `tools/gen-double/gen-double.py` text substitution. Same algorithm, native double precision throughout. Validation: ~98% convergence on a 5k random Selig sample, ~86% within 1% CD of the float tree.

The doubled tree is exposed via three CLI modes in `tools/fortran-debug/ParallelPolarCompare`:

- `--double-polar NACA Re Nc panelCount αStart αEnd αStep` — generates a complete viscous polar through the doubled tree.
- `--mesh-study NACA Re Alpha Nc` — runs both facades at panels = {80, 120, 160, 200, 240, 320}; reports CL/CD trajectory.
- `--double-sweep <vectors-file> [--sample N]` — runs both facades on every (subset of) reference vectors, reports per-vector agreement plus a top-10 worst-CD-disagreement reporter (gated by `Plausible()`: |CL|≤5, 0≤CD≤1; worst-case rows include iteration count and final RMS residual to distinguish "convergence-edge noise" from "multi-attractor sensitivity").

## Documentation policy

- Documentation changes are part of code changes.
- The governing rule lives in [../DocumentationPolicy.md](../DocumentationPolicy.md).
- New behavior or newly learned behavior must be written back into the nearest `agents/` leaf and any affected parity/TODO file.
- The full-file C# parity ledger lives in `ParityAudit.md`.
- Repo-local developer automation is documented in [RepoAutomation.md](RepoAutomation.md).

## Project dependency graph

- `XFoil.Core`
  - Base domain and geometry services.
  - No project dependencies.
- `XFoil.Solver`
  - Depends on `XFoil.Core`.
  - Owns inviscid, wake, boundary-layer, Newton coupling, and polar sweep logic.
- `XFoil.Design`
  - Depends on `XFoil.Core` and `XFoil.Solver`.
  - Owns geometry editing, surrogate inverse workflows, and direct conformal `MAPGEN`.
- `XFoil.IO`
  - Depends on `XFoil.Core` and `XFoil.Solver`.
  - Owns deterministic export, legacy polar import, and batch session execution.
- `XFoil.ThesisClosureSolver`
  - Depends on `XFoil.Core` and `XFoil.Solver`.
  - Clean-room reimplementation of Drela's MSES closure (§4–§6 of the
    1986 MIT thesis). Parallel to `XFoil.Solver`, same
    `IAirfoilAnalysisService` surface. Closure library + BL marchers
    (Thwaites, closure-laminar/turbulent, thesis-exact-implicit
    laminar + turbulent, wake, composite transition) + source-
    distribution coupling helpers.
  - **Production-ready after F1–F3 (2026-04-22).** Default path is
    fully thesis-exact (laminar + turbulent + wake); opt into the
    Phase-5 one-way source coupling via
    `useSourceDistributionCoupling = true`. See
    `MsesClosurePlan.md`, `MsesSolverReadme.md`,
    `MsesValidation.md`.
- `XFoil.Cli`
  - Depends on `XFoil.Core`, `XFoil.Solver`, `XFoil.Design`, `XFoil.IO`,
    and `XFoil.ThesisClosureSolver`.
  - Owns argument parsing and presentation-only orchestration.

## Architectural layers

### Domain layer

- `XFoil.Core.Models`
  - `AirfoilGeometry`, `AirfoilPoint`, `AirfoilMetrics`, `AirfoilFormat`.
- Characteristics
  - Immutable or effectively immutable inputs.
  - No hidden global state.
  - Used everywhere else as the common language.

### Solver layer

- `XFoil.Solver.Models`
  - Operating-point inputs and results.
  - Linear-vortex workspaces.
  - Boundary-layer and Newton-system state containers.
- `XFoil.Solver.Services`
  - Public façade in `AirfoilAnalysisService`.
  - Linear-vortex inviscid + Newton-coupled viscous path: `CurvatureAdaptivePanelDistributor`, `PanelGeometryBuilder`, `StreamfunctionInfluenceCalculator`, `LinearVortexInviscidSolver`, `ViscousSolverEngine`, `PolarSweepRunner`.
  - Newton helpers: `BoundaryLayerSystemAssembler`, `BoundaryLayerCorrelations`, `EdgeVelocityCalculator`, `InfluenceMatrixBuilder`, `TransitionModel`, `ViscousNewtonAssembler`, `ViscousNewtonUpdater`, `DragCalculator`, `StagnationPointTracker`, `PostStallExtrapolator`, `WakeGapProfile`, `WakeSpacing`.
- `XFoil.Solver.Diagnostics`
  - `JsonlTraceWriter`, `MultiplexTextWriter`, and ambient `SolverTrace`.
  - Supports env-driven trace filtering and trigger-gated ring-buffer capture used by the parity harness.
  - The parity trace surface is intentionally allowed to grow when a blind spot blocks focused debugging; the current TRDIF trace set now includes row 2 / columns 3 and 4 split events so station-15 row23 / row24 bugs can be localized without broad viscous reruns.
- `XFoil.Solver.Numerics`
  - `ParametricSpline`, `ScaledPivotLuSolver`, `BlockTridiagonalSolver`, `BandMatrixSolver`, plus legacy dense and tridiagonal helpers.

### Design layer

- `XFoil.Design.Services`
  - Geometry transforms and edit commands corresponding to `xgdes`.
  - `QSpecDesignService` and `ModalInverseDesignService` corresponding to `xqdes` and `xmdes`.
  - `ConformalMapgenService` corresponding to direct `MAPGEN` logic in `xmdes.f`.

### IO layer

- Deterministic managed export for `.dat` and CSV.
- Legacy saved polar, reference polar, and polar-dump import.
- Session manifest execution to batch multiple analyses and imports.

### CLI layer

- One large command switch in `Program.cs`.
- Stateless command verbs that instantiate and compose service calls.
- No interactive retained shell or plotting state like original `xfoil.f`.

## Development automation boundary

- Repo-local Codex automation now lives in `.codex/` and `.agents/skills/`.
- The installed autonomous-loop wiring is a repository workflow aid, not part of the shipped managed runtime.
- Machine-global bootstrap still lives under `~/.codex/` and must exist before the repo-local hooks will validate.

## Design principles actually visible in code

- Explicit service composition instead of global `INCLUDE` state bags.
- Array-heavy internals are isolated behind typed models and façade services.
- Headless-first workflows: most features end in structured data or file export.
- Regression-driven migration: tests sit close to the migrated subsystem.
- Compatibility shims are kept visible rather than silently changing behavior; obsolete APIs throw and deprecated CLI commands print notices.

## Testing shape

- A single `tests/XFoil.Core.Tests` project covers all managed projects.
- Focused parity orchestration now has a registry-driven front door in `tools/fortran-debug/run_micro_rig_matrix.py`; see [MicroRigMatrixHarness.md](MicroRigMatrixHarness.md).
- Full-run parity is now explicitly a locator, not an inner-loop debugger.
  - `tools/fortran-debug/run_managed_case.sh --route-disparity` resolves a `parity_report` into a responsible micro-rig via `tools/fortran-debug/route_full_xfoil_disparity.py`, then pivots into that focused rig instead of encouraging another broad full-run patch cycle.
- Test categories present today:
  - Core geometry and parsing.
  - Solver paneling, wake, transition, numerics, viscous workflows, and parity tracking.
  - IO import/export and session execution.
  - Design geometry and inverse workflows.
  - Diagnostic dump generation for Fortran-vs-C# intermediate comparisons.
- The micro-rig matrix treats targeted oracles as the default parity surface:
  - `quick` mode runs the canonical rig set with `--no-build` and reports the first failing boundary.
  - `full` mode replays deterministic refresh cases so trace-backed rigs can grow toward `>=1000` real vectors without broad ad hoc reruns.
  - full mode can now use a narrower representative verification filter per rig after the harvest, so corpus-growth runs are not forced to rerun a whole suite-sized class just to validate one oracle.
  - Full-mode refresh captures are reused across rigs when the physical case signature matches, so one managed/reference capture can feed multiple record-kind oracles.
  - the accepted-final-state and first-downstream-handoff similarity block now fans out into separate `similarity-seed-final` and `similarity-seed-handoff` child rows, which lets the harness harvest those two corpora independently and keeps the shared-capture scan narrow for each child.
  - the matrix now reports an explicit vector-coverage policy per rig: normal trace-rich rows stay on the `standard` `>=1000` gate, while sparse full-march rows can declare `sparse_full_run` and use a visible `>=25` gate instead of pretending they should scale like packet-rich rigs.
  - the station-16 direct-seed proof is also split by lifecycle now: system, step, and final checks run as separate targeted tests, so a stale mixed reference bundle cannot hide a green local system oracle behind an unrelated missing step packet.
  - stable JSONL trace loading and persisted-corpus discovery are cached inside the harness, so repeated matrix rows do not keep reparsing the same unchanged trace trees.
- The canonical phase-1 matrix is now complete: all 10 locked rigs are green and over `>=1000` real vectors in `tools/fortran-debug/micro-rig-matrix/20260320T055026Z`.
- Phase-2 expansion now lives in the same registry and matrix shape.
  - the first promoted `transition-seed-system` target was split into `transition-seed-system-direct` and `transition-seed-system-inverse` so the direct station-15 seed-system branch and the narrow inverse transition-window branch can run independently instead of hiding each other's corpus limits.
  - a sibling `transition-interval-inputs` rig now tracks the direct-seed TRDIF interval-input producer boundary separately from both seed-system branches.
  - `trdif-transition-remap` is now also active in the same registry, using the row-focused TRDIF remap tests and references as a higher-yield phase-2 follow-on once the direct seed-system branch crossed the `>=1000` gate.
  - `stagnation-interpolation` is now active as a focused `STFIND` packet oracle backed by the dedicated `n0012_re1e6_a10_p80_stagnation` managed case plus the existing alpha-0 stagnation reference traces.
  - `wake-node-geometry` is now active as a focused `wake_node` trace oracle backed by the dedicated `n0012_re1e6_a10_p80_wakenode` managed case and the authoritative alpha-10 wake-column reference trace.
  - `wake-spacing` is now active as a focused `wake_spacing_input` producer oracle backed by the dedicated `n0012_re1e6_a10_p80_wakespacing_v2` managed case and the alpha-10 wake-spacing reference trace.
  - `blvar-cf-upstream` is now active as a focused `blvar_cf_terms` producer oracle using the existing station-5 direct-seed Cf-chain parity test plus the authoritative station-5 and alpha-0 full-trace BLVAR captures; one bounded harvest already pushed it green over the `>=1000` gate.
  - `blvar-turbulent-di-upstream` is now active as a focused `blvar_turbulent_di_terms` producer oracle using the split turbulent-DI packet ladder (`wall -> dfac -> outer -> full chain`) for quick mode, the stronger station-15 direct-seed BLVAR DI replay for representative verification, and the authoritative alpha-10 plus alpha-0 BLVAR trace families that already contribute `1352` unique real vectors under the rig dedupe key.
  - `bldif-log-terms` is now active as a focused `bldif_log_inputs -> bldif_common` helper oracle, which gives the direct-seed branch a machine-verifiable `1000+` owner surface for the log/common preamble without reopening the broader station-system replay.
  - the promoted broad rows are acceptance-only now; reopened broad failures should route into their owner families instead of serving as the inner debug loop.
  - `transition-seed-row11-carry-packets` and `transition-seed-gauss-packets` remain phase-2 shared-code owner families for the active alpha-0 direct branch.
  - `blsolv-packets` and `wake-coupling-packets` are the newest phase-2 shared-code owner families, and they now sit underneath the broad `block-tridiagonal-blsolv` and `inviscid-wake-coupling` acceptance rows respectively.
  - narrow phase-2 rigs can opt out of the broad persisted shared-capture scan when startup speed matters more than immediately inheriting the whole phase-1 shared corpus.
  - the newer inverse and interval-input expansion rigs now also carry deterministic sweep-backed refresh lattices so they are no longer structurally capped at a single seed case.
- Live managed-vs-reference compare is now part of that same harness shape instead of a separate manual workflow.
- The parity tests currently document a remaining Newton fixed-point gap; they do not show solved 0.001% parity.

## Current architectural strengths

- The code is scriptable and testable.
- The parity-critical Newton pieces are explicit and individually inspectable.
- Legacy formats are handled without pulling UI state into solver code.
- Design and inverse logic are decoupled from plotting and menu handling.

## Current architectural weaknesses

- `Program.cs` is still a very large single-file command dispatcher.
- Several CLI viscous parameters are compatibility-only and currently ignored by `CreateViscousSettings`.
- The viscous Newton stack exists, but the current reference tests still show convergence to a non-physical fixed point on a small set of cases.

## TODO

- Break `XFoil.Cli/Program.cs` into command handlers so the code shape matches this map.
- Keep the Newton-solver docs aligned with what the parity tests actually demonstrate.
