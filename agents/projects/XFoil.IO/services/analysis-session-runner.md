# AnalysisSessionRunner

- File: `src-cs/XFoil.IO/Services/AnalysisSessionRunner.cs`
- Role: batch manifest executor.

## Public methods

- `AnalysisSessionRunner()`
- `AnalysisSessionRunner(...)`
- `Run(manifestPath, outputDirectory)`

## Important helpers

- `RunSweep`
- `ImportLegacyPolar`
- `ImportLegacyReferencePolar`
- `ImportLegacyPolarDump`
- `ExportInviscidAlphaSweep`
- `ExportInviscidLiftSweep`
- `ExportViscousAlphaSweep`
- `ExportViscousLiftSweep`
- `LoadGeometry`
- `CreateInviscidSettings`
- `CreateViscousSettings`

## Parity

- Good batch/session bridge.
- Not the same as original stateful OPER workspace behavior.

## TODO

- Decide whether stateful session emulation is needed or whether manifests remain the intended direction.
