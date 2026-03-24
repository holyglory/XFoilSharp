# BLVAR Turbulent-DI Micro Parity Tests

## Scope

- Registry parent: `blvar-turbulent-di-upstream`
- Shared code owners:
  - `BoundaryLayerCorrelations.ComputeTurbulentWallDi`
  - `BoundaryLayerCorrelations.ComputeTurbulentDfac`
  - `BoundaryLayerCorrelations.ComputeTurbulentOuterDi`
  - `BoundaryLayerSystemAssembler` turbulent DI packet assembly

## Family Model

- This is a focused owner row, not a broad viscous-system consumer.
- The quick probe now routes through the already-split turbulent-DI packet ladder:
  - `TurbulentWallDiFortranParityTests`
  - `TurbulentDiDfacFortranParityTests`
  - `TurbulentOuterDiFortranParityTests`
  - `TurbulentDiChainFortranParityTests`
- The station-15 direct-seed BLVAR DI replay remains the representative trace-backed proof for the broader live path.

## Current Status

- The old quick wrapper path was the historical alpha-10 station-29 block alias, which loaded a `5.8 GB` managed solver trace and could time out the combined promoted quick pass before it reached a meaningful BLVAR DI verdict.
- `BlvarTurbulentDiMicroParityTests.Alpha10_BlvarTurbulentDiTerms_Station29SeedProducer_MatchFortran` is now a compatibility alias for the packet-level `TurbulentDiChain` owner surface instead of that giant-trace path.
- A standalone quick matrix rerun is green at `1352/1000` real vectors in `/Users/slava/Agents/XFoilSharp/tools/fortran-debug/mrm-blvar-di-quick7`.
- The remaining open issue is the representative station-15 trace-backed replay, which is still red on `blvar_turbulent_di_terms`.

## Debug Rule

- Treat this quick owner ladder as the first-line BLVAR DI debug loop.
- If the representative station-15 replay stays red after a short focused pass, route farther upstream into the specific DI packet (`wall`, `dfac`, `outer`, or final chain) instead of reopening broader viscous march consumers.

## TODO

- Reconcile the remaining station-15 representative replay mismatch against the current packet ladder.
- Decide whether the representative red is already visible in one of the smaller DI packets or only appears in the final assembled BLVAR DI packet.
