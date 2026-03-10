# Managed-vs-Legacy Parity And TODOs

## Status summary

The managed codebase covers a large portion of deterministic XFoil workflows, but it is still not a fidelity-complete replacement for the original Fortran application.

## Subsystem parity map

### Core geometry and parsing

- Implemented
  - Practical text parsing, normalization, and NACA 4-digit generation.
- Missing or weaker than legacy
  - Full `aread.f` corner-case compatibility.
  - Broader legacy shell/session metadata handling.

### Inviscid solver

- Implemented
  - Managed panel mesh generation.
  - Hess-Smith-style inviscid solve.
  - Prepared-system reuse across alpha sweeps.
  - Wake marching and subsonic compressibility correction.
  - Target-`CL` and alpha sweep workflows.
- In progress -- linear-vorticity port
  - State models for node-based panel geometry (LinearVortexPanelState), solver workspace (InviscidSolverState), and results (LinearVortexInviscidResult) added.
  - ParametricSpline (port of SPLINE/SPLIND/SEGSPL) with 3 BC modes, segmented fitting, evaluation, derivative evaluation, arc-length, inversion.
  - TridiagonalSolver (port of TRISOL).
  - ScaledPivotLuSolver (port of LUDCMP/BAKSUB) with Crout's method and scaled partial pivoting.
  - CosineClusteringPanelDistributor (port of PANGEN) for curvature-adaptive cosine spacing with TE density control, producing node-based panel distributions matching XFoil's panel placement.
  - PanelGeometryBuilder (ports of NCALC, APCALC, TECALC, COMSET, ATANC) for normal vectors, panel angles, TE gap analysis, compressibility parameters, and continuous atan2.
  - StreamfunctionInfluenceCalculator (port of PSILIN) for linear-vorticity streamfunction influence computation with self-influence singularity handling and TE panel contribution.
  - LinearVortexInviscidSolver (ports of GGCALC, SPECAL, SPECCL, CLCALC, CPCALC) complete. System assembly, LU factoring, basis solutions, alpha/CL specification, pressure integration with Karman-Tsien correction and DG*DX/12 CM correction. Sharp TE bisector condition implemented.
  - InviscidSolverType enum enables selection between Hess-Smith and linear-vorticity solvers via AnalysisSettings.
  - AirfoilAnalysisService.AnalyzeInviscid dispatches to LinearVortexInviscidSolver when InviscidSolverType.LinearVortex is set. Default HessSmith behavior unchanged.
  - End-to-end aerodynamic correctness validated: CL within 5% of thin-airfoil theory, correct signs for cambered airfoils, CL linearity, panel independence.
- Missing or weaker than legacy
  - Exact Fortran bit-parity not yet verified (Phase 4 test bench).
  - No Mach-CL Newton coupling iteration for compressible cases (M>0 will be addressed in Phase 3).

### Viscous solver and coupling

- Implemented
  - Boundary-layer topology.
  - Seed state and initial estimates.
  - Laminar interval system and local Newton-style solves.
  - Transition/amplification surrogate.
  - Surrogate interaction and displaced-geometry coupling.
  - Managed viscous alpha and lift sweeps.
- Missing or weaker than legacy
  - No full Newton-coupled viscous/inviscid system like `xbl.f` + `xblsys.f`.
  - No true source/displacement coupling directly in the inviscid matrix.
  - Transition is not a faithful e^n implementation.
  - Current drag is a profile-drag estimate, not full legacy drag decomposition.

### IO and polar/session formats

- Implemented
  - Deterministic CSV export.
  - Legacy text polar import.
  - Legacy reference polar import.
  - Synthetic-validated binary polar-dump import.
  - Manifest-driven batch execution.
- Missing or weaker than legacy
  - The original interactive polar/session shell behavior is absent.
  - Binary dump support still needs broader historical validation.

### Geometry design (`xgdes`)

- Implemented
  - Main batch geometry transforms.
  - Flap, TE-gap, LE-radius, point-edit, corner-refinement, and contour-modification workflows.
- Missing or weaker than legacy
  - No cursor-driven interactive edit session.
  - Some workflows remain spline-approximated rather than exact ports of all legacy internals.

### Inverse design (`xqdes`, `xmdes`)

- Implemented
  - `Qspec` extraction and editing.
  - Symmetry, `AQ`, `CQ`, smoothing, surrogate inverse execution.
  - Modal spectrum and perturbation workflows.
  - Direct managed `MAPGEN` port with TE-gap, TE-angle, and Hanning filter support.
- Missing or weaker than legacy
  - `QDES EXEC` and `MDES EXEC` remain surrogates outside the direct conformal path.
  - Full interactive inverse-design shell state is absent.

### CLI/UI/plotting

- Implemented
  - Rich batch CLI command set.
- Missing or weaker than legacy
  - No managed replacement for original plotting stack.
  - No interactive shell, plotting windows, or retained session state comparable to `xfoil.f`.

### ORRS and advanced features

- Implemented
  - None visible in managed code.
- Missing
  - `orrs/` functionality and any managed replacement of stability-map workflows.

## Priority TODOs

- TODO: (DONE) Linear-vorticity inviscid kernel ported, aerodynamically validated, and selectable through AirfoilAnalysisService. Remaining: exact Fortran bit-parity via Phase 4 test bench, compressible Mach-CL coupling iteration, sweep method dispatch.
- TODO: Replace surrogate viscous/displacement coupling with a true coupled outer-flow/BL system.
- TODO: Replace the current transition surrogate with a closer e^n-style formulation.
- TODO: Validate binary polar-dump import against real legacy dump artifacts, not only synthetic fixtures.
- TODO: Tighten `QDES EXEC` and `MDES EXEC` toward legacy behavior instead of normal-displacement surrogates.
- TODO: Decide whether to build a managed plotting abstraction replacing `plotlib`.
- TODO: Decide whether to port the interactive command shell or keep the project intentionally batch/headless.
- TODO: Port or explicitly defer ORRS workflows with a documented rationale.
