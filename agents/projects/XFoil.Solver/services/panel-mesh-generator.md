# PanelMeshGenerator

- File: `src-cs/XFoil.Solver/Services/PanelMeshGenerator.cs`
- Role: generate a normalized, weighted panel mesh from geometry.

## Public methods

- `Generate(geometry, panelCount)`
- `Generate(geometry, panelCount, panelingOptions)`

## Important helpers

- `BuildWeightedDistribution`
- `EstimateCurvature`
- `Gaussian`
- `InterpolateParameter`
- `BuildArcLengthParameters`
- `ComputeSignedArea`

## Parity

- Approximates legacy panel bunching behavior.
- Not a direct `PANGEN` port.

## TODO

- Add more node-distribution parity tests against original XFoil.
