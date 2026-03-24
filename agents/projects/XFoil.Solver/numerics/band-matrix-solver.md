# BandMatrixSolver

- File: `src/XFoil.Solver/Numerics/BandMatrixSolver.cs`
- Role: alternate solve path for the same Newton-system reduction handled by `BlockTridiagonalSolver`.

## Public methods

- `Solve(system)`

## Notes

- The current implementation does not build a general banded LU solve; it solves the same per-equation tridiagonal reduction that `BlockTridiagonalSolver` uses.
- The main value of this file today is equivalence testing and experimentation, not a distinct production solve path.

## TODO

- Either turn this into a real banded solve or keep documenting it as an alternate formulation of the current reduced system.
