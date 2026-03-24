#!/usr/bin/env python3
"""Compare one structured trace event between Fortran and C# artifacts."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", help="Path to the Fortran JSONL trace")
    parser.add_argument("managed", help="Path to the C# JSONL trace")
    parser.add_argument("--kind", required=True, help="Trace event kind")
    parser.add_argument("--name", help="Trace event name")
    parser.add_argument("--scope", help="Trace event scope")
    parser.add_argument(
        "--tag",
        action="append",
        default=[],
        metavar="KEY=VALUE",
        help="Tag or data-field filter; can be repeated",
    )
    parser.add_argument(
        "--index",
        type=int,
        default=0,
        help="Zero-based index within the matching records",
    )
    parser.add_argument(
        "--show-all",
        action="store_true",
        help="Print all differing fields instead of stopping at the first",
    )
    parser.add_argument(
        "--walk",
        action="store_true",
        help="Compare all matching records by ordinal and stop at the first mismatch",
    )
    parser.add_argument(
        "--abs-threshold",
        type=float,
        default=0.0,
        help="Ignore numeric mismatches whose absolute delta does not exceed this threshold",
    )
    parser.add_argument(
        "--none-is-zero",
        action="store_true",
        help="Treat null and numeric zero as equivalent for debug-only optional fields",
    )
    return parser.parse_args()


def coerce_value(raw: str) -> Any:
    lowered = raw.lower()
    if lowered == "true":
        return True
    if lowered == "false":
        return False
    if lowered == "null":
        return None
    try:
        if any(ch in raw for ch in ".eE"):
            return float(raw)
        return int(raw)
    except ValueError:
        return raw


def parse_filters(raw_filters: list[str]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for entry in raw_filters:
        if "=" not in entry:
            raise SystemExit(f"Invalid --tag value: {entry!r}")
        key, value = entry.split("=", 1)
        result[key] = coerce_value(value)
    return result


def iter_records(path: str):
    with Path(path).open("r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            yield json.loads(line)


def get_field(record: dict[str, Any], dotted_key: str) -> Any:
    data = record.get("data")
    tags = record.get("tags") or {}
    if dotted_key in tags:
        return tags[dotted_key]
    if isinstance(data, dict) and dotted_key in data:
        return data[dotted_key]

    current: Any = data
    for segment in dotted_key.split("."):
        if not isinstance(current, dict) or segment not in current:
            raise KeyError(dotted_key)
        current = current[segment]
    return current


def values_match(actual: Any, expected: Any) -> bool:
    if isinstance(actual, (int, float)) and isinstance(expected, (int, float)):
        return actual == expected
    return actual == expected


def select_records(records, args: argparse.Namespace, filters: dict[str, Any]) -> list[dict[str, Any]]:
    matches: list[dict[str, Any]] = []
    for record in records:
        if record.get("kind") != args.kind:
            continue
        record_name = record.get("name")
        if args.name is not None and (
            not isinstance(record_name, str)
            or record_name.lower() != args.name.lower()
        ):
            continue
        record_scope = record.get("scope")
        if args.scope is not None and (
            not isinstance(record_scope, str)
            or record_scope.lower() != args.scope.lower()
        ):
            continue

        try:
            if all(values_match(get_field(record, key), expected) for key, expected in filters.items()):
                matches.append(record)
        except KeyError:
            continue
    return matches


def flatten(prefix: str, value: Any, output: dict[str, Any]) -> None:
    if isinstance(value, dict):
        for key in sorted(value.keys()):
            child_prefix = f"{prefix}.{key}" if prefix else key
            flatten(child_prefix, value[key], output)
        return
    if isinstance(value, list):
        for index, item in enumerate(value):
            child_prefix = f"{prefix}[{index}]"
            flatten(child_prefix, item, output)
        return
    output[prefix] = value


def format_value(value: Any) -> str:
    if isinstance(value, float):
        if math.isnan(value):
            return "nan"
        return f"{value:.17g}"
    return repr(value)


def are_equivalent_none_zero(ref_value: Any, managed_value: Any, none_is_zero: bool) -> bool:
    if not none_is_zero:
        return False
    return (
        (ref_value is None and isinstance(managed_value, (int, float)) and managed_value == 0)
        or (managed_value is None and isinstance(ref_value, (int, float)) and ref_value == 0)
    )


def compare_records(
    reference: dict[str, Any],
    managed: dict[str, Any],
    show_all: bool,
    abs_threshold: float,
    none_is_zero: bool,
) -> int:
    ref_fields: dict[str, Any] = {}
    managed_fields: dict[str, Any] = {}
    flatten("data", reference.get("data"), ref_fields)
    flatten("data", managed.get("data"), managed_fields)
    flatten("tags", reference.get("tags") or {}, ref_fields)
    flatten("tags", managed.get("tags") or {}, managed_fields)

    keys = sorted(set(ref_fields) | set(managed_fields))
    mismatch_count = 0
    for key in keys:
        ref_value = ref_fields.get(key, "<missing>")
        managed_value = managed_fields.get(key, "<missing>")
        if are_equivalent_none_zero(ref_value, managed_value, none_is_zero):
            continue
        if (
            isinstance(ref_value, (int, float))
            and isinstance(managed_value, (int, float))
            and abs(managed_value - ref_value) <= abs_threshold
        ):
            continue
        if ref_value != managed_value:
            mismatch_count += 1
            print(f"{key}:")
            print(f"  fortran = {format_value(ref_value)}")
            print(f"  csharp  = {format_value(managed_value)}")
            if isinstance(ref_value, (int, float)) and isinstance(managed_value, (int, float)):
                diff = managed_value - ref_value
                print(f"  delta   = {format_value(diff)}")
            if not show_all:
                break
    if mismatch_count == 0:
        print("records match exactly")
    return mismatch_count


def main() -> int:
    args = parse_args()
    filters = parse_filters(args.tag)

    ref_matches = select_records(iter_records(args.reference), args, filters)
    managed_matches = select_records(iter_records(args.managed), args, filters)

    print(f"reference matches: {len(ref_matches)}")
    print(f"managed matches:   {len(managed_matches)}")

    if args.walk:
        pair_count = min(len(ref_matches), len(managed_matches))
        for ordinal in range(pair_count):
            print(f"ordinal {ordinal}:")
            reference = ref_matches[ordinal]
            managed = managed_matches[ordinal]
            print(f"  reference sequence: {reference.get('sequence')}")
            print(f"  managed sequence:   {managed.get('sequence')}")
            mismatches = compare_records(reference, managed, False, args.abs_threshold, args.none_is_zero)
            if mismatches:
                return 1
        if len(ref_matches) != len(managed_matches):
            print("record count mismatch after walking matched ordinals")
            return 2
        print("all walked records match exactly")
        return 0

    if len(ref_matches) <= args.index or len(managed_matches) <= args.index:
        return 2

    reference = ref_matches[args.index]
    managed = managed_matches[args.index]

    print(f"reference sequence: {reference.get('sequence')}")
    print(f"managed sequence:   {managed.get('sequence')}")
    return 1 if compare_records(reference, managed, args.show_all, args.abs_threshold, args.none_is_zero) else 0


if __name__ == "__main__":
    raise SystemExit(main())
