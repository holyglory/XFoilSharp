# DirectSeedStation15SystemMicroParityTests

## Scope

- Covers the focused station-15 direct-seed owner ladder around `BoundaryLayerSystemAssembler.AssembleStationSystem(...)`.
- Mixes broad emitted-system checks with narrow producer/consumer packet oracles for `TRDIF`, `BLDIF`, and carried station-state handoffs.

## March 21, 2026 harness note

- The iter-3 `bldif_log_inputs` and `bldif_eq3_d2_terms` rigs must replay the original station-15 interval plus the carried station-2 `kinematic_result` / primary state.
- They must not short-circuit the interval to the accepted `transition_interval_inputs` packet before `TransitionModel.ComputeTransitionPoint(...)` runs.
- That accepted packet is a post-`TRCHEK2` product. Feeding it back in as the downstream interval endpoint forces `wf2` artificially close to `1`, produces a false accepted `xt/ut/tt/dt` packet, and reopens `bldif_log_inputs.x2` plus `bldif_eq3_d2_terms.xot1` even though the solver is behaving correctly.

## Current status

- The targeted iter-3 `bldif_log_inputs` and `bldif_eq3_d2_terms` rigs are green again with the corrected harness path.
- The iter-1 `BLDIF`/BLVAR replay is now split more honestly too.
  - A new focused reference capture, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/reference/alpha10_p80_bldif_log_iter1_ref`, isolates the final pre-system `bldif_log_inputs` / `bldif_common` packets for station 15 iteration 1.
  - The new smallest owner surface is `BoundaryLayerSystemAssembler.ComputeBldifLogTerms(...)`, and the focused iter-1 `bldif_log_inputs` / `bldif_common` tests are green on that helper.
  - The older mixed iter-1 `bldif_eq3_d2` chain and station-15 BLVAR DI replay are still red because they replay the broader station-15 system vector, which does not reconstruct the earlier intermediate `ityp=1` log packet seen in the focused reference.
- `Alpha10_P80_DirectSeedStation15_Iteration9AcceptedState_ReplaysToFinalCarry` is green too once the test selects the newest `alpha10_p80_directseed_station16_ref` trace that actually contains both `laminar_seed_system` and `laminar_seed_final`, instead of blindly taking the highest numbered narrowed artifact.
- The March 21 class-wide rerun is now `48/48` green.
- The promoted broad acceptance wave is red again on the transition rows in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-promoted-broad-quick9/latest.md`.
- `transition-seed-system-direct` is `RED` at `1003/1000` and now routes explicitly through `transition-point-iteration-alpha10-station15-free -> transition-interval-inputs -> direct-seed-station15-live-handoff -> bldif-log-terms (when the first stable drift is still inside bldif_log_inputs/bldif_common) -> transition-seed-row11-carry-packets -> transition-seed-gauss-packets` instead of being debugged directly as one broad consumer.
- The newest live probe also showed why that route moved upstream: the current preparation trace is still emitting a laminar-only low-amplification packet family through upper stations `12-16`, so station 15 is only the first failing consumer witness. The actual producer proof point is the shared transition-point / interval lineage, and the broad row should stay on diversified `1000+` shared-code vectors instead of being treated as a station-specific implementation.
- The missing direct-side owner is formal now too. `PredictedEdgeVelocityMicroParityTests.Alpha10_P80_DirectSeed_FreeTransitionPointIteration_BitwiseMatchesFortranTrace` now has a dedicated phase-2 matrix rig, `transition-point-iteration-alpha10-station15-free`, so the direct broad row can route into a real transition-point producer surface instead of borrowing the older alpha-0 accepted-transition witness as its first upstream stop.
- That owner is now measured directly: the focused rerun fails on `ampl1` (`0x40D90421` reference vs `0x3EE1C4E2` managed), and the quick matrix pass in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct-trpoint-quick2/latest.json` records `transition-point-iteration-alpha10-station15-free` as `RED` at `183/1000`.
- The downstream direct `transition_interval_inputs` owner still fails earlier in control flow with `Sequence contains no elements` because the managed prep trace never emits the expected station-15 interval-input packet on that lane.
- The next smaller carried-station-1 BLKIN witness is blocked the same way. `Alpha10_P80_DirectSeed_UpperStation15_CarriedStation1BlkinInputs_BitwiseMatchesFortranTrace` currently stops in `SelectSecondaryForSystem(...)` because the managed lane never emits the expected `bldif_secondary_station` packet before the station-15 system.
- A new local split witness, `Alpha10_P80_DirectSeed_UpperStation14_PrecedingTransitionPointIteration_BitwiseMatchesFortranTrace`, now targets the predecessor transition-point packet one interval earlier. Reflection-probe output already shows that upper-station-14 packet is low before station 15 ever sees it (`0x3EDFB74F -> 0x3EE1C4E2` managed vs `0x4039B74A -> 0x40D90421` reference), so the next formal matrix split should move there if the station-15 free-transition-point rig remains the active owner.
- `trdif-transition-remap` is `RED` at `1272/1000` on row22 `transition_interval_bt2_terms.baseBits`, and its owner route is now recorded explicitly as `transition-interval-inputs -> row13 bt2 owner -> row22 bt2 owner`.
- The promoted inverse row is bounded more honestly now too. Quick mode no longer silently includes the whole `shared-full-mode` capture family for this rig; `quick_include_shared_persisted_captures=false` keeps the promoted quick pass on the curated inverse-window owner/adopted directories, where the row is currently `RED` at `45/1000` on iteration-5 `transition_final_sensitivities.xtA2`.

## TODO

- Keep the direct broad row on the new `bldif-log-terms` helper rig whenever the first stable mismatch is still in the iter-1 log/common preamble, and only widen back out once that owner stays green.
- Promote the new station-14 predecessor transition-point witness into its own matrix rig once the `Alpha10_P80_DirectSeed_UpperStation14_PrecedingTransitionPointIteration_BitwiseMatchesFortranTrace` wrapper is stable enough for quick-mode execution.
- Keep accepted-state replay oracles on a `contains required kinds` selector whenever a newer narrowed trace family can supersede a broad directory numerically without carrying every packet family that older harnesses still expect.
