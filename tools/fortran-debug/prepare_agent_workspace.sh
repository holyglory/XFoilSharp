#!/bin/bash
# prepare_agent_workspace.sh -- Create an isolated Fortran/debug sandbox for one parallel scout.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BASE_BUILD_DIR="$SCRIPT_DIR/build"
AGENT_NAME=""

usage() {
    cat <<'EOF'
Usage: prepare_agent_workspace.sh --agent <name>

Creates:
  tools/fortran-debug/agents/<name>/workspace
  tools/fortran-debug/agents/<name>/build
  tools/fortran-debug/agents/<name>/reference
  tools/fortran-debug/agents/<name>/csharp
  tools/fortran-debug/agents/<name>/managed-artifacts

The build directory is copied from tools/fortran-debug/build so the caller gets a
separate executable and scratch directory. The workspace directory is an rsynced
snapshot of the current repo so parallel scouts do not share source or obj/bin
trees with the live checkout.
EOF
}

sync_workspace_snapshot() {
    local source_root="$1"
    local workspace_root="$2"

    mkdir -p "$workspace_root"
    rsync -a --delete \
        --exclude='.git/' \
        --exclude='.DS_Store' \
        --exclude='bin/' \
        --exclude='obj/' \
        --exclude='tools/fortran-debug/agents/' \
        --exclude='tools/fortran-debug/build/' \
        --exclude='tools/fortran-debug/build-check/' \
        --exclude='tools/fortran-debug/build-isolate-*/' \
        --exclude='tools/fortran-debug/csharp/' \
        --exclude='tools/fortran-debug/reference/' \
        --exclude='tools/fortran-debug/*.jsonl' \
        --exclude='tools/fortran-debug/*_dump.txt' \
        --exclude='tools/fortran-debug/*_trace.jsonl' \
        --exclude='tools/fortran-debug/trace_counter.txt' \
        --exclude='f_xfoil/.git/' \
        "$source_root/" \
        "$workspace_root/"
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --agent)
            AGENT_NAME="${2:?missing agent name}"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "ERROR: unknown argument '$1'"
            usage
            exit 1
            ;;
    esac
done

if [ -z "$AGENT_NAME" ]; then
    echo "ERROR: --agent is required."
    usage
    exit 1
fi

if [ ! -d "$BASE_BUILD_DIR" ]; then
    echo "ERROR: base build directory not found: $BASE_BUILD_DIR"
    echo "Run tools/fortran-debug/build_debug.sh first."
    exit 1
fi

SANDBOX_ROOT="$SCRIPT_DIR/agents/$AGENT_NAME"
SANDBOX_WORKSPACE="$SANDBOX_ROOT/workspace"
SANDBOX_BUILD="$SANDBOX_ROOT/build"
SANDBOX_REFERENCE="$SANDBOX_ROOT/reference"
SANDBOX_CSHARP="$SANDBOX_ROOT/csharp"
SANDBOX_MANAGED_ARTIFACTS="$SANDBOX_ROOT/managed-artifacts"
TRACE_COUNTER_PATH="$SANDBOX_ROOT/trace_counter.txt"

mkdir -p "$SANDBOX_ROOT"
sync_workspace_snapshot "$REPO_ROOT" "$SANDBOX_WORKSPACE"
rm -rf "$SANDBOX_BUILD"
rm -rf "$SANDBOX_REFERENCE"
rm -rf "$SANDBOX_CSHARP"
rm -rf "$SANDBOX_MANAGED_ARTIFACTS"
cp -R "$BASE_BUILD_DIR" "$SANDBOX_BUILD"
mkdir -p "$SANDBOX_REFERENCE" "$SANDBOX_CSHARP" "$SANDBOX_MANAGED_ARTIFACTS"
if [ ! -f "$TRACE_COUNTER_PATH" ]; then
    printf '0\n' > "$TRACE_COUNTER_PATH"
fi

echo "sandbox_root=$SANDBOX_ROOT"
echo "workspace_dir=$SANDBOX_WORKSPACE"
echo "build_dir=$SANDBOX_BUILD"
echo "reference_dir=$SANDBOX_REFERENCE"
echo "csharp_dir=$SANDBOX_CSHARP"
echo "managed_artifacts_dir=$SANDBOX_MANAGED_ARTIFACTS"
echo "trace_counter_path=$TRACE_COUNTER_PATH"
