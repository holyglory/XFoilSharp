# ParametricSpline

- File: `src/XFoil.Solver/Numerics/ParametricSpline.cs`
- Role: Static utility class providing parametric cubic spline operations for the linear-vorticity geometry and solver pipeline. Direct port of XFoil's spline.f routines (SPLINE, SPLIND, SEGSPL, SEVAL, DEVAL, SINVRT, SCALC).

## Public methods

- `FitWithZeroSecondDerivativeBCs(values, derivatives, parameters, count)` -- natural spline (SPLINE)
- `FitWithBoundaryConditions(values, derivatives, parameters, count, startBc, endBc)` -- configurable BCs (SPLIND)
- `FitSegmented(values, derivatives, parameters, count)` -- segment-aware spline (SEGSPL)
- `Evaluate(s, values, derivatives, parameters, count)` -- cubic interpolation (SEVAL)
- `EvaluateDerivative(s, values, derivatives, parameters, count)` -- derivative evaluation (DEVAL)
- `ComputeArcLength(x, y, arcLength, count)` -- cumulative arc length (SCALC)
- `InvertSpline(targetValue, values, derivatives, parameters, count, initialGuess)` -- Newton inversion (SINVRT)

## Supporting types

- `SplineBoundaryCondition` -- readonly struct with three factory modes: `ZeroSecondDerivative`, `ZeroThirdDerivative`, `SpecifiedDerivative(value)`

## Relationship to existing CubicSpline

This is a separate parallel implementation. The existing `CubicSpline.cs` supports only natural (zero second derivative) end conditions and uses an instance-based API. `ParametricSpline` adds zero-third-derivative and specified-derivative BCs required by the linear-vorticity formulation, plus segmented spline support for corners. It uses a static API with raw arrays matching XFoil's calling convention.

## Parity

- Direct port of XFoil's spline.f with 0-based indexing.
- All three BC modes verified against Fortran algorithm structure.
- Segmented spline uses duplicate arc-length values to detect segment boundaries (same as Fortran SEGSPL).

## TODO

- Verify intermediate spline derivative values against Fortran binary output for specific test airfoils.
