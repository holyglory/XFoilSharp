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
  - PanelGeometryBuilder (ports of NCALC, APCALC, TECALC, COMSET, ATANC) for normal vectors, panel angles, TE gap analysis, compressibility parameters, and continuous atan2.
  - StreamfunctionInfluenceCalculator (port of PSILIN) for linear-vorticity streamfunction influence computation with self-influence singularity handling and TE panel contribution.
- Missing or weaker than legacy
  - GGCALC system assembly and solution (influence matrix factoring, basis solutions) pending.
  - CLCALC lift/moment recovery with Karman-Tsien correction pending.
  - No proof of kernel-level parity with original XFoil coefficients (awaiting end-to-end integration).

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

- TODO: Port a closer equivalent of the original inviscid kernel and coefficient recovery path from `xpanel.f`, `xsolve.f`, and related routines.
- TODO: Replace surrogate viscous/displacement coupling with a true coupled outer-flow/BL system.
- TODO: Replace the current transition surrogate with a closer e^n-style formulation.
- TODO: Validate binary polar-dump import against real legacy dump artifacts, not only synthetic fixtures.
- TODO: Tighten `QDES EXEC` and `MDES EXEC` toward legacy behavior instead of normal-displacement surrogates.
- TODO: Decide whether to build a managed plotting abstraction replacing `plotlib`.
- TODO: Decide whether to port the interactive command shell or keep the project intentionally batch/headless.
- TODO: Port or explicitly defer ORRS workflows with a documented rationale.
