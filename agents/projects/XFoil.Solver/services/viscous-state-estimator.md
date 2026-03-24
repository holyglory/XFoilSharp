# ViscousStateEstimator

- File: `src/XFoil.Solver/Services/ViscousStateEstimator.cs`
- Role: convert a seed into first-pass viscous station and branch state for diagnostic inspection flows.

## Public methods

- `Estimate(seed, settings)`

## Important helpers

- `EstimateSurfaceBranch`
- `EstimateWakeBranch`

## Parity

- Uses explicit heuristics and regime switching.
- Not a direct legacy BL initialization port.
- Not used by `AnalyzeViscous`; the primary operating-point viscous path initializes inside `ViscousSolverEngine`.

## TODO

- Keep this aligned with the `viscous-init-*` diagnostic commands or remove it if those commands are retired.
