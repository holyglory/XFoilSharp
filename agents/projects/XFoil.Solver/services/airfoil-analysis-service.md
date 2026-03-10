# AirfoilAnalysisService

- File: `src/XFoil.Solver/Services/AirfoilAnalysisService.cs`
- Role: high-level façade for all solver workflows.

## Public methods

- `AnalyzeInviscid`
- `AnalyzeBoundaryLayerTopology`
- `AnalyzeViscousStateSeed`
- `AnalyzeViscousInitialState`
- `AnalyzeViscousIntervalSystem`
- `AnalyzeViscousLaminarCorrection`
- `AnalyzeViscousLaminarSolve`
- `AnalyzeViscousInteraction`
- `AnalyzeDisplacementCoupledViscous`
- `SweepInviscidAlpha`
- `SweepDisplacementCoupledAlpha`
- `SweepDisplacementCoupledLiftCoefficient`
- `SweepInviscidLiftCoefficient`
- `AnalyzeInviscidForLiftCoefficient`
- `AnalyzeDisplacementCoupledForLiftCoefficient`

## Important helpers

- `AnalyzeInviscidLinearVortex` -- private adapter routing to `LinearVortexInviscidSolver` and mapping `LinearVortexInviscidResult` to `InviscidAnalysisResult`
- `PrepareInviscidSystem`
- `ToPolarPoint`
- `NormalizeStep`
- `ShouldContinue`
- `ComputeAdaptiveDisplacementRelaxation`
- `ComputeHybridSeedCouplingFactor`

## Solver dispatch

`AnalyzeInviscid` checks `settings.InviscidSolverType`:
- `HessSmith` (default): existing Hess-Smith path via `this.inviscidSolver`
- `LinearVortex`: routes to `LinearVortexInviscidSolver.AnalyzeInviscid` via the private adapter

The adapter maps `LinearVortexInviscidResult` fields into `InviscidAnalysisResult`:
- `Circulation`, `SourceStrengths`, `VortexStrength` set to zero/empty (Hess-Smith concepts)
- `PressureSamples` left empty (Cp mapping deferred to Phase 3)
- `WakeGeometry` empty (not produced by linear-vortex path)
- Mesh generated via `PanelMeshGenerator` for downstream consumers

Sweep methods (`SweepInviscidAlpha`, etc.) still use Hess-Smith only. Wiring sweep methods through the dispatch is a Phase 3 task.

## Parity

- Best single entry point for current managed workflow coverage.
- Also where many surrogate choices become visible.
- Both Hess-Smith and linear-vorticity solvers are now selectable through the `AnalyzeInviscid` method.

## TODO

- Wire sweep methods through the dispatch for LinearVortex selection (Phase 3).
- Break into smaller orchestrators once the CLI no longer needs a single façade.
