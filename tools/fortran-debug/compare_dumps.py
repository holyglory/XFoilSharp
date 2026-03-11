#!/usr/bin/env python3
"""Compare Fortran and C# intermediate value dumps to find divergence.

Parses both dump files into structured data, compares iteration-by-iteration
and station-by-station, reports the first divergence point with context,
and shows RMSBL convergence trajectories.

Usage:
    python3 compare_dumps.py reference_dump.txt csharp_dump.txt
"""

import sys
import re
import math
from collections import defaultdict


def parse_fortran_floats(text):
    """Parse Fortran-style scientific notation floats from text.
    Handles both 'E+00' and cases where sign is directly adjacent."""
    # Match Fortran-style floats: optional sign, digits, decimal, digits, E, sign, digits
    pattern = r'[+-]?\d+\.\d+(?:E[+-]?\d+)?|[+-]?\d+\.\d+'
    # Try more robust: split on known E-notation boundaries
    vals = re.findall(r'[+-]?[\d]+\.[\d]+(?:[Ee][+-]?\d+)?', text)
    return [float(v) for v in vals]


def parse_csharp_floats(text):
    """Parse C# scientific notation floats."""
    vals = re.findall(r'[+-]?[\d]+\.[\d]+(?:[Ee][+-]?\d+)?', text)
    return [float(v) for v in vals]


class Station:
    """Data for a single BL station in one iteration."""
    def __init__(self):
        self.side = 0
        self.ibl = 0
        self.iv = 0
        self.bl_state = {}  # x, Ue, th, ds, m
        self.va_rows = [[], [], []]  # VA_ROW1, VA_ROW2, VA_ROW3
        self.vb_rows = [[], [], []]
        self.vdel_r = []
        self.vdel_s = []
        self.vsrez = []
        self.vs2_14 = []
        self.due2 = None
        self.dds2 = None


class Iteration:
    """Data for a single Newton iteration."""
    def __init__(self, num):
        self.num = num
        self.stations = []
        self.rmsbl = None
        self.rmxbl = None
        self.rlx = None
        self.cl = None
        self.cd = None
        self.cm = None
        self.cdf = None
        self.converged = False


def parse_dump(filepath, is_fortran=True):
    """Parse a dump file into a list of Iteration objects."""
    with open(filepath, 'r') as f:
        lines = f.readlines()

    iterations = []
    current_iter = None
    current_station = None

    for line in lines:
        line = line.rstrip()
        if not line:
            continue

        # Iteration markers
        iter_match = re.search(r'=== ITER\s+(\d+)\s*===', line)
        if iter_match:
            if current_station and current_iter:
                current_iter.stations.append(current_station)
                current_station = None
            current_iter = Iteration(int(iter_match.group(1)))
            iterations.append(current_iter)
            continue

        if current_iter is None:
            # Handle DUE2/DDS2 lines before first station (Fortran only)
            continue

        # POST_UPDATE, POST_CALC, CONVERGED lines (can appear after last station)
        post_match = re.match(r'\s*POST_UPDATE\s+RMSBL=\s*([^\s]+)\s+RMXBL=\s*([^\s]+)\s+RLX=\s*([^\s]+)', line)
        if post_match:
            if current_station:
                current_iter.stations.append(current_station)
                current_station = None
            current_iter.rmsbl = float(post_match.group(1))
            current_iter.rmxbl = float(post_match.group(2))
            current_iter.rlx = float(post_match.group(3))
            continue

        post_calc = re.match(r'\s*POST_CALC\s+CL=\s*([^\s]+)\s+CD=\s*([^\s]+)\s+CM=\s*([^\s]+)', line)
        if post_calc:
            if current_station:
                current_iter.stations.append(current_station)
                current_station = None
            current_iter.cl = float(post_calc.group(1))
            current_iter.cd = float(post_calc.group(2))
            current_iter.cm = float(post_calc.group(3))
            cdf_match = re.search(r'CDF=\s*([^\s]+)', line)
            if cdf_match:
                current_iter.cdf = float(cdf_match.group(1))
            continue

        if 'CONVERGED' in line:
            if current_station:
                current_iter.stations.append(current_station)
                current_station = None
            current_iter.converged = True
            continue

        # DUE2/DDS2 line (Fortran has these before each station)
        due_match = re.match(r'\s*DUE2=\s*([^\s]+)\s+DDS2=\s*([^\s]+)', line)
        if due_match:
            # Will be attached to the next station
            continue

        # Station header
        station_match = re.match(r'STATION IS=\s*(\d+)\s+IBL=\s*(\d+)\s+IV=\s*(\d+)', line)
        if station_match:
            if current_station:
                current_iter.stations.append(current_station)
            current_station = Station()
            current_station.side = int(station_match.group(1))
            current_station.ibl = int(station_match.group(2))
            current_station.iv = int(station_match.group(3))
            continue

        if current_station is None:
            # BLSOLV lines etc -- skip
            continue

        # Station data lines
        bl_match = re.match(r'BL_STATE\s+x=\s*([^\s]+)\s+Ue=\s*([^\s]+)\s+th=\s*([^\s]+)\s+ds=\s*([^\s]+)\s+m=\s*([^\s]+)', line)
        if bl_match:
            current_station.bl_state = {
                'x': float(bl_match.group(1)),
                'Ue': float(bl_match.group(2)),
                'th': float(bl_match.group(3)),
                'ds': float(bl_match.group(4)),
                'm': float(bl_match.group(5)),
            }
            continue

        for row_idx, row_name in enumerate(['VA_ROW1', 'VA_ROW2', 'VA_ROW3']):
            if line.startswith(row_name):
                vals = parse_fortran_floats(line[len(row_name):]) if is_fortran else parse_csharp_floats(line[len(row_name):])
                current_station.va_rows[row_idx] = vals
                break

        for row_idx, row_name in enumerate(['VB_ROW1', 'VB_ROW2', 'VB_ROW3']):
            if line.startswith(row_name):
                vals = parse_fortran_floats(line[len(row_name):]) if is_fortran else parse_csharp_floats(line[len(row_name):])
                current_station.vb_rows[row_idx] = vals
                break

        if line.startswith('VDEL_R'):
            vals = parse_fortran_floats(line[6:]) if is_fortran else parse_csharp_floats(line[6:])
            current_station.vdel_r = vals

        if line.startswith('VDEL_S'):
            vals = parse_fortran_floats(line[6:]) if is_fortran else parse_csharp_floats(line[6:])
            current_station.vdel_s = vals

        if line.startswith('VSREZ'):
            vals = parse_fortran_floats(line[5:]) if is_fortran else parse_csharp_floats(line[5:])
            current_station.vsrez = vals

        if line.startswith('VS2_14'):
            vals = parse_fortran_floats(line[6:]) if is_fortran else parse_csharp_floats(line[6:])
            current_station.vs2_14 = vals

    # Don't forget the last station
    if current_station and current_iter:
        current_iter.stations.append(current_station)

    return iterations


def rel_err(fortran, csharp):
    """Compute relative error."""
    denom = max(abs(fortran), 1e-20)
    return abs(fortran - csharp) / denom


def compare_values(name, f_vals, c_vals, threshold=0.01):
    """Compare two value lists, return list of (index, f_val, c_val, rel_error) for divergent entries."""
    divergences = []
    min_len = min(len(f_vals), len(c_vals))
    for i in range(min_len):
        err = rel_err(f_vals[i], c_vals[i])
        if err > threshold:
            divergences.append((i, f_vals[i], c_vals[i], err))
    return divergences


def main():
    if len(sys.argv) < 3:
        print("Usage: compare_dumps.py <fortran_dump> <csharp_dump>")
        sys.exit(1)

    fortran_path = sys.argv[1]
    csharp_path = sys.argv[2]

    print("=" * 72)
    print("Fortran vs C# Intermediate Value Comparison")
    print("=" * 72)
    print(f"Fortran: {fortran_path}")
    print(f"C#:      {csharp_path}")
    print()

    fortran_iters = parse_dump(fortran_path, is_fortran=True)
    csharp_iters = parse_dump(csharp_path, is_fortran=False)

    print(f"Fortran iterations: {len(fortran_iters)}")
    print(f"C# iterations:      {len(csharp_iters)}")
    print()

    # Show station counts
    for i, it in enumerate(fortran_iters):
        print(f"  Fortran iter {it.num}: {len(it.stations)} stations")
    for i, it in enumerate(csharp_iters):
        print(f"  C# iter {it.num}: {len(it.stations)} stations")
    print()

    # === RMSBL Convergence Trajectory ===
    print("=" * 72)
    print("RMSBL CONVERGENCE TRAJECTORY")
    print("=" * 72)
    print(f"{'Iter':>4} | {'Fortran RMSBL':>15} | {'C# RMSBL':>15} | {'F trend':>8} | {'C trend':>8}")
    print("-" * 72)

    prev_f_rms = None
    prev_c_rms = None
    f_consecutive_decrease = 0
    c_consecutive_decrease = 0
    f_max_consec = 0
    c_max_consec = 0

    max_compare = min(len(fortran_iters), len(csharp_iters))
    for i in range(max_compare):
        f_rms = fortran_iters[i].rmsbl
        c_rms = csharp_iters[i].rmsbl

        f_trend = ""
        c_trend = ""
        if prev_f_rms is not None and f_rms is not None:
            if f_rms < prev_f_rms:
                f_trend = "DOWN"
                f_consecutive_decrease += 1
            else:
                f_trend = "UP"
                f_consecutive_decrease = 0
        if prev_c_rms is not None and c_rms is not None:
            if c_rms < prev_c_rms:
                c_trend = "DOWN"
                c_consecutive_decrease += 1
            else:
                c_trend = "UP"
                c_consecutive_decrease = 0

        f_max_consec = max(f_max_consec, f_consecutive_decrease)
        c_max_consec = max(c_max_consec, c_consecutive_decrease)

        f_str = f"{f_rms:.8E}" if f_rms is not None else "N/A"
        c_str = f"{c_rms:.8E}" if c_rms is not None else "N/A"
        print(f"{i+1:4d} | {f_str:>15} | {c_str:>15} | {f_trend:>8} | {c_trend:>8}")

        prev_f_rms = f_rms
        prev_c_rms = c_rms

    # Print remaining iterations if one has more
    for i in range(max_compare, len(fortran_iters)):
        f_rms = fortran_iters[i].rmsbl
        f_str = f"{f_rms:.8E}" if f_rms is not None else "N/A"
        print(f"{i+1:4d} | {f_str:>15} | {'N/A':>15} |")

    for i in range(max_compare, len(csharp_iters)):
        c_rms = csharp_iters[i].rmsbl
        c_str = f"{c_rms:.8E}" if c_rms is not None else "N/A"
        print(f"{i+1:4d} | {'N/A':>15} | {c_str:>15} |")

    print()
    if f_max_consec >= 3:
        print("Fortran RMSBL: CONVERGING (3+ consecutive decreases)")
    else:
        print(f"Fortran RMSBL: NOT CONVERGING (max consecutive decrease: {f_max_consec})")

    if c_max_consec >= 3:
        print("C# RMSBL: CONVERGING (3+ consecutive decreases)")
    else:
        print(f"C# RMSBL: DIVERGING (max consecutive decrease: {c_max_consec})")

    print()

    # === CL/CD/CM comparison ===
    print("=" * 72)
    print("CL/CD/CM COMPARISON (per iteration)")
    print("=" * 72)
    print(f"{'Iter':>4} | {'F CL':>12} | {'C CL':>12} | {'F CD':>12} | {'C CD':>12}")
    print("-" * 72)
    for i in range(max_compare):
        fi = fortran_iters[i]
        ci = csharp_iters[i]
        f_cl = f"{fi.cl:.6E}" if fi.cl is not None else "N/A"
        c_cl = f"{ci.cl:.6E}" if ci.cl is not None else "N/A"
        f_cd = f"{fi.cd:.6E}" if fi.cd is not None else "N/A"
        c_cd = f"{ci.cd:.6E}" if ci.cd is not None else "N/A"
        print(f"{i+1:4d} | {f_cl:>12} | {c_cl:>12} | {f_cd:>12} | {c_cd:>12}")
    print()

    # === Station-by-station comparison for iteration 1 ===
    print("=" * 72)
    print("STATION-BY-STATION COMPARISON (Iteration 1)")
    print("=" * 72)

    if len(fortran_iters) == 0 or len(csharp_iters) == 0:
        print("ERROR: No iterations found in one or both dumps!")
        return

    f_iter1 = fortran_iters[0]
    c_iter1 = csharp_iters[0]

    # Index stations by (side, ibl) for matching
    f_stations = {(s.side, s.ibl): s for s in f_iter1.stations}
    c_stations = {(s.side, s.ibl): s for s in c_iter1.stations}

    print(f"\nFortran has {len(f_stations)} stations, C# has {len(c_stations)} stations")

    # Check if station numbering matches
    f_keys = sorted(f_stations.keys())
    c_keys = sorted(c_stations.keys())
    print(f"Fortran first 5 stations: {f_keys[:5]}")
    print(f"C# first 5 stations:      {c_keys[:5]}")
    print()

    # Check for IBL offset (Fortran starts at IBL=2, C# might start at IBL=1)
    f_min_ibl = min(k[1] for k in f_keys) if f_keys else 0
    c_min_ibl = min(k[1] for k in c_keys) if c_keys else 0
    ibl_offset = f_min_ibl - c_min_ibl
    if ibl_offset != 0:
        print(f"*** IBL OFFSET DETECTED: Fortran starts at IBL={f_min_ibl}, C# at IBL={c_min_ibl}")
        print(f"    Offset = {ibl_offset} (Fortran IBL = C# IBL + {ibl_offset})")
        print()

    # Try matching with offset
    first_divergence = {}  # category -> (station_key, detail)
    categories = ['BL_STATE', 'VA', 'VB', 'VDEL_R', 'VSREZ']

    # Compare by IV (system line number) since that's the common index
    f_by_iv = {s.iv: s for s in f_iter1.stations}
    c_by_iv = {s.iv: s for s in c_iter1.stations}

    common_ivs = sorted(set(f_by_iv.keys()) & set(c_by_iv.keys()))
    if not common_ivs:
        print("*** NO COMMON IV VALUES -- cannot compare station-by-station")
        print("    This means the ISYS mapping is different between Fortran and C#")
        print()
        # Try comparing by sequential index instead
        print("Comparing by sequential station index instead...")
        common_count = min(len(f_iter1.stations), len(c_iter1.stations))
        for idx in range(min(common_count, 5)):
            fs = f_iter1.stations[idx]
            cs = c_iter1.stations[idx]
            print(f"\n--- Station index {idx} ---")
            print(f"  Fortran: IS={fs.side} IBL={fs.ibl} IV={fs.iv}")
            print(f"  C#:      IS={cs.side} IBL={cs.ibl} IV={cs.iv}")
            if fs.bl_state and cs.bl_state:
                for key in ['x', 'Ue', 'th', 'ds', 'm']:
                    fv = fs.bl_state.get(key, 0)
                    cv = cs.bl_state.get(key, 0)
                    err = rel_err(fv, cv)
                    flag = " ***" if err > 0.01 else ""
                    print(f"  BL_STATE {key:>3}: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%}{flag}")

            for row_idx in range(3):
                if fs.va_rows[row_idx] and cs.va_rows[row_idx]:
                    divs = compare_values(f"VA_ROW{row_idx+1}", fs.va_rows[row_idx], cs.va_rows[row_idx])
                    for d_idx, fv, cv, err in divs:
                        print(f"  VA_ROW{row_idx+1}[{d_idx}]: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%} ***")

            for row_idx in range(3):
                if fs.vb_rows[row_idx] and cs.vb_rows[row_idx]:
                    divs = compare_values(f"VB_ROW{row_idx+1}", fs.vb_rows[row_idx], cs.vb_rows[row_idx])
                    for d_idx, fv, cv, err in divs:
                        print(f"  VB_ROW{row_idx+1}[{d_idx}]: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%} ***")

            if fs.vdel_r and cs.vdel_r:
                divs = compare_values("VDEL_R", fs.vdel_r, cs.vdel_r)
                for d_idx, fv, cv, err in divs:
                    print(f"  VDEL_R[{d_idx}]: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%} ***")

            if fs.vsrez and cs.vsrez:
                divs = compare_values("VSREZ", fs.vsrez, cs.vsrez)
                for d_idx, fv, cv, err in divs:
                    print(f"  VSREZ[{d_idx}]: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%} ***")

    else:
        print(f"Found {len(common_ivs)} common IV values")
        for iv in common_ivs[:10]:
            fs = f_by_iv[iv]
            cs = c_by_iv[iv]
            print(f"\n--- IV={iv} (F: IS={fs.side} IBL={fs.ibl}, C: IS={cs.side} IBL={cs.ibl}) ---")

            if fs.bl_state and cs.bl_state:
                for key in ['x', 'Ue', 'th', 'ds', 'm']:
                    fv = fs.bl_state.get(key, 0)
                    cv = cs.bl_state.get(key, 0)
                    err = rel_err(fv, cv)
                    flag = " ***" if err > 0.01 else ""
                    print(f"  BL_STATE {key:>3}: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%}{flag}")

                    if err > 0.01 and 'BL_STATE' not in first_divergence:
                        first_divergence['BL_STATE'] = (iv, f"BL_STATE.{key} F={fv:.8E} C={cv:.8E} err={err:.1%}")

            for row_idx in range(3):
                if fs.va_rows[row_idx] and cs.va_rows[row_idx]:
                    divs = compare_values(f"VA_ROW{row_idx+1}", fs.va_rows[row_idx], cs.va_rows[row_idx])
                    for d_idx, fv, cv, err in divs:
                        print(f"  VA_ROW{row_idx+1}[{d_idx}]: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%} ***")
                        if 'VA' not in first_divergence:
                            first_divergence['VA'] = (iv, f"VA_ROW{row_idx+1}[{d_idx}] F={fv:.8E} C={cv:.8E} err={err:.1%}")

            for row_idx in range(3):
                if fs.vb_rows[row_idx] and cs.vb_rows[row_idx]:
                    divs = compare_values(f"VB_ROW{row_idx+1}", fs.vb_rows[row_idx], cs.vb_rows[row_idx])
                    for d_idx, fv, cv, err in divs:
                        print(f"  VB_ROW{row_idx+1}[{d_idx}]: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%} ***")
                        if 'VB' not in first_divergence:
                            first_divergence['VB'] = (iv, f"VB_ROW{row_idx+1}[{d_idx}] F={fv:.8E} C={cv:.8E} err={err:.1%}")

            if fs.vdel_r and cs.vdel_r:
                divs = compare_values("VDEL_R", fs.vdel_r, cs.vdel_r)
                for d_idx, fv, cv, err in divs:
                    print(f"  VDEL_R[{d_idx}]: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%} ***")
                    if 'VDEL_R' not in first_divergence:
                        first_divergence['VDEL_R'] = (iv, f"VDEL_R[{d_idx}] F={fv:.8E} C={cv:.8E} err={err:.1%}")

            if fs.vsrez and cs.vsrez:
                divs = compare_values("VSREZ", fs.vsrez, cs.vsrez)
                for d_idx, fv, cv, err in divs:
                    print(f"  VSREZ[{d_idx}]: F={fv:15.8E}  C={cv:15.8E}  err={err:.1%} ***")
                    if 'VSREZ' not in first_divergence:
                        first_divergence['VSREZ'] = (iv, f"VSREZ[{d_idx}] F={fv:.8E} C={cv:.8E} err={err:.1%}")

    # === Summary ===
    print()
    print("=" * 72)
    print("DIVERGENCE SUMMARY")
    print("=" * 72)

    if first_divergence:
        print(f"\nFirst divergence points (>1% relative error):")
        for cat in categories:
            if cat in first_divergence:
                iv, detail = first_divergence[cat]
                print(f"  {cat:>10}: IV={iv} -- {detail}")
            else:
                print(f"  {cat:>10}: MATCH (within 1%)")
    else:
        print("  No common stations found for comparison (different IV numbering)")

    print()
    print("=" * 72)
    print("DIAGNOSIS")
    print("=" * 72)

    # Determine divergence pattern
    bl_diverged = 'BL_STATE' in first_divergence
    va_diverged = 'VA' in first_divergence
    vb_diverged = 'VB' in first_divergence
    vdel_diverged = 'VDEL_R' in first_divergence
    vsrez_diverged = 'VSREZ' in first_divergence

    # Check structural issues
    print()
    if len(f_iter1.stations) != len(c_iter1.stations):
        print(f"*** STRUCTURAL: Station count mismatch (Fortran={len(f_iter1.stations)}, C#={len(c_iter1.stations)})")
        print("    This indicates different BL station mapping (IBLTE, NBL, or ISYS differ)")

    if f_keys and c_keys:
        if f_keys[0] != c_keys[0]:
            print(f"*** STRUCTURAL: First station differs (Fortran={f_keys[0]}, C#={c_keys[0]})")
            if f_min_ibl == 2 and c_min_ibl == 1:
                print("    C# starts at IBL=1 while Fortran starts at IBL=2")
                print("    This means C# is including the stagnation point (IBL=1) as a BL station")
                print("    Fortran skips IBL=1 (similarity station has no 'previous' station)")

    if bl_diverged:
        print("\n** Pattern A/B: BL_STATE diverges from the start")
        print("   Root cause: Different BL initialization or different station numbering")
        print("   The C# solver uses a BL-march primary driver that modifies BL state")
        print("   BEFORE SETBL assembly. Fortran's VISCAL does SETBL -> BLSOLV -> UPDATE")
        print("   with NO BL-march step. This fundamentally changes the BL state that the")
        print("   Newton system sees.")

    if va_diverged or vb_diverged:
        print("\n** VA/VB blocks diverge")
        print("   The Newton system matrix coefficients differ.")
        print("   Root causes to check:")
        print("   1. BoundaryLayerSystemAssembler.ComputeFiniteDifferences uses simplified")
        print("      Jacobians with hardcoded Re=1e6, not the actual REYBL from COMSET")
        print("   2. ViscousNewtonAssembler stuffs VA[k,col,iv] = VS2[k,col] and VB = VS1")
        print("      but Fortran's SETBL has VA(eq,1..2,iv) = VS2(eq,1..2)")
        print("      and VM(eq,jv,iv) uses D1_M/D2_M/U1_M/U2_M chains accumulated")
        print("      station-to-station. C# only uses local VS2 for VM.")

    if vsrez_diverged:
        print("\n** VSREZ (local equation residuals) diverge")
        print("   This means the BL equation assembly itself is wrong.")
        print("   Check BoundaryLayerSystemAssembler.ComputeFiniteDifferences:")
        print("   - Momentum equation Jacobian is simplified (not full BLDIF)")
        print("   - Shape parameter equation is simplified")

    if vdel_diverged:
        print("\n** VDEL_R (Newton RHS) diverges")
        print("   Fortran's VDEL(k,1,IV) = VSREZ(k) + VS1(k,4)*DUE1 + VS1(k,3)*DDS1")
        print("                            + VS2(k,4)*DUE2 + VS2(k,3)*DDS2 + XI terms")
        print("   C# sets VDEL[k,0,iv] = localResult.Residual[k] (VSREZ only)")
        print("   MISSING: DUE/DDS forced-change terms in VDEL RHS")

    print()
    print("*** CRITICAL ROOT CAUSE SUMMARY ***")
    print()
    print("The C# ViscousSolverEngine.SolveViscousFromInviscid does NOT implement")
    print("Fortran VISCAL's iteration loop correctly:")
    print()
    print("  Fortran VISCAL:  SETBL -> BLSOLV -> UPDATE -> QVFUE -> STMOVE -> CLCALC")
    print("  C# current:      BL-march -> DIJ-update -> SETBL -> BLSOLV -> conditional-UPDATE")
    print()
    print("The BL-march modifies theta/dstar/ctau BEFORE SETBL sees them,")
    print("creating a completely different BL state than what Fortran's Newton system works with.")
    print("Additionally, VDEL RHS is missing DUE/DDS forced-change terms,")
    print("and the Jacobians in ComputeFiniteDifferences are simplified approximations.")
    print()
    print("To fix: Remove BL-march from the Newton iteration loop entirely.")
    print("Use the clean VISCAL pattern: SETBL -> BLSOLV -> UPDATE -> QVFUE -> STMOVE.")


if __name__ == '__main__':
    main()
