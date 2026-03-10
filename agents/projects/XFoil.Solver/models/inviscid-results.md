# Inviscid Result Models

- Files:
  - `InviscidAnalysisResult.cs`
  - `PolarPoint.cs`
  - `PolarSweepResult.cs`
  - `InviscidTargetLiftResult.cs`
  - `InviscidLiftSweepResult.cs`

## Role

- Represent single-point and sweep outputs for inviscid analysis.

## Notes

- `InviscidAnalysisResult` is the main payload shared with design workflows.
- `PolarPoint` and sweep objects are batch-oriented replacements for legacy in-memory polar slots.

## Parity

- Workflow coverage exists.
- Storage/session behavior is much thinner than original XFoil.

## TODO

- Add more explicit mapping from each field to original XFoil coefficient outputs.
