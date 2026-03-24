#!/usr/bin/env python3
"""Attach exact integer/float bit metadata to JSONL trace records."""

from __future__ import annotations

import math
import struct
from typing import Any


def augment_record(record: dict[str, Any]) -> dict[str, Any]:
    """Return a shallow-cloned trace record with bit metadata fields added."""
    augmented = dict(record)

    data_bits = build_bits_map(augmented.get("data"))
    if data_bits:
        augmented["dataBits"] = data_bits

    tags_bits = build_bits_map(augmented.get("tags"))
    if tags_bits:
        augmented["tagsBits"] = tags_bits

    values_bits = build_values_bits(augmented.get("values"))
    if values_bits:
        augmented["valuesBits"] = values_bits

    return augmented


def build_bits_map(value: Any) -> dict[str, dict[str, str]]:
    bits: dict[str, dict[str, str]] = {}
    _append_bits(value, prefix=None, destination=bits)
    return bits


def build_values_bits(values: Any) -> list[dict[str, str]]:
    if not isinstance(values, list):
        return []

    result: list[dict[str, str]] = []
    for value in values:
        result.append(_describe_number(value) if _is_numeric(value) else {})
    return result


def _append_bits(value: Any, prefix: str | None, destination: dict[str, dict[str, str]]) -> None:
    if isinstance(value, dict):
        for key, item in value.items():
            child_prefix = key if not prefix else f"{prefix}.{key}"
            _append_bits(item, child_prefix, destination)
        return

    if isinstance(value, list):
        for index, item in enumerate(value):
            child_prefix = f"[{index}]" if not prefix else f"{prefix}[{index}]"
            _append_bits(item, child_prefix, destination)
        return

    if prefix and _is_numeric(value):
        destination[prefix] = _describe_number(value)


def _is_numeric(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def _describe_number(value: int | float) -> dict[str, str]:
    if isinstance(value, int):
        bits: dict[str, str] = {}
        if -(2**31) <= value <= (2**31) - 1:
            bits["i32"] = f"0x{value & 0xFFFFFFFF:08X}"
        if -(2**63) <= value <= (2**63) - 1:
            bits["i64"] = f"0x{value & 0xFFFFFFFFFFFFFFFF:016X}"
        else:
            bits["decimal"] = str(value)
        return bits

    if not math.isfinite(value):
        return {"special": repr(value)}

    float32_bits = struct.unpack(">I", struct.pack(">f", float(value)))[0]
    float64_bits = struct.unpack(">Q", struct.pack(">d", float(value)))[0]
    return {
        "f32": f"0x{float32_bits:08X}",
        "f64": f"0x{float64_bits:016X}",
    }
