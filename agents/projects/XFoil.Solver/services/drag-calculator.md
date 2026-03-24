# DragCalculator

- File: `src/XFoil.Solver/Services/DragCalculator.cs`
- Role: compute drag decomposition from a viscous boundary-layer state.

## Public methods

- `ComputeDrag(blState, panel, qinf, alfa, machNumber, teGap, useExtendedWake, useLockWaveDrag)`

## What it does

- Uses Squire-Young wake extrapolation for total `CD`.
- Integrates skin friction for `CDF`.
- Forms `CDP` by subtraction.
- Computes a TE base-drag term and a surface-integrated cross-check.
- Optionally adds a Lock-style wave-drag estimate for transonic cases.

## Notes

- The decomposition is part of the primary `AnalyzeViscous` result surface.
- The tests currently validate internal consistency such as `CDF + CDP = CD`, but absolute drag parity still depends on fixing the upstream Newton solution.

## TODO

- Revisit the extended-wake and wave-drag options once the core viscous fixed point matches Fortran.
