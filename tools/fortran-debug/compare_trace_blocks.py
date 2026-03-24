#!/usr/bin/env python3
import argparse
import json
from collections import defaultdict
from numbers import Number
from typing import Optional


KEY_FIELDS = (
    "ityp",
    "side",
    "station",
    "iteration",
    "mode",
    "phase",
    "pivotIndex",
    "rowIndex",
    "row",
    "column",
)


def load_records(path: str, kinds: set[str]):
    records = []
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue

            record = json.loads(line)
            if record.get("kind") in kinds:
                records.append(record)

    return records


def record_key(record: dict) -> tuple:
    data = record.get("data") or {}
    key = [record.get("kind"), record.get("scope"), record.get("name")]
    key.extend(data.get(field) for field in KEY_FIELDS)
    return tuple(key)


def compare(reference_path: str, managed_path: str, kinds: set[str]) -> int:
    reference_records = load_records(reference_path, kinds)
    managed_records = load_records(managed_path, kinds)

    reference_map: dict[tuple, list[dict]] = defaultdict(list)
    managed_map: dict[tuple, list[dict]] = defaultdict(list)
    ordered_keys: list[tuple] = []
    seen: set[tuple] = set()

    for record in reference_records:
        key = record_key(record)
        reference_map[key].append(record)
        if key not in seen:
            ordered_keys.append(key)
            seen.add(key)

    for record in managed_records:
        managed_map[record_key(record)].append(record)

    for key in ordered_keys:
        reference_group = reference_map[key]
        managed_group = managed_map.get(key, [])
        if len(reference_group) != len(managed_group):
            print(
                f"count mismatch key={key} reference={len(reference_group)} managed={len(managed_group)}"
            )
            return 1

        for occurrence, (reference_record, managed_record) in enumerate(
            zip(reference_group, managed_group)
        ):
            reference_data = reference_record.get("data") or {}
            managed_data = managed_record.get("data") or {}
            reference_bits = reference_record.get("dataBits") or {}
            managed_bits = managed_record.get("dataBits") or {}

            for field in sorted(set(reference_data) | set(managed_data)):
                reference_value = reference_data.get(field)
                managed_value = managed_data.get(field)
                if field_has_bit_mismatch(field, reference_value, managed_value, reference_bits, managed_bits):
                    print(f"first mismatch key={key} occurrence={occurrence}")
                    print(f"field={field}")
                    print(f"reference={reference_value!r} bits={format_bits(reference_bits.get(field))}")
                    print(f"managed={managed_value!r} bits={format_bits(managed_bits.get(field))}")
                    print(
                        f"reference_sequence={reference_record.get('sequence')} "
                        f"managed_sequence={managed_record.get('sequence')}"
                    )
                    return 1

                if reference_value != managed_value:
                    print(f"first mismatch key={key} occurrence={occurrence}")
                    print(f"field={field}")
                    print(f"reference={reference_value!r}")
                    print(f"managed={managed_value!r}")
                    if field in reference_bits or field in managed_bits:
                        print(f"reference_bits={format_bits(reference_bits.get(field))}")
                        print(f"managed_bits={format_bits(managed_bits.get(field))}")
                    print(
                        f"reference_sequence={reference_record.get('sequence')} "
                        f"managed_sequence={managed_record.get('sequence')}"
                    )
                    return 1

    print("no mismatches in selected kinds")
    return 0


def field_has_bit_mismatch(
    field: str,
    reference_value,
    managed_value,
    reference_bits: dict,
    managed_bits: dict,
) -> bool:
    if not isinstance(reference_value, Number) or not isinstance(managed_value, Number):
        return False

    reference_field_bits = reference_bits.get(field)
    managed_field_bits = managed_bits.get(field)
    if not isinstance(reference_field_bits, dict) or not isinstance(managed_field_bits, dict):
        return False

    for width in ("f32", "f64", "i32", "i64"):
        reference_word = reference_field_bits.get(width)
        managed_word = managed_field_bits.get(width)
        if reference_word is not None and managed_word is not None and reference_word != managed_word:
            return True

    return False


def format_bits(bits: Optional[dict]) -> str:
    if not isinstance(bits, dict):
        return "<none>"

    ordered = []
    for width in ("f32", "f64", "i32", "i64"):
        value = bits.get(width)
        if value is not None:
            ordered.append(f"{width}={value}")

    if not ordered:
        return "<none>"

    return ", ".join(ordered)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("reference")
    parser.add_argument("managed")
    parser.add_argument("--kind", action="append", dest="kinds", required=True)
    args = parser.parse_args()
    return compare(args.reference, args.managed, set(args.kinds))


if __name__ == "__main__":
    raise SystemExit(main())
