#!/usr/bin/env python3
"""Parse a full-run parity report and route it to the responsible micro-rig."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from parity_routing import (
    load_registry,
    parse_parity_report,
    render_route_markdown,
    route_disparity,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--parity-report", required=True, help="Path to parity_report*.txt produced by a full managed parity run.")
    parser.add_argument("--output-dir", help="Directory for responsible_rig.json / responsible_rig.md. Defaults to the parity report directory.")
    parser.add_argument("--run-rig-quick", action="store_true", help="Immediately run the resolved rig through run_micro_rig_matrix.py --mode quick.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    parity_report = Path(args.parity_report).resolve()
    if not parity_report.exists():
        raise SystemExit(f"Parity report does not exist: {parity_report}")

    output_dir = Path(args.output_dir).resolve() if args.output_dir else parity_report.parent
    output_dir.mkdir(parents=True, exist_ok=True)

    registry = load_registry()
    parsed = parse_parity_report(parity_report)
    quick_output_root = output_dir / "responsible-rig-matrix" if args.run_rig_quick else None
    route = route_disparity(parsed, registry, quick_output_root=quick_output_root)

    json_path = output_dir / "responsible_rig.json"
    md_path = output_dir / "responsible_rig.md"
    json_path.write_text(json.dumps(route, indent=2) + "\n", encoding="utf-8")
    md_path.write_text(render_route_markdown(route), encoding="utf-8")

    responsible = route.get("responsible_rig")
    if responsible:
        print(f"Responsible rig: {responsible['id']}")
        quick_probe = route.get("quick_probe")
        if quick_probe and quick_probe.get("result"):
            result = route["quick_probe"]["result"]
            print(
                f"Quick probe: {result['status']} "
                f"{result['unique_real_vector_count']}/{result['required_vector_count']}"
            )
        print(f"Route artifacts: {md_path}")
        return 0

    template = route.get("missing_rig_template") or {}
    candidate = route.get("missing_rig_candidate")
    if candidate:
        print(f"Missing responsible rig; closest backlog candidate: {candidate['id']}")
    if template.get("id"):
        print(f"Suggested missing rig id: {template['id']}")
        print(f"Suggested test scaffold: {template['suggested_test_file']}")
    print(f"Route artifacts: {md_path}")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
