#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
SRC_DIR="$REPO_ROOT/f_xfoil/src"
BUILD_DIR="$SCRIPT_DIR/build-micro-drivers"
source "$REPO_ROOT/tools/bootstrap-env.sh"
bootstrap_xfoilsharp_env

if ! command -v gfortran >/dev/null 2>&1; then
    echo "ERROR: gfortran not found on PATH" >&2
    exit 1
fi

# Free-form Fortran (.f90) may exceed the default 132-column limit on some
# gfortran versions that treat line-truncation as an error.  Remove the limit.
# -march=native enables FMA on capable CPUs, matching the parity-critical
# contraction behavior the managed C# code expects (MathF.FusedMultiplyAdd).
# When XFOIL_DISABLE_FMA=1, compile with -O0 -ffp-contract=off -march=x86-64
# to eliminate ALL FMA/contraction artifacts for debugging real parity bugs.
if [[ "${XFOIL_DISABLE_FMA:-0}" == "1" ]]; then
    F90FLAGS="-O0 -ffp-contract=off -march=x86-64 -ffree-line-length-none"
    FIXED_EXTRA="-O0 -ffp-contract=off -march=x86-64"
else
    F90FLAGS="-O2 -ffree-line-length-none -march=native"
    FIXED_EXTRA="-O2 -march=native"
fi

if [[ $# -eq 0 ]]; then
    set -- gauss psilin cf cq dil diwall diouter diturb didfac pswlinhalf
fi

mkdir -p "$BUILD_DIR"
cp "$SRC_DIR"/*.INC "$BUILD_DIR/" 2>/dev/null || true

compile_gauss() {
    (
        cd "$BUILD_DIR"
        gfortran -std=legacy $FIXED_EXTRA -ffixed-line-length-none -I"$BUILD_DIR" -c "$SCRIPT_DIR/xsolve_debug.f" -o xsolve_debug.o
        gfortran $F90FLAGS -c "$SCRIPT_DIR/gauss_trace_stub.f90" -o gauss_trace_stub.o
        gfortran $F90FLAGS "$SCRIPT_DIR/gauss_parity_driver.f90" xsolve_debug.o gauss_trace_stub.o -o gauss_parity_driver
    )
}

compile_psilin() {
    cp "$SRC_DIR/xpanel.f" "$BUILD_DIR/"
    (
        cd "$BUILD_DIR"
        # -march=native enables FMA so FMAF_REAL calls in xpanel.f emit hardware vfmadd.
        gfortran -std=legacy $FIXED_EXTRA -ffixed-line-length-none -c xpanel.f -o xpanel.o
        gfortran $F90FLAGS -c "$SCRIPT_DIR/xpanel_microtrace_stubs.f90" -o xpanel_microtrace_stubs.o
        gfortran -std=legacy $FIXED_EXTRA -ffixed-line-length-none -I"$BUILD_DIR" "$SCRIPT_DIR/psilin_parity_driver.f" xpanel.o xpanel_microtrace_stubs.o -o psilin_parity_driver
    )
}

compile_cf() {
    (
        cd "$BUILD_DIR"
        gfortran $F90FLAGS -c "$SCRIPT_DIR/bl_common_kernels.f90" -o bl_common_kernels.o
        gfortran $F90FLAGS -I"$BUILD_DIR" "$SCRIPT_DIR/cf_parity_driver.f90" bl_common_kernels.o -o cf_parity_driver
    )
}

compile_cq() {
    gfortran $F90FLAGS "$SCRIPT_DIR/cq_parity_driver.f90" -o "$BUILD_DIR/cq_parity_driver"
}

compile_dil() {
    gfortran $F90FLAGS "$SCRIPT_DIR/dil_parity_driver.f90" -o "$BUILD_DIR/dil_parity_driver"
}

compile_diwall() {
    (
        cd "$BUILD_DIR"
        gfortran $F90FLAGS -c "$SCRIPT_DIR/bl_common_kernels.f90" -o bl_common_kernels.o
        gfortran $F90FLAGS -I"$BUILD_DIR" "$SCRIPT_DIR/di_wall_parity_driver.f90" bl_common_kernels.o -o di_wall_parity_driver
    )
}

compile_diouter() {
    gfortran $F90FLAGS "$SCRIPT_DIR/di_outer_parity_driver.f90" -o "$BUILD_DIR/di_outer_parity_driver"
}

compile_diturb() {
    (
        cd "$BUILD_DIR"
        gfortran $F90FLAGS -c "$SCRIPT_DIR/bl_common_kernels.f90" -o bl_common_kernels.o
        gfortran $F90FLAGS -I"$BUILD_DIR" "$SCRIPT_DIR/di_turbulent_parity_driver.f90" bl_common_kernels.o -o di_turbulent_parity_driver
    )
}

compile_didfac() {
    (
        cd "$BUILD_DIR"
        gfortran $F90FLAGS -c "$SCRIPT_DIR/bl_common_kernels.f90" -o bl_common_kernels.o
        gfortran $F90FLAGS -I"$BUILD_DIR" "$SCRIPT_DIR/di_dfac_parity_driver.f90" bl_common_kernels.o -o di_dfac_parity_driver
    )
}

compile_pswlinhalf() {
    gfortran $F90FLAGS "$SCRIPT_DIR/pswlin_half_parity_driver.f90" -o "$BUILD_DIR/pswlin_half_parity_driver"
}

for target in "$@"; do
    case "$target" in
        gauss) compile_gauss ;;
        psilin) compile_psilin ;;
        cf) compile_cf ;;
        cq) compile_cq ;;
        dil) compile_dil ;;
        diwall) compile_diwall ;;
        diouter) compile_diouter ;;
        diturb) compile_diturb ;;
        didfac) compile_didfac ;;
        pswlinhalf) compile_pswlinhalf ;;
        *)
            echo "ERROR: unsupported micro-driver target '$target'" >&2
            exit 1
            ;;
    esac
done

echo "$BUILD_DIR"
