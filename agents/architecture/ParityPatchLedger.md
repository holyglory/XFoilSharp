# Parity Patch Ledger

This ledger tracks parity-related diffs so new agent suggestions can be checked against prior fixes, rejected ideas, and tooling pitfalls before anything is reapplied.

## Review Rule

- Every candidate diff must name the exact mismatch boundary it fixes.
- Every candidate diff must cite the closest known Fortran lineage.
- Every candidate diff must say whether it is a legacy-behavior replay, a managed-correctness fix, or diagnostics-only tooling.
- Every candidate diff must include before/after verification on at least one focused case.
- Before merging a new diff, compare it against this ledger so we do not reintroduce or re-litigate the same arithmetic shape in circles.

## Merged Solver Patches

### 2026-03-18: parity-only unary negation signed-zero replay

- Status: merged
- Files:
  - `src/XFoil.Solver/Numerics/LegacyPrecisionMath.cs`
  - `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`
  - `src/XFoil.Solver/Services/TransitionModel.cs`
- Legacy lineage:
  - `f_xfoil/src/xbl.f :: MRCHUE` local seed assembly flow
  - `f_xfoil/src/xblsys.f :: BLSYS/BLDIF` REAL unary negation semantics
- Diff:
  - Added `LegacyPrecisionMath.Negate(...)` and replaced parity-only `Subtract(0.0, x, true)` sites where the classic code uses unary minus directly.
- Why this was accepted:
  - The new alpha-10 panel-80 similarity seed-system micro-rig proved the first remaining local mismatch was `-0.0f` versus `+0.0f` in the captured `BLSYS(0)` matrix, with all upstream inputs already matching bitwise.
  - This is a legacy signed-zero replay fix, not a downstream workaround.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~SimilaritySeedSystemMicroParityTests" -v minimal`
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~SimilaritySeedStepMicroParityTests" -v minimal`
- Circularity guard:
  - Do not collapse parity-only unary negation back into `Subtract(0.0, x, ...)` unless a raw-bit local oracle proves the sign bit is irrelevant at that exact site.

### 2026-03-18: `BoundaryLayerCorrelations` turbulent `CFT` `FCARG` replay fix

- Status: merged
- Files:
  - `src/XFoil.Solver/Services/BoundaryLayerCorrelations.cs`
- Legacy lineage:
  - `f_xfoil/src/xblsys.f :: CFT`
- Diff:
  - The parity-sensitive `FCARG = 1.0 + 0.5*GM1*MSQ` path now replays the contracted native float add before `SQRT`, instead of a separately rounded helper chain.
- Why this was accepted:
  - After widening the standalone `CQ/CF/DI` batches to 1024 vectors, the first failing upstream producer was `fcArg` in `TurbulentSkinFriction`, with the downstream `CF/DI` families failing together.
  - Fixing `fcArg` closed the clustered failures across the turbulent `CF/DI` tests, so this is a true shared producer patch.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~CfChainFortranParityTests|FullyQualifiedName~TurbulentWallDiFortranParityTests|FullyQualifiedName~TurbulentDiDfacFortranParityTests|FullyQualifiedName~TurbulentDiChainFortranParityTests" -v minimal`
- Circularity guard:
  - Future `CFT` edits must cite the standalone raw-bit `CF/DI` rigs and prove the exact producer field before changing `FCARG` staging again.

### 2026-03-18: `StreamfunctionInfluenceCalculator` full-run `PSILIN` midpoint `rs0` replay fix

- Status: merged
- Files:
  - `src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs`
- Legacy lineage:
  - `f_xfoil/src/xpanel.f :: PSILIN`
- Diff:
  - The parity-only source-midpoint radius-square `rs0` now replays the single-rounding `X0*X0 + YY*YY` staging via `LegacyPrecisionMath.FusedMultiplyAdd(x0, x0, yy * yy)` instead of the fully separated `SeparateSumOfProducts(x0, x0, yy, yy)` path.
- Why this was accepted:
  - Fresh reduced-panel alpha-10 `fieldIndex=47` traces proved the first live `DZDM` producer mismatch was `panelIndex=2, half=1`, and its first bad subterm was `rs0/g0` with all upstream panel geometry inputs already matching.
  - The patch closes the full `BIJ row 47` window bitwise against Fortran, so this is a real legacy-replay fix, not a case-specific downstream workaround.
- Verification:
  - Focused `PSILIN` trace: `fieldIndex=47` now has no remaining `DZ*` mismatches against `reference_trace.936.jsonl`.
  - Focused `matrix_entry` trace: the full captured `BIJ row 47` window now matches `reference_trace.934.jsonl` bitwise.
- Circularity guard:
  - Do not revert `rs0` back to the fully separated square-sum helper unless a fresh full-run `PSILIN` producer trace proves the single-rounding replay was mischaracterized.

### 2026-03-17: `DenseLinearSystemSolver` standalone `GAUSS` replay fix

- Status: merged
- Files:
  - `src/XFoil.Solver/Numerics/DenseLinearSystemSolver.cs`
- Legacy lineage:
  - `f_xfoil/src/xsolve.f :: GAUSS`
- Diff:
  - The parity-sensitive forward-elimination matrix updates now use `LegacyPrecisionMath.SeparateMultiplySubtract(...)`.
  - The forward-elimination right-hand-side updates now use `LegacyPrecisionMath.SeparateMultiplySubtract(...)`.
  - The back-substitution updates now also use `LegacyPrecisionMath.SeparateMultiplySubtract(...)`.
- Why this was accepted:
  - The standalone `GAUSS` micro-driver proved the old fused updates diverged even when the raw inputs and pivot flow already matched.
  - This is a legacy-evaluation-order replay fix, not a case-specific workaround.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~DenseLinearSystemFortranParityTests" -v minimal`
- Circularity guard:
  - Do not revert these paths back to fused updates unless a fresh standalone `GAUSS` trace proves the legacy routine was misread.

### 2026-03-17: `StreamfunctionInfluenceCalculator` standalone `PSILIN` REAL-replay family

- Status: merged
- Files:
  - `src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs`
- Legacy lineage:
  - `f_xfoil/src/xpanel.f :: PSILIN`
- Diff:
  - `rs1` and `rs2` replay the contracted single-precision endpoint square-sums.
  - The source-midpoint `rs0` replay is now tracked separately by the full-run `BIJ` fix below; it does not share the exact same staging as the endpoint radius squares.
  - The source half-1 and half-2 `PDX` numerators now replay the visible Fortran source tree with explicit staged products and left-associated tail updates instead of hidden contracted forms.
  - The source half-1 and half-2 `PDYY`, `PSNI`, and `PDNI` sums now replay the visible REAL source tree instead of nested FMAs.
  - The vortex `PDX`, `PDYY`, `PSNI`, and `PDNI` sums now also replay the visible REAL source tree instead of fused helper chains.
- Why this was accepted:
  - The standalone `PSILIN` micro-driver kept moving the first mismatch boundary one producer field at a time while the preceding inputs already matched bitwise.
  - Each accepted change closed a proved producer mismatch and moved the boundary downstream until the full curated micro-batch went green.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~StreamfunctionMicroFortranParityTests" -v minimal`
  - The curated standalone `PSILIN` micro-batch now passes bitwise.
- Circularity guard:
  - Do not replace these staged REAL replays with blanket fused helpers unless the standalone `PSILIN` micro-driver proves the exact field still matches.

### 2026-03-17: `ParametricSpline` standalone `spline.f` parity replay

- Status: merged
- Files:
  - `src/XFoil.Solver/Numerics/ParametricSpline.cs`
- Legacy lineage:
  - `f_xfoil/src/spline.f :: SEVAL/DEVAL`
- Diff:
  - `CX1` and `CX2` now replay the contracted `DS*XS - XHIGH` path before the final `+ XLOW`.
  - `SEVAL` `CUBFAC` now replays the contracted `T - T*T` value.
  - `DEVAL` keeps a mixed replay: `FAC1` stays contracted, `FAC2` stays source-ordered, and the final numerator remains left-associated and unfused.
  - Operand-level tracing on the standalone spline driver was extended so `ParametricSpline` patches were accepted only after the exact producer term matched the Fortran oracle.
- Why this was accepted:
  - The earlier whole-run spline mismatch notes were too coarse and even pointed in conflicting directions.
  - The standalone raw-hex driver removed decimal-text ambiguity and proved the exact mixed staging directly against `spline.f` across explicit edge cases and randomized batches.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~ParametricSplineFortranParityTests.FloatSplineBatch_BitwiseMatchesFortranSplineDriver" -v minimal`
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~ParametricSplineTests|FullyQualifiedName~ParametricSplineFortranParityTests" -v minimal`
- Circularity guard:
  - Do not replace these replay choices with blanket “source-order everywhere” or blanket “fuse everything” edits. Future spline changes must cite the standalone driver and prove the exact field that differs.

### 2026-03-17: `ViscousSolverEngine` stale `ampl2` handoff

- Status: merged
- Files:
  - `src/XFoil.Solver/Services/ViscousSolverEngine.cs`
- Legacy lineage:
  - `f_xfoil/src/xoper.f :: MRCHUE` transition-probe / seed-march state handoff
- Diff:
  - `ampl2 = Math.Max(transitionPoint.DownstreamAmplification, 0.0);` now runs unconditionally after the transition probe instead of being skipped when `transitionInterval` is already true.
- Why this was accepted:
  - The old guard left a stale downstream amplification value in the seed path.
  - The fix matches the legacy intent that the probe refreshes the downstream amplification state each pass; it is not a case-specific fudge factor.
- Verification:
  - Curie isolated case `NACA 4415, Re=5e6, alpha=3, panels=57, Ncrit=9`
  - Before: `dCL=+2.587435700e-02`, `dCD=-5.617466863e-03`, `dCM=-1.511702000e-03`
  - After: `dCL=+1.315868300e-02`, `dCD=-7.373625400e-04`, `dCM=-6.214890000e-04`
  - Curie isolated case `NACA 4412, Re=3.5e6, alpha=1.5, panels=59, Ncrit=8`
  - Before: `dCL=+1.345544270e-01`, `dCD=-5.659983564e-03`, `dCM=-2.814366400e-02`
  - After: `dCL=+1.212533610e-01`, `dCD=-3.502964740e-03`, `dCM=-2.391895500e-02`
  - Mainline fresh rebuild re-verification matched Curie's improved post-patch dumps on both cases.
- Circularity guard:
  - Do not accept future diffs that reintroduce a conditional `ampl2` refresh in this block unless a fresh upstream trace proves the legacy state flow was misread.

### 2026-03-16: `BoundaryLayerSystemAssembler` left-associated `eq1` correction replay

- Status: merged
- Files:
  - `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`
- Legacy lineage:
  - `f_xfoil/src/xblsys.f :: BLDIF`
- Diff:
  - `AddGroupedEq1Correction` was changed from a grouped final add into strict left-associated legacy accumulation.
- Why this was accepted:
  - All `bldif_eq1_t_terms` subterms already matched Fortran.
  - Only the final rounded single-precision sum differed, proving this was an evaluation-order replay issue, not a formula rewrite.
- Verification:
  - Closed the proved `bldif_eq1_t_terms` row-22 mismatch on `n0012_re1e6_a10`.
- Circularity guard:
  - If a new diff proposes regrouping the same correction terms for readability, reject it in the parity branch unless the change is isolated to the default managed path.

### 2026-03-16: `BoundaryLayerSystemAssembler` TRDIF `BT2` parity replay family

- Status: merged
- Files:
  - `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`
- Legacy lineage:
  - `f_xfoil/src/xbl.f` call chain into `TRDIF`
- Diff:
  - Parity-only replay fixes were applied for proved `transition_interval_bt2_terms` mismatches including row 1 / column 3 and row 3 / column 3 on the reduced-panel alpha-10 rung.
- Why this was accepted:
  - Inputs matched and the divergence was isolated to transition-interval combine/evaluation order.
- Verification:
  - The first reduced-panel alpha-10 `transition_seed_system` event at `side=1, station=15, iteration=1` now matches exactly.
- Circularity guard:
  - Future BT2 edits must cite the exact row/column/event proof and should extend the same mismatch family rather than inventing a new local combine shape.

### 2026-03-16: `TransitionModel` parity arithmetic replay family

- Status: merged
- Files:
  - `src/XFoil.Solver/Services/TransitionModel.cs`
- Legacy lineage:
  - `f_xfoil/src/xblsys.f :: DAMPL/DAMPL2/AXSET/TRCHEK2`
- Diff:
  - Parity-sensitive staging fixes were applied for `AF_HMI`, `DADR`, `DADR_HK`, and cubic-ramp `RFAC`.
- Why this was accepted:
  - These were proved source-order or mixed-width staging mismatches in the transition model, not managed-branch correctness improvements.
- Verification:
  - Closed the corresponding alpha-10 transition-path trace mismatches called out in the campaign notes.
- Circularity guard:
  - Do not "simplify" these parity branches back into grouped double-precision expressions without a fresh trace proof that the legacy arithmetic was mischaracterized.

## Merged Tooling Patches

### 2026-03-18: alpha-10 similarity seed local-chain micro-rigs

- Status: merged
- Files:
  - `tests/XFoil.Core.Tests/FortranParity/SimilaritySeedSystemMicroParityTests.cs`
  - `tests/XFoil.Core.Tests/FortranParity/SimilaritySeedStepMicroParityTests.cs`
- Type:
  - diagnostics/tooling only
- Diff:
  - Added dedicated raw-IEEE754 micro-rigs for the alpha-10 panel-80 `station=2` similarity seed `BLSYS(0)` matrix/residuals and the immediately downstream dense solve plus seed-step limiter metrics.
- Why this was accepted:
  - The broad viscous traces were already narrowed to one local seed block, so a direct local oracle was cheaper, smaller, and more decisive than repeated full-march reruns.
  - The new rigs catch local matrix, step, and signed-zero bugs in milliseconds.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~SimilaritySeedSystemMicroParityTests|FullyQualifiedName~SimilaritySeedStepMicroParityTests" -v minimal`
- Circularity guard:
  - Extend this local-chain harness downstream (`laminar_seed_final`, handoff, next-station consumer) before reopening broad alpha-10 march traces for the same station.

### 2026-03-18: stations-3-to-5 laminar-seed helper micro-rig

- Status: merged
- Files:
  - `tests/XFoil.Core.Tests/FortranParity/LaminarSeedStations3To5MethodMicroParityTests.cs`
- Type:
  - diagnostics/tooling only
- Diff:
  - Added a tiny in-memory BL-state rig that runs the real `RefineSimilarityStationSeed` helper to populate the carried COM1 snapshot, then runs the real `RefineLaminarSeedStation` helper sequentially for `side=1, station=3/4/5`, and compares each accepted final station state against the captured Fortran traces.
- Why this was accepted:
  - The downstream laminar stations are the smallest local consumers that genuinely depend on the carried similarity-station snapshot, so a bare `BLSYS` replay there would have been incomplete.
  - This rig keeps the scope local while preserving the real method-level handoff semantics across several accepted stations instead of only one.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~LaminarSeedStations3To5MethodMicroParityTests" -v minimal`
- Circularity guard:
  - Use this helper-level oracle before reopening broad alpha-10 seed-march traces for stations 3 through 5 or later laminar stations.

### 2026-03-17: standalone `GAUSS` raw-hex oracle

- Status: merged
- Files:
  - `tools/fortran-debug/build_micro_drivers.sh`
  - `tools/fortran-debug/gauss_parity_driver.f90`
  - `tools/fortran-debug/gauss_trace_stub.f90`
  - `tests/XFoil.Core.Tests/FortranParity/FortranGaussDriver.cs`
  - `tests/XFoil.Core.Tests/FortranParity/DenseLinearSystemFortranParityTests.cs`
- Type:
  - diagnostics/tooling only
- Diff:
  - Added a standalone legacy `GAUSS` build and a managed test harness that compares phase-by-phase states and final solution bits.
- Why this was accepted:
  - Whole-solver traces were too coarse and too slow for dense linear-solver replay questions.
  - The dedicated oracle turns dense-solver mismatches into a direct producer proof in a few seconds.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~DenseLinearSystemFortranParityTests" -v minimal`
- Circularity guard:
  - New dense-solver arithmetic claims should cite this micro-driver before any downstream solver trace.

### 2026-03-17: standalone `PSILIN` raw-hex oracle

- Status: merged
- Files:
  - `tools/fortran-debug/build_micro_drivers.sh`
  - `tools/fortran-debug/psilin_parity_driver.f`
  - `tools/fortran-debug/xpanel_microtrace_stubs.f90`
  - `tests/XFoil.Core.Tests/FortranParity/FortranPsilinDriver.cs`
  - `tests/XFoil.Core.Tests/FortranParity/StreamfunctionMicroFortranParityTests.cs`
- Type:
  - diagnostics/tooling only
- Diff:
  - Added a standalone legacy `PSILIN` build and a managed raw-bit comparator over curated micro-cases.
  - The current harness compares panel setup, source-half terms, source derivative terms, source segment outputs, vortex segment outputs, TE correction terms, accumulation checkpoints, result terms, and the final kernel result vector.
- Why this was accepted:
  - It localizes streamfunction-kernel producer mismatches directly against the real legacy routine without broad inviscid or viscous reruns.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~StreamfunctionMicroFortranParityTests" -v minimal`
  - The curated standalone `PSILIN` micro-batch now passes bitwise.
- Circularity guard:
  - Use this micro-driver as the first oracle for future `PSILIN` questions before reopening broader inviscid or `AIJ` traces.

### 2026-03-17: standalone `SEGSPL` raw-hex NACA contour oracle

- Status: merged
- Files:
  - `tools/fortran-debug/segmented_spline_parity_driver.f`
  - `tools/fortran-debug/build_spline_driver.sh`
  - `tests/XFoil.Core.Tests/FortranParity/FortranSplineDriver.cs`
  - `tests/XFoil.Core.Tests/FortranParity/FortranSegmentedSplineDriver.cs`
  - `tests/XFoil.Core.Tests/FortranParity/ParametricSplineFortranParityTests.cs`
- Type:
  - diagnostics/tooling only
- Diff:
  - The spline micro-driver build now emits both the existing `SPLIND` batch binary and a new `SEGSPL` batch binary.
  - The managed spline parity tests now feed classic `NACA 0012`, `2412`, and `4415` contours through the standalone `SEGSPL` path and compare raw IEEE-754 words for both `x(s)` and `y(s)` curves.
- Why this was accepted:
  - The previous standalone spline proof was strong on arithmetic staging but still left an open question about real airfoil-shaped contour inputs.
  - This closes that gap without reopening large solver traces or relying on decimal-text logs.
- Verification:
  - `dotnet test tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj --filter "FullyQualifiedName~ParametricSplineFortranParityTests" -v minimal`
- Circularity guard:
  - Do not accept future spline claims based only on whole-run trace text when one of the standalone `SPLIND` or `SEGSPL` raw-bit oracles can answer the question directly.

### 2026-03-17: live-compare matched-context and completion checks

- Status: merged
- Files:
  - `tests/XFoil.Core.Tests/FortranParity/ParityTraceLiveComparator.cs`
  - `tests/XFoil.Core.Tests/FortranParity/ParityTraceLiveComparatorTests.cs`
  - `tests/XFoil.Core.Tests/FortranParity/FortranReferenceCases.cs`
- Type:
  - diagnostics/tooling only
- Diff:
  - The live comparator now keeps a bounded last-match context window and includes that neighborhood in the mismatch report.
  - Mismatch reports now carry the concrete reference and managed records at the failing boundary instead of only a single-line exception.
  - The harness now calls `AssertCompleted()` after a successful managed run so trailing comparable reference events become a hard failure.
  - `XFOIL_LIVE_COMPARE_CONTEXT_EVENTS` lets focused runs widen or shrink the recorded context without changing code.
- Why this was accepted:
  - The previous rig only reported the first traced mismatch line, which still left too much manual trace spelunking when deciding whether a candidate patch site was a producer or merely the first visible consumer.
  - It also had a blind spot where a managed run could stop early without proving that the remaining comparable reference events matched.
- Verification:
  - `ParityTraceLiveComparatorTests`, `JsonlTraceWriterTests`, and `ParityDumpDivergenceAnalyzerTests` pass.
  - Fresh smoke run `live_compare_smoke_alpha0_wakenode_rigcheck` produced a `~1.6 KB` `parity_report.741.txt` that includes the last matched `wake_node`, the concrete mismatch pair, and the boundary hint.
- Circularity guard:
  - If a future change weakens the live report back to a single-line mismatch or removes the completion check, reject it unless an equally compact structured replacement lands in the same patch.

### 2026-03-17: managed `parity_report` first-divergence harness

- Status: merged
- Files:
  - `tests/XFoil.Core.Tests/FortranParity/ParityDumpDivergenceAnalyzer.cs`
  - `tests/XFoil.Core.Tests/FortranParity/ParityDumpDivergenceAnalyzerTests.cs`
  - `tests/XFoil.Core.Tests/FortranParity/FortranReferenceCases.cs`
  - `tests/XFoil.Core.Tests/FortranParity/AdHocArtifactRefreshTests.cs`
  - `tools/fortran-debug/run_managed_case.sh`
- Type:
  - diagnostics/tooling only
- Diff:
  - The managed ad hoc harness now writes a versioned `parity_report.*.txt` after each run when a reference dump is available.
  - The report includes the final `CL/CD/CM` gap plus the earliest parsed dump-level divergence block and a short “next focus” hint.
  - `run_managed_case.sh` now exports `XFOIL_REFERENCE_OUTPUT_DIR` so the C# harness can compare against an explicitly chosen reference artifact directory instead of only the default case location.
- Why this was accepted:
  - The campaign was still spending too much time rediscovering the first coarse mismatch by hand after every full run.
  - This keeps the first locator inside the managed harness itself and turns the broad run into an actionable boundary report before any focused trace rerun begins.
- Verification:
  - `ParityDumpDivergenceAnalyzerTests` pass.
  - Fresh smoke run `harness_smoke_n0012_re1e6_a0_p160` produced `parity_report.724.txt` and surfaced the first coarse dump-level boundary automatically through `run_managed_case.sh`.
- Circularity guard:
  - Treat `parity_report` as a coarse locator, not a proof by itself. A candidate arithmetic patch still needs the usual upstream input/output trace proof before merge.

### 2026-03-17: auto-summary default and `--full-trace` override

- Status: merged
- Files:
  - `tools/fortran-debug/run_managed_case.sh`
  - `tools/fortran-debug/run_reference.sh`
- Type:
  - diagnostics/tooling only
- Diff:
  - The case runners now default to summary-only output when no trace selectors are present.
  - Full firehose traces require explicit selectors or `--full-trace`.
- Why this was accepted:
  - Multi-agent parity work was spending most of its disk and runtime budget on irrelevant JSON noise.
  - The new default keeps routine gap checks cheap while still allowing a second focused rerun around the first proved mismatch window.
- Verification:
  - Mainline alpha-0 focused reruns `main_alpha0_seed_window_*`, `main_alpha0_seed_finals_*`, `main_alpha0_station2_*`, and `main_alpha0_stagnation_*` produced the needed proof windows without falling back to broad full-run traces.
- Circularity guard:
  - Do not turn default case runs back into unfiltered full traces. New diagnostics should tighten selectors first and use `--full-trace` only when the focused window is still missing data.

### 2026-03-17: `run_managed_case.sh` stale-build and empty-array fixes

- Status: merged
- Files:
  - `tools/fortran-debug/run_managed_case.sh`
- Type:
  - diagnostics/tooling only
- Diff:
  - Replaced Bash-3.2-unsafe empty-array expansion with seeded command arrays.
  - Changed the runner to always execute an incremental `dotnet build` before the `--no-build` test step.
- Why this was accepted:
  - The old script could fail before running on macOS because `set -u` treated empty arrays as unbound.
  - More importantly, it could silently reuse stale test binaries after source edits, producing false "verified" parity results.
- Verification:
  - The runner now executes cleanly without `--artifacts-path`.
  - Fresh mainline rechecks of the merged `ampl2` solver patch matched Curie's isolated post-patch dumps once the stale-build path was removed.
- Circularity guard:
  - Treat any agent result produced without a fresh rebuild as untrusted until rerun under the current script semantics.

### 2026-03-16 to 2026-03-17: focused trace-filtering and LU term tracing

- Status: merged
- Files:
  - `src/XFoil.Solver/Diagnostics/JsonlTraceWriter.cs`
  - `tests/XFoil.Core.Tests/JsonlTraceWriterTests.cs`
  - `tools/fortran-debug/filter_trace.py`
  - `tools/fortran-debug/run_reference.sh`
  - `tools/fortran-debug/run_managed_case.sh`
  - `src/XFoil.Solver/Numerics/ScaledPivotLuSolver.cs`
  - `tools/fortran-debug/xsolve_debug.f`
  - `tools/fortran-debug/json_trace.f`
- Type:
  - diagnostics/tooling only
- Diff:
  - Added exact record-name and data-field filtering plus trigger selectors for small focused traces.
  - Added `lu_decompose_term` tracing in both managed and Fortran debug builds.
- Why this was accepted:
  - These changes reduce trace volume and let the campaign localize producer mismatches without full-firehose JSON.
- Verification:
  - `JsonlTraceWriterTests` cover the new filter behavior.
  - The LU term traces proved the alpha-0 row-31/col-30 LU drift was only a consumer of an already-diverged raw `aij(2,30)` input.
- Circularity guard:
  - Do not remove these selectors or the LU term trace path while the alpha-0 inviscid producer boundary is still open.

## Open Review Entries

### Alpha-0 inviscid producer boundary

- Status: open
- Current proved boundary:
  - raw `basis_aij_single -> aij(2,30)` differs before LU on the `NACA 0012, Re=1e6, alpha=0, panels=60` rung
- Current viscous-side consumer proof on the same rung:
  - `transition_interval_inputs` at `side=1, station=22, iteration=5` already mismatches.
  - `laminar_seed_final` at `side=1, station=21` already mismatches.
  - the first `laminar_seed_system` event at `side=1, station=2, iteration=1` already enters with mismatched `uei`, `theta`, and `dstar`.
  - the matching `USAV_SPLIT IS=1 IBL=2` dump row proves that seed-system `uei` is consuming an already-diverged `UINV` baseline.
- Producer status:
  - `basis_rhs_alpha0` matches
  - raw `aij(31,30)` mismatch is downstream of this
  - `lu_decompose_term` proved the row-31 consumer is reading an already-diverged `A(2,30)`
  - the earliest proved viscous mismatch on this rung is therefore upstream of `InitializeBLThwaitesXFoil`, `RefineSimilarityStationSeed`, `RefineLaminarSeedStation`, and `transition_interval_inputs`; those blocks are consumers until the inviscid baseline matches
- Guard against circular patches:
  - Do not patch `ScaledPivotLuSolver` again for this boundary until the raw matrix input matches.
  - Do not patch the alpha-0 seed or remarch helpers just because they show the first visible viscous mismatch; their inputs are already wrong.
  - Candidate diffs in `StreamfunctionInfluenceCalculator`, `LinearVortexInviscidSolver`, `EdgeVelocityCalculator`, `ViscousSolverEngine` stagnation mapping, or related assembly code must cite this entry and prove the exact upstream producer they change.

### Rejected reduced-case `BT2(K,2)` replay candidate

- Status: rejected
- Case:
  - `NACA 2412, Re=1e6, alpha=1, panels=54, Ncrit=7`
- File family:
  - `src/XFoil.Solver/Services/BoundaryLayerSystemAssembler.cs`
- Candidate:
  - A parity-only replay adjustment around the middle `transition_interval_bt2_terms` / `BT2(K,2)` combine path.
- Why this was rejected:
  - The reduced-case trace only proved that `transition_seed_system` row 22 was consuming an upstream `TRDIF/BLDIF` producer.
  - A local `BT2(K,2)` adjustment worsened multiple isolated cases and did not line up with a proven Fortran evaluation-order boundary.
- Circularity guard:
  - Do not resurrect this candidate unless a fresh trace proves matching inputs and a direct `BT2(K,2)` output-only divergence.
