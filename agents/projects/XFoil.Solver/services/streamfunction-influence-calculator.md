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

## Dependencies

- `LinearVortexPanelState` -- reads node coordinates, normals, panel angles, arc length
- `InviscidSolverState` -- reads vortex/source strengths, TE geometry; writes sensitivity arrays

## Fortran Origin

Port of PSILIN from xpanel.f lines 87-476. The GEOLIN=false path is implemented (no geometric sensitivities for direct analysis). The LIMAGE ground-effect path is not implemented.

## TODO

- Verify influence coefficients row-by-row against Fortran AIJ matrix for a reference airfoil.
- Add GEOLIN=true path if inverse design is needed.
