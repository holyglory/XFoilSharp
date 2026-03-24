#!/usr/bin/env python3
"""Filter JSONL parity traces with semantic selectors and an optional ring buffer."""

from __future__ import annotations

import argparse
import json
import os
import sys
from collections import deque
from dataclasses import dataclass
from typing import Any

from trace_bits import augment_record


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, help="Destination JSONL file path")
    return parser.parse_args()


def parse_set(name: str) -> set[str] | None:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return None

    values = {part.strip() for part in raw.split(",") if part.strip()}
    return values or None


def parse_int(name: str) -> int | None:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return None

    try:
        return int(raw)
    except ValueError:
        return None


def parse_str(name: str) -> str | None:
    raw = os.environ.get(name, "").strip()
    return raw or None


def parse_pairs(name: str) -> dict[str, str] | None:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return None

    result: dict[str, str] = {}
    for token in raw.split(";"):
        token = token.strip()
        if not token or "=" not in token:
            continue

        key, value = token.split("=", 1)
        key = key.strip()
        value = value.strip()
        if not key or not value:
            continue

        result[key] = value

    return result or None


def get_data_field(record: dict[str, Any], key: str) -> Any:
    data = record.get("data")
    if isinstance(data, dict):
        return data.get(key)

    return None


def normalize_field_value(value: Any) -> str | None:
    if value is None:
        return None

    if isinstance(value, bool):
        return "True" if value else "False"

    return str(value)


@dataclass(frozen=True)
class Selector:
    kinds: set[str] | None = None
    scopes: set[str] | None = None
    names: set[str] | None = None
    data_match: dict[str, str] | None = None
    side: int | None = None
    station: int | None = None
    iteration: int | None = None
    iteration_min: int | None = None
    iteration_max: int | None = None
    mode: str | None = None

    @property
    def active(self) -> bool:
        return any(
            value is not None
            for value in (
                self.kinds,
                self.scopes,
                self.names,
                self.data_match,
                self.side,
                self.station,
                self.iteration,
                self.iteration_min,
                self.iteration_max,
                self.mode,
            )
        )

    def matches(self, record: dict[str, Any]) -> bool:
        if self.kinds is not None and record.get("kind") not in self.kinds:
            return False

        if self.scopes is not None and record.get("scope") not in self.scopes:
            return False

        if self.names is not None and record.get("name") not in self.names:
            return False

        if self.data_match is not None:
            for key, expected in self.data_match.items():
                actual = normalize_field_value(get_data_field(record, key))
                if actual != expected:
                    return False

        if self.side is not None and get_data_field(record, "side") != self.side:
            return False

        if self.station is not None and get_data_field(record, "station") != self.station:
            return False

        if self.iteration is not None and get_data_field(record, "iteration") != self.iteration:
            return False

        iteration_value = get_data_field(record, "iteration")
        if self.iteration_min is not None:
            if iteration_value is None or iteration_value < self.iteration_min:
                return False

        if self.iteration_max is not None:
            if iteration_value is None or iteration_value > self.iteration_max:
                return False

        if self.mode is not None and get_data_field(record, "mode") != self.mode:
            return False

        return True

    @staticmethod
    def from_environment(
        *,
        kind_var: str,
        scope_var: str,
        name_var: str,
        data_match_var: str,
        side_var: str,
        station_var: str,
        iteration_var: str,
        iteration_min_var: str,
        iteration_max_var: str,
        mode_var: str,
    ) -> "Selector | None":
        selector = Selector(
            kinds=parse_set(kind_var),
            scopes=parse_set(scope_var),
            names=parse_set(name_var),
            data_match=parse_pairs(data_match_var),
            side=parse_int(side_var),
            station=parse_int(station_var),
            iteration=parse_int(iteration_var),
            iteration_min=parse_int(iteration_min_var),
            iteration_max=parse_int(iteration_max_var),
            mode=parse_str(mode_var),
        )
        return selector if selector.active else None


class TraceFilter:
    def __init__(
        self,
        capture_selector: Selector | None,
        trigger_selector: Selector | None,
        trigger_occurrence: int | None,
        ring_buffer: int,
        post_trigger_limit: int | None,
    ):
        self.capture_selector = capture_selector
        self.trigger_selector = trigger_selector
        self.trigger_occurrence = trigger_occurrence if trigger_occurrence and trigger_occurrence > 0 else None
        self.ring_buffer = max(ring_buffer, 0)
        self.post_trigger_limit = post_trigger_limit if post_trigger_limit is not None and post_trigger_limit >= 0 else None
        self.triggered = trigger_selector is None
        self.buffer: deque[dict[str, Any]] = deque(maxlen=self.ring_buffer if self.ring_buffer > 0 else None)
        self.trigger_match_count = 0
        self.post_trigger_count = 0

    @property
    def enabled(self) -> bool:
        return (
            self.capture_selector is not None
            or self.trigger_selector is not None
            or self.ring_buffer > 0
        )

    def matches_capture(self, record: dict[str, Any]) -> bool:
        if self.capture_selector is None:
            return True
        return self.capture_selector.matches(record)

    def is_trigger(self, record: dict[str, Any]) -> bool:
        if self.trigger_selector is None:
            return False
        return self.trigger_selector.matches(record)

    def emit(self, record: dict[str, Any], output_handle) -> None:
        output_handle.write(json.dumps(augment_record(record), separators=(",", ":")) + "\n")

    def process(self, record: dict[str, Any], output_handle) -> None:
        kind = record.get("kind")
        if kind in {"session_start", "session_end"}:
            self.emit(record, output_handle)
            return

        capture = self.matches_capture(record)
        if self.trigger_selector is None:
            if capture:
                self.emit(record, output_handle)
            return

        trigger_selector_match = self.is_trigger(record)
        trigger = False
        if trigger_selector_match:
            self.trigger_match_count += 1
            trigger = self.trigger_occurrence is None or self.trigger_match_count == self.trigger_occurrence

        if self.triggered:
            if capture or trigger_selector_match:
                self.write_post_trigger(record, output_handle)
            return

        if trigger:
            while self.buffer:
                self.emit(self.buffer.popleft(), output_handle)
            self.emit(record, output_handle)
            self.triggered = True
            return

        if capture and self.ring_buffer > 0:
            self.buffer.append(record)

    def write_post_trigger(self, record: dict[str, Any], output_handle) -> None:
        if self.post_trigger_limit is not None and self.post_trigger_count >= self.post_trigger_limit:
            return

        self.emit(record, output_handle)
        self.post_trigger_count += 1


def build_filter() -> TraceFilter:
    capture = Selector.from_environment(
        kind_var="XFOIL_TRACE_KIND_ALLOW",
        scope_var="XFOIL_TRACE_SCOPE_ALLOW",
        name_var="XFOIL_TRACE_NAME_ALLOW",
        data_match_var="XFOIL_TRACE_DATA_MATCH",
        side_var="XFOIL_TRACE_SIDE",
        station_var="XFOIL_TRACE_STATION",
        iteration_var="XFOIL_TRACE_ITERATION",
        iteration_min_var="XFOIL_TRACE_ITER_MIN",
        iteration_max_var="XFOIL_TRACE_ITER_MAX",
        mode_var="XFOIL_TRACE_MODE",
    )

    trigger = Selector.from_environment(
        kind_var="XFOIL_TRACE_TRIGGER_KIND",
        scope_var="XFOIL_TRACE_TRIGGER_SCOPE",
        name_var="XFOIL_TRACE_TRIGGER_NAME_ALLOW",
        data_match_var="XFOIL_TRACE_TRIGGER_DATA_MATCH",
        side_var="XFOIL_TRACE_TRIGGER_SIDE",
        station_var="XFOIL_TRACE_TRIGGER_STATION",
        iteration_var="XFOIL_TRACE_TRIGGER_ITERATION",
        iteration_min_var="XFOIL_TRACE_TRIGGER_ITER_MIN",
        iteration_max_var="XFOIL_TRACE_TRIGGER_ITER_MAX",
        mode_var="XFOIL_TRACE_TRIGGER_MODE",
    )

    ring_buffer = parse_int("XFOIL_TRACE_RING_BUFFER") or 0
    trigger_occurrence = parse_int("XFOIL_TRACE_TRIGGER_OCCURRENCE")
    post_trigger_limit = parse_int("XFOIL_TRACE_POST_LIMIT")
    return TraceFilter(capture, trigger, trigger_occurrence, ring_buffer, post_trigger_limit)


def main() -> int:
    args = parse_args()
    trace_filter = build_filter()

    with open(args.output, "w", encoding="utf-8") as output_handle:
        for raw_line in sys.stdin:
            if not raw_line.strip():
                continue

            record = json.loads(raw_line)

            if not trace_filter.enabled:
                trace_filter.emit(record, output_handle)
                continue

            trace_filter.process(record, output_handle)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
