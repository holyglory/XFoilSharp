# TridiagonalSolver

- File: `src/XFoil.Solver/Numerics/TridiagonalSolver.cs`
- Role: Static utility class implementing the Thomas algorithm for tridiagonal systems. Direct port of XFoil's TRISOL from spline.f.

## Public methods

- `Solve(lower, diagonal, upper, rhs, count)` -- in-place Thomas algorithm; rhs is replaced with solution; diagonal and upper are destroyed.

## Parity

- Direct port of XFoil's TRISOL with 0-based indexing.
- Used internally by `ParametricSpline` for spline coefficient computation.

## TODO

- None.
