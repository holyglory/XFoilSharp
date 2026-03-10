# BoundaryLayerSystemAssembler

Static utility class porting BL equation system assembly from XFoil's xblsys.f.

## Location

`src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`

## Purpose

Assembles the 3-equation BL residuals and Jacobian blocks for each station pair in the viscous Newton system. This is the innermost computational kernel of the viscous solver.

## Fortran Parity

Direct port of these routines from `f_xfoil/src/xblsys.f`:

| Fortran | C# Method | Lines |
|---------|-----------|-------|
| BLPRV | ConvertToCompressible | 701-722 |
| BLKIN | ComputeKinematicParameters | 725-780 |
| BLVAR | ComputeStationVariables | 784-1120 |
| BLMID | ComputeMidpointCorrelations | 1123-1191 |
| BLDIF | ComputeFiniteDifferences | 1551-1976 |
| TESYS | AssembleTESystem | 664-698 |
| BLSYS | AssembleStationSystem | 583-661 |

## Public API

- `ConvertToCompressible(uei, tkbl, qinfbl, tkbl_ms)` -- Karman-Tsien compressibility transform
- `ComputeKinematicParameters(...)` -- M2, R2, H, Hk, Rt with all Jacobian sensitivities
- `ComputeStationVariables(ityp, hk, rt, msq, h, ctau, dw, theta)` -- master correlation dispatch
- `ComputeMidpointCorrelations(ityp, hk1, rt1, m1, hk2, rt2, m2)` -- midpoint Cf
- `ComputeFiniteDifferences(ityp, x1..amcrit)` -- 3-equation residuals + 3x5 Jacobian blocks
- `AssembleTESystem(cte, tte, dte, ...)` -- TE-to-wake junction
- `AssembleStationSystem(...)` -- top-level dispatcher

## Dependencies

- `BoundaryLayerCorrelations` -- all correlation functions (Cf, H*, Di, Cteq)
- `BoundaryLayerCorrelationConstants` -- BLPAR constants

## Result Types

- `KinematicResult` -- M2, R2, H, Hk, Rt with sensitivities
- `StationVariables` -- Cf, Hs, Di, Cteq, Us, De, Hc
- `MidpointResult` -- Cfm with sensitivities
- `BldifResult` -- Residual[3], VS1[3,5], VS2[3,5]
- `BlsysResult` -- Residual[3], VS1[3,5], VS2[3,5]

## TODOs

- TRDIF (transition interval) is referenced in plan but deferred -- BLDIF handles laminar amplification directly
- Full Jacobian chain rules in BLDIF are simplified; exact Fortran-matching Jacobians needed for Newton convergence
- BLSYS compressibility mapping from incompressible to compressible Ue sensitivities is implemented but MSQ computation at stations is simplified for M=0 fast path
