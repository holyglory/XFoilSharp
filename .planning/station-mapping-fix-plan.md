# BL Station Mapping Fix — Implementation Plan

## Root Causes (5 compounding bugs in C# BL station setup)

1. **No fractional stagnation point (SST)**: C# `FindStagnationPointByMinSpeed` picks discrete node with min |speed|. Fortran `STFIND` (xpanel.f:1338) interpolates between nodes where GAM changes sign to get fractional arc-length SST.

2. **Wrong XSSI computation**: C# `SetBLArcLengths` uses cumulative node-to-node distances. Fortran `XICALC` (xpanel.f:1436) uses `|SST - S(IPAN)|` — absolute distance from fractional stagnation point to each panel node's arc-length. First station XSSI should be tiny (fraction of panel spacing), not a full panel length.

3. **Wrong UEDG initialization**: C# `SetInviscidEdgeVelocities` sets `UEDG[0,side] = Math.Abs(qinv[isp]) >= 0.001`. Fortran `UICALC` (xpanel.f:1523) sets `UINV(1,IS) = 0` (virtual stagnation), `UINV(IBL,IS) = VTI(IBL,IS) * QINV(IPAN)` for real stations.

4. **Wrong Thwaites formula**: C# `InitializeBLFromInviscidUe` uses `TSQ = 0.45/(reinf*ue0)*xsi0` and `dsi = 2.6*thi`. Fortran `MRCHUE` (xbl.f:554) uses `TSQ = 0.45*XSI/(6*UEI*REYBL)` (factor of 6 from `5*BULE+1` with `BULE=1`) and `dsi = 2.2*thi`.

5. **Missing similarity station in Newton system**: C# `MapStationsToSystemLines` starts from station 2 (skipping both virtual stag AND similarity). Fortran `IBLSYS` starts from IBL=2 (skips only virtual stag IBL=1). C# should start from station 1.

## Station Indexing Convention (after fix)

### Fortran (1-based):
- IBL=1: Virtual stagnation (XSSI=0, UEDG=0, not in Newton system)
- IBL=2: Similarity station (first in Newton system, SIMI=true)
- IBL=3..IBLTE: Airfoil stations
- IBL=IBLTE+1..NBL: Wake stations

### C# (0-based, after fix):
- Station 0: Virtual stagnation (XSSI=0, UEDG=0, IPAN=-1)
- Station 1: Similarity station (first in Newton system, simi=true)
- Station 2..IBLTE: Airfoil stations
- Station IBLTE+1..NBL-1: Wake stations

### Panel index mapping (IPAN):
- Side 0 (upper): station k → panel `isp - (k-1)` for k>=1 (isp, isp-1, ..., 0)
- Side 1 (lower): station k → panel `isp + k` for k>=1 (isp+1, isp+2, ..., n-1)
- Wake: station k → panel `n + (k - IBLTE) - 1`

### VTI signs:
- Side 0 (upper): VTI = +1.0
- Side 1 (lower): VTI = -1.0
- Wake: VTI = -1.0

### Station counts (n=160, isp=79):
- Side 0: IBLTE[0] = isp+1 = 80, NBL[0] = isp+2 = 81
- Side 1: IBLTE[1] = n-1-isp = 80, NBL[1] = n-isp+nWake = 81+nWake

## Changes Already Made

### BoundaryLayerSystemState.cs ✅
- Added `int[,] IPAN` array (panel index per station/side, -1 for virtual stag)
- Added `double[,] VTI` array (tangential velocity sign per station/side)

### AnalysisSettings.cs ✅
- No changes needed (removed the UseXFoilStationMapping flag per user request)

### ViscousSolverEngine.cs — PARTIALLY DONE
- Modified init sequence (lines ~80-110) to call XFoil methods
- BUT: still has branching logic that needs to be simplified (remove legacy branches)

## Remaining Changes

### 1. ViscousSolverEngine.cs — Add new methods

#### FindStagnationPointXFoil (port of STFIND, xpanel.f:1338-1373)
```csharp
private static (int isp, double sst) FindStagnationPointXFoil(
    double[] qinv, LinearVortexPanelState panel, int n)
{
    // Find where qinv changes sign (GAM(I) >= 0 and GAM(I+1) < 0)
    int ist = -1;
    for (int i = 0; i < n - 1; i++)
    {
        if (qinv[i] >= 0.0 && qinv[i + 1] < 0.0)
        {
            ist = i;
            break;
        }
    }
    if (ist < 0) ist = n / 2;

    double dgam = qinv[ist + 1] - qinv[ist];
    double ds = panel.ArcLength[ist + 1] - panel.ArcLength[ist];
    double sst;
    if (qinv[ist] < -qinv[ist + 1])
        sst = panel.ArcLength[ist] - ds * (qinv[ist] / dgam);
    else
        sst = panel.ArcLength[ist + 1] - ds * (qinv[ist + 1] / dgam);

    if (sst <= panel.ArcLength[ist]) sst = panel.ArcLength[ist] + 1.0e-7;
    if (sst >= panel.ArcLength[ist + 1]) sst = panel.ArcLength[ist + 1] - 1.0e-7;

    return (ist, sst);
}
```

#### ComputeStationCountsXFoil
```csharp
private static (int[] iblte, int[] nbl) ComputeStationCountsXFoil(int n, int isp, int nWake)
{
    int[] iblte = new int[2];
    int[] nbl = new int[2];
    iblte[0] = isp + 1;
    nbl[0] = isp + 2;
    iblte[1] = n - 1 - isp;
    nbl[1] = (n - 1 - isp) + 1 + nWake;
    return (iblte, nbl);
}
```

#### InitializeXFoilStationMapping (combines IBLPAN + XICALC + UICALC)
Populates IPAN, VTI, XSSI, UEDG for all stations on both sides:
- Station 0: IPAN=-1, VTI=±1, XSSI=0, UEDG=0
- Station k (airfoil): IPAN per mapping above, XSSI=|SST-S(IPAN)|, UEDG=VTI*qinv[IPAN]
- Station k (wake): cumulative XSSI, UEDG from TE with decay

#### InitializeBLThwaitesXFoil (port of MRCHUE similarity init, xbl.f:554-564)
Key differences from old InitializeBLFromInviscidUe:
- `tsq = 0.45 * xsi0 / (6.0 * ue0 * reinf)` (factor of 6)
- `dsi = 2.2 * thi` (not 2.6)
- Station 0: THET=thi, DSTR=dsi, MASS=0 (UEDG=0)
- Station 1: THET=thi, DSTR=dsi, MASS=dsi*UEDG[1]

### 2. ViscousSolverEngine.cs — Replace existing methods

#### Replace GetPanelIndex (line ~638)
```csharp
private static int GetPanelIndex(int ibl, int side, int isp, int nPanel,
    BoundaryLayerSystemState blState)
{
    if (ibl < 0 || ibl >= blState.MaxStations) return -1;
    return blState.IPAN[ibl, side];
}
```

#### Replace BuildViscousPanelSpeeds (line ~764) — use IPAN/VTI
```csharp
// Change inner loops to:
for (int side = 0; side < 2; side++)
    for (int ibl = 1; ibl < blState.NBL[side] && ibl <= blState.IBLTE[side]; ibl++)
    {
        int iPan = blState.IPAN[ibl, side];
        if (iPan >= 0 && iPan < n)
            qvis[iPan] = blState.VTI[ibl, side] * blState.UEDG[ibl, side];
    }
```

#### Replace ConvertUedgToSpeeds (line ~800) — use IPAN/VTI
Same pattern as BuildViscousPanelSpeeds.

#### Simplify SolveViscousFromInviscid init (lines ~80-110)
Remove branching, just use XFoil methods directly. Remove references to old methods.

#### Fix STMOVE section (line ~221)
When stagnation point moves, recalculate IPAN/VTI/station counts:
```csharp
if (newIsp != isp)
{
    // Recalculate station mapping for new stag point
    // Need to re-interpolate SST and rebuild IPAN/VTI
}
```

#### Delete old broken methods:
- `FindStagnationPointByMinSpeed` (replaced by FindStagnationPointXFoil)
- `SetBLArcLengths` (replaced by InitializeXFoilStationMapping)
- `SetInviscidEdgeVelocities` (replaced by InitializeXFoilStationMapping)
- `InitializeBLFromInviscidUe` (replaced by InitializeBLThwaitesXFoil)

### 3. EdgeVelocityCalculator.cs — Fix MapStationsToSystemLines

Change to start from station 1 instead of station 2:
```csharp
int side1Lines = nbl[0] - 1;  // was nbl[0] - 2
int side2Lines = nbl[1] - 1;  // was nbl[1] - 2
// Loop: for (int i = 1; ...) instead of for (int i = 2; ...)
```

MapPanelsToBLStations can be deleted (replaced by ComputeStationCountsXFoil in engine).

### 4. ViscousNewtonAssembler.cs — Fix march loop

#### Change march start (line 117)
```csharp
for (int ibl = 1; ibl < blState.NBL[side]; ibl++)  // was ibl = 2
```

#### Fix simi detection (line 131)
Detect from first ISYS entry per side:
```csharp
// Find first ISYS station for this side
int simiStation = -1;
for (int j = 0; j < nsys; j++)
    if (isys[j, 1] == side) { simiStation = isys[j, 0]; break; }
bool simi = (ibl == simiStation);  // was (ibl == 2)
```

#### Fix initialization before loop (lines 99-114)
Initialize "previous" from station 0 (virtual stag):
```csharp
double x1 = Math.Max(blState.XSSI[0, side], 1e-10);
double u1 = Math.Max(blState.UEDG[0, side], 1e-10);
double t1 = Math.Max(blState.THET[0, side], 1e-10);
double d1 = Math.Max(blState.DSTR[0, side], 1e-10);
```

#### Replace GetPanelIndex (line ~401)
```csharp
private static int GetPanelIndex(int ibl, int side, int isp, int nPanel,
    BoundaryLayerSystemState blState)
{
    if (ibl < 0 || ibl >= blState.MaxStations) return -1;
    return blState.IPAN[ibl, side];
}
```

#### Replace GetVTI (line ~437)
```csharp
private static double GetVTI(int ibl, int side, BoundaryLayerSystemState blState)
{
    if (ibl < 0 || ibl >= blState.MaxStations) return 1.0;
    return blState.VTI[ibl, side];
}
```

## Key Fortran Reference Files
- `f_xfoil/src/xpanel.f`: STFIND (1338), IBLPAN (1376), XICALC (1436), UICALC (1523)
- `f_xfoil/src/xbl.f`: SETBL (21), MRCHUE (530)
- `f_xfoil/src/xblsys.f`: BLPRV (701), BLKIN (725), BLSYS (583), BLDIF (1551)
- `f_xfoil/src/XBL.INC`: COMMON blocks for BL variables
- `f_xfoil/src/XFOIL.INC`: Panel arrays (X, Y, S, GAM, etc.)

## Testing Strategy
After implementing all fixes:
1. Run `dotnet test` to see how many tests pass/fail
2. Run diagnostic dump test to generate new csharp_dump.txt
3. Compare with reference_dump.txt using compare_dumps.py
4. Check station 1 x-coordinates and Ue values match Fortran
5. Check station count matches (should be same as Fortran: 182 per iteration)
6. If RMSBL converges (not diverges to INF), the fix worked
