# XFoil.ThesisClosureSolver — User Guide

The `XFoil.ThesisClosureSolver` assembly is a **hybrid** viscous solver:

- **Inviscid side:** clean-room linear-vortex panel method (Katz &
  Plotkin §11.4), *not* the streamline-Euler MSES of Drela's thesis.
- **Viscous side:** clean-room C# port of the 2nd-order integral
  boundary-layer closure from Drela's 1986 MIT thesis (laminar +
  turbulent + wake, Cτ-lag coupling, Squire-Young far-field CD).

It coexists with the parity-validated `XFoil.Solver` assembly (Fortran-
bit-exact XFoil 6.97 path); both implement `IAirfoilAnalysisService`
so callers pick at construction time.

In four-solver validation (see `FourSolverValidation.md`) this is
currently the only viscous path in the repo that converges on every
case across NACA 0012/2412/4412 at Re = 3·10⁶ without catastrophic
failures. CL is biased ~15–20 % high on cambered airfoils because
viscous displacement does not feed back into the inviscid solve (no
two-way coupling — that would require Option B, a real streamline-
Euler MSES; see the future-work doc).

See `agents/architecture/ThesisClosurePlan.md` for the full phase plan
and `agents/architecture/ThesisClosureValidation.md` for the validation
snapshot.

## Quickstart

```csharp
// Default: fully-thesis-exact (implicit-Newton laminar + implicit-
// Newton turbulent + wake marcher with Squire-Young far-field CD).
IAirfoilAnalysisService svc = new ThesisClosureAnalysisService();
var r = svc.AnalyzeViscous(geometry, alphaDegrees: 4.0, settings);
```

The result carries `CD`, `CDF`, `CDP`, `UpperTransition.XTransition`,
`LowerTransition.XTransition`, and per-station BL profiles.

## CLI

```bash
# Single-point viscous
dotnet run --project src/XFoil.Cli -- viscous-point-mses 4412 4

# Polar sweep (CSV)
dotnet run --project src/XFoil.Cli -- export-polar-mses 4412 /tmp/polar.csv 0 12 2

# Side-by-side against the parity-validated XFoil path
dotnet run --project src/XFoil.Cli -- compare-mses-modern 4412 4
```

All MSES commands accept these position-independent flags:

| Flag | Effect |
|------|--------|
| *(none)* | Default path: thesis-exact laminar + turbulent + wake |
| `--legacy-closure` | Opt out: Thwaites-λ laminar + Clauser placeholder + TE Squire-Young (for comparison studies only) |

The old `--thesis-exact`, `--wake`, `--thesis-laminar` flags and
their `XFOIL_MSES_*` env-var equivalents were removed in F3.2.
The CLI still recognizes the old flags and prints a one-line
"no-op" notice so scripts that pass them keep working, but they
have no effect (the closures they enabled are the default).

## Ctor knobs

```csharp
new ThesisClosureAnalysisService(
    inviscidProvider: null,                      // null → Modern XFoil inviscid
    viscousCouplingIterations: 0,                // Phase-5-lite, experimental
    viscousCouplingRelaxation: 0.3,
    useThesisExactTurbulent: true,               // default
    useWakeMarcher: true,                        // default
    useThesisExactLaminar: true,                 // default
    useSourceDistributionCoupling: false,        // F2, experimental
    sourceCouplingIterations: 20,
    sourceCouplingRelaxation: 0.5);
```

Pass `false` on the three marcher flags to revert to the pre-F1
baseline for A/B studies.

### Source-distribution coupling (experimental, opt-in)

Setting `useSourceDistributionCoupling: true` enables a one-way
Picard iteration:

```text
1. Solve inviscid → Ue₀ on each surface.
2. Run BL → δ*.
3. Compute σ = d(Ue·δ*)/ds; integrate Hilbert kernel → ΔUe.
4. Re-march BL on Ue₀ + α·ΔUe (α = sourceCouplingRelaxation).
5. Repeat until max δ* stops changing by > 0.5 %c.
```

**What it does:** the BL responds to the perturbed edge
velocity. Changes CD and δ*/θ distributions; improves deep-stall
convergence because aft-surface ΔUe > 0 relieves adverse gradient
growth on the upper surface.

**What it does NOT do:** the inviscid system is not re-solved,
so CL is unchanged (still the inviscid value). Full MSES-style
two-way coupling would modify CL by injecting σ back into the
linear-vortex Jacobian — that requires a non-parity inviscid
path and is deferred to a future phase.

Use this flag for:

- Better CD estimates past the attached regime.
- Higher deep-stall convergence rate on cambered airfoils.
- Diagnostic comparison against the uncoupled pipeline.

Don't rely on it for cambered-airfoil CL accuracy.

## What works today (F1 finalized)

- Attached-flow CD within ~10 % of wind-tunnel on NACA 0012 Re=3e6
  across α ∈ [0°, 12°].
- Transition locations match published XFoil references within
  0.02·c on NACA 0012 across Re ∈ [10⁶, 10⁷] and α ∈ [0°, 4°].
- Deep-stall native convergence ≥ 80 % on the 30-case NACA
  0012/4412 × M ∈ {0, 0.15, 0.3} showcase matrix.
- Drag decomposition (CDF / CDP) with conservation `CDF + CDP = CD`.
- Compressibility propagates via `ComputeHk(H, M)` and
  Prandtl-Glauert on the inviscid side; verified at M ≤ 0.3.

## Known limitations

- **CL has no viscous feedback.** Lift comes from the inviscid
  (linear-vortex) path; cambered-airfoil CL is overpredicted by
  5–15 %. Closing this requires Phase 5 (source-distribution
  coupling) — see `ThesisClosurePlan.md`.
- **6 NACA-4412 deep-stall cells don't converge** (α ≥ 16° across
  all three Mach numbers). Cause is fully-separated flow that the
  uncoupled marcher can't self-limit. Same Phase-5 fix.
- **Phase-5-lite geometric displacement** is present but has a
  sign issue on cambered airfoils (inflates CL instead of reducing
  it). Production users should keep
  `viscousCouplingIterations = 0`.
- **Compressibility untested at M > 0.3.**
- **No reference MSES parity** — MSES source is not publicly
  distributable; we validate against the text and against XFoil.

## When to use which solver

| Need | Solver |
|------|--------|
| Bit-exact Fortran XFoil parity | `XFoil.Solver` (Double tree) |
| Modern-tree viscous without parity concern | `XFoil.Solver.Modern` |
| MSES-class closure, stall-robust | `XFoil.ThesisClosureSolver` |
