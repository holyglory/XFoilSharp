#!/bin/bash
# generate_polar_vectors.sh -- Generate XFoil polar test vectors in parallel
# Output: tools/fortran-debug/reference/clean_fortran_polar_vectors.txt
# Exact-bit generation prefers a binary that emits CDCALC_HEX. Override with XFOIL_BIN if needed.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
XFOIL="${XFOIL_BIN:-$REPO_ROOT/f_xfoil/build-headless/xfoil_headless}"
OUTFILE="$REPO_ROOT/tools/fortran-debug/reference/clean_fortran_polar_vectors.txt"
TMPDIR="$(mktemp -d /tmp/xfoil_vectors_XXXXXX)"
MAX_PARALLEL=${MAX_PARALLEL:-80}

echo "=== XFoil Polar Vector Generation ==="
echo "Binary:   $XFOIL"
echo "Temp dir: $TMPDIR"
echo "Output:   $OUTFILE"
echo "Max parallel: $MAX_PARALLEL"

if [[ ! -x "$XFOIL" ]]; then
    echo "ERROR: xfoil_headless not found at $XFOIL"
    exit 1
fi

# Airfoils, Reynolds numbers, alphas, Ncrit values
AIRFOILS=(0012 2412 4412 0009 0015 0018 1412 2212 2312 4415 6412 0006)
REYNOLDS=(500000 750000 1000000 2000000 5000000)
ALPHAS=(-2 0 1 2 3 4 5 6 7 8 10)
NCRITS=(5 9 12)

TOTAL=$((${#AIRFOILS[@]} * ${#REYNOLDS[@]} * ${#ALPHAS[@]} * ${#NCRITS[@]}))
echo "Total cases: $TOTAL"

# Function to run a single XFoil case
run_case() {
    local naca="$1" re="$2" alpha="$3" ncrit="$4" outfile="$5"
    local rawfile="${outfile}.raw"

    local input="NACA ${naca}
OPER
VISC ${re}
VPAR
N ${ncrit}

ALFA ${alpha}.0

QUIT
"
    echo "$input" | timeout 60 "$XFOIL" >"$rawfile" 2>&1 || true

    # Check for convergence failure
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

    if [[ -n "$last_cl" && -n "$last_cd" && "$last_cl" != "0" || "$last_cd" != "0" ]]; then
        echo "${naca} ${re} ${alpha} ${ncrit} ${last_cl} ${last_cd}" > "$outfile"
    fi

    rm -f "$rawfile"
}

export -f run_case
export XFOIL

# Generate all case arguments
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

echo "Running $CASE_NUM cases with up to $MAX_PARALLEL parallel processes..."

# Run in parallel using xargs
cat "$TMPDIR/cases.txt" | xargs -P "$MAX_PARALLEL" -L 1 bash -c 'run_case "$@"' _

echo "Collecting results..."

# Collect all results
RESULT_FILE="$TMPDIR/all_results.txt"
cat "$TMPDIR"/case_*.txt 2>/dev/null | sort > "$RESULT_FILE" || true

NEW_COUNT=$(wc -l < "$RESULT_FILE" 2>/dev/null || echo 0)
echo "New vectors generated: $NEW_COUNT"

# Merge with existing vectors if any
if [[ -f "$OUTFILE" ]]; then
    EXISTING_COUNT=$(wc -l < "$OUTFILE")
    echo "Existing vectors: $EXISTING_COUNT"

    # The existing file has 5 fields (no Ncrit): NACA RE ALPHA CL CD
    # New file has 6 fields: NACA RE ALPHA NCRIT CL CD
    # Check existing format
    EXISTING_FIELDS=$(head -1 "$OUTFILE" | awk '{print NF}')
    if [[ "$EXISTING_FIELDS" == "5" ]]; then
        echo "Existing file has 5-field format (no Ncrit). New vectors use keyed records."
        echo "Keeping only newly generated keyed vectors."
        cp "$RESULT_FILE" "$TMPDIR/merged.txt"
    else
        # Merge by case key and keep the richest record (prefer exact-bit 8-field rows over legacy rounded rows).
        cat "$OUTFILE" "$RESULT_FILE" | \
            awk '
                {
                    key = $1 " " $2 " " $3 " " $4
                    if (!(key in seen) || NF >= fields[key]) {
                        seen[key] = $0
                        fields[key] = NF
                    }
                }
                END {
                    for (key in seen) print seen[key]
                }
            ' | sort -k1,1 -k2,2n -k3,3n -k4,4n > "$TMPDIR/merged.txt"
    fi
else
    cp "$RESULT_FILE" "$TMPDIR/merged.txt"
fi

FINAL_COUNT=$(wc -l < "$TMPDIR/merged.txt")
echo "Final vector count: $FINAL_COUNT"

# Write final output (ONLY after all data is safely in merged.txt)
cp "$TMPDIR/merged.txt" "$OUTFILE"
echo "Written to: $OUTFILE"

# Cleanup temp dir AFTER final file is safely written
rm -rf "$TMPDIR"

echo "=== Done ==="
