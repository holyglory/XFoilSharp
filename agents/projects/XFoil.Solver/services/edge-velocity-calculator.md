# EdgeVelocityCalculator

- File: `src/XFoil.Solver/Services/EdgeVelocityCalculator.cs`
- Role: helper utilities for BL station indexing, inviscid speed setup, and wake-speed utilities.

## Public methods

- `MapPanelsToBLStations`
- `ComputeBLArcLength`
- `MapStationsToSystemLines`
- `ComputeInviscidEdgeVelocity`
- `ComputeViscousEdgeVelocity`
- `SetVortexFromViscousSpeed`
- `SetInviscidSpeeds`
- `ComputeWakeVelocities`

## Notes

- `MapStationsToSystemLines` and `SetInviscidSpeeds` are the methods that the current Newton viscous path relies on directly.
- `MapPanelsToBLStations` and `ComputeBLArcLength` reflect the older staged station-building utilities; the current `ViscousSolverEngine` now builds its XFoil-style station mapping internally before handing line indexing off to this class.
- Fresh alpha-0 focused traces on `NACA 0012, Re=1e6, alpha=0, panels=60` show that the first seed-station `uei` mismatch is already present in `USAV_SPLIT IS=1 IBL=2`, so the inviscid edge-speed baseline feeding station 2 remains an open producer boundary.

## TODO

- Remove or rename the older helpers if the codebase fully standardizes on the engine-local XFoil station mapping path.
- Keep `SetInviscidSpeeds` and nearby baseline/stagnation consumers under active parity review for the alpha-0 rung before touching downstream seed logic again.
