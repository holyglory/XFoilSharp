#!/bin/bash
# build_debug.sh -- Build instrumented XFoil binary with debug WRITE statements
# This script copies original source, overwrites xbl.f/xoper.f/xsolve.f with
# instrumented versions, and compiles a debug binary.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$REPO_ROOT/tools/bootstrap-env.sh"
bootstrap_xfoilsharp_env

SRC_DIR="$REPO_ROOT/f_xfoil/src"
BUILD_DIR="$SCRIPT_DIR/build"
REFERENCE_BUILD_DIR="${REFERENCE_BUILD_DIR:-$(find_fortran_reference_build_dir "$REPO_ROOT")}"
PLOTLIB="$REFERENCE_BUILD_DIR/plotlib/libplt.a"
X11_LIB_DIR="${X11_LIB_DIR:-${CONDA_PREFIX:-}/lib}"

echo "=== Building instrumented XFoil debug binary ==="
echo "Source dir: $SRC_DIR"
echo "Build dir:  $BUILD_DIR"
echo "Ref build:  $REFERENCE_BUILD_DIR"
echo "Plot lib:   $PLOTLIB"

if [[ ! -d "$SRC_DIR" ]]; then
    echo "ERROR: XFoil source directory not found: $SRC_DIR"
    exit 1
fi

if [[ ! -f "$PLOTLIB" ]]; then
    echo "ERROR: plotlib archive not found: $PLOTLIB"
    echo "Build the reference Fortran tree first."
    exit 1
fi

if ! command -v gfortran >/dev/null 2>&1; then
    echo "ERROR: gfortran not found on PATH"
    exit 1
fi

# Clean and create build directory
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

# Copy ALL source files from f_xfoil/src/ into build dir
cp "$SRC_DIR"/*.f "$BUILD_DIR/"
cp "$SRC_DIR"/*.INC "$BUILD_DIR/"

# Overwrite with instrumented versions
cp "$SCRIPT_DIR/xbl_debug.f"    "$BUILD_DIR/xbl.f"
cp "$SCRIPT_DIR/xoper_debug.f"  "$BUILD_DIR/xoper.f"
cp "$SCRIPT_DIR/xsolve_debug.f" "$BUILD_DIR/xsolve.f"
cp "$SCRIPT_DIR/json_trace.f"   "$BUILD_DIR/json_trace.f"

echo "Source files copied and patched."

echo "Auditing fixed-form line lengths..."
python3 /Users/slava/Agents/XFoilSharp/tools/fortran_fixed_form_audit.py "$BUILD_DIR"

echo "Auditing trace-call scalar arguments..."
suspicious_trace_args="$(rg -n -P 'TRACE_[A-Z0-9_]+\([^\n]*\bW[0-9]\b(?!\s*\()' "$BUILD_DIR"/*.f || true)"
if [[ -n "$suspicious_trace_args" ]]; then
    echo "ERROR: suspicious bare Wn trace-call arguments found:"
    echo "$suspicious_trace_args"
    exit 1
fi

# Compile all source files matching the cmake XFOIL_SRCS list
# Using the same flags as the cmake build: -std=legacy -O2
cd "$BUILD_DIR"

# Source file list from CMakeLists.txt
SRCS="xfoil.f xpanel.f xoper.f xtcam.f xgdes.f xqdes.f xmdes.f \
      xsolve.f xbl.f xblsys.f xpol.f xplots.f pntops.f \
      xgeom.f xutils.f modify.f blplot.f polplt.f aread.f naca.f \
      spline.f plutil.f iopol.f gui.f sort.f dplot.f \
      profil.f userio.f frplot0.f json_trace.f"

echo "Compiling source files..."
for f in $SRCS; do
    echo "  Compiling $f"
    gfortran -std=legacy -O2 -c "$f" -o "${f%.f}.o"
done

echo "Linking..."
# Collect all .o files
OBJ_FILES=""
for f in $SRCS; do
    OBJ_FILES="$OBJ_FILES ${f%.f}.o"
done

# Link with plotlib and X11
LINK_FLAGS=()
if [[ -n "$X11_LIB_DIR" && -d "$X11_LIB_DIR" ]]; then
    LINK_FLAGS+=("-L$X11_LIB_DIR" "-Wl,-rpath,$X11_LIB_DIR")
fi

gfortran -o xfoil_debug $OBJ_FILES "$PLOTLIB" "${LINK_FLAGS[@]}" -lX11

echo "=== Build successful ==="
echo "Binary: $BUILD_DIR/xfoil_debug"
ls -la "$BUILD_DIR/xfoil_debug"
