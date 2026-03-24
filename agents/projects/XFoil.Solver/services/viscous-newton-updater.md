# ViscousNewtonUpdater

- File: `src/XFoil.Solver/Services/ViscousNewtonUpdater.cs`
- Role: apply the solved Newton step to BL variables with either XFoil-style relaxation or trust-region limiting.

## Public methods

- `ApplyNewtonUpdate(...)`

## Notes

- Supports two update modes through `AnalysisSettings.ViscousSolverMode`.
- Computes edge-velocity corrections from the DIJ matrix when that data is available.
- Owns the relaxed-step logic, acceptance checks, and rollback behavior for trust-region mode.

## TODO

- Revisit the limiter strategy once the coupled solve starts landing on the same fixed point as Fortran.
