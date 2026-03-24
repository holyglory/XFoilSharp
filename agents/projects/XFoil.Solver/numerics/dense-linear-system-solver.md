# DenseLinearSystemSolver

- File: `src/XFoil.Solver/Numerics/DenseLinearSystemSolver.cs`
- Role: legacy-aware dense matrix solver used by parity-sensitive panel and seed systems.

## Public methods

- `Solve(matrix, rightHandSide)`

## Important helpers

- `SwapRows`
- `SolveCore`

## Parity

- The parity path is now backed by a standalone raw-bit `GAUSS` oracle instead of whole-solver trace inference.
- `tools/fortran-debug/gauss_parity_driver.f90` links the real legacy `GAUSS` routine from `xsolve_debug.f` and emits phase-by-phase snapshots for `initial`, `normalized`, `eliminate`, `last`, and `backsub`.
- `tests/XFoil.Core.Tests/FortranParity/DenseLinearSystemFortranParityTests.cs` compares those snapshots and the final solution against the managed single-precision branch using raw IEEE-754 words.
- That micro-driver proved the forward-elimination and back-substitution updates must use `LegacyPrecisionMath.SeparateMultiplySubtract(...)`, not `FusedMultiplySubtract(...)`, in order to replay the classic XFoil REAL rounding sequence.
- The current parity batch now runs 1024 randomized 4x4 systems plus explicit fixtures, so new dense-solver edits should fail fast before they can hide inside broader seed or Newton traces.

## TODO

- Keep new dense-solver parity changes anchored to the standalone `GAUSS` driver, not downstream solver dumps.
- Document expected matrix sizes and conditioning assumptions.
