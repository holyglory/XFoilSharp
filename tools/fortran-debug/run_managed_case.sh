#!/bin/bash
# run_managed_case.sh -- Run a focused managed parity case with ad hoc airfoil/Re/alpha/panel/Ncrit settings.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$REPO_ROOT/tools/bootstrap-env.sh"
bootstrap_xfoilsharp_env

DOTNET_BIN="${DOTNET_BIN:-/Users/slava/.dotnet/dotnet}"
if [ ! -x "$DOTNET_BIN" ]; then
    DOTNET_BIN="$(command -v dotnet)"
fi

TEST_PROJECT="$REPO_ROOT/tests/XFoil.Core.Tests/XFoil.Core.Tests.csproj"
TEST_FILTER="FullyQualifiedName~AdHocArtifactRefreshTests.RefreshManagedArtifacts_FromEnvironment"

CASE_ID=""
AIRFOIL=""
REYNOLDS=""
ALPHA=""
PANELS="60"
MAX_ITER="80"
NCRIT="9"
MAX_TRACE_MB="${XFOIL_MAX_TRACE_MB:-100}"
SUMMARY_ONLY="0"
FORCE_TRACE="0"
LIVE_COMPARE="0"
LIVE_COMPARE_REFERENCE=""
ROUTE_DISPARITY="0"
OUTPUT_DIR=""
REFERENCE_OUTPUT_DIR=""
ARTIFACTS_PATH=""
TRACE_COUNTER_PATH=""

clear_trace_focus_env() {
    unset XFOIL_TRACE_KIND_ALLOW
    unset XFOIL_TRACE_SCOPE_ALLOW
    unset XFOIL_TRACE_NAME_ALLOW
    unset XFOIL_TRACE_DATA_MATCH
    unset XFOIL_TRACE_SIDE
    unset XFOIL_TRACE_STATION
    unset XFOIL_TRACE_ITERATION
    unset XFOIL_TRACE_ITER_MIN
    unset XFOIL_TRACE_ITER_MAX
    unset XFOIL_TRACE_MODE
    unset XFOIL_TRACE_TRIGGER_KIND
    unset XFOIL_TRACE_TRIGGER_SCOPE
    unset XFOIL_TRACE_TRIGGER_NAME_ALLOW
    unset XFOIL_TRACE_TRIGGER_DATA_MATCH
    unset XFOIL_TRACE_TRIGGER_OCCURRENCE
    unset XFOIL_TRACE_TRIGGER_SIDE
    unset XFOIL_TRACE_TRIGGER_STATION
    unset XFOIL_TRACE_TRIGGER_ITERATION
    unset XFOIL_TRACE_TRIGGER_ITER_MIN
    unset XFOIL_TRACE_TRIGGER_ITER_MAX
    unset XFOIL_TRACE_TRIGGER_MODE
    unset XFOIL_TRACE_RING_BUFFER
    unset XFOIL_TRACE_POST_LIMIT
}

has_trace_focus_env() {
    local vars=(
        XFOIL_TRACE_KIND_ALLOW
        XFOIL_TRACE_SCOPE_ALLOW
        XFOIL_TRACE_NAME_ALLOW
        XFOIL_TRACE_DATA_MATCH
        XFOIL_TRACE_SIDE
        XFOIL_TRACE_STATION
        XFOIL_TRACE_ITERATION
        XFOIL_TRACE_ITER_MIN
        XFOIL_TRACE_ITER_MAX
        XFOIL_TRACE_MODE
        XFOIL_TRACE_TRIGGER_KIND
        XFOIL_TRACE_TRIGGER_SCOPE
        XFOIL_TRACE_TRIGGER_NAME_ALLOW
        XFOIL_TRACE_TRIGGER_DATA_MATCH
        XFOIL_TRACE_TRIGGER_OCCURRENCE
        XFOIL_TRACE_TRIGGER_SIDE
        XFOIL_TRACE_TRIGGER_STATION
        XFOIL_TRACE_TRIGGER_ITERATION
        XFOIL_TRACE_TRIGGER_ITER_MIN
        XFOIL_TRACE_TRIGGER_ITER_MAX
        XFOIL_TRACE_TRIGGER_MODE
        XFOIL_TRACE_RING_BUFFER
        XFOIL_TRACE_POST_LIMIT
    )

    local var
    for var in "${vars[@]}"; do
        if [ -n "${!var:-}" ]; then
            return 0
        fi
    done

    return 1
}

sanitize_token() {
    printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | tr '.-' 'pm' | tr -cd '[:alnum:]_'
}

resolve_path() {
    python3 - <<'PY' "$REPO_ROOT" "$1"
from pathlib import Path
import sys

repo_root = Path(sys.argv[1])
raw = sys.argv[2]
path = Path(raw)
if not path.is_absolute():
    path = repo_root / path
print(path.resolve())
PY
}

latest_versioned_match() {
    python3 - <<'PY' "$1" "$2"
from pathlib import Path
import re
import sys

directory = Path(sys.argv[1])
pattern = sys.argv[2]
if not directory.is_dir():
    raise SystemExit(0)

suffix_re = re.compile(r'\.(\d+)(?=\.[^.]+$)')
best = None
for path in directory.glob(pattern):
    if not path.is_file():
        continue

    match = suffix_re.search(path.name)
    version = int(match.group(1)) if match else -1
    key = (path.stat().st_mtime_ns, version, path.name)
    if best is None or key > best[0]:
        best = (key, path)

if best is not None:
    print(best[1])
PY
}

usage() {
    cat <<'EOF'
Usage: run_managed_case.sh --airfoil <code> --re <value> --alpha <deg> [options]

Options:
  --case-id <id>       Output case id under tools/fortran-debug/csharp (default: derived from parameters)
  --airfoil <code>     NACA 4-digit code, e.g. 0012
  --re <value>         Reynolds number
  --alpha <deg>        Angle of attack in degrees
  --panels <count>     Panel count (default: 60)
  --iter <count>       Max viscous iterations (default: 80)
  --ncrit <value>      Ncrit / critical amplification factor (default: 9)
  --output-dir <dir>   Managed artifact directory override
  --reference-output-dir <dir>
                        Reference dump directory override used for automatic final-gap reporting
  --artifacts-path <dir>
                        Build/test outputs directory override for an isolated managed executable
  --live-compare        Compare managed trace events against the selected reference trace and stop on the first mismatch
  --live-compare-reference <path>
                        Explicit reference trace path to use for live compare
  --route-disparity    Route the resulting parity_report to the responsible micro-rig and run that rig in quick mode
  --trace-counter-path <file>
                        Counter file override so isolated sandboxes do not share numbering
  --max-trace-mb <mb>  Fail and delete the generated JSON trace if it exceeds this size (default: 100)
  --summary-only       Suppress trace capture and produce only dumps plus tiny session markers
  --full-trace         Force full persisted trace capture even during live compare

Trace filtering is controlled by the existing XFOIL_TRACE_* and XFOIL_TRACE_TRIGGER_* environment variables.
Use XFOIL_TRACE_NAME_ALLOW and XFOIL_TRACE_DATA_MATCH for exact record-name / data-field filters.
Data matches are semicolon-separated, for example: context=basis_gamma_alpha0_single;phase=forward;row=31
Use XFOIL_TRACE_POST_LIMIT with XFOIL_TRACE_RING_BUFFER to keep only a short tail after the trigger.
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --case-id)
            CASE_ID="${2:?missing case id}"
            shift 2
            ;;
        --airfoil)
            AIRFOIL="${2:?missing airfoil code}"
            shift 2
            ;;
        --re)
            REYNOLDS="${2:?missing Reynolds number}"
            shift 2
            ;;
        --alpha)
            ALPHA="${2:?missing angle of attack}"
            shift 2
            ;;
        --panels)
            PANELS="${2:?missing panel count}"
            shift 2
            ;;
        --iter)
            MAX_ITER="${2:?missing iteration count}"
            shift 2
            ;;
        --ncrit)
            NCRIT="${2:?missing Ncrit}"
            shift 2
            ;;
        --output-dir)
            OUTPUT_DIR="${2:?missing output directory}"
            shift 2
            ;;
        --reference-output-dir)
            REFERENCE_OUTPUT_DIR="${2:?missing reference output directory}"
            shift 2
            ;;
        --artifacts-path)
            ARTIFACTS_PATH="${2:?missing artifacts path}"
            shift 2
            ;;
        --live-compare)
            LIVE_COMPARE="1"
            shift
            ;;
        --live-compare-reference)
            LIVE_COMPARE_REFERENCE="${2:?missing live compare reference path}"
            shift 2
            ;;
        --route-disparity)
            ROUTE_DISPARITY="1"
            shift
            ;;
        --trace-counter-path)
            TRACE_COUNTER_PATH="${2:?missing trace counter path}"
            shift 2
            ;;
        --max-trace-mb)
            MAX_TRACE_MB="${2:?missing max trace size}"
            shift 2
            ;;
        --summary-only)
            SUMMARY_ONLY="1"
            shift
            ;;
        --full-trace)
            FORCE_TRACE="1"
            shift
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

if [ -z "$AIRFOIL" ] || [ -z "$REYNOLDS" ] || [ -z "$ALPHA" ]; then
    echo "ERROR: --airfoil, --re, and --alpha are required."
    usage
    exit 1
fi

if [ -z "$CASE_ID" ]; then
    CASE_ID="adhoc_n$(sanitize_token "$AIRFOIL")_re$(sanitize_token "$REYNOLDS")_a$(sanitize_token "$ALPHA")_p$(sanitize_token "$PANELS")_n$(sanitize_token "$NCRIT")"
fi

if [ -n "$OUTPUT_DIR" ]; then
    OUTPUT_DIR="$(resolve_path "$OUTPUT_DIR")"
fi

if [ -n "$REFERENCE_OUTPUT_DIR" ]; then
    REFERENCE_OUTPUT_DIR="$(resolve_path "$REFERENCE_OUTPUT_DIR")"
fi

if [ -n "$ARTIFACTS_PATH" ]; then
    ARTIFACTS_PATH="$(resolve_path "$ARTIFACTS_PATH")"
fi

if [ -n "$LIVE_COMPARE_REFERENCE" ]; then
    LIVE_COMPARE_REFERENCE="$(resolve_path "$LIVE_COMPARE_REFERENCE")"
fi

if [ -n "$TRACE_COUNTER_PATH" ]; then
    TRACE_COUNTER_PATH="$(resolve_path "$TRACE_COUNTER_PATH")"
fi

REFERENCE_DIR="${REFERENCE_OUTPUT_DIR:-$REPO_ROOT/tools/fortran-debug/reference/$CASE_ID}"
REFERENCE_TRACE=""
if [ -d "$REFERENCE_DIR" ]; then
    REFERENCE_TRACE="$(latest_versioned_match "$REFERENCE_DIR" 'reference_trace.*.jsonl')"
    if [ -z "$REFERENCE_TRACE" ] && [ -f "$REFERENCE_DIR/reference_trace.jsonl" ]; then
        REFERENCE_TRACE="$REFERENCE_DIR/reference_trace.jsonl"
    fi
fi

export XFOIL_CASE_ID="$CASE_ID"
export XFOIL_CASE_AIRFOIL="$AIRFOIL"
export XFOIL_CASE_RE="$REYNOLDS"
export XFOIL_CASE_ALPHA="$ALPHA"
export XFOIL_CASE_PANELS="$PANELS"
export XFOIL_CASE_ITER="$MAX_ITER"
export XFOIL_CASE_NCRIT="$NCRIT"
export XFOIL_MAX_TRACE_MB="$MAX_TRACE_MB"
if [ "$FORCE_TRACE" = "1" ]; then
    export XFOIL_TRACE_FULL="1"
else
    unset XFOIL_TRACE_FULL
fi
if [ -n "$OUTPUT_DIR" ]; then
    export XFOIL_CASE_OUTPUT_DIR="$OUTPUT_DIR"
else
    unset XFOIL_CASE_OUTPUT_DIR
fi
if [ -n "$REFERENCE_OUTPUT_DIR" ]; then
    export XFOIL_REFERENCE_OUTPUT_DIR="$REFERENCE_OUTPUT_DIR"
else
    unset XFOIL_REFERENCE_OUTPUT_DIR
fi
if [ "$LIVE_COMPARE" = "1" ]; then
    if [ -n "$LIVE_COMPARE_REFERENCE" ]; then
        if [ ! -f "$LIVE_COMPARE_REFERENCE" ]; then
            echo "ERROR: live compare reference trace '$LIVE_COMPARE_REFERENCE' does not exist."
            exit 1
        fi
    elif [ -z "$REFERENCE_TRACE" ]; then
        echo "ERROR: --live-compare requires an existing reference trace in '$REFERENCE_DIR' or an explicit --live-compare-reference path."
        exit 1
    fi
    export XFOIL_LIVE_COMPARE_ENABLED="1"
else
    unset XFOIL_LIVE_COMPARE_ENABLED
fi
if [ -n "$LIVE_COMPARE_REFERENCE" ]; then
    export XFOIL_LIVE_COMPARE_REFERENCE_TRACE="$LIVE_COMPARE_REFERENCE"
elif [ "$LIVE_COMPARE" = "1" ] && [ -n "$REFERENCE_TRACE" ]; then
    export XFOIL_LIVE_COMPARE_REFERENCE_TRACE="$REFERENCE_TRACE"
else
    unset XFOIL_LIVE_COMPARE_REFERENCE_TRACE
fi
if [ -n "$TRACE_COUNTER_PATH" ]; then
    export XFOIL_TRACE_COUNTER_PATH="$TRACE_COUNTER_PATH"
fi

if [ "$SUMMARY_ONLY" != "1" ] && [ "$FORCE_TRACE" != "1" ] && [ "$LIVE_COMPARE" != "1" ] && ! has_trace_focus_env; then
    SUMMARY_ONLY="1"
fi

if [ "$SUMMARY_ONLY" = "1" ]; then
    clear_trace_focus_env
    export XFOIL_TRACE_KIND_ALLOW="__summary_none__"
fi

BUILD_CMD=("$DOTNET_BIN" build "$TEST_PROJECT" -v minimal)
TEST_CMD=("$DOTNET_BIN" test "$TEST_PROJECT" --no-build --filter "$TEST_FILTER" -v minimal -l "console;verbosity=minimal")
if [ -n "$ARTIFACTS_PATH" ]; then
    BUILD_CMD+=(--artifacts-path "$ARTIFACTS_PATH")
    TEST_CMD+=(--artifacts-path "$ARTIFACTS_PATH")
fi

BUILD_START="$(date +%s)"
"${BUILD_CMD[@]}"
BUILD_END="$(date +%s)"

echo "=== Running managed parity case ==="
echo "Case: $CASE_ID"
echo "Airfoil: $AIRFOIL"
echo "Re: $REYNOLDS"
echo "Alpha: $ALPHA"
echo "Panels: $PANELS"
echo "Max iterations: $MAX_ITER"
echo "Ncrit: $NCRIT"
if [ -n "$ARTIFACTS_PATH" ]; then
    echo "Managed artifacts: $ARTIFACTS_PATH"
fi
if [ -n "$TRACE_COUNTER_PATH" ]; then
    echo "Trace counter: $TRACE_COUNTER_PATH"
fi
echo "Max trace size (MB): $MAX_TRACE_MB"
echo "Summary only: $SUMMARY_ONLY"
echo "Force trace: $FORCE_TRACE"
echo "Live compare: $LIVE_COMPARE"
echo "Route disparity: $ROUTE_DISPARITY"
if [ "$LIVE_COMPARE" = "1" ] && [ "$FORCE_TRACE" != "1" ] && ! has_trace_focus_env; then
    echo "Implicit persisted trace mode: summary markers only"
fi
if [ -n "${XFOIL_LIVE_COMPARE_REFERENCE_TRACE:-}" ]; then
    echo "Live compare reference: $XFOIL_LIVE_COMPARE_REFERENCE_TRACE"
fi
echo "Build time (s): $((BUILD_END - BUILD_START))"

TEST_START="$(date +%s)"
"${TEST_CMD[@]}"
TEST_END="$(date +%s)"
echo "Test time (s): $((TEST_END - TEST_START))"

MANAGED_OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/tools/fortran-debug/csharp/$CASE_ID}"
TRACE_PATH=""
DUMP_PATH=""
REPORT_PATH=""
if [ -d "$MANAGED_OUTPUT_DIR" ]; then
    TRACE_PATH="$(latest_versioned_match "$MANAGED_OUTPUT_DIR" 'csharp_trace.*.jsonl')"
    DUMP_PATH="$(latest_versioned_match "$MANAGED_OUTPUT_DIR" 'csharp_dump.*.txt')"
    REPORT_PATH="$(latest_versioned_match "$MANAGED_OUTPUT_DIR" 'parity_report.*.txt')"
fi

echo
echo "=== Managed artifact summary ==="
if [ -n "$TRACE_PATH" ] && [ -f "$TRACE_PATH" ]; then
    ls -lh "$TRACE_PATH"
fi
if [ -n "$DUMP_PATH" ] && [ -f "$DUMP_PATH" ]; then
    ls -lh "$DUMP_PATH"
fi
if [ -n "$REPORT_PATH" ] && [ -f "$REPORT_PATH" ]; then
    ls -lh "$REPORT_PATH"
fi

REFERENCE_DUMP=""
if [ -d "$REFERENCE_DIR" ]; then
    REFERENCE_DUMP="$(latest_versioned_match "$REFERENCE_DIR" 'reference_dump.*.txt')"
fi

if [ -n "$REFERENCE_DUMP" ] && [ -f "$REFERENCE_DUMP" ] && [ -n "$DUMP_PATH" ] && [ -f "$DUMP_PATH" ]; then
    echo
    echo "=== Final Gap ==="
    if FINAL_GAP_OUTPUT="$(python3 "$SCRIPT_DIR/report_final_gap.py" "$REFERENCE_DUMP" "$DUMP_PATH" 2>&1)"; then
        printf '%s\n' "$FINAL_GAP_OUTPUT"
    elif grep -q 'FINAL LIVE_COMPARE_ABORTED=True' "$DUMP_PATH"; then
        echo "Final gap skipped because managed live compare stopped at the first mismatch."
        printf '%s\n' "$FINAL_GAP_OUTPUT"
    else
        printf '%s\n' "$FINAL_GAP_OUTPUT" >&2
        exit 1
    fi
fi

if [ -n "$REPORT_PATH" ] && [ -f "$REPORT_PATH" ]; then
    echo
    echo "=== Harness Divergence Report ==="
    cat "$REPORT_PATH"
fi

if [ "$ROUTE_DISPARITY" = "1" ]; then
    if [ -z "$REPORT_PATH" ] || [ ! -f "$REPORT_PATH" ]; then
        echo
        echo "=== Responsible Rig Route ==="
        echo "ERROR: --route-disparity requires a parity_report artifact in $MANAGED_OUTPUT_DIR."
        exit 2
    fi

    echo
    echo "=== Responsible Rig Route ==="
    if ! python3 "$SCRIPT_DIR/route_full_xfoil_disparity.py" \
        --parity-report "$REPORT_PATH" \
        --output-dir "$MANAGED_OUTPUT_DIR" \
        --run-rig-quick; then
        echo "Route failed or no responsible rig is registered yet. Register the missing focused rig before any solver patching."
        exit 2
    fi
fi
