# WakeGeometryGenerator

- File: `src/XFoil.Solver/Services/WakeGeometryGenerator.cs`
- Role: postprocess wake geometry from an inviscid result.

## Public methods

- `Generate(...)`

## Important helpers

- `EvaluateWakeState`
- `NormalizeFallback`
- `ComputePointVelocityInfluence`
- `BuildStretchedDistances`
- `SolveGeometricRatio`

## Parity

- More physical than a fixed tangent extrapolation.
- Still not full original wake coupling.

## TODO

- Document the remaining differences from `XYWAKE` and wake/source coupling in original XFoil.
