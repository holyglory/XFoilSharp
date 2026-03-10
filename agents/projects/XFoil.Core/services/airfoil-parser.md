# AirfoilParser

- File: `src-cs/XFoil.Core/Services/AirfoilParser.cs`
- Role: turns text or files into `AirfoilGeometry`.

## Public methods

- `ParseFile(path)`
  - Reads a file and delegates to line parsing.
- `ParseLines(lines, fallbackName = "UNNAMED")`
  - Detects names, separators, and coordinate rows.

## Important helpers

- `SplitElements`
- `IsElementSeparator`
- `ParsePoint`
- `TryParsePoint`
- `ParseNumericTokens`
- `TryParseDouble`
- `Tokenize`

## Parity

- Supports the current practical fixture set, including labeled and ISES/MSES-like inputs.
- Does not claim full `aread.f` parity.

## TODO

- Audit against more historical input files from original XFoil.
