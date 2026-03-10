# ViscousStateEstimator

- File: `src-cs/XFoil.Solver/Services/ViscousStateEstimator.cs`
- Role: convert a seed into first-pass viscous station/branch state.

## Public methods

- `Estimate(seed, settings)`

## Important helpers

- `EstimateSurfaceBranch`
- `EstimateWakeBranch`

## Parity

- Uses explicit heuristics and regime switching.
- Not a direct legacy BL initialization port.

## TODO

- Replace heuristic estimates with a closer legacy-compatible initialization path.
