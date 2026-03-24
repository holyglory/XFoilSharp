# BldifLogTermsFortranParityTests

## Scope

- Shared-code owner for `BoundaryLayerSystemAssembler.ComputeBldifLogTerms(...)`
- Covers the `bldif_log_inputs -> bldif_common` preamble that feeds wider `BLDIF` owners

## Family Model

- This is a smaller shared-code packet family, not a broad station-system replay.
- The test batch reads curated reference traces that contain both `bldif_log_inputs` and `bldif_common`, then recomputes the log/ratio preamble directly from the captured packet inputs.
- The same batch now also auto-discovers `micro_rig_matrix_bldif-log-terms_*_ref` captures, so bounded matrix harvests can grow this owner without needing a second wrapper test.
- The matrix-side corpus for `bldif-log-terms` now opts into reading all numbered `reference_trace*.jsonl` files inside each curated directory, because this owner's truthful `1000+` depth is spread across those versioned artifacts rather than only the newest file.
- That lets direct-seed and reduced-panel regressions route to the exact log/common transform before reopening a wider `bldif_eq3_d2`, `residual`, or emitted-system consumer.

## Current Status

- The batch is green and validates `>=1000` packet pairs across the curated corpus.
- Registry rig: `bldif-log-terms`
- The newest focused witness is `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/reference/alpha10_p80_bldif_log_iter1_ref`, which proved the iter-1 direct-seed station-15 replay was still too broad for this owner.
- The helper-backed owner is green even while the older mixed iter-1 `BldifEq3D2InputChain` and station-15 BLVAR DI replay remain red, which means the remaining problem there is broader state reconstruction, not bad `xlog/ulog/tlog/hlog` arithmetic.

## Debug Rule

- Route any reopened `bldif_common.xlog/ulog/tlog/hlog` or `bldif_log_inputs` parity drift here first.
- If this batch stays green, move upstream or downstream to the next state-carry owner instead of patching `ComputeBldifLogTerms(...)` or the broader system replay blindly.

## TODO

- Grow `bldif-log-terms` with bounded owner captures if the curated owner-backed corpus ever stops being enough for the `>=1000` gate.
- Keep any reopened direct-seed iter-1 `bldif_common` drift on this helper-backed rig until the next broader state-carry owner is identified.
