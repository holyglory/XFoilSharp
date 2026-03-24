#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$REPO_ROOT/tools/bootstrap-env.sh"
bootstrap_xfoilsharp_env

BUILD_DIR="$SCRIPT_DIR/build-hst-driver"
DRIVER_SRC="$SCRIPT_DIR/hst_parity_driver.f"
OUTPUT_BIN="$BUILD_DIR/hst_parity_driver"

if ! command -v gfortran >/dev/null 2>&1; then
    echo "ERROR: gfortran not found on PATH" >&2
    exit 1
fi

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

gfortran -std=legacy -O2 -ffixed-line-length-none "$DRIVER_SRC" -o "$OUTPUT_BIN"
echo "$OUTPUT_BIN"
