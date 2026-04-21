# External reference data

Reference points used to validate Modern (#3) improvements. Three sources:

## `windtunnel.json` — published wind-tunnel measurements

Hand-curated from authoritative sources (NACA TR, NASA TM, UIUC). Each row carries:

- `airfoil` — airfoil name (used to map to a generator or `.dat` file).
- `alpha_deg`, `Re`, `Mach` — operating point.
- `CL`, `CD`, `CM_c4` — measured aerodynamic coefficients.
- `uncertainty` — per-row absolute uncertainty bands (or fall back to `uncertainty_default` at the file root).
- `source`, `citation` — provenance string + full citation.

### Schema v2 optional fields (2026-04-20)

Added for Phase 3 Tier-B validation (`agents/architecture/Phase3TierBMetrics.md`):

- `Xtr_U`, `Xtr_L` — measured transition x/c on upper / lower surface. For laminar
  separation bubbles this is typically the turbulent-reattachment point read from
  oil-flow visualization. Used by B2 (DAMPL2 + regime gating). Absent = not measured.
- `cp_distribution` — array of surface pressure records at chord stations, schema
  `{ "x_over_c": <float>, "Cp_upper": <float>, "Cp_lower": <float> }`. Used by B4
  (Karman-Tsien transonic). Rows with this field carry scalar `CL`/`CD`/`CM_c4`
  from the same run plus a Cp table at a handful of chord stations (0.025, 0.05,
  0.10, 0.20, 0.30, 0.50, 0.70, 0.90 for the initial NACA 0012 transonic rows).
- `uncertainty.Xtr_abs` / `uncertainty.Cp_abs` — per-row uncertainty bands for
  the new fields. Defaults at `uncertainty_default` are Xtr_abs = 0.05 c and
  Cp_abs = 0.05 (NACA LTPT-tunnel-class measurements read off curves).

### Known `source` keys

- `naca_tr824` — Abbott, von Doenhoff, Stivers (1945), NACA TR 824, "Summary of Airfoil Data". Primary NACA-series reference.
- `naca_tr1832` — historical alias retained for the McCroskey NACA 0012 compilation (NASA TM-100019).
- `selig_lowre` — Selig, Donovan, Fraser (1989), SoarTech 8 / UIUC LSAT.
- `selig_vol1` — Selig et al. (1995), "Summary of Low Speed Airfoil Data", Vol 1, UIUC LSAT.
- `selig_mcgranahan_2004` — Selig, McGranahan (2004), NREL/SR-500-34515 wind-turbine airfoil tests.
- `selig_s1223` — Selig, Guglielmo (1997), J. Aircraft 34(1), S1223 high-lift low-Re tests.
- `ladson_tm4074` — Ladson (1988), NASA TM 4074, NACA 0012 Mach/Re variation; LTPT data with transition tripped. Primary source for B3 post-stall NACA 0012 rows (alpha 10-17°). **2026-04-20 expansion**: ingested full α-sweep (α=-4…+19°) at three trip states (80/120/180 grit) from `github.com/TMBWG/turbmodels/NACA0012_validation/CLCD_Ladson_expdata.dat`. `trip` field records which grit; use to filter or compare tripping regimes. Note: alpha_deg is the as-reported precision from the TMBWG file (2-dp), not rounded.
- `ladson_tm100526` — Ladson, Hill, Johnson (1987), NASA TM 100526, NACA 0012 transonic pressure distributions. **2026-04-20 addition**: ingested from `github.com/TMBWG/turbmodels/NACA0012_validation/CP_Ladson.dat` (M=0.3 at α=0°, 10°, 15° × three transition states). These rows carry `cp_distribution` (interpolated to 8 chord stations: 0.025-0.90 c) but no scalar `CL`/`CD` — use `--b4-score` for them, not `--reference-sweep`. Schema v2 nulls (`CL: null`, `CD: null`) signal "Cp-only row, skip scalar scoring".
- `mcghee_tm4062` — McGhee, Walker, Millard (1988), NASA TM 4062, E387 Langley LTPT tests. Primary source for B2 LSB transition-location rows.
- `harris_tm81927` — Harris (1981), NASA TM 81927, NACA 0012 Langley 8-ft transonic pressure tunnel. Primary source for B4 Cp distributions at M ≥ 0.30. The report publishes only graphical Cp plots (no tabular appendix), so `cp_distribution` rows tagged with this source are hand-digitized from the figures — expect ±0.05-0.07 on each Cp sample.

Wind-tunnel uncertainty is non-negligible — e.g. NACA Langley two-dimensional tunnel CD repeatability is roughly ±0.0005-0.001 at low CL, larger near stall. Use the `uncertainty` field, not just exact-match comparison, when scoring Modern's deltas.

To add data: append rows to the `rows` array. New `source` keys are fine; document them here.

## `openfoam_fine/` — parametric CFD reference (Phase 3 Tier B)

New pipeline (2026-04-20) built for Phase 3 Tier B validation. Supersedes the legacy `openfoam/` tutorial-mesh sweep as the long-term path.

Architecture (`tools/external-references/openfoam_fine/`):
1. `make_case.py` + `tools/airfoil_geometry.py` — any NACA 4-digit designation or Selig `.dat` path → thin-extruded binary STL → `blockMesh` 160×100×2 rectangular far-field at 50c → `snappyHexMesh` with surface refinement levels 6-7 (~160k cells) + wake refinement box.
2. `simpleFoam` + k-ω SST for subsonic (bounded upwind div, `nutUSpaldingWallFunction`); `rhoCentralFoam` + k-ω SST for M ≥ 0.3. `potentialFoam` primes incompressible U.
3. `run_sweep.sh` parallelises via GNU parallel (`PARALLEL=48` default on the 192-core host).
4. `aggregate.py` walks `cases/` and emits `results/openfoam_fine_results.json` (schema v2, same row shape as `windtunnel.json` plus `solver`, `cells`, `y_plus_max`, `converged`, `iterations`).

**Smoke-test state (2026-04-20):**
- NACA 0012 α=4° Re=1e6 M=0 → CL=0.376, CD=0.0424 (XFoil CL ~0.44, CD ~0.008).
- NACA 4412 α=4° Re=1e6 M=0 → CL=0.863, CD=0.0545 (XFoil CL ~0.85, CD ~0.010).
- NACA 0012 α=0° Re=1e6 M=0.3 → rhoCentralFoam only reached t=7.2e-4s (~0.07 chord transits) before cutoff; marked `converged=false`.

**Known limitations (smoke-test):**
- **CD over-predicted ~5×** because boundary-layer prism insertion is disabled (`LAYER_COUNT=0`). snappyHexMesh's medial-axis layer extruder on the 2-cell-z slab produces thousands of concave/underdetermined cells that make the pressure matrix singular. y+ therefore sits at 10-600 (wall-function regime), not the originally-targeted <1. Fix path: rework the domain as a wedge with `empty` front/back patches so layer extrusion works — ~1 day of infrastructure work.
- **Compressible steady state slow to reach.** `rhoCentralFoam` is density-based explicit. Recommend swapping to `rhoSimpleFoam` (pressure-based SIMPLE compressible) for M ≥ 0.3 cases before kicking off the full sweep.

Full 1000-case sweep is **not yet run** pending the wedge-domain fix. Cost estimate at current pipeline: ~15 min/case × 1000 / 48 parallel ≈ 5 hrs wall-clock on the host, with CD still over-predicted.

## `openfoam/` — legacy CFD reference cases

Generated by `tools/external-references/openfoam/run_sweep.sh`. The script:
1. instantiates one case per `(alpha, Re)` from the airFoil2D template via `make_case.py`,
2. runs `simpleFoam` in `opencfd/openfoam-default:2312` Docker per case,
3. extracts the final `Cl`/`Cd`/`CmPitch` from the forceCoeffs log,
4. aggregates everything into `openfoam_results.json` (same row schema as `windtunnel.json`).

Solver: `simpleFoam` (steady incompressible) with k-ω SST turbulence model.
Mesh: pre-built ~22k-cell C-grid from the `airFoil2D` tutorial, scaled to chord=1.0 via `transformPoints -scale 1/35` at template-build time.
Sweep grid: 50 alphas × 20 Re = 1000 cases (alpha −8°…+20°, Re 1e5…1e7 geometric).

**Caveat — the airfoil isn't NACA 0012**: inspecting the wall point cloud, the tutorial mesh has asymmetric y bounds (max y ≈ 0.106 chord above, min y ≈ −0.075 below) — i.e. it's a cambered ~18% airfoil, not the symmetric NACA 0012 the tutorial name suggests. Treat the OpenFOAM results as **relative-trend** data (sensitivity of CL/CD to alpha and Re for *some* cambered airfoil), not absolute reference values for any specific NACA shape, until a parametric mesher (gmsh- or blockMesh-based) is in place.

**Caveat — coarse mesh + upwind schemes**: schemes are deliberately first-order upwind on (k, ω) for robustness. CD is over-predicted by 2-5× vs published values. Useful for trend agreement but not absolute matches.

## How the harness uses these

`tools/fortran-debug/ParallelPolarCompare --reference-sweep <manifest.json>` loads the manifest, runs each row through Float (#1), Doubled (#2), and Modern (#3) facades, and reports per-airfoil RMS error vs reference. Modern improvements are scored by whether they reduce that RMS error without breaking the #1 Fortran-bit-exact gate.
