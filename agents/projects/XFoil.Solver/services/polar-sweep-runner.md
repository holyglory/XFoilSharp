# PolarSweepRunner

- File: `src/XFoil.Solver/Services/PolarSweepRunner.cs`
- Role: run viscous Type 1, Type 2, and Type 3 polar sweeps on top of the Newton-coupled solver.

## Public methods

- `SweepAlpha`
- `SweepCL`
- `SweepRe`

## Notes

- Reuses panel geometry and the factored linear-vortex inviscid system across operating points.
- Warm-start support is currently lightweight: the runner tracks nearby converged alpha history, not a full reusable BL-state snapshot.
- Non-converged points are still returned and marked with `Converged = false` so the sweep can continue.

## TODO

- Upgrade the warm-start path if the solver starts carrying reusable BL state between points.
