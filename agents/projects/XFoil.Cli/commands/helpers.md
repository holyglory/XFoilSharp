# CLI Helper Methods

- File: `src/XFoil.Cli/Program.cs`

## Parsing helpers

- `ParseDouble`
- `ParseRemainingDoubles`
- `ParseInteger`
- `ParseControlPointsFile`
- `ParseBooleanFlag`
- `ParseCornerRefinementMode`
- `TryParseCornerRefinementMode`
- `ParseScaleOrigin`

## Formatting helpers

- `WriteQSpecCsv`
- `WriteConformalCoefficientCsv`
- `WriteModalSpectrumCsv`
- `WriteQSpecSetCsv`
- `EscapeCsv`

## TODO

- Move parsing/formatting helpers into shared CLI support files once `Program.cs` is decomposed.
