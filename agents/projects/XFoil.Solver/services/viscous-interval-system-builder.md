# ViscousIntervalSystemBuilder

- File: `src-cs/XFoil.Solver/Services/ViscousIntervalSystemBuilder.cs`
- Role: build interval residual and derived-state views from viscous station state.

## Public methods

- `Build(state, settings)`
- `BuildInterval(...)`

## Important helpers

- `BuildBranchIntervals`
- `BuildDerivedState`
- `ComputeUpwindWeight`
- `DetermineIntervalKind`

## TODO

- Keep narrowing the gap between these interval residuals and the original coupled BL system.
