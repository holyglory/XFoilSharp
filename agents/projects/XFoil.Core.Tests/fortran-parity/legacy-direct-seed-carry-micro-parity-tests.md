# Legacy Direct-Seed Carry Micro Parity Tests

## Scope

- Test family: `PredictedEdgeVelocityMicroParityTests.Alpha0_P12_LegacyDirectSeedCarry_*`
- Registry parent: `legacy-direct-seed-carry-packets`
- Shared code owner:
  - `ViscousSolverEngine.MarchLegacyBoundaryLayerDirectSeed`
  - `BoundaryLayerSystemAssembler.AssembleStationSystem`

## Family Model

- Lower station-8 / station-9 are only witness locations.
- The rigs are shared-code families, not station-specific implementations.
- Every 1000-vector trace rig in this family uses the diversified sweep rule:
  - multiple airfoils
  - multiple Reynolds numbers
  - multiple alphas

## Current Owner Split

The direct-seed carry parent is now split into smaller owner rigs:

- `legacy-direct-seed-carry-system`
- `legacy-direct-seed-carry-inputs`
- `legacy-direct-seed-carry-primary-station2`
- `legacy-direct-seed-carry-secondary-station2`
- `legacy-direct-seed-carry-eq3-d2`
- `legacy-direct-seed-carry-residual`
- `legacy-direct-seed-carry-step`
- `legacy-direct-seed-carry-final`

Quick matrix rerun: `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-legacy-direct-seed-carry-quick8/latest.md`

- `legacy-direct-seed-carry-system`: red at `221/1000` (`96` owner + `125` borrowed)
- `legacy-direct-seed-carry-inputs`: red at `30/1000` (`10` owner + `20` borrowed)
- `legacy-direct-seed-carry-primary-station2`: red at `338/1000` (`285` owner + `53` borrowed)
- `legacy-direct-seed-carry-secondary-station2`: red at `338/1000` (`285` owner + `53` borrowed)
- `legacy-direct-seed-carry-eq3-d2`: red at `337/1000` (`285` owner + `52` borrowed)
- `legacy-direct-seed-carry-residual`: red at `30/1000` (`10` owner + `20` borrowed)
- `legacy-direct-seed-carry-step`: red at `131/1000` (`96` owner + `35` borrowed)
- `legacy-direct-seed-carry-final`: red at `54/1000` (`15` owner + `39` borrowed)

## Current Witness Order

The lower station-8 witness now shows the producer order directly:

1. `bldif_primary_station station=2`
2. `bldif_secondary_station station=2`
3. `bldif_eq3_d2_terms`
4. `bldif_residual`
5. `laminar_seed_system`
6. downstream `step/final` carry drift into lower station-9

This is the useful narrowing: the family is no longer debugged through the broader remarch witness first.

## Known Gaps

- The `blsys_interval_inputs` and `bldif_residual` owner rigs are no longer empty placeholders. Two diversified owner probes are now promoted into persisted owner corpora, so both rows sit at `30/1000` with `10` owner vectors and `20` borrowed vectors.
- Quick phase-2 runs now count explicitly configured focused corpora for this family, but they still leave the owner-gap visible instead of collapsing borrowed evidence into a fake green state.
- The focused managed build-context witness still does not naturally emit a broader diversified `blsys_interval_inputs` family before the lower station-8 `laminar_seed_system` witness, so those packets still need dedicated owner harvests rather than more witness-only borrowing.
- A third diversified owner probe (`2312 / Re=7.5e5 / alpha=2 / 25-panel`) widened the broad `laminar_seed_system` borrowed frontier, but it did not increase the unique `inputs` or `residual` sets. That means the next owner-growth step for those two rows must prefer materially different interval-state shapes, not merely more packet-bearing cases.

## TODO

- Grow the `blsys_interval_inputs` and `bldif_residual` owner corpora with shape-diverse cases until they are real Phase-1-grade families instead of small owner-backed footholds.
- Keep debugging on the smaller owner rigs, not on the broad `laminar_seed_*` packets, until the first producer packet is green.
- After the first producer closes, rerun the whole `legacy-direct-seed-carry-packets` parent and then the downstream remarch witness lane.
