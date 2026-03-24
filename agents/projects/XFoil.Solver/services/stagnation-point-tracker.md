# StagnationPointTracker

- File: `src/XFoil.Solver/Services/StagnationPointTracker.cs`
- Role: relocate boundary-layer station data when the stagnation panel moves during viscous iteration.

## Public methods

- `FindStagnationPoint(speed, n)`
- `MoveStagnationPoint(blState, oldISP, newISP, nPanel)`

## Notes

- `MoveStagnationPoint` is the part used by the current Newton solver.
- `ViscousSolverEngine` now uses its own fractional XFoil-style stagnation finder during initialization and iteration, then hands panel-to-station shifting off to this class.
- The relocation logic is important for stability, but it is still heuristic compared with the original Fortran state motion.

## TODO

- Keep this in sync with the engine-local stagnation mapping logic so the two paths do not drift apart.
