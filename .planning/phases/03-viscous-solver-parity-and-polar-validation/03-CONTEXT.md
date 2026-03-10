# Phase 3: Viscous Solver Parity and Polar Validation - Context

**Gathered:** 2026-03-11
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the surrogate viscous coupling, transition model, and drag decomposition with precise implementations. Validate all three polar sweep types (alpha, CL, Re). The Newton-coupled viscous/inviscid system, full e^N transition model, and wake momentum drag decomposition must produce CL, CD, CM matching original XFoil within 0.001% (with XFoil-compatible mode enabled).

</domain>

<decisions>
## Implementation Decisions

### Viscous Coupling Strategy
- Full simultaneous Newton coupling (Drela & Giles, 1987) — the gold standard for IBL methods
- Replace the surrogate ViscousInteractionCoupler/ViscousLaminarSolver/EdgeVelocityFeedbackBuilder in-place (no selection enum — surrogate has no accuracy advantage)
- 3-variable BL system per station: Ctau/amplitude, momentum thickness (θ), mass defect (δ*×Ue)
- DIJ influence matrix (dUe/dσ) bridges BL displacement to inviscid edge velocity perturbation
- **Analytical DIJ** derived from LinearVortexInviscidSolver's influence coefficient arrays (primary, fast)
- **Numerical finite-difference DIJ** as validation/debugging tool and HessSmith adapter (perturb σ, re-solve, measure ΔUe/Δσ)
- Both inviscid solvers (LinearVortex and HessSmith) supported for viscous analysis: LinearVortex uses analytical DIJ, HessSmith uses finite-difference DIJ adapter

### Transition Model
- Full e^N envelope method: port DAMPL (Hk < 3.5) + DAMPL2 blend (Hk > 3.5 for separating profiles)
- Falkner-Skan critical Reynolds number correlation, smooth onset ramp (DGR=0.08)
- Full Jacobian sensitivities (AX_HK, AX_TH, AX_RT) feed into global Newton system
- Newton iteration (up to 30 iters) for exact transition location within interval
- **Modern database corrections** layered on top of XFoil correlations — ON by default, toggleable OFF for Phase 4 verification
- N_crit configurable per-analysis (default 9.0) and optionally per-surface (upper/lower independently)
- Forced transition (user-prescribed x/c location) supported per-surface, falls back to free transition if forced location is downstream of natural
- **Full BL profile output**: N(x), θ(x), δ*(x), Hk(x), cf(x), Reθ(x) at each station in result model
- Transition location, type (free/forced), and convergence status included in results

### Drag Decomposition
- **Primary: Wake momentum deficit** (Squire-Young formula) for total CD, extrapolated to infinity
- **CDF**: Skin friction coefficient from BL solution (not flat-plate correlations), integrated along both surfaces
- **CDP**: By subtraction (CD - CDF)
- **Cross-check**: Independent surface integration (friction + pressure) with discrepancy metric
- **TE base drag model** included in surface cross-check (~0.5×(TE_gap/chord)×ΔCp_base)
- **Full compressibility transform** (Lees-Dorodnitsyn) applied to all BL quantities, not just Kármán-Tsien on wake Ue
- **Extended wake** with convergence check (march until dθ/dξ < ε) as default; XFoil-length wake as option for verification
- **Basic Lock-type wave drag** estimate for transonic cases (M > 0.7) — extends useful Mach range beyond XFoil
- **Post-stall extrapolation** (Viterna-Corrigan style) available as optional mode with explicit flag; default reports only converged results
- Result model exposes: CD, CDF, CDP, CD_surface_crosscheck, discrepancy_metric

### Convergence Behavior
- **Trust-region Newton** as default solver (Levenberg-Marquardt style): max 50 iterations, tolerance 1e-6
- **XFoil RLXBL** (adaptive under-relaxation with DHI=1.5, DLO=-0.5 thresholds) as option for Phase 4 verification: max 20 iterations, tolerance 1e-4
- Stagnation point relocates per Newton iteration (port STMOVE)
- **BL initialization**: March with inviscid Ue (upgrade ViscousStateEstimator); flat-plate Blasius fallback if marching fails
- **Warm-start in polar sweeps**: Previous converged BL as initial guess. If non-convergence, try other nearby converged points as seeds before cold-start
- **Three linear solver implementations**: custom block-tridiagonal (default, port of BLSOLV), generic sparse LU, band matrix solver
- **Non-convergence handling**: Record point with convergence flag (converged=false, iterations, final_residual). Polar sweep continues to next point
- **Divergence recovery**: Auto-reduce trust-region radius by 50%, retry. After 3 consecutive shrinks → declare non-convergence
- **Full iteration history** recorded: per-iteration RMS residual, max residual location, relaxation factor, trust-region radius, CL/CD/CM
- Variable limiting: Ctau ≤ 0.25, no negative Ue islands, Hk ≥ 1.02 (laminar), Hk ≥ 1.00005 (wake)

### Claude's Discretion
- Block-tridiagonal VACCEL acceleration parameter tuning
- Trust-region initial radius and adaptation rate
- Modern transition database correction specifics (Arnal-Casalis-Habiballah vs Drela 2003 formulation)
- Viterna-Corrigan post-stall model parameter calibration
- Lock wave drag critical Mach estimation method
- Internal BL station density for marching initialization

</decisions>

<specifics>
## Specific Ideas

- **Dual-mode pattern**: Every major subsystem has a modern best-practice default AND an XFoil-compatible mode for Phase 4 verification. Toggle via AnalysisSettings
- User explicitly wants "the most precise aerodynamic results, even if it means no parity with XFoil in some cases" — modern enhancements are the production path, XFoil-compatible mode is for testing only
- Multi-seed warm-start strategy in sweeps: try previous point → try nearby converged points → cold-start. Adds time but increases convergence probability significantly
- Three linear solver options (block-tridiagonal, sparse, band) all available — block-tridiagonal default because it exploits BL problem structure

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `LinearVortexInviscidSolver`: Influence coefficient arrays can derive analytical DIJ matrix. System assembly and LU factoring already implemented
- `ScaledPivotLuSolver`: Generic LU solver from Phase 2 — can serve as the sparse solver option
- `StreamfunctionInfluenceCalculator`: Source/vortex influence computation — DIJ extraction extends this
- `BoundaryLayerTopologyBuilder`: Stagnation point detection and BL station distribution — needs STMOVE extension
- `ViscousStateSeedBuilder`: TE gap computation and wake closure — reusable for Newton system
- `WakeGeometryGenerator`: Wake panel geometry — needs extension for variable wake length
- `PanelGeometryBuilder.ComputeCompressibilityParameters()`: Mach/compressibility already available

### Established Patterns
- Static utility classes with raw arrays for numerics (Phase 2 convention)
- Readonly record structs for zero-allocation parameter passing
- InviscidSolverType enum + AnalysisSettings dispatch — same pattern extends to viscous solver modes
- Test-driven development with aerodynamic correctness assertions

### Integration Points
- `AirfoilAnalysisService`: Entry point for analysis — viscous path wires here
- `AnalysisSettings`: Receives new properties (N_crit, forced transition x/c, solver mode toggles, trust-region parameters)
- `LinearVortexInviscidResult`/`InviscidAnalysisResult`: Feed into viscous solver as inviscid baseline
- `PolarPoint`/`ViscousPolarPoint`: Receive extended drag decomposition and BL profile data

</code_context>

<deferred>
## Deferred Ideas

- Full transonic panel method (supersonic extensions) — future phase if needed
- ORRS stability integration for transition — per PROJECT.md out of scope
- Interactive BL profile plotting — no plotting abstraction per PROJECT.md

</deferred>

---

*Phase: 03-viscous-solver-parity-and-polar-validation*
*Context gathered: 2026-03-11*
