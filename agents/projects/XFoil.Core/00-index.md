# XFoil.Core

- Role: domain and geometry foundation.
- Depends on: nothing.
- Used by: solver, design, IO, CLI.
- Documentation rule: update this subtree when Core models, parsing, normalization, metrics, or NACA generation behavior changes.

## Children

- [models/airfoil-format.md](models/airfoil-format.md)
- [models/airfoil-geometry.md](models/airfoil-geometry.md)
- [models/airfoil-metrics.md](models/airfoil-metrics.md)
- [models/airfoil-point.md](models/airfoil-point.md)
- [services/airfoil-parser.md](services/airfoil-parser.md)
- [services/airfoil-normalizer.md](services/airfoil-normalizer.md)
- [services/airfoil-metrics-calculator.md](services/airfoil-metrics-calculator.md)
- [services/naca-airfoil-generator.md](services/naca-airfoil-generator.md)

## TODO

- Add explicit docs for any future multi-element/domain metadata that moves beyond the current `DomainParameters` bag.
