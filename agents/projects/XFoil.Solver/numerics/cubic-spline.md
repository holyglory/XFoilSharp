# CubicSpline

- File: `src/XFoil.Solver/Numerics/CubicSpline.cs`
- Role: natural cubic spline interpolation utility for solver-side numeric work.

## Public methods

- `CubicSpline(parameters, values)`
- `Evaluate(parameter)`

## Important helpers

- `FindUpperIndex`
- `ComputeNaturalSlopes`
- `SolveTriDiagonal`

## TODO

- Compare this implementation explicitly against any legacy spline use that affects solver fidelity.
