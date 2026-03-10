# AGENTS.md

## Purpose

This repository is a managed C# rewrite of XFoil. Work here should push the codebase toward a modern, controllable implementation while keeping discrepancies with original XFoil visible.

## Code Areas

- `src/XFoil.Core`
  - Domain models, geometry parsing, normalization, metrics, NACA generation.
- `src/XFoil.Solver`
  - Inviscid solver, wake logic, viscous state, coupling, aerodynamic workflows.
- `src/XFoil.Design`
  - Geometry editing, inverse-design workflows, conformal `MAPGEN`.
- `src/XFoil.IO`
  - Exporters, legacy format importers, session execution.
- `src/XFoil.Cli`
  - Headless command surface and workflow wiring.
- `tests/XFoil.Core.Tests`
  - Regression suite for all managed projects.
- `agents/`
  - Living documentation tree for architecture, modules, classes, methods, parity gaps, and TODOs.

## Working Rules

- Prefer direct code changes over long proposals unless the user explicitly asks for planning only.
- Preserve the managed architecture: explicit services, explicit models, minimal hidden state.
- Do not silently claim parity with original XFoil. Record approximations, surrogates, and gaps.
- Keep changes small, testable, and scriptable.
- Do not introduce interactive-only behavior unless the task requires it.

## Documentation Policy

Documentation maintenance is mandatory.

- Whenever code is added, changed, removed, or better understood, update the relevant Markdown files under `agents/` in the same work item.
- A change is not complete until those docs are updated.

Minimum required documentation updates:

- Update the nearest leaf doc for the changed class, service, model, command group, or module.
- Update the relevant project `00-index.md` if responsibilities, children, or public entry points changed.
- Update `agents/architecture/ParityAndTodos.md` when parity changed, a new discrepancy was found, or a TODO should be added/removed/reprioritized.
- Update `agents/architecture/Overview.md` when architecture, project boundaries, or dependency shape changed.

What counts as new knowledge:

- A new feature.
- A new limitation or discrepancy.
- A refactor that changes ownership, flow, dependencies, or public API.
- A newly discovered behavior in existing code.
- A new file format, command, import/export behavior, or workflow.

Documentation style:

- Prefer many small Markdown files over large monolithic docs.
- Keep docs concise.
- Put detail near the implementation it describes.
- Keep TODOs in the most relevant local doc, not only in a central list.

Read first:

- `agents/README.md`
- `agents/DocumentationPolicy.md`
- `agents/architecture/Overview.md`
- `agents/architecture/ParityAndTodos.md`

## Parity Rules

When touching solver, design, or IO behavior, compare against original XFoil expectations where relevant.

- If behavior matches legacy better, note that in `agents/`.
- If behavior is still surrogate or approximate, say so explicitly in `agents/`.
- If a legacy feature is still missing, add or keep a `TODO` in the closest relevant doc.

Current major known gaps include:

- Inviscid kernel/coefficient path is not yet a proven direct parity port.
- Viscous coupling remains weaker than original XFoil's fully coupled system.
- Transition modeling remains weaker than a full legacy-style e^n treatment.
- Some design and inverse workflows still use managed surrogates.
- Plotting, interactive shell behavior, and ORRS workflows are not fully ported.

## Testing Rules

- Run targeted tests for the changed subsystem whenever possible.
- Prefer `dotnet test src/XFoil.CSharp.slnx` after meaningful cross-cutting changes.
- If tests are not run, state that explicitly.
- If behavior is approximated rather than fully validated, state that in both the user-facing summary and the relevant `agents/` doc.

## CLI And Output Rules

- If you add or change a CLI command, update the relevant file under `agents/projects/XFoil.Cli/`.
- Keep CLI behavior headless and deterministic.
- Prefer structured outputs and stable file formats over ad hoc console-only behavior.

## Design And Solver Rules

- Prefer extending existing services over burying logic in `Program.cs`.
- Keep numerical assumptions visible in models or services, not implicit in unrelated orchestration code.
- When implementing approximations, document the approximation and its intended replacement path.

## Final Check Before Finishing

Before considering the task done, verify:

- Code changes are implemented.
- Relevant tests or smoke checks were run, or the omission was stated.
- Relevant `agents/` docs were updated.
- Parity/TODO notes were adjusted if the work changed the migration status.
