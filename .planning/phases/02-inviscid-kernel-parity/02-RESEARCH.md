# Phase 2: Inviscid Kernel Parity - Research

**Researched:** 2026-03-10
**Domain:** XFoil inviscid panel method -- linear-vorticity/source streamfunction formulation
**Confidence:** HIGH

## Summary

Phase 2 requires replacing the existing C# Hess-Smith inviscid solver with a faithful port of XFoil's linear-vorticity streamfunction formulation. The two approaches are fundamentally different: XFoil uses a streamfunction-based formulation with linearly-varying vorticity and quadratically-varying source distributions on each panel, whereas the current C# code uses a classic Hess-Smith constant-strength source/vortex approach. Achieving 0.001% parity demands a line-by-line port of the Fortran algorithms, not an "algorithmically equivalent" reimplementation.

The core inviscid workflow in XFoil is: (1) GGCALC solves for two unit vorticity distributions (alpha=0 and alpha=90), storing them in GAMU; (2) SPECAL/SPECCL superimposes these via cos/sin(alpha) to get the actual GAM distribution; (3) CLCALC integrates pressure forces using Karman-Tsien compressibility corrections. The influence coefficient computation (PSILIN) is the heart of the formulation -- it computes streamfunction and its derivatives at each node due to all panels. This uses a specific Green's function decomposition with log(r^2) and atan2 terms for both the vortex and source contributions.

**Primary recommendation:** Port the Fortran routines line-by-line into C#, preserving XFoil's data layout (node-based, not panel-midpoint-based). Build intermediate-value comparison tests against the Fortran binary to catch numerical drift at each stage.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INV-01 | Port PSILIN (streamfunction influence coefficients) faithfully from xpanel.f | Covered by Architecture Pattern 1 -- PSILIN algorithm fully analyzed, line-by-line port strategy documented |
| INV-02 | Port GGCALC (gamma/sigma solution) faithfully from xpanel.f | Covered by Architecture Pattern 2 -- GGCALC system assembly + LU factoring fully analyzed |
| INV-03 | Port CLCALC (lift/moment coefficient recovery) faithfully from xfoil.f | Covered by Architecture Pattern 3 -- CLCALC pressure integration with KT correction fully analyzed |
| INV-04 | Inviscid CL, CM match original XFoil within 0.001% for any valid airfoil | Covered by parity strategy section -- requires matching PANGEN, PSILIN, GGCALC, SPECAL, CLCALC chain end-to-end |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 10 | net10.0 | Target framework | Already established in Phase 1 |
| C# 10 | LangVersion 10.0 | Language | Pinned in Phase 1 to avoid Span overload issues |
| System.Numerics | built-in | Math operations | Standard .NET math -- double precision matches Fortran REAL*8 behavior |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xunit | existing | Unit tests | Intermediate value validation |
| FluentAssertions or manual Assert | existing | Tolerance assertions | 0.001% relative tolerance comparisons |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Span<T>/stackalloc for arrays | Regular heap arrays | Span could improve cache locality for large AIJ matrix, but correctness-first is more important; optimize later |
| MathNet.Numerics LU solver | Hand-ported LUDCMP/BAKSUB | XFoil's LUDCMP has specific partial-pivoting behavior that must be matched exactly; use the port |

## Architecture Patterns

### Critical Difference: XFoil vs Current C# Formulation

The current C# `HessSmithInviscidSolver` uses a fundamentally different panel method:

| Aspect | XFoil (Fortran) | Current C# |
|--------|-----------------|------------|
| **Unknowns** | Per-node vortex strength GAM(i) + internal streamfunction PSIO | Per-panel source strength + single global vortex strength |
| **Panel type** | Linear-vorticity with quadratic source sub-panels | Constant-strength source + constant vortex |
| **Influence** | Streamfunction PSI at each node from all panels | Normal/tangential velocity at panel midpoints |
| **Geometry** | Nodes at panel endpoints; N nodes = N panels (closed wrap) | Panel objects with start/end/midpoint; separate normal/tangent |
| **System size** | (N+1) x (N+1): N node equations + 1 Kutta | (N+1) x (N+1): N normal-flow equations + 1 Kutta |
| **TE treatment** | Explicit TE panel with source/vortex from gap geometry | Implicit via Kutta condition on first/last panel tangential velocity |
| **Sharp TE** | Internal bisector velocity = 0 condition replaces last equation | No special handling |
| **Solution reuse** | Two basis solutions (alpha=0, alpha=90) linearly combined | Two basis solutions (Ux=1, Uy=1) linearly combined |
| **CL recovery** | Pressure integration with Karman-Tsien correction on CPG | Circulation-based or pressure integration with separate KT |
| **CM recovery** | Trapezoidal with second-order correction (DG*DX/12) | Midpoint pressure x arm |

**Conclusion:** The formulations are incompatible. The C# code cannot be "tweaked" to match XFoil -- it must be replaced with a faithful port.

### Recommended Project Structure
```
src/XFoil.Solver/
  Services/
    XFoilInviscidSolver.cs        # New: faithful port (replaces HessSmithInviscidSolver for parity path)
    HessSmithInviscidSolver.cs     # Keep: existing code, not deleted (downstream consumers may use it)
  Numerics/
    XFoilLinearSystem.cs           # New: LUDCMP + BAKSUB port
    XFoilSpline.cs                 # New: SPLINE/SPLIND/SEGSPL port (XFoil's specific spline formulation)
    DenseLinearSystemSolver.cs     # Keep: existing Gauss elimination
  Models/
    XFoilPanelState.cs             # New: holds N, X[], Y[], S[], XP[], YP[], NX[], NY[], APANEL[], etc.
    XFoilInviscidState.cs          # New: holds GAM[], GAMU[,], SIG[], AIJ[,], BIJ[,], PSIO, etc.
    XFoilInviscidResult.cs         # New: CL, CM, CDP, CL_ALF, CL_MSQ, GAM[], QINV[], CPI[]
```

### Pattern 1: PSILIN -- Streamfunction Influence Coefficients
**What:** For each node I, computes PSI (streamfunction) and sensitivity arrays DZDG[], DZDM[], DQDG[], DQDM[] due to all N panels + freestream + TE panel.
**When to use:** Called once per node during GGCALC matrix assembly.

**Algorithm overview (from xpanel.f lines 87-476):**
1. Initialize all derivative arrays to zero
2. Compute TE bisector ratios SCS, SDS (from ANTE/ASTE/DSTE via TECALC)
3. For each panel JO = 1..N:
   - Compute local panel coordinate system: SX, SY (unit tangent), DSO (panel length)
   - Transform field point to local coords: X1, X2, YY (distance along and normal to panel)
   - Compute log(r^2) terms G1, G2 and atan2 terms T1, T2 with branch-cut handling (SGN flag)
   - **Vortex contribution:** PSIS (sum) and PSID (difference) integrals using linear vorticity
   - **Source contribution** (if SIGLIN): Two half-panel quadratic source integrals using midpoint X0, with PSUM/PDIF for each half
   - Accumulate DZDG[], DZDM[], PSI, PSI_NI, QTAN1, QTAN2
4. After loop: Handle last-to-first TE panel (special source SIGTE and vortex GAMTE)
5. Add freestream: PSI += QINF*(cos(a)*Y - sin(a)*X)
6. Optionally handle image system (LIMAGE -- ground effect, can skip for initial port)

**Critical numerical details:**
- SEPS = (S(N)-S(1)) * 1.0E-5 for degenerate panel detection
- QOPI = 1/(4*PI), HOPI = 1/(2*PI) are the scaling constants
- Self-influence (IO == JO or IO == JP) sets G1=0, T1=0 to avoid log(0)/atan2(0,0)
- The SGN flag for wake points prevents branch-cut issues in atan2
- X1I, X2I, YYI are normal-direction projections for computing tangential velocity (PSI_NI)

### Pattern 2: GGCALC -- Gamma/Sigma System Assembly and Solution
**What:** Builds and solves the (N+1)x(N+1) linear system for two unit vorticity distributions (alpha=0 and alpha=90 degrees).
**When to use:** Called once after geometry is paneled, before any alpha specification.

**Algorithm (from xpanel.f lines 976-1111):**
1. Zero out GAM, GAMU arrays
2. For each airfoil node I = 1..N:
   - Call PSILIN(I, X(I), Y(I), NX(I), NY(I), PSI, PSI_N, .FALSE., .TRUE.)
   - Store influence coefficients: AIJ(I,J) = DZDG(J) for J=1..N
   - Store source influence: BIJ(I,J) = -DZDM(J) for J=1..N
   - Kutta condition row: AIJ(I,N+1) = -1.0
   - RHS vectors: GAMU(I,1) = -QINF*Y(I), GAMU(I,2) = QINF*X(I)
3. Standard Kutta condition: AIJ(N+1,1) = 1, AIJ(N+1,N) = 1 (GAM(1) + GAM(N) = 0)
4. **Sharp TE override:** If SHARP, replace row N with internal bisector zero-velocity condition:
   - Compute bisector angle from TE geometry
   - Place control point slightly inside TE
   - Call PSILIN at bisector point to get DQDG[] (tangential velocity derivatives)
   - Row N becomes: AIJ(N,J) = DQDG(J), RHS: GAMU(N,1) = -CBIS, GAMU(N,2) = -SBIS
5. LU-factor AIJ via LUDCMP (Crout's method with scaled partial pivoting)
6. Back-substitute GAMU(:,1) and GAMU(:,2) via BAKSUB
7. Copy results to QINVU (inviscid surface speed basis vectors)

**Key state produced:**
- AIJ[,] -- LU-factored, reused for viscous source influence (QDCALC) later
- AIJPIV[] -- pivot index array for BAKSUB
- GAMU[,2] -- basis vorticity distributions
- GAMU(N+1,:) -- basis internal streamfunction values
- BIJ[,] -- source influence on streamfunction (used by QDCALC later)
- LQAIJ = true, LGAMU = true

### Pattern 3: SPECAL/SPECCL -- Alpha/CL Specification
**What:** Combines the two basis solutions for a given alpha, then iterates to converge Mach number (for compressible cases).
**When to use:** For each operating point.

**Algorithm (from xoper.f lines 2429-2513):**
1. Call GGCALC if not already done
2. Superimpose: GAM(I) = cos(a)*GAMU(I,1) + sin(a)*GAMU(I,2)
3. Compute GAM_A (dGAM/dalpha) for CL_ALF computation
4. Call TECALC (TE gap strengths), QISET (set QINV from QINVU)
5. Newton iteration on CLM (CL-Mach coupling) -- for M=0 this converges in 1 step
6. Call CLCALC at each iteration to update CL, CM

### Pattern 4: CLCALC -- Lift and Moment Recovery
**What:** Integrates surface pressures to get CL, CM, CDP, and derivatives CL_ALF, CL_MSQ.
**When to use:** After GAM distribution is set.

**Algorithm (from xfoil.f lines 1078-1158):**
1. Compute compressibility parameters: BETA = sqrt(1-M^2), BFAC = M^2/(2*(1+BETA))
2. For each panel edge I = 1..N:
   - Compute Karman-Tsien corrected Cp: CPG = (1-(GAM/QINF)^2) / (BETA + BFAC*(1-(GAM/QINF)^2))
   - This is NOT the same as simple Prandtl-Glauert -- it uses the full KT denominator
3. For each panel I to I+1:
   - DX = projected chord increment in wind axis
   - DY = projected thickness increment in wind axis
   - CL += DX * (CPG_avg)
   - CDP -= DY * (CPG_avg)
   - CM -= DX*(AG*AX + DG*DX/12) + DY*(AG*AY + DG*DY/12)  -- **note the DG*DX/12 second-order term**
4. CL_ALF = dCL/dalpha, CL_MSQ = dCL/dMach^2 (both computed analytically alongside)

**Critical detail:** The CM formula includes a second-order correction term (DG*DX/12 and DG*DY/12) that uses the pressure coefficient *difference* across the panel. The current C# code uses midpoint pressure x arm which misses this. This alone can cause non-trivial CM differences.

### Pattern 5: Supporting Routines That Must Be Ported
| Routine | Source | Purpose | Notes |
|---------|--------|---------|-------|
| SPLINE/SPLIND | spline.f | Cubic spline with zero 2nd/3rd derivative or specified BCs | Different from C#'s natural-slope spline -- must match exactly |
| SEGSPL | spline.f | Segmented spline allowing derivative discontinuities | Used by NCALC for normal vectors |
| TRISOL | spline.f | Tridiagonal solver | Standard Thomas algorithm |
| NCALC | xpanel.f | Normal vector computation from spline derivatives | Uses SEGSPL, then normalizes |
| APCALC | xpanel.f | Panel angle computation | atan2(SX, -SY) -- note swapped args |
| TECALC | xfoil.f | TE gap geometry: ANTE, ASTE, DSTE, SHARP flag | Critical for TE panel treatment |
| COMSET | xfoil.f | Karman-Tsien parameter TKLAM, sonic Cp/speed | Needed for compressibility |
| CPCALC | xfoil.f | Compressible Cp from speed | Used after SPECAL for Cp distribution |
| MRCL | xfoil.f | Mach/Re from CL dependency | Type 1 (fixed) is simplest |
| LUDCMP | xsolve.f | LU decomposition with scaled partial pivoting | Crout's method -- specific pivot scaling |
| BAKSUB | xsolve.f | LU back-substitution | Standard forward/back substitution |
| ATANC | xutils.f | atan2 with branch-cut continuation | Used in GGCALC sharp-TE bisector angle |
| PANGEN | xfoil.f | Panel distribution generation | Must match for identical panel geometry |
| QISET | xpanel.f | Set QINV = cos(a)*QINVU(:,1) + sin(a)*QINVU(:,2) | Simple linear combination |

### Anti-Patterns to Avoid
- **Algorithmic equivalence instead of line-by-line port:** The 0.001% tolerance means even mathematically equivalent formulas can fail due to floating-point ordering differences. Port the exact sequence of operations.
- **Using panel-midpoint geometry:** XFoil stores and computes everything at panel *nodes* (endpoints), not midpoints. The current C# Panel class uses midpoint control points -- the port needs node-based arrays.
- **Reusing the existing DenseLinearSystemSolver:** XFoil's LUDCMP uses Crout's method with *scaled* partial pivoting (VV[] scaling vector). The existing C# solver uses unscaled partial pivoting. The pivot ordering will differ, causing different roundoff propagation.
- **Ignoring Fortran array indexing:** Fortran is 1-indexed. Off-by-one errors in loop bounds are the most common source of subtle bugs in Fortran-to-C ports. Use explicit 1-based indexing comments or a thin wrapper.
- **Single-precision constants:** Fortran REAL defaults to single precision (~7 digits). Some XFoil constants like PI are declared to full precision. C# uses double (15 digits). This mismatch is actually beneficial (more precision) but means the C# results might be *more* accurate than Fortran -- for parity testing, compare against double-precision Fortran builds or accept 0.001% as the tolerance floor.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Linear system solving | Custom Gauss elimination | Port LUDCMP/BAKSUB exactly | Pivot ordering affects results at 0.001% level |
| Spline interpolation | Natural cubic spline | Port SPLINE/SPLIND/SEGSPL | XFoil's spline has specific zero-3rd-derivative BCs and segment discontinuities |
| Panel generation | Custom weighted distribution | Port PANGEN from xfoil.f | Panel placement determines node positions which determine all downstream results |
| Normal vectors | Finite-difference normals | Port NCALC using ported SEGSPL | Spline-derivative normals match XFoil's exactly |
| Compressibility | Prandtl-Glauert correction | Port CPCALC/CLCALC KT formulas | Karman-Tsien is NOT Prandtl-Glauert; the denominator structure matters |

**Key insight:** For 0.001% parity, every intermediate quantity must match. There is no shortcut -- the Fortran code IS the specification.

## Common Pitfalls

### Pitfall 1: Fortran Array Indexing and Equivalencing
**What goes wrong:** Off-by-one errors when translating 1-based Fortran loops to 0-based C# arrays.
**Why it happens:** Fortran DO loops are inclusive on both ends. C# for loops typically use `i < N`.
**How to avoid:** Use 1-based indexing internally (allocate arrays of size N+2, ignore index 0). Add explicit comments mapping Fortran indices to C# indices. This trades a small amount of memory for dramatically easier verification.
**Warning signs:** Results that are close but not matching -- check if adjacent panel values are being used.

### Pitfall 2: TE Panel Treatment (Sharp vs Finite-Thickness)
**What goes wrong:** Incorrect CL/CM for airfoils with non-zero TE gap.
**Why it happens:** XFoil has two distinct TE treatments. For `SHARP` (TE gap < 0.0001*chord), the last equation becomes a bisector-velocity=0 condition. For finite-thickness TE, the standard Kutta condition is used, plus an explicit TE panel with computed source/vortex strengths.
**How to avoid:** Port TECALC first and test with both NACA 0012 (sharp TE) and real airfoils with finite TE gaps. The SCS/SDS ratios (ANTE/DSTE, ASTE/DSTE) must be computed identically.
**Warning signs:** CL matches for symmetric airfoils but diverges for cambered ones, or vice versa.

### Pitfall 3: atan2 Branch Cut Handling
**What goes wrong:** PSI values jump discontinuously for wake points or points near the panel line.
**Why it happens:** PSILIN uses a SGN reflection flag to keep atan2 values in the correct branch. The ATANC function provides branch-cut-crossing continuation. Missing either of these causes sign flips in streamfunction values.
**How to avoid:** Port the SGN logic exactly as written. Test with wake points (IO > N) where the branch handling differs from airfoil surface points.
**Warning signs:** Wake velocities have wrong sign or magnitude.

### Pitfall 4: Panel Node Ordering Convention
**What goes wrong:** CL has the wrong sign; pressure distribution is flipped.
**Why it happens:** XFoil traverses the airfoil counterclockwise starting from the upper-surface TE, going around the LE to the lower-surface TE. The current C# code may use a different convention. The SHARP flag and KUTTA condition assume specific node numbering (node 1 = upper TE, node N = lower TE).
**How to avoid:** Ensure the airfoil point ordering matches XFoil's convention before entering the solver. The normalizer must produce the same node ordering as XFoil's PANGEN.
**Warning signs:** GAM values have reversed sign pattern from expected.

### Pitfall 5: Spline Implementation Mismatch
**What goes wrong:** Normal vectors NX, NY differ slightly, causing influence coefficients to diverge.
**Why it happens:** The existing C# CubicSpline uses natural (zero second derivative) end conditions. XFoil's SEGSPL calls SPLIND with XS1=XS2=-999.0, which sets *zero third derivative* end conditions. These produce different slope values at endpoints, propagating through NCALC into NX, NY values.
**How to avoid:** Port SPLIND with its three BC modes (specified derivative, zero 2nd derivative, zero 3rd derivative) and SEGSPL with its segment-joint discontinuity handling.
**Warning signs:** Normal vectors at TE and LE differ from Fortran values.

### Pitfall 6: Fortran REAL (Single-Precision) vs C# Double
**What goes wrong:** Parity tests pass at 0.001% but fail at tighter tolerances.
**Why it happens:** Standard Fortran REAL is 32-bit (about 7 decimal digits). C# double is 64-bit (about 15 digits). The Fortran code accumulates more roundoff. At 0.001% tolerance this is unlikely to matter, but if the Fortran reference binary uses single precision, the C# code being more precise could still show apparent disagreement.
**How to avoid:** Check if the Fortran build uses `-fdefault-real-8` or equivalent. If not, accept that 0.001% is a floor below which single-precision noise dominates. For intermediate-value testing, use tolerance-aware comparisons.
**Warning signs:** Agreement varies randomly between 4th and 7th significant digits.

### Pitfall 7: EQUIVALENCE Statements and Memory Layout
**What goes wrong:** Confusion about which arrays share memory in Fortran.
**Why it happens:** XFOIL.INC has `EQUIVALENCE (Q(1,1), W1(1)), ...` and `EQUIVALENCE (VM(1,1,1), BIJ(1,1))`. These mean Q and W1-W8 share memory (used as workspace), and VM/BIJ/CIJ share memory (used in different solver phases).
**How to avoid:** In C#, these become separate arrays. No memory sharing is needed -- just ensure the right array is used in the right context. BIJ is workspace during GGCALC (source influence on streamfunction), then reused as workspace for wake source influence in QDCALC.
**Warning signs:** Corrupted values when one routine's output overwrites another's input.

## Code Examples

### Example 1: PSILIN Core Panel Influence (Vortex Part)
```fortran
C Source: f_xfoil/src/xpanel.f lines 336-372
C------ calculate vortex panel contribution to Psi
        DXINV = 1.0/(X1-X2)
        PSIS = 0.5*X1*G1 - 0.5*X2*G2 + X2 - X1 + YY*(T1-T2)
        PSID = ((X1+X2)*PSIS + 0.5*(RS2*G2-RS1*G1 + X1*X1-X2*X2))*DXINV
C
        GSUM = GAM(JP) + GAM(JO)
        GDIF = GAM(JP) - GAM(JO)
C
        PSI = PSI + QOPI*(PSIS*GSUM + PSID*GDIF)
C
C------ dPsi/dGam
        DZDG(JO) = DZDG(JO) + QOPI*(PSIS-PSID)
        DZDG(JP) = DZDG(JP) + QOPI*(PSIS+PSID)
```
**C# translation pattern:**
```csharp
// Direct port -- preserve operation order for floating-point parity
double dxInv = 1.0 / (x1 - x2);
double psis = 0.5 * x1 * g1 - 0.5 * x2 * g2 + x2 - x1 + yy * (t1 - t2);
double psid = ((x1 + x2) * psis + 0.5 * (rs2 * g2 - rs1 * g1 + x1 * x1 - x2 * x2)) * dxInv;

double gsum = gam[jp] + gam[jo];
double gdif = gam[jp] - gam[jo];

psi += qopi * (psis * gsum + psid * gdif);

dzdg[jo] += qopi * (psis - psid);
dzdg[jp] += qopi * (psis + psid);
```

### Example 2: GGCALC System Assembly
```fortran
C Source: f_xfoil/src/xpanel.f lines 999-1026
      DO 20 I=1, N
        CALL PSILIN(I,X(I),Y(I),NX(I),NY(I),PSI,PSI_N,.FALSE.,.TRUE.)
C
        RES1 =  QINF*Y(I)
        RES2 = -QINF*X(I)
C
        DO 201 J=1, N
          AIJ(I,J) = DZDG(J)
  201   CONTINUE
C
        DO 202 J=1, N
          BIJ(I,J) = -DZDM(J)
  202   CONTINUE
C
        AIJ(I,N+1) = -1.0
        GAMU(I,1) = -RES1
        GAMU(I,2) = -RES2
   20 CONTINUE
```

### Example 3: CLCALC Pressure Integration
```fortran
C Source: f_xfoil/src/xfoil.f lines 1120-1155
      DO 10 I=1, N
        IP = I+1
        IF(I.EQ.N) IP = 1
C
        CGINC = 1.0 - (GAM(IP)/QINF)**2
        CPG2  = CGINC/(BETA + BFAC*CGINC)
C
        DX = (X(IP) - X(I))*CA + (Y(IP) - Y(I))*SA
        DY = (Y(IP) - Y(I))*CA - (X(IP) - X(I))*SA
        DG = CPG2 - CPG1
C
        AX = (0.5*(X(IP)+X(I))-XREF)*CA + (0.5*(Y(IP)+Y(I))-YREF)*SA
        AY = (0.5*(Y(IP)+Y(I))-YREF)*CA - (0.5*(X(IP)+X(I))-XREF)*SA
        AG = 0.5*(CPG2 + CPG1)
C
        CL  = CL  + DX*AG
        CDP = CDP - DY*AG
        CM  = CM  - DX*(AG*AX + DG*DX/12.0)
     &            - DY*(AG*AY + DG*DY/12.0)
```

### Example 4: LUDCMP with Scaled Partial Pivoting
```fortran
C Source: f_xfoil/src/xsolve.f lines 174-247
C     Key difference from generic Gaussian elimination:
C     Row scaling via VV(I) = 1/max(|A(I,J)|)
C     Pivot selection uses DUM = VV(I)*ABS(SUM) not just ABS(SUM)
      DO 12 I=1, N
        AAMAX = 0.
        DO 11 J=1, N
          AAMAX = MAX( ABS(A(I,J)) , AAMAX )
   11   CONTINUE
        VV(I) = 1.0/AAMAX
   12 CONTINUE
```

## Parity Strategy: Line-by-Line Port

### Why Not Algorithmic Equivalence

For 0.001% tolerance, the exact sequence of floating-point operations matters. Two mathematically identical formulas can produce different results due to:
- Associativity: (a + b) + c != a + (b + c) in floating point
- Intermediate rounding: different subexpression ordering changes roundoff
- Cancellation: nearly-equal values subtracted in different order

**Strategy:** Port each Fortran routine as a self-contained C# method, preserving:
1. Loop ordering (outer/inner loop nesting)
2. Accumulation order (which terms are added first)
3. Temporary variable decomposition (keep same intermediates)
4. Array indexing pattern (1-based in C#, using offset or explicit +1)

### Verification Hierarchy

| Level | What | How | Tolerance |
|-------|------|-----|-----------|
| 1 | Spline derivatives XP[], YP[] | Compare node-by-node after SEGSPL | 1e-12 |
| 2 | Normal vectors NX[], NY[] | Compare node-by-node after NCALC | 1e-12 |
| 3 | Panel angles APANEL[] | Compare node-by-node after APCALC | 1e-12 |
| 4 | TE geometry ANTE, ASTE, DSTE, SHARP | Compare after TECALC | 1e-12 |
| 5 | Influence coefficients AIJ[,] | Compare row-by-row after GGCALC assembly | 1e-10 |
| 6 | Basis solutions GAMU[,2] | Compare after LU solve | 1e-8 |
| 7 | Surface speed QINV[] at given alpha | Compare after QISET | 1e-8 |
| 8 | CL, CM values | Compare after CLCALC | 0.001% relative |

### Paneling: The First Domino

**PANGEN** (xfoil.f line 1613) is the panel distribution generator. It uses cosine spacing modified by curvature clustering, TE/LE density ratios, and local refinement zones. The current C# PanelMeshGenerator uses a different algorithm (Gaussian-weighted curvature clustering). For parity:

**Option A (recommended):** Port PANGEN exactly. This ensures identical panel node positions, making all downstream comparisons meaningful.

**Option B:** Accept different paneling and only test CL/CM at the output level. This is fragile because different panel positions change AIJ, GAMU, and everything downstream -- you cannot debug intermediate-value mismatches.

### Data Flow Summary

```
Airfoil coordinates (X,Y from file)
  |
  v
PANGEN --> Panel nodes X[], Y[], S[] (arc length)
  |
  v
SEGSPL/NCALC --> Spline derivatives XP[], YP[] --> Normals NX[], NY[]
  |
  v
APCALC --> Panel angles APANEL[]
  |
  v
TECALC --> TE gap geometry: ANTE, ASTE, DSTE, SHARP, SCS, SDS
  |
  v
GGCALC --> For each node: PSILIN computes influences
        --> Assembly: AIJ[,] (streamfunction/gamma), BIJ[,] (streamfunction/source)
        --> LU factor AIJ via LUDCMP
        --> Solve: GAMU[,1] (alpha=0), GAMU[,2] (alpha=90) via BAKSUB
        --> Copy: QINVU[,1], QINVU[,2]
  |
  v
SPECAL(alpha) --> GAM[] = cos(a)*GAMU[,1] + sin(a)*GAMU[,2]
              --> TECALC, QISET
              --> Newton iteration on CLM for compressibility
              --> CLCALC --> CL, CM, CDP, CL_ALF, CL_MSQ
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| C# Hess-Smith formulation | Need: XFoil linear-vorticity streamfunction | Phase 2 | Fundamental formulation change |
| C# natural cubic spline | Need: XFoil SPLIND/SEGSPL with 3 BC modes | Phase 2 | Affects normal vectors, hence all downstream |
| C# midpoint-based panels | Need: XFoil node-based arrays | Phase 2 | Different data layout throughout |
| C# generic Gauss solver | Need: XFoil LUDCMP/BAKSUB with scaled pivoting | Phase 2 | Different pivot ordering affects roundoff |

**Important:** The existing C# Hess-Smith solver and all its dependent infrastructure should NOT be deleted. It serves the existing viscous coupling workflow (Phase 3 will replace that workflow, but until then the old code has value for regression testing and understanding).

## Open Questions

1. **Panel generation parity: Port PANGEN or accept different paneling?**
   - What we know: Different panel positions cause cascading differences in all intermediate values, making debugging nearly impossible without matching paneling.
   - What's unclear: How much effort PANGEN porting requires (it's a complex routine with iterative cosine-spacing refinement).
   - Recommendation: Port PANGEN. The investment pays off immediately in debuggability. Without it, 0.001% parity is achievable but much harder to verify and debug.

2. **Fortran single vs double precision in reference binary**
   - What we know: Standard Fortran REAL is 32-bit. The f_xfoil submodule Makefile needs checking for precision flags.
   - What's unclear: Whether the reference Fortran binary will be compiled with `-fdefault-real-8` or not.
   - Recommendation: Compile Fortran with `-fdefault-real-8` for double precision to match C# double. If not possible, the 0.001% tolerance should absorb single-precision noise.

3. **Image system (ground effect)**
   - What we know: PSILIN has an LIMAGE branch (lines 480+) that adds an image airfoil below Y=YIMAGE.
   - What's unclear: Whether any test cases use ground effect.
   - Recommendation: Skip LIMAGE for initial port (set LIMAGE=false). It's a feature, not needed for parity testing.

4. **Inverse-design DOFs (QF0-QF3, QDOF0-QDOF3)**
   - What we know: PSILIN computes Z_QDOF0..3 sensitivities for inverse design (QDES).
   - What's unclear: Whether these affect inviscid-only results.
   - Recommendation: Port the GEOLIN=false path only (no geometric sensitivities). The Z_QDOF terms only matter for inverse design, not direct analysis. Set GEOLIN=false in the GGCALC calls as the Fortran does.

## Sources

### Primary (HIGH confidence)
- f_xfoil/src/xpanel.f -- PSILIN (lines 87-476), GGCALC (lines 976-1111), NCALC (lines 51-84), APCALC (lines 22-48), QISET (lines 1578-1595)
- f_xfoil/src/xsolve.f -- LUDCMP (lines 174-247), BAKSUB (lines 250-279)
- f_xfoil/src/xfoil.f -- CLCALC (lines 1078-1158), TECALC (lines 2270-2308), COMSET (lines 1019-1044), CPCALC (lines 1047-1075), MRCL (lines 774-824)
- f_xfoil/src/xoper.f -- SPECAL (lines 2429-2513), SPECCL (lines 2516+)
- f_xfoil/src/spline.f -- SPLINE (lines 21-60), SPLIND (lines 63-130), SEGSPL (lines 533-558)
- f_xfoil/src/xutils.f -- ATANC (lines 68-107)
- f_xfoil/src/XFOIL.INC -- all shared state declarations and documentation

### Secondary (MEDIUM confidence)
- agents/architecture/ParityAndTodos.md -- confirms inviscid solver is not a direct port, lists parity gaps
- src/XFoil.Solver/Services/HessSmithInviscidSolver.cs -- current C# implementation analyzed for differences

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - .NET 10 / C# 10 established in Phase 1, no new dependencies needed
- Architecture: HIGH - Fortran source code is definitive; algorithm fully analyzed from source
- Pitfalls: HIGH - based on direct analysis of code and known Fortran-to-C porting issues
- Parity strategy: HIGH - line-by-line port is the only approach proven to achieve sub-0.01% agreement in panel methods

**Research date:** 2026-03-10
**Valid until:** Indefinite -- Fortran source code is stable (XFoil has not been updated since ~2013)
