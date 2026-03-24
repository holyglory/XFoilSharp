# CLI Viscous Analysis Commands

- File: `src/XFoil.Cli/Program.cs`

## Live operating commands

- `viscous-polar-*`
- `export-viscous-polar-*`
- `viscous-solve-cl-*`
- `viscous-polar-cl-*`
- `export-viscous-polar-cl-*`

These flow into `SweepViscousAlpha`, `SweepViscousCL`, or a one-point `SweepViscousCL` wrapper for target-`CL`.

## Diagnostic commands

- `topology-*`
- `viscous-seed-*`
- `viscous-init-*`

These still run the topology, seed, and initial-state builders even though they are not the main viscous operating-point path.

## Deprecated compatibility commands

- `viscous-interval-*`
- `viscous-correct-*`
- `viscous-solve-*`
- `viscous-interact-*`
- `viscous-coupled-*`

These now print deprecation messages only. They do not execute the removed surrogate pipeline.

## Main helpers

- `WriteBoundaryLayerTopologySummary`
- `WriteViscousSeedSummary`
- `WriteViscousInitialStateSummary`
- `WriteViscousPolarSummary`
- `WriteViscousTargetLiftSummary`
- `WriteViscousLiftSweepSummary`
- `WriteViscousIntervalSummary`
- `WriteViscousCorrectionSummary`
- `WriteViscousSolveSummary`
- `WriteViscousInteractionSummary`
- `WriteDisplacementCoupledSummary`

## Important discrepancy

- `CreateViscousSettings` currently wires only `panelCount`, `machNumber`, `reynoldsNumber`, `transitionReynoldsTheta`, and `criticalAmplificationFactor`.
- Legacy parameters such as `couplingIterations`, `viscousIterations`, `residualTolerance`, and `displacementRelaxation` are still parsed by the CLI surface but ignored by the current settings construction path.

## TODO

- Either delete the legacy viscous arguments or map them to real `AnalysisSettings` and Newton-solver controls.
