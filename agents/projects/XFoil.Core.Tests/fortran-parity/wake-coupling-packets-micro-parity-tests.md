# Wake-Coupling Packets Micro Parity Tests

## Scope

- Registry parent: `wake-coupling-packets`
- Shared code owner:
  - `InfluenceMatrixBuilder.ComputeWakePanelState`
  - `InfluenceMatrixBuilder.ComputeWakeSourceSensitivitiesAtCoreSingle`

## Family Model

- The broad `inviscid-wake-coupling` row is acceptance-only.
- This phase-2 family is the new inner debug loop for that row.
- The child rigs keep wake-panel-state and wake-source NI owner packets separate while the corpus still follows the diversified `1000+` rule across profiles / Reynolds numbers / alphas.

## Current Owner Split

- `wake-coupling-packets-panel-state`
- `wake-coupling-packets-source-ni-terms`

## Current status

- The broad promoted `inviscid-wake-coupling` row remains an honest harvest gap in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-promoted-broad-quick9/latest.md`: `MISSING_VECTORS` at `3/1000`.
- The owner family is now live and narrower than that broad row:
  - `wake-coupling-packets-panel-state` currently has `9/1000` real vectors.
  - `wake-coupling-packets-source-ni-terms` currently has `88/1000` real vectors.
- That keeps wake-coupling routed into explicit owner packets while the broader acceptance row stays a sparse truth surface instead of pretending to be closed.

## Debug Rule

- Route reopened broad wake-coupling failures into this family before touching the broader prefix oracle.
- If the first mismatch is still downstream of both child packets after a short focused pass, add one more wake-specific family split instead of pushing on the broad row.

## TODO

- Grow a dedicated owner corpus for the NI-term child so the broad row can stop looking like a wake-panel-only prefix check.
- Decide whether the next split should be a wake-step packet family or a wider wake-source segment family.
