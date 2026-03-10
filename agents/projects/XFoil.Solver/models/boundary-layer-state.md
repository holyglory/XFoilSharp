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
  - `ViscousIntervalKind.cs`
  - `ViscousStationDerivedState.cs`
  - `ViscousIntervalState.cs`
  - `ViscousIntervalSystem.cs`
  - `ViscousCorrectionResult.cs`
  - `ViscousSolveResult.cs`
  - `ViscousInteractionResult.cs`
  - `DisplacementCoupledResult.cs`
  - `ViscousPolarPoint.cs`
  - `ViscousPolarSweepResult.cs`
  - `ViscousTargetLiftResult.cs`
  - `ViscousLiftSweepResult.cs`

## Role

- Hold the staged viscous pipeline from topology through coupled outputs.

## Parity

- Good for making the managed solver explicit and testable.
- Not evidence of full equation-level parity with original `xbl.f`/`xblsys.f`.

## TODO

- Split “state”, “residual system”, and “reported result” models into clearer documentation groups if more fields are added.
