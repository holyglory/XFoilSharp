# ContourModificationService

- File: `src/XFoil.Design/Services/ContourModificationService.cs`
- Role: graft a replacement contour segment from control points.

## Public methods

- `ModifyContour(...)`

## Important helpers

- `FindClosestPointIndex`
- `BuildArcLengths`
- `RescaleParameters`
- `DistanceSquared`

## Parity

- Headless equivalent of `MODI`/`SLOP`.

## TODO

- Continue narrowing differences from legacy interactive contour graft behavior.
