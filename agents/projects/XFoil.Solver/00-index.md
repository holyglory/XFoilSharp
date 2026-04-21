# XFoil.Solver

- Role: aerodynamic analysis engine.
- Depends on: `XFoil.Core`.
- Documentation rule: update this subtree when solver models, numerics, operating-point flows, or parity assumptions change.

## Children

- [diagnostics/jsonl-trace-writer.md](diagnostics/jsonl-trace-writer.md)
- [models/analysis-settings.md](models/analysis-settings.md)
- [models/inviscid-results.md](models/inviscid-results.md)
- [models/mesh-and-wake.md](models/mesh-and-wake.md)
- [models/boundary-layer-state.md](models/boundary-layer-state.md)
- [models/linear-vortex-state.md](models/linear-vortex-state.md)
- [numerics/cubic-spline.md](numerics/cubic-spline.md)
- [numerics/dense-linear-system-solver.md](numerics/dense-linear-system-solver.md)
- [numerics/parametric-spline.md](numerics/parametric-spline.md)
- [numerics/tridiagonal-solver.md](numerics/tridiagonal-solver.md)
- [numerics/scaled-pivot-lu-solver.md](numerics/scaled-pivot-lu-solver.md)
- [numerics/block-tridiagonal-solver.md](numerics/block-tridiagonal-solver.md)
- [numerics/band-matrix-solver.md](numerics/band-matrix-solver.md)
- [services/airfoil-analysis-service.md](services/airfoil-analysis-service.md)
- [services/panel-mesh-generator.md](services/panel-mesh-generator.md)
- [services/curvature-adaptive-panel-distributor.md](services/curvature-adaptive-panel-distributor.md)
- [services/hess-smith-inviscid-solver.md](services/hess-smith-inviscid-solver.md)
- [services/linear-vortex-inviscid-solver.md](services/linear-vortex-inviscid-solver.md)
- [services/panel-geometry-builder.md](services/panel-geometry-builder.md)
- [services/streamfunction-influence-calculator.md](services/streamfunction-influence-calculator.md)
- [services/wake-geometry-generator.md](services/wake-geometry-generator.md)
- [services/boundary-layer-topology-builder.md](services/boundary-layer-topology-builder.md)
- [services/viscous-state-seed-builder.md](services/viscous-state-seed-builder.md)
- [services/viscous-state-estimator.md](services/viscous-state-estimator.md)
- [services/viscous-laminar-corrector.md](services/viscous-laminar-corrector.md)
- [services/boundary-layer-correlations.md](services/boundary-layer-correlations.md)
- [services/edge-velocity-calculator.md](services/edge-velocity-calculator.md)
- [services/influence-matrix-builder.md](services/influence-matrix-builder.md)
- [services/transition-model.md](services/transition-model.md)
- [services/viscous-newton-assembler.md](services/viscous-newton-assembler.md)
- [services/viscous-newton-updater.md](services/viscous-newton-updater.md)
- [services/viscous-solver-engine.md](services/viscous-solver-engine.md)
- [services/drag-calculator.md](services/drag-calculator.md)
- [services/stagnation-point-tracker.md](services/stagnation-point-tracker.md)
- [services/post-stall-extrapolator.md](services/post-stall-extrapolator.md)
- [services/polar-sweep-runner.md](services/polar-sweep-runner.md)

## Notes

- The primary viscous operating-point path is now `ViscousSolverEngine` plus `PolarSweepRunner`.
- Topology, seed, and initial-state services still matter for diagnostics, but they are no longer the main viscous solve chain.
- `XFoil.Solver.Diagnostics` now supports env-driven trace filters and a triggerable ring buffer so parity runs can keep callsites intact without writing full-firehose artifacts every time.
- `XFoil.Solver.Diagnostics` also stamps persisted trace payloads with exact numeric bit metadata (`dataBits` / `valuesBits` / `tagsBits`), and the Fortran reference pipeline mirrors that rule through `tools/fortran-debug/filter_trace.py`.
- Solver parity work now also relies on standalone raw-bit micro-drivers for numerics and kernel blocks, not only whole-solver traces. The current dedicated oracles cover `spline.f` / `SEGSPL`, `xsolve.f :: GAUSS`, `xpanel.f :: PSILIN`, the `CQ/CF/DI` scalar families, the alpha-10 panel-80 similarity seed system/step/final/handoff chain, and the downstream laminar helper chain through stations `3` to `5` on a tiny in-memory BL state.

## TODO

- Keep this index aligned with the real solver surface; removed surrogate services should not reappear here.
