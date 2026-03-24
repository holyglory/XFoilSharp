# Boundary-Layer State Models

- Files:
  - `BoundaryLayerBranch.cs`
  - `BoundaryLayerStation.cs`
  - `BoundaryLayerTopology.cs`
  - `BoundaryLayerCorrelationConstants.cs`
  - `ViscousBranchSeed.cs`
  - `ViscousStationSeed.cs`
  - `ViscousStateSeed.cs`
  - `ViscousFlowRegime.cs`
  - `ViscousBranchState.cs`
  - `ViscousStationState.cs`
  - `ViscousStateEstimate.cs`
  - `BoundaryLayerSystemState.cs`
  - `ViscousNewtonSystem.cs`
  - `ViscousIntervalKind.cs`
  - `ViscousStationDerivedState.cs`
  - `ViscousIntervalState.cs`
  - `ViscousIntervalSystem.cs`
  - `ViscousCorrectionResult.cs`
  - `ViscousSolveResult.cs`
  - `ViscousInteractionResult.cs`
  - `DisplacementCoupledResult.cs`
  - `DragDecomposition.cs`
  - `TransitionInfo.cs`
  - `ViscousConvergenceInfo.cs`
  - `ViscousAnalysisResult.cs`
  - `ViscousPolarPoint.cs`
  - `ViscousPolarSweepResult.cs`
  - `ViscousTargetLiftResult.cs`
  - `ViscousLiftSweepResult.cs`

## Role

- Hold the diagnostic seed pipeline, the mutable Newton workspaces, and the reported outputs from single-point and polar viscous analysis.

## Notes

- `BoundaryLayerSystemState` and `ViscousNewtonSystem` are the in-place array workspaces used by `ViscousSolverEngine`, `ViscousNewtonAssembler`, and the linear solvers.
- `DragDecomposition`, `TransitionInfo`, `ViscousConvergenceInfo`, and `ViscousAnalysisResult` are part of the primary viscous API surface.
- `ViscousIntervalSystem`, `ViscousCorrectionResult`, `ViscousSolveResult`, `ViscousInteractionResult`, and `DisplacementCoupledResult` mostly survive to support obsolete compatibility APIs and historical docs.

## Parity

- The model layer now reflects far more of the real Newton solver than the old docs suggested.
- The parity gap is in solver behavior, not in missing result containers.

## TODO

- Split seed, Newton-workspace, and reported-result models into clearer documentation groups if this subtree grows further.
