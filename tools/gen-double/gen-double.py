#!/usr/bin/env python3
"""Generate a *.Double.cs twin from a *.cs source file.

Phase 1 of the float→double tree split. The float tree is the parity branch
(bit-exact with Fortran XFoil 6.97). The double tree is its algorithmic mirror
with float intermediates promoted to double — same control flow, same call
order, same variable names, just wider precision.

The double twin must be the script's textual output, never hand-edited. To
update a twin, re-run the script on the float source.

Substitution rules, applied in order on the source text:
  1. namespace XFoil.X.Y       -> namespace XFoil.X.Double.Y
  2. using XFoil.X.Y;          -> using XFoil.X.Double.Y;
     (only XFoil.* usings; System.* etc. left alone)
  3. \\bfloat\\b               -> double
     (whole-word; identifiers like 'someFloat' untouched)
  4. \\bMathF\\.                -> Math.
  5. literal Nf / Nf suffixes   -> Nd
     (e.g. 1f -> 1d, 0.5f -> 0.5d, 1e-3f -> 1e-3d; identifiers untouched)
  6. Auto-add `using <original_namespace>;` so the doubled file resolves
     short-name references to shared types (LegacyPrecisionMath, SolverBuffers,
     LegacyLibm, etc.) that intentionally live in the float namespace.

Output goes next to the source as <stem>.Double.cs.

Usage:
    python3 gen-double.py path/to/Foo.cs [path/to/Bar.cs ...]
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

NAMESPACE_RE = re.compile(
    r"^(?P<lead>\s*namespace\s+)(?P<ns>XFoil\.[A-Za-z0-9_.]+)(?P<end>\s*[;{])",
    re.MULTILINE,
)
USING_RE = re.compile(
    r"^(?P<lead>\s*using\s+(?:static\s+)?)(?P<ns>XFoil\.[A-Za-z0-9_.]+)(?P<end>\s*;)",
    re.MULTILINE,
)
FLOAT_WORD_RE = re.compile(r"\bfloat\b")
MATHF_PREFIX_RE = re.compile(r"\bMathF\.")
FLOAT_LITERAL_RE = re.compile(
    r"(?<![A-Za-z0-9_.])"
    r"(?P<num>\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)"
    r"[fF]"
    r"(?![A-Za-z0-9_])"
)
# SolverBuffers helper-method suffix convention: pool slot APIs end in `Float`
# for float[]/float[,] returns. Doubled callers need the parallel `Double`
# suffix variants (added by hand to SolverBuffers when first needed).
SOLVERBUFFERS_FLOAT_RE = re.compile(
    r"(?P<prefix>\bSolverBuffers\.[A-Z][A-Za-z0-9_]*?)Float\b"
)
# SolverBuffers `*Single` suffix → `*Double`. SolverBuffers exposes both
# *Single (float[]) and *Double (double[]) variants for parallel pool slots;
# doubled callers want the double variant.
SOLVERBUFFERS_SINGLE_RE = re.compile(
    r"(?P<prefix>\bSolverBuffers\.[A-Z][A-Za-z0-9_]*?)Single\b"
)
# LegacyPrecisionMath helper-method suffix convention: float helpers end in `F`
# (PowF, MultiplyAddF, etc.). Doubled callers reach the parallel `D`-suffix
# variants (added in LegacyPrecisionMath alongside the F family).
LEGACY_PRECISION_FLOAT_RE = re.compile(
    r"(?P<prefix>\bLegacyPrecisionMath\.[A-Z][A-Za-z0-9_]*?)F\b"
)
# LegacyLibm provides float-typed libm-equivalent functions (Sqrt, Pow, Exp,
# Log, Log10, Tanh, Atan2). The doubled tree promotes them to the standard
# Math.* equivalents.
LEGACYLIBM_PREFIX_RE = re.compile(r"\bLegacyLibm\.")
# BitConverter.Single<->Int32 (float bit pattern) → Double<->Int64.
BITCONVERTER_SINGLE_TO_INT_RE = re.compile(r"\bBitConverter\.SingleToInt32Bits\b")
BITCONVERTER_INT_TO_SINGLE_RE = re.compile(r"\bBitConverter\.Int32BitsToSingle\b")
BITCONVERTER_SINGLE_TO_UINT_RE = re.compile(r"\bBitConverter\.SingleToUInt32Bits\b")
BITCONVERTER_UINT_TO_SINGLE_RE = re.compile(r"\bBitConverter\.UInt32BitsToSingle\b")
# InviscidSolverState legacy float-precision LU factor field — doubled tree
# rewrites to the standard double[,] field of the same shape. Receiver-agnostic
# (matches any `<expr>.LegacyStreamfunctionInfluenceFactors` etc.) so callers
# in InfluenceMatrixBuilder using `inviscidState.X` are also rewritten.
STATE_LEGACY_LU_FACTORS_RE = re.compile(r"\.LegacyStreamfunctionInfluenceFactors\b")
STATE_LEGACY_PIVOT_RE = re.compile(r"\.LegacyPivotIndices\b")
# Phase 2: doubled tree should use modern double-precision paths in BLC/LPM
# helpers. Flip every `useLegacyPrecision: true` to `useLegacyPrecision: false`
# so the doubled tree calls the modern (non-legacy) branch of every helper.
USE_LEGACY_TRUE_RE = re.compile(r"\buseLegacyPrecision:\s*true\b")
# Phase 2: positional-true LPM helpers that switch to legacy float when their
# trailing bool is `true`. The doubled tree wants the modern branch, so flip
# the trailing positional `true` to `false`. Conservative — only matches the
# known precision-flag helpers to avoid mangling unrelated `, true)` literals.
LEGACY_PRECISION_POSITIONAL_TRUE_RE = re.compile(
    r"(?P<call>\bLegacyPrecisionMath\.(?:RoundToSingle|GammaMinusOne|Multiply|Add|Subtract|Negate|Divide|Average|Square|Sqrt|Pow|Exp|Log|Log10|Tanh|Sin|Cos|Atan2|Abs|Max|Min|MultiplyAdd|MultiplySubtract|AddScaled|SourceOrderedProductSum|SeparateMultiplySubtract|SeparateSumOfProducts|SumOfProducts|ProductThenAdd|ProductThenSubtract)\([^)]*?),\s*true\)"
)


def _insert_double(ns: str) -> str:
    parts = ns.split(".")
    if "Double" in parts:
        return ns  # idempotent
    if len(parts) >= 3:
        parts.insert(2, "Double")
    else:
        parts.append("Double")
    return ".".join(parts)


def _ns_sub(match: re.Match[str]) -> str:
    return f"{match['lead']}{_insert_double(match['ns'])}{match['end']}"


def _detect_original_namespace(source: str) -> str | None:
    m = NAMESPACE_RE.search(source)
    if not m:
        return None
    return m.group("ns")


def _add_shared_using(source: str, original_namespace: str | None) -> str:
    if original_namespace is None or "Double" in original_namespace.split("."):
        return source
    using_line = f"using {original_namespace};\n"
    if using_line in source:
        return source
    # Insert immediately before the (already-rewritten) namespace declaration.
    def insert(match: re.Match[str]) -> str:
        return f"{using_line}{match.group(0)}"
    return NAMESPACE_RE.sub(insert, source, count=1)


def transform(source: str) -> str:
    original_ns = _detect_original_namespace(source)
    out = NAMESPACE_RE.sub(_ns_sub, source)
    # Note: USING_RE rewrite intentionally disabled. Most XFoil.* namespaces
    # don't yet have doubled twins (Models, Diagnostics, Core.*), so blindly
    # rewriting `using XFoil.X.Y;` to `using XFoil.X.Double.Y;` breaks the
    # doubled file's compile. Leaving usings untouched lets the doubled file
    # keep referencing the original (shared) types. The auto-added
    # `using <original_namespace>;` below handles short-name resolution into
    # the original namespace itself.
    out = FLOAT_WORD_RE.sub("double", out)
    out = MATHF_PREFIX_RE.sub("Math.", out)
    out = FLOAT_LITERAL_RE.sub(lambda m: f"{m['num']}d", out)
    out = SOLVERBUFFERS_FLOAT_RE.sub(lambda m: f"{m['prefix']}Double", out)
    out = SOLVERBUFFERS_SINGLE_RE.sub(lambda m: f"{m['prefix']}Double", out)
    out = LEGACY_PRECISION_FLOAT_RE.sub(lambda m: f"{m['prefix']}D", out)
    out = LEGACYLIBM_PREFIX_RE.sub("Math.", out)
    out = BITCONVERTER_SINGLE_TO_INT_RE.sub("BitConverter.DoubleToInt64Bits", out)
    out = BITCONVERTER_INT_TO_SINGLE_RE.sub("BitConverter.Int64BitsToDouble", out)
    out = BITCONVERTER_SINGLE_TO_UINT_RE.sub("BitConverter.DoubleToUInt64Bits", out)
    out = BITCONVERTER_UINT_TO_SINGLE_RE.sub("BitConverter.UInt64BitsToDouble", out)
    out = STATE_LEGACY_LU_FACTORS_RE.sub(".StreamfunctionInfluence", out)
    out = STATE_LEGACY_PIVOT_RE.sub(".PivotIndices", out)
    out = USE_LEGACY_TRUE_RE.sub("useLegacyPrecision: false", out)
    # Iterate until no more nested LPM(..., true) patterns remain.
    for _ in range(8):
        new_out = _flip_lpm_positional_true(out)
        if new_out == out:
            break
        out = new_out
    # Special case: GammaMinusOne(true) → GammaMinusOne(false) (single arg, no comma)
    out = re.sub(r"\bLegacyPrecisionMath\.GammaMinusOne\(\s*true\s*\)",
                 "LegacyPrecisionMath.GammaMinusOne(false)", out)
    # Special case: RoundToSingle(x) (1-arg) hardcodes (float)x in LPM, which
    # clips the doubled tree's intermediates to float at entry points like
    # AssembleStationSystem. Route through the 2-arg overload with `false` so
    # the doubled tree keeps full double precision.
    out = _flip_lpm_round_to_single_one_arg(out)
    out = _add_shared_using(out, original_ns)
    return out


_LPM_FUNCS = {
    "RoundToSingle", "GammaMinusOne", "Multiply", "Add", "Subtract", "Negate",
    "Divide", "Average", "Square", "Sqrt", "Pow", "Exp", "Log", "Log10", "Tanh",
    "Sin", "Cos", "Atan2", "Abs", "Max", "Min", "MultiplyAdd", "MultiplySubtract",
    "AddScaled", "SourceOrderedProductSum", "SeparateMultiplySubtract",
    "SeparateSumOfProducts", "SumOfProducts", "ProductThenAdd", "ProductThenSubtract",
}
_LPM_PREFIX = "LegacyPrecisionMath."


def _flip_lpm_round_to_single_one_arg(source: str) -> str:
    """`LegacyPrecisionMath.RoundToSingle(<expr>)` (one arg) → `RoundToSingle(<expr>, false)`.
    The 1-arg overload is hardcoded to clip to float, which kills double precision
    in the doubled tree. Routing through the 2-arg overload with `false` returns
    the value unchanged."""
    out = []
    i = 0
    n = len(source)
    needle = "LegacyPrecisionMath.RoundToSingle("
    while i < n:
        idx = source.find(needle, i)
        if idx < 0:
            out.append(source[i:])
            break
        out.append(source[i:idx])
        depth = 1
        p = idx + len(needle)
        while p < n and depth > 0:
            ch = source[p]
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
                if depth == 0:
                    break
            p += 1
        if depth != 0:
            out.append(source[idx:p])
            i = p
            continue
        body = source[idx + len(needle):p]
        # If a top-level `,` exists, this is the 2-arg overload — leave alone.
        # Top-level meaning depth==0 across the body.
        d = 0
        has_top_comma = False
        for ch in body:
            if ch == "(":
                d += 1
            elif ch == ")":
                d -= 1
            elif ch == "," and d == 0:
                has_top_comma = True
                break
        if has_top_comma:
            out.append(source[idx:p + 1])
            i = p + 1
            continue
        out.append(needle + body + ", false)")
        i = p + 1
    return "".join(out)


def _flip_lpm_positional_true(source: str) -> str:
    """For each `LegacyPrecisionMath.<func>( ... , true)` call, recursively
    process the call body first, then flip the trailing `true` to `false` if
    present. Handles balanced parens (which `re` cannot)."""
    out = []
    i = 0
    n = len(source)
    while i < n:
        idx = source.find(_LPM_PREFIX, i)
        if idx < 0:
            out.append(source[i:])
            break
        out.append(source[i:idx])
        j = idx + len(_LPM_PREFIX)
        k = j
        while k < n and (source[k].isalnum() or source[k] == "_"):
            k += 1
        func = source[j:k]
        if func not in _LPM_FUNCS or k >= n or source[k] != "(":
            out.append(source[idx:k])
            i = k
            continue
        depth = 1
        p = k + 1
        while p < n and depth > 0:
            ch = source[p]
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
                if depth == 0:
                    break
            p += 1
        if depth != 0:
            out.append(source[idx:k + 1])
            i = k + 1
            continue
        # Recurse into the body so nested LPM(..., true) calls are flipped too.
        call_body = _flip_lpm_positional_true(source[k + 1:p])
        m = re.search(r",\s*true\s*$", call_body)
        if m is not None:
            call_body = call_body[:m.start()] + ", false"
        out.append(_LPM_PREFIX + func + "(" + call_body + ")")
        i = p + 1
    return "".join(out)


def process(path: Path) -> Path:
    if path.name.endswith(".Double.cs"):
        raise SystemExit(f"refusing to process double twin: {path}")
    if not path.is_file():
        raise SystemExit(f"not a file: {path}")
    out_path = path.with_name(f"{path.stem}.Double.cs")
    out_path.write_text(transform(path.read_text()))
    return out_path


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2
    for arg in argv[1:]:
        out = process(Path(arg))
        print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
