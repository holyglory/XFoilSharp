#!/usr/bin/env python3
"""Report final CL/CD/CM gaps between a Fortran dump and a managed dump."""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

REFERENCE_POST_CALC = re.compile(
    r"POST_CALC CL=\s*([+-]?\d(?:\.\d+)?(?:E[+-]?\d+)?)\s+"
    r"CD=\s*([+-]?\d(?:\.\d+)?(?:E[+-]?\d+)?)\s+"
    r"CM=\s*([+-]?\d(?:\.\d+)?(?:E[+-]?\d+)?)"
)
MANAGED_FINAL = re.compile(
    r"FINAL CL=([+-]?\d(?:\.\d+)?E[+-]?\d+)\s+"
    r"CD=([+-]?\d(?:\.\d+)?E[+-]?\d+)\s+"
    r"CM=([+-]?\d(?:\.\d+)?E[+-]?\d+)\s+"
    r"CONVERGED=(\w+)\s+ITER=(\d+)"
)
MANAGED_POST_CALC = re.compile(
    r"POST_CALC CL=([+-]?\d(?:\.\d+)?E[+-]?\d+)\s+"
    r"CD=([+-]?\d(?:\.\d+)?E[+-]?\d+)\s+"
    r"CM=([+-]?\d(?:\.\d+)?E[+-]?\d+)"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference_dump", help="Path to the Fortran dump")
    parser.add_argument("managed_dump", help="Path to the managed dump")
    return parser.parse_args()


def parse_reference(path: pathlib.Path) -> tuple[float, float, float]:
    matches = REFERENCE_POST_CALC.findall(path.read_text(encoding="utf-8"))
    if not matches:
        raise SystemExit(f"Could not find POST_CALC summary in reference dump: {path}")

    return tuple(float(value) for value in matches[-1])


def parse_managed(path: pathlib.Path) -> tuple[float, float, float, str, int]:
    text = path.read_text(encoding="utf-8")
    final_match = MANAGED_FINAL.search(text)
    if final_match:
        cl, cd, cm, converged, iterations = final_match.groups()
        return float(cl), float(cd), float(cm), converged, int(iterations)

    post_calc_matches = MANAGED_POST_CALC.findall(text)
    if not post_calc_matches:
        raise SystemExit(f"Could not find FINAL or POST_CALC summary in managed dump: {path}")

    cl, cd, cm = post_calc_matches[-1]
    return float(cl), float(cd), float(cm), "unknown", -1


def main() -> int:
    args = parse_args()
    reference_path = pathlib.Path(args.reference_dump)
    managed_path = pathlib.Path(args.managed_dump)

    ref_cl, ref_cd, ref_cm = parse_reference(reference_path)
    man_cl, man_cd, man_cm, converged, iterations = parse_managed(managed_path)

    print(
        f"reference CL={ref_cl:.9f} CD={ref_cd:.9e} CM={ref_cm:.9e}"
    )
    print(
        f"managed   CL={man_cl:.9f} CD={man_cd:.9e} CM={man_cm:.9e} "
        f"converged={converged} iter={iterations}"
    )
    print(
        f"delta     CL={man_cl - ref_cl:.9e} CD={man_cd - ref_cd:.9e} CM={man_cm - ref_cm:.9e}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
