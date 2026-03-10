# AirfoilGeometry

- File: `src/XFoil.Core/Models/AirfoilGeometry.cs`
- Kind: canonical geometry container.

## Constructor

- `AirfoilGeometry(name, points, format, domainParameters = null)`
  - Stores geometry identity and coordinates.

## Used by

- Paneling
- Design transforms
- IO export/import
- CLI command outputs

## Parity

- Managed replacement for geometry arrays spread across original XFoil global state.

## TODO

- Clarify long-term meaning of `DomainParameters`.
