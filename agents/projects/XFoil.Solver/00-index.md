# XFoil.Solver

- Role: aerodynamic analysis engine.
- Depends on: `XFoil.Core`.
- Documentation rule: update this subtree when solver models, numerics, operating-point flows, or parity assumptions change.

## Children

- [models/analysis-settings.md](models/analysis-settings.md)
- [models/inviscid-results.md](models/inviscid-results.md)
- [models/mesh-and-wake.md](models/mesh-and-wake.md)
- [models/boundary-layer-state.md](models/boundary-layer-state.md)
- [numerics/cubic-spline.md](numerics/cubic-spline.md)
- [numerics/dense-linear-system-solver.md](numerics/dense-linear-system-solver.md)
- [numerics/parametric-spline.md](numerics/parametric-spline.md)
- [numerics/tridiagonal-solver.md](numerics/tridiagonal-solver.md)
- [numerics/scaled-pivot-lu-solver.md](numerics/scaled-pivot-lu-solver.md)
- [models/linear-vortex-state.md](models/linear-vortex-state.md)
- [services/airfoil-analysis-service.md](services/airfoil-analysis-service.md)
- [services/panel-mesh-generator.md](services/panel-mesh-generator.md)
- [services/cosine-clustering-panel-distributor.md](services/cosine-clustering-panel-distributor.md)
- [services/hess-smith-inviscid-solver.md](services/hess-smith-inviscid-solver.md)
- [services/linear-vortex-inviscid-solver.md](services/linear-vortex-inviscid-solver.md)
- [services/wake-geometry-generator.md](services/wake-geometry-generator.md)
- [services/boundary-layer-topology-builder.md](services/boundary-layer-topology-builder.md)
- [services/viscous-state-seed-builder.md](services/viscous-state-seed-builder.md)
- [services/viscous-state-estimator.md](services/viscous-state-estimator.md)
- [services/laminar-amplification-model.md](services/laminar-amplification-model.md)
- [services/viscous-interval-system-builder.md](services/viscous-interval-system-builder.md)
- [services/viscous-laminar-corrector.md](services/viscous-laminar-corrector.md)
- [services/viscous-laminar-solver.md](services/viscous-laminar-solver.md)
- [services/edge-velocity-feedback-builder.md](services/edge-velocity-feedback-builder.md)
- [services/viscous-interaction-coupler.md](services/viscous-interaction-coupler.md)
- [services/panel-geometry-builder.md](services/panel-geometry-builder.md)
- [services/streamfunction-influence-calculator.md](services/streamfunction-influence-calculator.md)
- [services/displacement-surface-geometry-builder.md](services/displacement-surface-geometry-builder.md)
- [services/viscous-force-estimator.md](services/viscous-force-estimator.md)

## TODO

- Split inviscid and viscous concerns more explicitly if the project keeps growing.
