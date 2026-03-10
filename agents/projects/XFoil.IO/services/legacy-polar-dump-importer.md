# LegacyPolarDumpImporter

- File: `src/XFoil.IO/Services/LegacyPolarDumpImporter.cs`
- Role: import unformatted legacy polar dumps.

## Public methods

- `Import(path)`

## Important helpers

- `ReadGeometry`
- `ReadSideSamples`
- `ResolveMach`
- low-level record readers

## Parity

- Managed support exists.
- Historical validation breadth is still limited.

## TODO

- Validate against more real dump files from original XFoil runs.
