# Micro-Rig Matrix Harness

## Purpose

`tools/fortran-debug/run_micro_rig_matrix.py` is the focused parity front door for viscous and kernel debugging. It runs registered micro-rigs instead of broad solver reruns, emits one normalized result matrix, and keeps the “what is green, what is failing, what is still missing” view in one place.

The source of truth is `tools/fortran-debug/micro_rig_registry.json`.

The registry now has three buckets plus two promotion lists:

- `phase1_rigs` for the locked canonical 10
- `canonical_phase1_promoted_rig_ids` for phase-2 owner rigs that should now behave like canonical phase-1 rows without physically duplicating the definitions
- `promoted_phase1_rig_ids` for phase-2 entries that must now behave like phase-1 acceptance rigs in the matrix
- `phase2_rigs` for the remaining expansion rigs that are active in the matrix
- `future_backlog` for still-missing targets

## Phase-1 registry

The registry currently locks these 10 canonical rigs:

1. `similarity-seed-system`
2. `similarity-seed-step`
3. `similarity-seed-final-handoff`
4. `laminar-seed-stations-3-5`
5. `direct-seed-station15-system`
6. `predicted-edge-velocity`
7. `dense-linear-system`
8. `streamfunction-micro`
9. `cf-chain`
10. `cq-chain`

Each registry entry declares:

- rig id and display name
- covered module or producer/consumer boundary
- managed xUnit filter
- optional `quick_managed_test_filter` for suite-sized rigs
- Fortran micro-driver target or `none`
- vector source and minimum vector count
- live-compare capability
- artifact-family directories
- default failure classification
- coverage targets
- optional refresh-case recipes and focused trace env
- optional refresh-case sweeps for deterministic corpus growth
- optional `routing_hints` for full-run disparity routing

The routing hints are intentionally separate from corpus ownership:

- `vector_source` still defines what the rig proves and how its real-vector count is computed.
- `routing_hints` only help map a broad full-run `parity_report` onto the right rig or onto a `MISSING_RIG` backlog item.
- Typical routing signals are dump categories (`BL_STATE`, `VDEL_R`, `VSREZ`), live mismatch kinds/scopes, managed owning-scope hints, and a small report-keyword set for the ambiguous transition/direct-seed boundaries.

Vector coverage now has two explicit policy classes:

- `standard` keeps the original `>=1000` unique real-vector gate for trace-rich rigs.
- `sparse_full_run` allows `>=25` unique real vectors for rigs where a full XFoil case usually yields only one or a tiny handful of usable packets.

The matrix and per-rig summaries now print that policy next to the vector count so a lower gate is always visible in the report instead of being hidden as an unexplained registry tweak.

The `similarity-seed-final-handoff` entry now materializes as two child matrix rows, `similarity-seed-final` and `similarity-seed-handoff`, so the accepted-final-state and first-downstream-handoff harvests can run independently without solver-side changes.
Those two child rigs now use the explicit `sparse_full_run` coverage policy because each full march typically contributes only one accepted-final or first-handoff packet.
They also now opt back into persisted shared full-mode captures, because the shared sweep cases already carry the exact `laminar_seed_final` and `blsys_interval_inputs` packets these two sparse rows need and reusing those captures is much cheaper than recollecting them under rig-local ownership.

The same registry also carries a `future_backlog` list of `MISSING_RIG` targets so the coverage map stays stable while new rigs are added.
Promoted expansion rigs originally moved into `phase2_rigs` instead of inflating the canonical phase-1 list.
The current broad acceptance wave keeps the canonical 10 intact but elevates 10 high-value phase-2 entries into the practical phase-1 acceptance set through `promoted_phase1_rig_ids`:

- `transition-seed-system-direct`
- `transition-seed-system-inverse`
- `trdif-transition-remap`
- `stagnation-interpolation`
- `wake-node-geometry`
- `wake-spacing`
- `blvar-cf-upstream`
- `blvar-turbulent-di-upstream`
- `block-tridiagonal-blsolv`
- `inviscid-wake-coupling`

Those promoted rows still live physically in `phase2_rigs` so their provenance and surrounding backlog context stay stable, but the harness now treats them as phase-1 rows for quick-mode persisted-corpus accounting, matrix summaries, and corpus-growth expectations.

The registry can also split a single broad consumer into many station-owned micro rigs through `subrigs`. The current alpha-0 reduced-panel `predicted-edge-velocity-alpha0-station4` work now uses that pattern for contributor ownership: each contributing source station under the upper station-4 `predicted_edge_velocity_term` block can be routed and harvested as its own rig instead of forcing every rerun back through the full broad consumer. The first wave of contributor-owner rows covers upper sources `2..7` and lower sources `2..10`.

That same `subrigs` pattern is now also used one level deeper on the producer side, but with an explicit family-versus-witness split. The alpha-0 reduced-panel remarch lane now has a shared pre-constraint packet family parent, `legacy-remarch-preconstraint-system-packets`, with six child rigs:

- `legacy-remarch-preconstraint-primary-station2`
- `legacy-remarch-preconstraint-eq3-theta`
- `legacy-remarch-preconstraint-eq3-d1`
- `legacy-remarch-preconstraint-eq3-d2`
- `legacy-remarch-preconstraint-eq3-u2`
- `legacy-remarch-preconstraint-residual`

Those rows still use the old lower station-9 alpha-0 case as their first failing witness, but they are named after the shared local `BLDIF` packet family rather than the global airfoil station that exposed the bug. A fresh quick run on March 22 reported:

- `legacy-remarch-preconstraint-primary-station2`: red at `60/1000`
- `legacy-remarch-preconstraint-eq3-theta`: red at `60/1000`
- `legacy-remarch-preconstraint-eq3-d1`: red at `25/1000`
- `legacy-remarch-preconstraint-eq3-d2`: red at `57/1000`
- `legacy-remarch-preconstraint-eq3-u2`: red at `56/1000`
- `legacy-remarch-preconstraint-residual`: red at `1/1000`

That split makes the current owner boundary explicit: the earliest red packet is the shared `bldif_primary_station station=2` input (`t expected=0x3B904888 actual=0x3B941E77`), not the downstream station-9 iteration/final packets.

The old station-specific packet ladder still exists too, but now only as a witness lane under `legacy-remarch-alpha0-lower-station9-witness` with child rigs such as `legacy-remarch-alpha0-lower-station9-iteration` and `legacy-remarch-alpha0-lower-station9-final-system`. Those rows are there to preserve the exact failing trajectory after the shared family closes; they are no longer the family name for the shared code path itself.

That owner boundary moved one level earlier again on the same day. The new shared direct-seed carry parent, `legacy-direct-seed-carry-packets`, now harvests all direct `laminar_seed_system`, `laminar_seed_step`, and `laminar_seed_final` packets across the diversified sweep space while keeping lower station-8 / station-9 only as the first failing witnesses.

The first quick matrix split in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-legacy-direct-seed-carry-quick1/latest.json` established the broad parent:

- `legacy-direct-seed-carry-system`: red at `96/1000`
- `legacy-direct-seed-carry-step`: red at `96/1000`
- `legacy-direct-seed-carry-final`: red at `15/1000`

The next truthful quick split in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-legacy-direct-seed-carry-quick8/latest.md` now decomposes that parent into smaller owner rows while still counting the explicitly curated focused carry corpus:

- `legacy-direct-seed-carry-inputs`: red at `30/1000` (`10` owner + `20` borrowed)
- `legacy-direct-seed-carry-primary-station2`: red at `338/1000` (`285` owner + `53` borrowed)
- `legacy-direct-seed-carry-secondary-station2`: red at `338/1000` (`285` owner + `53` borrowed)
- `legacy-direct-seed-carry-eq3-d2`: red at `337/1000` (`285` owner + `52` borrowed)
- `legacy-direct-seed-carry-residual`: red at `30/1000` (`10` owner + `20` borrowed)
- `legacy-direct-seed-carry-system`: red at `221/1000` (`96` owner + `125` borrowed)
- `legacy-direct-seed-carry-step`: red at `131/1000` (`96` owner + `35` borrowed)
- `legacy-direct-seed-carry-final`: red at `54/1000` (`15` owner + `39` borrowed)

That second split is the important new knowledge. The lower-tail remarch family is not the first producer anymore, and even the broad direct-seed packet trio is no longer the best inner loop. The current first active shared owners are the smaller direct-seed carry packets themselves:

- `bldif_primary_station station=2`
- `bldif_secondary_station station=2`
- `bldif_eq3_d2_terms`
- `bldif_residual`

The lower station-8 `laminar_seed_system` witness remains useful as the broad symptom (`theta 0x3C0B3DF5 -> 0x3C0B3DF4` plus a nontrivial managed eq3 residual), but the matrix now has narrower owner rows in front of it.

The next carry-specific lesson is about owner growth, not just routing. Three diversified focused owner probes were tried under the lower station-8 trigger. Two of them (`2412 / Re=1.4e6 / alpha=2.5 / 15-panel` and `1412 / Re=2.2e6 / alpha=3.5 / 19-panel`) widened the `inputs` / `residual` owner rows from empty to `10` owner vectors each. A third probe (`2312 / Re=7.5e5 / alpha=2 / 25-panel`) widened the broader `laminar_seed_system` frontier but did not add new unique `inputs` / `residual` vectors, so the next harvest step for those rows must search for shape-diverse interval states rather than just more packet-bearing cases.

The matrix now also enforces a diversity rule for all 1000-vector trace rigs. Any such rig must define a refresh space that spans multiple airfoils, multiple Reynolds numbers, and multiple alphas. `run_micro_rig_matrix.py` validates that requirement before running, and each rig summary now records the available refresh diversity counts (`airfoils`, `re`, `alpha`, `panels`, `ncrit`) alongside the vector totals.

That rule is now satisfied across the whole active registry instead of just the newer promoted rows. On March 22, the remaining non-diversified 1000-vector trace rigs (`dense-linear-system`, `streamfunction-micro`, `cf-chain`, `cq-chain`, the alpha-0 station-4 contributor family, and `inviscid-wake-coupling`) were given explicit diversified refresh lattices too, and the validator now passes on the whole active `>=1000` trace-backed set.

There is now also a second promotion path for focused owners. `canonical_phase1_promoted_rig_ids` lets the registry graduate a small set of high-signal phase-2 owner rigs into the canonical phase-1 tier while still sourcing the full rig definition from `phase2_rigs`. The owner-promotion wave now covers 20 transition-carry / sensitivity / gauss-window owners:

- `direct-seed-station15-live-handoff`
- `transition-interval-inputs`
- `accepted-transition-kinematic-alpha0-station4`
- `transition-point-accepted-alpha0-station4-final`
- `transition-seed-system-row11-owner-accepted-transition-point-alpha0-station4-iter3`
- `transition-seed-system-row22-owner-transition-final-sensitivities-alpha0-station4-iter3`
- `transition-seed-system-row22-owner-transition-final-sensitivities-ax-t2-alpha0-station4-iter3`
- `transition-seed-system-row22-owner-final-transition-kinematic-alpha0-station4-iter3`
- `transition-seed-system-row22-owner-transition-interval-sensitivities-alpha0-station4-iter3`
- `transition-seed-step-deltadstar-owner-gauss-window-alpha0-station4-iter5`
- `legacy-direct-seed-carry-packets`
- `legacy-seed-final-alpha0-station4`
- `laminar-seed-final-alpha0-station3`
- `laminar-amplification-carry-alpha0-station3`
- `laminar-seed-system-ampl-alpha0-station3`
- `transition-point-iteration-alpha0-station3-first`
- `transition-seed-system-alpha0-station4-final`
- `transition-seed-step-alpha0-station4-final`
- `transition-interval-inputs-alpha0-station4-final`
- `transition-point-iteration-alpha0-station4-final`

Those additional canonical promotions still use station numbers only as witness locations inside shared code. They are not separate implementations, and their new `1000`-vector targets are backed by diversified profile / Reynolds / alpha refresh sweeps rather than one-case station facts.

That owner-promotion wave is now partially verified in fresh matrix output:

- `accepted-transition-kinematic-alpha0-station4` is now a real canonical phase-1 owner row instead of a false `MISSING_VECTORS` gap. Its vector source now keys off the same accepted `transition_interval_inputs` anchor state that the focused parity test uses to locate the downstream `kinematic_result`, and `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-accepted-transition-kinematic-fix3/20260322T005838676004Z/rigs/accepted-transition-kinematic-alpha0-station4/summary.json` shows it green at `91/1`.
- `transition-interval-inputs` also now has curated persisted-capture globs for the known transition-window / TRDIF families already on disk. The first curated rerun raised visible corpus from the old `5`-vector probe to at least `36` real vectors before any new refresh capture was needed, so the remaining work on that row is now an explicit corpus-growth problem rather than an invisible registry cap.
- The broader `transition-interval-inputs` row now also has an owner-adoption path like inverse. The current curated corpus is still only `123/1000` unique vectors, but the compatible transition-window families that supply those `123` vectors can now be counted as owner-backed through `owner_adopted_persisted_trace_globs`, so the live gap is corpus depth rather than provenance ambiguity.

The alpha-0 station-4 interval-input witness family was also split further on March 22 after the first focused reruns:

- `transition-interval-inputs-alpha0-station4-final` is no longer a one-vector sparse fact. It now constrains `iteration=7` explicitly and follows the same diversified `1000+` corpus rule as the other promoted alpha-0 witness owners.
- `transition-interval-inputs-alpha0-station4-iter8` and `transition-interval-inputs-alpha0-station4-iter14` are new smaller phase-2 interval-input rigs so the remaining red packets are localized by witness iteration instead of being debugged through one broad station-4 interval owner.
- The current focused xUnit status of that split is: station-3 final laminar seed green, station-4 final transition seed system green, station-4 final transition seed step green, station-4 final interval inputs red on `d2`, station-4 iteration-8 interval inputs red on `d1`, and station-4 iteration-14 interval inputs red on `xtX2`.

This owner-promotion list is intentionally separate from `promoted_phase1_rig_ids`:

- `promoted_phase1_rig_ids` tracks broad acceptance rows that should accumulate large honest corpora and report owner-vs-borrowed provenance.
- `canonical_phase1_promoted_rig_ids` tracks focused owner rows that should now participate in the canonical phase-1 work queue and summary counts.
The first two-shard promotion harvest already proved that the change is operationally meaningful:

- `transition-seed-system-direct` immediately landed at `1003/1000` and flipped to `RED` instead of hiding behind `MISSING_VECTORS`.
- `transition-seed-system-inverse` moved from a 5-vector green probe into a triple-digit real corpus and also flipped to `RED`.
- `trdif-transition-remap` crossed the `>=1000` gate and is now a broad-corpus `RED` row.

In other words, promotion is not just bookkeeping. It converts “narrow oracle exists” rows into broad acceptance rows that can fail honestly once enough real trace coverage is present.

When a promoted broad row reopens, the matrix must route back down to the earliest focused owner instead of treating the promoted broad row as the inner debug loop. The current direct-seed station-15 reopening is the live example: the promoted broad downstream consumer symptom (`transition-seed-system-direct`) now routes through the dedicated station-15 owner ladder, and the earliest current bad packet has been registered explicitly as `direct-seed-station15-row13-owner-transition-interval-bt2` rather than being debugged through the broader system oracle.

That same station-15 example also shows the next routing step after an owner closes: once the reopened row13 BT2 packet ladder was replayed back to green, the first bad boundary moved upstream again into `ComputeFiniteDifferences(...)`. The matrix now routes through the narrower iter-3 `bldif_common` UPW producer (`direct-seed-station15-bldif-common-upw-owner`) before the broader `direct-seed-station15-eq1-residual-owner`, and once that producer closed the next owner moved again to the iter-5 eq1 `sa` consumer (`direct-seed-station15-eq1-sa-owner`). After that consumer closed, the moved row12 / iter-7 residual symptom required another producer split, so a trigger-window reference capture was generated specifically to register `direct-seed-station15-bldif-common-upw-owner-iter7` and `direct-seed-station15-bldif-upw-terms-owner-iter7`. When that new UPW owner proved `hk1` was already bad, the workflow created another focused reference capture (`alpha10_p80_bldif_station1_iter7_producer_ref`) and registered the next producer pair (`direct-seed-station15-bldif-primary-station1-owner-iter7` and `direct-seed-station15-kinematic-station1-owner-iter7`) instead of patching the downstream blend. This is the intended routing behavior: keep shrinking onto the earliest still-red packet, and distinguish producer vs consumer ownership explicitly as the frontier moves.
The first promoted expansion target was later split into narrower direct and inverse subrigs:

- `transition-seed-system-direct` tracks the direct-branch station-15 seed-system oracle on the richer laminar-system trace family.
- `transition-seed-system-inverse` tracks the narrow inverse transition-window oracle on the dedicated transition-window reference traces.
- `transition-interval-inputs` tracks the direct-seed TRDIF interval-input oracle on the same transition-window trace family.
- `trdif-transition-remap` now tracks the focused TRDIF `transition_interval_bt2_terms` and `transition_interval_final_terms` remap family on the row-focused transition references plus fresh sweep captures.
- `current-primary-station2-alpha0-station4-iter13` tracks the carried `bldif_primary_station` producer packet that feeds the active alpha-0 reduced-panel station-4 transition-point drift.
- `transition-seed-system-row13-owner-eq1rows-row13-alpha0-station4-iter8` now tracks the earlier `bldif_eq1_rows` row13 producer that feeds the current alpha-0 reduced-panel station-4 iteration-8 `transition_interval_bt2` dtTerm mismatch.
- `transition-seed-system-row22-owner-transition-interval-rows-alpha0-station4-iter8` now tracks the tighter `transition_interval_rows` row22 final-combine producer that feeds the current alpha-0 reduced-panel station-4 iteration-8 `transition_seed_system` / `gauss_state` row22 mismatch.
- `transition-seed-system-row32-owner-eq3-t1-alpha0-station4-iter8` now tracks the next narrowed `bldif_eq3_t1_terms` producer packet that feeds the current alpha-0 reduced-panel station-4 iteration-8 `transition_seed_system` row32 mismatch.
- `transition-seed-system-row11-owner-station2-outer-di-alpha0-station4-iter8` now tracks the focused `blvar_outer_di_terms` `finalDiT` packet that feeds the remaining station-2 secondary `diT` carry into the same reduced-panel iteration-8 branch.
- `direct-seed-station15-iter4-row11-owner-eq1rows-row21` now tracks the focused `bldif_eq1_rows.row21` producer packet immediately before the alpha-10 panel-80 station-15 iteration-4 direct-seed system, so the broad row11 one-bit drift can be routed to either the eq1-rows producer or the downstream system combine instead of being debugged through the full station-15 matrix.

The inverse promoted row now also has a curated borrowed-corpus path. Its registry entry may reuse persisted traces from broader compatible capture families when those traces contain the exact inverse station-15 `transition_seed_system` packet. That changes only accounting, not semantics: the row still matches `side=1`, `station=15`, and `mode=inverse`, and the new provenance counters keep those borrowed vectors separate from dedicated inverse-owner captures.

That last rig is intentionally narrower than the accepted `transition_point_iteration` producer rigs around it:

- the accepted transition-point iteration tests are consumer-side proofs on `TRCHEK2`,
- the new current-primary rig owns the earliest proved bad packet upstream of those consumers, and
- the full-run router should now prefer that producer rig whenever an alpha-0 station-4 accepted-transition or transition-seed disparity still maps back to the same carried station-2 primary packet.

The new row32 rig is also adjacency-anchored by design:

- the focused `bldif_eq3_t1_terms` reference packet does not yet carry full station/iteration tags on the Fortran side,
- so the oracle intentionally selects the last `ityp=2` eq3-t1 producer immediately before the target station-4 iteration-8 `transition_seed_system` packet instead of pretending that the producer is self-identifying.

The same alpha-0 row34 ladder is one step deeper now too. A new focused owner row, `transition-seed-system-row34-owner-compressible-velocity-alpha0-station4-iter3`, tracks the BLPRV `compressible_velocity` packet between the iteration-3 `laminar_seed_step` and the next `transition_seed_system`. Unlike the older sparse row34 witnesses, this one is registered as a normal `standard` 1000-vector rig with diversified sweep growth, because BLPRV packets recur across both the alpha-0 reduced-panel branch and the existing alpha-10 direct-seed compressibility captures.

Focused ad hoc capture refreshes are less brittle now as well. `tools/fortran-debug/run_reference.sh` now verifies or rebuilds the instrumented Fortran binary before it writes a generated ad hoc input deck, so the build cleanup step can no longer delete the just-written deck on the same run. That harness fix is what made the new `alpha0_p12_blprv_iter3_ref` focused BLPRV witness reproducible instead of intermittently failing with a missing input file.

## Modes

### `quick`

- Uses `dotnet test --no-build`.
- Runs the registry filter for each rig.
- If a rig provides `quick_managed_test_filter`, that narrower probe is used instead of the whole class.
- Writes one matrix row per rig with pass/fail counts, first failure, vector counts, artifact paths, and coverage targets.
- Quick mode now keeps the default persisted-capture scan on canonical phase-1 rigs, but active phase-2 expansion rigs only ingest persisted traces that are explicitly curated on the rig via `additional_persisted_trace_globs`.
  - This keeps `--rig all` from walking the whole shared harvest backlog for every phase-2 row.
  - It also keeps focused families like `legacy-direct-seed-carry-inputs` honest in quick mode, because their curated borrowed corpora are still counted instead of disappearing behind phase-2 throughput shortcuts.
- Quick mode only reuses on-disk `summary.json` corpus counts for non-trace-backed rigs now.
  - Trace-backed rigs always recompute current corpus state so owner-vs-borrowed provenance, newly added focused corpora, and changed source routing stay truthful.
  - Full mode still rebuilds corpus state as before, because it must merge fresh captures into the real unique-key set.
- Each rig now emits explicit stderr breadcrumbs at start and completion.
  - The runner prints `start rig=...` and `done rig=... status=... vectors=...` messages so long quick batches can be monitored directly instead of inferring progress from the filesystem.
- Each rig summary now also reports provenance splits for trace-backed corpora.
  - `summary.json` / `matrix.json` now include owner-backed and non-owner unique-vector counts, per-owner counts, and `owner_backed_under_vectorized`.
  - `owner_adopted_persisted_trace_globs` lets a rig explicitly promote a curated compatible persisted family into owner-backed coverage without copying those traces into a new directory tree first.
  - That lets promoted broad rows stay honest when broad compatible evidence exists but the dedicated owner corpus is still thin.
- The matrix-level summary now reports the promoted acceptance wave explicitly too.
  - `matrix.json` / `matrix.md` now include promoted-only `green`, `red`, `missing_vectors`, `harness_error`, and `owner_gap` counts, plus a promoted-broad table showing owner-backed-vs-non-owner unique vectors per promoted row.
  - This keeps “full parity for the promoted broad set” machine-verifiable instead of reconstructing the promoted subset from the registry externally.
- Promoted broad rows now also carry explicit routing metadata in the registry and summaries.
  - Per-rig `summary.json` / `summary.md` and the promoted-broad table now surface `provenance_posture`, `broad_debug_class`, `owner_fix_mode`, and `owner_rig_ids`.
  - That keeps the acceptance-only broad rows honest: a reopened broad row should route immediately into its owner family instead of becoming the inner debug loop by accident.
- Quick mode now also supports `quick_include_shared_persisted_captures`.
  - That lets a rig keep full mode on the broader shared-full harvest path while forcing quick acceptance reruns onto a bounded curated owner/adopted corpus.
  - `transition-seed-system-inverse` now uses that override so its quick truth source stays on the five curated transition-window directories instead of silently scanning the whole shared-full archive.
- Rigs can also override the generic quick/full watchdogs now.
  - `quick_per_rig_timeout_seconds` and `full_per_rig_timeout_seconds` let one slow focused row ask for more wall-clock budget without forcing the whole matrix onto a laxer timeout.
  - `blvar-turbulent-di-upstream` still carries a `360s` quick override in the registry, but that row is no longer expected to lean on it.
  - The old timeout was traced to the historical alpha-10 station-29 wrapper loading a `5.8 GB` managed solver trace into the parity loader.
  - The quick row now routes through the smaller turbulent-DI packet ladder (`wall`, `dfac`, `outer`, `full chain`) instead of that giant-trace path, so the remaining BLVAR DI issue is the representative station-15 replay, not combined-pass startup overhead.

The current promoted-broad truth source is `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-promoted-broad-quick9/latest.md`.

- It currently reports `0` green, `5` red, `4` missing-vector rows, `1` harness error, and `6` promoted owner-gap rows.
- The active promoted reds are `transition-seed-system-direct`, `transition-seed-system-inverse`, `trdif-transition-remap`, `blvar-cf-upstream`, and `block-tridiagonal-blsolv`.
- The current promoted harvest gaps are `stagnation-interpolation`, `wake-node-geometry`, `wake-spacing`, and `inviscid-wake-coupling`.
- `blvar-turbulent-di-upstream` is the one row in that snapshot that is already known to be stale.
  - `quick9` still recorded it as `HARNESS_ERROR`, but the timeout has since been root-caused to the giant historical managed trace path and removed from the quick probe surface.
  - A standalone rerun of the new quick surface is already green at `1352/1000` real vectors.
  - The remaining BLVAR DI work is now the representative station-15 replay mismatch, not the combined-pass watchdog story.

This is the normal “find the next red boundary” loop.

There is now a companion full-run locator path too:

- `tools/fortran-debug/run_managed_case.sh --route-disparity` runs the broad parity case, then hands the resulting `parity_report` to `tools/fortran-debug/route_full_xfoil_disparity.py`.
- The router writes `responsible_rig.json` / `responsible_rig.md`, resolves the narrowest responsible active rig when one exists, and can immediately rerun that rig in quick mode.
- If no active rig owns the earliest boundary strongly enough, the router escalates to the best `future_backlog` match and emits a `MISSING_RIG` template instead of pretending another full-run patch cycle would be useful.

### `full`

- Uses the same matrix shape, but it also runs each trace-backed rig’s refresh-case recipes.
- When a rig declares `full_managed_test_filter`, full mode uses that narrower representative probe instead of the broader suite filter after corpus growth.
  - this keeps vector-harvest runs from timing out on wide xUnit classes when the real goal of the run is corpus expansion, not exhaustive behavioral retesting.
- Registry sweeps can generate many more deterministic refresh cases from airfoil, Reynolds, alpha, panel-count, and Ncrit grids.
- `--refresh-limit` can cap that list during validation runs.
- `--refresh-shard-count` and `--refresh-shard-index` can split the same deterministic refresh-case lattice into disjoint shards.
  - that allows multiple full-mode workers to harvest the same rig family in parallel without duplicate case ownership.
- `--refresh-offset` can resume a shard from a later position in that deterministic lattice.
  - this keeps follow-up shard waves from replaying the same first cases when a rig still needs one more corpus-growth batch.
- sweep-generated refresh cases are now reordered by a deterministic high-yield heuristic after the explicit seed cases
  - larger `|alpha|`, larger panel counts, larger Reynolds numbers, and larger `Ncrit` values are harvested first
  - this biases full-mode corpus growth toward cases that tend to expose more iterations and therefore more unique real vectors per capture
- Rigs can now override that generic sweep ordering with an explicit `refresh_priority` block in the registry.
  - This is important for boundaries whose best-yield operating region is not the global “largest alpha/panel/Re first” pattern.
  - `direct-seed-station15-system` now uses that override to prioritize the historically productive `0012/2412/2312`, `Re ~ 0.75e6-1.4e6`, `alpha ~ 2.5-10`, moderate-panel station-15 direct-seed neighborhood ahead of the previously wasteful `alpha=12, Re=5e6, panels=80` cases.
- Full-mode refresh captures are now cached by physical case signature `(airfoil, Re, alpha, panels, Ncrit, iter)` and reused across trace-backed rigs.
  - This avoids paying the managed/reference capture cost multiple times for the same physical case when different rigs harvest different record kinds from that one trace.
- Those shared full-mode captures now persist under one canonical `shared-full-mode` owner instead of the first rig that happened to request the case.
  - that prevents the same physical case from accumulating multiple per-rig `shared_*` directories across runs and being rediscovered as duplicate corpus provenance later.
- Persisted shared full-mode captures now carry a small metadata file with the normalized shared trace env and case signature.
  - only metadata-backed captures whose stored env matches the current full-mode shared env are reused as shared-cache seeds.
  - older legacy per-rig `shared_*` directories without that metadata are ignored by the shared-cache seed path instead of being treated as safe superset captures.
- Shared-capture corpus deltas now reuse the persisted reference-directory name as their provenance case id.
  - that keeps dedupe stable across reruns, so a harvest wave cannot temporarily count vectors under the caller's original case id and then shrink on the next run when the same shared directory is rediscovered from disk.
- Full-mode corpus growth now harvests reference traces only.
  - Managed/reference paired reruns are still reserved for quick-mode triage, where first-divergence evidence matters more than raw corpus throughput.
- Trace-backed corpus collection now also reuses persisted `tools/fortran-debug/reference/micro_rig_matrix_*_ref` directories from earlier runs.
  - That lets the harness recover already-captured real vectors from disk before it spends time on fresh reference reruns.
- The directory discovery and JSONL load paths are cached in-process.
  - repeated corpus scans now reuse the same parsed fixture data instead of reparsing the same stable trace files for every rig/source combination in a run.
- Captured reference traces from those refresh runs are folded back into the corpus counter.
- Vector dedupe now keys on case provenance plus the rig-local identity fields, so the same `station/iteration` from different cases does not collapse into one vector.

This is the “grow the real corpus toward 1000+ vectors” loop.

## Outputs

Per run, the harness writes:

- `matrix.json`
- `matrix.md`
- `coverage_map.md`
- per-rig `corpus.json`
- per-rig `summary.json`
- per-rig `summary.md`
- per-rig xUnit stdout, stderr, and TRX
- optional per-rig `captures/` or `triage/` artifacts

`tools/fortran-debug/micro-rig-matrix/latest.json` and `latest.md` mirror the newest successful write.

To keep reruns practical on a crowded workspace:

- matrix-owned xUnit logs now keep only the last `N` lines (`--log-tail-lines`, default `2000`)
- older run directories are pruned automatically (`--retain-runs`, default `8`)
- run directories now include microseconds in their UTC stamp
  - that prevents quick back-to-back single-rig launches from colliding into the same output folder and overwriting each other
- per-rig summaries now include `executed_refresh_case_count`, `available_refresh_case_count`, and `estimated_cases_to_green`
- per-rig summaries now also include `reused_refresh_case_count`, and the top-level matrix records the shared-capture execution vs reuse totals for a full-mode run
- full-mode trace-backed corpus growth now emits lightweight stderr progress breadcrumbs
  - each rig reports its starting real-vector count, queued refresh-case count, and periodic refresh progress during long harvests
  - that makes it much easier to decide quickly whether a long run should keep going, be split, or be aborted in favor of a higher-yield path
- long full-mode harvests can now be sharded across multiple worker processes when a single sequential sweep is too slow for the remaining case volume
- quick-mode rig timeouts now treat a completed clean TRX as authoritative even if the watchdog fired during late `vstest` shutdown
  - that avoids mislabeling successful single-rig probes as `HARNESS_ERROR` when the timeout budget is tight
- live compare now treats `0/1` and `false/true` as equivalent booleanish payloads
  - this avoids false `RED` outcomes when Fortran traces encode logical flags as integers but the managed trace writes JSON booleans
- large generated capture trees are no longer meant to live in the repo working tree
  - `tools/fortran-debug/csharp/`, `tools/fortran-debug/agents/`, matrix run folders, top-level trace dumps, and ad hoc `reference/` sweep captures are recreated on demand and ignored by `tools/fortran-debug/.gitignore`
  - the curated harness code, registry, scripts, and long-lived reference fixtures remain tracked
- narrow expansion rigs can now opt out of the broad persisted shared-capture scan
  - `include_shared_persisted_captures: false` keeps a phase-2 quick probe from rescanning the entire phase-1 shared corpus before it even launches its focused xUnit filter
- the split `similarity-seed-final` and `similarity-seed-handoff` child rigs also opt out of that broad scan, because each child only needs one stable trace family and the parent combined rig is now just a logical template

## Status model

Matrix rows currently use one primary status:

- `GREEN`
- `RED`
- `MISSING_VECTORS`
- `MISSING_RIG`
- `HARNESS_ERROR`

Status priority is:

1. `HARNESS_ERROR`
2. `RED`
3. `MISSING_VECTORS`
4. `GREEN`

That keeps real failing rigs visible even when they are also under the `>=1000` real-vector gate.

The matrix header now distinguishes:

- canonical phase-1 rig count
- active expansion rig count
- total active rig count

## Verified behavior

The current verified checkpoints are:

- Quick 10-rig matrix execution succeeded in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T145627Z`.
- Quick 10-rig matrix execution also succeeded after the per-rig summary and pruning changes in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T152400Z`.
- The current canonical quick matrix also succeeded in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T222929Z`.
  - After the direct-seed station-15 live-loop fix, that run reports `0 GREEN / 0 RED / 10 MISSING_VECTORS / 0 HARNESS_ERROR`.
  - `predicted-edge-velocity` still uses the stable station-6 direct-seed quick probe, but it is no longer red on the canonical quick pass.
- Real-corpus promotion is now verified for the former synthetic-only kernel rigs in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T224913Z`.
  - `dense-linear-system` is `GREEN` on `7868` unique real `gauss_state` vectors from `n0012_re1e6_a0_p12_n9_full`.
  - `streamfunction-micro` is `GREEN` on `1350` unique real `PSILIN` vectors from the same reduced-panel full trace.
  - `cf-chain` is `GREEN` on `6119` unique real `BLMID` CF-chain vectors from `n0012_re1e6_a5_p80`.
  - `cq-chain` is `GREEN` on `1292` unique real `CQ` vectors from `n0012_re1e6_a0_p12_n9_full`.
- Quick live-triage execution succeeded for `direct-seed-station15-system` in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T145930Z`.
  - The triage report proved the harness can launch focused managed/reference reruns and emit a first comparable mismatch summary plus rerun recipe.
- Full corpus expansion succeeded for `similarity-seed-system` in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T150414Z`.
  - The refreshed corpus count increased from 4 to 12 real vectors after three deterministic case captures.
- Sweep-backed full corpus expansion also succeeded for `similarity-seed-system` in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T152200Z`.
  - With `--refresh-limit 6`, the harness consumed 3 explicit refresh cases plus 3 generated sweep cases and increased the corpus from 4 to 24 real vectors.
- Sweep-backed full corpus expansion also succeeded for `similarity-seed-step` in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T154411Z`.
  - With the same six-case cap, the step rig also increased from 4 to 24 real vectors and currently estimates `244` more similarly dense cases to reach the `>=1000` gate.
- Sweep-backed full corpus expansion also succeeded on a still-red rig in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T152626Z`.
  - `direct-seed-station15-system` stayed `RED`, but its corpus still grew to 21 real vectors across 4 executed refresh cases.
- Estimated corpus-growth output is also verified in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T153852Z` and `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260319T154213Z`.
  - `similarity-seed-system` currently estimates `244` more similarly dense cases to reach the `>=1000` vector gate from the sampled six-case corpus.
  - `predicted-edge-velocity` currently estimates `499` more similarly dense cases from its current one-case corpus.
- The canonical phase-1 target is now fully closed in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T055026Z`.
  - all 10 canonical rigs are green and over `>=1000` real vectors there.
- After the later laminar and direct-seed regressions were reopened by local parity work, the canonical set was reclosed again with targeted single-rig probes instead of the slower aggregate quick path.
  - `laminar-seed-stations-3-5` is green again in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-lam345-postfix/20260320T232701765295Z`.
  - `similarity-seed-system`, `similarity-seed-step`, `similarity-seed-final`, and `similarity-seed-handoff` are green again in the `mrm-probe-sim-*` artifacts.
  - `predicted-edge-velocity`, `dense-linear-system`, and `cf-chain` are green again in their `mrm-probe-*` artifacts.
  - `direct-seed-station15-system` was reclosed through the focused xUnit oracles after adding the missing adjacent row13 `BT2(1,3)` base/packet replays in `BoundaryLayerSystemAssembler`.
  - its coverage gate is also closed again after the later bounded harvest waves in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-priority`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset20b`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset30`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset40`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset50`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset60`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset70`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset80`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset90`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset100`, `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset110`, and `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct15-grow-offset120`, which moved the rig through `1002/1000` real vectors.
- The last sparse phase-1 child row was then closed with a tiny focused harvest instead of another broad aggregate rerun.
  - `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-sim-final-grow/20260321T010410533743Z` proved the low-yield path was healthy, moving `similarity-seed-final` from `21/25` to `22/25`.
  - `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-sim-final-quickcheck/20260321T010548359954Z` then confirmed `similarity-seed-final` at `25/25` and `similarity-seed-handoff` still green at `138/25`.
  - The newest per-rig summaries now put the whole canonical set back in the green simultaneously: `similarity-seed-system 1328/25`, `similarity-seed-step 412/25`, `similarity-seed-final 25/25`, `similarity-seed-handoff 138/25`, `laminar-seed-stations-3-5 63/25`, `direct-seed-station15-system 1002/1000`, `predicted-edge-velocity 5859/1000`, `dense-linear-system 7924/1000`, `streamfunction-micro 1494/1000`, `cf-chain 6310/1000`, and `cq-chain 1512/1000`.
- The remaining quick-mode pain point was not test execution but persisted phase-2 corpus loading.
  - A broad `--rig all` quick run could finish the phase-1 rigs and then appear hung while reparsing large persisted phase-2 `reference_trace*.jsonl` corpora.
  - The harness now marks rigs with their registry category and skips persisted phase-2 corpus discovery in quick mode, which keeps the canonical batch fast without changing full-mode corpus accounting.
  - quick mode also now reuses the latest on-disk per-rig `summary.json` as a corpus-count cache, so canonical reruns no longer have to reparse large persisted phase-1 corpora just to recover vector counts that were already written previously.
  - each rig now prints explicit `start` and `done` stderr breadcrumbs, which made the later direct-seed harvest and aggregate quick reruns much easier to monitor in long desktop sessions.
- The direct-seed harvest waves also exposed a separate capture-side bottleneck in `filter_trace.py`.
  - the filter was eagerly calling `augment_record(...)` and `json.dumps(...)` on every incoming line before the selector knew whether the line would actually be emitted.
  - the filter now augments and serializes only the records that are really written, keeping selector behavior and emitted trace semantics intact while reducing the CPU tax on full-mode reference captures.
- The split phase-2 transition subrigs are now both verified as independent quick probes.
  - `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T061618752852Z` shows `transition-seed-system-direct` passing on its focused direct station-15 system oracle with `9` currently counted direct-branch vectors.
  - `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T061758714577Z` shows `transition-seed-system-inverse` passing on its focused transition-window oracle with `5` currently counted inverse-window vectors after the registry was corrected to read the stable `reference_trace.jsonl` files instead of the last rotated numbered dump.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T062634934852Z` shows `transition-interval-inputs` passing as an independent quick probe with `5` currently counted interval-input vectors after removing an invalid `side/station` match filter from its registry entry.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T062051464294Z` shows the first bounded direct full-mode harvest reaching `182` real vectors across `15` executed refresh cases before a suite-level timeout mislabeled the result as `HARNESS_ERROR`.
  - the harness now supports `full_managed_test_filter`, and `transition-seed-system-direct` uses its focused iteration-5 system probe for full-mode verification so future corpus-growth runs do not pay that broad-suite timeout tax.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T185909780348Z` shows `transition-seed-system-direct` closed as an active expansion rig at `1003/1000` real vectors after bounded full-mode direct-station15 harvest waves.
- That promoted direct row now also has an explicit downstream owner rig, `direct-seed-station15-live-handoff`.
  - The new owner is intentionally narrower than `transition-seed-system-direct`: it anchors the last station-15 `blsys_interval_inputs` packet immediately before the live full-march emitted iteration-5 system, so future direct-station15 live drifts route into the carry handoff first instead of going straight back to the broad emitted packet surface.
  - The focused xUnit oracle reuses `DirectSeedStation15SystemMicroParityTests.Alpha10_P80_DirectSeedStation15_Iteration5_RebuiltStation2Carry_MatchesLiveFullMarchSystem`, but it now selects the live packet by sequence and compares the carry words directly, which makes the handoff mismatch actionable instead of failing with a missing exact-payload lookup.
  - The registry now reads that owner's corpus from the actual station-15 `BLSYS` input traces (`phase=mrchue`, `ityp=2`, `tran=true`, `turb=false`) instead of the older station-16 laminar-seed-only reference family, and it shares the same deterministic direct-seed sweep lattice as the broad station-15 row so the sparse `25`-vector gate can grow honestly.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T190536270397Z` shows the newly promoted `trdif-transition-remap` rig quick-passing immediately at `224/1000` real vectors.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T200813440793Z` shows the newly promoted `stagnation-interpolation` rig quick-passing immediately at `6/1000` real vectors.
  - the dedicated `n0012_re1e6_a10_p80_stagnation` managed case keeps the focused stagnation test in the single-digit seconds instead of reusing the much broader `n0012_re1e6_a10_p80` trace set.
  - the rig now prefers that small managed artifact, skips the broad persisted shared-capture scan on quick runs, and also harvests the existing `main_alpha0_stagnation_ref`, `main_alpha0_stagnation_ref2`, and `n0012_re1e6_a0_p12_n9_full` reference traces.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T201124470345Z` shows the widened `stagnation-interpolation` rig still quick-passing after that registry cleanup, now at `21/1000` real vectors.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T201410017919Z` shows the newly promoted `wake-node-geometry` rig quick-passing immediately at `6/1000` real vectors.
  - the dedicated `n0012_re1e6_a10_p80_wakenode` managed case keeps the XYWAKE wake-node probe small and deterministic.
  - the current authoritative reference packet window in `alpha10_p80_wakecol_ref` only records the first six wake nodes, so the rig intentionally proves that six-node prefix instead of pretending the reference trace is longer.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T201830349899Z` shows the newly promoted `wake-spacing` rig quick-passing immediately at `2/1000` real vectors.
  - a focused `WakeSpacingMicroParityTests` packet first exposed a real trace-payload mismatch in `InfluenceMatrixBuilder.EstimateFirstWakeSpacing`: the float parity path already used the upper half-step for `ds1`, but it was still tracing the physical lower TE spacing instead of the effective parity payload seen in the reference trace.
  - the managed trace now reports `lowerDelta = 0` in that float parity packet, matching the authoritative reference while leaving the actual `ds1` computation unchanged.
- `blvar-cf-upstream` is now promoted as an active phase-2 rig too.
  - it reuses the existing focused station-5 direct-seed Cf-chain parity test instead of inventing a new wrapper, because that test already proves the exact `blvar_cf_terms` producer packet against the authoritative `alpha10_p80_station5_cfchain_ref` trace.
  - the registry also harvests the broader alpha-0 panel-12 full trace family for additional `blvar_cf_terms` vectors, so this rig can grow honestly while the `VSREZ` half of the old combined backlog remains future work.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T202812178003Z` shows `blvar-cf-upstream` closed as an active expansion rig at `2572/1000` real vectors after a single bounded full-mode harvest wave.
- `blvar-turbulent-di-upstream` is now promotable as an active phase-2 rig too.
  - its quick probe no longer depends on the broad alpha-10 station-29 trace loader.
  - `BlvarTurbulentDiMicroParityTests` now routes that quick surface through the already-split `TurbulentWallDi`, `TurbulentDiDfac`, `TurbulentOuterDi`, and `TurbulentDiChain` driver-backed packet tests, while keeping the stronger station-15 direct-seed BLVAR DI replay as the representative trace-backed proof.
  - the current registry sources already clear the real-vector gate on existing artifacts alone: `alpha10_p80_blvar_di_ref` plus `n0012_re1e6_a0_p12_n9_full` count `1352` unique `blvar_turbulent_di_terms` packets under the rig dedupe key, so no new solver-side instrumentation is required for promotion.
- `bldif-log-terms` is now active as a focused phase-2 owner rig too.
  - it proves `BoundaryLayerSystemAssembler.ComputeBldifLogTerms(...)` directly on the `bldif_log_inputs -> bldif_common` packet family instead of treating those fields as a side effect of a wider station-system replay.
  - the managed batch still validates the curated `1000+` corpus, and it now also auto-discovers future `micro_rig_matrix_bldif-log-terms_*_ref` captures so bounded matrix harvests can strengthen the same small owner surface instead of collecting vectors the test never replays.
  - the matrix runner now supports an opt-in `read_all_matching_files` trace-source mode for rigs like this one where the truthful corpus lives across numbered `reference_trace*.jsonl` artifacts inside the same curated directory, not only in the newest file.
  - quick mode also now honors owner-adopted persisted globs even when a rig has no separate borrowed `additional_persisted_trace_globs`, so owner-backed shared-code rigs do not silently collapse to their single configured source directory.
- `non-similarity-blsys1` is no longer tracked as future work.
  - the existing green phase-1 `laminar-seed-stations-3-5` rig already traces the real station-3/4/5 `blsys_interval_inputs` handoff plus the downstream laminar helper chain through `RefineLaminarSeedStation`, so carrying a separate future `BLSYS(1)` backlog item would have been duplicate accounting rather than a real gap.
- `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T202201299320Z` shows the newly promoted `inviscid-wake-coupling` rig quick-passing immediately at `3/1000` real vectors.
  - the current oracle is the focused `wake_panel_state` producer packet sequence backed by the dedicated `n0012_re1e6_a10_p80_wakepanelstate` managed case.
  - as with the other wake-focused promotions, the current authoritative reference only captures a prefix window, so the rig intentionally proves that prefix instead of inferring a longer oracle than the trace actually provides.
- The intermediate direct harvest waves also exposed a provenance-key bug in shared-capture corpus merging, now fixed by keying refresh deltas on the persisted shared directory name instead of the caller's transient case id.

## Current limitations

- The canonical phase-1 goal is closed again; the remaining work is phase-2 expansion depth, future backlog design, and keeping the green phase-1 set stable as new parity fixes land.
- Narrow expansion rigs can have highly uneven corpus density.
  - `transition-seed-system-direct` is red again at `1003/1000` on the promoted broad acceptance wave.
  - the broad direct row is acceptance-only; its current owner route starts at `transition-point-iteration-alpha10-station15-free -> transition-interval-inputs`, then uses `direct-seed-station15-live-handoff` only as a downstream witness cross-check before falling into `bldif-log-terms`, `transition-seed-row11-carry-packets`, and `transition-seed-gauss-packets`.
  - if the first stable drift is still inside `bldif_log_inputs` / `bldif_common`, the new `bldif-log-terms` owner rig is now the next required stop before reopening the broader station-15 replay.
  - it is intentionally a `sparse_full_run` rig because each full march contributes only one authoritative station-15 live-handoff packet for this exact boundary.
  - that owner row now harvests the real `mrchue` station-15 `blsys_interval_inputs` packet family and reuses the direct-station15 sweep lattice, so future missing-vector results on this row should indicate insufficient harvest depth rather than a bad source-directory pointer.
  - the newest live probe also showed why the route moved: the current preparation trace is still carrying a laminar-only low-amplification packet family through stations `12-16`, so station-15 is the first consumer witness, not the earliest shared producer. The new direct transition-point rig keeps that first producer surface explicit without promoting another broad row.
  - March 23 focused reruns verified the order: the new direct transition-point owner fails on `ampl1`, and the quick matrix pass in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-direct-trpoint-quick2/latest.json` records it as `RED` at `183/1000`.
  - the downstream direct `transition_interval_inputs` owner still throws `Sequence contains no elements` because the managed prep trace never emits the expected station-15 interval-input packet on that lane.
  - the same probe also exposed the next smaller witness inside that owner: the predecessor upper-station-14 transition-point packet is already low (`ampl1=0x3EDFB74F`, `ampl2` only rising to `0x3EE1C4E2`) while the focused reference reaches `0x4039B74A -> 0x40D90421`, so future direct-side splits should move into that station-14 carry packet before widening back out to station-15 consumers.
  - the inverse and interval-input splits are honest but still severely under-vectorized on their specialized references.
  - `transition-seed-system-inverse` is currently bounded to a curated quick owner/adopted corpus and is red again at `45/1000` on `transition_final_sensitivities.xtA2`.
  - `trdif-transition-remap` is now a rich owner-backed promoted red at `1272/1000`; future work should route through its interval / bt2 owner ladder instead of widening the broad remap row again.
  - `stagnation-interpolation` is now active too, but it still starts from a tiny focused corpus and will need deliberate harvest waves rather than broad shared-capture reuse.
  - `wake-node-geometry` is now active on the same narrow-oracle footing: the quick rig is trustworthy, but the current reference family only exposes a six-node prefix and needs deliberate harvest growth before it can reach the `>=1000` gate.
  - `wake-spacing` is even narrower at the moment; it is now green as a focused producer packet, but it currently has only two distinct real vectors on disk.
  - `blvar-cf-upstream` is red again on the promoted acceptance wave even though it is already the focused owner row for that station-5 producer packet.
  - `blvar-turbulent-di-upstream` is still a focused owner row too, but the old deterministic promoted quick pass is now known stale for that row.
  - the quick owner surface has been rerouted away from the giant historical trace and now passes standalone; the remaining open issue is the red station-15 representative replay.
  - `inviscid-wake-coupling` is still an acceptance-only broad row.
  - its new `wake-coupling-packets` owner family now carries the real debug loop, but both child packet owners still need deliberate harvest growth before the broad row can be treated as richly owner-backed.
  - `block-tridiagonal-blsolv` is in the same state: the broad row is acceptance-only, while the new `blsolv-packets` family is the inner debug loop for future reopenings.
- The inverse and interval-input phase-2 rigs now have deterministic sweep-backed refresh lattices too.
  - that removes the earlier artificial one-case ceiling and lets full-mode harvests grow those corpora with the same bounded sharding model already used by the richer direct split.
- Full-mode corpus growth is still the right tool for richer phase-2 direct oracles, but some future expansion rigs will need new authoritative reference capture shapes rather than just more sweep volume.
- Corpus-growth runs can still become large, so the harness should keep using bounded logs and on-demand captures instead of rebuilding giant checked-in trace trees.

## TODO

- Reroute `trdif-transition-remap` through `transition-interval-inputs` plus the focused row13 / row22 `BT2` owners and decide whether the current promoted red is a real solver gap or another narrow harness/data-selection mistake.
- Grow `stagnation-interpolation` through bounded full-mode harvest batches now that its quick probe is active and efficient.
- Grow `wake-node-geometry` through bounded full-mode harvest batches and decide whether additional focused wake-node reference capture shapes are needed beyond the current six-node alpha-10 window.
- Grow `wake-spacing` through bounded full-mode harvest batches now that the first packet is green and the trace payload matches the reference.
- Grow `wake-coupling-packets` through bounded full-mode harvest batches and decide whether the NI-term child is enough or whether a third wake-specific packet split is needed before widening the broad `inviscid-wake-coupling` row again.
- Grow `blsolv-packets` through bounded full-mode harvest batches and decide whether the current first-episode witness is enough or whether a second solve-episode split is needed.
- Investigate the remaining station-15 representative `blvar_turbulent_di_terms` replay mismatch now that the quick owner surface no longer times out on giant managed trace loading.
- Harvest the new deterministic sweep lattices for `transition-seed-system-inverse` and `transition-interval-inputs` and measure whether they produce enough real vectors to justify more bounded batches.
- Decide whether `transition-seed-system-inverse` should stay as a narrow honest oracle with a small specialized corpus or whether new transition-window reference capture workflows should be added specifically for it.
- Promote the next `MISSING_RIG` backlog item, with `vsrez-upstream` now the clearest trace-backed candidate; the leftover `remaining-di-family` work is narrower standalone DI decomposition beyond the new BLVAR packet rig.
