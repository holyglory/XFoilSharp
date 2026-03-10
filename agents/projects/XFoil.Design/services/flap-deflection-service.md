# FlapDeflectionService

- File: `src-cs/XFoil.Design/Services/FlapDeflectionService.cs`
- Role: trailing-edge flap edit around a hinge point.

## Public methods

- `DeflectTrailingEdge(...)`

## Important helpers

- `EditSurface`
- `SolveSurfaceBreaks`
- `SolvePerpendicularBreak`
- `ComputeClosingBreakResiduals`
- `RotateAroundHinge`
- `IsInside`
- `RemoveMicroSegments`

## Parity

- Closest managed equivalent to legacy `FLAP`.
- Uses spline-backed break solving and local cleanup.

## TODO

- Keep comparing against original `xgdes` flap behavior on harder hinge locations.
