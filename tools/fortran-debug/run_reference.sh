#!/bin/bash
# run_reference.sh -- Run instrumented XFoil on reference case and capture dump
# Reference case: NACA 0012, Re=1e6, alpha=0, 160 panels (default), NCrit=9 (default)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/build"
BINARY="$BUILD_DIR/xfoil_debug"

echo "=== Running reference case ==="
echo "Binary: $BINARY"

if [ ! -f "$BINARY" ]; then
    echo "ERROR: Binary not found. Run build_debug.sh first."
    exit 1
fi

cd "$BUILD_DIR"

# Create piped input for XFoil interactive commands
# NACA 0012, PANE for 160 panels, OPER mode, VISC at Re=1e6, 20 iterations, alpha=0
cat > xfoil_input.txt << 'XFOILINPUT'
NACA 0012
PANE
OPER
VISC 1000000
ITER 20
ALFA 0

QUIT
XFOILINPUT

echo "Running XFoil with reference case input..."
# Run XFoil with piped input; allow non-zero exit (XFoil may exit oddly)
./xfoil_debug < xfoil_input.txt > xfoil_stdout.txt 2>&1 || true

echo "XFoil run complete."

# Check for debug dump file
if [ -f "$BUILD_DIR/debug_dump.txt" ]; then
    echo "Debug dump file found."
    cp "$BUILD_DIR/debug_dump.txt" "$SCRIPT_DIR/reference_dump.txt"
    echo "Copied to $SCRIPT_DIR/reference_dump.txt"

    # Validate dump contents
    echo ""
    echo "=== Dump validation ==="
    echo -n "File size: "
    wc -c < "$SCRIPT_DIR/reference_dump.txt"
    echo -n "Line count: "
    wc -l < "$SCRIPT_DIR/reference_dump.txt"

    echo -n "ITER markers: "
    grep -c '=== ITER' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo -n "STATION lines: "
    grep -c 'STATION IS=' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo -n "POST_UPDATE lines: "
    grep -c 'POST_UPDATE' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo -n "POST_CALC lines: "
    grep -c 'POST_CALC' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo -n "CONVERGED lines: "
    grep -c 'CONVERGED' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo -n "BLSOLV_POST_FORWARD lines: "
    grep -c 'BLSOLV_POST_FORWARD' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo -n "VDEL_SOL lines: "
    grep -c 'VDEL_SOL' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo -n "BL_STATE lines: "
    grep -c 'BL_STATE' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo -n "TRANSITION lines: "
    grep -c 'TRANSITION' "$SCRIPT_DIR/reference_dump.txt" || echo "0"

    echo ""
    echo "=== First 30 lines of dump ==="
    head -30 "$SCRIPT_DIR/reference_dump.txt"

    echo ""
    echo "=== Last 10 lines of dump ==="
    tail -10 "$SCRIPT_DIR/reference_dump.txt"
else
    echo "ERROR: debug_dump.txt not found in $BUILD_DIR"
    echo "XFoil stdout:"
    cat "$BUILD_DIR/xfoil_stdout.txt"
    exit 1
fi

echo ""
echo "=== Reference case complete ==="
