# ViscousForceEstimator

- File: `src/XFoil.Solver/Services/ViscousForceEstimator.cs`
- Role: estimate profile drag from solved viscous state.

## Public methods

- `EstimateProfileDragCoefficient(state)`

## Important helpers

- `EstimateSurfaceBranchDrag`

## Parity

- Useful current estimate.
- Not full legacy drag decomposition.

## TODO

- Add pressure-drag and wake-extrapolation parity if solver fidelity improves.
