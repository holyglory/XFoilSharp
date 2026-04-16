#!/bin/bash
# Parallel hex-precision BL state parity test
# Compares Fortran and C# converged BL states directly in hex
# Usage: bash hex_parity_parallel.sh [N_CASES] [N_PARALLEL]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
FORT_BIN="$REPO_ROOT/f_xfoil/build-nofma-trace/xfoil_nofma"
COMPARE_PROJ="$REPO_ROOT/tools/fortran-debug/ParallelPolarCompare"
export PATH="$HOME/.dotnet:$PATH"
export XFOIL_DISABLE_FMA=1
export XFOIL_SETBL_HEX=1

N_CASES=${1:-100}
N_PARALLEL=${2:-8}
RESULTS_DIR=$(mktemp -d)

check_case() {
    local naca=$1 re=$2 alpha=$3 nc=$4 idx=$5
    local outfile="$RESULTS_DIR/$idx.txt"

    c_bl=$(dotnet run --project "$COMPARE_PROJ" -c Release -- --diag "$naca" "$re" "$alpha" "$nc" 2>&1 | grep "^C_BL s=" | sort)
    f_bl=$(echo -e "NACA $naca\nOPER\nVPAR\nN $nc\n\nVISC $re\nITER 100\nALFA $alpha\n\nQUIT" | timeout 15 "$FORT_BIN" 2>&1 | grep -a "^F_BL s=" | tail -182 | sort)

    c_count=$(echo "$c_bl" | grep -c . || true)
    f_count=$(echo "$f_bl" | grep -c . || true)

    if [ "$c_count" -eq 0 ] || [ "$f_count" -eq 0 ]; then
        echo "SKIP $naca Re=$re a=$alpha Nc=$nc" > "$outfile"
        return
    fi

    diff_count=0
    max_ulp=0

    while IFS= read -r line; do
        side=$(echo "$line" | grep -oP 's=\K\d+')
        stn=$(echo "$line" | grep -oP 'i=\s*\K\d+')
        c_t=$(echo "$line" | grep -oP 'T=\K\w+')
        c_d=$(echo "$line" | grep -oP 'D=\K\w+')
        c_u=$(echo "$line" | grep -oP 'U=\K\w+')
        f_line=$(echo "$f_bl" | grep "s=$side i= *$stn " | head -1)
        if [ -z "$f_line" ]; then continue; fi
        f_t=$(echo "$f_line" | grep -oP 'T=\K\w+')
        f_d=$(echo "$f_line" | grep -oP 'D=\K\w+')
        f_u=$(echo "$f_line" | grep -oP 'U=\K\w+')
        if [ "$c_t" != "$f_t" ] || [ "$c_d" != "$f_d" ] || [ "$c_u" != "$f_u" ]; then
            diff_count=$((diff_count + 1))
            t_ulp=$(python3 -c "print(abs(int('$c_t',16)-int('$f_t',16)))")
            d_ulp=$(python3 -c "print(abs(int('$c_d',16)-int('$f_d',16)))")
            u_ulp=$(python3 -c "print(abs(int('$c_u',16)-int('$f_u',16)))")
            this_max=$(python3 -c "print(max($t_ulp,$d_ulp,$u_ulp))")
            if [ "$this_max" -gt "$max_ulp" ]; then max_ulp=$this_max; fi
        fi
    done <<< "$c_bl"

    matched=$((c_count - diff_count))
    if [ "$diff_count" -eq 0 ]; then
        echo "EXACT $naca Re=$re a=$alpha Nc=$nc ($matched/$c_count)" > "$outfile"
    elif [ "$max_ulp" -le 10 ]; then
        echo "NEAR  $naca Re=$re a=$alpha Nc=$nc ($matched/$c_count, max_ulp=$max_ulp)" > "$outfile"
    else
        echo "FAIL  $naca Re=$re a=$alpha Nc=$nc ($matched/$c_count, max_ulp=$max_ulp)" > "$outfile"
    fi
}

export -f check_case
export REPO_ROOT FORT_BIN COMPARE_PROJ RESULTS_DIR

idx=0
while IFS=' ' read -r naca re alpha nc cl_ref cd_ref; do
    idx=$((idx + 1))
    [ "$idx" -gt "$N_CASES" ] && break
    echo "$naca $re $alpha $nc $idx"
done < "$REPO_ROOT/tools/fortran-debug/reference/clean_fortran_polar_vectors_4875.txt" | \
    xargs -P"$N_PARALLEL" -L1 bash -c 'check_case $1 $2 $3 $4 $5' _

# Collect results
EXACT=0; NEAR=0; FAIL=0; SKIP=0; TOTAL=0
for f in "$RESULTS_DIR"/*.txt; do
    result=$(cat "$f")
    TOTAL=$((TOTAL + 1))
    case "$result" in
        EXACT*) EXACT=$((EXACT + 1)) ;;
        NEAR*)  NEAR=$((NEAR + 1)) ;;
        FAIL*)  FAIL=$((FAIL + 1)) ;;
        SKIP*)  SKIP=$((SKIP + 1)) ;;
    esac
    echo "$result"
done | sort

echo ""
echo "=== SUMMARY ==="
echo "Total: $TOTAL"
echo "Bit-exact: $EXACT"
echo "Near (≤10 ULP): $NEAR"
echo "Fail (>10 ULP): $FAIL"
echo "Skip: $SKIP"

rm -rf "$RESULTS_DIR"
