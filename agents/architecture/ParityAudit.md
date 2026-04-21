# C# Parity Audit Ledger

## Scope

- Date: March 14, 2026
- Scope: every non-generated `.cs` file under `src/` and `tests/`
- Excluded from this ledger: generated `obj/**/*.cs` files only

## Audit policy outcome

- Direct legacy-XFoil calculation paths now use the shared parity template in `LegacyPrecisionMath` and, where appropriate, shared generic float or double solver cores.
- Files that remain without parity hooks are audited no-action cases, not unreviewed gaps.
- No-action categories are:
  - DTO or state container files with no active arithmetic branch to split.
  - CLI or import or export code where parity is about parsing or formatting, not float-vs-double execution.
  - Modern alternative solver or geometry utilities that are not the direct legacy parity path and are intentionally kept as managed defaults or auxiliary tools.
  - Test files, which validate behavior but do not carry runtime parity logic.

## Shared parity template

- `src/XFoil.Solver/Numerics/LegacyPrecisionMath.cs`

## Production files audited

### `src/XFoil.Cli`

- Audited no action: `src/XFoil.Cli/Program.cs`
  - Command orchestration only. No legacy arithmetic kernel lives here.

### `src/XFoil.Core`

- Parity-threaded direct legacy path:
  - `src/XFoil.Core/Services/NacaAirfoilGenerator.cs`
- Audited no action:
  - `src/XFoil.Core/Diagnostics/CoreTrace.cs`
  - `src/XFoil.Core/Models/AirfoilFormat.cs`, `src/XFoil.Core/Models/AirfoilGeometry.cs`, `src/XFoil.Core/Models/AirfoilMetrics.cs`, `src/XFoil.Core/Models/AirfoilPoint.cs`
  - `src/XFoil.Core/Services/AirfoilMetricsCalculator.cs`, `src/XFoil.Core/Services/AirfoilNormalizer.cs`, `src/XFoil.Core/Services/AirfoilParser.cs`
  - These files are parsing, normalization, metrics, or data-model code; they are not the active legacy parity arithmetic branch.

### `src/XFoil.Design`

- Audited no action:
  - `src/XFoil.Design/Models/ConformalMapgenResult.cs`, `src/XFoil.Design/Models/ConformalMappingCoefficient.cs`, `src/XFoil.Design/Models/ContourEditResult.cs`, `src/XFoil.Design/Models/ContourModificationResult.cs`, `src/XFoil.Design/Models/CornerRefinementParameterMode.cs`, `src/XFoil.Design/Models/FlapDeflectionResult.cs`, `src/XFoil.Design/Models/GeometryScaleOrigin.cs`, `src/XFoil.Design/Models/GeometryScalingResult.cs`, `src/XFoil.Design/Models/LeadingEdgeRadiusEditResult.cs`, `src/XFoil.Design/Models/ModalCoefficient.cs`, `src/XFoil.Design/Models/ModalInverseExecutionResult.cs`, `src/XFoil.Design/Models/ModalSpectrum.cs`, `src/XFoil.Design/Models/QSpecEditResult.cs`, `src/XFoil.Design/Models/QSpecExecutionResult.cs`, `src/XFoil.Design/Models/QSpecPoint.cs`, `src/XFoil.Design/Models/QSpecProfile.cs`, `src/XFoil.Design/Models/TrailingEdgeGapEditResult.cs`
  - `src/XFoil.Design/Services/BasicGeometryTransformService.cs`, `src/XFoil.Design/Services/ConformalMapgenService.cs`, `src/XFoil.Design/Services/ContourEditService.cs`, `src/XFoil.Design/Services/ContourModificationService.cs`, `src/XFoil.Design/Services/FlapDeflectionService.cs`, `src/XFoil.Design/Services/GeometryScalingService.cs`, `src/XFoil.Design/Services/GeometryTransformUtilities.cs`, `src/XFoil.Design/Services/LeadingEdgeRadiusService.cs`, `src/XFoil.Design/Services/ModalInverseDesignService.cs`, `src/XFoil.Design/Services/QSpecDesignService.cs`, `src/XFoil.Design/Services/TrailingEdgeGapService.cs`
  - These are managed geometry-design or inverse-design workflows, not the direct legacy parity solve path. They stay on the managed implementation unless a specific legacy algorithm port is requested.

### `src/XFoil.IO`

- Audited no action:
  - `src/XFoil.IO/Models/AnalysisSessionArtifact.cs`, `src/XFoil.IO/Models/AnalysisSessionManifest.cs`, `src/XFoil.IO/Models/AnalysisSessionRunResult.cs`, `src/XFoil.IO/Models/LegacyMachVariationType.cs`, `src/XFoil.IO/Models/LegacyPolarColumn.cs`, `src/XFoil.IO/Models/LegacyPolarDumpExportResult.cs`, `src/XFoil.IO/Models/LegacyPolarDumpFile.cs`, `src/XFoil.IO/Models/LegacyPolarDumpGeometryPoint.cs`, `src/XFoil.IO/Models/LegacyPolarDumpOperatingPoint.cs`, `src/XFoil.IO/Models/LegacyPolarDumpSide.cs`, `src/XFoil.IO/Models/LegacyPolarDumpSideSample.cs`, `src/XFoil.IO/Models/LegacyPolarFile.cs`, `src/XFoil.IO/Models/LegacyPolarRecord.cs`, `src/XFoil.IO/Models/LegacyPolarTripSetting.cs`, `src/XFoil.IO/Models/LegacyReferencePolarBlock.cs`, `src/XFoil.IO/Models/LegacyReferencePolarBlockKind.cs`, `src/XFoil.IO/Models/LegacyReferencePolarFile.cs`, `src/XFoil.IO/Models/LegacyReferencePolarPoint.cs`, `src/XFoil.IO/Models/LegacyReynoldsVariationType.cs`, `src/XFoil.IO/Models/SessionAirfoilDefinition.cs`, `src/XFoil.IO/Models/SessionSweepDefinition.cs`
  - `src/XFoil.IO/Services/AirfoilDatExporter.cs`, `src/XFoil.IO/Services/AnalysisSessionRunner.cs`, `src/XFoil.IO/Services/LegacyPolarDumpArchiveWriter.cs`, `src/XFoil.IO/Services/LegacyPolarDumpImporter.cs`, `src/XFoil.IO/Services/LegacyPolarImporter.cs`, `src/XFoil.IO/Services/LegacyReferencePolarImporter.cs`, `src/XFoil.IO/Services/PolarCsvExporter.cs`
  - Legacy IO code already follows on-disk legacy formats directly through parsing and serialization; it does not need a float-thread parity branch in the same sense as the solver kernels.

### `src/XFoil.Solver/Models`

- Parity-threaded or parity-aware runtime state:
  - `src/XFoil.Solver/Models/AnalysisSettings.cs`
  - `src/XFoil.Solver/Models/InviscidSolverState.cs`
- Audited no action, container or result models:
  - `src/XFoil.Solver/Models/BoundaryLayerBranch.cs`, `src/XFoil.Solver/Models/BoundaryLayerCorrelationConstants.cs`, `src/XFoil.Solver/Models/BoundaryLayerProfile.cs`, `src/XFoil.Solver/Models/BoundaryLayerStation.cs`, `src/XFoil.Solver/Models/BoundaryLayerSystemState.cs`, `src/XFoil.Solver/Models/BoundaryLayerTopology.cs`
  - `src/XFoil.Solver/Models/DisplacementCoupledResult.cs`, `src/XFoil.Solver/Models/DragDecomposition.cs`
  - `src/XFoil.Solver/Models/InviscidAnalysisResult.cs`, `src/XFoil.Solver/Models/InviscidLiftSweepResult.cs`, `src/XFoil.Solver/Models/InviscidSolverType.cs`, `src/XFoil.Solver/Models/InviscidTargetLiftResult.cs`
  - `src/XFoil.Solver/Models/LinearVortexInviscidResult.cs`, `src/XFoil.Solver/Models/LinearVortexPanelState.cs`
  - `src/XFoil.Solver/Models/Panel.cs`, `src/XFoil.Solver/Models/PanelMesh.cs`, `src/XFoil.Solver/Models/PanelingOptions.cs`
  - `src/XFoil.Solver/Models/PolarPoint.cs`, `src/XFoil.Solver/Models/PolarSweepMode.cs`, `src/XFoil.Solver/Models/PolarSweepResult.cs`
  - `src/XFoil.Solver/Models/PreparedInviscidSystem.cs`, `src/XFoil.Solver/Models/PressureCoefficientSample.cs`, `src/XFoil.Solver/Models/TransitionInfo.cs`
  - `src/XFoil.Solver/Models/ViscousAnalysisResult.cs`, `src/XFoil.Solver/Models/ViscousBranchSeed.cs`, `src/XFoil.Solver/Models/ViscousBranchState.cs`, `src/XFoil.Solver/Models/ViscousConvergenceInfo.cs`, `src/XFoil.Solver/Models/ViscousCorrectionResult.cs`, `src/XFoil.Solver/Models/ViscousFlowRegime.cs`, `src/XFoil.Solver/Models/ViscousInteractionResult.cs`, `src/XFoil.Solver/Models/ViscousIntervalKind.cs`, `src/XFoil.Solver/Models/ViscousIntervalState.cs`, `src/XFoil.Solver/Models/ViscousIntervalSystem.cs`, `src/XFoil.Solver/Models/ViscousLiftSweepResult.cs`, `src/XFoil.Solver/Models/ViscousNewtonSystem.cs`, `src/XFoil.Solver/Models/ViscousPolarPoint.cs`, `src/XFoil.Solver/Models/ViscousPolarSweepResult.cs`, `src/XFoil.Solver/Models/ViscousSolveResult.cs`, `src/XFoil.Solver/Models/ViscousSolverMode.cs`, `src/XFoil.Solver/Models/ViscousStateEstimate.cs`, `src/XFoil.Solver/Models/ViscousStateSeed.cs`, `src/XFoil.Solver/Models/ViscousStationDerivedState.cs`, `src/XFoil.Solver/Models/ViscousStationSeed.cs`, `src/XFoil.Solver/Models/ViscousStationState.cs`, `src/XFoil.Solver/Models/ViscousTargetLiftResult.cs`
  - `src/XFoil.Solver/Models/WakeGeometry.cs`, `src/XFoil.Solver/Models/WakePoint.cs`
  - These files store state, configuration, or results. The parity policy is carried by the solver and numerics services that consume them.

### `src/XFoil.Solver/Numerics`

- Parity-threaded direct legacy math:
  - `src/XFoil.Solver/Numerics/LegacyPrecisionMath.cs`
  - `src/XFoil.Solver/Numerics/DenseLinearSystemSolver.cs`
  - `src/XFoil.Solver/Numerics/ScaledPivotLuSolver.cs`
  - `src/XFoil.Solver/Numerics/TridiagonalSolver.cs`
  - `src/XFoil.Solver/Numerics/ParametricSpline.cs`
  - `src/XFoil.Solver/Numerics/BlockTridiagonalSolver.cs`
- Audited no action:
  - `src/XFoil.Solver/Numerics/BandMatrixSolver.cs`
  - `src/XFoil.Solver/Numerics/CubicSpline.cs`
  - `BandMatrixSolver` is an optional alternate linear solver, not the direct XFoil parity path. `CubicSpline` is used by managed meshing utilities rather than the classic parity chain.

### `src/XFoil.Solver/Diagnostics`

- Audited no action:
  - `src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs`
  - Structured tracing and JSONL serialization only.

### `src/XFoil.Solver/Services`

- Parity-threaded direct legacy path:
  - `src/XFoil.Solver/Services/AirfoilAnalysisService.cs`
  - `src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs`
  - `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`
  - `src/XFoil.Solver/Services/CurvatureAdaptivePanelDistributor.cs`
  - `src/XFoil.Solver/Services/DragCalculator.cs`
  - `src/XFoil.Solver/Services/EdgeVelocityCalculator.cs`
  - `src/XFoil.Solver/Services/InfluenceMatrixBuilder.cs`
  - `src/XFoil.Solver/Services/LinearVortexInviscidSolver.cs`
  - `src/XFoil.Solver/Services/PanelGeometryBuilder.cs`
  - `src/XFoil.Solver/Services/PolarSweepRunner.cs`
  - `src/XFoil.Solver/Services/StagnationPointTracker.cs`
  - `src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs`
  - `src/XFoil.Solver/Services/TransitionModel.cs`
  - `src/XFoil.Solver/Services/ViscousNewtonAssembler.cs`
  - `src/XFoil.Solver/Services/ViscousNewtonUpdater.cs`
  - `src/XFoil.Solver/Services/ViscousSolverEngine.cs`
- Audited no action, modern or auxiliary paths:
  - `src/XFoil.Solver/Services/BoundaryLayerTopologyBuilder.cs`
  - `src/XFoil.Solver/Services/HessSmithInviscidSolver.cs`
  - `src/XFoil.Solver/Services/PanelMeshGenerator.cs`
  - `src/XFoil.Solver/Services/PostStallExtrapolator.cs`
  - `src/XFoil.Solver/Services/ViscousLaminarCorrector.cs`
  - `src/XFoil.Solver/Services/ViscousStateEstimator.cs`
  - `src/XFoil.Solver/Services/ViscousStateSeedBuilder.cs`
  - `src/XFoil.Solver/Services/WakeGapProfile.cs`
  - `src/XFoil.Solver/Services/WakeGeometryGenerator.cs`
  - `src/XFoil.Solver/Services/WakeSpacing.cs`
  - These files are managed helpers, deprecated surrogate remnants, or alternative analysis paths. They are intentionally kept outside the direct legacy parity branch unless a specific port target requires otherwise.

## Test files audited

- Audited no runtime parity action:
  - `tests/XFoil.Core.Tests/AirfoilAnalysisServiceTests.cs`, `tests/XFoil.Core.Tests/AirfoilDatExporterTests.cs`, `tests/XFoil.Core.Tests/AirfoilParserTests.cs`, `tests/XFoil.Core.Tests/AnalysisSessionRunnerTests.cs`
  - `tests/XFoil.Core.Tests/BasicGeometryTransformServiceTests.cs`, `tests/XFoil.Core.Tests/BlockTridiagonalSolverTests.cs`, `tests/XFoil.Core.Tests/BoundaryLayerCorrelationsTests.cs`, `tests/XFoil.Core.Tests/BoundaryLayerSystemAssemblerTests.cs`, `tests/XFoil.Core.Tests/BoundaryLayerTopologyTests.cs`
  - `tests/XFoil.Core.Tests/CompressibilityTests.cs`, `tests/XFoil.Core.Tests/ConformalMapgenServiceTests.cs`, `tests/XFoil.Core.Tests/ContourEditServiceTests.cs`, `tests/XFoil.Core.Tests/ContourModificationServiceTests.cs`, `tests/XFoil.Core.Tests/CurvatureAdaptivePanelDistributorTests.cs`
  - `tests/XFoil.Core.Tests/DiagnosticDumpTests.cs`, `tests/XFoil.Core.Tests/DragCalculatorTests.cs`
  - `tests/XFoil.Core.Tests/EdgeVelocityCalculatorTests.cs`, `tests/XFoil.Core.Tests/FlapDeflectionServiceTests.cs`
  - `tests/XFoil.Core.Tests/GeometryScalingServiceTests.cs`
  - `tests/XFoil.Core.Tests/InfluenceMatrixBuilderTests.cs`, `tests/XFoil.Core.Tests/InfluenceMatrixParityDiagnosticsTests.cs`, `tests/XFoil.Core.Tests/InviscidSolverTests.cs`, `tests/XFoil.Core.Tests/InitialInfluenceTraceTests.cs`
  - `tests/XFoil.Core.Tests/LeadingEdgeRadiusServiceTests.cs`, `tests/XFoil.Core.Tests/LegacyPolarDumpImporterTests.cs`, `tests/XFoil.Core.Tests/LegacyPolarImporterTests.cs`, `tests/XFoil.Core.Tests/LegacyReferencePolarImporterTests.cs`, `tests/XFoil.Core.Tests/LinearVortexInviscidSolverTests.cs`
  - `tests/XFoil.Core.Tests/ModalInverseDesignServiceTests.cs`
  - `tests/XFoil.Core.Tests/NacaAirfoilGeneratorTests.cs`, `tests/XFoil.Core.Tests/NewtonSystemIndexingTests.cs`, `tests/XFoil.Core.Tests/NormalizationAndMetricsTests.cs`
  - `tests/XFoil.Core.Tests/PanelGeometryBuilderTests.cs`, `tests/XFoil.Core.Tests/PanelMeshGeneratorTests.cs`, `tests/XFoil.Core.Tests/ParametricSplineTests.cs`, `tests/XFoil.Core.Tests/PolarCsvExporterTests.cs`, `tests/XFoil.Core.Tests/PolarParityTests.cs`, `tests/XFoil.Core.Tests/PolarSweepRunnerTests.cs`
  - `tests/XFoil.Core.Tests/QSpecDesignServiceTests.cs`
  - `tests/XFoil.Core.Tests/ScaledPivotLuSolverTests.cs`, `tests/XFoil.Core.Tests/StreamfunctionInfluenceCalculatorTests.cs`
  - `tests/XFoil.Core.Tests/TestDataPaths.cs`, `tests/XFoil.Core.Tests/TrailingEdgeGapServiceTests.cs`, `tests/XFoil.Core.Tests/TransitionModelPortTests.cs`, `tests/XFoil.Core.Tests/TransitionModelTests.cs`
  - `tests/XFoil.Core.Tests/ViscousInitialStateTests.cs`, `tests/XFoil.Core.Tests/ViscousParityTests.cs`, `tests/XFoil.Core.Tests/ViscousSolverEngineTests.cs`, `tests/XFoil.Core.Tests/ViscousStateSeedTests.cs`, `tests/XFoil.Core.Tests/WakeGeometryTests.cs`

## Verification used during audit completion

- `dotnet build /Users/slava/Agents/XFoilSharp/src/XFoil.Solver/XFoil.Solver.csproj -v minimal`
- `dotnet test /Users/slava/Agents/XFoilSharp/tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "Naca0012_Re1e6_Alpha0_WritesDiagnosticDump" -v minimal`

## Remaining parity boundary after the audit

- The broad C# file audit is complete.
- The solver still does not reach full managed-vs-Fortran parity.
- The active remaining boundary stays in the viscous legacy path, not in missing repository-wide float-thread coverage.
