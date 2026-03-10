# CosineClusteringPanelDistributor

- File: `src/XFoil.Solver/Services/CosineClusteringPanelDistributor.cs`
- Role: panel distribution generator implementing XFoil's PANGEN algorithm (curvature-adaptive cosine spacing).

## Public methods

- `Distribute(inputX, inputY, inputCount, panel, desiredNodeCount, curvatureWeight, trailingEdgeDensityRatio, curvatureDensityRatio)` -- static method, populates a `LinearVortexPanelState` with distributed panel nodes.

## Important helpers (private)

- `FindLeadingEdge` -- Newton iteration to find LE arc-length parameter (tangent perpendicular to chord). Port of `LEFIND` from xgeom.f.
- `EvaluateSecondDerivative` -- second derivative d2X/dS2 at a parameter. Port of `D2VAL` from spline.f.
- `ComputeCurvature` -- curvature at a parameter from splined x/y. Port of `CURV` from spline.f.
- `SolveTridiagonalSegment` -- tridiagonal solve on a sub-range of arrays.

## Algorithm overview

1. Spline input coordinates using segmented spline (SEGSPL/SPLIND zero-3rd-derivative BCs)
2. Compute curvature array on input points, scale by half-arc-length
3. Find LE via LEFIND Newton iteration
4. Set TE artificial curvature (CTERAT * average LE curvature)
5. Smooth curvature via tridiagonal diffusion system with LE pinning
6. Normalize curvature to [0, 1] and spline it
7. Create IPFAC*(N-1)+1 oversampled initial node positions with TE density ratio
8. Newton iteration: equalize (1 + C*curvature)*deltaS on both sides of each node
9. Subsample to final N node positions, evaluate coordinates via spline
10. Handle corners (doubled arc-length points in buffer airfoil)
11. Recompute arc length, spline derivatives, LE/TE geometry on final nodes

## Parameters (XFoil defaults)

| Parameter | XFoil name | Default | Role |
|-----------|-----------|---------|------|
| desiredNodeCount | NPAN | 160 | number of panel nodes |
| curvatureWeight | CVPAR | 1.0 | panel bunching parameter |
| trailingEdgeDensityRatio | CTERAT | 0.15 | TE/LE panel density ratio |
| curvatureDensityRatio | CTRRAT | 0.2 | refinement region density ratio |

## Dependencies

- `ParametricSpline` -- arc-length, segmented spline fitting, evaluation, derivative evaluation
- `TridiagonalSolver` -- Thomas algorithm for curvature smoothing and Newton iteration systems
- `LinearVortexPanelState` -- output container for node positions and geometry

## Parity

- Direct port of PANGEN from xfoil.f (lines 1613-2115).
- Matches XFoil's curvature smoothing, LE detection, Newton iteration, TE density control.
- Refinement region bunching (XSREF/XPREF) disabled by default (matching XFoil's initial defaults of 1.0/1.0 which means no refinement).

## Known differences from existing PanelMeshGenerator

- PanelMeshGenerator uses Gaussian-weighted curvature clustering (approximate). This class uses XFoil's exact PANGEN algorithm.
- PanelMeshGenerator produces midpoint-based `Panel` objects. This class produces node-based `LinearVortexPanelState` arrays.
- Both are kept in the codebase; downstream consumers choose which to use.
