#!/usr/bin/env python3
"""Run the focused micro-rig parity matrix and emit machine-readable + human-readable reports."""

from __future__ import annotations

import argparse
import json
import os
import re
import signal
import shutil
import subprocess
import sys
import time
import threading
import xml.etree.ElementTree as ET
from functools import lru_cache
from math import ceil
from collections import defaultdict
from collections import deque
from dataclasses import dataclass
from datetime import datetime, timezone
from itertools import product
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = REPO_ROOT / "tools" / "fortran-debug"
REGISTRY_PATH = TOOLS_DIR / "micro_rig_registry.json"
DEFAULT_OUTPUT_ROOT = TOOLS_DIR / "micro-rig-matrix"
TEST_PROJECT = REPO_ROOT / "tests" / "XFoil.Core.Tests" / "XFoil.Core.Tests.csproj"
DOTNET_BIN = os.environ.get("DOTNET_BIN") or shutil.which("dotnet") or "/Users/slava/.dotnet/dotnet"
SHARED_FULL_MODE_CAPTURE_OWNER = "shared-full-mode"
SHARED_CAPTURE_METADATA_NAME = "shared_capture_metadata.json"


@dataclass
class CorpusSummary:
    total_vector_count: int
    unique_real_vector_count: int
    unique_keys: set[tuple[Any, ...]]
    provenance: list[dict[str, Any]]
    source_notes: list[str]


@dataclass
class TestSummary:
    pass_count: int
    fail_count: int
    skipped_count: int
    first_failure_message: str | None
    first_failure_vector_id: str | None
    first_failure_field: str | None
    test_filter: str
    stdout_path: str
    stderr_path: str
    trx_path: str
    elapsed_seconds: float


@dataclass
class SharedCaptureCache:
    captures_by_signature: dict[tuple[str, ...], tuple[Path, Path | None]]
    executed_count: int = 0
    reused_count: int = 0


@dataclass(frozen=True)
class PersistedTraceDir:
    path: Path
    case_id: str
    owner: str
    trust_class: str
    pattern: str
    origin_owner: str
    origin_trust_class: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=("quick", "full"), default="quick")
    parser.add_argument("--rig", default="all", help="Rig id, comma-separated ids, or 'all'.")
    parser.add_argument("--output-dir", default=str(DEFAULT_OUTPUT_ROOT))
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--skip-triage", action="store_true")
    parser.add_argument("--skip-expand", action="store_true")
    parser.add_argument("--log-tail-lines", type=int, default=2000, help="Keep only the last N stdout/stderr lines for matrix-owned dotnet test logs.")
    parser.add_argument("--retain-runs", type=int, default=8, help="Keep only the newest N run directories under the output root.")
    parser.add_argument("--no-prune-runs", action="store_true", help="Disable pruning of older run directories.")
    parser.add_argument("--refresh-limit", type=int, default=0, help="Limit the number of refresh cases consumed per rig in full mode. 0 means no limit.")
    parser.add_argument("--refresh-shard-count", type=int, default=1, help="Split refresh cases into N deterministic shards for parallel full-mode harvests.")
    parser.add_argument("--refresh-shard-index", type=int, default=0, help="Zero-based shard index to execute when --refresh-shard-count > 1.")
    parser.add_argument("--refresh-offset", type=int, default=0, help="Skip the first N refresh cases after sharding. Useful for resumable shard batches.")
    parser.add_argument("--per-rig-timeout-seconds", type=int, default=180, help="Terminate a rig's dotnet test process if it exceeds this wall-clock budget. 0 disables the timeout.")
    parser.add_argument("--capture-timeout-seconds", type=int, default=120, help="Terminate a reference or managed capture case if it exceeds this wall-clock budget. 0 disables the timeout.")
    return parser.parse_args()


def load_registry() -> dict[str, Any]:
    return json.loads(REGISTRY_PATH.read_text(encoding="utf-8"))


def materialize_rig(rig: dict[str, Any]) -> list[dict[str, Any]]:
    subrigs = rig.get("subrigs")
    if not subrigs:
        return [rig]

    materialized: list[dict[str, Any]] = []
    for index, subrig in enumerate(subrigs):
        child = dict(rig)
        child.update(subrig)
        child.pop("subrigs", None)
        child["parent_rig_id"] = rig["id"]
        child["parent_display_name"] = rig.get("display_name")
        child["subrig_index"] = index
        child["subrig_count"] = len(subrigs)
        materialized.append(child)
    return materialized


def materialize_rigs(rigs: list[dict[str, Any]]) -> list[dict[str, Any]]:
    expanded: list[dict[str, Any]] = []
    for rig in rigs:
        expanded.extend(materialize_rig(rig))
    return expanded


def select_rigs(registry: dict[str, Any], rig_selector: str) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    promoted_phase1_ids = set(registry.get("promoted_phase1_rig_ids", []))
    canonical_promoted_phase1_ids = set(registry.get("canonical_phase1_promoted_rig_ids", []))
    phase1 = [dict(rig, category="phase1", canonical_phase1_promoted=False) for rig in registry["phase1_rigs"]]
    phase1.extend(
        dict(
            rig,
            category="phase1",
            canonical_phase1_promoted=True,
            promoted_phase1=rig["id"] in promoted_phase1_ids,
        )
        for rig in registry.get("phase2_rigs", [])
        if rig["id"] in canonical_promoted_phase1_ids
    )
    phase2 = [
        dict(
            rig,
            category="phase1" if rig["id"] in promoted_phase1_ids else "phase2",
            promoted_phase1=rig["id"] in promoted_phase1_ids,
            canonical_phase1_promoted=False,
        )
        for rig in registry.get("phase2_rigs", [])
        if rig["id"] not in canonical_promoted_phase1_ids
    ]
    active = materialize_rigs(phase1 + phase2)
    backlog = registry["future_backlog"]
    if rig_selector == "all":
        return active, backlog

    requested = {token.strip() for token in rig_selector.split(",") if token.strip()}
    selected = [
        rig
        for rig in active
        if rig["id"] in requested or rig.get("parent_rig_id") in requested
    ]
    matched_ids = {rig["id"] for rig in selected}
    matched_parent_ids = {rig["parent_rig_id"] for rig in selected if rig.get("parent_rig_id")}
    missing = requested - matched_ids - matched_parent_ids
    if missing:
        raise SystemExit(f"Unknown rig id(s): {', '.join(sorted(missing))}")
    return selected, []


def ensure_directory(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def emit_progress(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def latest_matching_file(directory: Path, glob_pattern: str) -> Path | None:
    if not directory.exists():
        return None

    candidates = list(matching_files(directory, glob_pattern))
    if not candidates:
        return None

    def sort_key(path: Path) -> tuple[int, str]:
        match = re.search(r"\.(\d+)\.", path.name)
        version = int(match.group(1)) if match else -1
        return version, path.name

    return max(candidates, key=sort_key)


@lru_cache(maxsize=None)
def latest_matching_file_cached(directory: str, glob_pattern: str) -> str | None:
    latest = latest_matching_file(Path(directory), glob_pattern)
    return str(latest) if latest is not None else None


def matching_files(directory: Path, glob_pattern: str) -> tuple[Path, ...]:
    if not directory.exists():
        return tuple()

    candidates = sorted(directory.glob(glob_pattern))
    return tuple(path for path in candidates if path.is_file())


@lru_cache(maxsize=None)
def matching_files_cached(directory: str, glob_pattern: str) -> tuple[str, ...]:
    return tuple(str(path) for path in matching_files(Path(directory), glob_pattern))


def shared_capture_metadata_path(directory: Path) -> Path:
    return directory / SHARED_CAPTURE_METADATA_NAME


def normalize_trace_env(trace_env: dict[str, str]) -> dict[str, str]:
    normalized: dict[str, str] = {}
    for key in sorted(trace_env):
        value = str(trace_env[key]).strip()
        if not value:
            continue
        if key == "XFOIL_TRACE_KIND_ALLOW":
            kinds = sorted({kind.strip() for kind in value.split(",") if kind.strip()})
            if kinds:
                normalized[key] = ",".join(kinds)
            continue
        normalized[key] = value
    return normalized


def load_shared_capture_metadata(directory: Path) -> dict[str, Any] | None:
    path = shared_capture_metadata_path(directory)
    if not path.exists():
        return None

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return None

    if not isinstance(payload, dict):
        return None

    signature = payload.get("signature")
    trace_env = payload.get("trace_env")
    if not isinstance(signature, list) or not all(isinstance(item, str) for item in signature):
        return None
    if not isinstance(trace_env, dict) or not all(isinstance(key, str) and isinstance(value, str) for key, value in trace_env.items()):
        return None

    return {
        "signature": tuple(signature),
        "trace_env": normalize_trace_env(trace_env),
    }


def write_shared_capture_metadata(directory: Path, signature: tuple[str, ...], trace_env: dict[str, str]) -> None:
    payload = {
        "signature": list(signature),
        "trace_env": normalize_trace_env(trace_env),
    }
    shared_capture_metadata_path(directory).write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def persisted_trace_specs_for_rig(
    rig: dict[str, Any],
    *,
    include_default_patterns: bool = True,
    include_additional_patterns: bool = True,
    mode: str = "quick",
) -> tuple[tuple[str, str | None, str | None], ...]:
    specs: list[tuple[str, str | None, str | None]] = []
    include_shared_persisted_captures = (
        rig.get("quick_include_shared_persisted_captures", rig.get("include_shared_persisted_captures", True))
        if mode == "quick"
        else rig.get("include_shared_persisted_captures", True)
    )
    if include_default_patterns:
        specs.append((f"micro_rig_matrix_{rig['id']}_*_ref", None, None))
        if include_shared_persisted_captures:
            specs.append((f"micro_rig_matrix_{SHARED_FULL_MODE_CAPTURE_OWNER}_shared_*_ref", None, None))
    if include_additional_patterns:
        adopted_patterns = (
            rig.get("quick_owner_adopted_persisted_trace_globs", rig.get("owner_adopted_persisted_trace_globs", []))
            if mode == "quick"
            else rig.get("owner_adopted_persisted_trace_globs", [])
        )
        additional_patterns = (
            rig.get("quick_additional_persisted_trace_globs", rig.get("additional_persisted_trace_globs", []))
            if mode == "quick"
            else rig.get("additional_persisted_trace_globs", [])
        )
        for pattern in adopted_patterns:
            value = str(pattern).strip()
            if value:
                specs.append((value, f"{rig['id']}:adopted", "owner"))
        for pattern in additional_patterns:
            value = str(pattern).strip()
            if value:
                specs.append((value, None, None))
    deduped: list[tuple[str, str | None, str | None]] = []
    seen: set[tuple[str, str | None, str | None]] = set()
    for spec in specs:
        if spec in seen:
            continue
        deduped.append(spec)
        seen.add(spec)
    return tuple(deduped)


def classify_persisted_trace_dir(rig_id: str, directory: Path, pattern: str) -> tuple[str, str]:
    name = directory.name
    if name.startswith(f"micro_rig_matrix_{rig_id}_"):
        return rig_id, "owner"
    if name.startswith(f"micro_rig_matrix_{SHARED_FULL_MODE_CAPTURE_OWNER}_shared_"):
        return SHARED_FULL_MODE_CAPTURE_OWNER, "shared"
    if name.startswith("micro_rig_matrix_"):
        owner = name[len("micro_rig_matrix_") :].split("_shared_", 1)[0]
        owner = owner.rsplit("_", 1)[0]
        return owner, "borrowed"
    return "external", "borrowed"


@lru_cache(maxsize=None)
def discover_persisted_trace_dirs_cached(
    rig_id: str,
    pattern_specs: tuple[tuple[str, str | None, str | None], ...],
) -> tuple[tuple[str, str, str, str, str, str, str], ...]:
    reference_root = TOOLS_DIR / "reference"
    if not reference_root.exists():
        return tuple()

    candidates: list[tuple[str, str, str, str, str, str, str]] = []
    seen_paths: set[Path] = set()
    for pattern, forced_owner, forced_trust_class in pattern_specs:
        for directory in sorted(reference_root.glob(pattern)):
            if not directory.is_dir() or directory in seen_paths:
                continue
            if directory.name.startswith(f"micro_rig_matrix_{SHARED_FULL_MODE_CAPTURE_OWNER}_shared_"):
                if load_shared_capture_metadata(directory) is None:
                    continue
            if latest_matching_file_cached(str(directory), "reference_trace*.jsonl") is None:
                continue
            origin_owner, origin_trust_class = classify_persisted_trace_dir(rig_id, directory, pattern)
            owner = forced_owner or origin_owner
            trust_class = forced_trust_class or origin_trust_class
            candidates.append((str(directory), directory.name, owner, trust_class, pattern, origin_owner, origin_trust_class))
            seen_paths.add(directory)

    return tuple(candidates)


def discover_persisted_trace_dirs(
    rig: dict[str, Any],
    *,
    include_default_patterns: bool = True,
    include_additional_patterns: bool = True,
    mode: str = "quick",
) -> list[PersistedTraceDir]:
    pattern_specs = persisted_trace_specs_for_rig(
        rig,
        include_default_patterns=include_default_patterns,
        include_additional_patterns=include_additional_patterns,
        mode=mode,
    )
    return [
        PersistedTraceDir(Path(directory), case_id, owner, trust_class, pattern, origin_owner, origin_trust_class)
        for directory, case_id, owner, trust_class, pattern, origin_owner, origin_trust_class in discover_persisted_trace_dirs_cached(rig["id"], pattern_specs)
    ]


def load_jsonl(path: Path) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError:
                continue
    return records


@lru_cache(maxsize=None)
def load_jsonl_cached(path: str) -> tuple[dict[str, Any], ...]:
    return tuple(load_jsonl(Path(path)))


@lru_cache(maxsize=None)
def load_text_lines_cached(path: str) -> tuple[str, ...]:
    return tuple(Path(path).read_text(encoding="utf-8", errors="ignore").splitlines())


def record_matches(record: dict[str, Any], source: dict[str, Any]) -> bool:
    if record.get("kind") != source["record_kind"]:
        return False

    expected_scope = source.get("scope")
    if expected_scope is not None and record.get("scope") != expected_scope:
        return False

    expected_name = source.get("name")
    if expected_name is not None and record.get("name") != expected_name:
        return False

    data = record.get("data", {})
    if not isinstance(data, dict):
        return False

    for key, expected in source.get("match", {}).items():
        if data.get(key) != expected:
            return False

    for key, allowed in source.get("match_any", {}).items():
        if data.get(key) not in allowed:
            return False

    return True


def extract_record_field(record: dict[str, Any], field: str) -> Any:
    if field == "$kind":
        return record.get("kind")
    if field == "$scope":
        return record.get("scope")
    if field == "$name":
        return record.get("name")
    if field == "$values":
        values = record.get("values")
        if isinstance(values, list):
            return tuple(values)
        return values

    data = record.get("data", {})
    if isinstance(data, dict) and field in data:
        return data.get(field)

    current: Any = record
    for part in field.split("."):
        if not isinstance(current, dict):
            return None
        current = current.get(part)
    return current


def record_dedupe_key(record: dict[str, Any], source: dict[str, Any], case_id: str) -> tuple[Any, ...]:
    data = record.get("data", {})
    key = [record.get("kind"), case_id]
    for field in source.get("dedupe_fields", []):
        key.append(extract_record_field(record, field))
    if len(key) == 1:
        key.append(record.get("sequence"))
    return tuple(key)


def collect_trace_corpus(
    base_dir: Path,
    vector_source: dict[str, Any],
    extra_trace_dirs: list[PersistedTraceDir] | None = None,
) -> CorpusSummary:
    total = 0
    unique_keys: set[tuple[Any, ...]] = set()
    provenance: list[dict[str, Any]] = []
    notes: list[str] = []
    additional_dirs = extra_trace_dirs or []

    for source in vector_source["sources"]:
        directories = [
            PersistedTraceDir(
                path=base_dir / source["directory"],
                case_id=source.get("case_id", source["directory"]),
                owner="configured-source",
                trust_class="owner",
                pattern=source["directory"],
                origin_owner="configured-source",
                origin_trust_class="owner",
            ),
            *additional_dirs,
        ]
        seen_directories: set[Path] = set()
        for candidate in directories:
            directory = candidate.path
            case_id = candidate.case_id
            if directory in seen_directories:
                continue
            seen_directories.add(directory)

            if source.get("read_all_matching_files"):
                trace_paths = tuple(Path(path) for path in matching_files_cached(str(directory), source.get("file_glob", "*.jsonl")))
            else:
                latest_path = latest_matching_file_cached(str(directory), source.get("file_glob", "*.jsonl"))
                trace_paths = (Path(latest_path),) if latest_path is not None else tuple()

            if not trace_paths:
                notes.append(f"missing trace source: {directory}")
                continue

            matched = 0
            for trace_path in trace_paths:
                for record in load_jsonl_cached(str(trace_path)):
                    if not record_matches(record, source):
                        continue

                    matched += 1
                    total += 1
                    dedupe_key = record_dedupe_key(record, source, case_id)
                    unique_keys.add(dedupe_key)
                    data = record.get("data", {})
                    provenance.append(
                        {
                            "case_id": case_id,
                            "trace_path": str(trace_path),
                            "source_directory": str(directory),
                            "source_owner": candidate.owner,
                            "source_trust_class": candidate.trust_class,
                            "source_origin_owner": candidate.origin_owner,
                            "source_origin_trust_class": candidate.origin_trust_class,
                            "source_pattern": candidate.pattern,
                            "kind": record.get("kind"),
                            "scope": record.get("scope"),
                            "name": record.get("name"),
                            "sequence": record.get("sequence"),
                            "side": data.get("side"),
                            "station": data.get("station"),
                            "iteration": data.get("iteration"),
                            "dedupe_key": list(dedupe_key),
                        }
                    )

            source_tag = f"{candidate.trust_class}:{candidate.owner}"
            if candidate.owner != candidate.origin_owner or candidate.trust_class != candidate.origin_trust_class:
                source_tag += f" via {candidate.origin_trust_class}:{candidate.origin_owner}"
            if len(trace_paths) == 1:
                notes.append(f"{source['record_kind']} case={case_id} from {trace_paths[0]} -> {matched} records [{source_tag}]")
            else:
                notes.append(
                    f"{source['record_kind']} case={case_id} from {directory} ({len(trace_paths)} files) -> {matched} records [{source_tag}]"
                )

    return CorpusSummary(
        total_vector_count=total,
        unique_real_vector_count=len(unique_keys),
        unique_keys=unique_keys,
        provenance=provenance,
        source_notes=notes,
    )


def dump_match_value(match: re.Match[str], field: str) -> Any:
    if field.startswith("$group:"):
        group = field.split(":", 1)[1]
        return match.group(group)
    return match.groupdict().get(field)


def dump_record_dedupe_key(match: re.Match[str], source: dict[str, Any], case_id: str) -> tuple[Any, ...]:
    key = [source.get("record_key", "dump_record"), case_id]
    for field in source.get("dedupe_fields", []):
        key.append(dump_match_value(match, field))
    if len(key) == 2:
        key.append(match.group(0))
    return tuple(key)


def collect_dump_corpus(base_dir: Path, vector_source: dict[str, Any]) -> CorpusSummary:
    total = 0
    unique_keys: set[tuple[Any, ...]] = set()
    provenance: list[dict[str, Any]] = []
    notes: list[str] = []

    for source in vector_source["sources"]:
        directory = base_dir / source["directory"]
        case_id = source.get("case_id", source["directory"])
        if not directory.exists():
            notes.append(f"missing dump source: {directory}")
            continue

        files = sorted(directory.glob(source.get("file_glob", "reference_dump*.txt")))
        if not files:
            notes.append(f"missing dump files: {directory}")
            continue

        regex_pattern = re.sub(r"\(\?<([A-Za-z_][A-Za-z0-9_]*)>", r"(?P<\1>", source["regex"])
        regex = re.compile(regex_pattern)
        marker_contains = source.get("marker_contains")
        use_next_line = bool(source.get("use_next_line"))
        line_contains = source.get("line_contains")

        matched = 0
        for path in files:
            lines = load_text_lines_cached(str(path))
            candidate_lines: list[str] = []
            if marker_contains is not None:
                for index, line in enumerate(lines):
                    if marker_contains not in line:
                        continue
                    if use_next_line and index + 1 < len(lines):
                        candidate_lines.append(lines[index + 1])
                    else:
                        candidate_lines.append(line)
            else:
                candidate_lines = list(lines)

            for line in candidate_lines:
                if line_contains is not None and line_contains not in line:
                    continue

                match = regex.search(line)
                if not match:
                    continue

                matched += 1
                total += 1
                unique_keys.add(dump_record_dedupe_key(match, source, case_id))
                provenance.append(
                    {
                        "case_id": case_id,
                        "dump_path": str(path),
                        "record_key": source.get("record_key", "dump_record"),
                        "matched_line": line,
                    }
                )

        notes.append(f"{source.get('record_key', 'dump_record')} case={case_id} from {directory} -> {matched} records")

    return CorpusSummary(
        total_vector_count=total,
        unique_real_vector_count=len(unique_keys),
        unique_keys=unique_keys,
        provenance=provenance,
        source_notes=notes,
    )


def collect_static_corpus(vector_source: dict[str, Any]) -> CorpusSummary:
    return CorpusSummary(
        total_vector_count=vector_source["total_vector_count"],
        unique_real_vector_count=vector_source["real_vector_count"],
        unique_keys=set(),
        provenance=[
            {
                "source_file": vector_source.get("source_file"),
                "reason": vector_source.get("reason"),
            }
        ],
        source_notes=[vector_source.get("reason", "static batch")],
    )


def load_cached_corpus_from_summary(rig_id: str, exclude_run_dir: Path | None = None) -> CorpusSummary | None:
    candidates = sorted(TOOLS_DIR.glob(f"**/rigs/{rig_id}/summary.json"), key=lambda path: path.stat().st_mtime, reverse=True)
    exclude_prefix = exclude_run_dir.resolve() if exclude_run_dir is not None else None
    for path in candidates:
        resolved = path.resolve()
        if exclude_prefix is not None:
            try:
                resolved.relative_to(exclude_prefix)
                continue
            except ValueError:
                pass

        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue

        vector_count = payload.get("vector_count")
        unique_real_vector_count = payload.get("unique_real_vector_count")
        source_notes = payload.get("source_notes", [])
        if not isinstance(vector_count, int) or not isinstance(unique_real_vector_count, int):
            continue
        if not isinstance(source_notes, list) or not all(isinstance(item, str) for item in source_notes):
            source_notes = []

        return CorpusSummary(
            total_vector_count=vector_count,
            unique_real_vector_count=unique_real_vector_count,
            unique_keys=set(),
            provenance=[],
            source_notes=source_notes,
        )

    return None


def collect_corpus(
    base_dir: Path,
    rig: dict[str, Any],
    extra_trace_dirs: list[tuple[Path, str]] | None = None,
    include_default_persisted_trace_dirs: bool = True,
    include_additional_persisted_trace_dirs: bool = True,
    mode: str = "quick",
) -> CorpusSummary:
    vector_source = rig["vector_source"]
    kind = vector_source["kind"]
    if kind == "trace_records":
        persisted_trace_dirs = discover_persisted_trace_dirs(
            rig,
            include_default_patterns=include_default_persisted_trace_dirs,
            include_additional_patterns=include_additional_persisted_trace_dirs,
            mode=mode,
        )
        merged_trace_dirs = [*persisted_trace_dirs, *(extra_trace_dirs or [])]
        return collect_trace_corpus(base_dir, vector_source, merged_trace_dirs)
    if kind == "dump_records":
        return collect_dump_corpus(base_dir, vector_source)
    if kind == "static_batch":
        return collect_static_corpus(vector_source)
    raise ValueError(f"Unsupported vector source kind: {kind}")


def merge_corpus(existing: CorpusSummary, delta: CorpusSummary) -> CorpusSummary:
    merged_unique_keys = set(existing.unique_keys)
    merged_unique_keys.update(delta.unique_keys)
    return CorpusSummary(
        total_vector_count=existing.total_vector_count + delta.total_vector_count,
        unique_real_vector_count=len(merged_unique_keys),
        unique_keys=merged_unique_keys,
        provenance=[*existing.provenance, *delta.provenance],
        source_notes=[*existing.source_notes, *delta.source_notes],
    )


def estimate_cases_to_green(corpus: CorpusSummary, required_vector_count: int) -> int | None:
    if corpus.unique_real_vector_count >= required_vector_count:
        return 0

    case_ids = {item.get("case_id") for item in corpus.provenance if item.get("case_id")}
    if not case_ids:
        return None

    average_vectors_per_case = corpus.unique_real_vector_count / len(case_ids)
    if average_vectors_per_case <= 0:
        return None

    remaining_vectors = required_vector_count - corpus.unique_real_vector_count
    return int(ceil(remaining_vectors / average_vectors_per_case))


def summarize_trace_provenance(corpus: CorpusSummary) -> dict[str, Any]:
    owner_backed_keys: set[tuple[Any, ...]] = set()
    shared_only_keys: set[tuple[Any, ...]] = set()
    borrowed_only_keys: set[tuple[Any, ...]] = set()
    non_owner_keys: set[tuple[Any, ...]] = set()
    owner_cases: set[str] = set()
    non_owner_cases: set[str] = set()
    by_owner: dict[str, set[tuple[Any, ...]]] = defaultdict(set)
    by_trust_class: dict[str, set[tuple[Any, ...]]] = defaultdict(set)
    trust_classes_by_key: dict[tuple[Any, ...], set[str]] = defaultdict(set)

    for item in corpus.provenance:
        case_id = item.get("case_id")
        trace_path = item.get("trace_path")
        if not isinstance(case_id, str) or not isinstance(trace_path, str):
            continue

        dedupe_key = item.get("dedupe_key")
        if not isinstance(dedupe_key, list):
            continue
        record_key = tuple(dedupe_key)
        owner = str(item.get("source_owner") or "unknown")
        trust_class = str(item.get("source_trust_class") or "borrowed")
        by_owner[owner].add(record_key)
        by_trust_class[trust_class].add(record_key)
        trust_classes_by_key[record_key].add(trust_class)
        if trust_class == "owner":
            owner_cases.add(case_id)
        else:
            non_owner_cases.add(case_id)

    for record_key, trust_classes in trust_classes_by_key.items():
        if "owner" in trust_classes:
            owner_backed_keys.add(record_key)
            continue
        non_owner_keys.add(record_key)
        if "shared" in trust_classes:
            shared_only_keys.add(record_key)
        else:
            borrowed_only_keys.add(record_key)

    return {
        "owner_real_vector_count": len(owner_backed_keys),
        "borrowed_real_vector_count": len(non_owner_keys),
        "owner_backed_unique_vector_count": len(owner_backed_keys),
        "non_owner_unique_vector_count": len(non_owner_keys),
        "shared_only_unique_vector_count": len(shared_only_keys),
        "borrowed_only_unique_vector_count": len(borrowed_only_keys),
        "owner_case_count": len(owner_cases),
        "non_owner_case_count": len(non_owner_cases),
        "borrowed_case_count": len(non_owner_cases),
        "by_owner": {key: len(value) for key, value in sorted(by_owner.items())},
        "by_trust_class": {key: len(value) for key, value in sorted(by_trust_class.items())},
    }


def expand_refresh_case_sweeps(rig: dict[str, Any]) -> list[dict[str, str]]:
    explicit_cases = [dict(case) for case in rig.get("refresh_cases", [])]
    sweeps = rig.get("refresh_case_sweeps", [])
    if not sweeps:
        return explicit_cases

    seen_signatures = {case_signature(case) for case in explicit_cases}
    expanded = explicit_cases[:]
    for sweep in sweeps:
        iter_value = str(sweep.get("iter", "80"))
        for airfoil, reynolds, alpha, panels, ncrit in product(
            sweep["airfoils"],
            sweep["reynolds"],
            sweep["alphas"],
            sweep["panels"],
            sweep["ncrits"],
        ):
            airfoil_token = sanitize_case_token(str(airfoil))
            re_token = sanitize_case_token(str(reynolds))
            alpha_token = sanitize_case_token(str(alpha))
            panels_token = sanitize_case_token(str(panels))
            ncrit_token = sanitize_case_token(str(ncrit))
            case_id = (
                f"{sweep['case_prefix']}_n{airfoil_token}_re{re_token}_a{alpha_token}_"
                f"p{panels_token}_n{ncrit_token}"
            )
            recipe = {
                "case_id": case_id,
                "airfoil": str(airfoil),
                "re": str(reynolds),
                "alpha": str(alpha),
                "panels": str(panels),
                "ncrit": str(ncrit),
                "iter": iter_value,
            }
            signature = case_signature(recipe)
            if signature in seen_signatures:
                continue

            expanded.append(recipe)
            seen_signatures.add(signature)

    return expanded


def build_shared_full_mode_trace_env(rigs: list[dict[str, Any]]) -> dict[str, str]:
    kind_allow: set[str] = set()
    post_limit = 0
    ring_buffer = 0
    side = None

    for rig in rigs:
        trace_env = rig.get("trace_env", {})
        kinds = trace_env.get("XFOIL_TRACE_KIND_ALLOW", "")
        for kind in kinds.split(","):
            if kind.strip():
                kind_allow.add(kind.strip())

        post_limit = max(post_limit, int(trace_env.get("XFOIL_TRACE_POST_LIMIT", "0") or "0"))
        ring_buffer = max(ring_buffer, int(trace_env.get("XFOIL_TRACE_RING_BUFFER", "0") or "0"))

        trace_side = trace_env.get("XFOIL_TRACE_SIDE")
        if trace_side:
            side = trace_side

    shared_env: dict[str, str] = {}
    if kind_allow:
        shared_env["XFOIL_TRACE_KIND_ALLOW"] = ",".join(sorted(kind_allow))
    if post_limit > 0:
        shared_env["XFOIL_TRACE_POST_LIMIT"] = str(post_limit)
    if ring_buffer > 0:
        shared_env["XFOIL_TRACE_RING_BUFFER"] = str(ring_buffer)
    if side is not None:
        shared_env["XFOIL_TRACE_SIDE"] = side

    return shared_env


def shared_signature_from_dir_name(name: str) -> tuple[str, ...] | None:
    marker = "_shared_"
    suffix = "_ref"
    if marker not in name or not name.endswith(suffix):
        return None

    payload = name.split(marker, 1)[1][:-len(suffix)]
    parts = payload.split("_")
    if len(parts) != 6:
        return None
    return tuple(parts)


def seed_shared_capture_cache_from_persisted(rigs: list[dict[str, Any]], shared_full_mode_trace_env: dict[str, str]) -> SharedCaptureCache:
    cache = SharedCaptureCache(captures_by_signature={})
    expected_trace_env = normalize_trace_env(shared_full_mode_trace_env)
    seen_dirs: set[Path] = set()
    for rig in rigs:
        for candidate in discover_persisted_trace_dirs(rig):
            directory = candidate.path
            if directory in seen_dirs:
                continue
            seen_dirs.add(directory)
            signature = shared_signature_from_dir_name(directory.name)
            if signature is None:
                continue
            metadata = load_shared_capture_metadata(directory)
            if metadata is None:
                continue
            if metadata["signature"] != signature:
                continue
            if metadata["trace_env"] != expected_trace_env:
                continue
            cache.captures_by_signature.setdefault(signature, (directory, None))
    return cache


def sanitize_case_token(raw: str) -> str:
    return "".join(ch for ch in raw.lower().replace(".", "p").replace("-", "m") if ch.isalnum() or ch == "_")


def preference_rank(preferred_values: list[str] | None, actual_value: str) -> int:
    if not preferred_values:
        return 0

    normalized_actual = sanitize_case_token(actual_value)
    for index, preferred in enumerate(preferred_values):
        if sanitize_case_token(preferred) == normalized_actual:
            return index
    return len(preferred_values)


def case_priority_key(case_recipe: dict[str, str], rig: dict[str, Any] | None = None) -> tuple[int, int, int, int, int, float, int, int, int, str]:
    refresh_priority = rig.get("refresh_priority", {}) if rig is not None else {}
    alpha = abs(float(case_recipe.get("alpha", "0") or "0"))
    panels = int(float(case_recipe.get("panels", "0") or "0"))
    reynolds = int(float(case_recipe.get("re", "0") or "0"))
    ncrit = int(round(float(case_recipe.get("ncrit", "0") or "0") * 10))
    airfoil = sanitize_case_token(case_recipe.get("airfoil", ""))
    return (
        preference_rank(refresh_priority.get("airfoil"), case_recipe.get("airfoil", "")),
        preference_rank(refresh_priority.get("re"), case_recipe.get("re", "")),
        preference_rank(refresh_priority.get("alpha"), case_recipe.get("alpha", "")),
        preference_rank(refresh_priority.get("panels"), case_recipe.get("panels", "")),
        preference_rank(refresh_priority.get("ncrit"), case_recipe.get("ncrit", "")),
        -alpha,
        -panels,
        -reynolds,
        -ncrit,
        airfoil,
    )


def case_signature(case_recipe: dict[str, str]) -> tuple[str, ...]:
    return (
        sanitize_case_token(case_recipe["airfoil"]),
        sanitize_case_token(case_recipe["re"]),
        sanitize_case_token(case_recipe["alpha"]),
        sanitize_case_token(case_recipe["panels"]),
        sanitize_case_token(case_recipe["ncrit"]),
        sanitize_case_token(case_recipe.get("iter", "80")),
    )


def refresh_cases_for_rig(
    rig: dict[str, Any],
    refresh_limit: int,
    refresh_shard_count: int = 1,
    refresh_shard_index: int = 0,
    refresh_offset: int = 0,
) -> list[dict[str, str]]:
    cases = expand_refresh_case_sweeps(rig)
    explicit_case_count = len(rig.get("refresh_cases", []))
    explicit_cases = cases[:explicit_case_count]
    sweep_cases = sorted(cases[explicit_case_count:], key=lambda case: case_priority_key(case, rig))
    cases = [*explicit_cases, *sweep_cases]
    if refresh_shard_count > 1:
        cases = cases[refresh_shard_index::refresh_shard_count]
    if refresh_offset > 0:
        cases = cases[refresh_offset:]
    if refresh_limit > 0:
        return cases[:refresh_limit]
    return cases


def summarize_refresh_case_diversity(cases: list[dict[str, str]]) -> dict[str, int]:
    return {
        "airfoil_count": len({sanitize_case_token(case.get("airfoil", "")) for case in cases if case.get("airfoil")}),
        "re_count": len({sanitize_case_token(case.get("re", "")) for case in cases if case.get("re")}),
        "alpha_count": len({sanitize_case_token(case.get("alpha", "")) for case in cases if case.get("alpha")}),
        "panel_count": len({sanitize_case_token(case.get("panels", "")) for case in cases if case.get("panels")}),
        "ncrit_count": len({sanitize_case_token(case.get("ncrit", "")) for case in cases if case.get("ncrit")}),
    }


def validate_refresh_recipe_diversity(rigs: list[dict[str, Any]]) -> None:
    offenders: list[str] = []
    for rig in rigs:
        vector_source = rig.get("vector_source", {})
        if vector_source.get("kind") != "trace_records":
            continue

        required = int(vector_source.get("min_vector_count", 0) or 0)
        if required < 1000:
            continue

        refresh_cases = refresh_cases_for_rig(rig, refresh_limit=0)
        diversity = summarize_refresh_case_diversity(refresh_cases)
        if diversity["airfoil_count"] >= 2 and diversity["re_count"] >= 2 and diversity["alpha_count"] >= 2:
            continue

        offenders.append(
            f"{rig['id']} (airfoils={diversity['airfoil_count']}, re={diversity['re_count']}, alpha={diversity['alpha_count']})"
        )

    if offenders:
        joined = "; ".join(offenders)
        raise SystemExit(
            "All 1000-vector trace rigs must define diversified refresh coverage across multiple airfoils, Reynolds numbers, and alphas. "
            f"Offending rigs: {joined}"
        )


def rig_driver_targets(rigs: list[dict[str, Any]]) -> list[str]:
    targets = sorted(
        {
            rig["fortran_driver_target"]
            for rig in rigs
            if rig.get("fortran_driver_target") and rig["fortran_driver_target"] != "none"
        }
    )
    return targets


def driver_binary_exists(target: str) -> bool:
    binary_map = {
        "gauss": "gauss_parity_driver",
        "psilin": "psilin_parity_driver",
        "cf": "cf_parity_driver",
        "cq": "cq_parity_driver",
        "dil": "dil_parity_driver",
        "diwall": "di_wall_parity_driver",
        "diouter": "di_outer_parity_driver",
        "diturb": "di_turbulent_parity_driver",
        "didfac": "di_dfac_parity_driver",
        "pswlinhalf": "pswlin_half_parity_driver",
    }
    binary_name = binary_map.get(target)
    if binary_name is None:
        return False
    return (TOOLS_DIR / "build-micro-drivers" / binary_name).exists()


def run_logged_command(command: list[str], cwd: Path, stdout_path: Path, stderr_path: Path, env: dict[str, str] | None = None) -> subprocess.CompletedProcess[str]:
    with stdout_path.open("w", encoding="utf-8") as stdout_handle, stderr_path.open("w", encoding="utf-8") as stderr_handle:
        return subprocess.run(
            command,
            cwd=cwd,
            env=env,
            text=True,
            stdout=stdout_handle,
            stderr=stderr_handle,
            check=False,
        )


def terminate_process_group(process: subprocess.Popen[str]) -> None:
    if process.poll() is not None:
        return

    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        return

    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            return
        process.wait(timeout=5)


def prebuild_if_needed(rigs: list[dict[str, Any]], run_dir: Path, skip_build: bool, enable_reference_triage: bool) -> dict[str, str]:
    build_artifacts: dict[str, str] = {}
    if skip_build:
        return build_artifacts

    build_dir = run_dir / "build"
    ensure_directory(build_dir)

    dotnet_stdout = build_dir / "dotnet-build.stdout.txt"
    dotnet_stderr = build_dir / "dotnet-build.stderr.txt"
    dotnet_result = run_logged_command(
        [DOTNET_BIN, "build", str(TEST_PROJECT), "-c", "Debug"],
        cwd=REPO_ROOT,
        stdout_path=dotnet_stdout,
        stderr_path=dotnet_stderr,
    )
    build_artifacts["managed_build_stdout"] = str(dotnet_stdout)
    build_artifacts["managed_build_stderr"] = str(dotnet_stderr)
    if dotnet_result.returncode != 0:
        raise RuntimeError(f"Managed build failed; see {dotnet_stderr}")

    targets = rig_driver_targets(rigs)
    if targets:
        micro_stdout = build_dir / "micro-drivers.stdout.txt"
        micro_stderr = build_dir / "micro-drivers.stderr.txt"
        result = run_logged_command(
            ["/bin/bash", str(TOOLS_DIR / "build_micro_drivers.sh"), *targets],
            cwd=REPO_ROOT,
            stdout_path=micro_stdout,
            stderr_path=micro_stderr,
        )
        build_artifacts["driver_build_stdout"] = str(micro_stdout)
        build_artifacts["driver_build_stderr"] = str(micro_stderr)
        if result.returncode != 0:
            if all(driver_binary_exists(target) for target in targets):
                build_artifacts["driver_build_warning"] = (
                    "micro-driver rebuild failed, but all requested binaries already existed; "
                    f"continuing with cached binaries from {TOOLS_DIR / 'build-micro-drivers'}"
                )
            else:
                raise RuntimeError(f"Fortran micro-driver build failed; see {micro_stderr}")

    if enable_reference_triage and any(rig.get("supports_live_compare") for rig in rigs):
        reference_stdout = build_dir / "reference-build.stdout.txt"
        reference_stderr = build_dir / "reference-build.stderr.txt"
        reference_result = run_logged_command(
            ["/bin/bash", str(TOOLS_DIR / "build_debug.sh")],
            cwd=REPO_ROOT,
            stdout_path=reference_stdout,
            stderr_path=reference_stderr,
        )
        build_artifacts["reference_build_stdout"] = str(reference_stdout)
        build_artifacts["reference_build_stderr"] = str(reference_stderr)
        if reference_result.returncode != 0:
            raise RuntimeError(f"Reference debug build failed; see {reference_stderr}")

    return build_artifacts


def parse_first_failure(message: str | None) -> tuple[str | None, str | None]:
    if not message:
        return None, None

    vector_bits = []
    for key in ("iter", "case", "record", "station", "ibl", "is", "jo", "jp"):
        match = re.search(rf"\b{key}=([^\s,]+)", message)
        if match:
            vector_bits.append(f"{key}={match.group(1)}")

    field = None
    match = re.search(r"\bfield=([A-Za-z0-9_]+)", message)
    if match:
        field = match.group(1)
    else:
        match = re.search(r"\b(row\d+|residual\d+|hk2(?:_[TD]\d)?|delta[A-Za-z]+|ratio[A-Za-z]+|dmax|rlx|residualNorm)\b", message)
        if match:
            field = match.group(1)

    return (" ".join(vector_bits) if vector_bits else None), field


def is_harness_failure_message(message: str | None) -> bool:
    if not message:
        return False

    lowered = message.lower()
    harness_markers = (
        "sequence contains more than one matching element",
        "sequence contains no matching element",
        "reference triage trace missing",
        "reference triage capture failed",
        "managed triage run failed",
        "dotnet test failed before trx results were written",
        "per-rig timeout exceeded",
        "missing trace source:",
    )
    return any(marker in lowered for marker in harness_markers)


def parse_trx(trx_path: Path) -> tuple[int, int, int, str | None]:
    if not trx_path.exists():
        return 0, 0, 0, None

    tree = ET.parse(trx_path)
    root = tree.getroot()

    pass_count = 0
    fail_count = 0
    skipped_count = 0
    first_failure = None

    for result in root.iterfind(".//{*}UnitTestResult"):
        outcome = result.attrib.get("outcome", "")
        if outcome == "Passed":
            pass_count += 1
        elif outcome == "Failed":
            fail_count += 1
            if first_failure is None:
                message_node = result.find(".//{*}Message")
                if message_node is not None and message_node.text:
                    first_failure = " ".join(message_node.text.split())
        else:
            skipped_count += 1

    return pass_count, fail_count, skipped_count, first_failure


def extract_first_failure_from_stdout(stdout_text: str) -> str | None:
    failed_line = None
    error_message = None
    lines = stdout_text.splitlines()
    for index, line in enumerate(lines):
        stripped = line.strip()
        if failed_line is None and stripped.startswith("Failed "):
            failed_line = stripped
        if stripped == "Error Message:" and index + 1 < len(lines):
            error_message = lines[index + 1].strip()
            break

    if failed_line and error_message:
        return f"{failed_line} {error_message}"
    return error_message or failed_line


def write_text_lines(path: Path, lines: list[str]) -> None:
    text = "".join(lines)
    path.write_text(text, encoding="utf-8")


def managed_test_filter_for_mode(rig: dict[str, Any], mode: str) -> str:
    if mode == "quick":
        return rig.get("quick_managed_test_filter", rig.get("full_managed_test_filter", rig["managed_test_filter"]))
    if mode == "full":
        return rig.get("full_managed_test_filter", rig.get("quick_managed_test_filter", rig["managed_test_filter"]))
    return rig["managed_test_filter"]


def per_rig_timeout_for_mode(rig: dict[str, Any], mode: str, default_timeout_seconds: int) -> int:
    if mode == "quick":
        return int(rig.get("quick_per_rig_timeout_seconds", rig.get("per_rig_timeout_seconds", default_timeout_seconds)))
    if mode == "full":
        return int(rig.get("full_per_rig_timeout_seconds", rig.get("per_rig_timeout_seconds", default_timeout_seconds)))
    return int(rig.get("per_rig_timeout_seconds", default_timeout_seconds))


def run_rig_tests(rig: dict[str, Any], run_dir: Path, mode: str, log_tail_lines: int, per_rig_timeout_seconds: int) -> TestSummary:
    rig_dir = run_dir / "rigs" / rig["id"]
    ensure_directory(rig_dir)

    stdout_path = rig_dir / "dotnet-test.stdout.txt"
    stderr_path = rig_dir / "dotnet-test.stderr.txt"
    trx_path = rig_dir / "results.trx"
    test_filter = managed_test_filter_for_mode(rig, mode)
    command = [
        DOTNET_BIN,
        "test",
        str(TEST_PROJECT),
        "--no-build",
        "--filter",
        test_filter,
        "--logger",
        f"trx;LogFileName={trx_path.name}",
        "--results-directory",
        str(rig_dir),
        "-v",
        "minimal",
    ]

    state: dict[str, str | bool | None] = {
        "failed_line": None,
        "capture_next_error": False,
        "first_failure": None,
    }
    stdout_lines: deque[str] = deque(maxlen=max(log_tail_lines, 1))
    stderr_lines: deque[str] = deque(maxlen=max(log_tail_lines, 1))

    def consume_stream(stream: Any, is_stdout: bool) -> None:
        target_lines = stdout_lines if is_stdout else stderr_lines
        for line in stream:
            target_lines.append(line)

            if not is_stdout:
                continue

            stripped = line.strip()
            if state["failed_line"] is None and stripped.startswith("Failed "):
                state["failed_line"] = stripped
            elif stripped == "Error Message:":
                state["capture_next_error"] = True
            elif state["capture_next_error"]:
                failed_line = state["failed_line"] or ""
                state["first_failure"] = f"{failed_line} {stripped}".strip()
                state["capture_next_error"] = False

    started = time.perf_counter()
    timed_out = False
    process = subprocess.Popen(
        command,
        cwd=REPO_ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        bufsize=1,
    )

    if process.stdout is None or process.stderr is None:
        raise RuntimeError(f"Failed to capture dotnet test output for {rig['id']}")

    stdout_thread = threading.Thread(target=consume_stream, args=(process.stdout, True), daemon=True)
    stderr_thread = threading.Thread(target=consume_stream, args=(process.stderr, False), daemon=True)
    stdout_thread.start()
    stderr_thread.start()

    while process.poll() is None:
        time.sleep(0.25)
        if per_rig_timeout_seconds > 0 and (time.perf_counter() - started) > per_rig_timeout_seconds:
            timed_out = True
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
            break
        if mode != "quick" or not state["first_failure"]:
            continue

        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
        break

    return_code = process.wait()
    stdout_thread.join(timeout=5)
    stderr_thread.join(timeout=5)
    write_text_lines(stdout_path, list(stdout_lines))
    write_text_lines(stderr_path, list(stderr_lines))

    elapsed = time.perf_counter() - started

    pass_count, fail_count, skipped_count, first_failure = parse_trx(trx_path)
    if fail_count == 0 and mode == "quick":
        quick_failure = state["first_failure"] or extract_first_failure_from_stdout(
            stdout_path.read_text(encoding="utf-8", errors="replace")
        )
        if quick_failure:
            fail_count = 1
            first_failure = quick_failure

    completed_test_run = (pass_count + fail_count + skipped_count) > 0

    # A late watchdog hit can race with vstest shutdown on successful quick runs.
    # If the TRX was written and the test run completed cleanly, trust the results
    # instead of the timeout wrapper.
    if timed_out and completed_test_run and fail_count == 0:
        timed_out = False

    if timed_out:
        first_failure = first_failure or f"per-rig timeout exceeded ({per_rig_timeout_seconds}s)"
        if fail_count == 0 and pass_count == 0:
            fail_count = 1

    if return_code != 0 and fail_count == 0 and not (completed_test_run and pass_count > 0):
        stderr_text = stderr_path.read_text(encoding="utf-8", errors="replace")
        first_failure = first_failure or stderr_text.strip() or "dotnet test failed before trx results were written"

    vector_id, field = parse_first_failure(first_failure)
    return TestSummary(
        pass_count=pass_count,
        fail_count=fail_count,
        skipped_count=skipped_count,
        first_failure_message=first_failure,
        first_failure_vector_id=vector_id,
        first_failure_field=field,
        test_filter=test_filter,
        stdout_path=str(stdout_path),
        stderr_path=str(stderr_path),
        trx_path=str(trx_path),
        elapsed_seconds=elapsed,
    )


def find_latest_artifact_paths(rig: dict[str, Any]) -> tuple[str | None, str | None, str | None]:
    reference_path = None
    managed_path = None
    parity_report = None

    artifact_family = rig.get("artifact_family", {})
    for directory in artifact_family.get("reference_dirs", []):
        latest = latest_matching_file_cached(str(TOOLS_DIR / directory), "reference_trace*.jsonl")
        if latest is not None:
            reference_path = latest
            break

    for directory in artifact_family.get("managed_dirs", []):
        latest_trace = latest_matching_file_cached(str(TOOLS_DIR / directory), "csharp_trace*.jsonl")
        latest_report = latest_matching_file_cached(str(TOOLS_DIR / directory), "parity_report*.txt")
        if latest_trace is not None and managed_path is None:
            managed_path = latest_trace
        if latest_report is not None and parity_report is None:
            parity_report = latest_report

    return reference_path, managed_path, parity_report


def read_parity_report_summary(path: str | None) -> str | None:
    if not path:
        return None
    report_path = Path(path)
    if not report_path.exists():
        return None
    lines = [line.strip() for line in report_path.read_text(encoding="utf-8", errors="replace").splitlines() if line.strip()]
    return lines[0] if lines else None


def run_case_capture_parallel(
    rig: dict[str, Any],
    case_recipe: dict[str, str],
    run_dir: Path,
    trace_env_override: dict[str, str] | None = None,
    capture_managed_trace: bool = True,
    capture_timeout_seconds: int = 120,
    capture_owner_id: str | None = None,
) -> tuple[Path, Path, Path | None]:
    env = os.environ.copy()
    capture_trace_env = trace_env_override if trace_env_override is not None else rig.get("trace_env", {})
    for key, value in capture_trace_env.items():
        env[key] = value

    owner_id = capture_owner_id or rig["id"]
    reference_dir = TOOLS_DIR / "reference" / f"micro_rig_matrix_{owner_id}_{case_recipe['case_id']}_ref"
    managed_dir = TOOLS_DIR / "csharp" / f"micro_rig_matrix_{owner_id}_{case_recipe['case_id']}_man"
    ensure_directory(reference_dir)
    ensure_directory(managed_dir)

    capture_dir = run_dir / "rigs" / owner_id / "captures" / case_recipe["case_id"]
    ensure_directory(capture_dir)

    reference_stdout = capture_dir / "reference.stdout.txt"
    reference_stderr = capture_dir / "reference.stderr.txt"
    managed_stdout = capture_dir / "managed.stdout.txt"
    managed_stderr = capture_dir / "managed.stderr.txt"

    reference_command = [
        "/bin/bash",
        str(TOOLS_DIR / "run_reference.sh"),
        "--airfoil",
        case_recipe["airfoil"],
        "--re",
        case_recipe["re"],
        "--alpha",
        case_recipe["alpha"],
        "--panels",
        case_recipe["panels"],
        "--ncrit",
        case_recipe["ncrit"],
        "--iter",
        case_recipe.get("iter", "80"),
        "--output-dir",
        str(reference_dir),
    ]
    managed_code = 0
    if capture_managed_trace:
        managed_command = [
            "/bin/bash",
            str(TOOLS_DIR / "run_managed_case.sh"),
            "--airfoil",
            case_recipe["airfoil"],
            "--re",
            case_recipe["re"],
            "--alpha",
            case_recipe["alpha"],
            "--panels",
            case_recipe["panels"],
            "--ncrit",
            case_recipe["ncrit"],
            "--iter",
            case_recipe.get("iter", "80"),
            "--output-dir",
            str(managed_dir),
            "--reference-output-dir",
            str(reference_dir),
        ]
    else:
        managed_command = None

    with reference_stdout.open("w", encoding="utf-8") as ref_out, reference_stderr.open("w", encoding="utf-8") as ref_err, managed_stdout.open("w", encoding="utf-8") as man_out, managed_stderr.open("w", encoding="utf-8") as man_err:
        reference_proc = subprocess.Popen(
            reference_command,
            cwd=REPO_ROOT,
            env=env,
            stdout=ref_out,
            stderr=ref_err,
            text=True,
            start_new_session=True,
        )
        managed_proc = None
        if managed_command is not None:
            managed_proc = subprocess.Popen(
                managed_command,
                cwd=REPO_ROOT,
                env=env,
                stdout=man_out,
                stderr=man_err,
                text=True,
                start_new_session=True,
            )

        timeout = capture_timeout_seconds if capture_timeout_seconds > 0 else None
        try:
            reference_code = reference_proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired as exc:
            terminate_process_group(reference_proc)
            if managed_proc is not None:
                terminate_process_group(managed_proc)
            raise RuntimeError(
                f"case capture timed out for {rig['id']} {case_recipe['case_id']} "
                f"after {capture_timeout_seconds}s (reference)"
            ) from exc
        if managed_proc is not None:
            try:
                managed_code = managed_proc.wait(timeout=timeout)
            except subprocess.TimeoutExpired as exc:
                terminate_process_group(managed_proc)
                raise RuntimeError(
                    f"case capture timed out for {rig['id']} {case_recipe['case_id']} "
                    f"after {capture_timeout_seconds}s (managed)"
                ) from exc

    if reference_code != 0 or managed_code != 0:
        raise RuntimeError(
            f"case capture failed for {rig['id']} {case_recipe['case_id']} "
            f"(reference={reference_code}, managed={managed_code})"
        )

    latest_reference = latest_matching_file_cached(str(reference_dir), "reference_trace*.jsonl")
    latest_managed = latest_matching_file_cached(str(managed_dir), "csharp_trace*.jsonl")
    return reference_dir, managed_dir, Path(latest_reference) if latest_reference else (Path(latest_managed) if latest_managed else None)


def capture_case_with_cache(
    rig: dict[str, Any],
    case_recipe: dict[str, str],
    run_dir: Path,
    shared_capture_cache: SharedCaptureCache | None,
    trace_env_override: dict[str, str] | None,
    capture_managed_trace: bool,
    capture_timeout_seconds: int,
) -> tuple[Path, Path | None, bool, str]:
    if shared_capture_cache is None:
        reference_dir, managed_dir, _latest_trace = run_case_capture_parallel(
            rig,
            case_recipe,
            run_dir,
            trace_env_override,
            capture_managed_trace,
            capture_timeout_seconds,
        )
        return reference_dir, managed_dir, False, reference_dir.name

    signature = case_signature(case_recipe)
    cached = shared_capture_cache.captures_by_signature.get(signature)
    if cached is not None:
        shared_capture_cache.reused_count += 1
        return cached[0], cached[1], True, cached[0].name

    shared_case_id = "shared_" + "_".join(sanitize_case_token(token) for token in signature)
    shared_case_recipe = dict(case_recipe)
    shared_case_recipe["case_id"] = shared_case_id
    reference_dir, managed_dir, _latest_trace = run_case_capture_parallel(
        rig,
        shared_case_recipe,
        run_dir,
        trace_env_override,
        capture_managed_trace,
        capture_timeout_seconds,
        SHARED_FULL_MODE_CAPTURE_OWNER,
    )
    write_shared_capture_metadata(
        reference_dir,
        signature,
        trace_env_override if trace_env_override is not None else rig.get("trace_env", {}),
    )
    shared_capture_cache.captures_by_signature[signature] = (reference_dir, managed_dir)
    shared_capture_cache.executed_count += 1
    return reference_dir, managed_dir, False, reference_dir.name


def maybe_expand_trace_corpus(
    rig: dict[str, Any],
    corpus: CorpusSummary,
    run_dir: Path,
    mode: str,
    skip_expand: bool,
    refresh_limit: int,
    shared_capture_cache: SharedCaptureCache | None,
    shared_full_mode_trace_env: dict[str, str] | None,
    capture_timeout_seconds: int,
    refresh_shard_count: int,
    refresh_shard_index: int,
    refresh_offset: int,
) -> tuple[CorpusSummary, int, int]:
    if skip_expand or mode != "full":
        return corpus, 0, 0

    vector_source = rig["vector_source"]
    if vector_source["kind"] != "trace_records":
        return corpus, 0, 0

    if corpus.unique_real_vector_count >= vector_source["min_vector_count"]:
        return corpus, 0, 0

    refresh_cases = refresh_cases_for_rig(rig, refresh_limit, refresh_shard_count, refresh_shard_index, refresh_offset)
    if not refresh_cases:
        return corpus, 0, 0

    executed_refresh_cases = 0
    reused_refresh_cases = 0
    required_vectors = vector_source["min_vector_count"]
    total_refresh_cases = len(refresh_cases)
    progress_interval = 25
    emit_progress(
        f"[micro-rig] expanding {rig['id']} corpus "
        f"({corpus.unique_real_vector_count}/{required_vectors} real vectors, "
        f"{total_refresh_cases} refresh cases queued, "
        f"shard {refresh_shard_index + 1}/{refresh_shard_count}, "
        f"offset {refresh_offset})"
    )
    for case_recipe in refresh_cases:
        try:
            reference_dir, _managed_dir, reused_capture, persisted_case_id = capture_case_with_cache(
                rig,
                case_recipe,
                run_dir,
                shared_capture_cache,
                shared_full_mode_trace_env,
                False,
                capture_timeout_seconds,
            )
        except RuntimeError as exc:
            corpus.source_notes.append(str(exc))
            continue

        executed_refresh_cases += 1
        if reused_capture:
            reused_refresh_cases += 1
        delta_corpus = collect_trace_corpus(
            TOOLS_DIR,
            vector_source,
            [
                PersistedTraceDir(
                    path=reference_dir,
                    case_id=persisted_case_id,
                    owner=rig["id"],
                    trust_class="owner",
                    pattern="refresh_capture",
                    origin_owner=rig["id"],
                    origin_trust_class="owner",
                )
            ],
        )
        corpus = merge_corpus(corpus, delta_corpus)
        if (
            executed_refresh_cases == 1
            or executed_refresh_cases % progress_interval == 0
            or corpus.unique_real_vector_count >= required_vectors
            or executed_refresh_cases == total_refresh_cases
        ):
            emit_progress(
                f"[micro-rig] {rig['id']} refresh "
                f"{executed_refresh_cases}/{total_refresh_cases}: "
                f"{corpus.unique_real_vector_count}/{required_vectors} real vectors "
                f"(reused {reused_refresh_cases})"
            )
        if corpus.unique_real_vector_count >= vector_source["min_vector_count"]:
            break

    return corpus, executed_refresh_cases, reused_refresh_cases


def maybe_triage_failure(
    rig: dict[str, Any],
    run_dir: Path,
    skip_triage: bool,
    refresh_limit: int,
    refresh_shard_count: int,
    refresh_shard_index: int,
    refresh_offset: int,
) -> tuple[str | None, str | None, str | None]:
    if skip_triage or not rig.get("supports_live_compare"):
        return None, None, None

    refresh_cases = refresh_cases_for_rig(rig, refresh_limit, refresh_shard_count, refresh_shard_index, refresh_offset)
    if not refresh_cases:
        return None, None, None

    case_recipe = refresh_cases[0]
    env = os.environ.copy()
    for key, value in rig.get("trace_env", {}).items():
        env[key] = value

    triage_dir = run_dir / "rigs" / rig["id"] / "triage"
    ensure_directory(triage_dir)

    reference_dir = TOOLS_DIR / "reference" / f"micro_rig_matrix_triage_{rig['id']}_{case_recipe['case_id']}_ref"
    managed_dir = TOOLS_DIR / "csharp" / f"micro_rig_matrix_triage_{rig['id']}_{case_recipe['case_id']}_man"
    ensure_directory(reference_dir)
    ensure_directory(managed_dir)

    reference_stdout = triage_dir / "reference.stdout.txt"
    reference_stderr = triage_dir / "reference.stderr.txt"
    reference_command = [
        "/bin/bash",
        str(TOOLS_DIR / "run_reference.sh"),
        "--airfoil",
        case_recipe["airfoil"],
        "--re",
        case_recipe["re"],
        "--alpha",
        case_recipe["alpha"],
        "--panels",
        case_recipe["panels"],
        "--ncrit",
        case_recipe["ncrit"],
        "--iter",
        case_recipe.get("iter", "80"),
        "--output-dir",
        str(reference_dir),
    ]
    reference_result = run_logged_command(reference_command, cwd=REPO_ROOT, stdout_path=reference_stdout, stderr_path=reference_stderr, env=env)
    if reference_result.returncode != 0:
        return None, None, f"reference triage capture failed for {rig['id']}"

    reference_trace = latest_matching_file_cached(str(reference_dir), "reference_trace*.jsonl")
    if reference_trace is None:
        return None, None, f"reference triage trace missing for {rig['id']}"

    managed_stdout = triage_dir / "managed.stdout.txt"
    managed_stderr = triage_dir / "managed.stderr.txt"
    managed_command = [
        "/bin/bash",
        str(TOOLS_DIR / "run_managed_case.sh"),
        "--airfoil",
        case_recipe["airfoil"],
        "--re",
        case_recipe["re"],
        "--alpha",
        case_recipe["alpha"],
        "--panels",
        case_recipe["panels"],
        "--ncrit",
        case_recipe["ncrit"],
        "--iter",
        case_recipe.get("iter", "80"),
        "--output-dir",
        str(managed_dir),
        "--reference-output-dir",
        str(reference_dir),
        "--live-compare",
        "--live-compare-reference",
        reference_trace,
    ]
    managed_result = run_logged_command(managed_command, cwd=REPO_ROOT, stdout_path=managed_stdout, stderr_path=managed_stderr, env=env)
    if managed_result.returncode != 0 and not any(managed_dir.glob("parity_report*.txt")):
        return str(reference_trace), None, f"managed triage run failed for {rig['id']}"

    managed_trace = latest_matching_file_cached(str(managed_dir), "csharp_trace*.jsonl")
    parity_report = latest_matching_file_cached(str(managed_dir), "parity_report*.txt")
    summary = read_parity_report_summary(parity_report)
    return reference_trace, managed_trace, summary


def build_coverage_map(phase1_results: list[dict[str, Any]], backlog: list[dict[str, Any]]) -> list[tuple[str, list[str], list[str]]]:
    covered_by: dict[str, list[str]] = defaultdict(list)
    missing_by: dict[str, list[str]] = defaultdict(list)

    for result in phase1_results:
        for target in result.get("coverage_targets", []):
            covered_by[target].append(result["rig_id"])

    for item in backlog:
        for target in item.get("coverage_targets", []):
            missing_by[target].append(item["id"])

    targets = sorted(set(covered_by) | set(missing_by))
    return [(target, sorted(covered_by.get(target, [])), sorted(missing_by.get(target, []))) for target in targets]


def render_markdown(run_data: dict[str, Any]) -> str:
    lines = [
        "# Micro-Rig Matrix",
        "",
        f"- mode: `{run_data['mode']}`",
        f"- generated_utc: `{run_data['generated_utc']}`",
        f"- run_dir: `{run_data['run_dir']}`",
        f"- canonical_phase1_rigs: `{run_data['summary']['canonical_phase1_total']}`",
        f"- canonical_phase1_promoted_rigs: `{run_data['summary']['canonical_phase1_promoted_total']}`",
        f"- promoted_phase1_rigs: `{run_data['summary']['promoted_phase1_total']}`",
        f"- total_phase1_rigs: `{run_data['summary']['phase1_total']}`",
        f"- expansion_rigs: `{run_data['summary']['phase2_total']}`",
        f"- active_rigs: `{run_data['summary']['active_total']}`",
        f"- green: `{run_data['summary']['green']}`",
        f"- red: `{run_data['summary']['red']}`",
        f"- missing_vectors: `{run_data['summary']['missing_vectors']}`",
        f"- harness_error: `{run_data['summary']['harness_error']}`",
        f"- missing_rig: `{run_data['summary']['missing_rig']}`",
        f"- promoted_green: `{run_data['summary']['promoted_green']}`",
        f"- promoted_red: `{run_data['summary']['promoted_red']}`",
        f"- promoted_missing_vectors: `{run_data['summary']['promoted_missing_vectors']}`",
        f"- promoted_harness_error: `{run_data['summary']['promoted_harness_error']}`",
        f"- promoted_owner_gap: `{run_data['summary']['promoted_owner_gap']}`",
    ]

    pruned_runs = run_data.get("pruned_runs", [])
    if pruned_runs:
        lines.append(f"- pruned_runs: `{len(pruned_runs)}`")

    shared_capture_summary = run_data.get("shared_capture_summary")
    if shared_capture_summary:
        lines.append(f"- shared_captures_executed: `{shared_capture_summary['executed_count']}`")
        lines.append(f"- shared_captures_reused: `{shared_capture_summary['reused_count']}`")
        lines.append(f"- shared_capture_signatures: `{shared_capture_summary['unique_case_signatures']}`")

    lines.extend(
        [
            "",
            "| Rig | Status | Real Vectors | Refresh Cases | Pass | Fail | First Failure | Classification |",
            "| --- | --- | ---: | ---: | ---: | ---: | --- | --- |",
        ]
    )

    for result in run_data["phase1_results"]:
        first_failure = result["first_failure"].get("summary") if result["first_failure"] else ""
        lines.append(
            f"| `{result['rig_id']}` | `{result['status']}` | "
            f"`{result['unique_real_vector_count']}/{result['required_vector_count']}`"
            f" ({result['vector_policy']}) | "
            f"`{result['executed_refresh_case_count']}` | "
            f"`{result['pass_count']}` | `{result['fail_count']}` | {first_failure or ''} | "
            f"`{result['classification'] or ''}` |"
        )

    rows_lacking_owner_vectors = [
        result["rig_id"]
        for result in run_data["phase1_results"]
        if result.get("owner_backed_under_vectorized")
    ]
    if rows_lacking_owner_vectors:
        lines.append(f"- rows_lacking_trustworthy_owner_vectors: `{len(rows_lacking_owner_vectors)}`")
        lines.append(f"- owner_vector_gap_rigs: {', '.join(f'`{item}`' for item in rows_lacking_owner_vectors)}")

    promoted_results = [result for result in run_data["phase1_results"] if result.get("promoted_phase1")]
    if promoted_results:
        lines.extend(
            [
                "",
                "## Promoted Broad Summary",
                "",
                "| Rig | Status | Posture | Class | Fix Mode | Owner Route | Owner-Backed Unique Vectors | Non-Owner Unique Vectors | Owner Gap |",
                "| --- | --- | --- | --- | --- | --- | ---: | ---: | --- |",
            ]
        )
        for result in promoted_results:
            provenance_summary = result.get("provenance_summary") or {}
            owner_route = ", ".join(result.get("owner_rig_ids") or [])
            lines.append(
                f"| `{result['rig_id']}` | `{result['status']}` | "
                f"`{result.get('provenance_posture') or ''}` | "
                f"`{result.get('broad_debug_class') or ''}` | "
                f"`{result.get('owner_fix_mode') or ''}` | "
                f"`{owner_route}` | "
                f"`{provenance_summary.get('owner_real_vector_count', 0)}` | "
                f"`{provenance_summary.get('borrowed_real_vector_count', 0)}` | "
                f"`{bool(result.get('owner_backed_under_vectorized'))}` |"
            )

    if run_data["missing_rigs"]:
        lines.extend(["", "## Missing Rigs", ""])
        for rig in run_data["missing_rigs"]:
            lines.append(f"- `{rig['id']}`: {rig['module_or_boundary']}")

    lines.extend(["", "## Coverage Map", ""])
    for target, covered, missing in run_data["coverage_map"]:
        covered_text = ", ".join(f"`{item}`" for item in covered) if covered else "none"
        missing_text = ", ".join(f"`{item}`" for item in missing) if missing else "none"
        lines.append(f"- `{target}`: covered by {covered_text}; missing rigs {missing_text}")

    return "\n".join(lines) + "\n"


def render_rig_markdown(result: dict[str, Any]) -> str:
    lines = [
        f"# Rig Summary: {result['display_name']}",
        "",
        f"- rig_id: `{result['rig_id']}`",
    ]
    if result.get("parent_rig_id"):
        lines.append(f"- parent_rig_id: `{result['parent_rig_id']}`")
    lines.extend(
        [
        f"- status: `{result['status']}`",
        f"- canonical_phase1_promoted: `{result.get('canonical_phase1_promoted', False)}`",
        f"- module_or_boundary: {result['module_or_boundary']}",
        f"- provenance_posture: `{result['provenance_posture'] or ''}`",
        f"- broad_debug_class: `{result['broad_debug_class'] or ''}`",
        f"- owner_fix_mode: `{result['owner_fix_mode'] or ''}`",
        f"- owner_rig_ids: `{','.join(result.get('owner_rig_ids') or [])}`",
        f"- owner_route_note: {result['owner_route_note'] or ''}",
        f"- test_filter: `{result['test_filter']}`",
        f"- vector_policy: `{result['vector_policy']}`",
        f"- real_vectors: `{result['unique_real_vector_count']}/{result['required_vector_count']}`",
        f"- total_vectors: `{result['vector_count']}`",
        f"- executed_refresh_case_count: `{result['executed_refresh_case_count']}`",
        f"- reused_refresh_case_count: `{result['reused_refresh_case_count']}`",
        f"- available_refresh_case_count: `{result['available_refresh_case_count']}`",
        (
            "- refresh_case_diversity: "
            f"`airfoils={result['refresh_case_diversity']['airfoil_count']},"
            f" re={result['refresh_case_diversity']['re_count']},"
            f" alpha={result['refresh_case_diversity']['alpha_count']},"
            f" panels={result['refresh_case_diversity']['panel_count']},"
            f" ncrit={result['refresh_case_diversity']['ncrit_count']}`"
        ),
        f"- estimated_cases_to_green: `{result['estimated_cases_to_green'] if result['estimated_cases_to_green'] is not None else ''}`",
        f"- owner_backed_under_vectorized: `{result['owner_backed_under_vectorized'] if result['owner_backed_under_vectorized'] is not None else ''}`",
        f"- pass: `{result['pass_count']}`",
        f"- fail: `{result['fail_count']}`",
        f"- skipped: `{result['skipped_count']}`",
        f"- classification: `{result['classification'] or ''}`",
        f"- elapsed_seconds: `{result['elapsed_seconds']}`",
        ]
    )

    first_failure = result.get("first_failure")
    if first_failure:
        lines.extend(
            [
                "",
                "## First Failure",
                "",
                f"- summary: {first_failure.get('summary') or ''}",
                f"- vector_id: `{first_failure.get('vector_id') or ''}`",
                f"- field_or_block: `{first_failure.get('field_or_block') or ''}`",
                f"- parity_report: `{first_failure.get('parity_report') or ''}`",
            ]
        )

    lines.extend(
        [
            "",
            "## Artifacts",
            "",
            f"- corpus: `{result['corpus_artifact']}`",
            f"- managed_artifact: `{result['managed_artifact'] or ''}`",
            f"- reference_artifact: `{result['reference_artifact'] or ''}`",
            f"- stdout: `{result['test_artifacts']['stdout']}`",
            f"- stderr: `{result['test_artifacts']['stderr']}`",
            f"- trx: `{result['test_artifacts']['trx']}`",
        ]
    )

    if result["source_notes"]:
        lines.extend(["", "## Source Notes", ""])
        for note in result["source_notes"]:
            lines.append(f"- {note}")

    provenance_summary = result.get("provenance_summary")
    if provenance_summary:
        lines.extend(
            [
                "",
                "## Provenance",
                "",
                f"- owner_backed_unique_vectors: `{provenance_summary['owner_backed_unique_vector_count']}`",
                f"- non_owner_unique_vectors: `{provenance_summary['non_owner_unique_vector_count']}`",
                f"- shared_only_unique_vectors: `{provenance_summary['shared_only_unique_vector_count']}`",
                f"- borrowed_only_unique_vectors: `{provenance_summary['borrowed_only_unique_vector_count']}`",
                f"- owner_case_count: `{provenance_summary['owner_case_count']}`",
                f"- non_owner_case_count: `{provenance_summary['non_owner_case_count']}`",
            ]
        )
        if provenance_summary["by_owner"]:
            lines.extend(["", "### By Owner", ""])
            for owner, count in provenance_summary["by_owner"].items():
                lines.append(f"- `{owner}`: `{count}`")

    return "\n".join(lines) + "\n"


def write_rig_summary_artifacts(rig_artifact_dir: Path, result: dict[str, Any]) -> tuple[str, str]:
    summary_json_path = rig_artifact_dir / "summary.json"
    summary_md_path = rig_artifact_dir / "summary.md"
    summary_json_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    summary_md_path.write_text(render_rig_markdown(result), encoding="utf-8")
    return str(summary_json_path), str(summary_md_path)


def run_phase1_rig(
    rig: dict[str, Any],
    run_dir: Path,
    mode: str,
    skip_expand: bool,
    skip_triage: bool,
    log_tail_lines: int,
    refresh_limit: int,
    shared_capture_cache: SharedCaptureCache | None,
    shared_full_mode_trace_env: dict[str, str] | None,
    per_rig_timeout_seconds: int,
    capture_timeout_seconds: int,
    refresh_shard_count: int,
    refresh_shard_index: int,
    refresh_offset: int,
) -> dict[str, Any]:
    emit_progress(f"[micro-rig-matrix] start rig={rig['id']} mode={mode}")
    vector_source = rig["vector_source"]
    include_default_persisted_trace_dirs = mode == "full" or rig.get("category") == "phase1"
    include_additional_persisted_trace_dirs = any(
        rig.get(key)
        for key in (
            "additional_persisted_trace_globs",
            "quick_additional_persisted_trace_globs",
            "owner_adopted_persisted_trace_globs",
            "quick_owner_adopted_persisted_trace_globs",
        )
    )
    if mode == "quick" and skip_expand and vector_source.get("kind") != "trace_records":
        cached_corpus = load_cached_corpus_from_summary(rig["id"], exclude_run_dir=run_dir)
    else:
        cached_corpus = None

    corpus = cached_corpus or collect_corpus(
        TOOLS_DIR,
        rig,
        include_default_persisted_trace_dirs=include_default_persisted_trace_dirs,
        include_additional_persisted_trace_dirs=include_additional_persisted_trace_dirs,
        mode=mode,
    )
    available_refresh_cases = refresh_cases_for_rig(rig, refresh_limit, refresh_shard_count, refresh_shard_index, refresh_offset)
    refresh_case_diversity = summarize_refresh_case_diversity(available_refresh_cases)
    corpus, executed_refresh_case_count, reused_refresh_case_count = maybe_expand_trace_corpus(
        rig,
        corpus,
        run_dir,
        mode,
        skip_expand,
        refresh_limit,
        shared_capture_cache,
        shared_full_mode_trace_env,
        capture_timeout_seconds,
        refresh_shard_count,
        refresh_shard_index,
        refresh_offset,
    )
    provenance_summary = summarize_trace_provenance(corpus) if rig["vector_source"]["kind"] == "trace_records" else None
    rig_timeout_seconds = per_rig_timeout_for_mode(rig, mode, per_rig_timeout_seconds)
    test_summary = run_rig_tests(rig, run_dir, mode, log_tail_lines, rig_timeout_seconds)
    reference_artifact, managed_artifact, parity_report = find_latest_artifact_paths(rig)
    triage_reference, triage_managed, triage_summary = (None, None, None)
    if test_summary.fail_count > 0:
        triage_reference, triage_managed, triage_summary = maybe_triage_failure(
            rig,
            run_dir,
            skip_triage,
            refresh_limit,
            refresh_shard_count,
            refresh_shard_index,
            refresh_offset,
        )
        reference_artifact = triage_reference or reference_artifact
        managed_artifact = triage_managed or managed_artifact

    failure_summary = None
    if test_summary.first_failure_message or triage_summary:
        failure_summary = triage_summary or test_summary.first_failure_message

    required = vector_source["min_vector_count"]
    vector_policy = vector_source.get("policy", "standard")
    harness_error = (
        (test_summary.fail_count == 0 and test_summary.pass_count == 0 and bool(test_summary.first_failure_message))
        or is_harness_failure_message(failure_summary)
    )
    if harness_error:
        status = "HARNESS_ERROR"
    elif test_summary.fail_count > 0:
        status = "RED"
    elif corpus.unique_real_vector_count < required:
        status = rig["status_policy"]["missing_vectors_status"]
    else:
        status = "GREEN"

    classification = None
    if status == "RED":
        classification = rig["status_policy"]["default_failure_classification"]
    elif status == "HARNESS_ERROR":
        classification = "harness-side"

    rig_artifact_dir = run_dir / "rigs" / rig["id"]
    ensure_directory(rig_artifact_dir)
    corpus_path = rig_artifact_dir / "corpus.json"
    corpus_path.write_text(
        json.dumps(
            {
                "total_vector_count": corpus.total_vector_count,
                "unique_real_vector_count": corpus.unique_real_vector_count,
                "source_notes": corpus.source_notes,
                "provenance": corpus.provenance,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )

    result = {
        "rig_id": rig["id"],
        "display_name": rig["display_name"],
        "parent_rig_id": rig.get("parent_rig_id"),
        "parent_display_name": rig.get("parent_display_name"),
        "subrig_index": rig.get("subrig_index"),
        "subrig_count": rig.get("subrig_count"),
        "category": rig["category"],
        "promoted_phase1": rig.get("promoted_phase1", False),
        "canonical_phase1_promoted": rig.get("canonical_phase1_promoted", False),
        "module_or_boundary": rig["module_or_boundary"],
        "provenance_posture": rig.get("provenance_posture"),
        "broad_debug_class": rig.get("broad_debug_class"),
        "owner_fix_mode": rig.get("owner_fix_mode"),
        "owner_rig_ids": rig.get("owner_rig_ids", []),
        "owner_route_note": rig.get("owner_route_note"),
        "status": status,
        "test_filter": test_summary.test_filter,
        "vector_count": corpus.total_vector_count,
        "unique_real_vector_count": corpus.unique_real_vector_count,
        "required_vector_count": required,
        "vector_policy": vector_policy,
        "pass_count": test_summary.pass_count,
        "fail_count": test_summary.fail_count,
        "skipped_count": test_summary.skipped_count,
        "classification": classification,
        "first_failure": (
            {
                "summary": failure_summary,
                "vector_id": test_summary.first_failure_vector_id,
                "field_or_block": test_summary.first_failure_field,
                "parity_report": parity_report,
            }
            if failure_summary
            else None
        ),
        "managed_artifact": managed_artifact,
        "reference_artifact": reference_artifact,
        "parity_report": parity_report,
        "test_artifacts": {
            "stdout": test_summary.stdout_path,
            "stderr": test_summary.stderr_path,
            "trx": test_summary.trx_path,
        },
        "elapsed_seconds": round(test_summary.elapsed_seconds, 3),
        "coverage_targets": rig.get("coverage_targets", []),
        "corpus_artifact": str(corpus_path),
        "source_notes": corpus.source_notes,
        "under_vectorized": corpus.unique_real_vector_count < required,
        "owner_backed_under_vectorized": (
            provenance_summary["owner_real_vector_count"] < required
            if provenance_summary is not None
            else None
        ),
        "available_refresh_case_count": len(available_refresh_cases),
        "refresh_case_diversity": refresh_case_diversity,
        "executed_refresh_case_count": executed_refresh_case_count,
        "reused_refresh_case_count": reused_refresh_case_count,
        "estimated_cases_to_green": estimate_cases_to_green(corpus, required),
        "provenance_summary": provenance_summary,
    }
    summary_json, summary_md = write_rig_summary_artifacts(rig_artifact_dir, result)
    result["summary_artifacts"] = {
        "json": summary_json,
        "markdown": summary_md,
    }
    emit_progress(
        f"[micro-rig-matrix] done rig={rig['id']} status={status} "
        f"vectors={corpus.unique_real_vector_count}/{required} pass={test_summary.pass_count} fail={test_summary.fail_count}"
    )
    return result


def summarize_results(
    active_results: list[dict[str, Any]],
    missing_rigs: list[dict[str, Any]],
    canonical_phase1_total: int,
    canonical_phase1_promoted_total: int,
    promoted_phase1_total: int,
) -> dict[str, int]:
    phase1_total = sum(1 for result in active_results if result.get("category") == "phase1")
    phase2_total = sum(1 for result in active_results if result.get("category") == "phase2")
    promoted_results = [result for result in active_results if result.get("promoted_phase1")]
    summary = {
        "canonical_phase1_total": canonical_phase1_total,
        "canonical_phase1_promoted_total": canonical_phase1_promoted_total,
        "promoted_phase1_total": promoted_phase1_total,
        "phase1_total": phase1_total,
        "phase2_total": phase2_total,
        "active_total": len(active_results),
        "green": 0,
        "red": 0,
        "missing_vectors": 0,
        "harness_error": 0,
        "missing_rig": len(missing_rigs),
        "promoted_green": 0,
        "promoted_red": 0,
        "promoted_missing_vectors": 0,
        "promoted_harness_error": 0,
        "promoted_owner_gap": sum(1 for result in promoted_results if result.get("owner_backed_under_vectorized")),
    }
    for result in active_results:
        key = result["status"].lower()
        if key == "green":
            summary["green"] += 1
        elif key == "red":
            summary["red"] += 1
        elif key == "missing_vectors":
            summary["missing_vectors"] += 1
        elif key == "harness_error":
            summary["harness_error"] += 1
        if result.get("promoted_phase1"):
            if key == "green":
                summary["promoted_green"] += 1
            elif key == "red":
                summary["promoted_red"] += 1
            elif key == "missing_vectors":
                summary["promoted_missing_vectors"] += 1
            elif key == "harness_error":
                summary["promoted_harness_error"] += 1
    return summary


def prune_old_runs(output_root: Path, retain_runs: int) -> list[str]:
    run_dirs = sorted(
        [path for path in output_root.iterdir() if path.is_dir() and re.fullmatch(r"\d{8}T\d{6}(?:\d{6})?Z", path.name)],
        key=lambda path: path.name,
        reverse=True,
    )
    removed: list[str] = []
    for path in run_dirs[retain_runs:]:
        shutil.rmtree(path, ignore_errors=True)
        removed.append(str(path))
    return removed


def main() -> int:
    args = parse_args()
    if args.refresh_shard_count < 1:
        raise SystemExit("--refresh-shard-count must be >= 1")
    if args.refresh_shard_index < 0 or args.refresh_shard_index >= args.refresh_shard_count:
        raise SystemExit("--refresh-shard-index must be in [0, --refresh-shard-count)")
    if args.refresh_offset < 0:
        raise SystemExit("--refresh-offset must be >= 0")
    registry = load_registry()
    active_rigs, backlog = select_rigs(registry, args.rig)
    validate_refresh_recipe_diversity(active_rigs)

    run_timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output_root = Path(args.output_dir)
    ensure_directory(output_root)
    run_dir = output_root / run_timestamp
    ensure_directory(run_dir)

    try:
        build_artifacts = prebuild_if_needed(active_rigs, run_dir, args.skip_build, not args.skip_triage)
    except RuntimeError as exc:
        error_data = {
            "mode": args.mode,
            "generated_utc": datetime.now(timezone.utc).isoformat(),
            "error": str(exc),
        }
        (run_dir / "matrix.json").write_text(json.dumps(error_data, indent=2) + "\n", encoding="utf-8")
        (run_dir / "matrix.md").write_text(f"# Micro-Rig Matrix\n\n- error: `{exc}`\n", encoding="utf-8")
        print(str(exc), file=sys.stderr)
        return 1

    shared_full_mode_trace_env = build_shared_full_mode_trace_env(active_rigs) if args.mode == "full" and not args.skip_expand else None
    shared_capture_cache = (
        seed_shared_capture_cache_from_persisted(active_rigs, shared_full_mode_trace_env)
        if shared_full_mode_trace_env is not None
        else None
    )
    active_results = [
        run_phase1_rig(
            rig,
            run_dir,
            args.mode,
            args.skip_expand,
            args.skip_triage,
            args.log_tail_lines,
            args.refresh_limit,
            shared_capture_cache,
            shared_full_mode_trace_env,
            args.per_rig_timeout_seconds,
            args.capture_timeout_seconds,
            args.refresh_shard_count,
            args.refresh_shard_index,
            args.refresh_offset,
        )
        for rig in active_rigs
    ]
    missing_rigs = [
        {
            "id": item["id"],
            "category": item["category"],
            "module_or_boundary": item["module_or_boundary"],
            "status": item["status"],
            "coverage_targets": item.get("coverage_targets", []),
        }
        for item in backlog
    ]

    coverage_map = build_coverage_map(active_results, missing_rigs)
    summary = summarize_results(
        active_results,
        missing_rigs,
        canonical_phase1_total=len(registry["phase1_rigs"]) + len(registry.get("canonical_phase1_promoted_rig_ids", [])),
        canonical_phase1_promoted_total=len(registry.get("canonical_phase1_promoted_rig_ids", [])),
        promoted_phase1_total=len(registry.get("promoted_phase1_rig_ids", [])),
    )

    run_data = {
        "mode": args.mode,
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "build_artifacts": build_artifacts,
        "phase1_results": active_results,
        "missing_rigs": missing_rigs,
        "coverage_map": coverage_map,
        "summary": summary,
        "output_root": str(output_root),
        "run_dir": str(run_dir),
        "shared_capture_summary": (
            {
                "executed_count": shared_capture_cache.executed_count,
                "reused_count": shared_capture_cache.reused_count,
                "unique_case_signatures": len(shared_capture_cache.captures_by_signature),
            }
            if shared_capture_cache is not None
            else None
        ),
    }

    matrix_json_path = run_dir / "matrix.json"
    matrix_md_path = run_dir / "matrix.md"
    coverage_md_path = run_dir / "coverage_map.md"
    matrix_json_path.write_text(json.dumps(run_data, indent=2) + "\n", encoding="utf-8")
    matrix_md_path.write_text(render_markdown(run_data), encoding="utf-8")
    coverage_md_path.write_text(
        "\n".join(
            [f"- `{target}`: covered by {', '.join(covered) or 'none'}; missing rigs {', '.join(missing) or 'none'}" for target, covered, missing in coverage_map]
        )
        + "\n",
        encoding="utf-8",
    )

    latest_json = output_root / "latest.json"
    latest_md = output_root / "latest.md"
    latest_json.write_text(matrix_json_path.read_text(encoding="utf-8"), encoding="utf-8")
    latest_md.write_text(matrix_md_path.read_text(encoding="utf-8"), encoding="utf-8")

    if not args.no_prune_runs:
        pruned_runs = prune_old_runs(output_root, max(args.retain_runs, 1))
        if pruned_runs:
            run_data["pruned_runs"] = pruned_runs
            matrix_json_path.write_text(json.dumps(run_data, indent=2) + "\n", encoding="utf-8")
            matrix_md_path.write_text(render_markdown(run_data), encoding="utf-8")
            latest_json.write_text(matrix_json_path.read_text(encoding="utf-8"), encoding="utf-8")
            latest_md.write_text(matrix_md_path.read_text(encoding="utf-8"), encoding="utf-8")

    statuses = {result["status"] for result in active_results}
    if missing_rigs:
        statuses.add("MISSING_RIG")
    return 0 if statuses == {"GREEN"} else 1


if __name__ == "__main__":
    raise SystemExit(main())
