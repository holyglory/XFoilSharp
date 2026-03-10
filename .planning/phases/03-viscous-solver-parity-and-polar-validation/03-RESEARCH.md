# Phase 3: Viscous Solver Parity and Polar Validation - Research

**Researched:** 2026-03-11
**Domain:** Computational aerodynamics -- viscous/inviscid coupling, boundary layer methods, transition modeling, drag decomposition
**Confidence:** HIGH

## Summary

Phase 3 replaces the existing surrogate viscous solver infrastructure with a faithful port of XFoil's Newton-coupled viscous/inviscid system. The current C# codebase has a functioning but approximate viscous pipeline (ViscousLaminarSolver, ViscousInteractionCoupler, LaminarAmplificationModel, ViscousForceEstimator, EdgeVelocityFeedbackBuilder) that uses local Newton solves per interval, displacement geometry feedback, and profile-drag-estimate-only drag computation. All of these must be replaced by the global simultaneous Newton system from xbl.f + xblsys.f, which solves for all BL variables (Ctau, theta, mass defect) and edge velocity corrections in a single coupled system.

The Fortran reference consists of approximately 4,100 lines in xbl.f (1,581 lines -- SETBL, IBLSYS, UPDATE, etc.) and xblsys.f (2,522 lines -- TRCHEK2, BLSYS, BLVAR, BLDIF, TRDIF, DAMPL, DAMPL2, and all BL correlations), plus supporting routines in xpanel.f (QDCALC for DIJ matrix, STMOVE, STFIND, XICALC, IBLPAN, UICALC, QVFUE, GAMQV) and xsolve.f (BLSOLV -- the block-tridiagonal solver). The viscous iteration loop in VISCAL (xoper.f:2583-2729) orchestrates the outer Newton iteration: SETBL builds the system, BLSOLV solves it, UPDATE applies the Newton step with relaxation (RLXBL), and STMOVE relocates the stagnation point per iteration.

**Primary recommendation:** Port the Fortran subroutines in dependency order: (1) BL correlations from xblsys.f, (2) DAMPL/DAMPL2 transition amplification, (3) BLSYS/BLVAR/BLMID/BLDIF/TRDIF system assembly, (4) QDCALC DIJ influence matrix, (5) BLSOLV block-tridiagonal solver, (6) SETBL global Newton assembly, (7) UPDATE with RLXBL relaxation, (8) VISCAL outer iteration loop, (9) CDCALC drag decomposition, (10) polar sweep modes. Each subsystem gets unit-tested against known Fortran outputs before integration.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Full simultaneous Newton coupling (Drela & Giles, 1987) -- the gold standard for IBL methods
- Replace the surrogate ViscousInteractionCoupler/ViscousLaminarSolver/EdgeVelocityFeedbackBuilder in-place (no selection enum -- surrogate has no accuracy advantage)
- 3-variable BL system per station: Ctau/amplitude, momentum thickness (theta), mass defect (delta*xUe)
- DIJ influence matrix (dUe/dsigma) bridges BL displacement to inviscid edge velocity perturbation
- Analytical DIJ derived from LinearVortexInviscidSolver's influence coefficient arrays (primary, fast)
- Numerical finite-difference DIJ as validation/debugging tool and HessSmith adapter
- Both inviscid solvers (LinearVortex and HessSmith) supported for viscous analysis
- Full e^N envelope method: port DAMPL (Hk < 3.5) + DAMPL2 blend (Hk > 3.5)
- Falkner-Skan critical Reynolds number correlation, smooth onset ramp (DGR=0.08)
- Full Jacobian sensitivities (AX_HK, AX_TH, AX_RT) feed into global Newton system
- Newton iteration (up to 30 iters) for exact transition location within interval
- Modern database corrections layered on top of XFoil correlations -- ON by default, toggleable OFF for Phase 4 verification
- N_crit configurable per-analysis (default 9.0) and optionally per-surface
- Forced transition (user-prescribed x/c location) supported per-surface
- Full BL profile output: N(x), theta(x), delta*(x), Hk(x), cf(x), Re_theta(x)
- Primary drag: Wake momentum deficit (Squire-Young formula) for total CD
- CDF: Skin friction from BL solution integrated along both surfaces
- CDP: By subtraction (CD - CDF)
- Cross-check: Independent surface integration with discrepancy metric
- TE base drag model included
- Full compressibility transform (Lees-Dorodnitsyn)
- Extended wake with convergence check as default; XFoil-length wake as option
- Basic Lock-type wave drag estimate for transonic cases (M > 0.7)
- Post-stall extrapolation (Viterna-Corrigan style) available as optional mode
- Trust-region Newton as default solver (Levenberg-Marquardt style): max 50 iterations, tolerance 1e-6
- XFoil RLXBL (adaptive under-relaxation with DHI=1.5, DLO=-0.5 thresholds) as option for Phase 4 verification
- Stagnation point relocates per Newton iteration (port STMOVE)
- BL initialization: March with inviscid Ue (upgrade ViscousStateEstimator); flat-plate Blasius fallback
- Warm-start in polar sweeps: Previous converged BL as initial guess
- Three linear solver implementations: custom block-tridiagonal (default), generic sparse LU, band matrix solver
- Non-convergence handling: Record point with convergence flag, polar sweep continues
- Divergence recovery: Auto-reduce trust-region radius by 50%, retry, 3 shrinks -> non-convergence
- Full iteration history recorded
- Variable limiting: Ctau <= 0.25, no negative Ue islands, Hk >= 1.02 (laminar), Hk >= 1.00005 (wake)
- Dual-mode pattern: Every major subsystem has a modern default AND an XFoil-compatible mode

### Claude's Discretion
- Block-tridiagonal VACCEL acceleration parameter tuning
- Trust-region initial radius and adaptation rate
- Modern transition database correction specifics (Arnal-Casalis-Habiballah vs Drela 2003 formulation)
- Viterna-Corrigan post-stall model parameter calibration
- Lock wave drag critical Mach estimation method
- Internal BL station density for marching initialization

### Deferred Ideas (OUT OF SCOPE)
- Full transonic panel method (supersonic extensions) -- future phase if needed
- ORRS stability integration for transition -- per PROJECT.md out of scope
- Interactive BL profile plotting -- no plotting abstraction per PROJECT.md
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| VISC-01 | Port full Newton-coupled viscous/inviscid system from xbl.f + xblsys.f | Core of this phase: SETBL, BLSYS, BLVAR, BLDIF, TRDIF, BLSOLV, UPDATE, VISCAL loop. Architecture patterns section details the Fortran structure and C# mapping. |
| VISC-02 | Replace surrogate displacement coupling with true source/displacement in inviscid matrix | QDCALC port creates the DIJ influence matrix. Analytical DIJ from LinearVortexInviscidSolver's BIJ/AIJ arrays; numerical DIJ as validation path. |
| VISC-03 | Port full e^n transition model replacing current laminar amplification surrogate | DAMPL + DAMPL2 + TRCHEK2 + AXSET. Full Jacobian sensitivities. Newton iteration for transition location. |
| VISC-04 | Port full drag decomposition (form, friction, pressure) from original | CDCALC (Squire-Young wake momentum deficit), CDF integration, CDP by subtraction, TE base drag. |
| VISC-05 | Viscous CL, CD, CM match original XFoil within 0.001% | End-to-end integration test with XFoil-compatible mode enabled. Requires all above plus STMOVE and converged outer Newton loop. |
| POL-01 | Alpha sweep (Type 1) produces results within 0.001% of original XFoil | SPECAL path in VISCAL with warm-start between alpha points. |
| POL-02 | CL sweep (Type 2) produces results within 0.001% of original XFoil | SPECCL path -- prescribe CL, solve for alpha inside viscous Newton loop. |
| POL-03 | Re sweep (Type 3, fixed CL varying Re) produces results within 0.001% of original XFoil | MRCL type 3 logic -- Re varies with CL via REINF1*CL^RETYP relation. |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 10 | net10.0 | Runtime target | Already established in Phase 1 |
| C# 10 | LangVersion 10.0 | Language features | LangVersion pinned per Phase 1 decision |
| XFoil.Solver | - | All viscous solver code lives here | Established project boundary |
| xUnit | latest | Test framework | Already used in tests/XFoil.Core.Tests |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| ScaledPivotLuSolver | (existing) | Generic sparse LU for alternate solver path | Non-default solver option for validation |
| TridiagonalSolver | (existing) | Thomas algorithm for tridiagonal systems | Sub-component within block-tridiagonal solver |
| ParametricSpline | (existing) | Spline interpolation for BL variable profiles | BL station interpolation and wake marching |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom block-tridiagonal | MathNet.Numerics sparse | Block-tridiagonal exploits BL problem structure (3x3 blocks + mass defect column); generic sparse would be 10-50x slower |
| Manual Jacobian assembly | Automatic differentiation | XFoil's hand-coded sensitivities are numerically exact and proven; AD would add dependency and hide the physics |

**Installation:**
No new packages needed. All work is within existing XFoil.Solver and XFoil.Core.Tests projects.

## Architecture Patterns

### Fortran-to-C# Mapping Strategy

The Fortran XFoil viscous solver uses global mutable state through COMMON blocks (XFOIL.INC, XBL.INC). The C# port must decompose this into explicit state containers while preserving the exact numerical operations.

**Key Fortran global state and C# equivalent:**

| Fortran (COMMON) | C# Equivalent | Description |
|-------------------|---------------|-------------|
| UEDG(IVX,2) | ViscousEdgeState arrays | Edge velocity at each BL station, 2 sides |
| THET(IVX,2), DSTR(IVX,2) | BoundaryLayerProfile arrays | theta, delta* at each station |
| CTAU(IVX,2) | BoundaryLayerProfile.Ctau | Shear stress coefficient |
| MASS(IVX,2) | BoundaryLayerProfile.MassDefect | m = delta* x Ue |
| VA(3,2,IVX), VB(3,2,IVX) | NewtonSystemBlocks | Block-tridiagonal system A,B blocks |
| VM(3,IZX,IVX) | NewtonSystemMassColumn | Mass defect influence column |
| VDEL(3,2,IVX) | NewtonSystemRHS | Right-hand side / solution deltas |
| DIJ(IQX,IQX) | InfluenceMatrix | dUe/dSigma (source influence on edge velocity) |
| ISYS(IVX,2) | BoundaryLayerSystemMap | BL station -> global system line mapping |

### Recommended Project Structure
```
src/XFoil.Solver/
├── Models/
│   ├── BoundaryLayerProfile.cs           # Per-station BL state (theta, delta*, Ctau, Hk, etc.)
│   ├── BoundaryLayerSystemState.cs       # Full BL state for both sides + wake
│   ├── ViscousNewtonSystem.cs            # Block-tridiagonal system (VA, VB, VM, VDEL arrays)
│   ├── ViscousConvergenceInfo.cs         # Per-iteration diagnostics (RMS, max residual, location)
│   ├── ViscousAnalysisResult.cs          # Full viscous result with BL profiles and drag decomposition
│   ├── DragDecomposition.cs              # CD, CDF, CDP, cross-check, discrepancy
│   ├── TransitionInfo.cs                 # Transition location, type, N value
│   ├── PolarSweepMode.cs                 # Enum: AlphaSweep, CLSweep, ReSweep
│   ├── ViscousSolverMode.cs              # Enum: TrustRegion (default), XFoilRelaxation
│   └── AnalysisSettings.cs               # Extended with N_crit, forced xtr, solver mode, etc.
├── Numerics/
│   ├── BlockTridiagonalSolver.cs         # Port of BLSOLV from xsolve.f
│   └── BandMatrixSolver.cs              # Alternative solver option
├── Services/
│   ├── BoundaryLayerCorrelations.cs      # Port of BLVAR, BLKIN, HKIN, HCT, HSL, HST, CFL, CFT, DIL, DILW
│   ├── TransitionModel.cs                # Port of DAMPL, DAMPL2, AXSET, TRCHEK2
│   ├── BoundaryLayerSystemAssembler.cs   # Port of BLSYS, BLDIF, TRDIF, BLMID, TESYS, BLPRV
│   ├── ViscousNewtonAssembler.cs         # Port of SETBL -- builds global Newton system
│   ├── ViscousNewtonUpdater.cs           # Port of UPDATE -- applies Newton step with relaxation
│   ├── InfluenceMatrixBuilder.cs         # Port of QDCALC -- builds DIJ matrix
│   ├── StagnationPointTracker.cs         # Port of STMOVE, STFIND
│   ├── EdgeVelocityCalculator.cs         # Port of UICALC, QVFUE, GAMQV, QISET, QWCALC
│   ├── DragCalculator.cs                 # Port of CDCALC + enhanced decomposition
│   ├── ViscousSolverEngine.cs            # Port of VISCAL -- outer Newton iteration loop
│   ├── PolarSweepRunner.cs              # Alpha/CL/Re sweep orchestration with warm-start
│   └── WakeGeometryGenerator.cs          # Extend existing for variable wake length
└── (existing files retained/modified)
```

### Pattern 1: Static Utility with Jacobian Sensitivities
**What:** Each BL correlation returns both value AND all partial derivatives
**When to use:** Every BL correlation function (cf, H*, dissipation, etc.)
**Example:**
```csharp
// Matches Fortran pattern: CALL CFL(HK2, RT2, M2, CF2, CF2_HK2, CF2_RT2, CF2_M2)
public static (double Cf, double Cf_Hk, double Cf_Rt, double Cf_Msq)
    LaminarSkinFriction(double hk, double rt, double msq)
{
    // Port of CFL from xblsys.f:2351
    // Returns value + all 3 Jacobian sensitivities
}
```

### Pattern 2: Mutable Workspace State
**What:** Pre-allocated arrays mutated in-place during Newton iteration
**When to use:** The Newton system arrays (VA, VB, VM, VDEL), BL station arrays, DIJ matrix
**Example:**
```csharp
// Matches established Phase 2 convention: static classes with raw arrays
public sealed class ViscousNewtonSystem
{
    // VA(3,2,IVX) -- diagonal blocks
    public double[,,] DiagonalBlocks { get; }  // [3, 2, maxStations]
    // VB(3,2,IVX) -- sub-diagonal blocks
    public double[,,] SubDiagonalBlocks { get; }
    // VM(3,IZX,IVX) -- mass defect influence column
    public double[,,] MassInfluenceColumn { get; }
    // VDEL(3,2,IVX) -- RHS / solution
    public double[,,] RightHandSide { get; }
}
```

### Pattern 3: Dual-Mode Toggle via AnalysisSettings
**What:** Every major subsystem has modern default + XFoil-compatible mode
**When to use:** Solver mode, transition model corrections, wake length, relaxation strategy
**Example:**
```csharp
public sealed class AnalysisSettings
{
    // Existing properties...
    public ViscousSolverMode ViscousSolverMode { get; } // TrustRegion (default) | XFoilRelaxation
    public double NCrit { get; }  // default 9.0
    public double? ForcedTransitionUpper { get; }  // x/c or null for free
    public double? ForcedTransitionLower { get; }
    public bool UseExtendedWake { get; }  // true = march until convergence; false = XFoil fixed length
    public bool UseModernTransitionCorrections { get; }  // true by default
    public int MaxViscousIterations { get; }  // 50 for trust-region, 20 for XFoil mode
}
```

### Anti-Patterns to Avoid
- **Re-inventing the Newton system structure:** The 3x3 block + mass defect column structure of BLSOLV is deeply coupled to the BL equation formulation. Don't try to reorganize it into a "cleaner" generic sparse system -- the block structure IS the performance.
- **Losing Jacobian chain rules:** Every BL correlation in XFoil carries explicit sensitivity derivatives (e.g., CF2_HK2, CF2_RT2). Dropping these and using finite differences would break Newton convergence. Port the exact Jacobian chains.
- **Separating laminar and turbulent into different classes:** In XFoil, BLVAR switches behavior via ITYP (1=laminar, 2=turbulent, 3=wake). A single class with mode dispatch is correct; separate class hierarchies would fragment the shared correlation infrastructure.
- **Attempting to make the surrogate displacement coupling coexist:** Per user decision, surrogates are replaced in-place with no selection enum.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| BL correlations (Cf, H*, dissipation) | Custom empirical formulas | Exact port of xblsys.f correlations (CFL, CFT, HSL, HST, DIL, DILW, HCT, HKIN) | These correlations have been validated for 35+ years; any deviation produces wrong drag |
| Transition prediction | Own instability model | Port DAMPL + DAMPL2 + TRCHEK2 | The envelope e^n method with Drela's correlations is the reference; no shortcut matches it |
| Block-tridiagonal solver | Generic sparse solver | Port BLSOLV from xsolve.f | Exploits the 3x3 block + mass column structure; generic sparse would be much slower and lose the VACCEL acceleration |
| Squire-Young wake drag | Integrate Cp around contour | Port CDCALC | Wake momentum deficit extrapolation to infinity is the standard XFoil approach; surface pressure integration alone underestimates CD |
| Stagnation point relocation | Fixed stagnation point | Port STMOVE from xpanel.f | The stagnation point moves during viscous iteration as the effective angle of attack changes; fixing it causes convergence failure |
| BL equation finite differences | Simple forward/backward differences | Port BLDIF + TRDIF + TESYS | The finite difference formulations in XFoil include careful Jacobian sensitivities for all 3 equations (momentum, shape parameter, amplification/Ctau) |

**Key insight:** XFoil's viscous solver is a single tightly-coupled system where every correlation function feeds Jacobian sensitivities into the Newton system. Replacing any single piece with a "simpler" approximation breaks convergence because the Newton Jacobian becomes inconsistent.

## Common Pitfalls

### Pitfall 1: Compressibility Transform Mismatch
**What goes wrong:** BL variables are computed in incompressible form but edge velocity is compressible; mixing them produces wrong Hk and Cf
**Why it happens:** XFoil uses Karman-Tsien compressibility on edge velocities and Lees-Dorodnitsyn transforms on BL quantities. The transforms are applied in BLPRV and reversed in BLKIN.
**How to avoid:** Port BLPRV exactly -- it converts incompressible Uei to compressible U2 using the TKBL/QINFBL parameters. Verify U2_UEI and U2_MS sensitivities match.
**Warning signs:** Cf values that are off by 10-30% at M > 0.3; Newton convergence failure at M > 0.5

### Pitfall 2: Transition Location Newton Convergence
**What goes wrong:** TRCHEK2's inner Newton loop (up to 30 iterations) fails to converge, producing wrong transition location
**Why it happens:** The implicit amplification equation N2 - N1 - AX*(X2-X1) = 0 must be solved for N2 with careful relaxation. The damping factor (DAEPS = 5e-5) and the safeguard against stepping across Ncrit are essential.
**How to avoid:** Port TRCHEK2 exactly including the C2SAV save/restore of station 2 variables, the AMPL2 clamping near AMCRIT, and the relaxation limits on DXT and DA2.
**Warning signs:** Transition jumping between stations, non-monotonic N(x) curves, sudden drag spikes

### Pitfall 3: DIJ Matrix Consistency with Inviscid Solver
**What goes wrong:** The DIJ matrix (source influence on edge velocity) is inconsistent with the inviscid solver, causing the viscous/inviscid coupling to diverge
**Why it happens:** QDCALC in Fortran uses the same factored streamfunction influence matrix (AIJ) to compute DIJ via back-substitution. If the C# DIJ doesn't use the same factored system, the coupling breaks.
**How to avoid:** For LinearVortex: extract DIJ analytically from the already-factored StreamfunctionInfluence matrix + SourceInfluence arrays in InviscidSolverState. For HessSmith: use the numerical finite-difference adapter.
**Warning signs:** Viscous CL diverging from inviscid CL, oscillating edge velocities, RMSBL not decreasing

### Pitfall 4: Block-Tridiagonal System Assembly Order
**What goes wrong:** The global Newton system is assembled in wrong order, producing singular or wrong system
**Why it happens:** XFoil's SETBL marches from stagnation to TE on side 1, then side 2, then wake. The ISYS mapping connects BL stations to global system lines. The VZ block couples the two surfaces at the TE.
**How to avoid:** Port IBLSYS (BL station -> system line mapping) exactly. Port SETBL's marching order: IS=1 (side 1) IBL=2..IBLTE(1), IS=2 (side 2) IBL=2..IBLTE(2), then wake IBL=IBLTE(2)+1..NBL(2). The first wake point uses TESYS for the TE-to-wake transition.
**Warning signs:** Zero pivots in BLSOLV, deltas that grow instead of shrink, RMSBL stuck at O(1)

### Pitfall 5: Under-Relaxation Limits (RLXBL)
**What goes wrong:** Newton updates blow up, producing negative theta, Hk < 1, or Ctau > 1
**Why it happens:** XFoil's UPDATE subroutine applies careful variable limiting: DHI=1.5 and DLO=-0.5 for the maximum allowed change in log(delta*), RLXBL is the minimum relaxation factor across all variables.
**How to avoid:** Port UPDATE exactly with the DHI/DLO thresholds. Apply variable bounds: Ctau <= 0.3 (clamped in UPDATE), theta > 0, delta* > 0, Hk >= 1.00005 (wake) / 1.05 (surface).
**Warning signs:** Variables going negative, convergence oscillation, RMSBL increasing

### Pitfall 6: Wake First Point Velocity
**What goes wrong:** Discontinuity at TE-to-wake junction causes drag error
**Why it happens:** XFoil explicitly sets DIJ(N+1,J) = DIJ(N,J) -- the first wake point has the same velocity influence as the last airfoil point (see xpanel.f line 1249). Missing this creates a velocity jump.
**How to avoid:** After computing DIJ for all wake points, copy the TE row to the first wake row.
**Warning signs:** Step change in edge velocity at TE, large discrepancy between surface-integrated and wake-momentum drag

## Code Examples

### BL Correlation: Kinematic Shape Parameter (HKIN)
```csharp
// Source: xblsys.f:2275-2286
public static (double Hk, double Hk_H, double Hk_Msq) KinematicShapeParameter(double h, double msq)
{
    // HK = (H - 0.29*MSQ) / (1.0 + 0.113*MSQ + 0.00056*MSQ^2)
    double hk = (h - 0.29 * msq) / (1.0 + 0.113 * msq + 0.00056 * msq * msq);
    double hk_h = 1.0 / (1.0 + 0.113 * msq + 0.00056 * msq * msq);
    double hk_msq = (-0.29 - hk * (0.113 + 2.0 * 0.00056 * msq))
                   / (1.0 + 0.113 * msq + 0.00056 * msq * msq);
    return (hk, hk_h, hk_msq);
}
```

### Newton Iteration Outer Loop Pattern (VISCAL)
```csharp
// Source: xoper.f:2583-2729
public ViscousAnalysisResult SolveViscous(/* parameters */)
{
    // 1. Calculate wake trajectory (XYWAKE)
    // 2. Set wake velocities from airfoil vorticity (QWCALC)
    // 3. Set velocities for initial alpha (QISET)
    // 4. Set up BL position <-> panel pointers (STFIND, IBLPAN, XICALC, IBLSYS)
    // 5. Set inviscid BL edge velocity (UICALC)
    // 6. Initialize BL variables from inviscid Ue if first time
    // 7. Set up DIJ source influence matrix (QDCALC)

    for (int iter = 0; iter < maxIterations; iter++)
    {
        // a. Build Newton system (SETBL)
        // b. Solve block-tridiagonal system (BLSOLV)
        // c. Apply Newton update with relaxation (UPDATE)
        // d. Update Mach/Re from new CL if alpha-prescribed (MRCL, COMSET)
        //    OR set new inviscid speeds for new alpha if CL-prescribed (QISET, UICALC)
        // e. Calculate edge velocities from UEDG (QVFUE)
        // f. Set GAM from QVIS (GAMQV)
        // g. Relocate stagnation point (STMOVE)
        // h. Calculate CL, CM, CD (CLCALC, CDCALC)
        // i. Check convergence: RMSBL < 1e-4
    }
}
```

### Squire-Young Wake Drag (CDCALC)
```csharp
// Source: xfoil.f:1162-1198
public static DragDecomposition ComputeDrag(BoundaryLayerSystemState blState, double qinf, double alfa)
{
    // Wake end quantities
    double thwake = blState.Theta[blState.NStations[1] - 1, 1];  // wake end theta
    double urat = blState.EdgeVelocity[blState.NStations[1] - 1, 1] / qinf;
    double uewake = blState.EdgeVelocity[blState.NStations[1] - 1, 1]
                  * (1.0 - tklam) / (1.0 - tklam * urat * urat);
    double shwake = blState.DeltaStar[blState.NStations[1] - 1, 1] / thwake;

    // Squire-Young extrapolation to infinity
    double cd = 2.0 * thwake * Math.Pow(uewake / qinf, 0.5 * (5.0 + shwake));

    // Friction drag
    double cdf = IntegrateSkinFriction(blState, qinf, alfa);

    // Pressure drag by subtraction
    double cdp = cd - cdf;

    return new DragDecomposition(cd, cdf, cdp);
}
```

## State of the Art

| Old Approach (Current Surrogate) | Current Approach (XFoil Port) | Impact |
|----------------------------------|-------------------------------|--------|
| Local Newton solve per BL interval (ViscousLaminarSolver) | Global simultaneous Newton for all BL stations + Ue coupling (SETBL+BLSOLV) | Converges in 5-20 iterations vs 100s of local sweeps; captures upstream influence |
| Displacement geometry feedback (DisplacementSurfaceGeometryBuilder) | DIJ influence matrix coupling (QDCALC) | Direct linear coupling vs iterative geometry modification; exact Jacobian |
| Simple Re_theta threshold for transition (LaminarAmplificationModel) | Full e^n envelope method (DAMPL+DAMPL2+TRCHEK2) | Correct prediction of transition for all Hk ranges including separation bubbles |
| Skin friction integration only (ViscousForceEstimator) | Squire-Young wake momentum deficit (CDCALC) | Correct total CD including wake; friction-only misses form drag |
| Laminar-only BL correlations | Full laminar + turbulent + wake correlations (BLVAR) | Handles turbulent BL and wake correctly |

**Deprecated/outdated:**
- Current surrogate files will be replaced: ViscousLaminarSolver, ViscousInteractionCoupler, EdgeVelocityFeedbackBuilder, ViscousLaminarCorrector, LaminarAmplificationModel, ViscousForceEstimator
- DisplacementSurfaceGeometryBuilder: Replaced by DIJ coupling (no displaced geometry needed)
- ViscousIntervalSystemBuilder: Replaced by global Newton system assembly

## Discretionary Recommendations

### VACCEL Acceleration Parameter
**Recommendation:** Start with VACCEL = 0.01 (matching XFoil default). This controls the threshold below which off-diagonal mass influence coefficients in BLSOLV are dropped during Gaussian elimination. Lower values = more accurate but slower; higher values = faster but may not converge for difficult cases.

### Trust-Region Initial Radius
**Recommendation:** Initial radius = 1.0, adaptation factor = 0.5 for shrink, 2.0 for expand (standard Levenberg-Marquardt). Switch to XFoil RLXBL mode for Phase 4 verification testing. The trust-region approach is more robust for difficult cases but may give slightly different convergence paths.

### Modern Transition Corrections
**Recommendation:** Use Drela 2003 formulation (updated DAMPL with improved correlations for separating profiles at high Hk). This is the same approach used in MISES and is a natural extension of XFoil's existing DAMPL/DAMPL2 framework. Specifically: keep DAMPL for Hk < 3.5, use DAMPL2 for Hk > 3.5 (this is already what XFoil does via the IDAMPV flag), and add optional crossflow corrections as a separate toggleable layer.

### BL Station Density for Initialization
**Recommendation:** Use the same station distribution as the inviscid panel nodes (matching XFoil's approach). XFoil's IBLPAN maps BL stations 1:1 to panel nodes on each surface. Additional intermediate stations are not needed and would complicate the ISYS mapping.

## Open Questions

1. **Compressible Mach-CL coupling iteration**
   - What we know: XFoil couples Mach and Re to CL via MRCL for Type 1/2/3 polars. This wasn't needed for inviscid Phase 2 but is essential for viscous polars at M > 0.
   - What's unclear: The exact RETYP/MATYP logic for each polar type.
   - Recommendation: Port MRCL and MRSHOW from xfoil.f:774 completely. This is < 200 lines and is well-documented in the OPER command help.

2. **Test data for 0.001% validation before Phase 4**
   - What we know: Phase 4 builds the full randomized test bench. Phase 3 needs to validate against SOMETHING.
   - What's unclear: How to get reference XFoil output without the Phase 4 binary.
   - Recommendation: Use hardcoded reference values from running XFoil manually on 2-3 standard airfoils (NACA 0012 at Re=1M, NACA 2412 at Re=3M, NACA 4415 at Re=6M). Include these as test constants. The 0.001% tolerance for Phase 3 can be tested against these known cases; the randomized testing is Phase 4's job.

3. **Handling of existing surrogate test expectations**
   - What we know: Existing tests (DisplacementCoupledViscousTests, ViscousInteractionTests, etc.) test the surrogate pipeline.
   - What's unclear: Whether to update these tests or create new ones.
   - Recommendation: Create NEW test classes for the ported solver. Keep old tests but mark them as testing the legacy surrogate (they may be removed later). This avoids breaking the test suite during development.

## Sources

### Primary (HIGH confidence)
- XFoil Fortran source: f_xfoil/src/xbl.f (1,581 lines) -- SETBL, IBLSYS, UPDATE
- XFoil Fortran source: f_xfoil/src/xblsys.f (2,522 lines) -- All BL correlations, TRCHEK2, BLSYS, BLDIF, TRDIF, BLVAR
- XFoil Fortran source: f_xfoil/src/xoper.f (2,780 lines) -- VISCAL outer loop, polar sweep orchestration
- XFoil Fortran source: f_xfoil/src/xpanel.f -- QDCALC, STMOVE, STFIND, XICALC, IBLPAN, UICALC, QVFUE
- XFoil Fortran source: f_xfoil/src/xsolve.f (488 lines) -- BLSOLV block-tridiagonal solver
- XFoil Fortran source: f_xfoil/src/xfoil.f (2,580 lines) -- CDCALC, CLCALC, MRCL
- Existing C# codebase: src/XFoil.Solver/ -- All current solver files examined

### Secondary (MEDIUM confidence)
- Drela, M., Giles, M., "Viscous-Inviscid Analysis of Transonic and Low Reynolds Number Airfoils", AIAA Journal, Vol. 25, No. 10, Oct. 1987 -- Cited in DAMPL header comments; original reference for the IBL method
- Drela, M., "XFOIL: An Analysis and Design System for Low Reynolds Number Airfoils", Springer-Verlag Lecture Notes in Engineering, 1989 -- General XFoil reference

### Tertiary (LOW confidence)
- Modern transition corrections (Arnal-Casalis-Habiballah, Drela 2003) -- Referenced in CONTEXT.md decisions; specific formulation details not verified from primary source

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All within existing project, no new dependencies
- Architecture: HIGH - Direct line-by-line mapping from well-documented Fortran source available in repo
- BL correlations: HIGH - Exact Fortran source is the reference; port is mechanical
- Newton system structure: HIGH - BLSOLV algorithm is explicit in xsolve.f
- Transition model: HIGH - DAMPL/DAMPL2/TRCHEK2 fully readable in xblsys.f
- Drag decomposition: HIGH - CDCALC is 36 lines of straightforward code
- Polar sweep modes: MEDIUM - MRCL type logic needs careful reading of xfoil.f/xoper.f interaction
- Modern enhancements (trust-region, wave drag, Viterna-Corrigan): MEDIUM - These go beyond XFoil; design decisions are user's but implementation details are Claude's discretion
- Pitfalls: HIGH - Identified from structural analysis of the coupling mechanism

**Research date:** 2026-03-11
**Valid until:** 2026-04-11 (stable domain -- XFoil source is frozen; C# codebase changes only through this project)
