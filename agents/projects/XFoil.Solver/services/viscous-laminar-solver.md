# ViscousLaminarSolver

- File: `src/XFoil.Solver/Services/ViscousLaminarSolver.cs`
- Role: current local Newton-style viscous solve.

## Public methods

- `Solve(...)`

## Important helpers

- `SolveSurfaceBranch`
- `SolveIntervalEndState`
- `CreateUpdatedStation`
- `SolveWakeBranch`
- `SolveWakeIntervalEndTheta`
- `ComputeSurfaceResidual`
- `ComputeTransitionResidual`
- `ComputeWakeResidual`
- `ComputeSecondaryResidual`

## Parity

- Main managed viscous solver today.
- Still far from original full BL/Newton system.

## TODO

- Replace local updates with a more faithful coupled global solve.
