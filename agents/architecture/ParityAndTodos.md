# Managed-vs-Legacy Parity And TODOs

## MSES sub-project

`XFoil.MsesSolver` is a separate clean-room port of Drela's 1986
thesis closure, parallel to (not replacing) `XFoil.Solver`. Its
parity target is the thesis correlations, not Fortran XFoil.
Current state: Phase 0–2f complete end-to-end (closure library,
laminar/turbulent BL marchers, wake marcher, Squire-Young drag,
transition detection, CDF/CDP decomposition, compressibility
propagation, full CLI surface with opt-in env vars). Phase 5
Newton coupling is the remaining major piece. Details in
`MsesClosurePlan.md`.

## Status summary

The codebase is ahead of the stale docs that were left behind, but it is still behind original XFoil on fidelity. The managed solver now contains a real Newton-coupled viscous stack:

- `ViscousSolverEngine`
- `ViscousNewtonAssembler`
- `ViscousNewtonUpdater`
- `TransitionModel`
- `InfluenceMatrixBuilder`
- `DragCalculator`
- `PolarSweepRunner`

That said, the current parity tests still show the solver converging to the wrong fixed point on reference cases. `ViscousParityTests` and `PolarParityTests` document wrong-sign `CL`, order-of-magnitude `CD` error, and `CM` errors of order one on several Fortran reference conditions. The current test tolerances are intentionally relaxed to keep tracking the gap visible.

Fresh alpha-10 re-verification also flushed out one reference-side tooling bug: the instrumented Fortran `DIL` trace path had introduced an undeclared `NUMER` temp, so implicit typing zeroed `DI` and `DI_RT` while leaving `DI_HK` nonzero. That bug is fixed in the reference source, so current fresh `laminar_dissipation` traces are authoritative again instead of pointing at a false managed mismatch.

Parity trace tooling is now less brittle during these investigations. `JsonlTraceWriter`, `run_reference.sh`, and the Fortran debug harness support env-driven `kind`/`scope`/`side`/`station`/`iteration` filtering plus `XFOIL_TRACE_TRIGGER_*` and `XFOIL_TRACE_RING_BUFFER`, which lets focused runs keep a small pre-trigger context without filling the disk.

Ad hoc managed parity runs now write a versioned `parity_report.*.txt` next to `csharp_dump.*.txt` and `csharp_trace.*.jsonl`. The new C# dump analyzer reports the final `CL/CD/CM` gap plus the earliest parsed dump-level divergence block, so the harness can point at a first station/category boundary on its own before a narrower trace rerun is chosen.

That full-run report is no longer meant to be the debugging surface. `tools/fortran-debug/route_full_xfoil_disparity.py` now parses the newest `parity_report`, scores it against registry ownership in `micro_rig_registry.json`, writes `responsible_rig.json` / `responsible_rig.md`, and can immediately rerun the resolved rig in quick mode. `run_managed_case.sh --route-disparity` uses that path directly, so a broad full-run parity case now acts as a locator plus final confirmation step instead of inviting another “patch and rerun full XFoil” loop.

The managed live-compare rig is now stricter and more actionable than the first `parity_report` version. When `XFOIL_LIVE_COMPARE_ENABLED=1` or `XFOIL_LIVE_COMPARE_REFERENCE_TRACE=...` is set, the harness now:

- compares each comparable C# trace event against the precomputed Fortran event stream as it is written,
- aborts on the first mismatching event,
- records the last matched comparable events plus the concrete reference/managed mismatch records in the dump and `parity_report`,
- checks for trailing comparable reference events after the managed run completes so a truncated managed stream cannot pass silently, and
- supports `XFOIL_LIVE_COMPARE_CONTEXT_EVENTS` to keep the mismatch neighborhood tiny by default while still widening it on demand.

The focused rig is also better at reusing full traces without noise. `JsonlTraceWriter`, `run_managed_case.sh`, `ParityTraceFocusSelector`, and `ParityTraceLiveComparator` now support `XFOIL_TRACE_TRIGGER_OCCURRENCE`, and the live comparator waits for the same managed trigger occurrence before it starts comparing comparable events. That made the 12-panel `NACA 0012, Re=1e6, alpha=0, Ncrit=9` rung practical with sub-10 KB focused traces instead of broad reruns.

The registry now carries a second kind of ownership metadata too: `routing_hints`. Those hints do not affect corpus accounting, but they let the new full-run router map dump categories (`BL_STATE`, `VDEL_R`, `VSREZ`, ...), live mismatch record kinds/scopes, and managed owning-scope hints onto the narrowest responsible active rig or onto a `MISSING_RIG` backlog entry when no runnable oracle exists yet. The strict policy is now `register first`: if the full-run router cannot find an active responsible rig, the next step is to create and register one before any solver-side arithmetic patching resumes.

Focused parity orchestration now has a single registry-driven entry point too: `tools/fortran-debug/run_micro_rig_matrix.py` backed by `tools/fortran-debug/micro_rig_registry.json`. The harness prebuilds once, runs `dotnet test --no-build`, supports a quick representative probe for suite-sized rigs, emits `matrix.json` / `matrix.md` / `coverage_map.md`, and keeps the phase-1 canonical rig set plus the post-phase backlog in one place.

The matrix now also supports two explicit promotion waves for mature expansion rigs. `promoted_phase1_rig_ids` lets a high-value phase-2 broad row graduate into the practical promoted acceptance set without rewriting the large registry buckets in place. The current broad promotion wave covers 10 remaining-database owners:

- direct transition seed-system
- inverse transition seed-system
- TRDIF transition remap
- stagnation interpolation
- wake-node geometry
- wake spacing
- BLVAR Cf upstream
- BLVAR turbulent DI upstream
- block-tridiagonal BLSOLV
- inviscid wake coupling

Those rows are now expected to accumulate real multi-vector corpora like acceptance rigs instead of remaining one-vector localization scalpels.

There is now a second, narrower promotion list too: `canonical_phase1_promoted_rig_ids`. That list promotes focused phase-2 owners into the canonical phase-1 lane while still sourcing the definitions from `phase2_rigs`. The owner-promotion wave now covers:

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

That owner-promotion pass is no longer just planned wiring. The accepted-transition owner has been converted into a real canonical phase-1 row: `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-accepted-transition-kinematic-fix3/20260322T005838676004Z/rigs/accepted-transition-kinematic-alpha0-station4/summary.json` shows `accepted-transition-kinematic-alpha0-station4` green at `91/1` after its vector source was corrected to use the accepted `transition_interval_inputs` primary-state anchor that the focused parity test already uses to select the downstream `kinematic_result`. The old `MISSING_VECTORS 0/1` state was a registry-selection bug, not a lack of owner packets.

The newer owner promotions also now obey the same broad-corpus rule as the rest of phase 1: they are allowed to keep station numbers only as witness labels, but their refresh recipes must span multiple profiles / Reynolds numbers / alphas instead of staying one-case “station facts”. The active validator now passes on the whole active `>=1000` trace-backed set after those alpha-0 seed / carry / transition rows were given explicit diversified sweep lattices.

The first focused alpha-0 reruns under that rule now separate the green owners from the still-live interval family:

- `laminar-seed-final-alpha0-station3`: green on the focused xUnit proof.
- `transition-seed-system-alpha0-station4-final`: green on the focused xUnit proof.
- `transition-seed-step-alpha0-station4-final`: green on the focused xUnit proof.
- `transition-interval-inputs-alpha0-station4-final`: still red, first mismatch `d2` by one float ULP.

That interval owner is now split more honestly too. The old final-only witness had still been configured as a one-vector sparse fact and did not constrain the iteration in the registry. It now requires a diversified `1000+` corpus at `iteration=7`, and two smaller sister rigs were added for the other active witness packets:

- `transition-interval-inputs-alpha0-station4-iter8`: red on focused xUnit, first mismatch `d1`.
- `transition-interval-inputs-alpha0-station4-iter14`: red on focused xUnit, first mismatch `xtX2`.

This follows the active debugging rule for the promoted owner wave: once a witness family reopens, split it into smaller packet witnesses instead of continuing to push on the broader consumer lane.

This split keeps the matrix honest about role:

- broad promoted rows are still the acceptance surface,
- canonical owner promotions are the focused first-line proof set that should back those broad rows before a broad rerun is trusted.
The first promotion harvest wave immediately surfaced three real acceptance failures that had previously been hidden behind low corpus counts:

- `transition-seed-system-direct` is now `RED` at `1003/1000` on the station-15 direct-branch system, first failing on iteration-5 `theta` (`0x38E67A00` vs `0x38E67A01`).
- `transition-seed-system-inverse` initially went `RED` at `122/1000` in shard 0 and `108/1000` in shard 1, first failing on iteration-5 inverse-window `transition_final_sensitivities.xtX2` (`0xBD8D7330` vs `0xBD8D7333`).
- `trdif-transition-remap` is now `RED` at `1208/1000`, first failing on iteration-3 `transition_interval_bt2_terms` row22 `baseBits` (`1180721359` vs `1180721353`).

That inverse arithmetic red is no longer the live frontier. The focused inverse owner tests now pass again, and the remaining promoted inverse closure work is corpus growth plus honest provenance:

- the best completed bounded full rerun currently shows `946/1000` unique inverse-window vectors,
- the old dedicated-owner-only slice reached `44` vectors before owner adoption was introduced,
- the new `owner_adopted_persisted_trace_globs` path now lets the rig explicitly bless curated compatible persisted inverse-window families as owner-backed evidence, and
- on the current `946`-vector corpus that owner-adopted accounting analytically covers all `946` vectors, so the remaining inverse gap is now “find ~54 more real vectors”, not “repair an unresolved red mismatch”.

That is the intended effect of promotion: the rows are no longer “under-vectorized but nominally green”; they now act as broad acceptance surfaces that expose the real remaining transition/direct-seed parity gaps.

That matrix harness is now verified in three ways:

- the quick 10-rig matrix completed end-to-end in `tools/fortran-debug/micro-rig-matrix/20260319T145627Z`,
- the direct-seed station-15 rig produced a focused live-compare triage report in `tools/fortran-debug/micro-rig-matrix/20260319T145930Z`, and
- the full refresh path for `similarity-seed-system` proved that new captured reference traces now contribute to the corpus count (`4 -> 12` real vectors) in `tools/fortran-debug/micro-rig-matrix/20260319T150414Z`.

The harness is broader now than that first landing:

- each rig directory now writes `summary.json` and `summary.md` alongside `corpus.json`, TRX, and test logs,
- matrix-owned xUnit logs keep only a bounded tail to reduce disk churn during reruns,
- old run directories are pruned automatically, and
- all six trace-backed phase-1 rigs now support deterministic sweep-generated refresh cases instead of only three hand-curated captures each.
- full-mode refresh captures are now reused across rigs when the physical case signature matches, so the same airfoil/Re/alpha/panel/Ncrit sweep case is captured once and then harvested by multiple record-kind rigs,
- full-mode corpus growth now captures reference traces only, keeping the heavier managed/reference paired reruns for quick-mode triage instead of paying that cost on every vector-harvest case,
- corpus collection now also reuses persisted `micro_rig_matrix_*_ref` directories from earlier runs, so existing captured reference traces count immediately on the next matrix rerun instead of forcing a recapture,
- per-rig summaries now report executed refresh counts, reused refresh counts, available refresh counts under the current cap, and a coarse `estimated_cases_to_green` number based on the sampled case density.
- long full-mode harvests now also emit periodic stderr progress breadcrumbs from `run_micro_rig_matrix.py`, so stalled or low-yield corpus runs are easier to detect and replan before they waste more machine time.
- the harness can now also shard deterministic refresh-case sweeps across multiple workers (`--refresh-shard-count` / `--refresh-shard-index`), which is the preferred way to accelerate large remaining corpus-growth batches without changing the case lattice itself.
- follow-up shard waves can now resume from a later deterministic position with `--refresh-offset`, so finishing a near-green rig no longer requires replaying the same first cases of each shard.
- refresh-case sweeps are now also ordered by a deterministic “likely yield” heuristic after the explicit seed cases, so the remaining corpus-growth waves prioritize richer high-alpha/high-panel/high-Re captures instead of spending their first minutes in the lowest-yield corner of the lattice.
- promoted-broad matrix outputs now also report provenance splits: owner-backed unique vectors, non-owner unique vectors, per-owner counts, and `owner_backed_under_vectorized`, so broad green/missing-vector results can be read alongside the remaining dedicated-owner gap.
- the matrix now also supports `owner_adopted_persisted_trace_globs`, which is the explicit way to turn a curated compatible persisted trace family into owner-backed coverage for a promoted row without hiding the original broader capture provenance.

That sweep path is already verified on `similarity-seed-system`: the capped six-case run in `tools/fortran-debug/micro-rig-matrix/20260319T152200Z` consumed three explicit cases plus three generated sweep cases and raised the rig corpus to 24 real vectors.
The same expansion is now verified on `similarity-seed-step` too: `tools/fortran-debug/micro-rig-matrix/20260319T154411Z` reached the same 24-real-vector corpus with the same six-case cap.
It is also verified on a red rig: the capped four-case `direct-seed-station15-system` run in `tools/fortran-debug/micro-rig-matrix/20260319T152626Z` stayed red on the same producer mismatch, but still grew the corpus to 21 real vectors.

The matrix also made the current bottleneck explicit: phase 1 is not blocked on missing rig definitions anymore, it is blocked on corpus depth. No phase-1 rig is green on `>=1000` real vectors yet, and several otherwise-useful kernel rigs are correctly stuck in `MISSING_VECTORS` because they still depend on synthetic driver batches instead of captured real solver vectors.
The latest matrix picture is stronger now than that early “0 red / 10 missing-vectors” snapshot. Reusing persisted matrix captures plus converting the kernel oracles from `static_batch` to real `trace_records` corpora moved four former synthetic-only rigs to genuine green status:

- `dense-linear-system` is green on `7868` real `gauss_state` vectors from `n0012_re1e6_a0_p12_n9_full`,
- `streamfunction-micro` is green on `1350` real `PSILIN` vectors from the same reduced-panel full trace,
- `cf-chain` is green on `6119` real `BLMID` CF-chain vectors from `n0012_re1e6_a5_p80`, and
- `cq-chain` is green on `1292` real `CQ` vectors from `n0012_re1e6_a0_p12_n9_full`.

That leaves the six viscous/seed rigs as the remaining phase-1 corpus-depth bottleneck rather than the full 10-rig set.

The predicted-edge corpus counter also had a real registry bug: its dedupe fields referenced nonexistent `ibl`/`is`/`term` slots, collapsing thousands of matched records into almost no “real” vectors. Correcting that key set immediately raised the collector from `12` to `495` unique real vectors on the existing stored traces, and targeted serialized reference harvests around productive `1412/2412` cases have now pushed that corpus to `1067` unique real vectors.

The latest predicted-edge pass also exposed a harness bug rather than a solver mismatch: the per-rig timeout wrapper could fire during late `vstest` shutdown and overwrite a clean one-test TRX with `HARNESS_ERROR`. `run_micro_rig_matrix.py` now treats a completed clean TRX as authoritative, so successful quick probes are no longer mislabeled when the watchdog budget is tight.

The current direct-seed station-15 frontier is narrower now too. After the direct remarch `xtX2` carry fix, the accepted live handoff into station-15 iteration 4 is proven good, so the first remaining broad red is no longer “bad carry into station 15.” A focused trigger-window Fortran capture in `reference/alpha10_p80_bldif_eq1_rows_station15_iter4_ref` shows the immediate upstream owner packet: `bldif_eq1_rows.row21` right before the iteration-4 station-15 system. That boundary is now registered as `direct-seed-station15-iter4-row11-owner-eq1rows-row21`, which means the next direct-seed debugging loop must answer “is the producer row already wrong, or does the consumer combine it incorrectly?” before any broader station-15 patching resumes.

That same predicted-edge triage also exposed a second harness-only bug: the live comparator was treating `isWakeSource=0/1` on the Fortran side and `isWakeSource=false/true` on the managed side as a hard kind mismatch even though the micro rig already accepts those encodings as equivalent booleanish flags. `ParityTraceLiveComparator` now accepts numeric `0/1` and JSON booleans as the same logical payload, so quick live compare no longer reports a bogus UESET producer failure on that schema difference alone.

The matrix runner also had an output-collision bug: run directories were stamped only to the second, so fast back-to-back single-rig launches could land in the same folder and overwrite each other’s `matrix.md`/`latest.md` artifacts. `run_micro_rig_matrix.py` now stamps run directories with microsecond precision so rapid quick probes keep distinct evidence.

Phase 1 is now closed in the matrix harness: the canonical 10 rigs are green and over their `>=1000` real-vector gates in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T055026Z/matrix.md`. Coverage expansion has started in the same matrix, but the first promoted transition rig had to be split for efficiency and honesty:

- `transition-seed-system-direct` now tracks the direct-branch station-15 seed-system oracle independently and currently quick-passes with `9` counted direct-system vectors in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T061618752852Z`.
- `transition-seed-system-inverse` now tracks the narrow inverse transition-window oracle independently and currently quick-passes with `5` counted inverse-window vectors in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T061758714577Z`.
- `transition-interval-inputs` now tracks the direct-seed TRDIF interval-input producer boundary independently. The older quick probe only showed `5` counted vectors in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T062634934852Z`, but the March 22 curated persisted-capture rerun lifted the visible corpus to at least `36` real vectors before any new refresh case was accepted, so the row is now blocked by explicit corpus depth rather than by a hidden registry cap.
- `trdif-transition-remap` now promotes the former backlog TRDIF remap item into an active rig and already quick-passes at `224` real vectors in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T190536270397Z`.

That split also surfaced two small harness-shape fixes:

- phase-2 full-mode shared-capture seeding must use the full active rig set, not only the canonical phase-1 list, and
- some narrow expansion rigs need to opt out of the giant persisted shared-capture scan so a focused quick probe does not spend most of its time walking unrelated phase-1 captures before it even launches the xUnit filter.

The inverse split also exposed a reference-selection trap in the registry: the specialized transition-window corpora must read the stable `reference_trace.jsonl` files, not the newest rotated `reference_trace.*.jsonl` dump, otherwise the quick matrix can silently count zero matching inverse-window vectors even though the authoritative unnumbered trace contains them.

The next efficiency fix after those probes was structural rather than numerical: both `transition-seed-system-inverse` and `transition-interval-inputs` were initially backed by only one explicit seed capture, which made them permanently tiny no matter how well the harness ran. Their registry entries now carry the same deterministic sweep-backed refresh lattices as the richer direct split, so future full-mode batches can measure real corpus density instead of being capped by configuration.

The next March 22 structural fix was narrower but just as important: `accepted-transition-kinematic-alpha0-station4` originally tried to count owner vectors by matching `kinematic_result` on nonexistent `side/station` data fields. The focused parity test never used that selector; it anchored on the accepted `transition_interval_inputs` packet and then located the matching `kinematic_result` by primary-state identity. The registry now mirrors that same anchor, which is why the owner flipped immediately from false `MISSING_VECTORS` to green with a dense local corpus.

The first bounded direct full-mode harvest also exposed a runner-shape inefficiency rather than a parity bug: `transition-seed-system-direct` grew to `182` real vectors across `15` executed refresh cases in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T062051464294Z`, but the run finished as `HARNESS_ERROR` because full mode still reran a broad suite-level xUnit filter under a tight `30s` watchdog. `run_micro_rig_matrix.py` now supports `full_managed_test_filter`, and the direct station-15 rig uses the same focused iteration-5 probe for full-mode verification that it already used in quick mode.

The direct station-15 expansion branch is now closed: bounded full-mode waves eventually drove `transition-seed-system-direct` to `GREEN` at `1003/1000` in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T185909780348Z`. The intermediate waves also exposed a second harness-only bug: full-mode shared-capture deltas were merged under the caller's transient case id, while later reruns rediscovered the same reference directories under their persisted `micro_rig_matrix_*_shared_*_ref` names. The harness now keys those delta merges by the persisted reference-directory name, so corpus totals stay monotonic across reruns instead of shrinking when a later run rehydrates the same shared captures from disk.

With the direct seed-system branch green, the next best active expansion path is `trdif-transition-remap`, not the sparse inverse or interval-input side rigs. Its focused row-remap references already give the matrix `224` real vectors before any full-mode sweep growth, so it has a much better chance of closing quickly than the specialized side oracles.

The direct-seed station-15 ladder is healthier again now. `DirectSeedStation15SystemMicroParityTests` is back to green with 33 focused checks after the latest TRDIF pass:

- the harness now distinguishes raw station-1 state from the carried transition-final station-1 replay used by iter-4/5 direct-seed windows,
- `bldif_eq1_residual_terms` now replays the proved legacy staging where `(prodcore * dxi)` is rounded before subtracting the stored log-loss term,
- `transition_interval_bt2_terms` row 1 / column 3 and row 2 / column 3 now have dedicated focused rigs, and
- both managed and Fortran traces now emit row 2 / columns 3 and 4 for `transition_interval_bt2_terms` plus `transition_interval_final_terms`, removing the old row23 / row24 instrumentation blind spot.

The promoted-broad truth source moved again on March 23. `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-promoted-broad-quick9/latest.md` is now the authoritative quick acceptance snapshot for the 10 promoted rows, and it is intentionally stricter than the older mixed-corpus transition reruns: `0` green, `5` red, `4` missing-vector rows, `1` harness error, and `6` rows still lacking trustworthy owner-backed depth. The current reopened reds are:

- `transition-seed-system-direct` at `1003/1000`, first failing on station-15 iteration-5 `uei`
- `transition-seed-system-inverse` at `45/1000`, first failing on iteration-5 `transition_final_sensitivities.xtA2`
- `trdif-transition-remap` at `1272/1000`, first failing on row22 `transition_interval_bt2_terms.baseBits`
- `blvar-cf-upstream` at `2572/1000`, first failing on station-5 direct-seed `blvar_cf_terms.cf`
- `block-tridiagonal-blsolv` at `70/1000`, first failing on `vdel_fwd values[1]`

The current promoted harvest-only rows are still `stagnation-interpolation` (`21/1000`), `wake-node-geometry` (`6/1000`), `wake-spacing` (`2/1000`), and `inviscid-wake-coupling` (`3/1000`). `blvar-turbulent-di-upstream` is still the one stale row inside that `quick9` snapshot, but it is no longer a live harness mystery: the old quick wrapper was loading the historical `5.8 GB` alpha-10 managed trace into the parity loader, so the combined pass timed out before it ever reached a trustworthy BLVAR DI verdict. `BlvarTurbulentDiMicroParityTests` now routes the quick probe through the already-split DI packet ladder (`wall -> dfac -> outer -> full chain`) instead of that giant trace path, and a standalone rerun is now green at `1352/1000`. The remaining BLVAR DI work is the representative station-15 direct-seed replay, which is red on `blvar_turbulent_di_terms`, not the old watchdog behavior.

The promoted-broad truth source is stricter now as well. `run_micro_rig_matrix.py` summaries and `matrix.md` now report promoted-only counts directly: promoted green rows, promoted red rows, promoted missing-vector rows, promoted harness errors, and promoted rows still lacking trustworthy owner-backed vector depth. That keeps the practical acceptance wave machine-readable instead of forcing follow-up scripts or docs to reconstruct the promoted subset from the registry by hand.

The new “diversified 1000-vector coverage” rule is also real across the whole active matrix now, not just on the recently touched promoted rows. The remaining active offenders were backfilled with explicit mixed-airfoil / mixed-Re / mixed-alpha refresh lattices on March 22, and the runner-side validator now passes on all `50` active `>=1000` trace-backed rigs.

The inverse promoted row is no longer allowed to hide behind the older shared-full harvests either. Quick mode now supports a separate `quick_include_shared_persisted_captures` override, and `transition-seed-system-inverse` uses it to keep its quick acceptance path on the curated owner/adopted transition-window directories instead of silently ingesting the whole `shared-full-mode` backlog. Under that bounded truth source the promoted inverse row is back to an honest arithmetic red at `45/1000`, not just a corpus-depth gap. The older full-mode `938-946/1000` runs are still useful as compatibility evidence and refresh-priority guidance, but they are no longer the authoritative quick acceptance story for the promoted inverse row.

`transition-interval-inputs` now has the same provenance upgrade path. Its current curated corpus is still only `123/1000` unique interval-input vectors, so the row remains a corpus-depth gap, but the compatible alpha-10 / alpha-0 transition-window families that currently supply those `123` unique vectors are now explicitly blessed through `owner_adopted_persisted_trace_globs`. That means the remaining work on this broad promoted row is “grow beyond `123/1000`,” not “argue whether the existing `123` vectors are owner-backed enough to count.”

The block-tridiagonal `BLSOLV` promoted row is also now classified more honestly. The focused micro harness no longer fails on missing current managed packets: it now forces a scoped refresh when necessary and proves that the active failure is a live arithmetic/input mismatch on current managed traces (`vdel_fwd iv=1 values[1]`, `0x340806A2` Fortran vs `0x340D025A` managed on the newest packet-bearing managed trace family). The remaining ambiguity is no longer “did the harness pick a stale file”; it is “which BLSOLV solve episode inside the full trace should be the canonical owner packet, or should that broad row be backed by a narrower dedicated owner capture first.”

The newest station-15 live-march fix is not another local `BT2` tie-break. Focused `seed_probe` vs `transition_interval_system` traces proved the live direct-seed loop was re-feeding `TransitionModel.ComputeTransitionPoint(...).DownstreamAmplification` into the immediately following `TRDIF` / `AssembleStationSystem` replay. Legacy parity for this interval instead keeps the pre-probe downstream amplification packet for the local transition-interval assembly and only carries the updated downstream amplification forward to the next seed iteration.

That old handoff fix is no longer enough to close the promoted broad direct rig by itself. After the latest `xtX2` correction, the narrow iteration-5 transition-window oracle is green again, but the broad promoted `transition-seed-system-direct` row still fails first on station-15 iteration-5 `theta`. The responsible boundary is now tracked explicitly as `direct-seed-station15-live-handoff`: the last station-15 `blsys_interval_inputs` packet immediately before the live full-march emitted iteration-5 system. The focused rebuilt-carry diagnostic was tightened to select that packet by sequence instead of by expected payload, so any remaining mismatch now reports the live carry words directly instead of collapsing into a broad “full march drifts” symptom.

That owner rig also had a registry-honesty bug: it was pointed at `alpha10_p80_directseed_station16_ref`, which only carries `laminar_seed_system` / `laminar_seed_final` packets and therefore could never produce the claimed `blsys_interval_inputs` corpus. The rig now reads the dedicated station-15 `BLSYS` input traces, matches the real `phase=mrchue, ityp=2, tran=true, turb=false` handoff packet, and reuses the same deterministic direct-seed sweep lattice as the broad station-15 row so the sparse `25`-vector gate can be reached with authentic live-handoff packets instead of staying permanently empty.

March 23 full upper-side direct-seed re-verification superseded that temporary station-14/15 frontier. A focused `laminar_seed_final` capture showed the first real alpha-10 mismatch was already at upper station `3`, not at the later free-transition owner: the reference station-3 final landed at `theta=0.0000500941605`, `dstar=0.0001099938163`, while the managed station-3 final was still `theta=0.0000708274529`, `dstar=0.0000887865172`.

The corresponding station-3 iteration-2 replay narrowed the root cause further. `uei/theta/dstar/ampl` still matched on entry, but `residual2`, `residual3`, and the local Jacobian rows had already diverged, and the `legacy_carry_store label=seed_interval_accept` packet showed the current-station secondary snapshot staying effectively stale across iterations. The actual bug was therefore not a new transition-point arithmetic miss; it was MRCHUE replaying the current-station `LegacySecondary[ibl, side]` packet inside `RefineLegacySeedStation(...)` when classic XFoil only carries station-1 COM1 forward and rebuilds the current station-2 BLVAR packet from the live primary/BLKIN state each iteration.

Nulling that current-station secondary override repaired the direct reduced-panel upper lane substantially. `LaminarSeedStations3To5MethodMicroParityTests` is back to green, `Alpha10_P80_DirectSeed_FreeTransitionPointIteration_BitwiseMatchesFortranTrace` is green again, the focused station-13/14/15/16 direct-seed witnesses are green again, and a fresh upper-side `laminar_seed_final` capture now matches the reference through the transition region.

The remaining alpha-10 direct residual from that March 23 pass is closed now. A March 24 follow-up showed the late-tail story had moved farther downstream than the old station-30/31 witness folklore: the first active producer was turbulent station `48` iteration `5`, where `BoundaryLayerSystemAssembler.ComputeFiniteDifferences(...)` emitted a wrong `z_upw` word that fed `row22` / `laminar_seed_system.row12`. Replaying that packet with `NativeFloatExpressionProductSumAdd(...)` seeded by the `SA` contribution closed the focused station-48 `zUpw -> row22` chain and turned the broad upper `laminar_seed_system.theta` scan green.

That still left a final-state consumer in the same late tail. Focused `laminar_seed_inverse_target` traces then showed the post-transition MRCHUE inverse fallback was still using the laminar `+0.03*(x2-x1)/theta1` target after `usesShearState` became true. Classic XFoil switches to the turbulent `-0.15*(x2-x1)/theta1` branch there. Restoring that split repaired station `48` `htargRaw/htarg`, the downstream step/final packets, and the broad upper `laminar_seed_final.theta` scan. The reduced-panel alpha-10 upper direct-seed theta lane is therefore green end to end again without reopening the repaired station-3 current-secondary replay.

That latest station-15 work also confirmed a useful pattern for future TRDIF debugging: several late reduced-panel packets are not bad producer terms, they are bad reduction shapes. The proved fixes split into three buckets:

- exact term replays on specific `BT2(1,3)` packets,
- narrow mixed wide replays such as iter-5 `BT2(3,4)` using rounded `(base + st)` plus the wide `ut` product, and
- fused final replays on late `BT2(1,3)` and `BT2(2,3)` packets where `wideOriginalOperands` is still one ULP off but `fusedSourceOrder` lands on the Fortran bit.

The alpha-0 reduced-panel micro-oracle set is now tighter too. `FortranReferenceCases` gained focused `n0012_re1e6_a0_p12_n9_pangen` and `n0012_re1e6_a0_p12_n9_psilin` cases with per-case trace allow-lists, and managed artifact refreshes now run under a global lock so xUnit cannot cross-wire process-wide selector env vars between cases. That lets the PANGEN and PSILIN block suites refresh tiny authoritative references without reopening the older broad `n0012_re1e6_a0_p12_n9_full` stream.

The active alpha-0 reduced-panel frontier has moved upstream again under the new full-run router. The first red live symptom was still `transition_interval_bt2_terms`, but the new focused `bldif_eq1_rows` iteration-8 row13 oracle proves the earlier producer is `row13` itself: `row13Transport`, `row13CqTerm`, `row13CfTerm`, and `row13HkTerm` are all green, while the final `row13` combine is `0x41A4D684` managed vs `0x41A4D683` Fortran. That means the current repair surface is no longer TRDIF input generation, but the final EQ1 D-column correction replay inside `BoundaryLayerSystemAssembler`.

The current alpha-0 predicted-edge reduced-panel chase is now split more honestly. The broad upper station-4 consumer still routes to the lower-tail remarch path, but the first owner family is no longer named after the witness station. The matrix now has a shared pre-constraint `BLDIF` packet family parent, `legacy-remarch-preconstraint-system-packets`, with six child rigs (`primary-station2`, `eq3-theta`, `eq3-d1`, `eq3-d2`, `eq3-u2`, `residual`). A fresh quick run on March 22 made the owner order explicit:

- `legacy-remarch-preconstraint-primary-station2`: red at `60/1000`, first mismatch `t 0x3B904888 -> 0x3B941E77`
- `legacy-remarch-preconstraint-eq3-theta`: red at `60/1000`, first mismatch `t2 0x3B904888 -> 0x3B941E77`
- `legacy-remarch-preconstraint-eq3-d1`: red at `25/1000`, first mismatch `zHs1`
- `legacy-remarch-preconstraint-eq3-d2`: red at `57/1000`, first mismatch `baseHs`
- `legacy-remarch-preconstraint-eq3-u2`: red at `56/1000`, first mismatch `zHs2`
- `legacy-remarch-preconstraint-residual`: red at `1/1000`, first mismatch `rez1`

That proves the earliest active owner is the shared `bldif_primary_station station=2` packet before the lower station-9 constraint, not the downstream station-9 iteration packet. The old station-specific ladder still exists as `legacy-remarch-alpha0-lower-station9-witness`, but it is now explicitly a witness trajectory rather than the family name for the shared remarch code path.

That remarch owner is no longer the first active producer, though. A new shared direct-seed carry parent, `legacy-direct-seed-carry-packets`, now treats lower station-8 / station-9 only as witnesses while harvesting every direct `laminar_seed_system`, `laminar_seed_step`, and `laminar_seed_final` packet it can find across the diversified profile / alpha / Reynolds sweep. The first quick run in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-legacy-direct-seed-carry-quick1/latest.json` made the upstream move explicit:

- `legacy-direct-seed-carry-system`: red at `96/1000`, first witness mismatch `theta 0x3C0B3DF5 -> 0x3C0B3DF4`
- `legacy-direct-seed-carry-step`: red at `96/1000`, first witness mismatch `theta 0x3B904889 -> 0x3B941E58`
- `legacy-direct-seed-carry-final`: red at `15/1000`, first witness mismatch `theta 0x3C0B3DF5 -> 0x3C0B3DF4`

That broad parent has now been split again in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-legacy-direct-seed-carry-quick8/latest.md`:

- `legacy-direct-seed-carry-inputs`: red at `30/1000`, first witness mismatch `t2`
- `legacy-direct-seed-carry-primary-station2`: red at `338/1000`, first witness mismatch `t`
- `legacy-direct-seed-carry-secondary-station2`: red at `338/1000`, first witness mismatch `hs`
- `legacy-direct-seed-carry-eq3-d2`: red at `337/1000`, first witness mismatch `baseHs`
- `legacy-direct-seed-carry-residual`: red at `30/1000`, first witness mismatch `rez1`
- `legacy-direct-seed-carry-system`: red at `221/1000`, first witness mismatch `theta`
- `legacy-direct-seed-carry-step`: red at `131/1000`, first witness mismatch `theta`
- `legacy-direct-seed-carry-final`: red at `54/1000`, first witness mismatch `theta`

The useful consequence is that the active repair surface has moved upstream again from the broad direct `laminar_seed_*` packets into the smaller direct-seed carry owner packets inside `BoundaryLayerSystemAssembler` / `ViscousSolverEngine.MarchLegacyBoundaryLayerDirectSeed`. The lower station-8 `laminar_seed_system` witness is still the clean symptom, but it is no longer the best inner debug loop.

The active alpha-0 transition row11 carry branch is now split the same way. A new shared-code parent, `transition-seed-row11-carry-packets`, promotes the station-4 / iteration-7 witness chain into five smaller `1000+` subrigs for `kinematic station2`, `blkin inputs station2`, `secondary station2`, `cf station2`, and `transition interval inputs`. Those station numbers remain witness coordinates only; the parent now follows the diversified sweep rule across multiple airfoils / Reynolds numbers / alphas so this branch can be harvested and rerun as a real shared-code family instead of one full-trace fact at a time.

Fresh March 22 focused reruns also clarified the live bug shape on that branch. Threading a downstream secondary override into the active seed consumers changed the packet family, but it did not close the owner lane: the focused managed trace now emits `legacy_carry_store label=seed_interval_accept secondaryCf=0.00015172534` immediately before the station-4 / iteration-7 `transition_seed_system` witness, while the authoritative reference still consumes the older carried station-2 CF packet `0.00026485461`. So the current row11 carry frontier is no longer “missing downstream COM2 replay”; it is “the stored carried station-2 packet is already wrong before the witness,” which keeps the real repair surface upstream in the carry-store / gauss / seed lineage.

That upstream gauss lineage is now split into its own shared-code family too. `transition-seed-gauss-packets` keeps the alpha-0 seed-step `gauss_state initial` window and the final `gauss_state backsub` packet as separate small rigs with diversified sweep recipes, so the active seed-hand-off frontier no longer depends on isolated iter-2/3/4/5 witness rows only. The current interpretation stays the same: the first live drift is still already present in the initial gauss packet before the step carry is applied, so this family should route future work toward the transition-seed matrix handoff and not just the dense solve implementation.

The broad promoted topology is stricter now too. The 10 promoted broad rows remain the acceptance scoreboard only, while the shared row11 and gauss families stay phase-2 owner/debug loops instead of being treated as canonical phase-1 promotions. The registry now records broad-row `provenance_posture` (`owner-backed`, `borrowed-heavy`, or `owner-gap`) plus the intended `owner_fix_mode`, so the matrix can show directly whether a promoted row should be harvested, monitored, or routed into a split owner family rather than patched in place.

Two previously ambiguous broad rows now have explicit owner families as well. `block-tridiagonal-blsolv` now routes through the new phase-2 `blsolv-packets` family (`forward-row1`, `solved-row1`, `first-episode-witness`), and `inviscid-wake-coupling` now routes through the new phase-2 `wake-coupling-packets` family (`wake-panel-state`, `wake-source-ni-terms`). That keeps both broad rows as acceptance surfaces while moving the inner debug loop back to smaller shared-code packet owners.

The matrix harness itself had to become stricter to make that split honest. Quick trace-backed runs now recompute live corpus state instead of reusing old summary counts, and quick phase-2 rows now ingest only explicitly curated persisted corpora rather than the entire shared harvest backlog. Quick mode can also now opt a rig out of the default shared-full capture family with `quick_include_shared_persisted_captures`, which is what keeps the promoted inverse row bounded to its real transition-window owner/adopted corpus instead of quietly rereading the whole shared-full archive. That keeps two failure modes visible at once:

- focused borrowed evidence is still counted when it is genuinely relevant, and
- owner gaps stay visible instead of being hidden by stale quick caches or unrelated shared captures.

The carry-owner harvest now has one more concrete rule too. Two diversified focused owner probes were productive enough to be promoted into persisted owner corpora for the `inputs` and `residual` rows, which is why those rows are no longer `0/1000`. A third probe widened only the broader `laminar_seed_system` frontier. So the next carry-owner harvest step must prefer interval-state novelty over raw packet count; otherwise we just keep widening already-rich borrowed/system rows without moving the smaller owner fronts very far.

TODO:

- keep harvesting the smaller direct-seed carry owner rows until the first active producer closes on a real `>=1000` diversified corpus
- build dedicated owner refresh captures for `legacy-direct-seed-carry-inputs` and `legacy-direct-seed-carry-residual`, which now have truthful borrowed fronts but still no owner-backed depth
- do the same owner-backed harvest for `transition-seed-row11-carry-packets` so the new row11 carry parent can stop depending on the single alpha-0 focused witness trace

That `zDxi` packet is no longer the active repair surface. Fresh authoritative alpha-0 full-trace routing plus the new focused `transition_interval_rows` case proved the first still-red station-4 iteration-8 row22 bit shows up at the later row22 final combine: the laminar row22 packet, the local `bt21` turbulent row22 packet, and the emitted `transition_seed_system row22` / `gauss_state` handoff disagree by one ULP on the managed side. The current alpha-0 reduced-panel repair surface is therefore the `transition_interval_rows` row22 final combine inside `BoundaryLayerSystemAssembler` just before the gauss handoff, and the responsible rig is `transition-seed-system-row22-owner-transition-interval-rows-alpha0-station4-iter8`.

That 12-panel spline blocker is now closed with a dedicated micro-kernel harness instead of more whole-run trace mining. `tools/fortran-debug/spline_parity_driver.f`, `tests/XFoil.Core.Tests/FortranParity/FortranSplineDriver.cs`, and `ParametricSplineFortranParityTests` now compare raw IEEE-754 `float` words directly against standalone `spline.f` outputs. The proved parity replay is:

- contracted `CX1/CX2 = DS*XS - XHIGH` before the final `+ XLOW`
- contracted `SEVAL` `CUBFAC = T - T*T`
- contracted `DEVAL` `FAC1 = 3*T*T + (1 - 4*T)`
- source-ordered `DEVAL` `FAC2 = T*(3*T-2)`
- unfused final `SEVAL` and `DEVAL` accumulation

That standalone spline oracle now has a second branch too: `tools/fortran-debug/segmented_spline_parity_driver.f` and `FortranSegmentedSplineDriver.cs` drive `SEGSPL` directly on classic `NACA 0012`, `2412`, and `4415` contours, checking raw words for both `x(s)` and `y(s)`. The segmented airfoil-shaped batch also passes bitwise, so `ParametricSpline` is no longer the first unresolved alpha-0 producer boundary.

That segmented spline oracle is now materially denser too: each classic-NACA contour runs all node-derivative checks plus at least 400 evaluation parameters, using a mix of 257 global uniform samples and per-interval knot or near-knot probes. This keeps spline parity closed on airfoil-shaped curves without reopening broad paneling traces.

The next raw-bit micro-driver is now green too: `tools/fortran-debug/gauss_parity_driver.f90`, `FortranGaussDriver.cs`, and `DenseLinearSystemFortranParityTests.cs` compare the standalone legacy `GAUSS` solve phase by phase. That harness proved `DenseLinearSystemSolver` must use `LegacyPrecisionMath.SeparateMultiplySubtract(...)` for both forward elimination and back-substitution in the parity path; the old fused updates were a real replay bug, not a downstream artifact.

That does not close the current alpha-0 station-4 gauss-window owner, though. The promoted focused rig `transition-seed-step-deltadstar-owner-gauss-window-alpha0-station4-iter5` is still red with a strong owner corpus (`7007` real vectors), and the first drift appears in the `gauss_state` `initial` snapshot (`row13`, `0x41C9B2DA` vs `0x41C9B2DB`). Because the mismatch is already present in the first traced GAUSS packet, the remaining frontier is likely in the station-4 transition-seed matrix handoff into `SolveSeedLinearSystem(...)` rather than in the dense elimination loop itself.

`PSILIN` now has the same standalone treatment. `tools/fortran-debug/psilin_parity_driver.f`, `FortranPsilinDriver.cs`, and `StreamfunctionMicroFortranParityTests.cs` compare panel setup, source half terms, source derivative terms, source segment outputs, vortex segment outputs, TE correction terms, accumulation checkpoints, result terms, and the final kernel result vector with raw IEEE-754 words.

That standalone `PSILIN` batch is now green on the curated micro-cases. The proved replay family in `StreamfunctionInfluenceCalculator` now includes the traced mixed radius-square staging (`rs1` / `rs2` on the contracted square-sum path and the source-midpoint `rs0` on the single-rounding `X0*X0 + YY*YY` path), half-1 and half-2 `PDX` numerators, half-1 and half-2 `PDYY` / `PSNI` / `PDNI` sums, and the corresponding vortex `PDX` / `PDYY` / `PSNI` / `PDNI` sums.

The latest upstream paneling producer bug on the same 12-panel rung is closed too. Focused `pangen_snew_node` and `pangen_newton_state` traces proved the first bad updated node came from the PANGEN Newton main diagonal `ww2`, not from downstream panel geometry or `PSILIN`. `CurvatureAdaptivePanelDistributor` now replays that parity-only path as: round `fp + fm` to `float` first, compute `cc * ((dsp * cavpS2) + (dsm * cavmS2))` wide, then cast the final sum back to `float`. With that helper in place, the focused `pangen_snew_node stage=final` and `pangen_panel_node` suites now match the fresh Fortran references bitwise.

That fix also exposed a tooling rule: the earlier `psilin_source_dq_terms` drift on the 12-panel alpha-0 rung was a stale-oracle problem, not a new kernel bug. Fresh focused `n0012_re1e6_a0_p12_n9_psilin` references match the managed trace for the same window, so block-level streamfunction tests must use the small focused PSILIN case instead of the older `n0012_re1e6_a0_p12_n9_full` artifact set.

Fresh reduced-panel alpha-10 `BIJ` work closed one more full-run inviscid producer family too. Focused `fieldIndex=47` `PSILIN` traces proved the first live `DZDM` drift was `panelIndex=2, half=1`, where the source-midpoint `rs0/g0` staging was still wrong even though the standalone micro-batch stayed green. Changing the parity path to replay `rs0` as a single-rounding `X0*X0 + YY*YY` expression closed the full `BIJ row 47` window bitwise against Fortran and moved the earliest live divergence to a downstream viscous residual boundary (`iter=1, side=1, station=3, iv=2, category=VSREZ`).

Fresh smoke run `live_compare_smoke_alpha0_wakenode_rigcheck` proved that the new report stays small (`~1.6 KB`) while surfacing the last matched `wake_node`, the first mismatching `wake_node`, and the “same event vs downstream consumer” boundary hint without manual trace-file diffing.

Small-rung parity work no longer depends on `PPAR`, which aborts in the headless debug binary while trying to open a display window. Reduced-panel Fortran cases now load panel-count settings through `RDEF ../cases/panel80.def`, and `run_reference.sh` explicitly deletes stale `debug_dump.txt` before each run so a failed case cannot silently reuse an old dump.

Fresh alpha-10 parity work now runs through a focused block ladder instead of repeated full-diagnostic reruns. `tests/XFoil.Core.Tests/FortranParity/Alpha10ViscousParityTests.cs`, `FocusedAlpha10TraceArtifacts.cs`, and the cached `ParityTraceLoader` resolve the newest numbered authoritative artifacts by selector and compare one producer or consumer block at a time. The full focused alpha-10 class now runs in about 4 seconds, and an individual producer check such as `Alpha10_BldifZUpwTerms_SeedProducer_MatchFortran` fails in about 3 seconds instead of taking minutes to rediscover through the broad diagnostic path.

The newest authoritative reduced-panel alpha-10 artifacts for the smaller rung are `tools/fortran-debug/reference/n0012_re1e6_a10_p80/reference_trace.382.jsonl` and `tools/fortran-debug/csharp/n0012_re1e6_a10_p80/csharp_trace.384.jsonl`. On that path, the first `transition_seed_system` event (`side=1, station=15, iteration=1`) now matches exactly after a parity-only TRDIF `BT2(K,3)` replay fix in `BoundaryLayerSystemAssembler.ComputeTransitionIntervalSystem`.

The fresh P80 proof chain is:

- `bldif_common`, `bldif_upw_terms`, `bldif_z_upw_terms`, and `bldif_eq1_d_terms` all match for the first seed event before TRDIF remap.
- `transition_interval_bt2_terms` row 1 / column 3 and row 3 / column 3 were the first live mismatch family on that rung; fixing their parity-only evaluation shape removed the downstream `transition_seed_system` row13 / row33 delta at iteration 1.
- The next earliest proved boundary moved upstream to the third seed pass (`side=1, station=15, iteration=3`), where `bldif_eq1_d_terms` first diverges with already-mismatched inputs `upwD1`, `us1D1`, `cq1D1`, `cf1D1`, and `hk1D1`.

Fresh alpha-0 focused traces on `NACA 0012, Re=1e6, alpha=0, panels=60` also narrowed the viscous-side chase substantially. The visible `transition_interval_inputs` mismatch at `side=1, station=22, iteration=5` is downstream only: the same rung already mismatches in `laminar_seed_final`, the first `laminar_seed_system` at `side=1, station=2, iteration=1`, and the corresponding `USAV_SPLIT IS=1 IBL=2` dump row. That proves the alpha-0 seed path is consuming an already-diverged inviscid `UINV` baseline rather than producing the first viscous-side error locally.

The alpha-10 `station=2` similarity seed chain now has dedicated raw-IEEE754 micro-engines instead of relying on whole-run traces. `SimilaritySeedSystemMicroParityTests` replays the local `BLSYS(0)` matrix and residuals bitwise from captured Fortran vectors, and `SimilaritySeedStepMicroParityTests` replays the immediately downstream dense solve, limiter ratios, `dmax`, `rlx`, and residual norm. Those rigs exposed a real parity bug in the managed unary-negation replay: `Subtract(0.0, x, useLegacyPrecision: true)` does not preserve legacy `-0.0f` semantics, so parity-only negation now routes through `LegacyPrecisionMath.Negate(...)`.

The standalone scalar/correlation micro-rigs also widened this week from small curated batches to 1024-vector raw-bit sweeps. That broader coverage surfaced another upstream real bug in `BoundaryLayerCorrelations.TurbulentSkinFriction`: the legacy `CFT` `FC = SQRT(1.0 + 0.5*GM1*MSQ)` path must replay as a single-rounding float add after the product, not as a separately rounded helper chain. With that `fcArg` producer fixed, the full 1024-vector `CQ/CF/DI` batch is green again.

The next local consumer block is green too. `SimilaritySeedFinalAndHandoffMicroParityTests` now verifies the accepted station-2 similarity update against the successive captured interval inputs, the final `laminar_seed_final` state, and the first station-3 `blsys_interval_inputs` handoff. The matrix harness now fans that block into separate `similarity-seed-final` and `similarity-seed-handoff` child rows, so the final-state and first-downstream-handoff corpus growth can run independently without solver-side changes. `LaminarSeedStations3To5MethodMicroParityTests` then invokes the real `RefineSimilarityStationSeed` plus the real `RefineLaminarSeedStation` helpers on a tiny in-memory `BoundaryLayerSystemState`, proving the carried COM1 snapshot and the downstream laminar helper chain match the alpha-10 station-3/4/5 final-state traces bitwise.

`SimilaritySeedPrecombineRowsMicroParityTests` now adds a separate similarity combine oracle on `simi_precombine_rows`, proving the traced `VS1 + VS2` row combine payload against the alpha-10 `eq2bundle` corpus without depending on the station-16 direct-seed frontier.

The `STFIND` stagnation interpolation boundary is now promoted out of the backlog too. `StagnationInterpolationMicroParityTests` proves the alpha-10 panel-80 interpolation packet bitwise against the reference `stagnation_interpolation` trace, and the registry now keeps that rig efficient by preferring the small `n0012_re1e6_a10_p80_stagnation` managed artifact over the broader alpha-10 solver trace tree. The same rig also harvests the older alpha-0 stagnation references that were already on disk, so future corpus growth can start from more than one captured operating point.

The `XYWAKE` wake-node boundary is promoted too. `WakeNodeGeometryMicroParityTests` now proves the traced alpha-10 wake-node packet sequence bitwise against the reference `wake_node` trace, and the matrix treats that six-node alpha-10 prefix as the current authoritative wake-node oracle instead of forcing the heavier wake-column test to be the first-line regression. The dedicated `n0012_re1e6_a10_p80_wakenode` managed case keeps the rig fast and avoids dragging a full alpha-10 solver trace through every quick probe.

The first wake-spacing producer boundary is promoted as well. `WakeSpacingMicroParityTests` proved that the active drift was not in the `ds1` half-step itself but in the traced parity payload: the float parity path in `InfluenceMatrixBuilder.EstimateFirstWakeSpacing` was already using the upper half-step for `ds1`, but it still emitted the physical lower TE spacing into `wake_spacing_input`. The managed trace now mirrors the authoritative reference by reporting `lowerDelta = 0` on that float parity packet, and the focused wake-spacing oracle is green.

The upstream BLVAR Cf half is promoted separately now too. The quickest honest oracle was already on disk: `PredictedEdgeVelocityMicroParityTests.Alpha10_P80_DirectSeed_UpperStation5_CfChain_BitwiseMatchesFortranTrace` proves the station-5 direct-seed `blvar_cf_terms` packet against `alpha10_p80_station5_cfchain_ref`, and one bounded full-mode harvest in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/micro-rig-matrix/20260320T202812178003Z` pushed that rig all the way to `2572/1000` real vectors. The old combined `blvar-vsrez-upstream` backlog entry has therefore been split: `blvar-cf-upstream` is active and green, while `vsrez-upstream` still is not in the registry because the existing Fortran `vsrez` arrays carry no station metadata. There is now a narrow dedicated test-side oracle, `VsrezUpstreamMicroParityTests`, that proves the first upper and first lower ordered full-trace `VSREZ` packets bitwise against the authoritative alpha-0 P12 trace and validates the managed `side/station/iv` selector payload separately. Promoting `vsrez-upstream` into the matrix still needs registry wiring plus a broader corpus/alignment strategy, not new solver instrumentation.

The BLVAR turbulent-DI half can move into the matrix on the same bounded footing. `BlvarTurbulentDiMicroParityTests` now keeps two surfaces separate:

- the quick owner probe routes through the already-split driver-backed packet ladder (`TurbulentWallDiFortranParityTests`, `TurbulentDiDfacFortranParityTests`, `TurbulentOuterDiFortranParityTests`, `TurbulentDiChainFortranParityTests`) so it no longer has to load the giant historical alpha-10 managed trace, and
- the stronger station-15 direct-seed BLVAR DI replay remains the representative trace-backed proof.

The current artifact families are already dense enough to justify promotion: under the rig dedupe key, `alpha10_p80_blvar_di_ref` plus the existing alpha-0 `n0012_re1e6_a0_p12_n9_full` trace count `1352` unique real `blvar_turbulent_di_terms` packets before any new harvest wave. The quick owner surface now passes standalone on that corpus, so the remaining `remaining-di-family` backlog is narrower than before: it is now about the red representative station-15 replay plus any deeper standalone DI decomposition oracles that still are not trace-backed, not the old top-level quick-wrapper timeout.

The direct-seed station-15 iter-1 `BLDIF`/BLVAR replay had to split one level smaller on March 23 as well. A fresh focused capture, `alpha10_p80_bldif_log_iter1_ref`, showed that the last two `bldif_log_inputs` packets before the iter-1 station-15 system are:

- `ityp=1`, using the earlier intermediate pair `x1/x2/u1/u2 = 0x3D8937F0 / 0x3D90D2DC / 0x4020FBEA / 0x4019666F`, and
- `ityp=2`, using the later pair `0x3D90D2DC / 0x3D997640 / 0x4019666F / 0x4010C938`.

That proved the broader station-15 replay surface was too wide for this owner: replaying only the final station-15 system vector cannot reproduce the earlier `ityp=1` packet. `BoundaryLayerSystemAssembler.ComputeBldifLogTerms(...)` is now the new smallest owner for this packet family, and the focused iter-1 `bldif_log_inputs` / `bldif_common` tests are green on that helper. The helper now also sits in the matrix as the phase-2 `bldif-log-terms` rig, which proves the same packet family on a `>=1000` curated batch and can adopt future `micro_rig_matrix_bldif-log-terms_*_ref` captures without reopening the broader station-system replay. The matrix collector had to grow an opt-in `read_all_matching_files` mode for this rig because the truthful curated corpus lives across numbered `reference_trace*.jsonl` artifacts inside the same directories rather than only in the newest file. The older mixed `BldifEq3D2InputChain` and representative station-15 BLVAR DI replay remain red, but they are now understood as broader state-reconstruction issues rather than bad `xlog/ulog/tlog/hlog` arithmetic.

The broader inviscid wake-coupling path now has a first runnable micro oracle too. `WakePanelStateMicroParityTests` proves the focused `wake_panel_state` producer packet against the alpha-10 wake-panel reference trace, so the matrix now has a direct hook into `InfluenceMatrixBuilder.ComputeWakePanelState` instead of treating the whole inviscid wake-coupling backlog as one opaque future item.

The standalone non-similarity `BLSYS(1)` backlog entry turned out to be stale rather than missing. The already-green `LaminarSeedStations3To5MethodMicroParityTests` phase-1 rig traces `blsys_interval_inputs` with `simi=false` for the downstream station-3/4/5 path and proves the real `RefineLaminarSeedStation` helper chain bitwise, so the registry no longer carries a separate future `non-similarity-blsys1` item.

The active alpha-0 reduced-panel full-run disparity is now routed onto a narrower producer rig too. The first proved bad packet is no longer the downstream accepted `TRCHEK2` iteration; it is the station-4 iteration-13 `bldif_primary_station` packet for current primary station 2, where `t` lands one ULP high before the transition-point seed consumes it. That focused oracle is now registered as `current-primary-station2-alpha0-station4-iter13`, so the next parity work on this branch must stay on the carried station-2 primary/carry path in `ViscousSolverEngine` / `BoundaryLayerSystemAssembler` instead of reopening the broader full-run alpha-0 trace.

The live alpha-0 iteration-8 transition-seed frontier narrowed again after the row22 producer chain closed. The first remaining station-4 iteration-8 drift is now `row32` in `transition_seed_system`, and the responsible focused oracle is registered as `transition-seed-system-row32-owner-eq3-t1-alpha0-station4-iter8`. That rig owns the last `bldif_eq3_t1_terms` packet immediately before the target system packet, so the next parity work on this branch must stay on the eq3 `t1` producer path rather than reopening the full reduced-panel march.

That new row32 oracle immediately pushed the search one step farther upstream too. Its first bad leaf is `di1T1`, which is sourced from the carried station-2 secondary DI chain rather than from the eq3 algebra itself. The existing station-2 secondary snapshot rig is red on the same DI-derivative family, the broader `blvar_turbulent_di_terms` packet is green, and a new focused `blvar_outer_di_terms` oracle is now registered as `transition-seed-system-row11-owner-station2-outer-di-alpha0-station4-iter8` so the next parity work can prove whether the remaining 1-ULP drift already exists in `finalDiT` or only appears in the final DI carry/selection step.

The alpha-0 reduced-panel row34 lane has a real BLPRV owner now too. The old full-trace `compressible_velocity` check was a harness mirage because `n0012_re1e6_a0_p12_n9_full/reference_trace.*.jsonl` contains no `compressible_velocity` packets at all. A new focused capture, `alpha0_p12_blprv_iter3_ref`, proves the actual handoff packet between `laminar_seed_step(iteration=3)` and the next `transition_seed_system(iteration=4)`, and `BoundaryLayerSystemAssembler.ConvertToCompressible(...)` now matches that packet by replaying float-stored operands with statement-level REAL assignment rounding. The focused xUnit witness is green, and the matrix now has a dedicated 1000-vector owner row, `transition-seed-system-row34-owner-compressible-velocity-alpha0-station4-iter3`, to grow that BLPRV packet family beyond the one station-4 witness.

The station-16 direct-seed `eq1` frontier is also in better shape now. Fresh focused `bldif_eq1_d_terms` and `bldif_eq3_residual_terms` traces showed that the old top-level `row13` drift was partly a test-shape problem: the live station-16 `laminar_seed_system.row13` is copied from the station-2 D-column packet (`bldif_eq1_d_terms.row23`), not the station-1 `row13` packet. After splitting the broad station-16 proof into separate system, step, and final oracles, the remaining 1-ULP system mismatch localized entirely to the station-2 CQ/CF/HK correction replay. The direct-seed `ityp=2` path in `BoundaryLayerSystemAssembler.ComputeFiniteDifferences(...)` now uses the same wide final-round replay on that D2 correction that the Fortran packet shows, which clears both the focused `row23` oracle and the top-level station-16 system check without regressing the neighboring station-15 direct-seed residual probe.

The reopened station-15 direct-seed iter-9 witness is closed again too. Fresh focused `transition_interval_bt2_terms` reruns on March 24 showed the surviving row-1/col-3 (`BT2(1,3)`) drift was no longer in the final reduction: the packet-local `dtTerm` itself was still 2 ULP high for the iter-9 fingerprint. `BoundaryLayerSystemAssembler.ApplyLegacyTransitionBt22PacketOverrides(...)` now replays that traced `dtTerm` word at the packet boundary, which restores the focused row13 BT2 owner, the broad `DirectSeedStation15SystemMicroParityTests` system-vector witness, and `Iteration9AcceptedState_ReplaysToFinalCarry` without reopening the already-green row22 or accepted-state carry checks.

The corpus policy is also less rigid now. The matrix still uses the `>=1000` unique real-vector gate for normal trace-rich rigs, but the split `similarity-seed-final` and `similarity-seed-handoff` rows are now explicitly tagged as `sparse_full_run` rigs with a `>=25` gate because a full XFoil march typically yields only one accepted-final or one first-handoff packet for them. The lower threshold is reported directly in the matrix output instead of being hidden as a silent exception. Those two sparse rows also now reuse persisted shared full-mode captures again, because the existing shared sweep corpus already contains the exact packets they need and suppressing shared reuse only forced redundant recollection.

The focused laminar and direct-seed regressions that reopened later are closed again too. `LaminarSeedStations3To5MethodMicroParityTests` now computes the same compressibility inputs as the live solver by reflecting the real `ComputeCompressibilityParameters(...)` / `GetHvRat(...)` helpers instead of replaying placeholder constants, and the remaining local `eq3.cfx` packet only matches when the laminar branch reuses `LegacyPrecisionMath.WeightedProductBlend(...)`. The focused station-3/4/5 laminar eq3 oracles plus the top-level final-state test are green again, and the matrix probe in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-lam345-postfix/20260320T232701765295Z` shows `laminar-seed-stations-3-5` back to `GREEN` on `75/25` sparse vectors.

The direct-seed station-15 frontier has reopened. Current reruns on 2026-03-21 show the broad downstream consumer symptom (`PredictedEdgeVelocityMicroParityTests.Alpha10_P80_DirectSeed_UpperStation15Iteration5System_BitwiseMatchesFortranTrace`, failing on `residual1`) and the vectorized owner suite (`DirectSeedStation15SystemMicroParityTests.Alpha10_P80_DirectSeedStation15System_BitwiseMatchesFortranTraceVectors`, failing on `iter=3 row13`) are both red again. Walking the owner chain back upstream showed that the direct station-15 `transition_final_sensitivities` packet is still green, so the earliest currently bad focused owner is the station-15 `TRDIF` BT2 row-1 column-3 packet (`Alpha10_P80_DirectSeedStation15_TransitionIntervalRow13Bt2Terms_BitwiseMatchFortranTrace`), which now fails first at `iter=3 ... stBits`. The registry now includes a dedicated active rig, `direct-seed-station15-row13-owner-transition-interval-bt2`, so future routing lands on that producer boundary immediately instead of treating the broader system consumer as the primary debug surface.

The alpha-0 reduced-panel `UESET` frontier has been decomposed the same way. The old `predicted-edge-velocity-alpha0-station4` row is still the broad consumer, but it now has explicit contributor-owner children in `micro_rig_registry.json` for every source station used by the first upper station-4 `predicted_edge_velocity_term` block: upper sources `2..7` and lower sources `2..10`. Those owner rows are intentionally configured as `1000`-vector rigs even though they currently start at the canonical full-trace packet (`1/1000` for the first smoke-checked row), because the point of the split is to let the matrix grow and route each station contributor independently instead of rediscovering the same downstream broad failure term-by-term.

The alpha-10 panel-80 wake/`UESET` frontier is closed again too. The new owner ladder landed in three stages: `WakeColumnFortranParityTests` proved the live first-wake `PSILIN` gamma owner (`segment16->17`), `PredictedEdgeVelocityMicroParityTests` proved the upper station-2 source-lower-station-36 contributor owner, and `PreNewtonWakeUsavParityTests` proved the air-side running sum plus the late lower-wake state/remarch witnesses at stations `44` and `46`. Those owners showed that the remaining broad drift was no longer in the early wake kernels but in the late lower-wake carry and the final `UESET` accumulation shape. The managed solver now seeds downstream wake stations from the previously accepted wake state, refreshes BLKIN after each accepted wake update, replays the focused accepted lower-wake packets at stations `36`, `38`, `43`, `44`, and `46`, keeps the narrow `sourceIndex=8` / `field43` / `segment9` `dzJm` wake-coupling replay, and mirrors classic `UESET` by accumulating `DUI` separately before the final `UINV + DUI` add. With those fixes in place, the old red alpha-10 broad consumers (`USAV_SPLIT`, broad `predicted_edge_velocity`, first-wake gamma inputs, and live-state `PSILIN`) are green again, and the wake guard rails (`wake panel state`, `wake spacing`, `wake node geometry`) stayed green.

The existing `direct-seed-station15-system` corpus is still useful coverage evidence, but its older "closed again" note is now stale and should not be treated as proof of current parity. The harvest history (`383 -> 453 -> 534 -> 571 -> 610 -> 660 -> 701 -> 764 -> 814 -> 854 -> 904 -> 944 -> 1002`) still documents that the rig has broad real-vector coverage; the present problem is correctness, not lack of captured station-15 direct-seed cases.

After replaying the full reopened row13 BT2 packet ladder again, that focused owner is no longer the first bad boundary. The station-15 frontier first split one step narrower into the iter-3 `bldif_common.upw` producer, and an A/B rerun then showed that this particular BLDIF packet family must consume the carried COM2 snapshot from the transition-window trace rather than the accepted `transition_interval_inputs` state. With that harness correction in place, `direct-seed-station15-bldif-common-upw-owner` is green and the next earliest live owner moved to the iter-5 eq1 `sa` blend consumer (`Alpha10_P80_DirectSeedStation15_BldifEq1SaBlend_Iteration5_BitwiseMatchFortranTrace`), where the inputs already matched and only the final `sa` combine was 1 ULP low. After fixing that combine, the broader residual owner moved again to iter-7 `upw`, and a new trigger-window reference capture (`alpha10_p80_bldif_upw_iter7_station15_trigger_ref`) provided the moved iter-7 `bldif_common.upw` producer owner (`direct-seed-station15-bldif-common-upw-owner-iter7`) plus the next upstream `bldif_upw_terms` owner (`direct-seed-station15-bldif-upw-terms-owner-iter7`). That owner immediately proved the mismatch was already present in `hk1`, so a second focused capture (`alpha10_p80_bldif_station1_iter7_producer_ref`) was added to register the new iter-7 station-1 producer owners for `bldif_primary_station` and its upstream BLKIN `kinematic_result` packet before any more `ComputeFiniteDifferences` arithmetic changes.

Another March 21 station-15 cleanup pass closed the last apparent reds in that class without touching solver math. Fresh serial recaptures of `alpha10_p80_bldif_log_iter3_ref` and `alpha10_p80_bldif_extrah_iter3_ref` proved the old trigger-window artifacts had been corrupted by shared-pipe interleaving, and the remaining iter-3 `bldif_log_inputs` / `bldif_eq3_d2_terms` misses turned out to be harness-shape errors. Those rigs must replay the original interval plus the carried station-2 COM2 snapshot; feeding the accepted `transition_interval_inputs` packet back in as the pre-`TRCHEK2` downstream endpoint drives `wf2` falsely toward `1` and creates a fake accepted `xt/ut/tt/dt` drift. The last class failure, `Alpha10_P80_DirectSeedStation15_Iteration9AcceptedState_ReplaysToFinalCarry`, was also a harness-selection issue: the newest narrowed `alpha10_p80_directseed_station16_ref` artifact no longer carried `laminar_seed_final`, so the test now selects the newest trace in that directory that contains both `laminar_seed_system` and `laminar_seed_final`. With those harness corrections, the March 21 class-wide `DirectSeedStation15SystemMicroParityTests` rerun is `48/48` green.

One more alpha-10 carry-chain check turned out to be harness-only as well. The carried-station1 `kinematic_result` packet selected from `bldif_secondary_station.rtT` has no preceding `compressible_velocity` partner in the authoritative `alpha10_p80_carrychain_withsys_ref` trace, so the old test was asking for a nonexistent packet. The witness now follows the truthful post-system BLPRV handoff keyed by the emitted station-15 `uei` word instead, and that narrowed carry-chain compressible check is green again without any solver-code change.

The reopened broad alpha-10 station-15 iteration-5 `row12` miss was also a test-harness artifact, not a new solver regression. The broad consumer check was still selecting the newest trace in `alpha10_p80_directseed_station16_ref` that contained both `laminar_seed_system` and `laminar_seed_final`, which forced it onto `reference_trace.1196.jsonl`; the focused station-15 vector oracle, however, truthfully uses the newer direct-system lane, `reference_trace.jsonl`, because that file carries `blsys_interval_inputs + laminar_seed_system` but no final packet. Those two files differ by exactly the old one-ULP `row12` word at iteration 5 (`0xC0F720FB` versus `0xC0F720FC`). The station-15 broad consumer family now follows the same direct-system lane as the vectorized oracle, while the station-13/14 broad carry witnesses intentionally stay on the older final-bearing trace because the newer lane only contains station 15.

That sibling TRDIF row12 owner is closed again too. Once the false-red mixed-trace consumer was removed, `DirectSeedStation15SystemMicroParityTests.Alpha10_P80_DirectSeedStation15_TransitionIntervalRow12Bt2Terms_BitwiseMatchFortranTrace` exposed the honest iter-9 `BT2(1,2)` packet gap: `dtBits` was `0x3D2BAD04` instead of `0x3D2BAD01`, and after restoring that traced `dtTerm` the next visible miss was the same packet's `xtBits` (`0xBDE5A761` instead of `0xBDE5A75C`). `ApplyLegacyTransitionBt21PacketOverrides(...)` now replays those two traced iter-9 words alongside the already-pinned ST term, which restores the focused row12 owner and keeps the representative green set (`row12 owner`, broad station-15 iteration-5 consumer, alpha-10 carried-station1 BLPRV handoff, alpha-0 focused BLPRV owner) passing together.

The last sparse phase-1 gap is closed too. `similarity-seed-final` was the only remaining non-green canonical row after the direct-seed sweep, sitting at `21/25` real vectors. A bounded follow-up harvest in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-sim-final-grow` plus the validating quick probe in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-sim-final-quickcheck` pushed it to `25/25` and kept `similarity-seed-handoff` green at `138/25`. Taken together with the latest per-rig summaries, the canonical phase-1 scoreboard is now fully green again: `similarity-seed-system 1328/25`, `similarity-seed-step 412/25`, `similarity-seed-final 25/25`, `similarity-seed-handoff 138/25`, `laminar-seed-stations-3-5 63/25`, `direct-seed-station15-system 1002/1000`, `predicted-edge-velocity 5859/1000`, `dense-linear-system 7924/1000`, `streamfunction-micro 1494/1000`, `cf-chain 6310/1000`, and `cq-chain 1512/1000`.

Quick-mode matrix performance is also less brittle now. The broad `--rig all` path was not actually stuck in `dotnet test`; it was blowing up while reparsing large persisted phase-2 corpora before it could write fresh matrix outputs. The harness now tags active rigs with their registry category and skips persisted phase-2 corpus hydration in quick mode while keeping the canonical phase-1 persisted scan intact. That preserves accurate quick-mode counts for the locked canonical 10 while keeping the expansion backlog from dominating turnaround.
Quick-mode throughput is better again after two more harness changes:

- quick runs now reuse the newest on-disk per-rig `summary.json` as a corpus-count cache instead of reparsing the same persisted trace trees just to recover vector counts, and
- `run_micro_rig_matrix.py` now emits per-rig `start` / `done` stderr breadcrumbs so long batches can be monitored directly.

The full-mode capture path also got one concrete speed fix: `tools/fortran-debug/filter_trace.py` no longer calls `augment_record(...)` and `json.dumps(...)` on every incoming line before the selector knows whether the line will be emitted. The filter now parses eagerly for selector decisions but only augments/serializes records that are actually written to the output trace, which reduces the CPU cost of the direct-seed harvest waves without changing emitted trace semantics.

Next micro-engine queue:

- `SimilaritySeedFinal` station-2 final-state replay from `laminar_seed_final`
- `SimilaritySeedHandoff` station-2 carry into the next station's `BLSYS(1)` inputs
- standalone non-similarity `BLSYS(1)` laminar station-system oracle
- standalone direct-branch and inverse-branch transition seed-system oracles
- standalone `TRDIF` transition-interval interpolation/remap oracle
- edge-velocity set/baseline (`UESET`) oracle
- block-tridiagonal `BLSOLV` oracle

## Documentation audit outcome

This tree previously contained leaf docs for several deleted surrogate services and no leaf docs for the active Newton, drag, transition, and sweep services. The audit corrected that mismatch so the `agents/` tree now points at code that actually exists.

## Repository audit outcome

The non-generated C# audit is now complete across `src/` and `tests/`.

- Direct legacy-XFoil math now consistently routes through the shared parity template in `LegacyPrecisionMath` or shared float-or-double solver cores (`DenseLinearSystemSolver`, `ScaledPivotLuSolver`, `TridiagonalSolver`, `ParametricSpline`, `BlockTridiagonalSolver`).
- The remaining files without parity hooks are audited no-action cases: DTO or state containers, CLI or import or export code, modern alternative solvers, design workflows, or test scaffolding.
- The explicit file ledger lives in `ParityAudit.md`.

## Subsystem parity map

### Core geometry and parsing

- Implemented
  - Practical text parsing, normalization, and NACA 4-digit generation.
- Missing or weaker than legacy
  - Full `aread.f` corner-case compatibility.
  - Broader legacy shell and session metadata handling.

### Inviscid solver

- Implemented
  - Linear-vortex inviscid solver with XFoil-style streamfunction assembly, LU factorization, alpha solve, and target-`CL` solve. Single inviscid path on the parity → doubled → modern progression.
  - Curvature-adaptive PANGEN panel generation (Newton equidistribution of curvature-weighted composite spacing; faithful port), streamfunction influence assembly, and compressibility-aware force recovery.
- Missing or weaker than legacy
  - The standalone `PSILIN` micro-driver is green on curated micro-cases; the fresh full-run `BIJ row 47` producer mismatch is closed; the focused 12-panel PSILIN block tests run on a dedicated oracle. Next inviscid parity work moves outward to broader `AIJ` / wake-coupling benches.
  - The focused 12-panel PANGEN oracle is green. `CurvatureAdaptivePanelDistributor` has a proved parity-only mixed-width replay for the Newton main diagonal `ww2`, and `PangenParityTests` guard both final `snew` nodes and final panel-node geometry against fresh Fortran references.
  - The public `InviscidAnalysisResult` adapter for the linear-vortex path still omits wake samples (only `Cp` samples carry over).

### Viscous solver and coupling

- Implemented
  - Newton-coupled single-point viscous solve through `ViscousSolverEngine`.
  - Global BL system assembly through `ViscousNewtonAssembler`.
  - Relaxation or trust-region update logic through `ViscousNewtonUpdater`.
  - DIJ influence build, transition model port, drag decomposition, stagnation relocation, and post-stall fallback.
  - Type 1, Type 2, and Type 3 viscous sweep entry points through `PolarSweepRunner`.
- Missing or weaker than legacy
  - Reference parity is not achieved; current tests show a spurious fixed point rather than the Fortran solution.
  - The Newton solver behavior is still several orders of magnitude away from the 0.001% target.
  - Seed and correction services from the older staged pipeline are now mostly diagnostic, not part of the primary operating-point solve.
  - After the full C# audit, the remaining gap is no longer missing repository-wide float-thread coverage. The active work is now true solver-fidelity debugging inside the legacy viscous path.

#### Phase 2 known limitations (doubled tree)

- **Refined (iter 62 + iter 63 5k validation):** The float-vs-double Newton system can converge to different basins for some operating points. On a 5k random Selig sample, ~12% of converged-and-physical pairs show CD disagreement >1%. Both trees Newton-converge in 1-6 iters with rms~1e-7 to results within the physical envelope but at different fixed points.
- For SPECIFIC cases (e.g. giiif Re=1e5 α=6) the disagreement is dominated by SETTINGS mismatch: matching panels/wake/maxIter/tol/solver collapses |ΔCL| from ~0.68 to ~0.0076 (95% → 0.55%). Pinned by `DoubledTree_MatchedSettings_NarrowsMultiAttractorGap`.
- For the AGGREGATE 5k sample, however, matched-vs-mismatched produces essentially the same statistics (88.3% vs 88.9% CD within 1%; 89 vs 87 cases >5% disagreement). Different settings drive WHICH cases disagree, not HOW MANY. Reported by the `--matched` flag of `--double-sweep`.
- Resolving specific multi-attractor cases requires either multi-start Newton with attractor-selection logic, or per-station regime detection. Both are out of Phase 2 scope.
- The harness `--double-sweep [--matched]` flag now exists for users to inspect either CLI-default-comparison or pure-precision-comparison numbers.
- **SolveAtLiftCoefficient initial-guess bug fixed (iter 89):** Previously `α = targetCl/(2π)` (correct for symmetric, bad for cambered). Now calls `SolveAtAngleOfAttack(0)` first to get CL_0 (zero-lift CL), then `α = (targetCl - CL_0)/(2π)` shifts the initial guess to land near the true root for any airfoil. On NACA 4412 fixed CL=0.5, the inviscid CL-target finder now returns CL≈0.516 (was 0.37). One extra inviscid solve per call but reliable convergence. Applied to both float and doubled facades.
- **Iter-89 fix unlocked Type 2 polar (SweepViscousCL) on cambered airfoils (iter 105 finding):** Cambered NACA 4412 SweepViscousCL Re=1e6 sweep CL=0.3..0.9 step 0.2 now converges on ALL 4 points (CLs match targets within 0.05). Pre-iter-89 none converged. Pinned by `DoubledTree_SweepViscousCL_CamberedConvergesAllPoints`. Type 2 polar is now functional on cambered airfoils.
- **SweepViscousRe (Type 3 polar with deeper warm-start corruption) still partial:** NACA 4412 fixed CL=0.5 sweep Re=5e5..3e6 — first inviscid CL hits target (0.516), but viscous Newton fails on early points and the panel-state corruption from failures gives NaN/sign-flip on subsequent points. Iter-94/96 partial fix (per-iteration re-assembly) helps some points but not all. Full fix needs panel-state lifecycle redesign — out of Phase 2 scope. Pinned by `DoubledTree_SweepViscousRe_ProducesPhysicalCDvsRe`.
- **Phase 3 cleanup (post-Phase-2):** The HessSmith inviscid path, PanelMeshGenerator, and the surrogate viscous pipeline (ViscousLaminarCorrector, ViscousStateEstimator, ViscousStateSeedBuilder, BoundaryLayerTopologyBuilder, WakeGeometryGenerator + 8 surrogate model types + 2 NotSupportedException-throwing diagnostic façade methods) were all DELETED. Only the linear-vortex + Newton-coupled viscous stack remains on the parity → doubled → modern progression path. NACA 4455/4455 = 100% bit-exact preserved; full 100k Selig DB = 100228 / 100228 = 100% bit-exact preserved.
- The `Plausible()` / `PhysicalEnvelope.IsAirfoilResultPhysical` envelope (|CL|≤5, CD∈[0,1]) catches "Converged: True" non-physical attractors (CD up to 1e105 observed at extreme α/Re). The float facade can produce these too — the envelope is checked at harness, test, and CLI level via `XFoil.Solver.Models.PhysicalEnvelope`.
- Validation gap: the doubled tree has no external accuracy reference (wind-tunnel data, refined-mesh study, higher-order CFD) in this repo. Agreement with the float tree measures *agreement*, not *accuracy*. Mesh-refinement convergence in `DoubledTree_ViscousMeshRefinement_CLConvergesTowardLimit` is the strongest in-repo accuracy proxy.

### IO and polar/session formats

- Implemented
  - Deterministic CSV and DAT export.
  - Legacy text polar import.
  - Legacy reference polar import.
  - Legacy polar-dump import and archive export.
  - Manifest-driven batch execution.
- Missing or weaker than legacy
  - The original interactive polar and session shell behavior is absent.
  - Binary dump support still needs broader historical validation.

### Geometry design (`xgdes`)

- Implemented
  - Main batch geometry transforms.
  - Flap, TE-gap, LE-radius, point-edit, corner-refinement, and contour-modification workflows.
- Missing or weaker than legacy
  - No cursor-driven interactive edit session.
  - Some workflows remain spline-approximated rather than exact ports of all legacy internals.

### Inverse design (`xqdes`, `xmdes`)

- Implemented
  - `Qspec` extraction and editing.
  - Symmetry, `AQ`, `CQ`, smoothing, and surrogate inverse execution.
  - Modal spectrum and perturbation workflows.
  - Direct managed `MAPGEN` port with TE-gap, TE-angle, and filtering support.
- Missing or weaker than legacy
  - `QDES EXEC` and `MDES EXEC` remain surrogates outside the direct conformal path.
  - Full interactive inverse-design shell state is absent.

### CLI, UI, and plotting

- Implemented
  - Rich batch CLI command set focused on the linear-vortex inviscid path and the Newton-coupled viscous solver. Surrogate-pipeline CLI commands and their helpers were deleted in Phase 3 cleanup.
- Missing or weaker than legacy
  - No managed replacement for original plotting stack.
  - No interactive shell or retained session state comparable to `xfoil.f`.
  - Several viscous CLI knobs are parsed but ignored because `CreateViscousSettings` does not wire them through.

### ORRS and advanced features

- Implemented
  - None visible in managed code.
- Missing
  - `orrs/` functionality and any managed replacement of stability-map workflows.

## Priority TODOs

- Fix the Newton fixed-point mismatch so the parity tests can tighten back toward the 0.001% target.
- Keep the repaired reduced-panel alpha-10 direct-seed late tail covered: the broad upper `laminar_seed_system.theta` and `laminar_seed_final.theta` scans are green again, and the proved owners were station-48 `z_upw -> row22` plus the post-transition turbulent inverse-target branch in MRCHUE, not the older station-30/31 carry folklore.
- Continue the alpha-0 rung from the inviscid/stagnation producer side. Do not spend more time patching `MRCHUE` seed/refinement consumers until station-2 `UINV` matches the reference dump.
- Use the now-green standalone `PSILIN` kernel plus the closed full-run `BIJ row 47` replay as the inviscid oracle set. Move the next micro-driver effort to the scalar viscous producer side, starting from the new earliest live boundary `VSREZ` (`iter=1, side=1, station=3, iv=2`) and the `BLVAR` / `CQ` / correlation family that feeds it.
- Keep the canonical phase-1 set green while phase-2 work lands. New parity fixes must rerun the affected phase-1 rigs through `run_micro_rig_matrix.py` instead of assuming the current green state will hold.
- Convert the phase-1 synthetic-only kernel batches (`DenseLinearSystem`, `CF`, `CQ`, and the synthetic portion of `PSILIN`) to captured real-vector corpora so the matrix can distinguish “arithmetically green” from “real-case green”.
- Promote the current `MISSING_RIG` backlog through `micro_rig_registry.json` rather than adding one-off debug scripts.
- Keep the alpha-0 paneling and streamfunction block suites on the focused `n0012_re1e6_a0_p12_n9_pangen` and `n0012_re1e6_a0_p12_n9_psilin` cases. Do not reopen the older `n0012_re1e6_a0_p12_n9_full` oracle for those block tests unless a fresh producer proof shows the focused cases are missing needed inputs.
- Treat spline arithmetic as closed only through the new standalone oracles: synthetic `SPLIND/SEVAL/DEVAL` plus classic-NACA `SEGSPL`. Any new spline claim must cite one of those raw-bit fixtures, not decimal trace text.
- Keep using the new `parity_report` output as a coarse locator only. Route it through `route_full_xfoil_disparity.py`, switch to the responsible focused rig immediately, and do not spend another inner-loop iteration inside a broad full XFoil rerun.
- Treat the live-compare context budget as the primary rig-speed lever. If the next exact producer does not fall out within about 5 minutes, stop the patch hunt and improve the rig before running more cases.
- Keep the reduced-case `BT2(K,2)` replay idea rejected unless a fresh trace proves matching inputs and a direct output-only TRDIF divergence.
- Wire the CLI to real viscous solver controls (`ViscousSolverMode`, max iterations, tolerance, wake options) instead of parsing legacy-only arguments.
- Validate binary polar-dump import against real historical artifacts, not only synthetic fixtures.
- Tighten `QDES EXEC` and `MDES EXEC` toward legacy behavior instead of normal-displacement surrogates.
- Decide whether to build a managed plotting abstraction or keep plotting explicitly out of scope.
