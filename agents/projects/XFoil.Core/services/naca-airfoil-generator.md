# NacaAirfoilGenerator

- File: `src/XFoil.Core/Services/NacaAirfoilGenerator.cs`
- Role: analytic NACA generator.

## Public methods

- `Generate4Digit(designation, pointCount = 161)`

## Important helpers

- `MeanCamber`

## Parity

- Covers NACA 4-digit generation used by current tests and CLI.
- Missing broader legacy generator coverage such as 5-digit support.

## TODO

- Add any additional NACA families actually needed for parity.
