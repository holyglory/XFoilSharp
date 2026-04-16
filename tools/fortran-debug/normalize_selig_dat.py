#!/usr/bin/env python3
"""normalize_selig_dat.py

Walk a directory of airfoil .dat files and rewrite each one in Selig format
(title line + counterclockwise TE→LE→TE coordinates) so that XFoil's AREAD
parses them correctly. Files that are already in Selig format are left
unchanged. Lednicer-format files (line 2 is "<n> <n>" upper/lower counts) are
converted in place.

Both formats are common in the UIUC database. XFoil's AREAD does not support
the Lednicer count line, so it interprets ``66.0 66.0`` as a coordinate point
and produces nonsense geometry.

Usage:
    python3 normalize_selig_dat.py <dat_dir> [<dat_dir2> ...]
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

NUMBER_RE = re.compile(r"^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?$")


def parse_floats(line: str) -> list[float] | None:
    tokens = line.replace(",", " ").split()
    if not tokens:
        return None
    out = []
    for tok in tokens:
        if not NUMBER_RE.match(tok):
            return None
        try:
            out.append(float(tok))
        except ValueError:
            return None
    return out


def is_lednicer_count_pair(values: list[float]) -> bool:
    """Detect ``upper_count lower_count`` line where both are integral and >= 3."""
    if len(values) != 2:
        return False
    a, b = values
    return (
        a >= 3
        and b >= 3
        and a < 1000
        and b < 1000
        and a == int(a)
        and b == int(b)
    )


def convert_file(path: Path) -> str:
    raw_lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    # Strip blank lines and trailing whitespace.
    lines = [line.rstrip() for line in raw_lines]
    while lines and not lines[0].strip():
        lines.pop(0)
    if not lines:
        return "empty"

    title = lines[0]
    body = lines[1:]
    # Ensure title is text (not a coordinate). If first line parses as numbers,
    # there is no header line — fall back to a synthetic title.
    title_floats = parse_floats(title)
    if title_floats is not None and len(title_floats) >= 2:
        title = path.stem.upper()
        body = lines

    # Find first non-blank line in body.
    while body and not body[0].strip():
        body.pop(0)
    if not body:
        return "empty"

    second_floats = parse_floats(body[0])
    if second_floats is None:
        return "invalid"

    if is_lednicer_count_pair(second_floats):
        upper_count = int(second_floats[0])
        lower_count = int(second_floats[1])
        coord_lines = [line for line in body[1:] if line.strip()]
        # Sanity: collect float pairs.
        coords: list[tuple[float, float]] = []
        for line in coord_lines:
            vals = parse_floats(line)
            if vals is None or len(vals) < 2:
                continue
            coords.append((vals[0], vals[1]))
        expected = upper_count + lower_count
        if len(coords) < expected:
            return f"short ({len(coords)}/{expected})"
        upper = coords[:upper_count]
        lower = coords[upper_count:upper_count + lower_count]
        # Selig: TE → upper(reverse) → LE → lower → TE
        upper_rev = list(reversed(upper))
        # Drop duplicate LE between reversed upper end and lower start.
        if upper_rev and lower and upper_rev[-1] == lower[0]:
            lower = lower[1:]
        merged = upper_rev + lower
        out = [title]
        for x, y in merged:
            out.append(f"{x: .6f} {y: .6f}")
        path.write_text("\n".join(out) + "\n", encoding="utf-8")
        return "lednicer->selig"

    # Already Selig (line 2 is a coordinate). Make sure title is on its own line
    # and there are no blank lines breaking the coordinate stream.
    coord_lines = [line for line in body if line.strip()]
    coords: list[tuple[float, float]] = []
    for line in coord_lines:
        vals = parse_floats(line)
        if vals is None or len(vals) < 2:
            continue
        coords.append((vals[0], vals[1]))
    if len(coords) < 5:
        return f"too-few ({len(coords)})"
    out = [title]
    for x, y in coords:
        out.append(f"{x: .6f} {y: .6f}")
    path.write_text("\n".join(out) + "\n", encoding="utf-8")
    return "selig (clean)"


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 1
    counts: dict[str, int] = {}
    for arg in argv[1:]:
        root = Path(arg)
        if not root.exists():
            print(f"missing: {root}", file=sys.stderr)
            continue
        for path in sorted(root.glob("*.dat")):
            try:
                status = convert_file(path)
            except Exception as exc:  # noqa: BLE001
                status = f"error: {exc}"
            counts[status] = counts.get(status, 0) + 1
    for key in sorted(counts):
        print(f"  {key}: {counts[key]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
