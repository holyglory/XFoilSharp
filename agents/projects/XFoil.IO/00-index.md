# XFoil.IO

- Role: deterministic export, legacy import, batch session execution.
- Depends on: `XFoil.Core`, `XFoil.Solver`.
- Documentation rule: update this subtree when file formats, exporters, importers, or session behavior changes.

## Children

- [models/session-models.md](models/session-models.md)
- [models/legacy-polar-models.md](models/legacy-polar-models.md)
- [models/legacy-reference-polar-models.md](models/legacy-reference-polar-models.md)
- [models/legacy-polar-dump-models.md](models/legacy-polar-dump-models.md)
- [services/airfoil-dat-exporter.md](services/airfoil-dat-exporter.md)
- [services/polar-csv-exporter.md](services/polar-csv-exporter.md)
- [services/legacy-polar-importer.md](services/legacy-polar-importer.md)
- [services/legacy-reference-polar-importer.md](services/legacy-reference-polar-importer.md)
- [services/legacy-polar-dump-importer.md](services/legacy-polar-dump-importer.md)
- [services/legacy-polar-dump-archive-writer.md](services/legacy-polar-dump-archive-writer.md)
- [services/analysis-session-runner.md](services/analysis-session-runner.md)

## TODO

- Document any future choice between stateful legacy session emulation and current manifest-only execution.
