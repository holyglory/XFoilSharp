#!/bin/bash
# generate_selig_vectors.sh -- Generate Fortran XFoil parity vectors over the
# Selig airfoil database. One XFoil invocation per (airfoil, re, alpha, ncrit)
# combination, mirroring how the C# parity harness analyzes each case from a
# fresh state. Uses the headless stub binary so no X11 display is needed.
#
# Output: tools/fortran-debug/reference/selig_polar_vectors.txt
# Format per line:
#   <relative-dat-path> <re> <alpha> <ncrit> <cl> <cd> 0x<cl_hex> 0x<cd_hex>
#
# Non-converged cases are dropped silently.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
XFOIL="${XFOIL_BIN:-$REPO_ROOT/f_xfoil/build-headless-stub/src/xfoil-6.97}"
DAT_DIR="${SELIG_DAT_DIR:-$REPO_ROOT/tools/selig-database}"
OUTFILE="${SELIG_OUT:-$REPO_ROOT/tools/fortran-debug/reference/selig_polar_vectors.txt}"
TMPDIR="$(mktemp -d /tmp/xfoil_selig_XXXXXX)"
MAX_PARALLEL=${MAX_PARALLEL:-192}

# Per-case timeout (seconds). XFoil can hang on some pathological geometries.
CASE_TIMEOUT=${CASE_TIMEOUT:-30}

# Parameter sweep: 20 alphas x 5 Re x 2 nCrit = 200 cases per airfoil.
ALPHAS=(-10 -8 -6 -4 -2 0 1 2 3 4 5 6 7 8 10 12 14 16 18 20)
REYNOLDS=(100000 500000 1000000 3000000 10000000)
NCRITS=(5 9)

if [[ ! -x "$XFOIL" ]]; then
    echo "ERROR: xfoil binary not found at $XFOIL" >&2
    exit 1
fi

if [[ ! -d "$DAT_DIR" ]]; then
    echo "ERROR: Selig dat dir not found at $DAT_DIR" >&2
    exit 1
fi

mkdir -p "$(dirname "$OUTFILE")"
echo "=== Selig Vector Generation ==="
echo "Binary:      $XFOIL"
echo "Dat dir:     $DAT_DIR"
echo "Out file:    $OUTFILE"
echo "Tmp dir:     $TMPDIR"
echo "Parallel:    $MAX_PARALLEL"
echo "Alphas:      ${#ALPHAS[@]}"
echo "Reynolds:    ${#REYNOLDS[@]}"
echo "nCrit:       ${#NCRITS[@]}"

DAT_LIST=("$DAT_DIR"/*.dat)
N_AIRFOILS=${#DAT_LIST[@]}
TOTAL=$(( N_AIRFOILS * ${#ALPHAS[@]} * ${#REYNOLDS[@]} * ${#NCRITS[@]} ))
echo "Airfoils:    $N_AIRFOILS"
echo "Total cases: $TOTAL"

# Worker: one (airfoil, re, alpha, ncrit) case → one converged line in $5.
run_case() {
    local airfoil="$1" re="$2" alpha="$3" ncrit="$4" outfile="$5"
    local rawfile="${outfile}.raw"
    local rel_path
    rel_path="$(realpath --relative-to="$REPO_ROOT" "$airfoil" 2>/dev/null || echo "$airfoil")"

    {
        echo "LOAD $airfoil"
        echo ""
        echo "PANE"
        echo "OPER"
        echo "VPAR"
        echo "N $ncrit"
        echo ""
        echo "VISC $re"
        echo "ITER 80"
        echo "ALFA $alpha"
        echo ""
        echo "QUIT"
    } | timeout "$CASE_TIMEOUT" "$XFOIL" >"$rawfile" 2>&1 || true

    # Capture the LAST CDCALC_HEX line, regardless of convergence. The user
    # requirement is binary parity over ALL test vectors, including the ones
    # XFoil failed to converge — the C# parity branch must reproduce whatever
    # Fortran printed.
    #
    # Format directly from grep+awk to avoid forking python per case.
    local hex_line
    hex_line=$(grep -a "CDCALC_HEX" "$rawfile" | tail -1 || true)
    if [[ -z "$hex_line" ]]; then
        rm -f "$rawfile"
        return 0
    fi
    # Parse "CDCALC_HEX CL=<hex> CD=<hex>" or "CDCALC_HEX CD=<hex> CDF=<hex> CL=<hex>"
    local cl_hex cd_hex
    cl_hex=$(echo "$hex_line" | grep -oE 'CL=[0-9A-F]+' | head -1 | sed 's/CL=//')
    cd_hex=$(echo "$hex_line" | grep -oE 'CD=[0-9A-F]+' | head -1 | sed 's/CD=//')
    if [[ -z "$cl_hex" || -z "$cd_hex" ]]; then
        rm -f "$rawfile"
        return 0
    fi
    # Write minimal record: <path> <re> <alpha> <ncrit> 0x<cl_hex> 0x<cd_hex>
    # Decimal float values are reconstructed at compare time.
    printf '%s %s %s %s 0x%s 0x%s\n' "$rel_path" "$re" "$alpha" "$ncrit" "$cl_hex" "$cd_hex" >> "$outfile"
    rm -f "$rawfile"
}
export -f run_case
export XFOIL REPO_ROOT CASE_TIMEOUT

JOBLIST="$TMPDIR/jobs.txt"
JOB_NUM=0
for airfoil in "${DAT_LIST[@]}"; do
    for re in "${REYNOLDS[@]}"; do
        for ncrit in "${NCRITS[@]}"; do
            for alpha in "${ALPHAS[@]}"; do
                echo "$airfoil $re $alpha $ncrit $TMPDIR/case_${JOB_NUM}.txt" >> "$JOBLIST"
                JOB_NUM=$((JOB_NUM + 1))
            done
        done
    done
done

echo "Jobs:        $JOB_NUM"
STARTED=$(date +%s)

cat "$JOBLIST" | xargs -P "$MAX_PARALLEL" -L 1 bash -c 'run_case "$@"' _

ELAPSED=$(( $(date +%s) - STARTED ))
echo "XFoil sweep finished in ${ELAPSED}s"

# Concatenate outputs and sort deterministically
find "$TMPDIR" -name 'case_*.txt' -size +0 -print0 \
    | xargs -0 cat 2>/dev/null \
    | sort -k1,1 -k2,2n -k3,3n -k4,4n > "$OUTFILE" || true

FINAL_COUNT=$(wc -l < "$OUTFILE" 2>/dev/null || echo 0)
echo "Converged vectors: $FINAL_COUNT / $TOTAL"
echo "Output:            $OUTFILE"

rm -rf "$TMPDIR"
echo "=== Done ==="
