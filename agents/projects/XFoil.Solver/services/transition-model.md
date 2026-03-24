# TransitionModel

- File: `src/XFoil.Solver/Services/TransitionModel.cs`
- Role: port of XFoil's e^N envelope calculations and transition checks.

## Public methods

- `ComputeAmplificationRate`
- `ComputeAmplificationRateHighHk`
- `ComputeTransitionSensitivities`
- `CheckTransition`

## Notes

- This covers the `DAMPL`, `DAMPL2`, `AXSET`, and `TRCHEK2` family from `xblsys.f`.
- `TransitionModel.CheckTransition` is called from `ViscousNewtonAssembler` during the station march.
- The dedicated transition tests verify this file more directly than most viscous components.
- The active March 21 parity frontier is the station-15 iteration-5 `transition_final_sensitivities.xtX2` update. The current legacy replay now rounds the `XT_A2/Z_A2 * Z_X2` correction product back to `REAL` before the final subtraction, because the promoted direct/inverse station-15 transition-window rigs proved that keeping that correction wide reproduces the wrong managed word (`0xBD8D7333` instead of `0xBD8D7330`).

## TODO

- Keep the free and forced transition behavior aligned with the settings surface as more CLI wiring is added.
