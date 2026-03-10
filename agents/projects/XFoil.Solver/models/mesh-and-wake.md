# Mesh And Wake Models

- Files:
  - `Panel.cs`
  - `PanelMesh.cs`
  - `PanelingOptions.cs`
  - `PreparedInviscidSystem.cs`
  - `PressureCoefficientSample.cs`
  - `WakeGeometry.cs`
  - `WakePoint.cs`

## Role

- Hold panel geometry, reusable inviscid system state, surface samples, and wake output.

## Parity

- The model split is cleaner than the original Fortran arrays.
- The underlying physics is still approximate in places.

## TODO

- Add a documented mapping from `PreparedInviscidSystem` fields to the legacy panel solve concepts they approximate.
