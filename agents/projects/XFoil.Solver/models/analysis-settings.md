# AnalysisSettings

- File: `src/XFoil.Solver/Models/AnalysisSettings.cs`
- Role: central runtime input bag for paneling, inviscid solver choice, Newton-solver behavior, transition, and post-stall options.

## Constructor

- `AnalysisSettings(...)`

## Properties

- `PanelCount`, `FreestreamVelocity`, `MachNumber`, `ReynoldsNumber`, `Paneling`
- `TransitionReynoldsTheta`, `CriticalAmplificationFactor`, `NCritUpper`, `NCritLower`
- `InviscidSolverType` -- selects between `HessSmith` and `LinearVortex` for single-point inviscid analysis
- `ViscousSolverMode` -- selects trust-region or XFoil-style relaxation in the Newton updater
- `ForcedTransitionUpper`, `ForcedTransitionLower`
- `UseExtendedWake`, `UseModernTransitionCorrections`
- `MaxViscousIterations`, `ViscousConvergenceTolerance`
- `UsePostStallExtrapolation`

## Notes

- This is the managed replacement for a large amount of original run-state configuration.
- Not every caller wires every property today.
  - Example: the CLI helper `CreateViscousSettings` currently sets panel count, Mach, Reynolds, transition Reynolds-theta, and global `Ncrit`, but not solver mode, iteration limits, tolerance, wake mode, or forced transition.

## TODO

- Separate stable user-facing settings from parity and debugging knobs once the CLI catches up.
