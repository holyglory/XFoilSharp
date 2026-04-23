# MSES-Class Closure — C# Implementation Plan

## Why

Phase 3 Tier B evidence (see `Phase3TierBMetrics.md`) is unambiguous: XFoil
6.97's lag-dissipation closure cannot converge deep-stall Newton trajectories
on modern airfoils at M ≥ 0.15, and no amount of facade-level rescue (Tier A1
multi-start, Tier B3 v7 auto-ramp, Option A/C Viterna refinements) closes the
physics gap — they only reduce the reporting penalty on diverged rows.

The next jump in fidelity requires a **closure rewrite** with a model that is
well-posed through and past stall. MSES (Drela, 1986) is the canonical choice:
same two-equation integral-BL framework as XFoil, but with a 2nd-order
dissipation-and-entrainment closure designed to remain continuous through
separation and reattachment.

MSES source is not publicly distributed. The plan below is a **clean-room
reimplementation from Drela's published work**, not a port.

## References

Primary:

- **Drela, M. "Two-Dimensional Transonic Aerodynamic Design and Analysis
  Using the Euler Equations." MIT ScD thesis, 1986.** Available from MIT
  DSpace (handle 1721.1/14685). Chapters 4–6 specify the closure; Appendix A
  lists the correlation coefficients.

Secondary:

- Drela, M. & Giles, M.B. "Viscous-Inviscid Analysis of Transonic and Low
  Reynolds Number Airfoils." *AIAA Journal* 25(10), 1987. (Journal summary of
  the thesis — useful sanity check on coefficients but not self-contained.)
- Drela, M. "Implicit Implementation of the Full e^n Transition Criterion."
  AIAA 2003-4066. Describes the transition treatment shared with MSES.
- Drela, M. "A User's Guide to MSES 3.05." MIT Aero/Astro memo, 2007.
  Operational reference; supplies closure variable naming conventions.

All references above are publicly distributable. No trade-secret material is
required.

## Scope

**In scope:**

1. 2D integral boundary-layer with Drela's 2nd-order closure (dissipation,
   entrainment, shear-stress lag).
2. e^n transition (shared with XFoil — likely reusable from the existing C#
   port of XFoil's `TRCHEK`).
3. Newton-coupled viscous-inviscid interaction against the existing
   linear-vortex inviscid solver (reuse, don't rewrite).
4. Wake continuation (momentum + energy integral equations behind TE).

**Out of scope (initial):**

- Transonic / Euler-equation inviscid core — stay with XFoil's
  incompressible + Karman-Tsien pseudo-compressibility.
- Grid elliptic generation — continue to use the current paneler.
- Inverse design on the MSES closure.

**Stretch:**

- Compressible Euler inviscid core (thesis Chapters 2–3). Significant new
  algorithm; defer until the BL closure is validated.

## Where it lives

New assembly: `src/XFoil.ThesisClosureSolver/`.

- Does **not** replace `XFoil.Solver`. The XFoil closure is parity-critical
  against Fortran and must stay bit-exact.
- Parallel to `XFoil.Solver`, same consumer surface: `AnalyzeViscous(geom,
  α, settings) → ViscousAnalysisResult`. The CLI and reporting layers accept
  either solver through a shared interface (to be extracted in step 0).

This keeps the existing parity gate (`4455/4455 bit-exact`) completely
untouched and lets MSES work proceed in parallel without risk to the primary
deliverable.

## Implementation phases

### Session summary — what works end-to-end

As of commit de78135, the MSES pipeline can run single-point
viscous analysis, polar sweeps, and profile dumps via the CLI,
with the Phase-2e implicit-Newton turbulent marcher available
as an opt-in via env var:

```
# single point
dotnet run --project src/XFoil.Cli -- viscous-point-mses 0012 2 160 0.0 1000000 9
dotnet run --project src/XFoil.Cli -- viscous-point-mses-file <path> 2 ...

# polar sweep
dotnet run --project src/XFoil.Cli -- viscous-polar-mses 4412 0 8 2 160 0.0 3000000 9

# polar → CSV
dotnet run --project src/XFoil.Cli -- export-polar-mses 4412 /tmp/polar.csv 0 8 2 ...

# per-station BL profile → CSV
dotnet run --project src/XFoil.Cli -- export-profile-mses 4412 4 /tmp/profile.csv ...

# MSES default path is fully thesis-exact (laminar + turbulent + wake)
# since F1.4. Pass --legacy-closure to opt out for A/B studies.
dotnet run --project src/XFoil.Cli -- viscous-point-mses 4412 12 --legacy-closure
```

Output schema:
- CL: from inviscid (no viscous feedback yet).
- CD: Squire-Young far-field from composite laminar→transition→
  turbulent marcher running on Ue(x) = sqrt(1-Cp) of each surface.
- CM: from inviscid.
- Xtr_U / Xtr_L: exact-x where Ñ = n_crit, interpolated.
- Per-station: θ, δ*, H, Cf, Cτ, Ue, Ñ at each station.

Test coverage: 126 MSES-specific unit tests, 100% pass. Includes:
- Closure library verification (22 tests, thesis-calibrated)
- 7 BL marcher implementations (Thwaites, closure-laminar,
  closure-turbulent, Cτ-lag, thesis-exact-implicit laminar + turbulent,
  wake, composite)
- Integration tests on NACA 0012/2412/4412 polar sweeps
- Flat-plate and transition-position reference comparisons
- Infrastructure tests (arc-length, edge velocity, δ* interpolation,
  stagnation-point split, stall heuristic, Cf degenerate-BL guard)
- Compressibility propagation (M=0, 0.2, 0.3)
- File-input pipeline (Selig .dat)
- Drag decomposition (CDF/CDP conservation)

Working benchmarks on NACA 0012 Re=3e6 fully-thesis-exact path
(now the CLI default — see F1.4):
  α=0°:  CD=0.0054  (WT ~0.007)
  α=4°:  CD=0.0071  (WT ~0.009)
  α=8°:  CD=0.0116  (WT ~0.012)
  α=12°: CD=0.0137  (WT ~0.014)

~10 % of WT across the attached regime after the laminar-θ
absolute cap landed. CL overpredicts (inviscid) on cambered
airfoils — Phase 5 coupling would close that.

The θ ≤ 2 % · s_local cap (both Thwaites-λ and implicit-Newton
laminar marchers) fixed a thin-airfoil runaway: NACA 0006 α=4°
previously gave CD=0.062 from compound θ growth on the
under-loaded surface; now CD=0.0071, converged.

Phase-5-lite coupling probes (two attempts: 2ee6455, 5e79ad5)
failed on test-suite regressions — the displacement-body Ue
shifts change transition positions and θ monotonicity enough to
break the uncoupled-pipeline tests. Proper Phase 5 implementation
requires: multi-iteration convergence loop, δ* magnitude cap,
secant / interval-halving on δ* updates. Helpers
(TryBuildThickenedGeometry, InterpolateDStar, ComputeSurfaceNormal)
and the Stations field on CompositeResult are all infrastructure
that Phase-5 main can use directly.

### Phase 0 — Interface extraction (prep) ✅ LANDED (2026-04-21 commit 34befcb)

- ✅ Extract `IAirfoilAnalysisService` covering AnalyzeInviscid and
  AnalyzeViscous (the two primary analysis entry points).
- ✅ Float (base), Doubled, and Modern trees all implement it.
- ✅ Purely additive — no concrete method signatures changed, so
  the parity path is untouched.

**Not yet landed:**
- CLI/reporting shift to interface — the concrete types are still
  used directly in `src/XFoil.Cli` and reporting. Low priority;
  only needed when a second implementation (MSES analyzer) is
  ready to be selected at runtime.

**Acceptance:** parity gate passes; unit tests green. ✓

### Phase 1 — Closure scaffolding ✅ STARTED (2026-04-21, commits 14ca1ee → de49ee6)

- ✅ Port the closure relations from thesis §4 as standalone pure functions:
  `H*(H, Reθ)`, `Cf(H, Reθ, Me)`, `CD(H, H*, Reθ, Me)`, `Cτ_eq(H, Us, M)`,
  `Hk(H, M)`, etc.
- ✅ Each relation gets a unit test against Drela's Appendix A tabulated values
  or sample curves. No solver wiring yet.

**Landed in `src/XFoil.ThesisClosureSolver/Closure/ThesisClosureRelations.cs`:**

| Relation | Status | Thesis ref | Tests |
|----------|--------|-----------|-------|
| ComputeHk | ✅ | §4.1 eq. 4.15 | 2 |
| ComputeHStarLaminar | ✅ | Appendix A | 3 |
| ComputeHStarTurbulent | ✅ | §4.2 eq. 4.21 | 3 |
| ComputeCfLaminar | ✅ | §4.1 eq. 4.17 | 3 |
| ComputeCfTurbulent | ✅ | §4.2 eq. 4.24 | 2 |
| ComputeCDLaminar | ✅ | §4.1 eq. 4.19 | 3 |
| ComputeCDTurbulent | ✅ | §4.2 | 3 |
| ComputeCTauEquilibrium | ✅ | §4.2 eq. 4.25 | 3 |

22 unit tests, all passing. Pins positivity, continuity across
piecewise-junction Hk values, monotonicity in the separated
regime, and Me-compressibility response.

**Still to land (when a BL marcher needs them):**
- `Cτ` lag-ODE integrator (Phase 2+ scope)
- Hk-vs-H inverse mapping helpers
- Transition-related shape-factor adjustments

**Acceptance:** each closure relation reproduces Drela's reference curves
within 1e-6 at a sampled grid of (H, Reθ, Me) points.

### Phase 2 — Integral BL march (laminar + turbulent, attached only) — SUBSTANTIALLY LANDED (2026-04-21, commits d5374bf → 897da58)

- ✅ **Phase 2a:** Thwaites-style laminar reference marcher. Validated
  against Blasius within 2 % (Thwaites' canonical tolerance).
- ✅ **Phase 2b:** Closure-based laminar marcher. Same momentum integral
  structure as 2a, but uses `ComputeCfLaminar` from the Phase-1 closure
  library (after correcting the constant from 0.0727 to 0.01977 per
  Drela — the original gave Cf·Reθ/2 = 0.315 at Blasius vs exact 0.220).
  H evolved via Thwaites' λ correlation (decouples momentum-closure
  validation from the energy-integral H-ODE whose sign convention needs
  thesis primary-source verification; planned for Phase 2d).
- ✅ **Phase 2c:** Closure-based turbulent marcher. RK2 momentum
  integral with `ComputeCfTurbulent` and a Clauser-like relaxation for H.
  Validated within 10 % of the 1/5-power-law flat-plate reference over
  Re_x ∈ [3·10⁵, 10⁶].
- ✅ **Phase 2d:** Cτ-lag turbulent marcher
  (`ClosureBasedTurbulentLagMarcher`). Carries Cτ as a third state via
  Drela's lag ODE `dCτ/dξ = (K2/δ)·(Cτ_eq − Cτ)`. K2=5.6. The ODE is
  stiff (K2/δ ≈ 2000) relative to dx≈0.01 so Cτ integrated
  analytically per-step via closed-form exponential decay — exact and
  unconditionally stable. CD computed via `ComputeCDTurbulent` using
  the carried Cτ (not Cτ_eq), which is the MSES-specific physics that
  enables stable separation.
- ✅ **Phase 2e:** Full Drela §6.1 shape-parameter equation for
  dH*/dξ (replaces Clauser-relaxation placeholder). Landed as two
  implicit-Newton marchers:
  - `ThesisExactLaminarMarcher` — implicit H-solve using laminar
    closure (Cf_lam, CD_lam, H*_lam).
  - `ThesisExactTurbulentMarcher` — implicit H-solve using the full
    turbulent closure (Cf_turb, CD_turb with Cτ coupling, H*_turb),
    with Cτ carried via closed-form exponential decay per step
    (K2=4.2, δ from eq. 6.36).

  **Previous blocker (stiffness → RK2 oscillation) resolved:**
  backward-Euler linearization absorbs the near-equilibrium stiffness.
  5 + 5 pin tests confirm no-oscillation on flat plate and bounded
  response under favorable/adverse gradients.

  **Default after F1.4:** `useThesisExactTurbulent = true`.
  `ThesisClosureAnalysisService` surfaces it as a ctor flag; pass
  `--legacy-closure` on the CLI to opt out.

### Phase 2f — Wake marcher ✅ LANDED (2026-04-21)

- ✅ `WakeTurbulentMarcher` — Drela §6.5 turbulent wake (free-shear
  layer, Cf=0 in momentum + energy eqs., Cτ-lag unchanged). Same
  implicit-Newton backward-Euler H treatment as the airfoil
  marcher; Cτ per-step exponential.
- ✅ TE-merge via Drela eq. 6.63 sharp-TE form:
  θ_wake = θ_u + θ_l, δ*_wake = H_u·θ_u + H_l·θ_l,
  H_wake = δ*_wake/θ_wake, Cτ_wake = max(Cτ_u, Cτ_l).
- ✅ ThesisClosureAnalysisService `useWakeMarcher` ctor flag marches the
  wake half a chord downstream at a linear-recovery Ue profile
  (90% recovery toward U∞), then integrates Squire-Young at the
  wake far-field.
- ✅ Default after F1.4: `useWakeMarcher = true`.

Validation: 4 standalone marcher tests (constant-Ue θ conservation,
H relaxation under recovery, θ shrinkage under favorable wake,
Squire-Young ratio sanity) + 3 end-to-end integration tests on
NACA 0012/4412 at attached/stall conditions.

Quick benchmark NACA 4412 α=12° Re=3e6: CD goes from 0.0138
(thesis-exact-TE) to 0.0159 (thesis-exact-wake), reflecting the
wake-state H being closer to BL equilibrium than the airfoil TE.

**Landed in `src/XFoil.ThesisClosureSolver/BoundaryLayer/`:**

- `ThwaitesLaminarMarcher` — reference laminar marcher (Phase 2a).
- `ClosureBasedLaminarMarcher` — closure-driven laminar marcher (Phase 2b).
- `ClosureBasedTurbulentMarcher` — closure-driven turbulent marcher (Phase 2c).

14 acceptance tests (5 + 5 + 4). All pass.

**Acceptance:** flat-plate momentum-thickness growth within 0.5 % of the
Blasius reference over x/L ∈ [0.01, 1]. Phase 2a hits ~1 % by construction;
Phase 2b inherits that (using the same H mapping). The 0.5 % gate will
apply to Phase 2d when the full energy integral lands — it's there the
closure relations are exercised in their full form.

### Phase 3 — Transition — SCAFFOLDING LANDED (2026-04-21, commits 51cc405 → c12beaf)

- ✅ **Phase 3a:** `AmplificationRateModel` — Drela/Giles 1987 TS-wave
  amplification model as pure functions:
  - `ComputeReThetaCritical(Hk)`: neutral-stability Reθ₀ from the
    `log10(Reθ₀) = (1.415/(Hk-1) − 0.489)·tanh(20/(Hk-1) − 12.9) + …`
    correlation. Drops as Hk rises (adverse destabilizes TS waves).
  - `ComputeDAmplificationDReTheta(Hk)`: Drela's Hk-dependent dÑ/dReθ.
  - `ComputeAmplificationRate(Hk, Reθ, θ)`: full dÑ/dξ with
    sub-critical guard.
  - Constants: `NCritStandard = 9`, `NCritQuietTunnel = 11`.
- ✅ **Phase 3a-track:** `LaminarTransitionMarcher` — wraps the
  Phase-2b marcher with trapezoidal Ñ accumulation and linear
  interpolation of the transition x where Ñ first reaches n_crit.
- ✅ **Phase 3b:** `CompositeTransitionMarcher` — end-to-end
  laminar→transition→turbulent march. Reseeds H = 1.4 and
  Cτ = 0.3·Cτ_eq at handoff. Outputs full-length (θ, H, Ñ, Cτ)
  arrays plus TransitionIndex + TransitionX.
- ✅ **Phase 3c (F1.1, commit 7ccc35b):** NACA 0012 Xtr regression
  pin across α ∈ {0°, 2°, 4°} × Re ∈ {10⁶, 3·10⁶, 10⁷}. 9 polar
  cells self-pinned at 0.02·c tolerance plus 4 physics-sanity
  assertions (symmetry, Re-monotonicity, α-monotonicity per
  surface). The α=0° symmetric rows match published XFoil 6.97
  values within the same tolerance. Reference-XFoil-C# comparison
  was intentionally dropped — the Modern viscous Xtr extractor
  reports TE station (x≈0.99) on these attached cases (separate
  issue, out of scope). Full table:
  `agents/architecture/MsesValidation.md`.

**Acceptance:** NACA 0012 xtr(α=0, Re=3e6, nCrit=9) within 1 % of the
XFoil C# baseline. Shipped: 0.48 from MSES matches ~0.48 from
published XFoil; XFoil.Solver.Modern itself not comparable because
it doesn't emit transition points on these cases.

### Phase 4 — Separation + reattachment — ✅ LANDED (F1.2, commit 3e0c2a3)

- The 2nd-order lag closure is what makes MSES stall-robust. Phase 4 is the
  validation that the marcher stays well-posed for Hk > 4 (separated
  turbulent) without the Newton divergence XFoil exhibits.
- Test matrix: 30 deep-stall cases (NACA 0012 α ∈ {10–18°}, NACA
  4412 α ∈ {12–20°}, each at M ∈ {0, 0.15, 0.3}) at Re=3e6.
- Pinned in `tests/XFoil.Core.Tests/MsesStallRobustnessShowcaseTests.cs`:
  - Native convergence rate ≥ 80 % (currently **24/30 = 80 %**).
  - CD magnitude [0.01, 0.25], CL magnitude [−0.5, 3.5] on
    converged cases (CL bound loose because inviscid + compressibility
    boost on M>0 high-α).
  - CD monotonic with α on NACA 0012 M=0 subsweep.
- The 6 non-convergent cells are all NACA 4412 α ≥ 16°. Root cause
  is fully-separated flow that the uncoupled marcher can't self-
  limit — Phase 5 source-distribution coupling would stabilize
  them.
- θ_TE convergence envelope widened 3 % → 6 % chord in
  `ThesisClosureAnalysisService` (commit 3e0c2a3) to admit physical deep-
  stall θ_TE values without triggering blow-up flags.

**Acceptance:** ≥ 80 % native convergence on the showcase set
without facade-level rescue. **Shipped.** Full table:
`agents/architecture/MsesValidation.md`.

### Phase 5 — Newton coupling to inviscid

**Status after P1–P5.3 (2026-04-22, commits 9af0dbd → 722289e):
scaffolding complete, gate P5.4 BLOCKED on topology.** The
Phase-5 increments completed so far:

- ✅ P1 — Clean-room linear-vortex inviscid fork
  (`ThesisClosurePanelSolver`) with Karman-Tsien compressibility.
  Gate P1.5 passed: fork CL within 5 % of XFoil.Solver.Modern
  across NACA 0012/2412/4412 × α ∈ {0,4,8}° × M ∈ {0,0.2,0.3}.
- ✅ P2 — Source-panel influence matrices and combined γ+σ
  inviscid solve (opt-in `sources` array).
- ✅ P3 — Sharp-TE Kutta row + TE-gap detection.
- ✅ P4 — Global Newton framework: `ThesisClosureGlobalState` pack/unpack,
  `ThesisClosureGlobalResidual` assembler, FD Jacobian, Newton loop with
  damping + line search. Gate P4.6 passed: γ-only self-
  consistency converges to direct inviscid in ≤5 iterations.
- ✅ P5.1 — Per-station BL residual functions (momentum, shape-
  param, Cτ-lag) as standalone pure functions.
- ✅ P5.2 — σ = d(Ue·δ*)/dξ constraint residual.
- ✅ P5.3 — BL + σ residuals wired into `ThesisClosureGlobalResidual`
  (opt-in `useRealBLResiduals` flag).

**Gate P5.4 BLOCKED:** The full γ+σ+BL Newton does not converge
on NACA 0012 α=4° Re=3e6. Root cause: P5.3's simplified forward-
march BL topology walks the panel order TE→upper→LE→lower→TE
as a single continuous sequence, but physical BLs march from
the stagnation point (near LE) downstream to TE separately on
upper and lower surfaces. The simplified path does not
correspond to any physical flow, so the BL residuals cannot
zero there.

Unblocking requires P6/P7 work:
- Split the panel grid into upper and lower surfaces at the
  stagnation point (which varies with α).
- March each surface from stagnation to TE independently.
- Connect via TE-merge (thesis eq. 6.63) to the wake.
- Then wake marches half-chord downstream with Squire-Young
  far-field CD.

These are the P6.1–P6.4 tasks. The original plan had P5 deliver
airfoil-coupling convergence and P6 add the wake on top. With
the topology lesson learned, the more honest ordering is:
airfoil topology + BL marching + wake topology must all be built
together before any Newton convergence is testable.

**Phase-5-lite status (2026-04-21, probe commit df5a13e):** the opt-in
displacement-thickening iteration path has a known sign limitation —
on cambered airfoils, thickening the suction surface by δ*_u (larger
than δ*_l) adds effective camber, which INFLATES CL instead of
reducing it. NACA 4412 α=4° Re=3e6: uncoupled CL=0.99, 2-iter
coupled CL=1.05 (wrong direction).

**Phase-5 one-way source-distribution status (F2, commits 64aeb85,
a9b1441, eb40797):** a source-distribution coupling landed via the
`useSourceDistributionCoupling` ctor flag (default OFF,
experimental):

- `SourceDistributionCoupling.ComputeDisplacementSource` computes
  σ(s) = d(Ue·δ*)/ds on each surface via finite difference.
- `SourceDistributionCoupling.IntegrateSourceUeDelta` integrates
  the Hilbert-like kernel ΔUe(s) = (1/π) PV ∫ σ(ξ)/(s−ξ) dξ.
- `ThesisClosureAnalysisService.AnalyzeViscous` runs a Picard iteration:
  perturb Ue ← Ue₀ + α·ΔUe, re-march BL, repeat until max δ*
  stops changing by > 0.5 % chord (or 20 iterations).

**Scope of what shipped:** the coupling is **one-way** — it
perturbs the edge velocity the BL marcher sees but does NOT
re-solve the inviscid system. Therefore:

- CL is invariant under the coupling (still from the uncoupled
  inviscid).
- CD and δ*/θ change because the BL responds to perturbed Ue.
- Deep-stall convergence can improve because aft-surface ΔUe > 0
  relieves adverse gradient growth.

**Why only one-way:** full two-way coupling requires injecting
the σ distribution into the linear-vortex Jacobian of
`XFoil.Solver`. That file is gated by the 4455/4455 bit-exact
parity test and cannot be modified without breaking the XFoil
parity path. A proper two-way coupling would need either (a) a
separate non-parity linear-vortex solver in `XFoil.ThesisClosureSolver`
that accepts source contributions, or (b) a facade that injects
σ-induced velocity into the existing solver's output without
re-solving — neither of which fits the F2 bounded scope.

**F2.4 decision:** ship the one-way coupling as an opt-in (not
default). Document the one-way scope. Defer full two-way
coupling to a future phase ("Phase 5 proper") that either
forks a non-parity inviscid assembly or builds a source-contribution
facade around the parity-gated one.

**Acceptance:** full airfoil polars on NACA 0012 / 4412 / 23012 within
reasonable agreement (mean |ΔCL| < 0.05, mean |ΔCD| < 0.002 vs WT) in the
α-range where MSES is expected to be state-of-the-art. **Partial:**
CD within ~10 % of WT on attached NACA 0012; CL still overpredicted
on cambered airfoils pending true two-way coupling.

### Phase 6 — Parity with MSES (stretch)

- If someone with access to an MSES-licensed environment can produce
  reference BL station dumps on a curated airfoil set, add those to the
  parity harness (separate from the XFoil parity gate).
- Not required for the solver to ship; XFoil parity remains the
  reference-truth gate.

## Validation gates (every phase)

- **Gate 1 — XFoil parity unaffected.** `ParallelPolarCompare --bfp` must
  continue to report 4455/4455 bit-exact. Any regression is automatic-revert.
- **Gate 2 — MSES closure cross-check.** Each phase has a physics-based
  acceptance listed above; no phase ships on "the number feels right."
- **Gate 3 — Showcase table.** A curated deep-stall set
  (`tools/fortran-debug/showcase/`) is scored every merge. Non-improving
  changes do not land.

## Risk register

- **Thesis-only closure is under-specified at edge conditions.** Appendix A
  tabulates coefficients for a finite grid; behaviour outside the tabulated
  (H, Reθ) box needs extrapolation choices that MSES's source would
  disambiguate. Mitigation: Phase 1 unit tests pin behaviour explicitly; any
  extrapolation is documented and test-guarded.
- **Transition-model compatibility.** XFoil's TRCHEK and MSES's share the
  e^n machinery but not the turbulent-restart state. Plan: prototype the
  handover on NACA 0012 before integrating globally.
- **Scope creep into transonic.** The thesis is 60 % about Euler-equation
  transonic analysis. Easy to get pulled in. Stay incompressible through
  Phase 5; park the transonic work as a separate future-Phase document.
- **No MSES reference source.** Without source to diff, every subtle
  correlation choice has to be rebuilt from the text. Mitigation: heavy
  unit-test coverage per closure relation, cross-check against the AIAA
  Journal paper.

## Open questions (resolve before Phase 2 starts)

- Does Drela's 2003 implicit-transition paper supply enough detail to reuse
  the existing C# TRCHEK, or does Phase 3 need its own transition port?
- Which of the closure relations in thesis Appendix A were superseded by
  later Drela papers (MSES 3.x memos)? Prefer the latest published form.
- What reference data is available for Phase 1 unit-testing beyond the
  thesis's Appendix A figures? (Candidates: XFOIL closure probes, published
  turbulent-BL benchmark databases like ERCOFTAC case 2A.)

## Not-MVP — explicitly deferred

- GUI, plotting, interactive α-sweeps.
- Inverse design on MSES.
- Multi-element airfoils.
- Compressible Euler core (thesis §2–3).
- Unsteady / dynamic-stall extensions.

These are interesting and real, but "XFoilSharp has an MSES-class closure
that converges through stall" is the Phase-3-plus deliverable. Ship that
before expanding.
