# ScaledPivotLuSolver

- File: `src/XFoil.Solver/Numerics/ScaledPivotLuSolver.cs`
- Role: Static utility class providing LU decomposition with Crout's method and scaled partial pivoting. Direct port of XFoil's LUDCMP and BAKSUB from xsolve.f.

## Public methods

- `Decompose(matrix, pivotIndices, size)` -- in-place LU decomposition with scaled partial pivoting. Each row is normalized by its maximum absolute element before pivot comparison.
- `BackSubstitute(luMatrix, pivotIndices, rhs, size)` -- forward/backward substitution with pivot unscrambling. Multiple RHS vectors can be solved against the same LU factoring.

## Key difference from DenseLinearSystemSolver

The existing `DenseLinearSystemSolver` uses unscaled partial pivoting (Gaussian elimination with row swaps based on raw absolute value). This solver uses *scaled* pivoting where each candidate pivot is multiplied by `1/max(|row elements|)` before comparison. This prevents rows with large absolute values from dominating pivot selection and produces different roundoff propagation. At the 0.001% parity tolerance, this distinction matters.

## Parity

- Direct port of XFoil's LUDCMP/BAKSUB with 0-based indexing.
- Pivot selection uses `>=` comparison (matching Fortran behavior of picking the last row with maximum scaled value).
- The `II` optimization in BAKSUB (skip leading zeros) is preserved.

## TODO

- None.
