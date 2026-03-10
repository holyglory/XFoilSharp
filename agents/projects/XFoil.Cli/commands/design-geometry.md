# CLI Design Geometry Commands

- File: `src-cs/XFoil.Cli/Program.cs`

## Commands

- `design-flap-*`
- `set-te-gap-*`
- `set-le-radius-*`
- `scale-geometry-*`
- `adeg-*`
- `arad-*`
- `tran-*`
- `scal-*`
- `lins-*`
- `dero-*`
- `unit-*`
- `addp-*`
- `movp-*`
- `delp-*`
- `corn-*`
- `cadd-*`
- `modi-*`

## Main helpers

- `ExportFlapGeometry`
- `ExportTrailingEdgeGapGeometry`
- `ExportLeadingEdgeRadiusGeometry`
- `ExportScaledGeometry`
- `ExportGeometry`
- `ExportContourEditGeometry`
- `ExportContourModificationGeometry`

## TODO

- Add any future interactive geometry shell replacement outside this one-shot command model.
