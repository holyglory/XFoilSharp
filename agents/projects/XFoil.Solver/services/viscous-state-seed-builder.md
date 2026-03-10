# ViscousStateSeedBuilder

- File: `src/XFoil.Solver/Services/ViscousStateSeedBuilder.cs`
- Role: create branchwise seed arrays for viscous work.

## Public methods

- `Build(analysis, topology)`

## Important helpers

- `BuildSurfaceSeed`
- `BuildWakeSeed`
- `ComputeTrailingEdgeGeometry`
- `ComputeWakeGapDerivative`

## TODO

- Document which seed fields are intended to mirror original XFoil state and which are purely managed convenience.
