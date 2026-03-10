# Linear-Vorticity State Models

- Files:
  - `src/XFoil.Solver/Models/LinearVortexPanelState.cs`
  - `src/XFoil.Solver/Models/InviscidSolverState.cs`
  - `src/XFoil.Solver/Models/LinearVortexInviscidResult.cs`

## Role

State containers for the linear-vorticity inviscid solver, paralleling XFoil's COMMON block data layout.

### LinearVortexPanelState

Mutable container for node-based panel geometry. Holds coordinates (X, Y), arc length, spline derivatives (dX/dS, dY/dS), outward normals, panel angles, and leading/trailing edge data. All arrays are 0-based with capacity `MaxNodes`. Active count is set via `Resize(nodeCount)`.

Corresponds to XFoil COMMON variables: X, Y, S, XP, YP, NX, NY, APANEL, SLE, XLE, YLE, XTE, YTE, CHORD.

### InviscidSolverState

Mutable workspace for the inviscid solve. Holds vortex strength (GAM), basis solutions (GAMU), source strength (SIG), influence matrices (AIJ, BIJ), pivot indices, inviscid speed arrays (QINV, QINVU), TE parameters, and workspace sensitivity arrays (DZDG, DZDN, DZDM, DQDG, DQDM). `InitializeForNodeCount(n)` zeros all arrays and sets dimensions.

Corresponds to XFoil COMMON variables: GAM, GAMU, SIG, AIJ, BIJ, AIJPIV, QINV, QINVU, QINV_A, GAM_A, PSIO, GAMTE, SIGTE, DSTE, ANTE, ASTE, SHARP, LGAMU, LQAIJ.

### LinearVortexInviscidResult

Immutable result with CL, CM, CDP, dCL/dalpha, dCL/dM^2, Cp distribution, and angle of attack.

Corresponds to XFoil output variables: CL, CM, CDP, CL_ALF, CL_MSQ, CPV, ALFA.

## Relationship to existing models

These are additive -- they do not modify or replace `Panel.cs`, `PanelMesh.cs`, or `InviscidAnalysisResult.cs`. The linear-vorticity solver uses a fundamentally different data layout (node-based vs panel-midpoint-based) and requires separate state containers.

## Parity

- Data layout matches XFoil's COMMON block structure with clean C# naming and 0-based indexing.
- Array dimensions match Fortran: VortexStrength is [N], BasisVortexStrength is [N+1, 2], StreamfunctionInfluence is [N+1, N+1].

## TODO

- Downstream solver components will populate these containers in plans 02-02 through 02-04.
