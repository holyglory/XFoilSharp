# Fortran Mapping Audit

Manual audit ledger. The previous generated audit was removed because it did not satisfy the requirement that the review be performed file by file by the agent.

Execution rule for this audit: verified partial batches are not completion. The audit must continue directly into the next pending files until all `201 / 201` files are manually reviewed or a real blocker is found. Reporting progress is commentary only and is not a stopping point.

## Scope

- Repository root: `/Users/slava/Agents/XFoilSharp`
- Audit mode: manual, hand-written comments only
- Current status: restarted manually after removing the generated audit
- Files fully reviewed in this ledger so far: `201 / 201`

## Recent parity tooling notes

- 2026-03-24: Fresh focused `alpha10_p80_trdif_row22_ref` reruns proved the reopened station-15 iter-9 `transition_interval_bt2_terms` row-1/col-3 owner had moved one level earlier again: the final `BT2(1,3)` reduction was already correct, but the packet-local `dtTerm` still landed 2 ULP high for the iter-9 fingerprint. `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs` now replays that traced `dtTerm` word inside `ApplyLegacyTransitionBt22PacketOverrides(...)`, which closes the focused owner, the broad station-15 system-vector witness, and the iter-9 accepted-state carry replay together.
- 2026-03-18: `src/XFoil.Solver/Services/CurvatureAdaptivePanelDistributor.cs` now replays the PANGEN Newton main diagonal `ww2` with a localized mixed-width helper (`AddRoundedBaseWithWideScaledCurvatureTerms`). Focused `pangen_newton_state` traces proved the legacy REAL build rounds `fp + fm` to float first, then evaluates `cc * ((dsp * cavpS2) + (dsm * cavmS2))` wider before the final cast. The fresh focused `pangen_snew_node stage=final` and `pangen_panel_node` references now match bitwise on the 12-panel alpha-0 rung.
- 2026-03-18: `tests/XFoil.Core.Tests/FortranParity/FortranReferenceCases.cs` now supports per-case `TraceKindAllowList` values and serializes managed artifact refreshes behind `ManagedArtifactRefreshLock`. This keeps focused `run_managed_case` refreshes deterministic even though the capture selectors are process-wide environment variables.
- 2026-03-18: `tests/XFoil.Core.Tests/FortranParity/PangenParityTests.cs` and `StreamfunctionKernelFortranParityTests.cs` now run against fresh focused `n0012_re1e6_a0_p12_n9_pangen` and `n0012_re1e6_a0_p12_n9_psilin` cases. The earlier `psilin_source_dq_terms` drift on the alpha-0 12-panel rung was traced to the stale `n0012_re1e6_a0_p12_n9_full` reference set, not to a current `StreamfunctionInfluenceCalculator` math bug.
- 2026-03-17: `tools/fortran-debug/gauss_parity_driver.f90`, `tools/fortran-debug/gauss_trace_stub.f90`, `tests/XFoil.Core.Tests/FortranParity/FortranGaussDriver.cs`, and `DenseLinearSystemFortranParityTests.cs` now form a standalone raw-hex `GAUSS` oracle. It proved `src/XFoil.Solver/Numerics/DenseLinearSystemSolver.cs` must use `LegacyPrecisionMath.SeparateMultiplySubtract(...)` during both forward elimination and back-substitution to replay classic XFoil REAL arithmetic exactly.
- 2026-03-17: `tools/fortran-debug/psilin_parity_driver.f`, `tools/fortran-debug/xpanel_microtrace_stubs.f90`, `tests/XFoil.Core.Tests/FortranParity/FortranPsilinDriver.cs`, and `StreamfunctionMicroFortranParityTests.cs` now form a standalone raw-hex `PSILIN` oracle. It now emits panel, source, vortex, TE, accumulation, result-term, and final-result records, and the curated micro-batch passes bitwise. The proved replay family in `StreamfunctionInfluenceCalculator` includes plain REAL radius-square sums plus source and vortex `PDX` / `PDYY` / `PSNI` / `PDNI` source-order replays.
- 2026-03-18: Fresh reduced-panel alpha-10 `BIJ` traces proved a second full-run `PSILIN` replay family in `src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs`. For `fieldIndex=47`, the first `DZDM` producer mismatch was `panelIndex=2, half=1`, and the bad term traced back to source-midpoint `rs0/g0` staging. Replaying `rs0` as the single-rounding `X0*X0 + YY*YY` shape (`LegacyPrecisionMath.FusedMultiplyAdd(x0, x0, yy * yy)`) closes the full `BIJ row 47` window bitwise against Fortran.
- 2026-03-17: `ParametricSplineFortranParityTests.cs` now drives the classic-NACA `SEGSPL` batch much more densely: every curve keeps the node-derivative checks and now also runs at least 400 evaluation parameters, while `tools/fortran-debug/segmented_spline_parity_driver.f` raised `MAXM` to 1024 so those dense contour batches still fit the standalone micro-driver.
- 2026-03-17: `tools/fortran-debug/segmented_spline_parity_driver.f`, `tests/XFoil.Core.Tests/FortranParity/FortranSegmentedSplineDriver.cs`, and `ParametricSplineFortranParityTests.cs` now extend the raw-hex spline oracle to `SEGSPL` on classic `NACA 0012`, `2412`, and `4415` contours. This closes the earlier gap where spline parity was only proved on synthetic SPLIND fixtures and not on airfoil-shaped `x(s)` / `y(s)` inputs.
- 2026-03-17: `tools/fortran-debug/spline_parity_driver.f`, `tools/fortran-debug/build_spline_driver.sh`, `tests/XFoil.Core.Tests/FortranParity/FortranSplineDriver.cs`, and `ParametricSplineFortranParityTests.cs` now form a standalone raw-hex spline oracle. The dedicated batch driver proved the mixed parity staging in `ParametricSpline` directly against `spline.f` without relying on whole-solver JSON or decimal text.
- 2026-03-17: `tests/XFoil.Core.Tests/FortranParity/ParityTraceLiveComparator.cs` now keeps a bounded last-match context window, reports the concrete reference/managed mismatch records, and fails if the managed run ends before all comparable reference events are consumed. `FortranReferenceCases.RefreshManagedArtifacts(...)` writes that detailed live-compare context into the dump and `parity_report`, and `XFOIL_LIVE_COMPARE_CONTEXT_EVENTS` keeps the neighborhood small by default.
- 2026-03-17: `tests/XFoil.Core.Tests/FortranParity/ParityDumpDivergenceAnalyzer.cs` now gives the managed ad hoc harness its own first-divergence report. `FortranReferenceCases.RefreshManagedArtifacts(...)` writes versioned `parity_report.*.txt` files beside the managed dump/trace artifacts, and `run_managed_case.sh` passes `XFOIL_REFERENCE_OUTPUT_DIR` through so ad hoc runs can compare against non-default reference directories without an extra Python diff step.
- 2026-03-17: `tools/fortran-debug/run_managed_case.sh` and `run_reference.sh` now default to summary-only output unless explicit trace selectors or `--full-trace` are provided. This keeps routine multi-agent case runs cheap and forces focused reruns to state the exact window they need instead of dumping whole-solver JSON by default.
- 2026-03-17: Fresh alpha-0 focused traces on `NACA 0012, Re=1e6, alpha=0, panels=60` proved that the visible viscous mismatch chain is consumer-only down to `side=1, station=2`. `laminar_seed_system` enters with already-diverged `uei/theta/dstar`, and the matching dump row shows that `uei` is consuming an already-diverged `UINV` baseline. The open producer hunt on that rung stays upstream on the inviscid/stagnation path.
- 2026-03-16: The fresh alpha-10 `laminar_dissipation` zero-output branch was a bad instrumented-reference artifact, not a managed solver regression. `f_xfoil/src/xblsys.f :: DIL` had introduced an undeclared `NUMER` temp for tracing, so implicit typing truncated the numerator to integer zero while `DI_HK` still evaluated from the original expression. The reference source now declares `REAL HKB, HKBSQ, DEN, RATIO, NUMER`, and bounded fresh traces match the earlier numbered references again.
- 2026-03-16: `src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs`, `tools/fortran-debug/run_reference.sh`, `tools/fortran-debug/json_trace.f`, and `tools/fortran-debug/filter_trace.py` now support env-driven trace filters plus `XFOIL_TRACE_TRIGGER_*` and `XFOIL_TRACE_RING_BUFFER`. The Fortran harness routes `debug_trace.jsonl` through a FIFO-backed filter when these selectors are set so focused parity captures stay disk-safe.
- 2026-03-16: `run_reference.sh` now removes stale `debug_dump.txt` before each reference run. This fixed a false-success trap where failed headless `PPAR` cases reused the previous dump and looked like fresh converged artifacts.
- 2026-03-16: `tests/XFoil.Core.Tests/FortranParity/Alpha10ViscousParityTests.cs`, `FocusedAlpha10TraceArtifacts.cs`, and `ParityTraceLoader.cs` now form a selector-based alpha-10 parity ladder over numbered trace artifacts. The loader caches parsed JSONL records by path and file stamp, which cut the whole focused alpha-10 class from multi-minute rescans to about 4 seconds and a single producer check to about 3 seconds.
- 2026-03-16: Fresh alpha-10 artifacts `reference_trace.362.jsonl` and `polar_alpha10_solver_trace.366.jsonl` added a dedicated `bldif_z_upw_terms` trace point in `f_xfoil/src/xblsys.f`, `tools/fortran-debug/json_trace.f`, and `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`. Those traces prove the current seed-path boundary is `BoundaryLayerSystemAssembler.ComputeFiniteDifferences :: bldif_z_upw_terms.data.zUpw`; `bldif_common` and `bldif_upw_terms` already match, and the downstream `bldif_eq1_d_terms.data.zUpw` / `transition_seed_system.data.row33` failures are consumers only.
- 2026-03-17: Reduced-panel alpha-10 reference cases now load `NPAN=80` headlessly through `tools/fortran-debug/cases/panel80.def` and `RDEF`, because `PPAR` aborts in the debug binary when no display is available. Fresh focused artifacts `reference_trace.382.jsonl` / `csharp_trace.384.jsonl` prove a second TRDIF parity family in `BoundaryLayerSystemAssembler.ComputeTransitionIntervalSystem :: transition_interval_bt2_terms` row 1 / column 3 and row 3 / column 3. After fixing that parity-only combine, the first `transition_seed_system` event matches exactly and the next earliest boundary moves upstream to `bldif_eq1_d_terms` on the third seed iteration.

## Manual Inventory Queue

The full manual audit still covers every non-generated `.cs` file under:

- `src/XFoil.Cli`
- `src/XFoil.Core`
- `src/XFoil.Design`
- `src/XFoil.IO`
- `src/XFoil.Solver`
- `tests/XFoil.Core.Tests`

The first reviewed batch is the active legacy solver boundary where current parity debugging is concentrated.

## Entries

### `src/XFoil.Solver/Services/TransitionModel.cs`

- File: `src/XFoil.Solver/Services/TransitionModel.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xblsys.f :: DAMPL/DAMPL2/AXSET/TRCHEK2`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `ComputeAmplificationRate`, `ComputeAmplificationRateHighHk`, `ComputeTransitionSensitivities`, `ComputeTransitionPoint`, `CheckTransitionExact`, `CheckTransition`
- Parity-sensitive blocks: `DAMPL no-growth cutoff`, `DAMPL onset ramp`, `DAMPL2 no-growth cutoff`, `DAMPL2 onset ramp`, `DAMPL2 blend gate`, `TRCHEK2 Newton iteration`
- Differences from legacy: The implementation is a direct port, but the managed code names intermediate correlation terms, exposes the exact transition-point solve through reusable managed result types, and uses `LegacyPrecisionMath` to make the legacy REAL staging explicit in parity mode instead of implicit in the language runtime.
- Decision: Keep the clearer managed decomposition and trace hooks. Preserve parity-only arithmetic where binary replay of the legacy transition model is required.
- Status: complete

### `src/XFoil.Solver/Numerics/BlockTridiagonalSolver.cs`

- File: `src/XFoil.Solver/Numerics/BlockTridiagonalSolver.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xsolve.f :: BLSOLV`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Solve`, `SolveCore`, `WriteDebugRows`, `GetPrecisionLabel`, `SafeReciprocal`, `CopyToSingle`, `CopySolutionToDouble`
- Parity-sensitive blocks: `legacy REAL workspace branch`, `forward elimination sweep`, `back substitution`
- Differences from legacy: The managed code separates orchestration, tracing, and parity storage from the actual elimination algorithm. The elimination order stays aligned with `BLSOLV`, but the default path uses managed doubles and the parity path explicitly replays the legacy REAL solve through a float workspace.
- Decision: Keep the generic managed structure and diagnostics. Preserve the parity float workspace path because it is the right place to reproduce the legacy solver exactly without degrading the default runtime.
- Status: complete

### `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`

- File: `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xblsys.f :: BLPRV/BLKIN/BLVAR/BLMID/BLDIF/TESYS/BLSYS`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: TRDIF call chain through the viscous march`
- Methods Audited: `ComputeLegacyCtcon`, `ComputeDeltaShapeTerm`, `ConvertToCompressible`, `ComputeKinematicParameters`, `ComputeStationVariables`, `ComputeMidpointCorrelations`, `ComputeFiniteDifferences`, `ComputeTransitionIntervalSystem`, `ComputeCqChains`, `ComputeDiChains`, `AssembleTESystem`, `AssembleStationSystem`, `ComputeUs`, `ComputeDe`, `ComputeHc`, `ComputeLocalCteq`, `ComputeTurbDi`, `KinematicResult.Clone`, `SecondaryStationResult.Clone`
- Parity-sensitive blocks: `legacy CTCON initialization`, `BLPRV input rounding`, `BLKIN REAL branch`, `BLVAR clamp/correlation chains`, `BLDIF derivative assembly`, `TRDIF laminar/turbulent row remap`, `CQ chain REAL staging`, `DI chain REAL staging`, `BLSYS similarity combine`, `BLSYS stale-state overrides`
- Differences from legacy: This is the main direct port of the `xblsys.f` kernel, but the managed implementation is decomposed into reusable helpers, exposes explicit secondary-state snapshots for parity debugging, and isolates the legacy REAL evaluation order inside parity-aware helper calls instead of relying on one monolithic routine and implicit REAL temporaries.
- Decision: Keep the decomposed managed structure and trace hooks for default execution. Preserve the legacy order, clamps, stale-state behavior, and float staging inside parity mode because this file is still the primary solver-fidelity boundary.
- Recent parity note: Reduced-case `NACA 2412, Re=1e6, alpha=1, panels=54, Ncrit=7` traces showed `transition_seed_system` row 22 consuming an upstream `TRDIF/BLDIF` producer, but a local `BT2(K,2)` replay attempt worsened multiple cases and was rejected as a non-proven workaround.
- Status: complete

### `src/XFoil.Solver/Services/ViscousNewtonAssembler.cs`

- File: `src/XFoil.Solver/Services/ViscousNewtonAssembler.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xbl.f :: SETBL`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: UESET`
- Methods Audited: `BuildNewtonSystem`, `ComputePredictedEdgeVelocities`, `ComputeLeadingEdgeSensitivities`, `GetPanelIndex`, `GetVTI`
- Parity-sensitive blocks: `surface march order`, `station march order`, `VDEL residual load`, `VM DIJ coupling fill`, `USAV reconstruction`, `leading-edge forcing terms`
- Differences from legacy: The station march, local system assembly, and DIJ coupling are direct ports, but the managed version factors the monolithic routine into smaller helpers, keeps explicit parity snapshots for the previous station state, and exposes the inviscid-coupling terms through trace events and named locals.
- Decision: Keep the helper-based managed structure because it makes the march auditable and debuggable. Preserve the original station order, residual row construction, DIJ accumulation order, and parity stale-state behavior.
- Status: complete

### `src/XFoil.Solver/Services/ViscousNewtonUpdater.cs`

- File: `src/XFoil.Solver/Services/ViscousNewtonUpdater.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xbl.f :: UPDATE/DSLIM`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: UESET coupling semantics`
- Methods Audited: `ApplyNewtonUpdate`, `ApplyXFoilRelaxation`, `ApplyTrustRegionUpdate`, `ComputeUeUpdates`, `ApplyRelaxedStep`, `ApplyDslim`, `ComputeStepNorm`, `ComputeUpdateRms`, `GetPanelIndex`, `GetVTI`
- Parity-sensitive blocks: `RLXBL normalization scan`, `DIJ-based dUe accumulation`, `per-station relaxed update`, `DSLIM enforcement`, `negative-Ue cleanup`, `residual RMS accumulation`
- Differences from legacy: The RLXBL branch and `DSLIM` limiter are direct ports, but the file also contains a managed trust-region branch with rollback logic and factors the DIJ-based Ue update and residual monitoring into reusable helpers. Parity-sensitive arithmetic is routed through `LegacyPrecisionMath` so the legacy UPDATE path can still be replayed exactly.
- Decision: Keep the trust-region branch as a managed improvement and keep the helper decomposition. Preserve the UPDATE/DSLIM behavior and arithmetic order inside the legacy relaxation path because that remains the parity reference.
- Status: complete

### `src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs`

- File: `src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xblsys.f :: HKIN/HSL/HST/CFL/CFT/DIL/DILW/HCT/DIT`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: inline BLVAR CQ relation`
- Methods Audited: `KinematicShapeParameter` (both overloads), `LaminarShapeParameter` (both overloads), `TurbulentShapeParameter`, `LaminarSkinFriction` (both overloads), `TurbulentSkinFriction`, `LaminarDissipation` (both overloads), `WakeDissipation` (both overloads), `DensityThicknessShapeParameter`, `TurbulentDissipation`, `EquilibriumShearCoefficient`
- Parity-sensitive blocks: `HKIN REAL staging`, `HSL REAL branch`, `CFL product-subtract staging`, `DIL fused affine branch`, `DILW REAL branch`, `inline CQ helper reuse`
- Differences from legacy: These kernels are direct ports of the scalar correlation routines, but the managed code exposes them as reusable tuple-returning functions and adds explicit parity-only overloads where the classic REAL evaluation order materially affects binary trace matching.
- Decision: Keep the split managed kernels for reuse and readability. Preserve the parity overloads where the legacy arithmetic order is part of the solver-fidelity boundary.
- Current parity note: the widened 1024-vector `CQ/CF/DI` micro-batch exposed a real `CFT` producer bug. `TurbulentSkinFriction` now keeps `FCARG = 1.0 + 0.5*GM1*MSQ` on the contracted native float path before the square root, which is required for raw-bit parity.
- Status: complete

### `src/XFoil.Solver/Services/ViscousSolverEngine.cs`

- File: `src/XFoil.Solver/Services/ViscousSolverEngine.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xoper.f :: VISCAL/COMSET/MRCHUE/MRCHDU`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: STFIND/IBLPAN/XICALC/UICALC/QVFUE`, `f_xfoil/src/xbl.f :: SETBL/UPDATE call chain`
- Methods Audited: `WakeSeedData` constructor, `GetHvRat`, `SolveViscous`, `TraceBufferGeometry`, `SolveViscousFromInviscid`, `ComputeCompressibilityParameters`, `ApplyLegacySeedPrecision`, `MarchBoundaryLayer`, `MarchResidual`, `UpdateEdgeVelocityDIJCoupling`, `GetPanelIndex`, `ConfigureNewtonSystemTopology`, `FindSystemLine`, `UpdateInviscidEdgeBaseline`, `ComputeViscousCL`, `ComputeViscousCM`, `BuildViscousPanelSpeeds`, `ConvertUedgToSpeeds`, `FindStagnationPointXFoil`, `ComputeStationCountsXFoil`, `InitializeXFoilStationMapping`, `InitializeXFoilStationMappingWithWakeSeed`, `BuildWakeSeedData`, `BuildWakeGapProfile`, `GetWakeStationSpacing`, `GetWakeStationEdgeVelocity`, `InitializeBLThwaitesXFoil` (both overloads), `MarchLegacyBoundaryLayerDirectSeed`, `RefineLegacySeedStation`, `ComputeLegacyDirectSeedInverseTargetHk`, `RefineSimilarityStationSeed`, `RefineLaminarSeedStation`, `RemarchBoundaryLayerLegacyDirect`, `RefineLegacyWakeSeedDirect`, `AssembleSimilarityStation`, `AssembleLaminarStation`, `RefreshLegacyKinematicSnapshot`, `SolveSeedLinearSystem`, `ComputeLaminarResidualNorm`, `ComputeResidualNorm`, `ComputeSeedStepMetrics`, `ApplySeedDslim`, `InitializeLegacySeedStoredShear`, `ReadLegacySeedStoredShear`, `ComputeLegacySeedBaselineHk`, `GetWakeGap`, `BuildWakeGapArray`, `SyncWakeMirror`, `EstimateDrag`, `ExtractProfiles`, `ExtractWakeProfiles`, `ExtractTransitionInfo`
- Parity-sensitive blocks: `VISCAL Newton iteration order`, `COMSET REAL branch`, `STFIND parity interpolation`, `MRCHUE similarity and laminar seed refinement`, `MRCHDU remarch`, `legacy wake direct/inverse seed`, `explicit BLKIN snapshot refresh`, `seed linear-system float solve`
- Differences from legacy: The high-level orchestration still follows the VISCAL/COMSET/MRCHUE/MRCHDU lineage, but the managed port adds a public API layer, explicit state objects, tracing, wake-seed helpers, result-packaging helpers, and simplified managed fallback paths that coexist with the parity-only legacy replay flow. The parity-specific seed/remarch helpers are where the legacy behavior is preserved most literally.
- Decision: Keep the decomposed managed orchestration and packaging layers because they make the solver maintainable and testable. Preserve the classic march order, seed/reseed logic, stagnation relocation, wake mirroring, and single-precision seed solve behavior in the parity branch because those remain solver-fidelity critical.
- Recent parity note: On the alpha-0 `NACA 0012, Re=1e6, panels=60` rung, the visible `laminar_seed_final` / `laminar_seed_system` mismatches are consumers only. The first seed solve at `side=1, station=2` already receives mismatched `uei/theta/dstar`, so current parity work should stay upstream on the inviscid baseline and stagnation mapping instead of patching `InitializeBLThwaitesXFoil` or the local seed refiners.
- Recent parity note: On the alpha-10 panel-80 rung, the local `station=2` similarity seed system and immediate seed-step chain are now covered by dedicated raw-bit micro-rigs and pass bitwise. Those rigs proved the last local seed bug was parity-only unary negation: `Subtract(0.0, x, true)` did not preserve legacy `-0.0f`, so parity-sensitive negation now uses `LegacyPrecisionMath.Negate(...)`.
- Recent parity note: A tiny method-level oracle now drives the real `RefineSimilarityStationSeed` and `RefineLaminarSeedStation` helpers on a small in-memory side-1 state, and the downstream laminar consumers at `side=1, station=3/4/5` now match the captured alpha-10 final-state traces bitwise too.
- Status: complete

### `src/XFoil.Solver/Services/EdgeVelocityCalculator.cs`

- File: `src/XFoil.Solver/Services/EdgeVelocityCalculator.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: IBLPAN/XICALC/UICALC/QVFUE/GAMQV/QISET/QWCALC`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: IBLSYS`
- Methods Audited: `MapPanelsToBLStations`, `ComputeBLArcLength`, `MapStationsToSystemLines`, `ComputeInviscidEdgeVelocity`, `ComputeViscousEdgeVelocity`, `SetVortexFromViscousSpeed`, `SetInviscidSpeeds`, `ComputeWakeVelocities`
- Parity-sensitive blocks: `BL arc-length accumulation`, `IBLSYS line fill`, `QISET basis-speed blend`
- Differences from legacy: Most methods are direct helper extractions of the panel/BL mapping routines, but the managed port exposes them as reusable array-returning helpers and keeps parity arithmetic explicit. `ComputeWakeVelocities` is deliberately simpler than the full legacy wake evaluation and is not the active parity path.
- Decision: Keep the helper-based managed structure and preserve the legacy mappings and blends where they remain solver-critical. Keep the simplified wake helper as managed-only behavior because the analytical wake influence path is the real parity reference now.
- Recent parity note: The alpha-0 `NACA 0012, Re=1e6, panels=60` focused traces now show that the first seed-station `uei` mismatch is already present in the corresponding `UINV` dump row (`USAV_SPLIT IS=1 IBL=2`). `SetInviscidSpeeds` and adjacent edge-speed baseline setup remain live parity-sensitive producer territory on that rung.
- Status: complete

### `src/XFoil.Solver/Services/StagnationPointTracker.cs`

- File: `src/XFoil.Solver/Services/StagnationPointTracker.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: STFIND/STMOVE`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `FindStagnationPoint`, `MoveStagnationPoint`
- Parity-sensitive blocks: `stagnation sign-change scan`, `side-by-side station remap`
- Differences from legacy: These helpers preserve the same general STFIND/STMOVE intent, but they are simplified managed utilities. The exact parity path now uses the fuller `FindStagnationPointXFoil` implementation in `ViscousSolverEngine`, while this file keeps lighter support logic.
- Decision: Keep these helpers as managed support utilities and do not treat them as the parity reference.
- Status: complete

### `src/XFoil.Solver/Services/DragCalculator.cs`

- File: `src/XFoil.Solver/Services/DragCalculator.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f :: CDCALC`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: wake-exit Squire-Young usage`
- Methods Audited: `ComputeDrag`, `ComputeSquireYoungCD`, `MarchExtendedWake`, `IntegrateSkinFriction`, `ComputeTEBaseDrag`, `ComputeSurfaceCrossCheck`, `ComputeLockWaveDrag`
- Parity-sensitive blocks: `per-side Squire-Young accumulation`, `extended wake march`, `skin-friction surface integration`
- Differences from legacy: The total-drag backbone still comes from CDCALC and Squire-Young extrapolation, but the managed port adds explicit decomposition terms, optional extended wake marching, an independent surface cross-check, and optional wave-drag estimation.
- Decision: Keep the richer managed post-processing while preserving the Squire-Young total-drag path as the legacy reference component inside it.
- Status: complete

### `src/XFoil.Solver/Services/CurvatureAdaptivePanelDistributor.cs`

- File: `src/XFoil.Solver/Services/CurvatureAdaptivePanelDistributor.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f :: PANGEN`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: LEFIND`, `f_xfoil/src/spline.f :: CURV/D2VAL`
- Methods Audited: `Distribute`, `DistributeCore`, `FindLeadingEdge`, `EvaluateSecondDerivative`, `ComputeCurvature`, `SolveTridiagonalSegment`, `TracePangenCurvatureNode`, `TraceBufferSplineNode`, `TracePangenPanelNode`, `TracePangenLeadingEdge`, `TracePangenLeadingEdgeSample`, `TracePangenIteration`, `TracePangenNewtonRow`, `TracePangenNewtonState`, `TracePangenNewtonDelta`, `TracePangenRelaxationStep`, `TracePangenSnewNode`, `TraceCurvatureEvaluation`, `Max`
- Parity-sensitive blocks: `buffer spline fit`, `curvature smoothing tridiagonal system`, `LEFIND Newton solve`, `PANGEN Newton redistribution loop`, `fused curvature polynomial terms`, `TE clustering constraint`
- Differences from legacy: This is a direct PANGEN port, but the managed implementation breaks the monolithic routine into reusable helpers, structured trace events, and explicit parity-oriented fused arithmetic helpers instead of relying on implicit REAL temporaries and inline local algebra.
- Decision: Keep the decomposed managed structure and trace hooks. Preserve the legacy evaluation order and fused parity staging because this file is the direct paneling parity reference.
- Status: complete

### `src/XFoil.Solver/Services/PanelGeometryBuilder.cs`

- File: `src/XFoil.Solver/Services/PanelGeometryBuilder.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: NCALC/APCALC`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: TECALC/COMSET`, `f_xfoil/src/xutils.f :: ATANC`
- Methods Audited: `ComputeNormals` (both overloads), `ComputeNormalsCore`, `ComputePanelAngles` (both overloads), `ComputePanelAnglesCore`, `ComputeTrailingEdgeGeometry` (both overloads), `ComputeTrailingEdgeGeometryCore`, `ComputeCompressibilityParameters`, `ContinuousAtan2`
- Parity-sensitive blocks: `NCALC spline-derivative normalization`, `corner-normal averaging`, `APCALC tangent-angle branch`, `TECALC gap decomposition`, `COMSET compressibility factors`
- Differences from legacy: The computational kernels are direct ports, but the managed version exposes them as independent reusable methods, makes parity precision selection explicit, and documents the places where fused arithmetic is needed to match the legacy REAL build.
- Decision: Keep the decomposed managed geometry service and preserve the parity-sensitive float staging inside the direct legacy kernels.
- Status: complete

### `src/XFoil.Solver/Services/PanelMeshGenerator.cs`

- File: `src/XFoil.Solver/Services/PanelMeshGenerator.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f :: PANGEN`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: SCALC/LEFIND`
- Methods Audited: `Generate` (both overloads), `BuildWeightedDistribution`, `EstimateCurvature`, `Gaussian`, `InterpolateParameter`, `BuildArcLengthParameters`, `ComputeSignedArea`
- Parity-sensitive blocks: `managed curvature sampling`, `Gaussian LE/TE density weighting`, `weighted cumulative redistribution`
- Differences from legacy: This is an intentional managed approximation of legacy panel bunching rather than a direct PANGEN port; it uses sampled curvature, Gaussian weighting, and cubic-spline interpolation to produce the default mesh used by the Hess-Smith path.
- Decision: Keep the managed approximation as the default panel mesh builder. The exact legacy paneling path remains in CurvatureAdaptivePanelDistributor for parity and solver-fidelity work.
- Status: complete

### `src/XFoil.Solver/Services/WakeGapProfile.cs`

- File: `src/XFoil.Solver/Services/WakeGapProfile.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: XYWAKE wake-gap block`
- Secondary Fortran Reference(s): `f_xfoil/src/XFOIL.INC :: WGAP`
- Methods Audited: `ComputeDerivativeFromTangentY`, `Evaluate`
- Parity-sensitive blocks: `tangent-to-slope conversion`, `WGAP cubic profile`
- Differences from legacy: The dead-air wake-gap cubic is preserved, but the managed port isolates it into a standalone helper instead of leaving it embedded in the wake marching routine and COMMON-backed arrays.
- Decision: Keep the isolated helper because it makes the legacy wake-gap law reusable and auditable.
- Status: complete

### `src/XFoil.Solver/Services/BoundaryLayerTopologyBuilder.cs`

- File: `src/XFoil.Solver/Services/BoundaryLayerTopologyBuilder.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: STFIND`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: surface/wake branch ordering`
- Methods Audited: `Build`, `BuildNodeArcLengths`, `BuildPanelArcLengths`, `FindStagnationLocation`, `BuildUpperStations`, `BuildLowerStations`, `BuildWakeStations`, `EstimateNodeEdgeVelocity`, `InterpolateZeroCrossing`, `Interpolate`
- Parity-sensitive blocks: `stagnation sign-change scan`, `upper/lower branch station ordering`, `wake-branch continuation`
- Differences from legacy: The topology lineage comes from STFIND and the surface/wake station bookkeeping, but the managed code builds immutable station records from panel-mesh results instead of mutating the original side-indexed viscous arrays in place.
- Decision: Keep the managed topology builder for diagnostics and setup. The stricter parity path remains in the direct station-mapping logic inside ViscousSolverEngine.
- Status: complete

### `src/XFoil.Solver/Services/WakeSpacing.cs`

- File: `src/XFoil.Solver/Services/WakeSpacing.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xutils.f :: SETEXP`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: XYWAKE call site`
- Methods Audited: `BuildStretchedDistances` (both overloads), `BuildStretchedDistancesCore`, `SolveGeometricRatio`
- Parity-sensitive blocks: `initial quadratic ratio estimate`, `SETEXP Newton iteration`, `geometric distance accumulation`
- Differences from legacy: The ratio solve is a direct SETEXP port, but the managed version wraps it in typed overloads and returns a fresh array rather than writing into the existing Fortran workspace.
- Decision: Keep the typed managed wrapper and preserve the SETEXP solve unchanged because it is the wake-spacing parity reference.
- Status: complete

### `src/XFoil.Solver/Services/WakeGeometryGenerator.cs`

- File: `src/XFoil.Solver/Services/WakeGeometryGenerator.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: XYWAKE`
- Secondary Fortran Reference(s): `f_xfoil/src/xutils.f :: SETEXP`
- Methods Audited: `Generate`, `EvaluateWakeState`, `NormalizeFallback`, `ComputePointVelocityInfluence`
- Parity-sensitive blocks: `SETEXP-derived spacing usage`, `downstream wake march`, `local wake-direction update`
- Differences from legacy: The wake construction is derived from XYWAKE, but the managed port evaluates the local wake direction through explicit panel influence summation and returns immutable wake points instead of mutating the original wake arrays and shared solver workspace.
- Decision: Keep the managed wake builder because it fits the Hess-Smith and diagnostics architecture better, while preserving the legacy spacing and wake-gap conventions where they still matter.
- Status: complete

### `src/XFoil.Solver/Services/ViscousStateEstimator.cs`

- File: `src/XFoil.Solver/Services/ViscousStateEstimator.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: MRCHUE/COMSET initialization lineage`, `f_xfoil/src/xblsys.f :: transition and BL correlation families`
- Methods Audited: `Estimate`, `EstimateSurfaceBranch`, `EstimateWakeBranch`, `AdvanceAmplification`
- Parity-sensitive blocks: `none`
- Differences from legacy: There is no direct Fortran analogue for this file. It combines simplified Thwaites-style growth, heuristic regime switching, and a standalone amplification transport model into a diagnostic estimator that is separate from the operating-point viscous solve.
- Decision: Keep the estimator as a managed-only diagnostic tool and do not treat it as a parity reference.
- Status: complete

### `src/XFoil.Solver/Services/HessSmithInviscidSolver.cs`

- File: `src/XFoil.Solver/Services/HessSmithInviscidSolver.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: PSILIN/QISET/QWCALC influence-lineage`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: COMSET`
- Methods Audited: `Prepare`, `Analyze` (both overloads), `BuildUnitFreestreamRightHandSide`, `CombineBasisVectors`, `ComputeInfluence`, `IntegratePressureForces`, `ComputeIncompressiblePressureCoefficient`, `ApplyCompressibilityCorrection`, `DegreesToRadians`
- Parity-sensitive blocks: `influence matrix assembly`, `basis-vector reuse`, `pressure-force recovery`, `compressibility correction`
- Differences from legacy: This is the active managed inviscid runtime path, but it is not a literal XFoil port. It uses Hess-Smith influence formulas, precomputed unit-freestream basis solves, explicit managed force integration, and the WakeGeometryGenerator instead of replaying the original XFoil inviscid work arrays.
- Decision: Keep the managed solver design because it is the current runtime implementation, while documenting clearly that it remains legacy-derived rather than parity-direct.
- Status: complete

### `src/XFoil.Solver/Services/PostStallExtrapolator.cs`

- File: `src/XFoil.Solver/Services/PostStallExtrapolator.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `ExtrapolatePostStall`
- Parity-sensitive blocks: `none`
- Differences from legacy: This file implements a Viterna-Corrigan fallback that does not exist in legacy XFoil. It is a managed reporting improvement used when the viscous solver fails past stall.
- Decision: Keep the fallback, but keep it explicitly outside the parity-critical solver path.
- Status: complete

### `src/XFoil.Solver/Services/ViscousLaminarCorrector.cs`

- File: `src/XFoil.Solver/Services/ViscousLaminarCorrector.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: old staged viscous correction lineage`
- Methods Audited: `Correct`
- Parity-sensitive blocks: `none`
- Differences from legacy: The current file is only a deprecation shim that throws and points callers at ViscousSolverEngine. There is no direct legacy implementation corresponding to its present behavior.
- Decision: Keep the stub as a migration guard only and do not treat it as part of the solver or parity path.
- Status: complete

### `src/XFoil.Solver/Services/ViscousStateSeedBuilder.cs`

- File: `src/XFoil.Solver/Services/ViscousStateSeedBuilder.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xoper.f :: MRCHUE seed-state lineage`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: TECALC`, `f_xfoil/src/xpanel.f :: XYWAKE/WGAP`
- Methods Audited: `Build`, `BuildSurfaceSeed`, `BuildWakeSeed`, `ComputeTrailingEdgeGeometry`, `ComputeWakeGapDerivative`
- Parity-sensitive blocks: `TE gap tuple reconstruction`, `wake-gap seed profile`
- Differences from legacy: The seed-building concepts come from the legacy viscous initialization flow, but the managed version packages them into explicit seed objects and uses WakeGapProfile/WakeGeometry helpers instead of filling the original XFoil seed arrays in place.
- Decision: Keep the managed seed builder for diagnostics and setup. The exact parity seed path remains in ViscousSolverEngine.
- Status: complete

### `src/XFoil.Solver/Services/AirfoilAnalysisService.cs`

- File: `src/XFoil.Solver/Services/AirfoilAnalysisService.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xoper.f :: SPECAL/SPECCL operating-point façade`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: MRCL`, `f_xfoil/src/xpanel.f :: GGCALC/PSILIN lineages via delegated services`
- Methods Audited: `AirfoilAnalysisService` (both constructors), `AnalyzeInviscid`, `AnalyzeBoundaryLayerTopology`, `AnalyzeViscousStateSeed`, `AnalyzeViscousInitialState`, `AnalyzeViscous`, `SweepViscousAlpha`, `SweepViscousCL`, `SweepViscousRe`, `AnalyzeViscousIntervalSystem`, `AnalyzeViscousLaminarCorrection`, `AnalyzeViscousLaminarSolve`, `AnalyzeViscousInteraction`, `AnalyzeDisplacementCoupledViscous`, `SweepDisplacementCoupledAlpha`, `SweepDisplacementCoupledLiftCoefficient`, `AnalyzeDisplacementCoupledForLiftCoefficient`, `SweepInviscidAlpha`, `SweepInviscidLiftCoefficient`, `AnalyzeInviscidForLiftCoefficient` (both overloads), `ExtractCoordinates`, `AnalyzeInviscidLinearVortex`, `PrepareInviscidSystem`, `ToPolarPoint`, `NormalizeStep`, `ShouldContinue`
- Parity-sensitive blocks: `linear-vortex adapter boundary`, `public viscous path dispatch`, `obsolete surrogate API shims`
- Differences from legacy: There is no single direct Fortran equivalent to this file. It is a managed façade that composes the actual solver kernels, exposes diagnostic-only APIs, and preserves obsolete compatibility members as explicit throw-only shims.
- Decision: Keep the façade because it defines the supported API surface and documents the split between current, diagnostic, and obsolete paths.
- Status: complete

### `src/XFoil.Solver/Services/PolarSweepRunner.cs`

- File: `src/XFoil.Solver/Services/PolarSweepRunner.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xoper.f :: SPECAL/SPECCL`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: MRCL`
- Methods Audited: `SweepAlpha`, `SweepCL`, `SweepRe`, `ComputeMRCL`, `FindBestWarmStart`, `CaptureBLSnapshot`, `SolveViscousWithWarmStart`, `WithAngleOfAttack`, `NormalizeStep`, `ShouldContinue`
- Parity-sensitive blocks: `Type-1 sweep loop`, `Type-2 sweep loop`, `Type-3 reduced MRCL branch`, `lightweight warm-start policy`
- Differences from legacy: The runner follows the legacy Type 1/2/3 sweep lineage, but it reuses prepared linear-vortex state, stores managed result objects explicitly, and currently keeps only a lightweight warm-start snapshot instead of a full reusable BL-state continuation.
- Decision: Keep the managed sweep runner and document clearly that its warm-start behavior is lighter than the original OPER continuation flow.
- Status: complete

### `src/XFoil.Solver/Services/LinearVortexInviscidSolver.cs`

- File: `src/XFoil.Solver/Services/LinearVortexInviscidSolver.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: GGCALC/PSILIN`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: SPECAL/SPECCL`, `f_xfoil/src/xfoil.f :: CPCALC/CLCALC`
- Methods Audited: `AssembleAndFactorSystem`, `AssembleSystem`, `TracePanelNodes`, `TraceInfluenceSystem`, `TraceBasisEntries` (both overloads), `SolveBasisRightHandSides`, `CopyMatrixToSingle`, `TraceFactoredMatrix` (both overloads), `TracePivotEntries`, `SolveAtAngleOfAttack`, `SolveAtLiftCoefficient`, `ComputeInviscidSpeed`, `IntegratePressureForces`, `ComputePressureCoefficients`, `AnalyzeInviscid`, `ApplySharpTrailingEdgeCondition`, `UpdateTrailingEdgeStrengths`
- Parity-sensitive blocks: `GGCALC matrix assembly`, `basis LU/backsolve`, `SPECAL alpha superposition`, `SPECCL Newton solve`, `CPCALC/CLCALC pressure recovery`, `sharp-TE override`, `TE source/vortex update`
- Differences from legacy: This is a direct solver port, but the managed implementation factors the inviscid pipeline into explicit stages with parity-aware helpers and trace hooks instead of one monolithic routine and shared workspace.
- Decision: Keep the decomposed managed structure and preserve the legacy arithmetic/order inside the parity-sensitive kernel stages.
- Status: complete

### `src/XFoil.Solver/Services/InfluenceMatrixBuilder.cs`

- File: `src/XFoil.Solver/Services/InfluenceMatrixBuilder.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: QDCALC`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: PSILIN/XYWAKE`
- Methods Audited: `BuildAnalyticalDIJ` (all overloads), `BackSubstituteAirfoilColumn`, `BuildWakeGeometryData`, `BuildNumericalDIJ`, `FillWakeApproximation`, `FillAnalyticalWakeCoupling`, `ComputeWakeSensitivitiesDelegate`, `CreateLegacyWakeSolveContext` (both overloads), `BackSubstituteWakeColumn`, `ComputeWakeSourceSensitivitiesAt`, `ComputeWakeSourceSensitivitiesAtLegacyPrecision`, `ComputeWakeSourceSensitivitiesAtCoreSingle`, `ComputeWakeSourceSensitivitiesAtCore`, `TracePswlinSegment`, `TracePswlinHalfTerms`, `BuildWakeGeometry`, `BuildWakeGeometryCore`, `ComputeWakePanelState`, `EstimateFirstWakeSpacing`, `ComputeTrailingEdgeWakeNormal`, `ToDoubleArray`, `TraceWakeGeometry`, `TraceWakeSpacing`
- Parity-sensitive blocks: `analytical DIJ airfoil-column backsolve`, `wake-column assembly`, `wake-row closure`, `PSWLIN wake-source kernels`, `legacy wake LU replay`, `XYWAKE-derived wake geometry`
- Differences from legacy: The analytical QDCALC path is preserved, but the managed implementation splits the airfoil solve, wake sensitivity kernels, wake geometry, and parity replay machinery into explicit helpers, and it adds a numerical DIJ cross-check path that has no direct legacy counterpart.
- Decision: Keep the analytical QDCALC structure and the explicit parity wake kernels, and keep the numerical path only as a managed validation tool.
- Status: complete

### `src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs`

- File: `src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: PSILIN`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: GGCALC/QDCALC call sites`
- Methods Audited: `ComputeInfluenceAt`, `ComputeInfluenceAtLegacyPrecision`, `TracePsilinField`, `TracePsilinPanel`, `TracePsilinResult`, `TracePsilinSourceSegment`, `TracePsilinSourceDqTerms`, `TracePsilinSourceDzTerms`, `TracePsilinTeCorrection`, `TracePsilinSourceHalfTerms`, `TracePsilinVortexSegment`, `ComputeSourceContribution`, `ComputeVortexContribution`, `ComputeTEPanelContribution`
- Parity-sensitive blocks: `double-path PSILIN kernel`, `legacy REAL replay kernel`, `source half-panel derivatives`, `vortex integral branch`, `TE correction`
- Differences from legacy: The PSILIN kernel is a direct port, but the managed code factors the source, vortex, and TE branches into dedicated helpers and exposes a dedicated single-precision replay kernel with structured diagnostics for binary mismatch tracing.
- Decision: Keep the decomposed structure and preserve the legacy arithmetic ordering inside the parity replay path because this file is a primary fidelity boundary.
- Recent parity note: The standalone micro-batch staying green was not enough to close the full-run `BIJ` producer path. Fresh `fieldIndex=47` traces on the reduced-panel alpha-10 rung proved the source-midpoint `rs0` value must use the single-rounding `X0*X0 + YY*YY` staging in the parity path; the old fully separated square-sum reopened `DZDM` and `BIJ` drift even though the surrounding panel geometry already matched bitwise.
- Status: complete

### `src/XFoil.Core/Models/AirfoilFormat.cs`

- File: `src/XFoil.Core/Models/AirfoilFormat.cs`
- Category: `dto/model`
- Primary Fortran Reference: `f_xfoil/src/aread.f :: AREAD`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: LOAD/ISAV/MSAV file-format conventions`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port represents file kinds as a typed enum, whereas legacy XFoil uses integer `ITYPE` flags and command/file state.
- Decision: Keep the enum because it makes the IO layer explicit and type-safe without affecting parity-sensitive math.
- Status: complete

### `src/XFoil.Core/Models/AirfoilPoint.cs`

- File: `src/XFoil.Core/Models/AirfoilPoint.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/XFOIL.INC :: X/Y coordinate arrays`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port stores each coordinate pair in a value record, while legacy XFoil uses parallel REAL arrays.
- Decision: Keep the value record because it simplifies geometry-handling code and has no direct parity obligation.
- Status: complete

### `src/XFoil.Core/Models/AirfoilGeometry.cs`

- File: `src/XFoil.Core/Models/AirfoilGeometry.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/aread.f :: AREAD`, `f_xfoil/src/naca.f :: NACA4/NACA5`
- Methods Audited: `AirfoilGeometry`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages geometry, format, and domain metadata into one immutable object, whereas legacy XFoil spreads the same state across arrays, names, and flags.
- Decision: Keep the managed container because it is the right infrastructure boundary for parsing, generation, and solver setup.
- Status: complete

### `src/XFoil.Core/Models/AirfoilMetrics.cs`

- File: `src/XFoil.Core/Models/AirfoilMetrics.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: LEFIND/GEOPAR`, `f_xfoil/src/spline.f :: SCALC`
- Methods Audited: `AirfoilMetrics`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns one immutable metrics object instead of leaving geometry properties in transient scalars and COMMON state.
- Decision: Keep the managed summary object because it is cleaner for preprocessing and tests and not part of the parity kernel.
- Status: complete

### `src/XFoil.Core/Services/AirfoilMetricsCalculator.cs`

- File: `src/XFoil.Core/Services/AirfoilMetricsCalculator.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xgeom.f :: LEFIND/GEOPAR`
- Secondary Fortran Reference(s): `f_xfoil/src/spline.f :: SCALC`
- Methods Audited: `Calculate`, `FindLeadingEdgeIndex`, `InterpolateY`, `Distance`
- Parity-sensitive blocks: `polyline arc-length sweep`, `uniform x thickness/camber sampling`
- Differences from legacy: The method lineage is geometric-property extraction, but the managed version uses direct point scans and linear interpolation rather than spline derivatives and exact geometry moments.
- Decision: Keep the simpler managed metric helper because it serves preprocessing and validation, not solver parity.
- Status: complete

### `src/XFoil.Core/Services/AirfoilNormalizer.cs`

- File: `src/XFoil.Core/Services/AirfoilNormalizer.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xgeom.f :: NORM`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: LEFIND`, `f_xfoil/src/spline.f :: SCALC`
- Methods Audited: `Normalize`
- Parity-sensitive blocks: `rigid-body normalization transform`
- Differences from legacy: The managed version normalizes only the discrete point set, while legacy XFoil normalizes spline coordinates, derivatives, and derived geometry arrays in place.
- Decision: Keep the simpler managed normalization because it is sufficient for import-time cleanup and does not participate in parity-sensitive solver arithmetic.
- Status: complete

### `src/XFoil.Core/Services/AirfoilParser.cs`

- File: `src/XFoil.Core/Services/AirfoilParser.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/aread.f :: AREAD`
- Secondary Fortran Reference(s): `f_xfoil/src/userio.f :: GETFLT`
- Methods Audited: `ParseFile`, `ParseLines`, `SplitElements`, `IsElementSeparator`, `ParsePoint`, `TryParsePoint`, `ParseNumericTokens`, `TryParseDouble`, `Tokenize`
- Parity-sensitive blocks: `header/format classification`, `MSES element split on 999.0 999.0`
- Differences from legacy: The parser preserves the legacy file-type and separator conventions but removes interactive element selection and returns structured managed metadata instead of mutating shared buffers.
- Decision: Keep the managed parser because it preserves the important IO rules in a more usable API surface.
- Status: complete

### `src/XFoil.Core/Services/NacaAirfoilGenerator.cs`

- File: `src/XFoil.Core/Services/NacaAirfoilGenerator.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/naca.f :: NACA4`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: NACA command wrapper`
- Methods Audited: `Generate4Digit`, `Generate4DigitClassic`, `Generate4DigitCore`, `ComputeTrailingEdgeBunchedCoordinate`, `ComputeThicknessPowers`, `MeanCamber`
- Parity-sensitive blocks: `NACA4 node-generation loop`, `trailing-edge bunching law`, `thickness-polynomial power chain`, `classic single-precision replay path`
- Differences from legacy: The classic path is a direct port of `NACA4`, but the managed port also offers an improved normal-offset construction, generic floating-point specialization, and explicit trace hooks for parity work.
- Decision: Keep both the improved managed path and the explicit classic/parity replay path because they serve different valid purposes.
- Status: complete

### `src/XFoil.Core/Diagnostics/CoreTrace.cs`

- File: `src/XFoil.Core/Diagnostics/CoreTrace.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Begin`, `Scope`, `Event`, `ScopeName`, `RestoreScope.RestoreScope`, `RestoreScope.Dispose`, `TraceScope.TraceScope`, `TraceScope.Dispose`, `EmptyScope.Dispose`
- Parity-sensitive blocks: `ambient writer install`, `structured call enter/exit scope`
- Differences from legacy: This is a .NET-only tracing abstraction with no direct Fortran counterpart; it exists solely to support parity diagnostics in the managed port.
- Decision: Keep the managed tracing utility because parity debugging depends on it and it does not alter solver behavior.
- Status: complete

### `src/XFoil.Core/Numerics/LegacyLibm.cs`

- File: `src/XFoil.Core/Numerics/LegacyLibm.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/naca.f :: NACA4 exponentiation sites`
- Methods Audited: `Pow`, `PowfMac`, `PowfLinux`, `PowfWindows`
- Parity-sensitive blocks: `platform powf dispatch`, `MathF fallback`
- Differences from legacy: Legacy XFoil relies on the host Fortran runtime's REAL math intrinsics implicitly; the managed port makes that dependency explicit through platform libm bindings.
- Decision: Keep the libm bridge because it is the cleanest way to reproduce legacy single-precision math where binary parity depends on it.
- Status: complete

### `src/XFoil.Design/Models/ConformalMapgenResult.cs`

- File: `src/XFoil.Design/Models/ConformalMapgenResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xmdes.f :: MAPGEN`
- Methods Audited: `ConformalMapgenResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns one immutable result object, while legacy XFoil leaves the same outputs in mutable working arrays and state.
- Decision: Keep the managed result object because it is the right service/API boundary.
- Status: complete

### `src/XFoil.Design/Models/ConformalMappingCoefficient.cs`

- File: `src/XFoil.Design/Models/ConformalMappingCoefficient.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xmdes.f :: MAPGEN`
- Methods Audited: `ConformalMappingCoefficient`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port exposes each mapping coefficient as an object rather than as entries in modal arrays.
- Decision: Keep the DTO because it makes modal output clearer and easier to serialize.
- Status: complete

### `src/XFoil.Design/Models/ContourEditResult.cs`

- File: `src/XFoil.Design/Models/ContourEditResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xgdes.f :: GDES edit command family`
- Methods Audited: `ContourEditResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: Legacy XFoil edits geometry in place and reports procedurally; the port returns an immutable edit summary.
- Decision: Keep the managed result object because it is more useful to callers and tests.
- Status: complete

### `src/XFoil.Design/Models/ContourModificationResult.cs`

- File: `src/XFoil.Design/Models/ContourModificationResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xgdes.f :: GDES contour-edit workflow`
- Methods Audited: `ContourModificationResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns structured modification metadata rather than relying solely on mutated geometry buffers.
- Decision: Keep the managed result object because it gives the service layer a clear contract.
- Status: complete

### `src/XFoil.Design/Models/CornerRefinementParameterMode.cs`

- File: `src/XFoil.Design/Models/CornerRefinementParameterMode.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xgdes.f :: CADD-style refinement workflow`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port uses a typed enum where legacy XFoil passes refinement semantics procedurally through commands.
- Decision: Keep the enum because it makes the service and CLI APIs explicit.
- Status: complete

### `src/XFoil.Design/Models/FlapDeflectionResult.cs`

- File: `src/XFoil.Design/Models/FlapDeflectionResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xgdes.f :: FLAP`
- Methods Audited: `FlapDeflectionResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: Legacy XFoil applies flap edits in place; the port returns the outcome as an immutable result object.
- Decision: Keep the managed result object because it captures flap-edit metadata clearly.
- Status: complete

### `src/XFoil.Design/Models/GeometryScaleOrigin.cs`

- File: `src/XFoil.Design/Models/GeometryScaleOrigin.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: This origin-selection abstraction is managed-only and does not exist as a dedicated legacy type.
- Decision: Keep the enum because it is the clearest API for the scaling helpers.
- Status: complete

### `src/XFoil.Design/Models/GeometryScalingResult.cs`

- File: `src/XFoil.Design/Models/GeometryScalingResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `GeometryScalingResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns an explicit scaling result object, whereas legacy XFoil would only mutate geometry in place.
- Decision: Keep the managed result object because it improves service usability and testing.
- Status: complete

### `src/XFoil.Design/Models/LeadingEdgeRadiusEditResult.cs`

- File: `src/XFoil.Design/Models/LeadingEdgeRadiusEditResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xgdes.f :: LERAD`
- Methods Audited: `LeadingEdgeRadiusEditResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns a typed edit summary instead of relying on mutated geometry and procedural output.
- Decision: Keep the managed result object because it makes the edit outcome explicit.
- Status: complete

### `src/XFoil.Design/Models/ModalCoefficient.cs`

- File: `src/XFoil.Design/Models/ModalCoefficient.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xmdes.f :: MAPGEN/PERT`
- Methods Audited: `ModalCoefficient`
- Parity-sensitive blocks: `none`
- Differences from legacy: The port packages modal coefficients into a named object instead of leaving them in indexed arrays.
- Decision: Keep the DTO because it makes modal spectra explicit and serializable.
- Status: complete

### `src/XFoil.Design/Models/ModalInverseExecutionResult.cs`

- File: `src/XFoil.Design/Models/ModalInverseExecutionResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xmdes.f :: MDES inverse-design workflow`
- Methods Audited: `ModalInverseExecutionResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns an immutable execution result instead of leaving geometry and modal updates in shared state.
- Decision: Keep the managed result object because it is a better service/API boundary.
- Status: complete

### `src/XFoil.Design/Models/ModalSpectrum.cs`

- File: `src/XFoil.Design/Models/ModalSpectrum.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xmdes.f :: MAPGEN/PERT modal spectra`
- Methods Audited: `ModalSpectrum`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port exposes a named immutable spectrum object instead of raw modal arrays and command context.
- Decision: Keep the managed spectrum object because it makes design outputs clearer and reusable.
- Status: complete

### `src/XFoil.Design/Models/QSpecEditResult.cs`

- File: `src/XFoil.Design/Models/QSpecEditResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xqdes.f :: QDES target-edit workflow`
- Methods Audited: `QSpecEditResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns an explicit edit summary instead of only mutating `QSPEC` arrays.
- Decision: Keep the managed result object because it improves service usability.
- Status: complete

### `src/XFoil.Design/Models/QSpecExecutionResult.cs`

- File: `src/XFoil.Design/Models/QSpecExecutionResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xqdes.f :: QDES execution workflow`
- Methods Audited: `QSpecExecutionResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns geometry and displacement metrics explicitly instead of exposing only mutated state.
- Decision: Keep the managed result object because it makes the workflow auditable.
- Status: complete

### `src/XFoil.Design/Models/QSpecPoint.cs`

- File: `src/XFoil.Design/Models/QSpecPoint.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xqdes.f :: QSPEC/SSPEC arrays`
- Methods Audited: `QSpecPoint`
- Parity-sensitive blocks: `none`
- Differences from legacy: The port groups per-sample data into an object instead of using parallel arrays.
- Decision: Keep the DTO because it simplifies profile export and inspection.
- Status: complete

### `src/XFoil.Design/Models/QSpecProfile.cs`

- File: `src/XFoil.Design/Models/QSpecProfile.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xqdes.f :: QSPEC state`
- Methods Audited: `QSpecProfile`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port uses an immutable named profile object instead of shared `QSPEC` arrays and command context.
- Decision: Keep the managed profile object because it is the right output boundary for design services.
- Status: complete

### `src/XFoil.Design/Models/TrailingEdgeGapEditResult.cs`

- File: `src/XFoil.Design/Models/TrailingEdgeGapEditResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xgdes.f :: TGAP`
- Methods Audited: `TrailingEdgeGapEditResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns an immutable edit summary instead of only mutating geometry and printing status.
- Decision: Keep the managed result object because it makes the edit outcome explicit.
- Status: complete

### `src/XFoil.Design/Services/BasicGeometryTransformService.cs`

- File: `src/XFoil.Design/Services/BasicGeometryTransformService.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: GDES transform/edit workflow`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: NORM/LEFIND`, `f_xfoil/src/spline.f :: SCALC`
- Methods Audited: `RotateDegrees`, `RotateRadians` (public and private), `Translate`, `ScaleAboutOrigin`, `ScaleYLinearly`, `Derotate`, `NormalizeUnitChord`, `ValidateGeometry`
- Parity-sensitive blocks: `none`
- Differences from legacy: The service generalizes several GDES-style rigid transforms into direct immutable APIs, and it adds managed-only helpers such as linear y scaling.
- Decision: Keep the managed transform service because it is an intentional API improvement; only the normalization path reuses legacy-derived logic.
- Status: complete

### `src/XFoil.Design/Services/ContourEditService.cs`

- File: `src/XFoil.Design/Services/ContourEditService.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: GDES CADD and point-edit command workflow`
- Secondary Fortran Reference(s): `f_xfoil/src/spline.f :: SCALC`
- Methods Audited: `AddPoint`, `MovePoint`, `DeletePoint`, `DoublePoint`, `RefineCorners`, `BuildResult`, `BuildSplineParameters`, `ComputeMaximumCornerAngle`, `ComputeCornerAngleDegrees`, `EvaluateSplinePoint`, `FindNonZeroVector`, `TryAppendDistinct`, `Distance`, `ValidateGeometry`, `ValidateInsertIndex`, `ValidatePointIndex`
- Parity-sensitive blocks: `none`
- Differences from legacy: The service preserves the GDES edit semantics but factors them into explicit immutable methods and a decomposed corner-refinement loop instead of interactive command handling.
- Decision: Keep the managed refactor because it is clearer, scriptable, and testable.
- Status: complete

### `src/XFoil.Design/Services/ContourModificationService.cs`

- File: `src/XFoil.Design/Services/ContourModificationService.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/modify.f :: MODIXY`
- Secondary Fortran Reference(s): `f_xfoil/src/xgdes.f :: GDES MODI command handling`
- Methods Audited: `ModifyContour`, `FindClosestPointIndex`, `BuildArcLengths`, `RescaleParameters`, `DistanceSquared`
- Parity-sensitive blocks: `none`
- Differences from legacy: The service preserves the contour-graft intent of `MODIXY` but expresses it through explicit spline helpers and immutable geometry results.
- Decision: Keep the managed refactor because it makes the workflow reusable and testable without the legacy command loop.
- Status: complete

### `src/XFoil.Design/Services/GeometryScalingService.cs`

- File: `src/XFoil.Design/Services/GeometryScalingService.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xgdes.f :: geometry-transform workflow`
- Methods Audited: `Scale`
- Parity-sensitive blocks: `none`
- Differences from legacy: This exact origin-aware scaling helper is a managed API addition rather than a direct legacy routine.
- Decision: Keep the managed scaling service because it is a useful extension of the design toolkit and not a parity-sensitive path.
- Status: complete

### `src/XFoil.Design/Services/LeadingEdgeRadiusService.cs`

- File: `src/XFoil.Design/Services/LeadingEdgeRadiusService.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: LERAD`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: LEFIND/GEOPAR`, `f_xfoil/src/spline.f :: SCALC/SINVRT`
- Methods Audited: `ScaleLeadingEdgeRadius`
- Parity-sensitive blocks: `leading-edge thickness rescaling loop`
- Differences from legacy: The service preserves the geometric intent of `LERAD` but expresses it through explicit chord-frame helpers and immutable geometry output.
- Decision: Keep the decomposed managed implementation because it makes the radius edit easier to audit and test.
- Status: complete

### `src/XFoil.Design/Services/TrailingEdgeGapService.cs`

- File: `src/XFoil.Design/Services/TrailingEdgeGapService.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: TGAP`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: LEFIND/GEOPAR`
- Methods Audited: `SetTrailingEdgeGap`, `FindLeadingEdgeIndex`, `ResolveGapDirection`, `Normalize`, `ComputeEndpointGap`
- Parity-sensitive blocks: `per-point TGAP displacement sweep`
- Differences from legacy: The service preserves the TGAP geometric intent but factors the blend and direction logic into explicit immutable helpers.
- Decision: Keep the managed refactor because it makes the operation reusable and testable while preserving its purpose.
- Status: complete

### `src/XFoil.Design/Services/FlapDeflectionService.cs`

- File: `src/XFoil.Design/Services/FlapDeflectionService.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: FLAP`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: LEFIND/NORM`, `f_xfoil/src/spline.f :: SCALC/SINVRT`
- Methods Audited: `DeflectTrailingEdge`, `EditSurface`, `SolveSurfaceBreaks`, `SolvePerpendicularBreak`, `ComputeClosingBreakResiduals`, `RotateAroundHinge`, `IsInside`, `RemoveMicroSegments`, `Distance`, `SurfaceData` constructor and methods, `NaturalCubicSpline` constructor and methods
- Parity-sensitive blocks: `FLAP surface recombination`, `closing-break Newton solve`, `perpendicular break solve`, `surface spline inversion`, `natural-spline tridiagonal assembly`
- Differences from legacy: The service preserves the FLAP geometric edit but factors the monolithic command routine into explicit surface samplers, nonlinear break solvers, and immutable geometry reconstruction, plus a managed-only micro-segment cleanup step.
- Decision: Keep the managed refactor because it makes the flap edit deterministic and testable while preserving the legacy geometric intent.
- Status: complete

### `src/XFoil.Design/Services/GeometryTransformUtilities.cs`

- File: `src/XFoil.Design/Services/GeometryTransformUtilities.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xgeom.f :: LEFIND/NORM/GEOPAR`
- Secondary Fortran Reference(s): `f_xfoil/src/spline.f :: SCALC/SINVRT/SPLIND`
- Methods Audited: `FindLeadingEdgeIndex`, `BuildChordFrame`, `ToChordFrame`, `FromChordFrame`, `EstimateLeadingEdgePoint`, `EstimateLeadingEdgeRadius`, `FindOppositePointAtSameChordX`, `InterpolateY`, `CircumcircleRadius`, `Distance`, `FindLeadingEdgeArcLength`, `SplineCurve2D` constructor and methods, `NaturalCubicSpline` constructors and methods
- Parity-sensitive blocks: `LEFIND seed/refinement`, `SCALC arc-length build`, `SINVRT-style chordwise inversion`, `spline tridiagonal assembly`
- Differences from legacy: The file extracts reusable geometry and spline primitives from the legacy command-oriented routines and expresses them as composable helpers rather than array-mutation utilities.
- Decision: Keep the managed refactor because these helpers are the right shared geometry foundation for the design services.
- Status: complete

### `src/XFoil.Design/Services/QSpecDesignService.cs`

- File: `src/XFoil.Design/Services/QSpecDesignService.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xqdes.f :: QDES/MODI/SMOOQ`
- Secondary Fortran Reference(s): `f_xfoil/src/xmdes.f :: PERT/CNCALC`, `f_xfoil/src/spline.f :: TRISOL`
- Methods Audited: `CreateFromInviscidAnalysis`, `Modify`, `Smooth`, `ForceSymmetry`, `ExecuteInverse`, `ApplySlopeConstraint`, `SolveTriDiagonal`, `RebuildProfile`, `FindClosestIndex`, `EstimateDerivative`, `BuildSampleDistances`, `SmoothDisplacements`, `ComputeCentroid`, `ComputeOutwardNormal`
- Parity-sensitive blocks: `MODI segment replacement`, `SMOOQ tridiagonal assembly`, `SYMM mirrored update`, `simplified inverse-displacement reconstruction`
- Differences from legacy: The service keeps the legacy QDES editing intent for grafting, smoothing, and symmetry but wraps it in immutable profile objects, and its execution helper intentionally uses a simpler managed normal-displacement reconstruction instead of the full legacy conformal inverse design path.
- Decision: Keep the managed refactor and managed-only execution helper because they fit the library API better than the interactive command workflow.
- Status: complete

### `src/XFoil.Design/Services/ModalInverseDesignService.cs`

- File: `src/XFoil.Design/Services/ModalInverseDesignService.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xmdes.f :: MDES/PERT`, `f_xfoil/src/xqdes.f :: QDES`
- Methods Audited: `CreateSpectrum`, `Execute`, `PerturbMode`, `ExecuteFromSpectrum`, `BuildSpectrum`, `ComputeSineCoefficient`, `ApplyFilter`, `ReconstructField`, `EnsureCompatibleProfiles`, `ComputeCentroid`, `ComputeOutwardNormal`
- Parity-sensitive blocks: `none`
- Differences from legacy: The service is inspired by the MDES/PERT workflow, but it deliberately replaces the legacy conformal-map modal basis with a simplified sine-basis spectrum and direct normal-displacement reconstruction.
- Decision: Keep the managed-only implementation because it is an intentional higher-level API rather than a parity target.
- Status: complete

### `src/XFoil.Design/Services/ConformalMapgenService.cs`

- File: `src/XFoil.Design/Services/ConformalMapgenService.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xmdes.f :: MAPGEN/CNCALC/PIQSUM/ZCCALC/ZCNORM/ZLEFIND`
- Secondary Fortran Reference(s): `f_xfoil/src/xgeom.f :: LEFIND`, `f_xfoil/src/spline.f :: SPLIND/SEVAL`
- Methods Audited: `Execute` overloads, `SolveForTrailingEdgeAngleTarget`, `SolveForProfileTarget`, `ExecuteWithContinuation`, `ExecuteCore`, `RebindReportedTargets`, `ShouldAcceptDirectResult`, `IsBetterResult`, `DetermineContinuationStageCount`, `DetermineInitialContinuationFilter`, `BlendProfiles`, `NormalizeBracket`, `TryFalsePosition`, `TrySecant`, `BuildCnFromQSpec`, `FourierTransform`, `PiqSum`, `ZcCalc`, `ZcNorm`, `FindLeadingEdge`, `EvaluateDzDw`, `SafeExp`, `IsFinite`, `AreFinite`, `CaptureState`, `RestoreState`, `EstimateSplineDerivative`, `EstimateSplineSecondDerivative`, `ResampleProfile`, `ComputeTrailingEdgeAngleDegrees`, `ApplyHanningFilter`, `ClampTrailingEdgeAngle`, `CirclePlaneState` constructor and `Create`, `CirclePlaneStateSnapshot`
- Parity-sensitive blocks: `CNCALC PIQ assembly`, `PIQSUM inverse reconstruction`, `ZCCALC geometry march`, `ZCNORM normalization`, `ZLEFIND refinement`, `MAPGEN Newton correction`, `managed continuation/damping wrappers`
- Differences from legacy: The core conformal-map algebra is a close direct port of MAPGEN and its helper routines, while the surrounding continuation, damping, target-angle bracketing, and finite-value guards are deliberate managed additions.
- Decision: Keep the managed wrappers because they improve robustness, but preserve the legacy formulas inside the conformal-map kernels.
- Status: complete

### `src/XFoil.IO/Models/AnalysisSessionArtifact.cs`

- File: `src/XFoil.IO/Models/AnalysisSessionArtifact.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: This file is a managed-only session artifact DTO; the legacy workflow did not package generated files into named result objects.
- Decision: Keep the managed DTO because it is the right automation boundary.
- Status: complete

### `src/XFoil.IO/Models/AnalysisSessionManifest.cs`

- File: `src/XFoil.IO/Models/AnalysisSessionManifest.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: This manifest is part of the .NET batch/session layer and has no legacy runtime counterpart.
- Decision: Keep the managed DTO because it cleanly represents session inputs.
- Status: complete

### `src/XFoil.IO/Models/AnalysisSessionRunResult.cs`

- File: `src/XFoil.IO/Models/AnalysisSessionRunResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The legacy workflow reported outputs procedurally rather than through a named run-result object.
- Decision: Keep the managed DTO because it is a better API for automation and tests.
- Status: complete

### `src/XFoil.IO/Models/LegacyMachVariationType.cs`

- File: `src/XFoil.IO/Models/LegacyMachVariationType.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The enum makes legacy polar-file Mach variation modes explicit, whereas the original runtime carried them as output tokens and session state.
- Decision: Keep the managed enum because it is clearer and type-safe.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarColumn.cs`

- File: `src/XFoil.IO/Models/LegacyPolarColumn.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarColumn`
- Parity-sensitive blocks: `none`
- Differences from legacy: The legacy runtime emitted column names procedurally; the port stores them in a validated DTO.
- Decision: Keep the managed DTO because it makes parsed polar tables explicit.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarDumpExportResult.cs`

- File: `src/XFoil.IO/Models/LegacyPolarDumpExportResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarDumpExportResult`
- Parity-sensitive blocks: `none`
- Differences from legacy: The legacy dump export wrote files procedurally and did not return a result object with explicit paths.
- Decision: Keep the managed DTO because it is useful for automation and tests.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarDumpFile.cs`

- File: `src/XFoil.IO/Models/LegacyPolarDumpFile.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarDumpFile`
- Parity-sensitive blocks: `none`
- Differences from legacy: This is a managed representation of the legacy polar-dump file format; the original runtime streamed the same content directly to disk.
- Decision: Keep the managed DTO because it provides a stable importer/exporter contract.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarDumpGeometryPoint.cs`

- File: `src/XFoil.IO/Models/LegacyPolarDumpGeometryPoint.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarDumpGeometryPoint`
- Parity-sensitive blocks: `none`
- Differences from legacy: The legacy dump writer produced raw coordinate lines rather than a typed point object.
- Decision: Keep the managed DTO because it is the natural in-memory form for imported geometry.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarDumpOperatingPoint.cs`

- File: `src/XFoil.IO/Models/LegacyPolarDumpOperatingPoint.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarDumpOperatingPoint`
- Parity-sensitive blocks: `none`
- Differences from legacy: The legacy dump workflow wrote operating-point sections directly; the port groups them into a named immutable object.
- Decision: Keep the managed DTO because it makes the dump structure explicit.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarDumpSide.cs`

- File: `src/XFoil.IO/Models/LegacyPolarDumpSide.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarDumpSide`
- Parity-sensitive blocks: `none`
- Differences from legacy: The legacy dump format had side sections in text, not a dedicated side object.
- Decision: Keep the managed DTO because it gives the importer/exporter a clearer structure.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarDumpSideSample.cs`

- File: `src/XFoil.IO/Models/LegacyPolarDumpSideSample.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarDumpSideSample`
- Parity-sensitive blocks: `none`
- Differences from legacy: The original dump writer emitted these values as raw rows rather than typed sample objects.
- Decision: Keep the managed DTO because it is the appropriate import representation.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarFile.cs`

- File: `src/XFoil.IO/Models/LegacyPolarFile.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarFile`
- Parity-sensitive blocks: `none`
- Differences from legacy: This is a managed representation of the legacy polar-file format; the original runtime produced the same information through formatted output and shared state.
- Decision: Keep the managed DTO because it provides a stable importer/exporter contract.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarRecord.cs`

- File: `src/XFoil.IO/Models/LegacyPolarRecord.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarRecord`, `SetValue`
- Parity-sensitive blocks: `none`
- Differences from legacy: The port stores polar rows in a case-insensitive dictionary and allows controlled key-based updates, while the legacy file format was positional.
- Decision: Keep the managed DTO because it makes importer normalization and test access easier.
- Status: complete

### `src/XFoil.IO/Models/LegacyPolarTripSetting.cs`

- File: `src/XFoil.IO/Models/LegacyPolarTripSetting.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyPolarTripSetting`
- Parity-sensitive blocks: `none`
- Differences from legacy: The legacy header carried trip settings as raw values rather than a typed object.
- Decision: Keep the managed DTO because it preserves the same semantics more clearly.
- Status: complete

### `src/XFoil.IO/Models/LegacyReferencePolarBlock.cs`

- File: `src/XFoil.IO/Models/LegacyReferencePolarBlock.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyReferencePolarBlock`
- Parity-sensitive blocks: `none`
- Differences from legacy: This comparison-block object belongs to the managed verification/import layer and has no direct runtime Fortran counterpart.
- Decision: Keep the managed DTO because it is useful for tooling and tests.
- Status: complete

### `src/XFoil.IO/Models/LegacyReferencePolarBlockKind.cs`

- File: `src/XFoil.IO/Models/LegacyReferencePolarBlockKind.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: This enum categorizes comparison-file blocks for the managed tooling layer rather than the legacy runtime.
- Decision: Keep the managed enum because it makes external file semantics explicit.
- Status: complete

### `src/XFoil.IO/Models/LegacyReferencePolarFile.cs`

- File: `src/XFoil.IO/Models/LegacyReferencePolarFile.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyReferencePolarFile`
- Parity-sensitive blocks: `none`
- Differences from legacy: This reference-polar file object belongs to the managed verification/import layer and has no direct runtime Fortran analogue.
- Decision: Keep the managed DTO because it provides a stable representation for tooling and tests.
- Status: complete

### `src/XFoil.IO/Models/LegacyReferencePolarPoint.cs`

- File: `src/XFoil.IO/Models/LegacyReferencePolarPoint.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `LegacyReferencePolarPoint`
- Parity-sensitive blocks: `none`
- Differences from legacy: The reference-comparison layer uses typed points, whereas the legacy runtime did not define this object.
- Decision: Keep the managed DTO because it is the natural import representation.
- Status: complete

### `src/XFoil.IO/Models/LegacyReynoldsVariationType.cs`

- File: `src/XFoil.IO/Models/LegacyReynoldsVariationType.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The enum makes legacy polar-file Reynolds variation modes explicit, whereas the original runtime carried them as output tokens and session state.
- Decision: Keep the managed enum because it is clearer and type-safe.
- Status: complete

### `src/XFoil.IO/Models/SessionAirfoilDefinition.cs`

- File: `src/XFoil.IO/Models/SessionAirfoilDefinition.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: This session-manifest airfoil definition belongs to the managed automation layer and has no legacy runtime counterpart.
- Decision: Keep the managed DTO because it cleanly represents one session input choice.
- Status: complete

### `src/XFoil.IO/Models/SessionSweepDefinition.cs`

- File: `src/XFoil.IO/Models/SessionSweepDefinition.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: This batch sweep definition is part of the managed automation layer rather than the legacy interactive runtime.
- Decision: Keep the managed DTO because it is the right structured input for scripted sweeps.
- Status: complete

### `src/XFoil.IO/Services/AirfoilDatExporter.cs`

- File: `src/XFoil.IO/Services/AirfoilDatExporter.cs`
- Category: `io-wrapper`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Format`, `Export`
- Parity-sensitive blocks: `none`
- Differences from legacy: The service formats and writes DAT coordinate files as a reusable API instead of relying on an interactive save/export path.
- Decision: Keep the managed exporter because it is the correct IO boundary for the .NET API.
- Status: complete

### `src/XFoil.IO/Services/LegacyPolarDumpArchiveWriter.cs`

- File: `src/XFoil.IO/Services/LegacyPolarDumpArchiveWriter.cs`
- Category: `io-wrapper`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Export`, `FormatDouble`, `FormatBoolean`
- Parity-sensitive blocks: `none`
- Differences from legacy: This service derives a CSV archive bundle from an imported legacy binary dump, which the original runtime did not produce.
- Decision: Keep the managed exporter because it is useful tooling on top of the legacy dump format.
- Status: complete

### `src/XFoil.IO/Services/LegacyPolarDumpImporter.cs`

- File: `src/XFoil.IO/Services/LegacyPolarDumpImporter.cs`
- Category: `io-wrapper`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Import`, `ReadGeometry`, `ReadSideSamples`, `ResolveMach`, `ReadSingles`, `ReadInt32`, `ReadSingle`, `ReadFixedString`, `ReadRecord`
- Parity-sensitive blocks: `none`
- Differences from legacy: The importer reconstructs the legacy unformatted dump record layout into nested DTOs with explicit validation, which is a compatibility layer rather than a runtime solver path.
- Decision: Keep the managed importer because it makes existing dump files reusable in the .NET toolchain.
- Status: complete

### `src/XFoil.IO/Services/LegacyReferencePolarImporter.cs`

- File: `src/XFoil.IO/Services/LegacyReferencePolarImporter.cs`
- Category: `io-wrapper`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Import`
- Parity-sensitive blocks: `none`
- Differences from legacy: This parser belongs to the managed verification/tooling layer and has no direct runtime Fortran counterpart.
- Decision: Keep the managed importer because it supports regression comparisons and tests.
- Status: complete

### `src/XFoil.IO/Services/AnalysisSessionRunner.cs`

- File: `src/XFoil.IO/Services/AnalysisSessionRunner.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: ASEQ/CSEQ operating-point workflow`, `f_xfoil/src/naca.f :: NACA4/NACA5`, `f_xfoil/src/aread.f :: AREAD`
- Methods Audited: `AnalysisSessionRunner` constructors, `Run`, `RunSweep`, `ImportLegacyPolar`, `ImportLegacyReferencePolar`, `ImportLegacyPolarDump`, `ExportInviscidAlphaSweep`, `ExportInviscidLiftSweep`, `ExportViscousAlphaSweep`, `ExportViscousLiftSweep`, `LoadGeometry`, `CreateInviscidSettings`, `CreateViscousSettings`, `CreateArtifact`, `NormalizeKind`, `SweepRequiresGeometry`, `RequireGeometry`, `SanitizeFileName`, `LoadManifest`, `CreateReadOptions`, `CreateWriteOptions`
- Parity-sensitive blocks: `none`
- Differences from legacy: This is a batch/session orchestration layer that composes managed parser, solver, and exporter services; the legacy runtime had no comparable manifest-driven automation API.
- Decision: Keep the managed orchestration layer because it is an intentional product capability, not a parity target.
- Status: complete

### `src/XFoil.IO/Services/LegacyPolarImporter.cs`

- File: `src/XFoil.IO/Services/LegacyPolarImporter.cs`
- Category: `io-wrapper`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: PACC/PWRT saved-polar output workflow`
- Methods Audited: `FloatRegex`, `VersionLineRegex`, `NameLineRegex`, `VariationLineRegex`, `TripLineRegex`, `MainParameterLineRegex`, `PropulsorLineRegex`, `Import`, `ParseColumns`, `ParseRecords`, `ApplyLegacyDefaults`, `GetValue`, `CreateUniqueKey`, `StripDuplicateSuffix`, `GetDuplicateIndex`, `NormalizeKey`, `ParseDouble`, `ParseInt`
- Parity-sensitive blocks: `none`
- Differences from legacy: The importer parses the text format emitted by the legacy saved-polar writer and normalizes omitted header defaults into explicit structured row data; this is a compatibility layer rather than a direct runtime port.
- Decision: Keep the managed importer because it makes legacy saved polar files reusable in the .NET toolchain.
- Status: complete

### `src/XFoil.IO/Services/PolarCsvExporter.cs`

- File: `src/XFoil.IO/Services/PolarCsvExporter.cs`
- Category: `io-wrapper`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: PACC/PWRT/BLDUMP/CPDUMP saved-output workflow`
- Methods Audited: `Format` overloads for `PolarSweepResult`, `ViscousPolarSweepResult`, `InviscidLiftSweepResult`, `ViscousLiftSweepResult`, `LegacyPolarFile`, `LegacyReferencePolarFile`; `Export` overloads; `FormatDouble`, `FormatInteger`, `FormatBoolean`, `WriteFile`
- Parity-sensitive blocks: `none`
- Differences from legacy: The exporter writes deterministic CSV projections of managed solver results and imported legacy files rather than replaying the original interactive save-file formats exactly.
- Decision: Keep the managed exporter because it is an intentional automation and tooling layer, not a parity-execution path.
- Status: complete

### `src/XFoil.Cli/Program.cs`

- File: `src/XFoil.Cli/Program.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: ALFA/CLI/ASEQ/CSEQ`, `f_xfoil/src/xoper.f :: PACC/PWRT/DUMP`, `f_xfoil/src/xgdes.f :: GDES command family`, `f_xfoil/src/xqdes.f :: QDES command family`, `f_xfoil/src/xmdes.f :: MDES/MAPGEN command family`
- Methods Audited: `WriteSummary`, `PrintUsage`, `WriteInviscidSummary`, `WritePolarSummary`, `WriteViscousPolarSummary`, `ExportPolarCsv`, `ExportViscousPolarCsv`, `ExportLiftSweepCsv`, `ImportLegacyPolar`, `WriteLegacyPolarSummary`, `ImportLegacyReferencePolar`, `WriteLegacyReferencePolarSummary`, `ImportLegacyPolarDump`, `WriteLegacyPolarDumpSummary`, `ExportViscousLiftSweepCsv`, `WriteTargetLiftSummary`, `WriteViscousTargetLiftSummary`, `WriteLiftSweepSummary`, `WriteViscousLiftSweepSummary`, `WriteBoundaryLayerTopologySummary`, `WriteViscousSeedSummary`, `WriteViscousInitialStateSummary`, `WriteViscousIntervalSummary`, `WriteViscousCorrectionSummary`, `WriteViscousSolveSummary`, `WriteViscousInteractionSummary`, `WriteDisplacementCoupledSummary`, `FindTransitionXi`, `FindTransitionAmplification`, `CreateViscousSettings`, `WriteExportSummary`, `ExportFlapGeometry`, `ExportTrailingEdgeGapGeometry`, `ExportLeadingEdgeRadiusGeometry`, `ExportScaledGeometry`, `ExportGeometry`, `ExportContourEditGeometry`, `ExportContourModificationGeometry`, `ExportQSpecProfile`, `ExportSymmetricQSpecProfile`, `ExportQSpecProfileSetForAngles`, `ExportQSpecProfileSetForLiftCoefficients`, `ExportModifiedQSpecProfile`, `ExportSmoothedQSpecProfile`, `ExportExecutedQSpecGeometry`, `ExportModalSpectrum`, `ExportModalExecutedGeometry`, `ExportPerturbedModalGeometry`, `ExportConformalMapgenGeometry`, `ExportConformalMapgenSpectrum`, `WriteQSpecCsv`, `WriteConformalCoefficientCsv`, `WriteModalSpectrumCsv`, `WriteQSpecSetCsv`, `RunSession`, `ParseDouble`, `ParseRemainingDoubles`, `ParseInteger`, `ParseControlPointsFile`, `ParseBooleanFlag`, `ParseCornerRefinementMode`, `TryParseCornerRefinementMode`, `ParseScaleOrigin`, `EscapeCsv`
- Parity-sensitive blocks: `none`
- Differences from legacy: The file is a managed, deterministic command-line front end over ported services. It documents the lineage of each command family, but there is no direct Fortran equivalent to the headless argument parser, static usage text, CSV helpers, session manifests, or explicit DTO-based result formatting.
- Decision: Keep the managed CLI structure because it is orchestration and tooling around the port, not a parity target.
- Status: complete

### `src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs`

- File: `src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: diagnostic WRITE lineage`, `tools/fortran-debug/json_trace.f :: parity trace instrumentation`
- Methods Audited: `JsonlTraceWriter` constructors, `Write`, `WriteLine`, `Scope`, `WriteEvent`, `WriteRecord`, `EnqueuePending`, `FlushPendingRecords`, `WriteArray`, `Dispose`, `CreateWriter`, `TraceScope` constructor, `TraceScope.Dispose`, `PreciseSingleJsonConverter.Read`, `PreciseSingleJsonConverter.Write`, `TraceFilterSettings.FromEnvironment`, `TraceSelector.FromEnvironment`, `TraceSelector.Matches`, `MultiplexTextWriter` constructor, `Write`, `WriteLine`, `Flush`, `Dispose`, `TryGetWriter`, `SolverTrace.Begin` overloads, `Suspend`, `Scope`, `Event`, `Array`, `Point`, `ScopeName`, `RestoreScope` constructor, `RestoreScope.Dispose`, `EmptyScope.Dispose`
- Parity-sensitive blocks: `PreciseSingleJsonConverter float-to-double write path`, `SolverTrace ambient scope plumbing`, `JSONL event sequencing`, `env-driven kind/scope/station filters`, `trigger-gated ring buffer`
- Differences from legacy: The file replaces ad hoc text diagnostics with structured JSONL records, explicit runtime/session metadata, float-preserving serialization, multiplexed sinks, ambient tracing helpers, and env-driven trace filters plus a triggerable pre-trigger ring buffer. It is derived from the needs of parity instrumentation rather than from a direct legacy subsystem.
- Decision: Keep the managed diagnostics stack because structured tracing is essential for parity work and has no direct Fortran runtime counterpart. Preserve the shared filter and ring-buffer semantics because they now define how focused parity traces are captured without deleting callsites.
- Status: complete

### `src/XFoil.Solver/Models/AnalysisSettings.cs`

- File: `src/XFoil.Solver/Models/AnalysisSettings.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/XFOIL.INC :: operating-point and solver control state`, `f_xfoil/src/xbl.f :: NCrit and viscous iteration control lineage`
- Methods Audited: `AnalysisSettings` constructor, `GetEffectiveNCrit`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port validates, defaults, and packages solver controls explicitly instead of scattering them across interactive command state and COMMON blocks.
- Decision: Keep the managed settings object because it is the correct public API boundary for solver configuration.
- Status: complete

### `src/XFoil.Solver/Models/BoundaryLayerBranch.cs`

- File: `src/XFoil.Solver/Models/BoundaryLayerBranch.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/XFOIL.INC :: side-1/side-2/wake indexing`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port replaces implicit side and wake indices with a typed enum.
- Decision: Keep the enum because it makes branch identity explicit without affecting parity arithmetic.
- Status: complete

### `src/XFoil.Solver/Models/BoundaryLayerProfile.cs`

- File: `src/XFoil.Solver/Models/BoundaryLayerProfile.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: extracted boundary-layer station/profile arrays`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages one branch of boundary-layer data and transition metadata into an immutable object instead of leaving the same information spread across arrays.
- Decision: Keep the managed container because it simplifies results, testing, and diagnostics.
- Status: complete

### `src/XFoil.Solver/Models/BoundaryLayerStation.cs`

- File: `src/XFoil.Solver/Models/BoundaryLayerStation.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: per-station XI/UEDG state arrays`
- Methods Audited: `BoundaryLayerStation` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port uses an explicit object per station instead of parallel arrays.
- Decision: Keep the managed DTO because it improves readability and transport.
- Status: complete

### `src/XFoil.Solver/Models/BoundaryLayerSystemState.cs`

- File: `src/XFoil.Solver/Models/BoundaryLayerSystemState.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/XFOIL.INC :: UEDG/THET/DSTR/CTAU/MASS/XSSI/IPAN/VTI/ITRAN/IBLTE/NBL`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: BL system workspace usage`
- Methods Audited: `BoundaryLayerSystemState` constructor, `InitializeForStationCounts`
- Parity-sensitive blocks: `named legacy array layout`, `parity-only pre-accept BLKIN snapshots`
- Differences from legacy: The workspace follows the legacy data layout but names the arrays explicitly, uses 0-based indexing, and adds explicit parity snapshot storage.
- Decision: Keep the managed workspace because it preserves the solver layout while making the state inspectable and testable.
- Status: complete

### `src/XFoil.Solver/Models/BoundaryLayerTopology.cs`

- File: `src/XFoil.Solver/Models/BoundaryLayerTopology.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: STFIND/IBLPAN`, `f_xfoil/src/xbl.f :: IBLSYS topology lineage`
- Methods Audited: `BoundaryLayerTopology` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages stagnation and station topology into one immutable result instead of reconstructing it from arrays and indices on demand.
- Decision: Keep the managed topology object because it is useful for diagnostics and tests.
- Status: complete

### `src/XFoil.Solver/Models/DisplacementCoupledResult.cs`

- File: `src/XFoil.Solver/Models/DisplacementCoupledResult.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `DisplacementCoupledResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The object records the outcome of an older managed surrogate workflow that had no direct classic XFoil counterpart.
- Decision: Keep the DTO because it documents and transports the historical managed workflow cleanly.
- Status: complete

### `src/XFoil.Solver/Models/DragDecomposition.cs`

- File: `src/XFoil.Solver/Models/DragDecomposition.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: CDCALC`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns drag components as one structured record instead of leaving them in transient scalars and print buffers.
- Decision: Keep the managed decomposition because it improves diagnostics and tests.
- Status: complete

### `src/XFoil.Solver/Models/InviscidAnalysisResult.cs`

- File: `src/XFoil.Solver/Models/InviscidAnalysisResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: ALFA operating-point output`
- Methods Audited: `InviscidAnalysisResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages one inviscid operating point into an immutable result instead of distributing the same information across solver arrays and report scalars.
- Decision: Keep the managed result object because it is the clean API boundary for inviscid analysis.
- Status: complete

### `src/XFoil.Solver/Models/InviscidLiftSweepResult.cs`

- File: `src/XFoil.Solver/Models/InviscidLiftSweepResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: CLI/CSEQ operating-point sweep lineage`
- Methods Audited: `InviscidLiftSweepResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns a stable sweep object instead of accumulating points only in interactive polar buffers.
- Decision: Keep the managed sweep object because it is better for batch APIs and tests.
- Status: complete

### `src/XFoil.Solver/Models/InviscidSolverState.cs`

- File: `src/XFoil.Solver/Models/InviscidSolverState.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: QISET/GAMQV/QVFUE workspace arrays`
- Secondary Fortran Reference(s): `f_xfoil/src/XFOIL.INC :: AIJ/BIJ/GAM/SIG/QINV state`
- Methods Audited: `InviscidSolverState` constructor, `InitializeForNodeCount`
- Parity-sensitive blocks: `legacy AIJ/BIJ/GAM workspace layout`, `legacy single-precision factor storage`
- Differences from legacy: The managed port keeps the same broad workspace structure but exposes the arrays as named properties, resets them explicitly, and carries parity-only float factors alongside the default double path.
- Decision: Keep the managed workspace because it preserves the solver layout while remaining inspectable and reusable.
- Status: complete

### `src/XFoil.Solver/Models/InviscidSolverType.cs`

- File: `src/XFoil.Solver/Models/InviscidSolverType.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: classic inviscid kernel lineage`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The enum exposes solver selection as a managed extension point, which classic XFoil did not provide in this typed form.
- Decision: Keep the enum because solver choice is an intentional managed capability.
- Status: complete

### `src/XFoil.Solver/Models/InviscidTargetLiftResult.cs`

- File: `src/XFoil.Solver/Models/InviscidTargetLiftResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: CLI target-lift operating-point lineage`
- Methods Audited: `InviscidTargetLiftResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns the requested lift coefficient and the solved operating point together instead of leaving that relationship implicit in terminal output and state.
- Decision: Keep the managed DTO because it clarifies target versus solved values.
- Status: complete

### `src/XFoil.Solver/Models/LinearVortexInviscidResult.cs`

- File: `src/XFoil.Solver/Models/LinearVortexInviscidResult.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/orrs/src :: linear-vorticity formulation lineage`, `f_xfoil/src/xpanel.f :: inviscid coefficient output lineage`
- Methods Audited: `LinearVortexInviscidResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: This result shape belongs to the alternative managed linear-vorticity solver rather than to the classic XFoil Hess-Smith runtime.
- Decision: Keep the managed result object because it is the correct contract for the alternative solver.
- Status: complete

### `src/XFoil.Solver/Models/LinearVortexPanelState.cs`

- File: `src/XFoil.Solver/Models/LinearVortexPanelState.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/orrs/src :: linear-vorticity geometry workspace lineage`, `f_xfoil/src/xpanel.f :: surface geometry arrays`
- Methods Audited: `LinearVortexPanelState` constructor, `Resize`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port owns and validates the alternative solver's geometry arrays explicitly instead of relying on shared work arrays and local conventions.
- Decision: Keep the managed workspace because it is the cleanest API for the alternative solver.
- Status: complete

### `src/XFoil.Solver/Models/Panel.cs`

- File: `src/XFoil.Solver/Models/Panel.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: panel endpoint and normal/tangent geometry arrays`
- Methods Audited: `Panel` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port stores panel geometry as one explicit object instead of across parallel arrays.
- Decision: Keep the managed DTO because it improves readability and reuse.
- Status: complete

### `src/XFoil.Solver/Models/PanelMesh.cs`

- File: `src/XFoil.Solver/Models/PanelMesh.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: node/panel geometry buffers`
- Methods Audited: `PanelMesh` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages nodes, panels, and orientation metadata into one mesh object instead of leaving them as related arrays.
- Decision: Keep the managed mesh container because it is the right geometry contract for solver consumers.
- Status: complete

### `src/XFoil.Solver/Models/PanelingOptions.cs`

- File: `src/XFoil.Solver/Models/PanelingOptions.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: PANGEN weighting controls`
- Methods Audited: `PanelingOptions` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port validates and packages paneling weights as one immutable object instead of letting them live in interactive paneling state.
- Decision: Keep the managed options object because it makes paneling behavior explicit and testable.
- Status: complete

### `src/XFoil.Solver/Models/PolarPoint.cs`

- File: `src/XFoil.Solver/Models/PolarPoint.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: PACC/PWRT operating-point storage`
- Methods Audited: `PolarPoint` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port uses an immutable object per polar point instead of polar arrays and save buffers.
- Decision: Keep the managed DTO because it is clearer for sweeps, exports, and tests.
- Status: complete

### `src/XFoil.Solver/Models/PolarSweepMode.cs`

- File: `src/XFoil.Solver/Models/PolarSweepMode.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: Type 1/2/3 polar modes`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port replaces numeric polar-type codes with a typed enum.
- Decision: Keep the enum because it makes the sweep mode explicit in the public API.
- Status: complete

### `src/XFoil.Solver/Models/PolarSweepResult.cs`

- File: `src/XFoil.Solver/Models/PolarSweepResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: ASEQ operating-point sweep lineage`, `f_xfoil/src/xoper.f :: PACC/PWRT`
- Methods Audited: `PolarSweepResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns a stable sweep object instead of accumulating points only in interactive polar buffers.
- Decision: Keep the managed sweep object because it is better for batch use and tests.
- Status: complete

### `src/XFoil.Solver/Models/PreparedInviscidSystem.cs`

- File: `src/XFoil.Solver/Models/PreparedInviscidSystem.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: QISET influence and basis setup lineage`
- Methods Audited: `PreparedInviscidSystem` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages preassembled influence matrices and basis solutions for reuse instead of keeping them only in solver work arrays.
- Decision: Keep the managed container because it is the right reuse boundary for prepared inviscid solves.
- Status: complete

### `src/XFoil.Solver/Models/PressureCoefficientSample.cs`

- File: `src/XFoil.Solver/Models/PressureCoefficientSample.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: CPWRIT pressure output lineage`
- Methods Audited: `PressureCoefficientSample` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns Cp samples as explicit objects with location and corrected/unadjusted values instead of writing them directly from arrays.
- Decision: Keep the managed DTO because it simplifies exports and diagnostics.
- Status: complete

### `src/XFoil.Solver/Models/TransitionInfo.cs`

- File: `src/XFoil.Solver/Models/TransitionInfo.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: TRCHEK2 and forced-transition state`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages transition location, trigger type, amplification, and convergence into one value object instead of leaving that information distributed across station indices and flags.
- Decision: Keep the managed DTO because it makes transition reporting explicit and testable.
- Status: complete

### `src/XFoil.Solver/Models/ViscousAnalysisResult.cs`

- File: `src/XFoil.Solver/Models/ViscousAnalysisResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: viscous operating-point state`, `f_xfoil/src/xfoil.f :: CDCALC reporting lineage`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages drag, transition, profile, and convergence outputs into one immutable operating-point result instead of leaving them across many solver arrays and report scalars.
- Decision: Keep the managed result object because it is the correct API boundary for viscous analysis output.
- Status: complete

### `src/XFoil.Solver/Models/ViscousBranchSeed.cs`

- File: `src/XFoil.Solver/Models/ViscousBranchSeed.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: COMSET/MRCHUE seed lineage`
- Methods Audited: `ViscousBranchSeed` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages one seed branch into an explicit object instead of keeping the same state in arrays.
- Decision: Keep the managed DTO because it clarifies seed ownership and branch identity.
- Status: complete

### `src/XFoil.Solver/Models/ViscousBranchState.cs`

- File: `src/XFoil.Solver/Models/ViscousBranchState.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: per-branch viscous state arrays`
- Methods Audited: `ViscousBranchState` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages one solved branch into an immutable object instead of leaving it in parallel arrays.
- Decision: Keep the managed DTO because it simplifies result transport and testing.
- Status: complete

### `src/XFoil.Solver/Models/ViscousConvergenceInfo.cs`

- File: `src/XFoil.Solver/Models/ViscousConvergenceInfo.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: UPDATE/BLSOLV convergence diagnostics`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port stores per-iteration convergence metrics as explicit history records instead of only reporting them transiently.
- Decision: Keep the managed record because it improves diagnostics and regression tests.
- Status: complete

### `src/XFoil.Solver/Models/ViscousCorrectionResult.cs`

- File: `src/XFoil.Solver/Models/ViscousCorrectionResult.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `ViscousCorrectionResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: This result type belongs to an older managed surrogate workflow rather than to a direct classic XFoil runtime path.
- Decision: Keep the DTO because it documents that historical managed workflow cleanly.
- Status: complete

### `src/XFoil.Solver/Models/ViscousFlowRegime.cs`

- File: `src/XFoil.Solver/Models/ViscousFlowRegime.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: laminar/turbulent/wake regime selection`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port replaces implicit regime flags with a typed enum.
- Decision: Keep the enum because it makes regime transitions explicit.
- Status: complete

### `src/XFoil.Solver/Models/ViscousInteractionResult.cs`

- File: `src/XFoil.Solver/Models/ViscousInteractionResult.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `ViscousInteractionResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The object records the outcome of an older managed outer interaction workflow that was not a direct legacy runtime result type.
- Decision: Keep the DTO because it captures historical managed behavior explicitly.
- Status: complete

### `src/XFoil.Solver/Models/ViscousIntervalKind.cs`

- File: `src/XFoil.Solver/Models/ViscousIntervalKind.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: laminar/turbulent/wake interval equations`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port stores the interval-equation family explicitly instead of inferring it only from transition logic.
- Decision: Keep the enum because it makes interval assembly intent visible.
- Status: complete

### `src/XFoil.Solver/Models/ViscousIntervalState.cs`

- File: `src/XFoil.Solver/Models/ViscousIntervalState.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: BLDIF/TRDIF interval state lineage`
- Methods Audited: `ViscousIntervalState` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages one interval's assembled state and residuals into an explicit object instead of leaving them transient inside monolithic routines.
- Decision: Keep the managed DTO because it is valuable for diagnostics and tests.
- Status: complete

### `src/XFoil.Solver/Models/ViscousIntervalSystem.cs`

- File: `src/XFoil.Solver/Models/ViscousIntervalSystem.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: assembled interval-system lineage`
- Methods Audited: `ViscousIntervalSystem` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages interval-system state into one explicit object instead of leaving it in solver arrays.
- Decision: Keep the managed container because it makes older workflow states inspectable.
- Status: complete

### `src/XFoil.Solver/Models/ViscousLiftSweepResult.cs`

- File: `src/XFoil.Solver/Models/ViscousLiftSweepResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: CSEQ/VISC lift-sweep lineage`
- Methods Audited: `ViscousLiftSweepResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns a stable sweep object instead of accumulating points only in interactive polar buffers.
- Decision: Keep the managed sweep container because it fits batch APIs and tests better.
- Status: complete

### `src/XFoil.Solver/Models/ViscousNewtonSystem.cs`

- File: `src/XFoil.Solver/Models/ViscousNewtonSystem.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xsolve.f :: BLSOLV`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: SETBL/UPDATE system-assembly usage`
- Methods Audited: `ViscousNewtonSystem` constructors, `MaxWake`, `SetupISYS`
- Parity-sensitive blocks: `BLSOLV array layout`, `ISYS line-map copy`
- Differences from legacy: The managed port preserves the broad BLSOLV layout but names the arrays explicitly, uses global line indexing, and adds compatibility helpers for clearer ownership.
- Decision: Keep the managed workspace because it preserves the BLSOLV structure while making the system state auditable and reusable.
- Status: complete

### `src/XFoil.Solver/Models/ViscousPolarPoint.cs`

- File: `src/XFoil.Solver/Models/ViscousPolarPoint.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: saved-polar operating-point reporting lineage`
- Methods Audited: `ViscousPolarPoint` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages the headline quantities of one viscous polar point into an immutable object instead of print buffers and temporary scalars.
- Decision: Keep the managed DTO because it simplifies exports and tests.
- Status: complete

### `src/XFoil.Solver/Models/ViscousPolarSweepResult.cs`

- File: `src/XFoil.Solver/Models/ViscousPolarSweepResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: ASEQ/VISC sweep lineage`, `f_xfoil/src/xoper.f :: PACC/PWRT`
- Methods Audited: `ViscousPolarSweepResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns a stable sweep object instead of storing points only in interactive polar buffers.
- Decision: Keep the managed sweep container because it is better suited to batch use and testing.
- Status: complete

### `src/XFoil.Solver/Models/ViscousSolveResult.cs`

- File: `src/XFoil.Solver/Models/ViscousSolveResult.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: viscous Newton solve convergence lineage`
- Methods Audited: `ViscousSolveResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages initial/final systems and residual summaries explicitly instead of leaving them transient in solver locals.
- Decision: Keep the managed DTO because it preserves useful diagnostics for tests and tooling.
- Status: complete

### `src/XFoil.Solver/Models/ViscousSolverMode.cs`

- File: `src/XFoil.Solver/Models/ViscousSolverMode.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: RLXBL adaptive relaxation lineage`
- Methods Audited: `none`
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port exposes both the classic relaxation path and a trust-region strategy as a typed public choice.
- Decision: Keep the enum because strategy selection is an intentional managed capability.
- Status: complete

### `src/XFoil.Solver/Models/ViscousStateEstimate.cs`

- File: `src/XFoil.Solver/Models/ViscousStateEstimate.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: assembled viscous state arrays`
- Methods Audited: `ViscousStateEstimate` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages the solved upper, lower, and wake branch states into one explicit object instead of leaving them in distributed arrays.
- Decision: Keep the managed DTO because it clarifies the solved-state boundary.
- Status: complete

### `src/XFoil.Solver/Models/ViscousStateSeed.cs`

- File: `src/XFoil.Solver/Models/ViscousStateSeed.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: COMSET/MRCHUE seed lineage`
- Methods Audited: `ViscousStateSeed` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages the full upper, lower, and wake seed state plus trailing-edge gap metadata into one explicit object instead of several arrays and scalars.
- Decision: Keep the managed DTO because it makes seed transport and inspection straightforward.
- Status: complete

### `src/XFoil.Solver/Models/ViscousStationDerivedState.cs`

- File: `src/XFoil.Solver/Models/ViscousStationDerivedState.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: BLVAR/BLMID derived-state correlations`
- Methods Audited: `ViscousStationDerivedState` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages derived kinematic and compressibility quantities explicitly instead of leaving them transient inside the boundary-layer kernels.
- Decision: Keep the managed DTO because it improves solver observability.
- Status: complete

### `src/XFoil.Solver/Models/ViscousStationSeed.cs`

- File: `src/XFoil.Solver/Models/ViscousStationSeed.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xoper.f :: MRCHUE seed-station lineage`
- Methods Audited: `ViscousStationSeed` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages one seed station into an immutable object instead of separate seed arrays.
- Decision: Keep the managed DTO because it simplifies seed tracing and testing.
- Status: complete

### `src/XFoil.Solver/Models/ViscousStationState.cs`

- File: `src/XFoil.Solver/Models/ViscousStationState.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f :: THET/DSTR/H/CF/Re_theta per-station state arrays`
- Methods Audited: `ViscousStationState` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port uses an immutable object per solved station instead of parallel arrays and side-index conventions.
- Decision: Keep the managed DTO because it improves readability and result transport.
- Status: complete

### `src/XFoil.Solver/Models/ViscousTargetLiftResult.cs`

- File: `src/XFoil.Solver/Models/ViscousTargetLiftResult.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f :: CLI/VISC target-lift operating-point lineage`
- Methods Audited: `ViscousTargetLiftResult` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port returns the requested viscous lift coefficient, solved angle, and operating point together instead of leaving that relationship implicit in interactive output.
- Decision: Keep the managed DTO because it makes target versus solved values explicit.
- Status: complete

### `src/XFoil.Solver/Models/WakeGeometry.cs`

- File: `src/XFoil.Solver/Models/WakeGeometry.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: wake geometry arrays`
- Methods Audited: `WakeGeometry` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages wake points into one immutable object instead of leaving them in arrays and plot buffers.
- Decision: Keep the managed DTO because it is the clean API contract for wake consumers.
- Status: complete

### `src/XFoil.Solver/Models/WakePoint.cs`

- File: `src/XFoil.Solver/Models/WakePoint.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: wake-point coordinate and tangent arrays`
- Methods Audited: `WakePoint` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: The managed port packages one wake point into an explicit object instead of parallel arrays.
- Decision: Keep the managed DTO because it improves diagnostics and downstream use.
- Status: complete

### `src/XFoil.Solver/Models/BoundaryLayerCorrelationConstants.cs`

- File: `src/XFoil.Solver/Models/BoundaryLayerCorrelationConstants.cs`
- Category: `dto/model`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: HST/CFT/DIT correlation constants`
- Methods Audited: `BoundaryLayerCorrelationConstants` constructor
- Parity-sensitive blocks: `none`
- Differences from legacy: Legacy XFoil inlines these coefficients inside correlation routines, while the managed port packages them as a reusable constants object with an explicit default set and derived `CtConstant`.
- Decision: Keep the managed constants object because it makes correlation tuning and testing explicit.
- Status: complete

### `src/XFoil.Solver/Numerics/BandMatrixSolver.cs`

- File: `src/XFoil.Solver/Numerics/BandMatrixSolver.cs`
- Category: `managed-only`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `f_xfoil/src/xsolve.f :: BLSOLV`
- Methods Audited: `Solve`
- Parity-sensitive blocks: `none`
- Differences from legacy: The helper reuses the viscous Newton system but solves it through an explicit tridiagonal reduction and Thomas sweep rather than replaying the original BLSOLV block elimination.
- Decision: Keep the managed alternative because it is useful for comparison and debugging, but it is not the parity reference path.
- Status: complete

### `src/XFoil.Solver/Numerics/DenseLinearSystemSolver.cs`

- File: `src/XFoil.Solver/Numerics/DenseLinearSystemSolver.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/xsolve.f :: GAUSS`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Solve` overloads, `SolveCore`
- Parity-sensitive blocks: `parity-only single-precision solve`, `separate versus fused multiply-subtract staging`
- Differences from legacy: The managed port preserves the GAUSS elimination order but exposes shared generic float/double control flow, explicit validation, and parity-sensitive arithmetic staging through `LegacyPrecisionMath`.
- Decision: Keep the managed generic solver because it preserves the GAUSS algorithm while making precision control explicit.
- Current parity note: the standalone `GAUSS` oracle now runs 1024 randomized matrices per batch in addition to the explicit fixtures, and the current parity replay still requires the separated multiply-subtract updates on both elimination and back-substitution legs.
- Status: complete

### `src/XFoil.Solver/Numerics/CubicSpline.cs`

- File: `src/XFoil.Solver/Numerics/CubicSpline.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/spline.f :: SPLIND/SEVAL`
- Secondary Fortran Reference(s): `f_xfoil/src/spline.f :: SCALC`
- Methods Audited: `CubicSpline` constructor, `Evaluate`, `FindUpperIndex`, `ComputeNaturalSlopes`, `SolveTriDiagonal`
- Parity-sensitive blocks: `none`
- Differences from legacy: The helper follows the same natural-spline lineage but packages fit and evaluation logic into an object with explicit parameter/value arrays and a local tridiagonal solve.
- Decision: Keep the managed spline helper because it preserves the spline workflow while fitting the .NET API surface.
- Status: complete

### `src/XFoil.Solver/Numerics/TridiagonalSolver.cs`

- File: `src/XFoil.Solver/Numerics/TridiagonalSolver.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/spline.f :: TRISOL`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Solve` overloads, `SolveCore`, `GetPrecisionLabel`, `TraceForwardElimination`, `TraceLastPivot`, `TraceBackSubstitution`
- Parity-sensitive blocks: `float parity solve path`, `fused forward/backward recurrence staging`
- Differences from legacy: The managed port preserves the TRISOL Thomas algorithm but exposes float/double/generic entry points and standardized trace hooks instead of relying on one implicit precision mode and unstructured runtime state.
- Decision: Keep the managed shared solver because it preserves the TRISOL algorithm while making precision and tracing explicit.
- Status: complete

### `src/XFoil.Solver/Numerics/ScaledPivotLuSolver.cs`

- File: `src/XFoil.Solver/Numerics/ScaledPivotLuSolver.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/xsolve.f :: LUDCMP/BAKSUB`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `Decompose` overloads, `BackSubstitute` overloads, `DecomposeCore`, `BackSubstituteCore`, `TracePivotSelection`, `TraceBackSubstituteRow`, `TraceBackSubstituteTerm`
- Parity-sensitive blocks: `float parity LU path`, `fused subtract staging in decomposition`, `row/term trace hooks`
- Differences from legacy: The managed port preserves the scaled-pivot LU algorithm but exposes float/double entry points, a shared generic core, and structured trace hooks instead of relying on one implicit precision build and ad hoc debugging.
- Decision: Keep the managed shared solver because it preserves the LUDCMP/BAKSUB algorithm while making precision and tracing explicit.
- Status: complete

### `src/XFoil.Solver/Numerics/LegacyPrecisionMath.cs`

- File: `src/XFoil.Solver/Numerics/LegacyPrecisionMath.cs`
- Category: `legacy-derived`
- Primary Fortran Reference: `f_xfoil/src/XFOIL.INC :: REAL arithmetic lineage`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f :: BLKIN/BLVAR/BLDIF scalar staging`, `f_xfoil/src/xsolve.f :: GAUSS/LUDCMP/BAKSUB contraction-sensitive updates`, `f_xfoil/src/xpanel.f :: streamfunction and panel-kernel REAL staging`
- Methods Audited: `GammaMinusOne`, `RoundToSingle` overloads, `Multiply` overloads, `Add`, `Subtract`, `Negate`, `Divide`, `Average`, `Square`, `Sqrt`, `Pow`, `Exp`, `Log`, `Log10`, `Tanh`, `Sin`, `Cos`, `Atan2`, `Abs`, `Max`, `Min`, `AddScaled`, `MultiplyAdd`, `MultiplySubtract`, `ProductThenAdd`, `ProductThenSubtract`, `SumOfProducts` overloads, `SourceOrderedProductSum` overloads, `SourceOrderedProductSumAdd`, `FusedMultiplyAdd`, generic `SumOfProducts` overloads, `SumOfProductsAndAdd`, `WeightedProductBlend`, `DifferenceOfProducts`, `FusedMultiplySubtract`, generic `ProductThenAdd`, generic `ProductThenSubtract`, `SeparateMultiplySubtract`
- Parity-sensitive blocks: `scalar REAL staging helpers`, `source-ordered product sums`, `explicit fused helper family`, `separate multiply-subtract GAUSS path`
- Differences from legacy: The legacy code relies on source-level REAL declarations and native contraction behavior, while the managed port spells those arithmetic choices out as named helpers and keeps the default runtime on double.
- Decision: Keep the shared helper surface because it gives the audit one central place to enforce parity arithmetic policy without degrading the default managed path.
- Current parity note: the alpha-10 similarity-seed micro-rigs proved parity-only unary negation needs an explicit helper because `Subtract(0.0, x, true)` does not preserve legacy `-0.0f` bits.
- Status: complete

### `src/XFoil.Solver/Numerics/ParametricSpline.cs`

- File: `src/XFoil.Solver/Numerics/ParametricSpline.cs`
- Category: `legacy-direct`
- Primary Fortran Reference: `f_xfoil/src/spline.f :: SPLINE/SPLIND/SEGSPL/SEVAL/DEVAL/SINVRT/SCALC`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `SplineBoundaryCondition` constructor, `SpecifiedDerivative`, `FitWithZeroSecondDerivativeBCs` overloads, `FitWithBoundaryConditions` overloads, `FitWithBoundaryConditionsCore`, `FitSegmented` overloads, `Evaluate` overloads, `EvaluateDerivative` overloads, `ComputeArcLength` overloads, `InvertSpline`, `ApplyStartBc`, `ApplyEndBc`, `FitSegment`, `FitSegmentedCore`, `EvaluateCore`, `EvaluateDerivativeCore`, `ComputeArcLengthCore`, `FindUpperIndex`, `DescribeBoundaryCondition`, `GetPrecisionLabel`, `TraceSplineEvaluation`, `TraceSplineSegment`, `TraceSplineSystemRow`, `TraceSplineSolutionNode`, `TraceArcLengthStep`, `ComputeDistance`
- Parity-sensitive blocks: `SEVAL contracted CX/CUBFAC replay`, `DEVAL mixed FAC1/FAC2 staging`, `SPLIND row tracing`, `SCALC distance staging`
- Differences from legacy: The algorithms remain aligned with the spline.f family, but the managed port packages boundary conditions as a typed value, shares float/double/generic paths, and adds structured tracing instead of relying on implicit work arrays and ad hoc debugging.
- Decision: Keep the managed spline utility because it preserves the spline.f algorithms while making precision, boundary conditions, and tracing explicit.
- Current parity note: the dedicated standalone spline driver now passes bitwise. The proved float replay is mixed: `CX1`, `CX2`, `SEVAL CUBFAC`, and `DEVAL FAC1` all land on the contracted native path, while `DEVAL FAC2` still follows source-ordered float staging and the final accumulations stay unfused.
- Status: complete

### `tests/XFoil.Core.Tests/AirfoilAnalysisServiceTests.cs`

- File: `tests/XFoil.Core.Tests/AirfoilAnalysisServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f :: OPER/ALFA/CLI/ASEQ/CSEQ`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f :: PANGEN`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed xUnit regression coverage of the public analysis facade rather than the interactive legacy command loop.
- Decision: Keep the managed test harness because it verifies the ported behavior at the public API boundary.
- Status: complete

### `tests/XFoil.Core.Tests/AirfoilDatExporterTests.cs`

- File: `tests/XFoil.Core.Tests/AirfoilDatExporterTests.cs`
- Category: `test`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed-only serialization tests for deterministic DAT formatting and filesystem behavior with no direct Fortran analogue.
- Decision: Keep the managed-only harness because DAT export is a .NET-facing compatibility feature, not a legacy solver routine.
- Status: complete

### `tests/XFoil.Core.Tests/AirfoilParserTests.cs`

- File: `tests/XFoil.Core.Tests/AirfoilParserTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f :: LOAD`
- Secondary Fortran Reference(s): `f_xfoil/src/userio.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed parser tests operate on in-memory lines instead of the legacy interactive file loader.
- Decision: Keep the managed harness because it isolates preserved file-format recognition rules more cleanly.
- Status: complete

### `tests/XFoil.Core.Tests/AnalysisSessionRunnerTests.cs`

- File: `tests/XFoil.Core.Tests/AnalysisSessionRunnerTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f :: OPER/ASEQ/CSEQ`
- Secondary Fortran Reference(s): `legacy polar import/export workflows`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed integration tests cover JSON-manifest batch orchestration, which wraps legacy-derived analysis flows rather than replaying the legacy UI.
- Decision: Keep the managed harness because batch sessions are an intentional port extension over legacy workflows.
- Status: complete

### `tests/XFoil.Core.Tests/BasicGeometryTransformServiceTests.cs`

- File: `tests/XFoil.Core.Tests/BasicGeometryTransformServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: ROTATE/NORM/DERO/SCAL`
- Secondary Fortran Reference(s): `f_xfoil/src/geom.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests exercise explicit geometry-transform service methods instead of the legacy GDES command loop.
- Decision: Keep the managed harness because it validates preserved geometry-edit behavior through the refactored API.
- Status: complete

### `tests/XFoil.Core.Tests/BlockTridiagonalSolverTests.cs`

- File: `tests/XFoil.Core.Tests/BlockTridiagonalSolverTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xblsolv.f :: BLSOLV`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `BLSOLV block elimination`, `VACCEL branch`, `banded cross-check`
- Differences from legacy: Managed unit tests isolate the reusable numeric component rather than reaching the solver only through a full viscous run.
- Decision: Keep the managed harness because it gives direct coverage of the ported linear solver and its parity-sensitive branches.
- Status: complete

### `tests/XFoil.Core.Tests/BoundaryLayerCorrelationsTests.cs`

- File: `tests/XFoil.Core.Tests/BoundaryLayerCorrelationsTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xblsys.f :: HKIN/HSL/HST/CFL/CFT/DIL/DILW/HCT/DIT`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `all scalar correlation branches`, `all analytical Jacobians`
- Differences from legacy: Managed tests validate the extracted tuple-returning correlation helpers and their derivatives directly instead of only through assembled viscous states.
- Decision: Keep the managed harness because direct formula and Jacobian checks are the strongest parity protection for these kernels.
- Status: complete

### `tests/XFoil.Core.Tests/BoundaryLayerSystemAssemblerTests.cs`

- File: `tests/XFoil.Core.Tests/BoundaryLayerSystemAssemblerTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xblsys.f :: BLPRV/BLKIN/BLVAR/BLMID/BLDIF/TESYS/BLSYS`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `compressibility conversion`, `station-variable dispatch`, `finite-difference residual/Jacobian assembly`, `TE system coupling`
- Differences from legacy: Managed tests exercise extracted helper methods and assembled block outputs directly instead of only through end-to-end Newton solves.
- Decision: Keep the managed harness because it isolates the main viscous assembly kernel and its parity-sensitive Jacobians.
- Status: complete

### `tests/XFoil.Core.Tests/BoundaryLayerTopologyTests.cs`

- File: `tests/XFoil.Core.Tests/BoundaryLayerTopologyTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xbl.f :: STFIND/UICALC`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `stagnation location`, `branch-distance monotonicity`
- Differences from legacy: Managed tests inspect typed topology results rather than transient arrays in the legacy viscous setup.
- Decision: Keep the managed harness because it validates preserved topology rules at the public API boundary.
- Status: complete

### `tests/XFoil.Core.Tests/CompressibilityTests.cs`

- File: `tests/XFoil.Core.Tests/CompressibilityTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f compressibility correction path`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests compare corrected versus uncorrected diagnostics on typed inviscid results instead of reading legacy command output.
- Decision: Keep the managed harness because it directly protects the ported diagnostic contract.
- Status: complete

### `tests/XFoil.Core.Tests/ConformalMapgenServiceTests.cs`

- File: `tests/XFoil.Core.Tests/ConformalMapgenServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xmapgen.f :: MAPGEN`
- Secondary Fortran Reference(s): `f_xfoil/src/xqdes.f`
- Methods Audited: `all [Fact]/[Theory] methods in file`
- Parity-sensitive blocks: `MAPGEN convergence`, `TE gap/angle targets`, `harmonic filtering`
- Differences from legacy: Managed tests drive the conformal map generator through structured service calls and result objects instead of the inverse-design command environment.
- Decision: Keep the managed harness because it validates the MAPGEN port and the explicit diagnostics added by the port.
- Status: complete

### `tests/XFoil.Core.Tests/ContourEditServiceTests.cs`

- File: `tests/XFoil.Core.Tests/ContourEditServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: ADDP/MOVP/DELP and corner refinement workflow`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests call explicit contour-edit service methods and inspect structured results instead of mutating the legacy editor state.
- Decision: Keep the managed harness because it validates the refactored geometry-edit API while preserving legacy edit semantics.
- Status: complete

### `tests/XFoil.Core.Tests/ContourModificationServiceTests.cs`

- File: `tests/XFoil.Core.Tests/ContourModificationServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f contour modification workflow`
- Secondary Fortran Reference(s): `f_xfoil/src/spline.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests validate immutable contour modification results and explicit options rather than the legacy editor's mutable state.
- Decision: Keep the managed harness because it protects the refactored API without changing the geometric intent.
- Status: complete

### `tests/XFoil.Core.Tests/CurvatureAdaptivePanelDistributorTests.cs`

- File: `tests/XFoil.Core.Tests/CurvatureAdaptivePanelDistributorTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: PANGEN`
- Secondary Fortran Reference(s): `f_xfoil/src/spline.f`
- Methods Audited: `all [Fact]/[Theory] methods in file`
- Parity-sensitive blocks: `node ordering`, `leading-edge/trailing-edge clustering`, `arc-length and chord preservation`
- Differences from legacy: Managed tests exercise the extracted panel distributor directly instead of only through panel generation side effects.
- Decision: Keep the managed harness because it isolates the redistributed-node behavior of the PANGEN port.
- Status: complete

### `tests/XFoil.Core.Tests/DiagnosticDumpTests.cs`

- File: `tests/XFoil.Core.Tests/DiagnosticDumpTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xbl.f and xblsys.f diagnostic state lineage`
- Secondary Fortran Reference(s): `tools/fortran-debug`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `full reference-case diagnostic dump`
- Differences from legacy: Managed-only trace/dump harness writes structured artifacts that did not exist in the legacy runtime.
- Decision: Keep the managed diagnostics suite because it is essential to parity debugging and solver-fidelity work.
- Status: complete

### `tests/XFoil.Core.Tests/DragCalculatorTests.cs`

- File: `tests/XFoil.Core.Tests/DragCalculatorTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f drag accumulation and Squire-Young output`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `CD/CDF/CDP decomposition`, `wave drag branch`, `extended wake drag`, `managed post-stall extrapolator`
- Differences from legacy: Managed tests validate the structured drag decomposition and the managed-only post-stall extension directly.
- Decision: Keep the managed harness because it protects both the legacy-derived drag formulas and the deliberate post-stall extension.
- Status: complete

### `tests/XFoil.Core.Tests/EdgeVelocityCalculatorTests.cs`

- File: `tests/XFoil.Core.Tests/EdgeVelocityCalculatorTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xbl.f :: STFIND/UICALC/STMOVE`
- Secondary Fortran Reference(s): `f_xfoil/src/xwake.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `stagnation search`, `station mapping`, `edge-speed conversions`, `wake velocity helper`
- Differences from legacy: Managed tests exercise extracted helper routines directly instead of only through the viscous march.
- Decision: Keep the managed harness because it isolates parity-sensitive preprocessing helpers cleanly.
- Status: complete

### `tests/XFoil.Core.Tests/FlapDeflectionServiceTests.cs`

- File: `tests/XFoil.Core.Tests/FlapDeflectionServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: FLAP`
- Secondary Fortran Reference(s): `f_xfoil/src/geom.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests inspect immutable geometry-edit results and cleanup accounting instead of the interactive GDES state.
- Decision: Keep the managed harness because it validates preserved flap-edit behavior through the refactored API.
- Status: complete

### `tests/XFoil.Core.Tests/GeometryScalingServiceTests.cs`

- File: `tests/XFoil.Core.Tests/GeometryScalingServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: SCAL`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests exercise explicit origin-selection modes and structured results rather than the legacy editor state.
- Decision: Keep the managed harness because it validates preserved scaling behavior through the refactored API.
- Status: complete

### `tests/XFoil.Core.Tests/InfluenceMatrixBuilderTests.cs`

- File: `tests/XFoil.Core.Tests/InfluenceMatrixBuilderTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f influence matrix assembly`
- Secondary Fortran Reference(s): `wake coupling lineage in the inviscid setup`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `analytical DIJ`, `numerical DIJ`, `wake row copy`
- Differences from legacy: Managed tests validate the extracted builder directly and compare analytical versus numerical assembly paths, which are more explicit than in the legacy runtime.
- Decision: Keep the managed harness because it gives direct coverage of the ported influence assembly.
- Status: complete

### `tests/XFoil.Core.Tests/InfluenceMatrixParityDiagnosticsTests.cs`

- File: `tests/XFoil.Core.Tests/InfluenceMatrixParityDiagnosticsTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f influence assembly and paneling lineage`
- Secondary Fortran Reference(s): `tools/fortran-debug`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `initial DIJ mismatch localization`, `legacy spline/paneling parity boundaries`, `wake kernel trace output`
- Differences from legacy: Managed-only parity diagnostics harness built around structured trace artifacts and mismatch reports.
- Decision: Keep the managed diagnostics suite because it is required to localize solver-fidelity divergences against the legacy runtime.
- Status: complete

### `tests/XFoil.Core.Tests/InitialInfluenceTraceTests.cs`

- File: `tests/XFoil.Core.Tests/InitialInfluenceTraceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f initial influence assembly lineage`
- Secondary Fortran Reference(s): `tools/fortran-debug`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `initial influence trace emission`
- Differences from legacy: Managed-only JSONL tracing harness for parity debugging.
- Decision: Keep the managed diagnostics test because it captures a trace artifact that the legacy runtime never exposed directly.
- Status: complete

### `tests/XFoil.Core.Tests/InviscidSolverTests.cs`

- File: `tests/XFoil.Core.Tests/InviscidSolverTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f inviscid operating-point workflow`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests check typed inviscid results and physical trends instead of legacy interactive output.
- Decision: Keep the managed harness because it validates public-surface inviscid behavior directly.
- Status: complete

### `tests/XFoil.Core.Tests/LeadingEdgeRadiusServiceTests.cs`

- File: `tests/XFoil.Core.Tests/LeadingEdgeRadiusServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f leading-edge shaping workflow`
- Secondary Fortran Reference(s): `f_xfoil/src/geom.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests validate explicit radius metadata and locality of the edit rather than legacy editor state.
- Decision: Keep the managed harness because it protects the refactored leading-edge editing API.
- Status: complete

### `tests/XFoil.Core.Tests/LegacyPolarDumpImporterTests.cs`

- File: `tests/XFoil.Core.Tests/LegacyPolarDumpImporterTests.cs`
- Category: `test`
- Primary Fortran Reference: `legacy XFOIL dump binary format`
- Secondary Fortran Reference(s): `legacy dump post-processing/export workflow`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests synthesize binary dump fixtures and validate structured import/export artifacts rather than using external tooling.
- Decision: Keep the managed compatibility harness because it is the clearest regression for legacy dump support.
- Status: complete

### `tests/XFoil.Core.Tests/LegacyPolarImporterTests.cs`

- File: `tests/XFoil.Core.Tests/LegacyPolarImporterTests.cs`
- Category: `test`
- Primary Fortran Reference: `legacy saved polar file format`
- Secondary Fortran Reference(s): `legacy polar accumulation/reporting conventions`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests validate parsed DTO content instead of interactive polar plotting/reporting.
- Decision: Keep the managed compatibility harness because it protects interoperability with historical saved polar files.
- Status: complete

### `tests/XFoil.Core.Tests/LegacyReferencePolarImporterTests.cs`

- File: `tests/XFoil.Core.Tests/LegacyReferencePolarImporterTests.cs`
- Category: `test`
- Primary Fortran Reference: `legacy reference polar file format`
- Secondary Fortran Reference(s): `reference-polar plotting workflow`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests parse the reference polar into typed blocks and points instead of feeding it to the old plotting environment.
- Decision: Keep the managed compatibility harness because it is the clearest regression for this legacy file format.
- Status: complete

### `tests/XFoil.Core.Tests/LinearVortexInviscidSolverTests.cs`

- File: `tests/XFoil.Core.Tests/LinearVortexInviscidSolverTests.cs`
- Category: `test`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `shared inviscid force-integration and Cp conventions from f_xfoil/src/xpanel.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed-only backend acceptance and internal regression tests for the linear-vortex solver extension added by the port.
- Decision: Keep the managed-only harness because this solver backend is an intentional extension with no direct Fortran implementation to replay.
- Status: complete

### `tests/XFoil.Core.Tests/ModalInverseDesignServiceTests.cs`

- File: `tests/XFoil.Core.Tests/ModalInverseDesignServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xqdes.f modal inverse-design lineage`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests validate explicit modal-spectrum and perturbation APIs rather than legacy design-session state.
- Decision: Keep the managed harness because it protects the refactored inverse-design API and its intentional extensions.
- Status: complete

### `tests/XFoil.Core.Tests/NacaAirfoilGeneratorTests.cs`

- File: `tests/XFoil.Core.Tests/NacaAirfoilGeneratorTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/naca.f`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f geometry-generation entry points`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests validate the direct generator service instead of the geometry-generation command path.
- Decision: Keep the managed harness because it protects preserved analytical geometry generation at the API boundary.
- Status: complete

### `tests/XFoil.Core.Tests/NewtonSystemIndexingTests.cs`

- File: `tests/XFoil.Core.Tests/NewtonSystemIndexingTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xblsys.f system indexing/storage layout`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsolv.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `ISYS mapping`, `global-line storage sizing`, `small solver fixture`
- Differences from legacy: Managed tests validate explicit storage and indexing objects rather than hidden common-block offsets.
- Decision: Keep the managed harness because it documents and protects the refactored Newton-system layout.
- Status: complete

### `tests/XFoil.Core.Tests/NormalizationAndMetricsTests.cs`

- File: `tests/XFoil.Core.Tests/NormalizationAndMetricsTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f :: NORM`
- Secondary Fortran Reference(s): `f_xfoil/src/geom.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests compose normalization and metrics services explicitly instead of reading back mutable geometry state.
- Decision: Keep the managed harness because it validates preserved normalization behavior through the refactored API.
- Status: complete

### `tests/XFoil.Core.Tests/PanelGeometryBuilderTests.cs`

- File: `tests/XFoil.Core.Tests/PanelGeometryBuilderTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: NCALC/APCALC/TECALC`
- Secondary Fortran Reference(s): `f_xfoil/src/xfoil.f compressibility parameter setup`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `normals`, `panel angles`, `trailing-edge geometry`, `compressibility parameters`
- Differences from legacy: Managed tests validate extracted preprocessing helpers directly on analytical fixtures.
- Decision: Keep the managed harness because it isolates panel-geometry formulas more clearly than the legacy runtime.
- Status: complete

### `tests/XFoil.Core.Tests/PanelMeshGeneratorTests.cs`

- File: `tests/XFoil.Core.Tests/PanelMeshGeneratorTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: PANGEN`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `leading-edge clustering`, `mesh closure/orientation`
- Differences from legacy: Managed tests drive the panel mesh generator directly and inspect immutable mesh objects.
- Decision: Keep the managed harness because it protects the paneling behavior of the PANGEN port at the API boundary.
- Status: complete

### `tests/XFoil.Core.Tests/ParametricSplineTests.cs`

- File: `tests/XFoil.Core.Tests/ParametricSplineTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/spline.f :: SPLINA/SEGSPL/DEVAL/SCALC`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `spline fit branches`, `evaluation/derivative`, `arc length`
- Differences from legacy: Managed tests validate the extracted reusable spline helpers directly on analytical fixtures instead of only through consumers.
- Decision: Keep the managed harness because direct numerical checks are the strongest protection for this heavily reused math.
- Status: complete

### `tests/XFoil.Core.Tests/PolarCsvExporterTests.cs`

- File: `tests/XFoil.Core.Tests/PolarCsvExporterTests.cs`
- Category: `test`
- Primary Fortran Reference: `legacy polar reporting conventions`
- Secondary Fortran Reference(s): `saved-polar file formats`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed-only CSV formatting/export layer over typed sweep and legacy-import results.
- Decision: Keep the managed harness because deterministic CSV export is a port-specific reporting contract.
- Status: complete

### `tests/XFoil.Core.Tests/PolarParityTests.cs`

- File: `tests/XFoil.Core.Tests/PolarParityTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f :: ASEQ/CL/per-Re viscous sweep workflows`
- Secondary Fortran Reference(s): `authoritative Fortran XFoil 6.97 PACC output`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `alpha sweep parity`, `CL sweep parity`, `Re sweep parity`, `drag decomposition and transition metadata`
- Differences from legacy: Managed parity harness compares typed sweep results against embedded authoritative references rather than runtime polar files.
- Decision: Keep the managed harness because it is the stable executable contract for polar-sweep parity.
- Status: complete

### `tests/XFoil.Core.Tests/PolarSweepRunnerTests.cs`

- File: `tests/XFoil.Core.Tests/PolarSweepRunnerTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f :: ASEQ/CSEQ and per-Re sweep orchestration`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f/xblsys.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `warm starts`, `batched alpha/CL sweeps`, `service wiring`
- Differences from legacy: Managed tests validate deterministic sweep-runner APIs rather than the interactive sweep commands and session state.
- Decision: Keep the managed harness because it protects the port's sweep orchestration layer directly.
- Status: complete

### `tests/XFoil.Core.Tests/QSpecDesignServiceTests.cs`

- File: `tests/XFoil.Core.Tests/QSpecDesignServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xqdes.f :: QDES/QSPEC workflows`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests exercise explicit immutable QSPEC profile operations and inverse-design results rather than a legacy design-session workspace.
- Decision: Keep the managed harness because it validates the refactored inverse-design API directly.
- Status: complete

### `tests/XFoil.Core.Tests/ScaledPivotLuSolverTests.cs`

- File: `tests/XFoil.Core.Tests/ScaledPivotLuSolverTests.cs`
- Category: `test`
- Primary Fortran Reference: `legacy LU/pivoting solve lineage`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `scaled pivot selection`, `back-substitution`
- Differences from legacy: Managed tests validate the reusable dense-solver component directly rather than only through consuming algorithms.
- Decision: Keep the managed harness because it gives direct coverage of this shared numeric primitive.
- Status: complete

### `tests/XFoil.Core.Tests/StreamfunctionInfluenceCalculatorTests.cs`

- File: `tests/XFoil.Core.Tests/StreamfunctionInfluenceCalculatorTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xpanel.f :: PSILIN`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `self-influence singularity handling`, `source/vortex/TE contributions`, `symmetry`
- Differences from legacy: Managed tests validate the extracted streamfunction kernel directly and inspect explicit sensitivity outputs.
- Decision: Keep the managed harness because it isolates a parity-sensitive influence kernel cleanly.
- Status: complete

### `tests/XFoil.Core.Tests/TestDataPaths.cs`

- File: `tests/XFoil.Core.Tests/TestDataPaths.cs`
- Category: `test`
- Primary Fortran Reference: `none`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `GetRunsFixturePath`, `FindRepoRoot`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed-only test infrastructure for locating fixtures in the repository workspace.
- Decision: Keep the managed helper because it is required by the .NET harness and has no direct Fortran analogue.
- Status: complete

### `tests/XFoil.Core.Tests/TrailingEdgeGapServiceTests.cs`

- File: `tests/XFoil.Core.Tests/TrailingEdgeGapServiceTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xgdes.f trailing-edge gap editing workflow`
- Secondary Fortran Reference(s): `f_xfoil/src/geom.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `none`
- Differences from legacy: Managed tests validate explicit gap metadata and immutable geometry results instead of the legacy editor state.
- Decision: Keep the managed harness because it protects the refactored trailing-edge editing API.
- Status: complete

### `tests/XFoil.Core.Tests/TransitionModelPortTests.cs`

- File: `tests/XFoil.Core.Tests/TransitionModelPortTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xblsys.f :: DAMPL/DAMPL2/AXSET/TRCHEK2`
- Secondary Fortran Reference(s): `none`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `amplification-rate branches`, `transition sensitivities`, `transition-point solve/clamping`
- Differences from legacy: Managed tests exercise the extracted transition helpers and their derivatives directly instead of only through end-to-end viscous solves.
- Decision: Keep the managed harness because direct formula and derivative checks are the strongest parity protection for this subsystem.
- Status: complete

### `tests/XFoil.Core.Tests/TransitionModelTests.cs`

- File: `tests/XFoil.Core.Tests/TransitionModelTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xblsys.f transition model lineage`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f initial-state setup`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `transition onset`, `NCrit sensitivity`
- Differences from legacy: Managed tests inspect typed viscous-initial-state stations rather than legacy amplification/regime arrays.
- Decision: Keep the managed harness because it validates transition behavior at the public API boundary.
- Status: complete

### `tests/XFoil.Core.Tests/ViscousInitialStateTests.cs`

- File: `tests/XFoil.Core.Tests/ViscousInitialStateTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xbl.f initial boundary-layer state setup`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `surface branch initialization`, `wake initialization`
- Differences from legacy: Managed tests inspect explicit station objects and wake metadata instead of legacy state arrays.
- Decision: Keep the managed harness because it protects public-surface initial-state invariants directly.
- Status: complete

### `tests/XFoil.Core.Tests/ViscousParityTests.cs`

- File: `tests/XFoil.Core.Tests/ViscousParityTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f viscous operating-point workflow`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f/xblsys.f/xblsolv.f`, `authoritative Fortran XFoil 6.97 reference runs`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `single-point viscous parity`, `drag decomposition`, `transition reporting`, `cross-case trend checks`
- Differences from legacy: Managed parity harness compares typed solver results against embedded reference values rather than interactive output or imported polar text.
- Decision: Keep the managed harness because it is the stable executable contract for single-point viscous parity.
- Status: complete

### `tests/XFoil.Core.Tests/ViscousSolverEngineTests.cs`

- File: `tests/XFoil.Core.Tests/ViscousSolverEngineTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xfoil.f viscous operating-point solve`
- Secondary Fortran Reference(s): `f_xfoil/src/xbl.f/xblsys.f/xblsolv.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `full Newton solve`, `convergence history`, `drag decomposition`, `transition info`
- Differences from legacy: Managed tests validate structured convergence metadata and richer result objects rather than legacy interactive state and printed diagnostics.
- Decision: Keep the managed harness because it protects the public solver-engine contract directly.
- Status: complete

### `tests/XFoil.Core.Tests/ViscousStateSeedTests.cs`

- File: `tests/XFoil.Core.Tests/ViscousStateSeedTests.cs`
- Category: `test`
- Primary Fortran Reference: `f_xfoil/src/xbl.f viscous seed/station setup`
- Secondary Fortran Reference(s): `f_xfoil/src/xblsys.f`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `branch xi progression`, `wake seed velocity/gap`
- Differences from legacy: Managed tests inspect immutable seed objects instead of legacy arrays.
- Decision: Keep the managed harness because it validates pre-Newton seed invariants at the public API boundary.
- Status: complete

### `tests/XFoil.Core.Tests/WakeGeometryTests.cs`

- File: `tests/XFoil.Core.Tests/WakeGeometryTests.cs`
- Category: `test`
- Primary Fortran Reference: `legacy inviscid wake marching lineage`
- Secondary Fortran Reference(s): `f_xfoil/src/xpanel.f trailing-edge wake setup`
- Methods Audited: `all [Fact] methods in file`
- Parity-sensitive blocks: `wake origin`, `downstream distance monotonicity`, `wake deflection`
- Differences from legacy: Managed tests inspect typed wake objects instead of hidden solver arrays.
- Decision: Keep the managed harness because it validates the exposed wake-geometry contract directly.
- Status: complete
