# PolarCsvExporter

- File: `src/XFoil.IO/Services/PolarCsvExporter.cs`
- Role: deterministic CSV export for managed and imported polar data.

## Public methods

- `Format` / `Export` overloads for:
  - `PolarSweepResult`
  - `ViscousPolarSweepResult`
  - `InviscidLiftSweepResult`
  - `ViscousLiftSweepResult`
  - `LegacyPolarFile`
  - `LegacyReferencePolarFile`

## Important helpers

- `FormatDouble`
- `FormatInteger`
- `FormatBoolean`
- `WriteFile`

## TODO

- Add exact legacy dump/Cp export formats if parity requires them.
