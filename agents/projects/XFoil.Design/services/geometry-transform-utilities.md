# GeometryTransformUtilities

- File: `src/XFoil.Design/Services/GeometryTransformUtilities.cs`
- Role: shared geometry helper layer for design workflows.

## Important methods

- `FindLeadingEdgeIndex`
- `BuildChordFrame`
- `ToChordFrame`
- `FromChordFrame`
- `EstimateLeadingEdgePoint`
- `EstimateLeadingEdgeRadius`
- `FindOppositePointAtSameChordX`
- `InterpolateY`

## Parity

- Carries much of the managed `xgeom`/`SOPPS`/`LEFIND` equivalent behavior.

## TODO

- Keep centralizing shared geometry logic here instead of duplicating it in service classes.
