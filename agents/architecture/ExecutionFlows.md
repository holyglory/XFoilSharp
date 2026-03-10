# Execution Flows

## 1. Geometry import and normalization

1. CLI or session input selects a file or NACA designation.
2. `AirfoilParser.ParseFile` or `NacaAirfoilGenerator.Generate4Digit` creates `AirfoilGeometry`.
3. `AirfoilNormalizer.Normalize` is used where panel generation or normalized transforms require chord-based geometry.
4. Optional metrics are computed with `AirfoilMetricsCalculator.Calculate`.

## 2. Inviscid operating point

1. `AirfoilAnalysisService.AnalyzeInviscid`
2. `PanelMeshGenerator.Generate`
3. `HessSmithInviscidSolver.Prepare`
   - Builds reusable geometry-dependent influence data.
4. `HessSmithInviscidSolver.Analyze`
   - Combines X/Y freestream basis responses for angle of attack.
   - Computes tangential velocity and pressure samples.
   - Applies compressibility correction if `MachNumber > 0`.
   - Produces wake geometry through `WakeGeometryGenerator`.
5. `InviscidAnalysisResult`
   - Returned to CLI, design, or IO layers.

## 3. Inviscid sweeps and target-lift solves

1. `AirfoilAnalysisService.SweepInviscidAlpha`
2. `AirfoilAnalysisService.AnalyzeInviscidForLiftCoefficient`
3. `AirfoilAnalysisService.SweepInviscidLiftCoefficient`

## 4. Viscous preparation and laminar solve

1. Start from `InviscidAnalysisResult`.
2. `BoundaryLayerTopologyBuilder.Build`
3. `ViscousStateSeedBuilder.Build`
4. `ViscousStateEstimator.Estimate`
5. `ViscousIntervalSystemBuilder.Build`
6. `ViscousLaminarCorrector.Correct`
7. `ViscousLaminarSolver.Solve`

## 5. Viscous interaction and displacement coupling

1. `ViscousInteractionCoupler.Couple`
2. `DisplacementSurfaceGeometryBuilder.Build`
3. `AirfoilAnalysisService.AnalyzeDisplacementCoupledViscous`
4. `ViscousForceEstimator.EstimateProfileDragCoefficient`

## 6. Geometry design flow

1. CLI selects a geometry-edit command.
2. A design service transforms `AirfoilGeometry`.
3. `AirfoilDatExporter.Export` writes deterministic `.dat`.

Examples:

- `FlapDeflectionService.DeflectTrailingEdge`
- `TrailingEdgeGapService.SetTrailingEdgeGap`
- `LeadingEdgeRadiusService.ScaleLeadingEdgeRadius`
- `ContourEditService.AddPoint/MovePoint/DeletePoint/DoublePoint/RefineCorners`
- `ContourModificationService.ModifyContour`

## 7. QDES flow

1. `AirfoilAnalysisService.AnalyzeInviscid`
2. `QSpecDesignService.CreateFromInviscidAnalysis`
3. One of:
   - `Modify`
   - `Smooth`
   - `ForceSymmetry`
4. Optional inverse execution through `QSpecDesignService.ExecuteInverse`

## 8. MDES and direct MAPGEN flow

### Modal inverse

1. Build baseline and target `QSpecProfile`.
2. `ModalInverseDesignService.CreateSpectrum`
3. `Execute`, `PerturbMode`, or `ExecuteFromSpectrum`
4. Export resulting geometry.

### Direct conformal map generation

1. Build baseline and modified `QSpecProfile`.
2. `ConformalMapgenService.Execute`
   - Resamples profile to circle points.
   - Builds `Cn` coefficients from `Qspec`.
   - Optionally filters high modes.
   - Applies TE-gap Newton loop.
   - Optionally outer-solves TE angle.
   - Uses continuation for harder inverse edits.
3. Result geometry or coefficient CSV is exported.

## 9. IO and batch session flow

### Export

- `PolarCsvExporter.Export`
- `AirfoilDatExporter.Export`
- `LegacyPolarDumpArchiveWriter.Export`

### Import

- `LegacyPolarImporter.Import`
- `LegacyReferencePolarImporter.Import`
- `LegacyPolarDumpImporter.Import`

### Session

1. `AnalysisSessionRunner.Run`
2. Load manifest and geometry.
3. Dispatch sweep or import job by kind.
4. Write artifacts and summary JSON.

## 10. CLI orchestration pattern

`Program.cs` follows the same broad shape for most commands:

1. Parse raw strings.
2. Load or generate geometry.
3. Call one service or one orchestration flow.
4. Print a text summary.
5. Optionally export output artifacts.

## TODO

- Add a flow document for anything that eventually replaces original interactive menus.
- Document the exact coupling loop stopping conditions if `AirfoilAnalysisService` becomes more complex.
- Split CLI flow documentation by command family once `Program.cs` is decomposed.
