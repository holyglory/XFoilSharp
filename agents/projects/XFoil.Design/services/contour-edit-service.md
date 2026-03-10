# ContourEditService

- File: `src-cs/XFoil.Design/Services/ContourEditService.cs`
- Role: point-level contour editing and corner refinement.

## Public methods

- `AddPoint`
- `MovePoint`
- `DeletePoint`
- `DoublePoint`
- `RefineCorners`

## Important helpers

- `BuildResult`
- `BuildSplineParameters`
- `ComputeMaximumCornerAngle`
- `ComputeCornerAngleDegrees`
- `EvaluateSplinePoint`

## Parity

- Covers `ADDP`, `MOVP`, `DELP`, `CORN`, `CADD`.

## TODO

- Missing legacy cursor-driven editing ergonomics remain outside this service.
