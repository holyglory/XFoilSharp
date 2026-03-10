# BoundaryLayerTopologyBuilder

- File: `src-cs/XFoil.Solver/Services/BoundaryLayerTopologyBuilder.cs`
- Role: derive stagnation-centered upper/lower/wake branch topology from inviscid data.

## Public methods

- `Build(analysis)`

## Important helpers

- `BuildNodeArcLengths`
- `BuildPanelArcLengths`
- `FindStagnationLocation`
- `BuildUpperStations`
- `BuildLowerStations`
- `BuildWakeStations`
- `EstimateNodeEdgeVelocity`

## TODO

- Tighten mapping to original topology bookkeeping in `xpanel.f`/`xbl.f`.
