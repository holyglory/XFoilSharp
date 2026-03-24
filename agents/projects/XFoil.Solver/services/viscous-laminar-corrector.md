# ViscousLaminarCorrector

- File: `src/XFoil.Solver/Services/ViscousLaminarCorrector.cs`
- Role: retained correction helper from the older staged viscous pipeline.

## Public methods

- `Correct(...)`

## Important helpers

- `CorrectSurfaceBranch`
- `RebuildWakeBranch`
- `ComputeNormalizedAmplificationResidual`

## Notes

- This class is no longer part of the primary viscous operating-point path.
- The current `AnalyzeViscous` flow goes through `ViscousSolverEngine` instead.

## TODO

- Decide whether this helper still earns its keep as a diagnostic tool.
