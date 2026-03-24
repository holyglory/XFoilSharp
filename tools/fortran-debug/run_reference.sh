#!/bin/bash
# run_reference.sh -- Run instrumented XFoil on a named reference case and capture dump/trace artifacts.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
source "$REPO_ROOT/tools/bootstrap-env.sh"
bootstrap_xfoilsharp_env

BUILD_DIR="${XFOIL_BUILD_DIR:-$SCRIPT_DIR/build}"
BINARY="$BUILD_DIR/xfoil_debug"
CASES_DIR="$SCRIPT_DIR/cases"
TRACE_COUNTER_PATH="${XFOIL_TRACE_COUNTER_PATH:-$SCRIPT_DIR/trace_counter.txt}"
CASE_ID="n0012_re1e6_a0"
OUTPUT_DIR=""
TRACE_FILTER_PID=""
TRACE_PIPE_PATH=""
AIRFOIL=""
REYNOLDS=""
ALPHA=""
PANELS=""
MAX_ITER=""
NCRIT=""
MAX_TRACE_MB="${XFOIL_MAX_TRACE_MB:-}"
SUMMARY_ONLY="0"
FORCE_TRACE="0"
MANAGED_OUTPUT_DIR=""

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

build_case_id() {
    printf 'adhoc_n%s_re%s_a%s_p%s_n%s' \
        "$(sanitize_token "$AIRFOIL")" \
        "$(sanitize_token "$REYNOLDS")" \
        "$(sanitize_token "$ALPHA")" \
        "$(sanitize_token "$PANELS")" \
        "$(sanitize_token "$NCRIT")"
}

write_panel_definition() {
    local template_path="$1"
    local output_path="$2"
    local panel_count="$3"
    python3 - <<'PY' "$template_path" "$output_path" "$panel_count"
from pathlib import Path
import sys

template_path, output_path, panel_count = sys.argv[1:]
lines = Path(template_path).read_text(encoding='utf-8').splitlines()
if not lines:
    raise SystemExit(f"Template panel definition is empty: {template_path}")

tokens = lines[0].split()
if not tokens:
    raise SystemExit(f"Template panel definition first line is empty: {template_path}")

tokens[0] = panel_count
lines[0] = " ".join(tokens)
Path(output_path).write_text("\n".join(lines) + "\n", encoding='utf-8')
PY
}

write_ad_hoc_input() {
    local output_path="$1"
    local airfoil="$2"
    local reynolds="$3"
    local alpha="$4"
    local max_iter="$5"
    local ncrit="$6"
    local panel_def_path="$7"
    python3 - <<'PY' "$output_path" "$airfoil" "$reynolds" "$alpha" "$max_iter" "$ncrit" "$panel_def_path"
from pathlib import Path
import sys

output_path, airfoil, reynolds, alpha, max_iter, ncrit, panel_def_path = sys.argv[1:]
lines = []
if panel_def_path:
    lines.append(f"RDEF {panel_def_path}")

lines.extend(
    [
        f"NACA {airfoil}",
        "PANE",
        "OPER",
        "VPAR",
        f"N {ncrit}",
        "",
        f"VISC {reynolds}",
        f"ITER {max_iter}",
        f"ALFA {alpha}",
        "",
        "QUIT",
    ]
)

Path(output_path).write_text("\n".join(lines) + "\n", encoding='utf-8')
PY
}

validate_reference_stdout() {
    local stdout_path="$1"
    local requested_panels="$2"
    python3 - <<'PY' "$stdout_path" "$requested_panels"
from pathlib import Path
import re
import sys

stdout_path, requested_panels = sys.argv[1:]
text = Path(stdout_path).read_text(encoding='utf-8', errors='replace')

not_found_lines = [line.strip() for line in text.splitlines() if 'not found' in line.lower()]
benign_default_warning = 'file  xfoil.def  not found'
unexpected_not_found = [
    line for line in not_found_lines
    if line.lower() != benign_default_warning
]
if unexpected_not_found:
    raise SystemExit(
        "Reference setup failed because XFoil could not load an input file:\n"
        + "\n".join(unexpected_not_found)
    )

if requested_panels:
    matches = re.findall(r'Number of panel nodes\s+(\d+)', text)
    if not matches:
        raise SystemExit(
            f"Reference setup failed because XFoil did not report a panel-node count for requested mesh {requested_panels}."
        )

    actual_panels = int(matches[-1])
    expected_panels = int(requested_panels)
    if actual_panels != expected_panels:
        raise SystemExit(
            f"Reference setup failed because XFoil used {actual_panels} panel nodes instead of requested {expected_panels}."
        )
PY
}

usage() {
    cat <<'EOF'
Usage:
  run_reference.sh [--case <caseId>] [--output-dir <dir>]
  run_reference.sh --airfoil <code> --re <value> --alpha <deg> [options]

Options:
  --case <caseId>       Named case from tools/fortran-debug/cases
  --case-id <id>        Ad hoc case id (default: derived from parameters)
  --airfoil <code>      NACA 4-digit code, e.g. 0012
  --re <value>          Reynolds number
  --alpha <deg>         Angle of attack in degrees
  --panels <count>      Panel count via a generated RDEF file (default for ad hoc: 60)
  --iter <count>        Max viscous iterations (default for ad hoc: 80)
  --ncrit <value>       Ncrit / critical amplification factor (default for ad hoc: 9)
  --output-dir <dir>    Artifact directory override
  --build-dir <dir>     Build directory / executable directory override
  --managed-output-dir <dir>
                        Managed dump directory override used for automatic final-gap reporting
  --trace-counter-path <file>
                        Counter file override so isolated sandboxes do not share numbering
  --max-trace-mb <mb>   Delete and fail if the generated JSON trace exceeds this size
  --summary-only        Suppress trace capture and keep only dump plus tiny session markers
  --full-trace          Force trace capture even when no XFOIL_TRACE_* selector is set

Trace filtering is controlled by the existing XFOIL_TRACE_* and XFOIL_TRACE_TRIGGER_* environment variables.
Use XFOIL_TRACE_NAME_ALLOW and XFOIL_TRACE_DATA_MATCH for exact record-name / data-field filters.
Data matches are semicolon-separated, for example: context=basis_gamma_alpha0_single;phase=forward;row=31
Use XFOIL_TRACE_POST_LIMIT with XFOIL_TRACE_RING_BUFFER to keep only a short tail after the trigger.
EOF
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

while [ "$#" -gt 0 ]; do
    case "$1" in
        --case)
            CASE_ID="${2:?missing case id}"
            shift 2
            ;;
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
        --build-dir)
            BUILD_DIR="${2:?missing build directory}"
            BINARY="$BUILD_DIR/xfoil_debug"
            shift 2
            ;;
        --managed-output-dir)
            MANAGED_OUTPUT_DIR="${2:?missing managed output directory}"
            shift 2
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

if [ -n "$AIRFOIL" ]; then
    if [ -z "$REYNOLDS" ] || [ -z "$ALPHA" ]; then
        echo "ERROR: --airfoil, --re, and --alpha are required for ad hoc runs."
        usage
        exit 1
    fi

    PANELS="${PANELS:-60}"
    MAX_ITER="${MAX_ITER:-80}"
    NCRIT="${NCRIT:-9}"

    if [ -z "$CASE_ID" ] || [ "$CASE_ID" = "n0012_re1e6_a0" ]; then
        CASE_ID="$(build_case_id)"
    fi
fi

BUILD_DIR="$(resolve_path "$BUILD_DIR")"
BINARY="$BUILD_DIR/xfoil_debug"
TRACE_COUNTER_PATH="$(resolve_path "$TRACE_COUNTER_PATH")"

if [ -n "$OUTPUT_DIR" ]; then
    OUTPUT_DIR="$(resolve_path "$OUTPUT_DIR")"
fi

if [ -n "$MANAGED_OUTPUT_DIR" ]; then
    MANAGED_OUTPUT_DIR="$(resolve_path "$MANAGED_OUTPUT_DIR")"
fi

if [ "$SUMMARY_ONLY" = "1" ]; then
    clear_trace_focus_env
    export XFOIL_TRACE_KIND_ALLOW="__summary_none__"
fi

if [ "$SUMMARY_ONLY" != "1" ] && [ "$FORCE_TRACE" != "1" ] && ! has_trace_focus_env; then
    SUMMARY_ONLY="1"
    clear_trace_focus_env
    export XFOIL_TRACE_KIND_ALLOW="__summary_none__"
fi

if [ -z "$OUTPUT_DIR" ]; then
    OUTPUT_DIR="$SCRIPT_DIR/reference/$CASE_ID"
fi
OUTPUT_DIR="$(resolve_path "$OUTPUT_DIR")"

mkdir -p "$BUILD_DIR"

echo "=== Running reference case ==="
echo "Case: $CASE_ID"
echo "Binary: $BINARY"
echo "Trace counter: $TRACE_COUNTER_PATH"
echo "Summary only: $SUMMARY_ONLY"

if [ ! -f "$BINARY" ]; then
    if [ "$BUILD_DIR" = "$SCRIPT_DIR/build" ]; then
        echo "Reference debug binary missing; rebuilding with build_debug.sh"
        "$SCRIPT_DIR/build_debug.sh"
    fi
fi

if [ ! -f "$BINARY" ]; then
    echo "ERROR: Binary not found. Run build_debug.sh first."
    exit 1
fi

if [ -n "$AIRFOIL" ]; then
    mkdir -p "$OUTPUT_DIR"
    INPUT_FILE="$BUILD_DIR/${CASE_ID}.in"
    PANEL_DEF_PATH=""
    if [ -n "$PANELS" ]; then
        PANEL_DEF_PATH="$BUILD_DIR/panel.def"
        write_panel_definition "$CASES_DIR/panel80.def" "$PANEL_DEF_PATH" "$PANELS"
        PANEL_DEF_INPUT="panel.def"
    else
        PANEL_DEF_INPUT=""
    fi

    write_ad_hoc_input "$INPUT_FILE" "$AIRFOIL" "$REYNOLDS" "$ALPHA" "$MAX_ITER" "$NCRIT" "$PANEL_DEF_INPUT"
else
    INPUT_FILE="$CASES_DIR/$CASE_ID.in"
fi

if [ ! -f "$INPUT_FILE" ]; then
    echo "ERROR: Case input not found: $INPUT_FILE"
    exit 1
fi

mkdir -p "$OUTPUT_DIR"
mkdir -p "$(dirname "$TRACE_COUNTER_PATH")"

TRACE_COUNTER="$(python3 - <<'PY' "$TRACE_COUNTER_PATH"
from pathlib import Path
import sys

path = Path(sys.argv[1])
current = 0
if path.exists():
    text = path.read_text(encoding='utf-8').strip()
    if text:
        current = int(text)
next_value = current + 1
path.write_text(f"{next_value}\n", encoding='utf-8')
print(next_value)
PY
)"
UNIQUE_TRACE_PATH="$OUTPUT_DIR/reference_trace.$TRACE_COUNTER.jsonl"
UNIQUE_DUMP_PATH="$OUTPUT_DIR/reference_dump.$TRACE_COUNTER.txt"

cd "$BUILD_DIR"
cp "$INPUT_FILE" "$BUILD_DIR/xfoil_input.txt"
rm -f "$BUILD_DIR/debug_trace.jsonl"
rm -f "$BUILD_DIR/debug_dump.txt"
rm -f "$BUILD_DIR/xfoil_stdout.txt"
TRACE_PIPE_PATH="$BUILD_DIR/debug_trace.pipe"
rm -f "$TRACE_PIPE_PATH"
mkfifo "$TRACE_PIPE_PATH"
python3 "$SCRIPT_DIR/filter_trace.py" --output "$BUILD_DIR/debug_trace.jsonl" < "$TRACE_PIPE_PATH" &
TRACE_FILTER_PID=$!
export XFOIL_TRACE_PIPE_PATH="$TRACE_PIPE_PATH"

echo "Running XFoil with case input from $INPUT_FILE ..."
./xfoil_debug < xfoil_input.txt > xfoil_stdout.txt 2>&1 || true

wait "$TRACE_FILTER_PID"
rm -f "$TRACE_PIPE_PATH"
unset XFOIL_TRACE_PIPE_PATH

echo "XFoil run complete."
validate_reference_stdout "$BUILD_DIR/xfoil_stdout.txt" "${PANELS:-}"

if [ -f "$BUILD_DIR/debug_dump.txt" ]; then
    cp "$BUILD_DIR/debug_dump.txt" "$UNIQUE_DUMP_PATH"
    cp "$UNIQUE_DUMP_PATH" "$OUTPUT_DIR/reference_dump.txt"
    echo "Copied versioned dump to $UNIQUE_DUMP_PATH"
    echo "Copied compatibility dump to $OUTPUT_DIR/reference_dump.txt"
else
    echo "ERROR: debug_dump.txt not found in $BUILD_DIR"
    echo "XFoil stdout:"
    cat "$BUILD_DIR/xfoil_stdout.txt"
    exit 1
fi

cp "$BUILD_DIR/xfoil_stdout.txt" "$OUTPUT_DIR/xfoil_stdout.txt"

if [ -f "$BUILD_DIR/debug_trace.jsonl" ]; then
    python3 - <<'PY' "$BUILD_DIR/debug_trace.jsonl" "$UNIQUE_TRACE_PATH" "$TRACE_COUNTER" "$CASE_ID" "$SCRIPT_DIR"
import datetime
import json
import shutil
import sys

source_path, destination_path, trace_counter, case_id, script_dir = sys.argv[1:]
sys.path.insert(0, script_dir)
from trace_bits import augment_record

timestamp = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='microseconds').replace('+00:00', 'Z')
session_record = augment_record({
    "sequence": 0,
    "runtime": "fortran",
    "kind": "session_start",
    "scope": "session",
    "name": None,
    "data": {
        "caseId": case_id,
        "traceCounter": int(trace_counter),
        "source": "run_reference.sh"
    },
    "values": None,
    "tags": None,
    "timestampUtc": timestamp,
})

with open(destination_path, 'w', encoding='utf-8') as destination:
    destination.write(json.dumps(session_record, separators=(',', ':')))
    destination.write('\n')
    with open(source_path, 'r', encoding='utf-8') as source:
        shutil.copyfileobj(source, destination)
PY
    cp "$UNIQUE_TRACE_PATH" "$OUTPUT_DIR/reference_trace.jsonl"
    echo "Copied versioned trace to $UNIQUE_TRACE_PATH"
    echo "Copied compatibility trace to $OUTPUT_DIR/reference_trace.jsonl"
    if [ -n "$MAX_TRACE_MB" ]; then
        python3 - <<'PY' "$UNIQUE_TRACE_PATH" "$OUTPUT_DIR/reference_trace.jsonl" "$MAX_TRACE_MB"
from pathlib import Path
import sys

versioned_path = Path(sys.argv[1])
compatibility_path = Path(sys.argv[2])
max_mb = int(sys.argv[3])
max_bytes = max_mb * 1024 * 1024
actual_bytes = versioned_path.stat().st_size

if actual_bytes > max_bytes:
    versioned_path.unlink(missing_ok=True)
    compatibility_path.unlink(missing_ok=True)
    raise SystemExit(
        f"ERROR: reference trace exceeded {max_mb} MB ({actual_bytes} bytes) and was deleted."
    )
PY
    fi
else
    echo "WARNING: debug_trace.jsonl not found in $BUILD_DIR"
fi

# Preserve the legacy flat artifact locations for the default case so older tooling still works.
if [ "$CASE_ID" = "n0012_re1e6_a0" ]; then
    cp "$OUTPUT_DIR/reference_dump.txt" "$SCRIPT_DIR/reference_dump.txt"
    if [ -f "$OUTPUT_DIR/reference_trace.jsonl" ]; then
        cp "$OUTPUT_DIR/reference_trace.jsonl" "$SCRIPT_DIR/reference_trace.jsonl"
    fi
fi

echo ""
echo "=== Dump validation ==="
echo -n "File size: "
wc -c < "$OUTPUT_DIR/reference_dump.txt"
echo -n "Line count: "
wc -l < "$OUTPUT_DIR/reference_dump.txt"

echo -n "ITER markers: "
grep -c '=== ITER' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo -n "STATION lines: "
grep -c 'STATION IS=' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo -n "POST_UPDATE lines: "
grep -c 'POST_UPDATE' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo -n "POST_CALC lines: "
grep -c 'POST_CALC' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo -n "CONVERGED lines: "
grep -c 'CONVERGED' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo -n "BLSOLV_POST_FORWARD lines: "
grep -c 'BLSOLV_POST_FORWARD' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo -n "VDEL_SOL lines: "
grep -c 'VDEL_SOL' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo -n "BL_STATE lines: "
grep -c 'BL_STATE' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo -n "TRANSITION lines: "
grep -c 'TRANSITION' "$OUTPUT_DIR/reference_dump.txt" || echo "0"

echo ""
echo "=== First 30 lines of dump ==="
head -30 "$OUTPUT_DIR/reference_dump.txt"

echo ""
echo "=== Last 10 lines of dump ==="
tail -10 "$OUTPUT_DIR/reference_dump.txt"

echo ""
echo "=== Reference case complete ==="

MANAGED_DIR="${MANAGED_OUTPUT_DIR:-$SCRIPT_DIR/csharp/$CASE_ID}"
MANAGED_DUMP=""
if [ -d "$MANAGED_DIR" ]; then
    MANAGED_DUMP="$(latest_versioned_match "$MANAGED_DIR" 'csharp_dump.*.txt')"
fi

if [ -n "$MANAGED_DUMP" ] && [ -f "$MANAGED_DUMP" ]; then
    echo ""
    echo "=== Final Gap ==="
    python3 "$SCRIPT_DIR/report_final_gap.py" "$OUTPUT_DIR/reference_dump.txt" "$MANAGED_DUMP"
fi
