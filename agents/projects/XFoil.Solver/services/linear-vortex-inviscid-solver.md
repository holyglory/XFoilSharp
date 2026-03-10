# LinearVortexInviscidSolver

Static service implementing the complete linear-vorticity inviscid solver pipeline using XFoil's streamfunction formulation.

## Responsibility

Orchestrates all previously built components (geometry, influence coefficients, panel distribution, numerics) into the complete inviscid analysis chain. Provides system assembly, alpha/CL specification, and pressure integration.

## Public API

- `AssembleAndFactorSystem(panel, state, freestreamSpeed)` -- Port of GGCALC. Assembles (N+1)x(N+1) influence matrix, LU-factors, solves two basis RHS vectors.
- `SolveAtAngleOfAttack(alphaRadians, panel, state, freestreamSpeed, machNumber)` -- Port of SPECAL. Superimposes basis solutions, computes speeds, integrates forces.
- `SolveAtLiftCoefficient(targetCl, panel, state, freestreamSpeed, machNumber)` -- Port of SPECCL. Newton iteration for target CL.
- `ComputeInviscidSpeed(alphaRadians, state, nodeCount)` -- Basis speed superposition.
- `IntegratePressureForces(panel, state, alpha, mach, qinf, refX, refY)` -- Port of CLCALC. Pressure integration with Karman-Tsien correction and DG*DX/12 CM correction.
- `ComputePressureCoefficients(surfaceSpeed, qinf, mach, cp, count)` -- Port of CPCALC.
- `AnalyzeInviscid(inputX, inputY, count, alphaDeg, panels, mach)` -- High-level convenience entry point.

## Dependencies

- `StreamfunctionInfluenceCalculator` -- influence matrix rows
- `PanelGeometryBuilder` -- normals, angles, TE geometry, compressibility
- `CosineClusteringPanelDistributor` -- panel distribution
- `ScaledPivotLuSolver` -- LU decomposition and back-substitution

## Key Design Decisions

- Static class with raw arrays, matching XFoil Fortran calling convention.
- Sharp TE override replaces last airfoil-node row with internal bisector zero-velocity condition.
- Surface speed equals vortex strength (linear-vorticity property).
- CM includes second-order DG*DX/12 correction for moment accuracy.

## Parity Status

Aerodynamically correct (CL within 5% of thin-airfoil theory for NACA 0012). Exact Fortran bit-parity refinement deferred to Phase 4 test bench.

## File

`src/XFoil.Solver/Services/LinearVortexInviscidSolver.cs`
