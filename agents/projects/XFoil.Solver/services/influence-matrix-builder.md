# InfluenceMatrixBuilder

- File: `src/XFoil.Solver/Services/InfluenceMatrixBuilder.cs`
- Role: build the DIJ coupling matrix used to convert BL mass changes into edge-velocity corrections.

## Public methods

- `BuildAnalyticalDIJ(inviscidState, panelState, nWake)`
- `BuildNumericalDIJ(inviscidState, panelState, nWake, epsilon)`

## Notes

- The analytical path is the one used by `ViscousSolverEngine`.
- The numerical path exists as a debugging and validation cross-check against the analytical assembly.
- This class depends on the linear-vortex inviscid state being already assembled and LU-factored.
- The alpha-10 wake-coupling split also left one intentionally narrow parity replay here. The generic wake-source stencil now keeps the widened half-2 `dqJq` replay, but the half-1 `dzJm` family still has one owner-specific raw-word patch for the `sourceIndex=8`, `field 43`, `segment 9` wake RHS packet. Broadening that replay to the whole half-1 recurrence moved the old `UESET` frontier upstream again, so future parity work should treat that `dzJm` fix as a focused owner closure, not as evidence that the whole wake-source formula needs to change.

## TODO

- Keep the analytical and numerical paths close enough that the numerical build remains a useful regression check.
