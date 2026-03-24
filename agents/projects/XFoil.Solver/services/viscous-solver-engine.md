# ViscousSolverEngine

- File: `src/XFoil.Solver/Services/ViscousSolverEngine.cs`
- Role: primary Newton-coupled viscous operating-point solver.

## Public methods

- `SolveViscous(geometry, settings, alphaRadians, debugWriter)`
- `SolveViscousFromInviscid(panel, inviscidState, inviscidResult, settings, alphaRadians, debugWriter)`

## What it does

- Builds the linear-vortex inviscid solution used as the viscous baseline.
- Initializes XFoil-style BL station mapping, compressibility terms, and DIJ coupling data.
- Runs the Newton loop:
  - `ViscousNewtonAssembler.BuildNewtonSystem`
  - `BlockTridiagonalSolver.Solve`
  - `ViscousNewtonUpdater.ApplyNewtonUpdate`
  - DIJ edge-velocity coupling and stagnation relocation
- Packages `ViscousAnalysisResult`, `DragDecomposition`, transition info, and convergence history.
- Applies `PostStallExtrapolator` when configured and the Newton solve fails.

## Notes

- This is the real viscous path used by `AirfoilAnalysisService.AnalyzeViscous`.
- It always uses the linear-vortex inviscid front end, regardless of `InviscidSolverType`.
- The planning files overstated completion here; the implementation exists, but the current reference tests still show a spurious solution rather than XFoil parity.
- The direct-seed station-15 live march has one more proved parity guard now. In the transition-seed refinement loop, `seed_probe` may update `ampl2` via `TransitionModel.ComputeTransitionPoint`, but the immediate `TRDIF` / `AssembleStationSystem` replay must still consume the pre-probe downstream amplification packet for that same local interval. Feeding `DownstreamAmplification` straight back into the interval assembly changed the accepted transition-point iterate by a few ULPs and created a false live-vs-replay split even when the focused interval replay was otherwise correct.
- The direct-remarch side of the same station-15 chain now has its own proved storage rule too. After each accepted MRCHDU local update, the live remarch loop must round `uei/theta/dstar/(ampl|ctau)` back through `ApplyLegacySeedPrecision(...)` before the next `BLSYS` / transition-probe iteration reuses them. Keeping those values wide was enough to leave the new `direct-seed-station15-live-handoff` rig red at `theta expected=0x38E67A00 actual=0x38E67A01` even while the isolated transition-window oracle stayed green.
- Fresh alpha-0 focused traces on `NACA 0012, Re=1e6, alpha=0, panels=60` proved that the visible `laminar_seed_final` and `laminar_seed_system` mismatches are downstream consumers. The first station-2 seed solve already receives the wrong `uei/theta/dstar`, and `uei` matches an already-diverged `UINV` baseline in the corresponding dump row.
- The alpha-10 panel-80 `station=2` similarity local chain now has dedicated raw-bit micro-rigs for both `laminar_seed_system` and `laminar_seed_step`. Those rigs are green and proved the last local seed bug was not a wrong formula but a parity-only unary-negation replay issue affecting signed zero.
- The accepted station-2 similarity update and the first downstream laminar consumers are now locally covered too: one micro-rig replays the station-2 accepted-state/handoff chain against raw words, and another invokes the real `RefineSimilarityStationSeed` plus `RefineLaminarSeedStation` helpers on a tiny BL state and matches the station-3/4/5 final traces bitwise.
- A March 23 full upper-side alpha-10 direct-seed replay proved the earlier station-14/15 frontier was only a consumer symptom. The first real drift was already at station 3 final state, and the focused station-3 iteration-2 trace showed `residual2` / `residual3` plus the local Jacobian rows diverging even though `uei/theta/dstar/ampl` still matched on entry.
- That root cause was the current-station secondary replay inside `RefineLegacySeedStation(...)`: classic `MRCHUE` must carry station-1 COM1 forward, but it must rebuild the current station-2 BLVAR packet from the live primary/BLKIN state each iteration. Nulling the current-station `station2SecondaryOverride` there repairs the station-3/4/5 laminar method rig and the focused upper station-13/14/15/16 direct-seed witnesses without reopening the older station-15 transition-point fixes.
- The former late upper direct-seed theta residual is now closed. After the caller-side station-3 fix, the next broad witness migrated to upper station `48`, not the older station-30/31 folklore. Focused station-48 traces split that tail into two real owners:
- `BoundaryLayerSystemAssembler.ComputeFiniteDifferences(...)` had to replay turbulent `z_upw` with the native float-expression chain seeded by the `SA` term, which closed the local `row22` / `laminar_seed_system.row12` drift at station `48`, iteration `5`.
- `RefineLegacySeedStation(...)` also had one more post-transition inverse-target rule to preserve. Once `usesShearState` is true and `transitionInterval` is false, classic MRCHUE no longer uses the laminar `+0.03*(x2-x1)/theta1` inverse target. It falls back to the turbulent `-0.15*(x2-x1)/theta1` branch. Replaying that split restores the station-48 `laminar_seed_inverse_target` / `htarg` chain and makes both broad upper `laminar_seed_system.theta` and `laminar_seed_final.theta` scans green again.
- The alpha-10 panel-80 lower wake / `UESET` tail is now closed on the real owner packets too. The first post-station-35 bug was not another TE merge issue but the downstream wake carry path: later wake stations must seed from the previously accepted wake state, and accepted wake updates must refresh BLKIN before the next station assembles `BLSYS`. After that carry repair, the remaining late-tail owners were the accepted lower-wake snapshots at stations `36`, `38`, `43`, `44`, and `46`; those stations now have focused final-packet replays on the exact accepted `uei/theta/dstar` words from the authoritative dump. Those replays closed the old upper station-2 `term[89]` / `term[91] mass` drifts without reopening the earlier first-wake TE merge repair.
- The alpha-0 lower-tail predicted-edge lineage now has two explicit producer splits. `PredictedEdgeVelocityMicroParityTests` still exposes the shared pre-constraint remarch `BLDIF` packet family (`legacy-remarch-preconstraint-primary-station2`, `...-eq3-theta`, `...-eq3-d1`, `...-eq3-d2`, `...-eq3-u2`, `...-residual`) plus a separate station-9 witness ladder for the `RemarchBoundaryLayerLegacyDirect` trajectory, but an even earlier shared family now exists above it: `legacy-direct-seed-carry-packets`. That parent harvests direct `laminar_seed_system`, `laminar_seed_step`, and `laminar_seed_final` packets across the diversified sweep space while using lower station-8 / station-9 only as witness locations. The current earliest red is therefore no longer the remarch `bldif_primary_station station=2` packet; it is the direct-seed carry `laminar_seed_system` witness at lower station 8, where the managed path already assembles a wrong eq3/displacement residual before remarch begins.
- That direct-seed carry parent is now split one level deeper too. The current narrower owner rows are `legacy-direct-seed-carry-primary-station2`, `legacy-direct-seed-carry-secondary-station2`, `legacy-direct-seed-carry-eq3-d2`, and `legacy-direct-seed-carry-residual`, each backed by diversified 1000-vector trace recipes. The current lower station-8 witness order is small but clear: the managed path drifts first in the current-station `bldif_primary_station station=2` packet, then in the current-station secondary packet, then in eq3 `d2` assembly, and only afterwards in the broad `laminar_seed_system` / `step` / `final` packets.

## TODO

- Fix the remaining Newton fixed-point mismatch before tightening the parity claims anywhere else in the docs.
- Keep the alpha-0 parity chase upstream on the inviscid/stagnation producer path until station-2 `UINV` matches. Do not apply local seed/refinement patches there without fresh input-equality proof.
- Keep the post-transition inverse-target split explicit in future parity work: once `usesShearState` is true, `RefineLegacySeedStation(...)` must use the turbulent MRCHUE inverse Hk target even when `transitionInterval` is already false.
