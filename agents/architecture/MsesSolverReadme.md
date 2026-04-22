# XFoil.MsesSolver — User Guide

The `XFoil.MsesSolver` assembly is a clean-room C# port of the MSES-class
2nd-order boundary-layer closure from Drela's 1986 MIT thesis. It
coexists with the parity-validated `XFoil.Solver` assembly (Fortran-
bit-exact XFoil 6.97 path); both implement `IAirfoilAnalysisService`
so callers pick at construction time.

See `agents/architecture/MsesClosurePlan.md` for the full phase plan
and `agents/architecture/MsesValidation.md` for the validation
snapshot.

## Quickstart

```csharp
// Default: fully-thesis-exact (implicit-Newton laminar + implicit-
// Newton turbulent + wake marcher with Squire-Young far-field CD).
IAirfoilAnalysisService svc = new MsesAnalysisService();
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
| `--thesis-exact`, `--wake`, `--thesis-laminar` | No-ops under the current defaults; retained so existing scripts don't break |

Env-var equivalents (`XFOIL_MSES_THESIS_EXACT`, `XFOIL_MSES_WAKE`,
`XFOIL_MSES_THESIS_LAMINAR`) are deprecated and scheduled for
removal.

## Ctor knobs

```csharp
new MsesAnalysisService(
    inviscidProvider: null,                      // null → Modern XFoil inviscid
    viscousCouplingIterations: 0,                // Phase-5-lite, experimental
    viscousCouplingRelaxation: 0.3,
    useThesisExactTurbulent: true,               // default
    useWakeMarcher: true,                        // default
    useThesisExactLaminar: true);                // default
```

Pass `false` on the three marcher flags to revert to the pre-F1
baseline for A/B studies.

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
  coupling) — see `MsesClosurePlan.md`.
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
| MSES-class closure, stall-robust | `XFoil.MsesSolver` |
