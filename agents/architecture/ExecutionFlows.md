# Execution Flows

## 1. Geometry import and normalization

1. CLI or session input selects a file or NACA designation.
2. `AirfoilParser.ParseFile` or `NacaAirfoilGenerator.Generate4Digit` creates `AirfoilGeometry`.
3. `AirfoilNormalizer.Normalize` is used where panel generation or normalized transforms require chord-based geometry.
4. Optional metrics are computed with `AirfoilMetricsCalculator.Calculate`.

## 2. Single-point inviscid analysis

1. `AirfoilAnalysisService.AnalyzeInviscid`
2. Branch on `AnalysisSettings.InviscidSolverType`

### Hess-Smith branch

1. `PanelMeshGenerator.Generate`
2. `HessSmithInviscidSolver.Prepare`
3. `HessSmithInviscidSolver.Analyze`
4. `WakeGeometryGenerator.Generate`
5. Return `InviscidAnalysisResult`

### Linear-vortex branch

1. `CurvatureAdaptivePanelDistributor.Distribute`
2. `LinearVortexInviscidSolver` assembles or reuses the streamfunction system
3. `PanelGeometryBuilder` and `StreamfunctionInfluenceCalculator` provide geometry and influence data
4. `ScaledPivotLuSolver` solves the factored system
5. `AirfoilAnalysisService` adapts `LinearVortexInviscidResult` back into `InviscidAnalysisResult`

Current limitation:

- The public adapter still leaves `PressureSamples` and `WakeGeometry` empty on the linear-vortex path.

## 3. Public inviscid sweeps and target-lift solves

1. `AirfoilAnalysisService.SweepInviscidAlpha`
2. `AirfoilAnalysisService.AnalyzeInviscidForLiftCoefficient`
3. `AirfoilAnalysisService.SweepInviscidLiftCoefficient`

Current behavior:

- These public sweep helpers still use the prepared Hess-Smith path only.
- `InviscidSolverType.LinearVortex` currently affects single-point `AnalyzeInviscid`, not the sweep APIs.

## 4. Single-point viscous analysis

1. `AirfoilAnalysisService.AnalyzeViscous`
2. `ViscousSolverEngine.SolveViscous`
3. `CurvatureAdaptivePanelDistributor.Distribute`
4. `LinearVortexInviscidSolver.SolveAtAngleOfAttack`
5. `EdgeVelocityCalculator.SetInviscidSpeeds`
6. `InfluenceMatrixBuilder.BuildAnalyticalDIJ`
7. `ViscousNewtonAssembler.BuildNewtonSystem`
8. `BlockTridiagonalSolver.Solve`
9. `ViscousNewtonUpdater.ApplyNewtonUpdate`
10. `StagnationPointTracker.MoveStagnationPoint` when the stagnation panel shifts
11. `DragCalculator.ComputeDrag`
12. Optional `PostStallExtrapolator.ExtrapolatePostStall` if the Newton solve does not converge and post-stall fallback is enabled

Notes:

- The current viscous path always uses the linear-vortex inviscid front end, regardless of `InviscidSolverType`.
- `TransitionModel.CheckTransition` is called from the Newton assembly during the BL march.

## 5. Viscous polar sweeps

1. `AirfoilAnalysisService.SweepViscousAlpha`
2. `AirfoilAnalysisService.SweepViscousCL`
3. `AirfoilAnalysisService.SweepViscousRe`
4. All three delegate to `PolarSweepRunner`

Implementation detail:

- `PolarSweepRunner` reuses the panel geometry and factored inviscid system across points.
- The current warm-start mechanism records nearby alpha history, not a full boundary-layer state snapshot.

## 6. Topology and seed diagnostics

1. `AirfoilAnalysisService.AnalyzeBoundaryLayerTopology`
2. `BoundaryLayerTopologyBuilder.Build`
3. `AirfoilAnalysisService.AnalyzeViscousStateSeed`
4. `ViscousStateSeedBuilder.Build`
5. `AirfoilAnalysisService.AnalyzeViscousInitialState`
6. `ViscousStateEstimator.Estimate`

These remain useful for inspection-oriented CLI commands, but they are not the primary operating-point viscous solver path anymore.

## 7. Deprecated surrogate viscous pipeline

The old staged surrogate methods are still present on `AirfoilAnalysisService` for compatibility:

- `AnalyzeViscousIntervalSystem`
- `AnalyzeViscousLaminarCorrection`
- `AnalyzeViscousLaminarSolve`
- `AnalyzeViscousInteraction`
- `AnalyzeDisplacementCoupledViscous`
- Related displacement-coupled sweep helpers

Current behavior:

- These methods are marked obsolete and throw `NotSupportedException`.
- Matching CLI verbs print deprecation messages instead of running a solver.

## 8. Geometry design flow

1. CLI selects a geometry-edit command.
2. A design service transforms `AirfoilGeometry`.
3. `AirfoilDatExporter.Export` writes deterministic `.dat`.

Examples:

- `FlapDeflectionService.DeflectTrailingEdge`
- `TrailingEdgeGapService.SetTrailingEdgeGap`
- `LeadingEdgeRadiusService.ScaleLeadingEdgeRadius`
- `ContourEditService.AddPoint/MovePoint/DeletePoint/DoublePoint/RefineCorners`
- `ContourModificationService.ModifyContour`

## 9. QDES flow

1. `AirfoilAnalysisService.AnalyzeInviscid`
2. `QSpecDesignService.CreateFromInviscidAnalysis`
3. One of:
   - `Modify`
   - `Smooth`
   - `ForceSymmetry`
4. Optional inverse execution through `QSpecDesignService.ExecuteInverse`

## 10. MDES and direct MAPGEN flow

### Modal inverse

1. Build baseline and target `QSpecProfile`.
2. `ModalInverseDesignService.CreateSpectrum`
3. `Execute`, `PerturbMode`, or `ExecuteFromSpectrum`
4. Export resulting geometry.

### Direct conformal map generation

1. Build baseline and modified `QSpecProfile`.
2. `ConformalMapgenService.Execute`
3. Resample, solve coefficients, optionally filter high modes, and solve TE constraints
4. Export resulting geometry or coefficient CSV

## 11. IO and batch session flow

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

## 12. CLI orchestration pattern

`Program.cs` follows the same broad shape for most commands:

1. Parse raw strings.
2. Load or generate geometry.
3. Call one service or one orchestration flow.
4. Print a text summary.
5. Optionally export output artifacts.

Important current discrepancy:

- The viscous CLI still parses surrogate-era knobs such as coupling iterations, viscous iterations, residual tolerance, and displacement relaxation.
- `CreateViscousSettings` currently wires only panel count, Mach, Reynolds, transition Reynolds-theta, and critical `N`, so those extra arguments are ignored.

## 13. Repo automation flow

1. Machine bootstrap installs `autonomous-loop` and writes `~/.codex/` machine config plus global hooks and skill.
2. Repo-local `.codex/autoloop.project.json` declares the build and test gates for this checkout.
3. Repo-local `.codex/hooks.json` points Codex session hooks at the machine-installed CLI path.
4. Repo-local `.agents/skills/autonomous-loop/SKILL.md` tells agents how to enable and operate the loop when the user requests it.
5. `autonomous-loop doctor --cwd <repo>` validates the combined machine-plus-repo setup.

## TODO

- Remove or rewire the deprecated viscous CLI verbs once migration decisions are final.
- Document the warm-start flow in more detail if `PolarSweepRunner` starts carrying real BL snapshots.
