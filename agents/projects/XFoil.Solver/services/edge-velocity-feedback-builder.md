# EdgeVelocityFeedbackBuilder

- File: `src/XFoil.Solver/Services/EdgeVelocityFeedbackBuilder.cs`
- Role: update seed `Ue` using solved displacement trends.

## Public methods

- `ApplyDisplacementFeedback(...)`
- `ComputeAverageRelativeEdgeVelocityChange(...)`

## Important helpers

- `ApplyBranchFeedback`
- `EstimateDisplacementGradient`
- `ComputeGradient`
- `MapIndex`
- `PairStations`

## TODO

- Replace this surrogate feedback with direct coupled outer-flow feedback when available.
