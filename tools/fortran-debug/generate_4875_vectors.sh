#!/bin/bash
# generate_4875_vectors.sh -- Generate 4875-entry XFoil polar reference vectors
#
# Matrix: 25 NACA profiles x 13 alphas x 5 Re x 3 nCrit = 4875 combinations
# Unconverged cases are omitted, so the final count will be <= 4875.
#
# Output: tools/fortran-debug/reference/clean_fortran_polar_vectors_4875.txt
# Exact-bit generation prefers a binary that emits CDCALC_HEX. Override with XFOIL_BIN if needed.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
XFOIL="${XFOIL_BIN:-$REPO_ROOT/f_xfoil/xfoil_headless_v2}"
OUTFILE="$REPO_ROOT/tools/fortran-debug/reference/clean_fortran_polar_vectors_4875.txt"
TMPDIR="$(mktemp -d /tmp/xfoil_4875_XXXXXX)"
MAX_PARALLEL=${MAX_PARALLEL:-25}

echo "=== XFoil 4875-Vector Reference Generation ==="
echo "Binary:       $XFOIL"
echo "Temp dir:     $TMPDIR"
echo "Output:       $OUTFILE"
echo "Max parallel: $MAX_PARALLEL"

if [[ ! -x "$XFOIL" ]]; then
    echo "ERROR: xfoil_headless_v2 not found at $XFOIL"
    exit 1
fi

# 25 NACA 4-digit profiles
AIRFOILS=(
    0003 0006 0009 0012 0015 0018 0021 0024
    1408 1410 1412
    2212 2312 2408 2410 2412 2415
    4406 4408 4412 4415 4418
    6406 6412 6415
)

# 13 alphas
ALPHAS=(-4 -2 -1 0 1 2 3 4 5 6 7 8 10)

# 5 Reynolds numbers
REYNOLDS=(500000 750000 1000000 2000000 5000000)

# 3 nCrit values
NCRITS=(5 9 12)

N_AIRFOILS=${#AIRFOILS[@]}
N_ALPHAS=${#ALPHAS[@]}
N_RE=${#REYNOLDS[@]}
N_NCRIT=${#NCRITS[@]}
TOTAL=$((N_AIRFOILS * N_ALPHAS * N_RE * N_NCRIT))

echo "Profiles: $N_AIRFOILS  Alphas: $N_ALPHAS  Re: $N_RE  nCrit: $N_NCRIT"
echo "Total combinations: $TOTAL"

# Sanity check
if [[ "$TOTAL" -ne 4875 ]]; then
    echo "ERROR: expected 4875 combinations, got $TOTAL"
    exit 1
fi

# Function to run a single XFoil case
run_case() {
    local naca="$1" re="$2" alpha="$3" ncrit="$4" outfile="$5"
    local rawfile="${outfile}.raw"

    # Build XFoil input script
    # Use integer alpha for integer values, decimal for others
    local alpha_str
    if [[ "$alpha" == *"."* ]]; then
        alpha_str="$alpha"
    else
        alpha_str="${alpha}.0"
    fi

    local input="NACA ${naca}
OPER
VISC ${re}
VPAR
N ${ncrit}

ALFA ${alpha_str}

QUIT
"
    echo "$input" | timeout 120 "$XFOIL" >"$rawfile" 2>&1 || true

    # Skip if VISCAL reports global convergence failure
    if grep -a -q "VISCAL:  Convergence failed" "$rawfile"; then
        rm -f "$rawfile"
        return 0
    fi

    # Prefer exact float bits from the final CDCALC_HEX packet when available.
    local hex_line last_cl_hex last_cd_hex
    hex_line=$(grep -a "CDCALC_HEX" "$rawfile" | tail -1 || true)
    last_cl_hex=$(echo "$hex_line" | sed -n 's/.*CL=\([0-9A-F][0-9A-F]*\).*/\1/p')
    last_cd_hex=$(echo "$hex_line" | sed -n 's/.*CD=\([0-9A-F][0-9A-F]*\).*/\1/p')

    if [[ -n "$last_cl_hex" && -n "$last_cd_hex" ]]; then
        local exact_values exact_cl exact_cd
        exact_values=$(python3 - <<'PY' "$last_cl_hex" "$last_cd_hex"
import struct
import sys

cl_bits = int(sys.argv[1], 16)
cd_bits = int(sys.argv[2], 16)
cl = struct.unpack('!f', struct.pack('!I', cl_bits))[0]
cd = struct.unpack('!f', struct.pack('!I', cd_bits))[0]
print(f"{cl:.9g} {cd:.9g}")
PY
)
        exact_cl="${exact_values%% *}"
        exact_cd="${exact_values##* }"
        echo "${naca} ${re} ${alpha} ${ncrit} ${exact_cl} ${exact_cd} 0x${last_cl_hex} 0x${last_cd_hex}" > "$outfile"
        rm -f "$rawfile"
        return 0
    fi

    # Fallback for older headless builds that only print rounded final CL/CD values.
    local last_cl last_cd
    last_cl=$(grep -a "CL =" "$rawfile" | tail -1 | sed 's/.*CL = *\([^ ]*\).*/\1/')
    last_cd=$(grep -a "CD =" "$rawfile" | tail -1 | sed 's/.*CD = *\([^ ]*\).*/\1/')

    if [[ -n "$last_cl" && -n "$last_cd" ]]; then
        echo "${naca} ${re} ${alpha} ${ncrit} ${last_cl} ${last_cd}" > "$outfile"
    fi

    rm -f "$rawfile"
}

export -f run_case
export XFOIL

# Generate all case arguments into a job file
CASE_NUM=0
for naca in "${AIRFOILS[@]}"; do
    for re in "${REYNOLDS[@]}"; do
        for alpha in "${ALPHAS[@]}"; do
            for ncrit in "${NCRITS[@]}"; do
                echo "$naca $re $alpha $ncrit $TMPDIR/case_${CASE_NUM}.txt"
                CASE_NUM=$((CASE_NUM + 1))
            done
        done
    done
done > "$TMPDIR/cases.txt"

echo "Generated $CASE_NUM job entries"
echo "Running with up to $MAX_PARALLEL parallel processes..."

# Run in parallel using xargs with progress tracking
STARTED=$(date +%s)
cat "$TMPDIR/cases.txt" | xargs -P "$MAX_PARALLEL" -L 1 bash -c 'run_case "$@"' _
ELAPSED=$(( $(date +%s) - STARTED ))

echo "Completed in ${ELAPSED}s"
echo "Collecting results..."

# Collect and sort all results
# Sort order: NACA (lex), Re (numeric), alpha (numeric), nCrit (numeric)
cat "$TMPDIR"/case_*.txt 2>/dev/null | sort -k1,1 -k2,2n -k3,3n -k4,4n > "$OUTFILE" || true

FINAL_COUNT=$(wc -l < "$OUTFILE" 2>/dev/null || echo 0)
echo "Converged vectors: $FINAL_COUNT / $TOTAL"
echo "Written to: $OUTFILE"

# Cleanup
rm -rf "$TMPDIR"

echo "=== Done ==="
