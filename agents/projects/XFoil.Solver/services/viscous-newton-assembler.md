# ViscousNewtonAssembler

- File: `src/XFoil.Solver/Services/ViscousNewtonAssembler.cs`
- Role: assemble the global BL Newton system for the current viscous state.

## Public methods

- `BuildNewtonSystem(...)`

## What it does

- Marches both surfaces and the wake in XFoil order.
- Calls `TransitionModel.CheckTransition` during the march.
- Delegates local equation assembly to `BoundaryLayerSystemAssembler`.
- Writes residuals and Jacobian blocks into `ViscousNewtonSystem`.

## Notes

- This is the current managed home of the `SETBL` style logic.
- The recent documentation drift was particularly misleading here: the code has a real Newton assembler, but the old docs still described only the removed staged interval builder.
- Even with the full assembler present, the current parity tests still show the overall Newton solve converging to the wrong fixed point.
- The alpha-10 panel-80 wake/`UESET` closure also proved one exact `UESET` storage rule here: classic XFoil accumulates the source-induced delta into a standalone `DUI` word across the full contributor loop, keeps `DUIA` / `DUIW` only as diagnostic subtotals, and performs the final predicted-edge update with one legacy `UINV + DUI` add. Streaming `UINV` through every contributor term kept the broad upper station-2 `predicted` / `USAV_SPLIT` word high even after every individual `predicted_edge_velocity_term` packet already matched bitwise.

## TODO

- Keep this file synchronized with any future Fortran-side diagnostics used to chase the remaining fixed-point mismatch.
