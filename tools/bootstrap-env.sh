#!/usr/bin/env bash
set -euo pipefail

bootstrap_xfoilsharp_env() {
    local tool_paths=(
        "$HOME/.dotnet"
        "$HOME/.local/bin"
        "$HOME/.local/xfoilsharp-env/bin"
    )

    export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
    export MAMBA_ROOT_PREFIX="${MAMBA_ROOT_PREFIX:-$HOME/.local/share/mamba}"

    for dir in "${tool_paths[@]}"; do
        if [[ -d "$dir" && ":$PATH:" != *":$dir:"* ]]; then
            PATH="$dir:$PATH"
        fi
    done
    export PATH

    if [[ -z "${CONDA_PREFIX:-}" && -d "$HOME/.local/xfoilsharp-env" ]]; then
        export CONDA_PREFIX="$HOME/.local/xfoilsharp-env"
    fi

    if [[ -n "${CONDA_PREFIX:-}" ]]; then
        if [[ -d "$CONDA_PREFIX/lib" && ":${DYLD_FALLBACK_LIBRARY_PATH:-}:" != *":$CONDA_PREFIX/lib:"* ]]; then
            export DYLD_FALLBACK_LIBRARY_PATH="$CONDA_PREFIX/lib${DYLD_FALLBACK_LIBRARY_PATH:+:$DYLD_FALLBACK_LIBRARY_PATH}"
        fi

        if [[ -d "$CONDA_PREFIX/lib/pkgconfig" && ":${PKG_CONFIG_PATH:-}:" != *":$CONDA_PREFIX/lib/pkgconfig:"* ]]; then
            export PKG_CONFIG_PATH="$CONDA_PREFIX/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
        fi
    fi

    if [[ "$(uname -s)" == "Darwin" ]] && command -v xcrun >/dev/null 2>&1 && [[ -z "${SDKROOT:-}" ]]; then
        export SDKROOT
        SDKROOT="$(xcrun --show-sdk-path)"
    fi
}

find_fortran_reference_build_dir() {
    local repo_root="$1"
    local candidates=(
        "$repo_root/f_xfoil/build-user-x11"
        "$repo_root/f_xfoil/build"
    )

    local dir
    for dir in "${candidates[@]}"; do
        if [[ -f "$dir/plotlib/libplt.a" ]]; then
            printf '%s\n' "$dir"
            return 0
        fi
    done

    find "$repo_root/f_xfoil" -maxdepth 3 -type f -name libplt.a -print 2>/dev/null \
        | sed 's#/plotlib/libplt\.a$##' \
        | head -n 1
}
