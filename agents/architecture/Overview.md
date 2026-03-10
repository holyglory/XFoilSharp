# Architecture Overview

## System intent

The managed codebase is a layered rewrite of XFoil that separates domain data, aerodynamic solution logic, design/inverse workflows, persistence, and user-facing command dispatch.

## Documentation policy

- Documentation changes are part of code changes.
- The governing rule lives in [../DocumentationPolicy.md](../DocumentationPolicy.md).
- New behavior or newly learned behavior must be written back into the nearest `agents/` leaf and any affected parity/TODO file.

## Project dependency graph

- `XFoil.Core`
  - Base domain and geometry services.
  - No project dependencies.
- `XFoil.Solver`
  - Depends on `XFoil.Core`.
  - Owns inviscid, wake, boundary-layer, and coupling logic.
- `XFoil.Design`
  - Depends on `XFoil.Core` and `XFoil.Solver`.
  - Owns geometry editing, `QDES`/`MDES` surrogates, and direct conformal `MAPGEN`.
- `XFoil.IO`
  - Depends on `XFoil.Core` and `XFoil.Solver`.
  - Owns deterministic export, legacy polar import, and batch session execution.
- `XFoil.Cli`
  - Depends on `XFoil.Core`, `XFoil.Solver`, `XFoil.Design`, and `XFoil.IO`.
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
  - Panel geometry and boundary-layer state containers.
- `XFoil.Solver.Services`
  - High-level orchestration in `AirfoilAnalysisService`.
  - Lower-level implementations for inviscid and viscous pieces.
- `XFoil.Solver.Numerics`
  - `CubicSpline`, `DenseLinearSystemSolver`.

### Design layer

- `XFoil.Design.Services`
  - Geometry transforms and edit commands corresponding to `xgdes`.
  - `QSpecDesignService` and `ModalInverseDesignService` corresponding to `xqdes`/`xmdes`.
  - `ConformalMapgenService` corresponding to direct `MAPGEN` logic in `xmdes.f`.

### IO layer

- Deterministic managed export for `.dat` and CSV.
- Legacy saved polar, reference polar, and polar-dump import.
- Session manifest execution to batch multiple analyses/imports.

### CLI layer

- One large command switch in `Program.cs`.
- Stateless command verbs that instantiate and compose service calls.
- No interactive retained shell/session state like original `xfoil.f`.

## Design principles actually visible in code

- Explicit service composition instead of global `INCLUDE` state bags.
- Immutable result objects for most operating-point and workflow outputs.
- Heavy use of constructor-shaped models instead of mutable arrays in shared global memory.
- Headless-first workflows: most features end in structured data or file export.
- Regression-driven migration: tests mirror migrated subsystems.

## Testing shape

- A single `tests/XFoil.Core.Tests` project covers all managed projects.
- Test categories present today:
  - Core geometry and parsing.
  - Solver paneling, wake, compressibility, transition, viscous workflows.
  - IO import/export and session execution.
  - Design geometry and inverse workflows.
  - Direct `MAPGEN` regressions against representative NACA and file-based airfoils.

## Current architectural strengths

- The code is scriptable and testable.
- Most workflows are accessible through pure services.
- Legacy formats are handled without pulling UI state into solver code.
- Design and inverse logic are decoupled from plotting and menu handling.

## Current architectural weaknesses

- `Program.cs` is a very large single-file command dispatcher.
- `XFoil.Solver` contains both inviscid and viscous logic instead of a dedicated `XFoil.Viscous` split planned earlier.
- Viscous coupling remains surrogate-heavy compared with original XFoil.
- Interactive UI/plotting/session behavior from original XFoil is largely absent.

## TODO

- Split `XFoil.Solver` into clearer inviscid and viscous sub-packages or projects if the rewrite continues to grow.
- Break `XFoil.Cli/Program.cs` into command handlers to make the documented architecture match the code more closely.
- Add a dedicated architecture doc for tests if the regression suite keeps expanding.
- Decide whether the missing plotting/session layer should become a managed UI project or remain out of scope.
