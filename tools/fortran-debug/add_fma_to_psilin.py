#!/usr/bin/env python3
"""
Systematically add MathF.FusedMultiplyAdd to float expressions in
StreamfunctionInfluenceCalculator.cs to match gfortran's natural FMA contraction.

Transforms patterns like:
  float x = a * b + c;          -> float x = MathF.FusedMultiplyAdd(a, b, c);
  float x = a * b - c;          -> float x = MathF.FusedMultiplyAdd(a, b, -c);
  float x = c + a * b;          -> float x = MathF.FusedMultiplyAdd(a, b, c);
  float x = (a * b) + (c * d);  -> float x = MathF.FusedMultiplyAdd(a, b, c * d);

Only transforms SIMPLE patterns that are clearly FMA-eligible.
Skips expressions already using FMA/SumOfProducts.
"""
import re
import sys

def transform_line(line: str) -> str:
    """Transform a single line, adding FMA where appropriate."""
    stripped = line.strip()

    # Skip lines that already have FMA or are comments
    if any(x in stripped for x in ['FusedMultiplyAdd', 'SumOfProducts', '//', '/*']):
        return line

    # Skip non-float assignments
    if 'double ' in stripped and 'float' not in stripped:
        return line

    # Skip control flow
    if any(stripped.startswith(x) for x in ['if', 'for', 'while', 'return', 'else', '{', '}']):
        return line

    # Pattern: float x = a * b + c  where c is not a product
    # This is the most common FMA pattern
    # We need to be very careful not to break complex expressions

    # For now, just report what WOULD be changed
    return line

def analyze_file(path: str):
    """Analyze the file and report FMA opportunities."""
    with open(path) as f:
        lines = f.readlines()

    in_float_path = False
    candidates = []

    for i, line in enumerate(lines, 1):
        stripped = line.strip()

        # Track when we're in the float parity path (after line 400)
        if i < 400:
            continue

        # Skip non-FMA-eligible lines
        if any(x in stripped for x in ['FusedMultiplyAdd', 'SumOfProducts', '//', '/*', 'double ']):
            continue
        if not any(x in stripped for x in ['float ', ' += ', ' -= ', ' = ']):
            continue

        # Must have at least one multiply and one add/sub
        if '=' not in stripped:
            continue
        rhs = stripped.split('=', 1)[1]
        if '*' not in rhs or ('+' not in rhs and '-' not in rhs):
            continue

        # Skip non-arithmetic
        if any(x in rhs for x in ['string', 'bool', '"', '==', '!=', 'Length', 'new ', 'Get', '?']):
            continue

        candidates.append((i, stripped[:100]))

    return candidates

if __name__ == '__main__':
    path = 'src/XFoil.Solver/Services/StreamfunctionInfluenceCalculator.cs'
    candidates = analyze_file(path)
    print(f"Found {len(candidates)} FMA candidates in float path:")
    for ln, code in candidates:
        print(f"  {ln:4d}: {code}")

