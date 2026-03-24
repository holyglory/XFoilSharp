#!/usr/bin/env python3
"""Route full-run parity disparities to the responsible focused micro-rig."""

from __future__ import annotations

import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = REPO_ROOT / "tools" / "fortran-debug"
REGISTRY_PATH = TOOLS_DIR / "micro_rig_registry.json"
RUN_MATRIX_PATH = TOOLS_DIR / "run_micro_rig_matrix.py"


@dataclass
class ParsedDisparityReport:
    parity_report_path: str
    full_text: str
    first_divergence: dict[str, Any] | None
    live_mismatch: dict[str, Any] | None
    reference_mismatch: dict[str, Any] | None
    managed_mismatch: dict[str, Any] | None
    managed_owning_scope: str | None
    managed_parent_scope: str | None
    reference_dump_path: str | None
    managed_dump_path: str | None


def load_registry(path: Path = REGISTRY_PATH) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


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


def materialize_active_rigs(registry: dict[str, Any]) -> list[dict[str, Any]]:
    active: list[dict[str, Any]] = []
    for bucket in ("phase1_rigs", "phase2_rigs"):
        for rig in registry.get(bucket, []):
            active.extend(materialize_rig(rig))
    return active


def normalize_token(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = value.strip()
    if not normalized or normalized == "?":
        return None
    return normalized


def maybe_int(value: str | None) -> int | None:
    value = normalize_token(value)
    if value is None:
        return None
    try:
        return int(value)
    except ValueError:
        return None


def extract_event_line(pattern: str, text: str) -> dict[str, Any] | None:
    match = re.search(pattern, text, re.MULTILINE)
    if not match:
        return None
    return {key: normalize_token(value) for key, value in match.groupdict().items()}


def parse_first_divergence(text: str) -> dict[str, Any] | None:
    match = re.search(
        r"^iter=(?P<iteration>-?\d+)\s+side=(?P<side>-?\d+)\s+station=(?P<station>-?\d+)\s+iv=(?P<iv>-?\d+)\s+category=(?P<category>[A-Z_]+)$",
        text,
        re.MULTILINE,
    )
    if not match:
        return None
    return {
        "iteration": maybe_int(match.group("iteration")),
        "side": maybe_int(match.group("side")),
        "station": maybe_int(match.group("station")),
        "iv": maybe_int(match.group("iv")),
        "category": match.group("category"),
    }


def parse_parity_report(path: str | Path) -> ParsedDisparityReport:
    report_path = Path(path)
    text = report_path.read_text(encoding="utf-8", errors="replace")
    first_divergence = parse_first_divergence(text)
    live_mismatch = extract_event_line(
        r"^Live parity mismatch at kind=(?P<kind>\S+)\s+name=(?P<name>\S+)\s+side=(?P<side>\S+)\s+station=(?P<station>\S+)\s+iteration=(?P<iteration>\S+)\.",
        text,
    )
    reference_mismatch = extract_event_line(
        r"^Reference mismatch event:\s+kind=(?P<kind>\S+)(?:\s+name=(?P<name>\S+))?(?:\s+scope=(?P<scope>\S+))?(?:\s+side=(?P<side>\S+))?(?:\s+station=(?P<station>\S+))?(?:\s+iteration=(?P<iteration>\S+))?(?:\s+iv=(?P<iv>\S+))?",
        text,
    )
    managed_mismatch = extract_event_line(
        r"^Managed mismatch event:\s+kind=(?P<kind>\S+)(?:\s+name=(?P<name>\S+))?(?:\s+scope=(?P<scope>\S+))?(?:\s+side=(?P<side>\S+))?(?:\s+station=(?P<station>\S+))?(?:\s+iteration=(?P<iteration>\S+))?(?:\s+iv=(?P<iv>\S+))?",
        text,
    )
    owning_scope = re.search(r"^Managed owning scope hint:\s+(?P<scope>.+)$", text, re.MULTILINE)
    parent_scope = re.search(r"^Managed parent scope hint:\s+(?P<scope>.+)$", text, re.MULTILINE)
    reference_dump = re.search(r"^referenceDump=(?P<path>.+)$", text, re.MULTILINE)
    managed_dump = re.search(r"^managedDump=(?P<path>.+)$", text, re.MULTILINE)
    return ParsedDisparityReport(
        parity_report_path=str(report_path),
        full_text=text,
        first_divergence=first_divergence,
        live_mismatch=live_mismatch,
        reference_mismatch=reference_mismatch,
        managed_mismatch=managed_mismatch,
        managed_owning_scope=owning_scope.group("scope").strip() if owning_scope else None,
        managed_parent_scope=parent_scope.group("scope").strip() if parent_scope else None,
        reference_dump_path=reference_dump.group("path").strip() if reference_dump else None,
        managed_dump_path=managed_dump.group("path").strip() if managed_dump else None,
    )


def source_station_candidates(source: dict[str, Any]) -> set[int]:
    stations: set[int] = set()
    match = source.get("match", {})
    if isinstance(match, dict) and isinstance(match.get("station"), int):
        stations.add(match["station"])
    match_any = source.get("match_any", {})
    if isinstance(match_any, dict) and isinstance(match_any.get("station"), list):
        stations.update(value for value in match_any["station"] if isinstance(value, int))
    return stations


def source_side_candidates(source: dict[str, Any]) -> set[int]:
    sides: set[int] = set()
    match = source.get("match", {})
    if isinstance(match, dict) and isinstance(match.get("side"), int):
        sides.add(match["side"])
    match_any = source.get("match_any", {})
    if isinstance(match_any, dict) and isinstance(match_any.get("side"), list):
        sides.update(value for value in match_any["side"] if isinstance(value, int))
    return sides


def extract_scope_owner(scope_hint: str | None) -> str | None:
    scope_hint = normalize_token(scope_hint)
    if scope_hint is None:
        return None
    return scope_hint.split("(", 1)[0].split(".", 1)[0]


def coverage_target_stems(rig: dict[str, Any]) -> set[str]:
    stems: set[str] = set()
    for target in rig.get("coverage_targets", []):
        stems.add(Path(target).stem)
    return stems


def report_events(parsed: ParsedDisparityReport) -> list[dict[str, Any]]:
    events = []
    for candidate in (parsed.managed_mismatch, parsed.reference_mismatch, parsed.live_mismatch):
        if candidate is not None:
            events.append(candidate)
    return events


def value_matches(expected_values: list[str] | None, actual_value: str | None) -> bool:
    actual_value = normalize_token(actual_value)
    if not expected_values or actual_value is None:
        return False
    return actual_value in expected_values


def score_rig(parsed: ParsedDisparityReport, rig: dict[str, Any]) -> tuple[int, list[str]]:
    score = 0
    reasons: list[str] = []
    seen_reason_keys: set[tuple[str, str]] = set()
    routing_hints = rig.get("routing_hints", {})
    events = report_events(parsed)

    def add_score(points: int, reason: str, key: tuple[str, str]) -> None:
        nonlocal score
        if key in seen_reason_keys:
            return
        seen_reason_keys.add(key)
        score += points
        reasons.append(reason)

    for source in rig.get("vector_source", {}).get("sources", []):
        for event in events:
            if source.get("record_kind") and event.get("kind") == source.get("record_kind"):
                add_score(90, f"trace kind {event['kind']} matches vector source", ("source-kind", str(source.get("record_kind"))))
            if source.get("name") and event.get("name") == source.get("name"):
                add_score(50, f"trace name {event['name']} matches vector source", ("source-name", str(source.get("name"))))
            if source.get("scope") and event.get("scope") == source.get("scope"):
                add_score(50, f"trace scope {event['scope']} matches vector source", ("source-scope", str(source.get("scope"))))

            event_station = maybe_int(event.get("station"))
            event_side = maybe_int(event.get("side"))
            source_stations = source_station_candidates(source)
            source_sides = source_side_candidates(source)
            if event_station is not None and event_station in source_stations:
                add_score(25, f"station {event_station} matches vector source", ("source-station", str(event_station)))
            if event_side is not None and event_side in source_sides:
                add_score(20, f"side {event_side} matches vector source", ("source-side", str(event_side)))

    first_divergence = parsed.first_divergence or {}
    if value_matches(routing_hints.get("dump_categories"), first_divergence.get("category")):
        add_score(70, f"dump category {first_divergence.get('category')} matches routing hint", ("dump-category", str(first_divergence.get("category"))))

    if value_matches(routing_hints.get("trace_kinds"), next((event.get("kind") for event in events if event.get("kind")), None)):
        add_score(70, "live/reference trace kind matches routing hint", ("routing-trace-kind", ",".join(routing_hints.get("trace_kinds", []))))

    for event in events:
        if value_matches(routing_hints.get("trace_names"), event.get("name")):
            add_score(45, f"trace name {event.get('name')} matches routing hint", ("routing-trace-name", str(event.get("name"))))
        if value_matches(routing_hints.get("trace_scopes"), event.get("scope")):
            add_score(45, f"trace scope {event.get('scope')} matches routing hint", ("routing-trace-scope", str(event.get("scope"))))

    if value_matches(routing_hints.get("owning_scopes"), parsed.managed_owning_scope):
        add_score(60, f"managed owning scope {parsed.managed_owning_scope} matches routing hint", ("owning-scope", str(parsed.managed_owning_scope)))
    if value_matches(routing_hints.get("parent_scopes"), parsed.managed_parent_scope):
        add_score(35, f"managed parent scope {parsed.managed_parent_scope} matches routing hint", ("parent-scope", str(parsed.managed_parent_scope)))

    owner = extract_scope_owner(parsed.managed_owning_scope)
    if owner and owner in coverage_target_stems(rig):
        add_score(30, f"coverage target {owner}.cs matches owning scope", ("coverage-owner", owner))

    parent_owner = extract_scope_owner(parsed.managed_parent_scope)
    if parent_owner and parent_owner in coverage_target_stems(rig):
        add_score(20, f"coverage target {parent_owner}.cs matches parent scope", ("coverage-parent", parent_owner))

    full_text = parsed.full_text
    for keyword in routing_hints.get("report_keywords", []):
        if keyword in full_text:
            add_score(35, f"report keyword '{keyword}' matched", ("report-keyword", keyword))

    if first_divergence:
        divergence_station = first_divergence.get("station")
        if isinstance(divergence_station, int):
            for source in rig.get("vector_source", {}).get("sources", []):
                stations = source_station_candidates(source)
                if divergence_station in stations:
                    add_score(15, f"first divergence station {divergence_station} overlaps source station", ("divergence-station", str(divergence_station)))
                    break

    return score, reasons


def sort_candidates(parsed: ParsedDisparityReport, rigs: list[dict[str, Any]]) -> list[dict[str, Any]]:
    scored: list[dict[str, Any]] = []
    for rig in rigs:
        score, reasons = score_rig(parsed, rig)
        if score <= 0:
            continue
        scored.append(
            {
                "id": rig["id"],
                "display_name": rig.get("display_name", rig["id"]),
                "module_or_boundary": rig.get("module_or_boundary"),
                "status": rig.get("status", "ACTIVE"),
                "category": rig.get("category", "active"),
                "score": score,
                "reasons": reasons,
                "rig": rig,
            }
        )

    def sort_key(item: dict[str, Any]) -> tuple[int, int, str]:
        routing_priority = item["rig"].get("routing_hints", {}).get("priority", 100)
        return (-item["score"], routing_priority, item["id"])

    return sorted(scored, key=sort_key)


def find_source_file_stems() -> dict[str, str]:
    mapping: dict[str, str] = {}
    for path in REPO_ROOT.glob("src/**/*.cs"):
        mapping[path.stem] = str(path)
    return mapping


def suggest_missing_rig(parsed: ParsedDisparityReport) -> dict[str, Any]:
    first = parsed.first_divergence or {}
    event = parsed.managed_mismatch or parsed.reference_mismatch or parsed.live_mismatch or {}
    base_name = event.get("kind") or first.get("category") or "uncovered-disparity"
    normalized = re.sub(r"[^a-z0-9]+", "-", str(base_name).lower()).strip("-") or "uncovered-disparity"
    if first.get("category") == "VSREZ":
        rig_id = "vsrez-upstream"
        module = "Upstream VSREZ producer chain oracle"
    else:
        station = first.get("station")
        station_suffix = f"-station{station}" if isinstance(station, int) and station > 0 else ""
        rig_id = f"{normalized}{station_suffix}"
        module = f"Uncovered disparity from {base_name}"

    source_files = find_source_file_stems()
    coverage_targets: list[str] = []
    for scope_hint in (parsed.managed_owning_scope, parsed.managed_parent_scope):
        owner = extract_scope_owner(scope_hint)
        if owner and owner in source_files and source_files[owner] not in coverage_targets:
            coverage_targets.append(source_files[owner])

    class_name = "".join(part.capitalize() for part in rig_id.replace("-", " ").split()) + "MicroParityTests"
    return {
        "id": rig_id,
        "category": "future",
        "module_or_boundary": module,
        "status": "MISSING_RIG",
        "coverage_targets": coverage_targets,
        "suggested_test_file": str(REPO_ROOT / "tests" / "XFoil.Core.Tests" / "FortranParity" / f"{class_name}.cs"),
        "suggested_test_class": class_name,
        "trigger": {
            "first_divergence": first,
            "managed_owning_scope": parsed.managed_owning_scope,
            "managed_parent_scope": parsed.managed_parent_scope,
            "live_kind": event.get("kind"),
            "live_name": event.get("name"),
            "live_scope": event.get("scope"),
        },
    }


def parse_matrix_result(path: Path, rig_id: str) -> dict[str, Any] | None:
    if not path.exists():
        return None
    payload = json.loads(path.read_text(encoding="utf-8"))
    for result in payload.get("phase1_results", []):
        if result.get("rig_id") == rig_id or result.get("parent_rig_id") == rig_id:
            return result
    return None


def run_quick_rig(rig_id: str, output_root: Path, skip_build: bool = True, skip_triage: bool = True) -> dict[str, Any]:
    command = [sys.executable, str(RUN_MATRIX_PATH), "--mode", "quick", "--rig", rig_id, "--output-dir", str(output_root)]
    if skip_build:
        command.append("--skip-build")
    if skip_triage:
        command.append("--skip-triage")

    completed = subprocess.run(
        command,
        cwd=REPO_ROOT,
        text=True,
        capture_output=True,
        check=False,
    )

    latest_json = output_root / "latest.json"
    result = parse_matrix_result(latest_json, rig_id)
    return {
        "command": command,
        "returncode": completed.returncode,
        "stdout": completed.stdout,
        "stderr": completed.stderr,
        "result": result,
        "latest_json": str(latest_json),
        "latest_md": str(output_root / "latest.md"),
    }


def route_disparity(parsed: ParsedDisparityReport, registry: dict[str, Any], quick_output_root: Path | None = None) -> dict[str, Any]:
    active_candidates = sort_candidates(parsed, materialize_active_rigs(registry))
    backlog_candidates = sort_candidates(parsed, registry.get("future_backlog", []))

    best_active = active_candidates[0] if active_candidates else None
    best_backlog = backlog_candidates[0] if backlog_candidates else None
    use_active = best_active is not None and (
        best_backlog is None or best_active["score"] >= best_backlog["score"]
    )
    responsible = best_active if use_active else None
    missing = best_backlog

    route_status = "RESPONSIBLE_RIG" if responsible is not None else "MISSING_RIG"
    missing_template = suggest_missing_rig(parsed) if responsible is None else None

    quick_probe = None
    if responsible is not None and quick_output_root is not None:
        quick_probe = run_quick_rig(responsible["id"], quick_output_root)

    return {
        "route_status": route_status,
        "register_first": True,
        "parity_report": parsed.parity_report_path,
        "first_divergence": parsed.first_divergence,
        "live_mismatch": parsed.live_mismatch,
        "reference_mismatch": parsed.reference_mismatch,
        "managed_mismatch": parsed.managed_mismatch,
        "managed_owning_scope": parsed.managed_owning_scope,
        "managed_parent_scope": parsed.managed_parent_scope,
        "reference_dump": parsed.reference_dump_path,
        "managed_dump": parsed.managed_dump_path,
        "responsible_rig": (
            {
                "id": responsible["id"],
                "display_name": responsible["display_name"],
                "module_or_boundary": responsible["module_or_boundary"],
                "reasons": responsible["reasons"],
                "score": responsible["score"],
                "run_micro_rig_matrix_command": [
                    sys.executable,
                    str(RUN_MATRIX_PATH),
                    "--mode",
                    "quick",
                    "--rig",
                    responsible["id"],
                    "--skip-build",
                ],
            }
            if responsible is not None
            else None
        ),
        "candidate_rigs": [
            {
                "id": item["id"],
                "module_or_boundary": item["module_or_boundary"],
                "score": item["score"],
                "reasons": item["reasons"],
            }
            for item in active_candidates[:5]
        ],
        "missing_rig_candidate": (
            {
                "id": missing["id"],
                "module_or_boundary": missing["module_or_boundary"],
                "score": missing["score"],
                "reasons": missing["reasons"],
            }
            if missing is not None
            else None
        ),
        "missing_rig_template": missing_template,
        "quick_probe": quick_probe,
    }


def render_route_markdown(route: dict[str, Any]) -> str:
    lines = [
        "# Responsible Rig Route",
        "",
        f"- route_status: `{route['route_status']}`",
        f"- register_first: `{route['register_first']}`",
        f"- parity_report: `{route['parity_report']}`",
    ]

    first = route.get("first_divergence")
    if first:
        lines.extend(
            [
                "",
                "## First Divergence",
                "",
                f"- category: `{first.get('category')}`",
                f"- iteration: `{first.get('iteration')}`",
                f"- side: `{first.get('side')}`",
                f"- station: `{first.get('station')}`",
                f"- iv: `{first.get('iv')}`",
            ]
        )

    responsible = route.get("responsible_rig")
    if responsible:
        lines.extend(
            [
                "",
                "## Responsible Rig",
                "",
                f"- id: `{responsible['id']}`",
                f"- module_or_boundary: {responsible['module_or_boundary']}",
                f"- score: `{responsible['score']}`",
            ]
        )
        for reason in responsible["reasons"]:
            lines.append(f"- reason: {reason}")

    quick_probe = route.get("quick_probe")
    if quick_probe and quick_probe.get("result"):
        result = quick_probe["result"]
        lines.extend(
            [
                "",
                "## Quick Probe",
                "",
                f"- rig: `{result['rig_id']}`",
                f"- status: `{result['status']}`",
                f"- vectors: `{result['unique_real_vector_count']}/{result['required_vector_count']}`",
                f"- pass: `{result['pass_count']}`",
                f"- fail: `{result['fail_count']}`",
                f"- matrix_json: `{quick_probe['latest_json']}`",
            ]
        )

    if route.get("missing_rig_candidate") or route.get("missing_rig_template"):
        lines.extend(["", "## Missing Rig Path", ""])
        candidate = route.get("missing_rig_candidate")
        if candidate:
            lines.append(f"- backlog_candidate: `{candidate['id']}` ({candidate['module_or_boundary']})")
            for reason in candidate["reasons"]:
                lines.append(f"- backlog_reason: {reason}")

        template = route.get("missing_rig_template")
        if template:
            lines.append(f"- suggested_id: `{template['id']}`")
            lines.append(f"- suggested_test_file: `{template['suggested_test_file']}`")
            lines.append(f"- suggested_test_class: `{template['suggested_test_class']}`")

    if route.get("candidate_rigs"):
        lines.extend(["", "## Candidate Rigs", ""])
        for item in route["candidate_rigs"]:
            lines.append(f"- `{item['id']}` score={item['score']}: {item['module_or_boundary']}")

    return "\n".join(lines) + "\n"
