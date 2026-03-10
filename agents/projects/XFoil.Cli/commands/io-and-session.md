# CLI IO And Session Commands

- File: `src/XFoil.Cli/Program.cs`

## Commands

- `export-polar-*`
- `export-polar-cl-*`
- `export-viscous-polar-*`
- `export-viscous-polar-cl-*`
- `import-legacy-polar`
- `show-legacy-polar`
- `import-legacy-reference-polar`
- `show-legacy-reference-polar`
- `import-legacy-polar-dump`
- `show-legacy-polar-dump`
- `run-session`

## Main helpers

- `ExportPolarCsv`
- `ExportViscousPolarCsv`
- `ExportLiftSweepCsv`
- `ExportViscousLiftSweepCsv`
- `ImportLegacyPolar`
- `WriteLegacyPolarSummary`
- `ImportLegacyReferencePolar`
- `WriteLegacyReferencePolarSummary`
- `ImportLegacyPolarDump`
- `WriteLegacyPolarDumpSummary`
- `RunSession`

## TODO

- Decide whether the CLI should ever emulate original stateful polar/session handling.
