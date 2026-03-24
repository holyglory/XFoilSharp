# Transition-Seed Gauss Micro Parity Tests

## Scope

- Registry parent: `transition-seed-gauss-packets`
- Shared code owner:
  - `ViscousSolverEngine.SolveSeedLinearSystem`
  - `DenseLinearSystemSolver.Solve`

## Family Model

- Station-4 seed-step iterations remain witness locations only.
- The new gauss parent keeps the producer lane small while harvesting diversified `1000+` corpora across multiple airfoils / Reynolds numbers / alphas.
- This family stays phase-2 while it serves as the shared owner/debug loop for the broader transition-seed direct row.

## Current Owner Split

- `transition-seed-gauss-window`
- `transition-seed-gauss-backsub`

## Current Finding

- The active alpha-0 frontier is still red at the first `gauss_state initial` packet before the seed-step carry is applied.
- That means the remaining work is not a generic LU/backsub implementation gap alone; it is still about the transition-seed matrix handoff into the shared gauss solve.

## TODO

- Grow the new gauss parent on diversified captures so the current iter-5 witness lane becomes a real shared-code producer family.
- Keep routing the next alpha-0 seed-step parity work through the gauss parent before reopening broader `laminar_seed_step` consumers.
