# Phase 3 status — Modern (#3) tree

Snapshot taken 2026-04-20.

## Tree architecture

```
#1 binary parity (float)  →  #2 algorithmic parity (double)  →  #3 modern
```

- **#1 (Float, binary parity)** — the Fortran-bit-exact baseline. Lives in `src/XFoil.Solver/Services/*.cs` etc. (no suffix).
- **#2 (Doubled)** — auto-generated from #1 by `tools/gen-double/gen-double.py` via text substitution (float→double, MathF.→Math., 1f→1d, etc.). Lives in `*.Double.cs`. Namespace: `XFoil.Solver.Double.Services`.
- **#3 (Modern)** — inherits from #2 by C# subclassing. Lives in `*.Modern.cs`. Namespace: `XFoil.Solver.Modern.Services`. Empty by default; algorithmic improvements land as `override` methods that replace specific bodies. Un-overridden methods inherit Doubled, so Doubled changes propagate.

### Why inheritance for Modern (not wrapping)

We dropped `gen-modern.py` after one iteration. The reason: Python text-generated wrappers were brittle (tuple return types, generic methods, default-value parameters all needed special handling). C# inheritance is the natural OO pattern — just unseal the facade in #1, mark public methods `virtual`, and gen-double propagates the modifiers automatically.

**Constraint: only INSTANCE classes can be inherited.** Static helpers (`LinearVortexInviscidSolver`, `ViscousSolverEngine`, `PolarSweepRunner`, `BoundaryLayerCorrelations`, etc.) cannot be sub-classed in C#. Modern improvements live at the facade level (`AirfoilAnalysisService`, `NacaAirfoilGenerator`); inside an override, we call the doubled static helpers directly.

### Bit-parity verification of the inheritance change

Before/after `virtual` modifier conversion:
- NACA 4455 = 4455/4455 = 100% bit-exact (smoke gate)
- Selig 100k = 100228/100228 = 100% bit-exact (full gate)
- 59/59 Phase 2 facade tests pass

`virtual` did not change runtime arithmetic (with `XFOIL_DISABLE_FMA=1` no inlining-driven FMA contraction).

## Plumbing in place

- `tools/gen-double/gen-double.py` — Doubled tree generator, modifier-preserving (already existed).
- `gen-modern.py` — REMOVED. Use C# inheritance.
- `src/XFoil.Solver/Services/AirfoilAnalysisService.Modern.cs` — Modern facade subclass.
- `src/XFoil.Core/Services/NacaAirfoilGenerator.Modern.cs` — Modern geometry subclass.
- `tests/XFoil.Core.Tests/ModernTreeFacadeTests.cs` — facade tests + bit-exact agreement tripwire (`ModernTree_AgreesWithDoubled_BitExact_NoOverrides`).
- `tools/fortran-debug/ParallelPolarCompare/Program.cs`:
  - `--triple-sweep <vectors-file>` — runs Float #1, Doubled #2, Modern #3 on each vector, prints agreement matrix.
  - `--reference-sweep <manifest.json>` — runs all three facades on each manifest row, reports per-airfoil RMS error vs reference.
- `src/XFoil.Cli/Program.cs` — `viscous-polar-naca-modern` command exposes Modern via CLI (analogous to `viscous-polar-naca-double`).
- `tools/external-references/windtunnel.json` — hand-curated 25-row wind-tunnel reference dataset (NACA 0012/4412 from NACA TR 824, NASA TM-100019, UIUC LSAT).
- `tools/external-references/openfoam/` — Docker-based OpenFOAM v2312 simpleFoam + k-ω SST pipeline. **1000-case sweep generated** in `results/openfoam_results.json`. Schema matches `windtunnel.json`.

### OpenFOAM caveat (read this before using the data)

The airFoil2D tutorial mesh is **not** NACA 0012 unit-chord. Wall-point inspection shows chord = 35 (now scaled to 1.0 via `transformPoints -scale 1/35`) with asymmetric y-bounds (max y = 0.106, min y = -0.075) — i.e. it's a cambered ~18% airfoil. The 1000 OpenFOAM cases are **relative-trend** data only. To produce absolute reference values for any specific NACA shape, we need a parametric mesher (gmsh- or blockMesh-based) that builds known geometries per case.

Schemes are first-order upwind on (k, ω) for FPE-trap robustness; CD is over-predicted ~2-5×.

## Modern overrides landed

| Tier | Override | Effect |
|------|----------|--------|
| A1 | `AirfoilAnalysisService.AnalyzeViscous` — multi-start retry on non-physical primary result (alpha jitter ±0.05° steps, up to 5 attempts, accept first physical hit) | **+63 / 5000 Selig samples converged vs Doubled** (4664 vs 4601). Bit-exact-equivalent to Doubled when the primary result is already physical (conservative gate via `PhysicalEnvelope.IsAirfoilResultPhysical`). NACA 4455 = 100% bit-exact. Selig 100k = 100% bit-exact. |
| A2 | `AirfoilAnalysisService.SweepViscousRe` — fresh per-Re-point state (`AnalyzeInviscidForLiftCoefficient` → `this.AnalyzeViscous`) instead of `PolarSweepRunner.SweepRe`'s shared inviscid state | Defense-in-depth against the historic Type-3 zero-convergence bug. On the canonical NACA 4412 fixed-CL=0.5 sweep both Modern and Doubled now converge 6/6 (the original bug was apparently fixed in float-parity by other work), so this is a no-measurable-gain override on that specific case. Each inner viscous call also gets the Tier A1 multi-start. |
| B1 v1 | `AirfoilAnalysisService.AnalyzeInviscid` — solution-adaptive panel refinement. Coarse PANGEN solve → Cp-gradient + Cp-curvature sensor → per-buffer-point density bias pushed via `CurvatureAdaptivePanelDistributor.PushDensityScale` (thread-static ambient hook) → refined PANGEN solve at the same panel count. N-dependent boost `1 + 0.9·sqrt(160/N)` throttles at fine N where biased PANGEN's Newton noise exceeds the placement gain. | **29.1% reduction in aggregate inviscid CL error on the default 10-NACA set** (score ratio M/D = 0.709, target 0.70). On the 20-airfoil curated set covering NACAs + 10 Selig airfoils the improvement is 20.7% (ratio 0.793) — aggressive Selig airfoils (s1223, e387) have multi-peak Cp fields the first-iter sensor doesn't cleanly target. NACA 4455 = 4455/4455 = 100% bit-exact preserved (Modern doesn't touch Float). gen-double regeneration is idempotent on the renamed `CurvatureAdaptivePanelDistributor` source. |
| B1 v2 | `AirfoilAnalysisService.AnalyzeViscous` — composes B1 bias with Tier A1 multi-start. Fetches a cheap unbiased inviscid seed via `base.AnalyzeInviscid` (virtual dispatch does NOT recurse through Modern's B1-inviscid override), computes the same Cp-gradient+curvature scale, pushes it for the full A1 multi-start loop (primary + jittered alphas all inherit the same bias). **Panel-count gated at N ≤ 100** — empirically B1 bias helps viscous only at coarse panel counts; at N > 100 the Newton-attractor perturbation costs more than the panel-placement gain. | Viscous panel-efficiency sweep (6 NACAs, α=4°, Re=1e6, N∈{80,120,160,200} scored vs Doubled N=320 truth): **CL M/D = 0.887 (+11.3%)**, **CD M/D = 0.938 (+6.2%)**. At N=80: CL ratio 0.83, CD ratio 0.89 (clear win). At N>100: transparent (both identical to Doubled+A1). 66/66 Phase 2+3 facade tests pass. NACA 4455 = 100% bit-exact preserved. gen-double idempotent. |
| B1 v3 | Sensor smoothing exponent tuned from 1.0 to **0.2**. The transform `scale[i] = 1 + (boost-1) · sensor[i]^0.2` lifts low-sensor regions so they still get meaningful bias, distributing the boost budget across the airfoil instead of concentrating on the single highest peak. Empirical sweep: 0.5 → (0.580, 0.690); 0.3 → (0.477, 0.613); **0.2 → (0.430, 0.572)**; 0.1 regresses (0.513, too flat). No structural code changes — single constant retune. | Inviscid: **10-NACA ratio 0.430 (+57.0%)**, **20-airfoil diverse ratio 0.572 (+42.8%)** — both far below the 0.70 target. Viscous (N-gated at ≤100): **CL M/D = 0.872 (+12.8%)**, **CD M/D = 0.857 (+14.3%)**, combined 0.864 (+13.6%). 66/66 facade tests pass. NACA 4455 = 100% bit-exact preserved. gen-double idempotent. Tripwire tightened from 0.75 to 0.55 to catch regressions of the v3 gain. |
| B1 v4 | (1) Confirmed empirically that the viscous N>100 gate is structural, not an exponent artifact — probing with the gate disabled under v3's 0.2 exponent regressed combined viscous ratio from 0.864 to 1.098. Newton-attractor perturbation at high N is fundamental to biased PANGEN on the coupled system. (2) **Per-surface sensor normalization.** Finding the LE via min-x on the panel samples and normalizing the upper/lower segments independently. On cambered airfoils the upper surface dominates global-max normalization, starving the lower surface of bias even though it has its own meaningful gradient structure. | Inviscid: 10-NACA **0.412** (+4.4% over v3), 20-div **0.564** (+1.4% over v3). Viscous improvement is dramatic: **CL M/D = 0.715 (+17.1% over v3)**, **CD M/D = 0.806 (+5.1% over v3)**, **combined 0.760 (+10.4% over v3)** — cambered airfoils make up most of the viscous set and benefit strongly from lower-surface bias. 66/66 facade tests pass. NACA 4455 = 100% bit-exact preserved. gen-double idempotent. |
| B1 v5 | BL-aware sensor probe. Tested four cheap Cp-derived BL proxies: stagnation-weight term (Cp>0 boost) at 0.1 and 0.5, viscous-specific boost scale at 0.7 and 1.3. **All regressed or stayed neutral.** v4's tuning (inviscid-Cp sensor, exp=0.2, per-surface norm, boost scale 1.0 same as inviscid, N-gate ≤100, no stagnation term) is the empirical sweet spot. `stagnationWeight` parameter kept in `SolutionAdaptivePanelBias.BuildFromCpGradient` API for future use; a proper two-viscous-solve BL shear-stress sensor remains deferred. Added a 7-airfoil diverse-camber tripwire test (0.70 threshold) to complement the 6-airfoil smoke tripwire (0.55). 8/8 Modern tests pass. | No B1 behavior change vs v4. v5 is a documented null-findings iteration: locks v4 as the empirical optimum. |
| B1 v6 | Polar-sweep B1 probe. Tested two approaches to layer B1 onto `SweepViscousAlpha`: (1) replace the warm-start sweep loop with a per-α `this.AnalyzeViscous` iteration (+63% CL-error regression on NACA 2412 N=80 — losing warm-start cost more than gaining A1+B1); (2) push a single mid-α B1 bias around Doubled's warm-start sweep (still regressed, sumErrM 5.883e-2 vs 4.975e-2). Warm-start carries state keyed to panel positions; B1 changes panel positions → warm-start state mis-aligns. **B1 and warm-start polar sweeps are architecturally incompatible.** A proper fix requires reworking `PolarSweepRunner` to fold in per-α panel redistribution — out of scope for Modern-only overrides. | No B1 behavior change vs v5. `Modern.SweepViscousAlpha` inherits `Doubled` unchanged. Polar-sweep B1 is explicitly deferred. Single-point B1 via `AnalyzeViscous` still delivers the v4 viscous gains for users who want per-α B1+A1. |
| B1 v7 | `AirfoilAnalysisService.SweepViscousCL` (Type-2 polar: fixed Re, sweep CL) — override routes each CL target through `this.AnalyzeInviscidForLiftCoefficient` + `this.AnalyzeViscous` per point. Mirrors the A2 pattern for Re sweep. Unlike `SweepViscousAlpha`, the CL-target finder already does a fresh inviscid solve per point, so there's no warm-start to lose here. Each point inherits full Modern stack: A1 multi-start at all N, B1 bias at N ≤ 100. | CL-sweep now gets Modern's B1+A1 robustness. New test `ModernTree_SweepViscousCL_RoutesThroughAnalyzeViscous_PhysicalOnCambered` asserts physical-envelope convergence on NACA 4412 at CL=0.3..0.9. 67/67 Phase 2+3 facade tests pass. NACA 4455 = 100% bit-exact preserved. gen-double idempotent. |
| B1 v8 | `AirfoilAnalysisService.SweepInviscidAlpha` (Type-1 inviscid polar) — override routes each α point through `this.AnalyzeInviscid`, picking up B1 bias. Unlike viscous SweepViscousAlpha (v6 deferred), the inviscid sweep is a simple per-α loop with NO warm-start (each call builds fresh panel state via PANGEN), so B1 composes cleanly without architectural conflict. | Inviscid polar sweeps now get Modern's B1 bias. New test `ModernTree_SweepInviscidAlpha_RoutesThroughB1_AndReducesCoarseError` asserts that on NACA 4412 α=0..8° at N=80, Modern's aggregate CL error beats Doubled (tripwire passes). |
| B1 v9 | `AirfoilAnalysisService.SweepInviscidLiftCoefficient` (Type-2 inviscid polar: sweep CL targets) — override wraps the base call in a B1 bias scope keyed to `initialAlphaDegrees`. The base method runs an internal Newton alpha-finder per CL target with `alphaGuess` warm-starting across targets, but unlike viscous warm-start, the inviscid α-guess is a pure scalar convergence hint — it's compatible with any panel layout, so a single bias for the whole sweep works cleanly. | Completes B1 API coverage on the inviscid side. 69/69 facade tests pass. NACA 4455 = 100% bit-exact preserved. Final B1 API coverage: inviscid variants (single-point, α-sweep, CL-sweep, CL-target) all biased; viscous variants (single-point, CL-sweep, Re-sweep) all biased at N ≤ 100 with A1; only viscous α-sweep deferred due to the warm-start architectural constraint documented in v6. |
| B1 v10 | CLI integration: new `viscous-point-modern <naca> <alpha>` command. The existing `viscous-polar-naca-modern` command uses `Modern.SweepViscousAlpha` which inherits Doubled (per v6 deferral), so it did NOT demonstrate B1+A1 gains in practice. The new single-α command goes through `Modern.AnalyzeViscous` — the path where B1 bias (at N ≤ 100) AND A1 multi-start both actually apply. Output reports whether B1 is active for the chosen panel count. | Makes B1+A1 user-visible via CLI. Smoke-tested: NACA 4412 α=4 N=80 → CL=0.993 CD=0.004 Quality=PHYSICAL, "B1 applied: YES". 69/69 facade tests pass. NACA 4455 = 100% bit-exact preserved. |
| B1 α-range validation | Measured panel-efficiency at α ∈ {0°, 2°, 4°, 6°, 8°} on both the 10-NACA default set and the 20-airfoil curated set. **B1 wins at every α on both sets.** 10-NACA mean M/D = 0.464 (54% improvement across α); 20-airfoil mean M/D = 0.611 (39% improvement). The α=4° tuning target produces the strongest improvement but the bias is robust — even at α=0° (weakest Cp gradient) the 20-airfoil ratio is 0.703, meeting the 0.70 target. | Confirms B1 isn't an α=4°-tuning artifact. Documented in Phase3TierBMetrics.md α-range validation table. |
| B1 v11 | **B1 viscous DISABLED.** Re-range validation revealed the v2-v5 viscous "wins" (combined M/D = 0.760 at Re=1e6) were coincidental, not generalizable. Measured M/D across Re ∈ {3e5, 5e5, 7e5, 8e5, 9e5, 1e6, 1.1e6, 1.2e6, 1.5e6, 2e6, 3e6}: WIN at Re=1.0e6 (0.760) and Re=1.2e6 (0.808); REGRESS at all other Re (ratios 1.02-1.40). Per-N breakdown at Re=3e5 showed B1-active N=80 regresses CL by 65% and CD by 5%. Biased panel redistribution perturbs the Newton-attractor landscape in Re-dependent ways the Cp-based sensor cannot predict. | Honest engineering retreat. Modern viscous now returns bit-exact Doubled output when primary result is physical (A1 multi-start only fires on non-physical primaries). B1 INVISCID retains all v4 tuning — inviscid wins (57% 10-NACA, 43% 20-div) are real and Re-independent since inviscid is Re-invariant. Phase3TierBMetrics.md updated with the Re-range findings table. 69/69 facade tests pass. NACA 4455 = 100% bit-exact preserved. |
| B1 v12 | **Cleanup after v11.** Removed orphaned viscous-B1 infrastructure: `B1ViscousMaxPanels`, `B1ViscousStagnationWeight`, `B1ViscousBoostScale` constants (no longer referenced after v11 disabled viscous B1). Renamed `TryComputeB1ScaleFromInviscid` → `TryComputeB1Scale` to reflect its role (now only called from `SweepInviscidLiftCoefficient`). Dropped the `stagnationWeight` parameter on `SolutionAdaptivePanelBias.BuildFromCpGradient` since the v5 experiment was abandoned. | No behavior change. 59/59 facade tests pass. NACA 4455 = 100% bit-exact preserved. Inviscid 20-div M/D = 0.564 (unchanged from v4-v11 — stagnationWeight was always 0 for inviscid, so the code removal is a pure refactoring). |
| B1 Mach-range validation | Added `--mach <M>` flag to `--panel-efficiency` harness. Measured B1 inviscid M/D across Mach on both sets. **Mach-robust.** 10-NACA: M=0 → 0.412, M=0.3 → 0.409, M=0.5 → 0.420, M=0.7 → 0.548. 20-airfoil: M=0 → 0.564, M=0.2 → 0.563, M=0.4 → 0.569, M=0.6 → 0.651. Even at transonic M=0.7 the bias still delivers 45% improvement on NACA. Contrasts with viscous Re-sensitivity (v11) — the inviscid pipeline's linear behavior is fundamentally different from the viscous coupled-Newton. | Confirms inviscid B1 is a genuine algorithmic win, not a tuning artifact at M=0. Documented in Phase3TierBMetrics.md Mach-range validation table. |
| B1 v16 | Probed percentile normalization (90th/95th pctile instead of max) to lift secondary peaks on aggressive Selig airfoils. Per-airfoil behavior: aggressive-Selig cases improved (s1223 0.981→0.938, e387 0.836→0.753, ah79k135 0.557→0.337), but thick symmetric NACAs regressed hard (0018 0.095→0.785, 0024 0.179→0.846) — they depend on concentrated LE bias. Net: 20-div +4.5pp, 10-NACA -8.7pp. Trade-off, not pure win. | Reverted to v4 max-normalization. Documented as a known trade-off: concentrated bias helps single-peak airfoils, distributed bias helps multi-peak airfoils. A per-airfoil adaptive normalization is the proper fix; deferred. No behavior change vs v12-v15. |
| B1 v17 | Probed adaptive normalization via peak/mean ratio gate: if peak/mean > threshold, use max norm (single-peak); else use 90th-percentile norm (multi-peak). Tested threshold ∈ {4.0, 2.5, 1.5}. All cases pass the threshold because every airfoil has at least one dominant peak even when overall multi-peak — the peak/mean metric doesn't discriminate single-peak from multi-peak topology. | No behavior change (gate never fires). A proper multi-peak discriminator needs second-peak detection (via 2nd-derivative local maxima or sorted-peak 2nd-largest ratio); that's deferred with the underlying sensor work for aggressive Selig airfoils. 10/10 Modern tests pass. NACA 4455 = 100% bit-exact preserved. |
| B1 v18 | Implemented proper 2nd-peak-ratio discriminator: suppress neighborhood around global max per surface, find 2nd-largest peak, compare ratio against threshold. Tested thresholds 0.5 (too aggressive, 10-NACA regressed to 0.433) and 0.7 (marginal: 10-NACA 0.412→0.410, 20-div 0.564→0.563). Long-tail aggressive Selig cases (s1223 0.975, e387/rg15/sd7037 ~0.85) essentially unchanged from v4. | Reverted — gains are within measurement noise, not worth the code complexity. The long-tail gap is a sensor-design limitation (inviscid Cp proxy misses BL-specific features), not a normalization-choice issue. Locked at v4 max-norm. Documented in code comment. 10/10 Modern tests pass. NACA 4455 = 100% bit-exact preserved. |
| B1 v19 | Re-swept `B1SmoothingExponent` under v4 per-surface max-norm to verify v3's choice of 0.20 after all the normalization work. Result: Pareto-optimal at 0.20. 10-NACA min at 0.25 (0.407 vs 0.412 at 0.20, +0.005pp), 20-div min at 0.15 (0.562 vs 0.564 at 0.20, +0.002pp). Neither strictly dominates 0.20. Mean-of-both is minimized at 0.20 (0.488). | No scalar retune beats v4. Documents the exponent Pareto frontier in Phase3TierBMetrics.md. |
| B1 v20 | New `--b1-benchmark` harness mode. Runs full B1 validation matrix (α ∈ {0,2,4,6,8}° on M=0, plus M ∈ {0,0.3,0.5,0.7} at α=4°) on both 10-NACA default and the 20-airfoil curated set in a single command. Optional `--json <path>` emits a structured report for regression tracking over time. | Consolidates previously-scattered validation runs (iter 10 α-sweep, iter 13 Mach-sweep) into one authoritative harness. Smoke-tested — output matches prior measurements exactly (α=4,M=0: 0.412/0.564). 10/10 Modern tests pass. NACA 4455 = 100% bit-exact preserved. |

## Test status

- Modern + Doubled + facade + envelope tests: 66/66 pass (post-B1)
- ModernTreeFacadeTests: 8/8 pass — constructs, alpha sweep, Type-3 polar, B1 symmetry preservation, B1 active-on-high-gradient, B1 N=640 convergence guard, B1 NACA-smoke tripwire (<0.55), B1 diverse-camber tripwire (<0.70)
- Build: 0 warnings, 0 errors
- NACA 4455 smoke gate: 4455/4455 = 100% bit-exact (re-verified after B1 v4)
- Selig 100k full gate: 100228/100228 = 100% bit-exact (unaffected by Modern overrides — Modern does not touch Float path)

Pre-existing failures (47 tests across various classes — `BlockTridiagonalSolverTests`, `BoundaryLayerCorrelationsTests`, `ConformalMapgenServiceTests`, `DiagnosticDumpTests`, `LinearVortexInviscidSolverTests` precision-tolerance, `ViscousSolverEngineTests` NACA 0012 spurious-Newton, etc.) come from earlier perf commits that stripped trace logging, made the BTSS solver float-only, and tightened ULP tolerances. None caused by Phase 3 changes.

## What's next — Tier B refined physics

Each Tier B override is a substantial single-feature project. Each must:
1. Land as a single override method on Modern (no changes to #1 or #2).
2. Continue to pass `ModernTreeAgreesWithDoubled_BitExact_NoOverrides` for un-overridden methods.
3. Continue to pass NACA 4455 + Selig 100k bit-exact gates (Modern overrides don't touch the Float path).
4. Pass its primary metric and all guards as defined in `agents/architecture/Phase3TierBMetrics.md`.

| Tier | Override | Scope | Status |
|------|----------|-------|--------|
| B1 | Solution-adaptive panel refinement — a-posteriori refinement using Cp / BL shear-stress gradients from a first solve, layered on top of the existing curvature-adaptive PANGEN distribution | ~1-2 days | **Active (2026-04-20).** Metrics defined in `Phase3TierBMetrics.md`. Scope corrected — the Float tree already ports PANGEN's curvature-weighted Newton equidistribution (`CurvatureAdaptivePanelDistributor`, renamed from the previously-misleading `CosineClusteringPanelDistributor`). Modern adds the solution-adaptive step Fortran XFoil doesn't do. |
| B2 | DAMPL2 transition correlations with proper regime gating | ~2-3 days | Plateau (iter 14). Facade-layer approaches (DAMPL2 tuning, LSB heuristic) all null-found. Bit-exact baseline: e387 mean errU=0.186 errL=0.245. Needs in-solver LSB bubble modeling — out of facade scope. |
| B3 | 2nd-order BL closure (Drela MSES corrections) | ~3-5 days | **v7+v8 auto-ramp + amplification carry LANDED + tested (ralph-loop iters 10-50).** FMA=on aggregate: 49 scored vs baseline 33; DIVERGED 16→**0**; mean \|ΔCL\| 0.334→**0.293** (−12%); rms \|ΔCL\| 0.400→**0.330** (−18%); mean \|ΔCD\| 0.01113→0.02839 (targeted CD floor closes 11% of deep-stall gap, iter 42). Dramatic rescues: NACA 4412 α=16° \|ΔCL\| 0.809→**0.093**; NACA 0012 α=19.2° DIVERGED→\|ΔCL\|=**0.011**; NACA 0012 α=14.2° M=0.15 under FMA=off CL 1.111→**1.260** via amplification carry (iter 46). v7 wired into both single-point and polar-sweep CLIs (`viscous-point-modern`, `viscous-polar-naca-modern`, `viscous-polar-file-modern`). 4 regression tests in ModernTreeFacadeTests (14/14 passing in both FMA modes); 68/68 total facade tests pass. Remaining gap to win target (mean \|ΔCL\| < 0.15): core MSES 2nd-order BL closure port or full BL state threading (TINDEX + secondary snapshots). |
| B4 | Full KT geometric transformation + wake/BL consistency | ~1 week | Iter 1 bugfix landed (clMach2=0 hardcode). Current RMS Cp=0.0886 vs win target <0.10 (already met at M=0). Blocked for transonic: no Ladson M=0.5/0.7 data ingested yet. |

### B4 scope correction (2026-04-20)

The earlier B4 framing ("Karman-Tsien compressibility correction, replacing
legacy linearised form") was a misread of the codebase. Classic Fortran XFoil
(CPCALC/CLCALC in `f_xfoil/src/xfoil.f`) already implements proper
Karman-Tsien:

- `COMSET` sets `TKLAM = M²/(1+β)²` and `BFAC = 0.5·M²/(1+β)`.
- `CPCALC` applies `Cp = Cp_inc / (β + bfac·Cp_inc)` — the KT correction
  with `Cp_inc` in the denominator (not just PG).

Our float-parity tree mirrors this faithfully
(`LinearVortexInviscidSolver.IntegratePressureForces` /
`ComputePressureCoefficients`). Real B4 scope, therefore, is what Fortran
does NOT do:

1. Full KT geometric transformation (transform geometry → solve
   incompressible → transform back). More accurate than KT-at-Cp for thick
   airfoils at high M.
2. Extend KT consistency to the wake/BL. `ViscousNewtonUpdater.cs:498`
   hardcodes `CPG = CGINC` (M=0 assumption).
3. Populate `dCL/dM²` — `IntegratePressureForces:551` hardcodes
   `clMach2 = 0.0`; Fortran `CLCALC` does accumulate it. (#1-tree parity
   bug, not Phase-3 scope, but worth flagging.)

See `Phase3TierBMetrics.md` for the full B4 scoring plan.

## Other followups, not blocking

- Parametric airfoil mesher for OpenFOAM so the 1000-case dataset becomes absolute-reference-quality.
- Extend `windtunnel.json` to include NACA 23012 (5-digit; currently dropped — `NacaAirfoilGenerator` only supports 4-digit) by shipping `tools/external-references/geometry/naca23012.dat` and switching the airfoil string to a path.
- The 47 pre-existing test failures (precision tolerances, removed traces, NACA 0012 spurious Newton) — all from pre-Phase-3 perf commits, low-priority cleanup.

## Key files for resume

- Plan file: `/home/holyglory/.claude/plans/swift-stirring-meerkat.md` (original Phase 3 plan).
- This status: `agents/architecture/Phase3Status.md`.
- Modern code: `src/XFoil.Solver/Services/AirfoilAnalysisService.Modern.cs`, `src/XFoil.Core/Services/NacaAirfoilGenerator.Modern.cs`.
- Tests: `tests/XFoil.Core.Tests/ModernTreeFacadeTests.cs`.
- Harness: `tools/fortran-debug/ParallelPolarCompare/Program.cs` (search for `--triple-sweep` and `--reference-sweep`).
- Reference data: `tools/external-references/`.
- README index: `README.md` (top-level).
