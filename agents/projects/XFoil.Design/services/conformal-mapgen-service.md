# ConformalMapgenService

- File: `src/XFoil.Design/Services/ConformalMapgenService.cs`
- Role: direct managed conformal inverse path.

## Public methods

- `Execute(geometry, targetProfile, ...)`
- `Execute(geometry, baselineProfile, targetProfile, ...)`

## Important helpers

- `SolveForTrailingEdgeAngleTarget`
- `SolveForProfileTarget`
- `ExecuteWithContinuation`
- `ExecuteCore`
- `BuildCnFromQSpec`
- `FourierTransform`
- `PiqSum`
- `ZcCalc`
- `ZcNorm`
- `FindLeadingEdge`
- `ApplyHanningFilter`

## Parity

- Closest managed equivalent to direct `MAPGEN` in `xmdes.f`.
- Supports TE gap, TE angle, filtering, and continuation.

## TODO

- Expand parity/regression coverage further across original airfoil families and inverse edits.
