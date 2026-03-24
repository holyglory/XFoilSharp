# AirfoilAnalysisService

- File: `src/XFoil.Solver/Services/AirfoilAnalysisService.cs`
- Role: high-level façade for public inviscid analysis, diagnostic boundary-layer prep, and the current Newton-coupled viscous APIs.

## Active public methods

- `AnalyzeInviscid`
- `AnalyzeBoundaryLayerTopology`
- `AnalyzeViscousStateSeed`
- `AnalyzeViscousInitialState`
- `AnalyzeViscous`
- `SweepViscousAlpha`
- `SweepViscousCL`
- `SweepViscousRe`
- `SweepInviscidAlpha`
- `SweepInviscidLiftCoefficient`
- `AnalyzeInviscidForLiftCoefficient`

## Compatibility-only methods

- `AnalyzeViscousIntervalSystem`
- `AnalyzeViscousLaminarCorrection`
- `AnalyzeViscousLaminarSolve`
- `AnalyzeViscousInteraction`
- `AnalyzeDisplacementCoupledViscous`
- `SweepDisplacementCoupledAlpha`
- `SweepDisplacementCoupledLiftCoefficient`
- `AnalyzeDisplacementCoupledForLiftCoefficient`

These members are `[Obsolete]` and throw `NotSupportedException`. They remain only to keep older callers from silently compiling against changed behavior.

## Important helpers

- `AnalyzeInviscidLinearVortex` -- private adapter routing to `LinearVortexInviscidSolver` and mapping `LinearVortexInviscidResult` to `InviscidAnalysisResult`
- `PrepareInviscidSystem`
- `ToPolarPoint`
- `NormalizeStep`
- `ShouldContinue`
- `ExtractCoordinates`

## Solver dispatch

- `AnalyzeInviscid` checks `settings.InviscidSolverType`.
  - `HessSmith` uses the prepared Hess-Smith path.
  - `LinearVortex` uses the private adapter around `LinearVortexInviscidSolver`.
- `AnalyzeViscous` and the public viscous sweep methods always use the linear-vortex plus Newton path in `ViscousSolverEngine` and `PolarSweepRunner`.
- `SweepInviscidAlpha`, `SweepInviscidLiftCoefficient`, and `AnalyzeInviscidForLiftCoefficient` still use the prepared Hess-Smith path.

The adapter maps `LinearVortexInviscidResult` fields into `InviscidAnalysisResult`:
- `Circulation`, `SourceStrengths`, and `VortexStrength` are Hess-Smith-centric placeholders.
- `PressureSamples` are still left empty.
- `WakeGeometry` is still empty on the adapted linear-vortex result.
- `PanelMeshGenerator` is still used to populate the public mesh-facing result shape.

## Parity

- This is the best single entry point for understanding what is actually supported today.
- It also exposes the main API discrepancy in the solver layer:
  - single-point inviscid analysis can select the linear-vortex solver
  - public inviscid sweeps cannot
  - viscous analysis always uses the linear-vortex front end

## Notes

- `BoundaryLayerTopologyBuilder`, `ViscousStateSeedBuilder`, and `ViscousStateEstimator` still back the diagnostic CLI commands.
- `ViscousLaminarCorrector` is still constructed here, but it is no longer part of the primary viscous operating-point solve.

## TODO

- Either add linear-vortex routing to the public inviscid sweep APIs or keep the Hess-Smith-only behavior explicit.
- Remove the obsolete surrogate API surface once compatibility no longer matters.
