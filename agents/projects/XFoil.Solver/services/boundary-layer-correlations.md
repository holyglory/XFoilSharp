# BoundaryLayerCorrelations

- File: `src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs`
- Role: value-and-Jacobian correlation library for the Newton-coupled boundary-layer solver.

## Public method families

- Shape parameters
  - `KinematicShapeParameter`
  - `LaminarShapeParameter`
  - `TurbulentShapeParameter`
  - `DensityThicknessShapeParameter`
- Skin friction and dissipation
  - `LaminarSkinFriction`
  - `TurbulentSkinFriction`
  - `LaminarDissipation`
  - `WakeDissipation`
  - `TurbulentDissipation`
- Closure terms
  - `EquilibriumShearCoefficient`

## Notes

- Each method returns both the value and the analytic sensitivities needed by `BoundaryLayerSystemAssembler` and `ViscousNewtonAssembler`.
- This file is a major `xblsys.f` port, but it is not by itself evidence of full viscous parity; the current fixed-point mismatch sits in the full coupled solve.
- The fresh alpha-10 `laminar_dissipation` zero-output branch turned out to be an instrumented-reference bug, not a managed `LaminarDissipation` bug: the traced Fortran `DIL` path had introduced an undeclared `NUMER` temp, so implicit typing truncated the numerator to integer zero while leaving `DI_HK` on the original expression.
- The standalone `CQ/CF/DI` raw-bit rigs now run 1024 randomized vectors per batch. That widened sweep exposed a real upstream bug in `TurbulentSkinFriction`: the legacy `CFT` `FCARG` replay for `1.0 + 0.5*GM1*MSQ` must stay on the contracted native float path before the square root, not on a separately rounded helper chain.
- The next layer up now has a trace-backed oracle as well: the active `blvar-turbulent-di-upstream` rig counts emitted `blvar_turbulent_di_terms` packets from the existing alpha-10 and alpha-0 reference families, so future BLVAR DI questions no longer need to start from the standalone scalar drivers alone.

## TODO

- Keep these value-and-derivative signatures aligned with any future `BLVAR` and `BLDIF` debugging.
- Keep future turbulent `CF/DI` parity changes anchored to the standalone raw-bit rigs before reopening broad viscous traces.
