# StreamfunctionInfluenceCalculator

- File: `src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs`
- Type: static utility class
- Created: Phase 2 Plan 2

## Purpose

Computes streamfunction influence coefficients for the linear-vorticity panel method. This is the computational heart of the formulation -- a direct port of XFoil's PSILIN algorithm. For each field point, it computes PSI and all sensitivity arrays due to all panels, the TE panel, and the freestream.

## Public API

- `ComputeInfluenceAt(fieldNodeIndex, fieldX, fieldY, fieldNormalX, fieldNormalY, computeGeometricSensitivities, includeSourceTerms, panel, state, freestreamSpeed, angleOfAttackRadians)` -- Returns `(psi, psiNormalDerivative)` and populates workspace arrays in state.

## Workspace Arrays Written

| Array | XFoil Name | Meaning |
|-------|-----------|---------|
| StreamfunctionVortexSensitivity | DZDG | dPSI/dGamma_j |
| StreamfunctionSourceSensitivity | DZDM | dPSI/dSigma_j |
| VelocityVortexSensitivity | DQDG | dQ_tangential/dGamma_j |
| VelocitySourceSensitivity | DQDM | dQ_tangential/dSigma_j |

## Algorithm Structure

1. Initialize: zero all sensitivity arrays, compute TE bisector ratios (SCS, SDS).
2. Main panel loop (for each panel 0 to N-2):
   - Transform field point to panel-local coordinates (x1, x2, yy)
   - Compute log(r^2) and atan2 terms with self-influence protection
   - Source contribution: two half-panel quadratic integrals (if includeSourceTerms)
   - Vortex contribution: linear vorticity sum/difference integrals
3. TE panel: source-like and vortex-like contributions from gap geometry
4. Freestream: PSI += qinf*(cos(a)*y - sin(a)*x)

## Numerical Care

- Self-influence: when field point IS a panel endpoint, log and atan terms are set to known limiting values (0) instead of computing log(0) or atan2(0,0).
- SGN reflection flag: for airfoil surface points SGN=1; for off-body points SGN=sign(YY) to prevent atan2 branch-cut issues.
- Degenerate panels (zero length) are skipped entirely.
- Legacy single-precision parity mode is not uniformly "more FMA". The traced PSILIN source branch currently needs a mix of explicit fused multiply-adds and explicitly staged non-fused product pairs to match the Fortran build on .NET/arm64.
- Fresh reduced-panel alpha-10 `fieldIndex=47` traces proved the source-midpoint radius-square `rs0` does not share the same staging as the endpoint `rs1` / `rs2` squares. The current parity replay is:
  - `rs1` / `rs2`: contracted square-sums
  - `rs0`: single-rounding `X0*X0 + YY*YY` replay (`FMA(X0, X0, YY*YY)`)
- The current PSILIN frontier is now driven by a standalone raw-bit micro-driver:
  - `tools/fortran-debug/psilin_parity_driver.f`
  - `tests/XFoil.Core.Tests/FortranParity/FortranPsilinDriver.cs`
  - `tests/XFoil.Core.Tests/FortranParity/StreamfunctionMicroFortranParityTests.cs`
- That driver now compares raw IEEE-754 words for panel setup, source half terms, source `DZ` and `DQ` derivative terms, source segment outputs, vortex segment outputs, TE correction terms, accumulation checkpoints, result terms, and the final `(psi, psiNi, DZDG/DZDM/DQDG/DQDM)` sample vector.
- The curated PSILIN micro-batch is now green, and the reduced-panel alpha-10 `BIJ row 47` full-run producer window is now closed too. The proved replay family includes:
  - mixed radius-square staging (`rs1` / `rs2` on contracted square-sums, `rs0` on the single-rounding midpoint replay),
  - source half-1 and half-2 `PDX` numerators,
  - source half-1 and half-2 `PDYY`, `PSNI`, and `PDNI` sums,
  - and vortex `PDX`, `PDYY`, `PSNI`, and `PDNI` sums.
- The 12-panel alpha-0 artifact-backed PSILIN tests now use the focused `n0012_re1e6_a0_p12_n9_psilin` case instead of the older `n0012_re1e6_a0_p12_n9_full` broad trace. Fresh reference window `psilin_source_dq_terms` matches bitwise there; the older block-test failure was a stale-oracle issue, not a new kernel regression.
- If a PSILIN block test fails again, refresh the focused case first. Do not treat the old broad `..._full` trace as authoritative for this service unless a fresh focused reference proves it is missing needed events.

## Dependencies

- `LinearVortexPanelState` -- reads node coordinates, normals, panel angles, arc length
- `InviscidSolverState` -- reads vortex/source strengths, TE geometry; writes sensitivity arrays

## Fortran Origin

Port of PSILIN from xpanel.f lines 87-476. The GEOLIN=false path is implemented (no geometric sensitivities for direct analysis). The LIMAGE ground-effect path is not implemented.

## TODO

- Verify influence coefficients row-by-row against broader Fortran `AIJ` / wake-coupling benches now that both the standalone PSILIN kernel and the reduced-panel alpha-10 `BIJ row 47` producer window are green.
- Reuse the standalone PSILIN oracle before any future `AIJ` or inviscid-kernel patch; do not reopen broad inviscid traces for this block first.
- Add GEOLIN=true path if inverse design is needed.
