# Documentation Maintenance Policy

This policy applies to any future work done by the main agent or by spawned agents in this repository.

## Rule

Whenever code is added, changed, removed, or better understood, the relevant Markdown files under `agents/` must be updated in the same work item.

## Minimum required updates

- Update the nearest leaf document for the changed class, service, model, command group, or module.
- Update any project `00-index.md` file if the set of children, responsibilities, or public entry points changed.
- Update [architecture/ParityAndTodos.md](architecture/ParityAndTodos.md) when the change affects parity with original XFoil, removes a gap, reveals a new discrepancy, or changes a TODO priority.
- Update [architecture/Overview.md](architecture/Overview.md) if the architectural shape, dependency graph, or system boundaries changed.

## What counts as "new knowledge"

- A newly implemented feature.
- A behavioral discovery about existing code.
- A clarified limitation, bug, discrepancy, or parity gap.
- A refactor that changes ownership, flow, dependencies, or public API shape.
- A new command, file format, import/export behavior, or workflow.

## Style rule

- Prefer many small files over one large narrative.
- Keep docs concise.
- Put detail as close as possible to the concrete implementation it describes.
- Record missing features as `TODO` bullets in the most relevant file, not only in a central backlog.

## Completion rule

A code change is not considered complete until the relevant `agents/` docs have been updated and any stale TODOs adjusted.
