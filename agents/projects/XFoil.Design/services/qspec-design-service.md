# QSpecDesignService

- File: `src-cs/XFoil.Design/Services/QSpecDesignService.cs`
- Role: main managed `QDES` surface.

## Public methods

- `CreateFromInviscidAnalysis`
- `Modify`
- `Smooth`
- `ForceSymmetry`
- `ExecuteInverse`

## Important helpers

- `ApplySlopeConstraint`
- `SolveTriDiagonal`
- `RebuildProfile`
- `FindClosestIndex`
- `EstimateDerivative`
- `BuildSampleDistances`
- `SmoothDisplacements`
- `ComputeCentroid`
- `ComputeOutwardNormal`

## Parity

- Covers practical `Qspec` extraction and editing.
- `ExecuteInverse` remains a surrogate rather than full legacy inverse execution.

## TODO

- Tighten `ExecuteInverse` toward original `QDES EXEC`.
