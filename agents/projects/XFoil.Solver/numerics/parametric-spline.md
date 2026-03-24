# ParametricSpline

- File: `src/XFoil.Solver/Numerics/ParametricSpline.cs`
- Role: Static utility class providing parametric cubic spline operations for the linear-vorticity geometry and solver pipeline. Direct port of XFoil's spline.f routines (SPLINE, SPLIND, SEGSPL, SEVAL, DEVAL, SINVRT, SCALC).

## Public methods

- `FitWithZeroSecondDerivativeBCs(values, derivatives, parameters, count)` -- natural spline (SPLINE)
- `FitWithBoundaryConditions(values, derivatives, parameters, count, startBc, endBc)` -- configurable BCs (SPLIND)
- `FitSegmented(values, derivatives, parameters, count)` -- segment-aware spline (SEGSPL)
- `Evaluate(s, values, derivatives, parameters, count)` -- cubic interpolation (SEVAL)
- `EvaluateDerivative(s, values, derivatives, parameters, count)` -- derivative evaluation (DEVAL)
- `ComputeArcLength(x, y, arcLength, count)` -- cumulative arc length (SCALC)
- `InvertSpline(targetValue, values, derivatives, parameters, count, initialGuess)` -- Newton inversion (SINVRT)

## Supporting types

- `SplineBoundaryCondition` -- readonly struct with three factory modes: `ZeroSecondDerivative`, `ZeroThirdDerivative`, `SpecifiedDerivative(value)`

## Relationship to existing CubicSpline

This is a separate parallel implementation. The existing `CubicSpline.cs` supports only natural (zero second derivative) end conditions and uses an instance-based API. `ParametricSpline` adds zero-third-derivative and specified-derivative BCs required by the linear-vorticity formulation, plus segmented spline support for corners. It uses a static API with raw arrays matching XFoil's calling convention.

## Parity

- Direct port of XFoil's spline.f with 0-based indexing.
- All three BC modes verified against Fortran algorithm structure.
- Segmented spline uses duplicate arc-length values to detect segment boundaries (same as Fortran SEGSPL).
- `tests/XFoil.Core.Tests/FortranParity/ParametricSplineFortranParityTests.cs` now exercises a dedicated standalone `spline.f` batch driver and compares raw IEEE-754 `float` words, not decimal text.
- `tests/XFoil.Core.Tests/FortranParity/ParametricSplineFortranParityTests.cs` now also exercises a dedicated standalone `SEGSPL` batch driver on classic `NACA 0012`, `2412`, and `4415` contours, comparing raw IEEE-754 `float` words for both `x(s)` and `y(s)` curves.
- The NACA-shaped segmented spline batch is no longer sparse:
  - each curve now includes all node-derivative checks plus at least `400` evaluation parameters,
  - the current test rig generates `257` global uniform samples and extra per-interval knot, near-knot, and interior probes per contour,
  - and the standalone Fortran segmented driver capacity was raised to `MAXM=1024` so dense contour batches stay inside the micro-driver instead of falling back to whole-solver traces.
- The current parity-sensitive float replay proved by that driver is mixed, not purely source-order:
  - `CX1` and `CX2` match the contracted `DS*XS - XHIGH` path before the final `+ XLOW`.
  - `SEVAL` `CUBFAC` matches the contracted `T - T*T` value.
  - `DEVAL` `FAC1` matches the contracted `3*T*T + (1 - 4*T)` value.
  - `DEVAL` `FAC2` still matches the source-ordered `T*(3*T-2)` replay.
  - Final `SEVAL` and `DEVAL` accumulation stays unfused and left-associated in the parity path.
- The current standalone spline batch matches bitwise across the explicit edge/two-point fixtures, the randomized SPLIND case set, and the denser classic NACA SEGSPL contour set.

## TODO

- If new spline mismatches appear, treat the standalone raw-hex driver as the oracle before touching any whole-solver trace.
- If the next geometry-side mismatch touches paneling or contour interpolation, start from the dedicated `SEGSPL` NACA oracle before reopening broader `PANGEN` or solver traces.
- Reuse this dedicated-driver pattern for the next parity micro-kernel instead of reopening broad `PANGEN` tracing first.
