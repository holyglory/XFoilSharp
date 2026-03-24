# BLSOLV Packets Micro Parity Tests

## Scope

- Registry parent: `blsolv-packets`
- Shared code owner:
  - `BlockTridiagonalSolver.Solve`

## Family Model

- The broad `block-tridiagonal-blsolv` row is acceptance-only.
- This phase-2 family is the new inner debug loop for that row.
- The child rigs keep the forward packet, solved packet, and first-episode witness separate while still harvesting a diversified `1000+` corpus across profiles / Reynolds numbers / alphas.

## Current Owner Split

- `blsolv-packets-forward-row1`
- `blsolv-packets-solved-row1`
- `blsolv-packets-first-episode-witness`

## Current status

- The broad promoted `block-tridiagonal-blsolv` row is `RED` again in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-promoted-broad-quick9/latest.md` at `70/1000`.
- The new split focused tests now isolate the first forward row and first solved row independently, so the broad row no longer has to hide whether the first mismatch belongs to forward elimination or backward substitution.
- The current broad first failure is still the forward packet: `values[1] 0x340806A2 -> 0x340D025A`.

## Debug Rule

- Route reopened broad `BLSOLV` failures into this family first.
- If the first mismatch is still a mixed solve-episode ambiguity after a short focused pass, split the family again instead of pushing the broad row longer.

## TODO

- Grow dedicated owner-backed `vdel_fwd` and `vdel_sol` corpora so the broad row can stop depending on generic full-trace packet provenance.
- Decide whether the current first-episode witness is enough or whether the active red requires one more explicit solve-episode split.
