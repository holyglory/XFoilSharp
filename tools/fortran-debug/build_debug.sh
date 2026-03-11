#!/bin/bash
# build_debug.sh -- Build instrumented XFoil binary with debug WRITE statements
# This script copies original source, overwrites xbl.f/xoper.f/xsolve.f with
# instrumented versions, and compiles a debug binary.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
SRC_DIR="$REPO_ROOT/f_xfoil/src"
BUILD_DIR="$SCRIPT_DIR/build"
PLOTLIB="$REPO_ROOT/f_xfoil/build/plotlib/libplt.a"

echo "=== Building instrumented XFoil debug binary ==="
echo "Source dir: $SRC_DIR"
echo "Build dir:  $BUILD_DIR"

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

echo "Source files copied and patched."

# Compile all source files matching the cmake XFOIL_SRCS list
# Using the same flags as the cmake build: -std=legacy -O2
cd "$BUILD_DIR"

# Source file list from CMakeLists.txt
SRCS="xfoil.f xpanel.f xoper.f xtcam.f xgdes.f xqdes.f xmdes.f \
      xsolve.f xbl.f xblsys.f xpol.f xplots.f pntops.f \
      xgeom.f xutils.f modify.f blplot.f polplt.f aread.f naca.f \
      spline.f plutil.f iopol.f gui.f sort.f dplot.f \
      profil.f userio.f frplot0.f"

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
gfortran -o xfoil_debug $OBJ_FILES "$PLOTLIB" -lX11

echo "=== Build successful ==="
echo "Binary: $BUILD_DIR/xfoil_debug"
ls -la "$BUILD_DIR/xfoil_debug"
