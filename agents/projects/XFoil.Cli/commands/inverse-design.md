# CLI Inverse Design Commands

- File: `src-cs/XFoil.Cli/Program.cs`

## Commands

- `qdes-profile-*`
- `qdes-symm-*`
- `qdes-aq-*`
- `qdes-cq-*`
- `qdes-modi-*`
- `qdes-smoo-*`
- `qdes-exec-*`
- `mdes-spec-*`
- `mdes-exec-*`
- `mdes-pert-*`
- `mapgen-exec-*`
- `mapgen-spec-*`
- `mapgen-filt-*`
- `mapgen-filt-spec-*`
- `mapgen-tang-*`

## Main helpers

- `ExportQSpecProfile`
- `ExportSymmetricQSpecProfile`
- `ExportQSpecProfileSetForAngles`
- `ExportQSpecProfileSetForLiftCoefficients`
- `ExportModifiedQSpecProfile`
- `ExportSmoothedQSpecProfile`
- `ExportExecutedQSpecGeometry`
- `ExportModalSpectrum`
- `ExportModalExecutedGeometry`
- `ExportPerturbedModalGeometry`
- `ExportConformalMapgenGeometry`
- `ExportConformalMapgenSpectrum`

## TODO

- Keep CLI verbs aligned with any deeper parity improvements in `QDES`, `MDES`, and `MAPGEN`.
