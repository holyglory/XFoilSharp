#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$repo_root/tools/bootstrap-env.sh"
bootstrap_xfoilsharp_env

echo "DOTNET_ROOT=${DOTNET_ROOT:-}"
echo "CONDA_PREFIX=${CONDA_PREFIX:-}"
echo "SDKROOT=${SDKROOT:-}"
echo "PATH=$PATH"
echo

echo "== dotnet =="
command -v dotnet
dotnet --list-sdks
echo

echo "== cmake =="
command -v cmake
cmake --version | head -n 1
echo

echo "== gfortran =="
command -v gfortran
gfortran --version | head -n 1
echo

echo "== make =="
command -v make
make --version | head -n 1
echo

echo "== submodule =="
git -C "$repo_root" submodule status

echo
echo "== reference build =="
reference_build_dir="$(find_fortran_reference_build_dir "$repo_root" || true)"
if [[ -n "$reference_build_dir" ]]; then
    echo "BUILD_DIR=$reference_build_dir"
    if [[ -x "$reference_build_dir/src/xfoil" ]]; then
        echo "XFOIL_BINARY=$reference_build_dir/src/xfoil"
    elif [[ -x "$reference_build_dir/src/xfoil-6.97" ]]; then
        echo "XFOIL_BINARY=$reference_build_dir/src/xfoil-6.97"
    else
        echo "XFOIL_BINARY=missing"
    fi
else
    echo "BUILD_DIR=missing"
fi
