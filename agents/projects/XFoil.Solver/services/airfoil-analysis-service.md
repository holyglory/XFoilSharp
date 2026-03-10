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

- `PrepareInviscidSystem`
- `ToPolarPoint`
- `NormalizeStep`
- `ShouldContinue`
- `ComputeAdaptiveDisplacementRelaxation`
- `ComputeHybridSeedCouplingFactor`

## Parity

- Best single entry point for current managed workflow coverage.
- Also where many surrogate choices become visible.

## TODO

- Break into smaller orchestrators once the CLI no longer needs a single façade.
