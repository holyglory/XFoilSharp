# AnalysisSettings

- File: `src/XFoil.Solver/Models/AnalysisSettings.cs`
- Role: central runtime input bag for paneling, Mach, Reynolds, transition, and coupling controls.

## Constructor

- `AnalysisSettings(...)`

## Properties

- `PanelCount`, `FreestreamVelocity`, `MachNumber`, `ReynoldsNumber`, `Paneling`, `TransitionReynoldsTheta`, `CriticalAmplificationFactor`
- `InviscidSolverType` -- selects between `HessSmith` (default, backward-compatible) and `LinearVortex` (XFoil parity solver). Added in Phase 02 Plan 04.

## Notes

- This is the managed replacement for a large amount of original run-state configuration.

## TODO

- Separate stable user-facing settings from experimental surrogate-coupling knobs.
