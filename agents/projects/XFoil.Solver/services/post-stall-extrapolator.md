# PostStallExtrapolator

- File: `src/XFoil.Solver/Services/PostStallExtrapolator.cs`
- Role: provide a Viterna-Corrigan fallback for `CL` and `CD` when the Newton solver fails past stall.

## Public methods

- `ExtrapolatePostStall(alpha, lastConvergedAlpha, lastConvergedCL, lastConvergedCD, aspectRatio)`

## Notes

- Only used when `AnalysisSettings.UsePostStallExtrapolation` is enabled and the viscous solver does not converge.
- This is a fallback reporting tool, not part of the parity-critical Newton path.

## TODO

- Revisit the aspect-ratio and stall-anchor assumptions if post-stall reporting becomes a first-class feature.
