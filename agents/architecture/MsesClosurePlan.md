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

New assembly: `src/XFoil.MsesSolver/`.

- Does **not** replace `XFoil.Solver`. The XFoil closure is parity-critical
  against Fortran and must stay bit-exact.
- Parallel to `XFoil.Solver`, same consumer surface: `AnalyzeViscous(geom,
  α, settings) → ViscousAnalysisResult`. The CLI and reporting layers accept
  either solver through a shared interface (to be extracted in step 0).

This keeps the existing parity gate (`4455/4455 bit-exact`) completely
untouched and lets MSES work proceed in parallel without risk to the primary
deliverable.

## Implementation phases

### Phase 0 — Interface extraction (prep)

- Extract `IAirfoilAnalysisService` covering the public methods of the
  existing facade.
- Shift CLI/reporting to the interface.
- No behavioural change; parity must still be 4455/4455.

**Acceptance:** parity gate passes; unit tests green.

### Phase 1 — Closure scaffolding ✅ STARTED (2026-04-21, commits 14ca1ee → de49ee6)

- ✅ Port the closure relations from thesis §4 as standalone pure functions:
  `H*(H, Reθ)`, `Cf(H, Reθ, Me)`, `CD(H, H*, Reθ, Me)`, `Cτ_eq(H, Us, M)`,
  `Hk(H, M)`, etc.
- ✅ Each relation gets a unit test against Drela's Appendix A tabulated values
  or sample curves. No solver wiring yet.

**Landed in `src/XFoil.MsesSolver/Closure/MsesClosureRelations.cs`:**

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
- ⏳ **Phase 2e:** Full Drela §4.2 energy integral for dH/dξ (replaces
  the Clauser relaxation in 2c/2d). Requires thesis primary-source
  verification of pressure-gradient term signs.

**Landed in `src/XFoil.MsesSolver/BoundaryLayer/`:**

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
- ⏳ **Phase 3c:** NACA 0012 xtr(α=0, Re=3e6, nCrit=9) parity against
  XFoil C# baseline. Requires wiring the composite marcher into a
  real Ue(x) distribution from the inviscid solver. Scope creep into
  Phase 5 (Newton coupling) — probably bundled there.

**Acceptance:** NACA 0012 xtr(α=0, Re=3e6, nCrit=9) within 1 % of the
XFoil C# baseline. Not yet run; blocked on Phase 5.

### Phase 4 — Separation + reattachment (the point)

- The 2nd-order lag closure is what makes MSES stall-robust. Phase 4 is the
  validation that the marcher stays well-posed for Hk > 4 (separated
  turbulent) without the Newton divergence XFoil exhibits.
- Test matrix: the exact cases XFoil currently fails on (NACA 0012 α ≥ 10°
  at M ≥ 0.15, NACA 4412 α ≥ 12°). Target: converged Newton iteration that
  produces a physical CL(α) curve through stall.

**Acceptance:** Phase3 Tier B showcase set (currently ramp-rescued) has
≥ 80 % native convergence without any facade-level rescue.

### Phase 5 — Newton coupling to inviscid

- Reuse `XFoil.Solver`'s linear-vortex inviscid solver — the inviscid
  side is identical in XFoil and MSES for our (incompressible) scope.
- Re-derive the viscous-inviscid Jacobian blocks from Drela's thesis §6.
- Integrate into the existing global Newton assembler; keep the same block-
  tridiagonal solver.

**Acceptance:** full airfoil polars on NACA 0012 / 4412 / 23012 within
reasonable agreement (mean |ΔCL| < 0.05, mean |ΔCD| < 0.002 vs WT) in the
α-range where MSES is expected to be state-of-the-art.

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
