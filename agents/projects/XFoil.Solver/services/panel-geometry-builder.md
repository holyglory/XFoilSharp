# PanelGeometryBuilder

- File: `src/XFoil.Solver/Services/PanelGeometryBuilder.cs`
- Type: static utility class
- Created: Phase 2 Plan 2

## Purpose

Geometry pipeline for the linear-vorticity panel method. Computes panel normals, angles, trailing-edge gap analysis, compressibility parameters, and branch-cut-continuous atan2.

## Public API

- `ComputeNormals(LinearVortexPanelState panel)` -- Outward unit normals from spline derivatives, with corner averaging. Port of NCALC.
- `ComputePanelAngles(LinearVortexPanelState panel, InviscidSolverState state)` -- Panel angles using XFoil convention atan2(dy, -dx). Port of APCALC.
- `ComputeTrailingEdgeGeometry(LinearVortexPanelState panel, InviscidSolverState state)` -- TE gap magnitude, sharp flag, normal/streamwise decomposition. Port of TECALC.
- `ComputeCompressibilityParameters(double machNumber)` -- Karman-Tsien beta and bfac. Port of COMSET.
- `ContinuousAtan2(double y, double x, double referenceAngle)` -- Branch-cut continuation. Port of ATANC.

## Dependencies

- `ParametricSpline.FitSegmented` -- for spline derivatives in ComputeNormals
- `LinearVortexPanelState` -- reads X, Y, ArcLength; writes NormalX, NormalY, PanelAngle, XDerivative, YDerivative
- `InviscidSolverState` -- reads/writes TE geometry fields

## Fortran Origins

| Method | Fortran routine | Source file |
|--------|----------------|-------------|
| ComputeNormals | NCALC | xpanel.f:51-84 |
| ComputePanelAngles | APCALC | xpanel.f:22-48 |
| ComputeTrailingEdgeGeometry | TECALC | xfoil.f:2270-2308 |
| ComputeCompressibilityParameters | COMSET | xfoil.f:1019-1044 |
| ContinuousAtan2 | ATANC | xutils.f:68-112 |

## Parity Notes

- Normal computation uses segmented spline (matching SEGSPL) for derivative computation.
- Corner handling uses duplicate arc-length values (matching Fortran convention).
- Panel angle convention atan2(sx, -sy) matches Fortran APCALC exactly.
- TE geometry uses the exact Fortran decomposition: ANTE = DXS*DYTE - DYS*DXTE, ASTE = DXS*DXTE + DYS*DYTE.

## TODO

- Verify normal vectors node-by-node against Fortran NCALC output on a reference airfoil.
