# HessSmithInviscidSolver

- File: `src/XFoil.Solver/Services/HessSmithInviscidSolver.cs`
- Role: current managed inviscid core.

## Public methods

- `Prepare(mesh)`
- `Analyze(preparedSystem, angleOfAttackDegrees, machNumber = 0d)`
- `Analyze(mesh, angleOfAttackDegrees, machNumber = 0d)`

## Important helpers

- `BuildUnitFreestreamRightHandSide`
- `CombineBasisVectors`
- `ComputeInfluence`
- `IntegratePressureForces`
- `ComputeIncompressiblePressureCoefficient`
- `ApplyCompressibilityCorrection`

## Parity

- Reuses basis solutions across alpha.
- Still not a direct port of original XFoil inviscid internals.

## TODO

- Bring force recovery and influence logic closer to original `xpanel.f`/`xoper.f`.
