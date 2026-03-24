# Transition-Seed Row11 Carry Micro Parity Tests

## Scope

- Test family: `PredictedEdgeVelocityMicroParityTests.Alpha0_P12_UpperStation4_Iteration7SystemRow11*`
- Registry parent: `transition-seed-row11-carry-packets`
- Shared code owner:
  - `BoundaryLayerSystemAssembler.ComputeKinematicParameters`
  - `BoundaryLayerSystemAssembler.ComputeFiniteDifferences`
  - `ViscousSolverEngine` transition-seed replay / carry path

## Family Model

- Upper station-4 / iteration-7 remain witness coordinates, not separate solver implementations.
- The phase-2 parent follows the same diversified `1000+` rule as the other active matrix families:
  - multiple airfoils
  - multiple Reynolds numbers
  - multiple alphas
- This family is intentionally not part of the promoted broad acceptance surface. It is the owner/debug loop for that surface.

## Current Owner Split

- `transition-seed-row11-carry-kinematic-station2`
- `transition-seed-row11-carry-blkin-inputs-station2`
- `transition-seed-row11-carry-secondary-station2`
- `transition-seed-row11-carry-cf-station2`
- `transition-seed-row11-carry-interval-inputs`

## Current Finding

- The active row11 carry branch is no longer treated as one sparse witness-only rig.
- Focused March 22 reruns proved the active managed path does replay a carried station-2 secondary packet now, but the stored packet is still wrong before the iteration-7 / station-4 `transition_seed_system` witness.
- The latest focused managed trace stores `seed_interval_accept secondaryCf = 0.00015172534` immediately before the witness, while the reference witness still consumes an older carried station-2 CF packet `0.00026485461`.

## Debug Rule

- Keep pushing this family through the smaller carried-packet owners first.
- Do not reopen the broad row11 `transition_seed_system` consumer until one of the upstream packet owners closes on diversified evidence.

## TODO

- Add dedicated persisted owner captures for the new row11 carry parent so the five subrigs grow on shared-code breadth instead of one full-trace witness.
- Determine whether the remaining wrong packet comes from storing the wrong accepted secondary snapshot or from an earlier gauss/seed solve that already corrupts the packet before carry.
