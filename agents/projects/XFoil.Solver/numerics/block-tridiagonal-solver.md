# BlockTridiagonalSolver

- File: `src/XFoil.Solver/Numerics/BlockTridiagonalSolver.cs`
- Role: solve the current `ViscousNewtonSystem` reduction used by the BL Newton loop.

## Public methods

- `Solve(system, vaccel, debugWriter)`

## Notes

- Despite the name, the current implementation solves three scalar tridiagonal systems extracted from `VA` and `VB`, while sparsifying small `VM` entries through the `vaccel` threshold.
- This is the linear solver used by `ViscousSolverEngine`.
- Debug logging writes the forward-sweep and solution values used by the diagnostic dump tests.

## TODO

- Revisit the implementation if the Newton system returns to a more explicitly block-coupled solve.
