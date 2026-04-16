#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$REPO_ROOT/tools/bootstrap-env.sh"
bootstrap_xfoilsharp_env

SRC_DIR="$REPO_ROOT/f_xfoil/src"
BUILD_DIR="$SCRIPT_DIR/build-spline-driver"

if ! command -v gfortran >/dev/null 2>&1; then
    echo "ERROR: gfortran not found on PATH" >&2
    exit 1
fi

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

cp "$SRC_DIR"/spline.f "$BUILD_DIR/"
cp "$SCRIPT_DIR"/spline_parity_driver.f90 "$BUILD_DIR/"
cp "$SCRIPT_DIR"/segmented_spline_parity_driver.f "$BUILD_DIR/"
cp "$SCRIPT_DIR"/spline_trace_stub.f "$BUILD_DIR/"

(
    cd "$BUILD_DIR"
    # When XFOIL_DISABLE_FMA=1, compile without FMA for debugging real parity bugs.
    if [[ "${XFOIL_DISABLE_FMA:-0}" == "1" ]]; then
        FFLAGS="-O0 -ffp-contract=off -march=x86-64"
    else
        FFLAGS="-O2 -march=native"
    fi
    gfortran -std=legacy $FFLAGS -ffixed-line-length-none -c spline.f -o spline.o
    gfortran -std=legacy $FFLAGS -ffixed-line-length-none -c spline_trace_stub.f -o spline_trace_stub.o
    gfortran $FFLAGS -ffree-line-length-none spline_parity_driver.f90 spline.o spline_trace_stub.o -o spline_parity_driver
    gfortran -std=legacy $FFLAGS -ffixed-line-length-none segmented_spline_parity_driver.f spline.o spline_trace_stub.o -o segmented_spline_parity_driver
)

echo "$BUILD_DIR"
