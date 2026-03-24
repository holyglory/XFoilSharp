#!/usr/bin/env python3
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import re
import sys


NUMERIC_PATTERN = re.compile(r"\bdouble\b|\bMathF?\.")
LEGACY_PATTERN = re.compile(r"\buseLegacyPrecision\b|\bUseLegacyBoundaryLayerInitialization\b|\bLegacyPrecisionMath\b")
FORTRAN_PATTERN = re.compile(r"Source:\s+.*\.f:|Port of .* from .*\.f", re.IGNORECASE)
SIGNATURE_START_PATTERN = re.compile(r"^\s*(public|private|internal|protected).*\(")
ARITH_ASSIGNMENT_PATTERN = re.compile(r"=\s*[^;]*(\+|\-|\*|/|Math\.|MathF\.)")
KINEMATIC_HELPER_PATTERN = re.compile(r"\bKinematicShapeParameter\(")
SEED_SOLVE_BYPASS_PATTERN = re.compile(r"\bsolver\.Solve\s*\(\s*matrix\s*,\s*rhs\s*\)")
SEED_TRACE_PATTERN = re.compile(r'"laminar_seed_step"|\"laminar_seed_step\"|\blaminar_seed_step\b')
DERIVATIVE_CHAIN_PATTERN = re.compile(r"\b(?:cf1|cf2|cfm)_[a-z0-9]+\s*=")
PRODUCER_OVERRIDE_PATTERN = re.compile(
    r"\bcorrelationType\b|"
    r"\b(?:LaminarShapeParameter|TurbulentShapeParameter|ComputeCqChains|"
    r"ComputeDiChains|ComputeMidpointCorrelations|LaminarSkinFriction|"
    r"TurbulentSkinFriction)\([^;\n]*\bbldifType\b|"
    r"\bif\s*\(\s*bldifType\s*(?:==|!=|<=|>=|<|>)"
)
BLKIN_SHORTCUT_PATTERN = re.compile(
    r"\bm[12]_ms\s*=\s*1\.0\b|"
    r"\brt[12]_ms\s*=\s*0\.0\b|"
    r"\bhk[12]_u[12]\s*=\s*0\.0\b"
)
FASTPATH_CONDITION_PATTERN = re.compile(
    r"\bif\s*\([^)]*(?:mach|minf|msq|beta|herat|qinf|reinf)[^)]*(?:<|<=|==)[^)]*\)",
    re.IGNORECASE,
)
DERIVATIVE_ZERO_PATTERN = re.compile(
    r"\b[a-zA-Z0-9_]+_(?:ms|re)\s*=\s*0(?:\.0+)?\b",
    re.IGNORECASE,
)
HVRAT_DIRECT_PATTERN = re.compile(r"\b(?:HvRat|DefaultHvRat|LegacyHvRat)\b")
LEGACY_GM1_USAGE_PATTERN = re.compile(r"\bGm1\b|\bgm1bl\b")
TRACE_RECOMPOSITION_PATTERN = re.compile(r"\b(?:cfRe|cfmRe|cqRe|diRe|hsRe|usRe)\s*=")
TRACE_FLOAT_CONVERTER_PATTERN = re.compile(r"\bJsonConverter<float>\b|\bWriteNumberValue\(\(double\)value\)")
PARITY_ROW_COMBINE_PATTERN = re.compile(r"\bresult\.(?:VS1|VS2|Residual)\s*\[[^]]+\]\s*=")
SECONDARY_OVERRIDE_PATTERN = re.compile(r"\bstation1KinematicOverride\s*:")
LEGACY_WRAPPER_CALL_PATTERN = re.compile(
    r"\b(?:AssembleLaminarStation|BoundaryLayerSystemAssembler\.AssembleStationSystem)\s*\("
)
LEGACY_SNAPSHOT_CALL_PATTERN = re.compile(r"\b(?:AssembleLaminarStation|AssembleSimilarityStation)\s*\(")
LEGACY_TERNARY_AFFINE_PATTERN = re.compile(r"\?\s*\(float\)\([^;\n]*\*[^;\n]*\+[^;\n]*\)")
LEGACY_REFRESH_CALL_PATTERN = re.compile(r"\bRefreshLegacy\w*Snapshot\s*\(")
LEGACY_KINEMATIC_REFRESH_CALL_PATTERN = re.compile(r"\bRefreshLegacyKinematicSnapshot\s*\(")
TRACE_SUSPEND_PATTERN = re.compile(r"\bSolverTrace\.Suspend\s*\(")
LEGACY_GAUSS_FMA_PATTERN = re.compile(r"\bFusedMultiplySubtract\s*\(")
LEGACY_FLOAT_COMBINE_PATTERN = re.compile(
    r"^\s*(?:float\s+)?\w+\s*=\s*[^;]*[\+\-][^;]*\*"
    r"|^\s*(?:float\s+)?\w+\s*=\s*[^;]*\*[^;]*[\+\-]"
)
LEGACY_CHAIN_WIDENING_PATTERN = re.compile(
    r"^\s*double\s+\w+_(?:u1|t1|d1|ms|re|u2|t2|d2)\s*=\s*[^;]*(?:\*|/|\+|\-)"
)
LEGACY_RAW_DERIVATIVE_ARITH_PATTERN = re.compile(
    r"^\s*(?:float|double)\s+\w+_(?:hk|th|rt|a1|a2|u1|u2|t1|t2|d1|d2|ms|re)\s*="
    r"\s*[^;]*(?:\+|\-|\*|/)"
)
LEGACY_HIDDEN_PRODSUB_PATTERN = re.compile(
    r"\bLegacyPrecisionMath\.Subtract\(\s*"
    r"(?:1\.0|3\.0|[A-Za-z_][A-Za-z0-9_]*)\s*,\s*"
    r"LegacyPrecisionMath\.(?:Square|Multiply)\("
)
LEGACY_RECURRENCE_UPDATE_PATTERN = re.compile(
    r"^\s*([A-Za-z_][A-Za-z0-9_]*(?:\[[^]]+\])?)\s*=\s*\1\s*[-+]\s*[^;]*\*"
)
LEGACY_WEIGHTED_BLEND_PATTERN = re.compile(
    r"^\s*(?:float|double)\s+\w+\s*=\s*"
    r"\(1\.0[fF]?\s*-\s*[A-Za-z_][A-Za-z0-9_]*\)\s*\*"
    r"\s*[A-Za-z_][A-Za-z0-9_]*\s*\*\s*[A-Za-z_][A-Za-z0-9_]*"
    r"\s*\+\s*[A-Za-z_][A-Za-z0-9_]*\s*\*"
    r"\s*[A-Za-z_][A-Za-z0-9_]*\s*\*\s*[A-Za-z_][A-Za-z0-9_]*"
)
LEGACY_PRODUCT_DIFFERENCE_PATTERN = re.compile(
    r"^\s*(?:float|double)\s+\w+\s*=\s*"
    r"[A-Za-z_][A-Za-z0-9_]*\s*\*\s*[A-Za-z_][A-Za-z0-9_]*"
    r"\s*-\s*"
    r"[A-Za-z_][A-Za-z0-9_]*\s*\*\s*[A-Za-z_][A-Za-z0-9_]*"
)
LEGACY_GROUPED_UPDATE_PATTERN = re.compile(
    r"^\s*[A-Za-z_][A-Za-z0-9_]*(?:\[[^]]+\])?\s*\+=\s*[^;]*\+[^;]*$"
)
LEGACY_PRODUCT_SUM_PLUS_PATTERN = re.compile(
    r"^\s*(?:float|double)\s+\w+\s*=\s*"
    r"[A-Za-z_][A-Za-z0-9_]*(?:\s*\*\s*\([^;]+\)|\s*\*\s*[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)"
    r"[^;]*\+[^;]*\*[^;]*\+[^;]*\*[^;]*\+[^;]*$"
)
LEGACY_RAW_POW_PATTERN = re.compile(r"\bMathF\.Pow\s*\(")
LEGACY_LITERAL_AFFINE_PATTERN = re.compile(
    r"^\s*float\s+\w+\s*=\s*"
    r"(?:\(?\s*[-+]?\d+(?:\.\d+)?f?\s*\*[^;]*[+\-]\s*[-+]?\d+(?:\.\d+)?f?\)?"
    r"|\(?\s*[-+]?\d+(?:\.\d+)?f?\s*[+\-]\s*[^;]*\*[^;]*\)?)\s*;"
)
LEGACY_FMA_REVIEW_PATTERN = re.compile(
    r"\b(?:LegacyPrecisionMath\.)?(?:FusedMultiplyAdd|MultiplyAdd|MultiplySubtract)\s*\("
    r"|\bMathF\.FusedMultiplyAdd\s*\("
)
LEGACY_SUMPROD_REVIEW_PATTERN = re.compile(r"\bLegacyPrecisionMath\.SumOfProducts\s*\(")
LEGACY_GROUPED_HALFSUM_PATTERN = re.compile(
    r"LegacyPrecisionMath\.Multiply\(\s*0\.5,\s*"
    r"LegacyPrecisionMath\.Add\(\s*"
    r"LegacyPrecisionMath\.SumOfProducts\(",
    re.DOTALL,
)


@dataclass
class FileStats:
    path: Path
    numeric_hits: int
    legacy_hits: int
    fortran_hits: int
    shared_preamble_hits: int
    bare_hkin_hits: int
    seed_bypass_hits: int
    derivative_chain_hits: int
    producer_override_hits: int
    blkin_shortcut_hits: int
    derivative_fastpath_hits: int
    hvrat_direct_hits: int
    legacy_gamma_staging_hits: int
    trace_recomposition_hits: int
    trace_precision_hits: int
    parity_row_combine_hits: int
    secondary_override_hits: int
    legacy_wrapper_override_hits: int
    legacy_snapshot_hits: int
    legacy_ternary_affine_hits: int
    legacy_snapshot_refresh_hits: int
    legacy_snapshot_reassembly_hits: int
    legacy_kinematic_refresh_hits: int
    legacy_refresh_trace_hits: int
    legacy_gauss_fma_hits: int
    legacy_float_combine_hits: int
    legacy_chain_widening_hits: int
    legacy_raw_derivative_arith_hits: int
    legacy_hidden_prodsub_hits: int
    legacy_recurrence_update_hits: int
    legacy_weighted_blend_hits: int
    legacy_product_difference_hits: int
    legacy_grouped_update_hits: int
    legacy_product_sum_plus_hits: int
    legacy_raw_pow_hits: int
    legacy_literal_affine_hits: int
    legacy_fma_review_hits: int
    legacy_sumprod_review_hits: int
    legacy_grouped_halfsum_hits: int
    branchless_legacy_methods: int
    legacy_raw_sumprod_hits: int
    legacy_branch_without_return: int


def count_derivative_chain_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        if "LegacyPrecisionMath" in line:
            continue
        if DERIVATIVE_CHAIN_PATTERN.search(line) and ("+" in line or "*" in line):
            total += 1
    return total


def count_seed_bypass_hits(text: str) -> int:
    total = 0
    lines = text.splitlines()
    for index, line in enumerate(lines):
        if not SEED_SOLVE_BYPASS_PATTERN.search(line):
            continue
        window_start = max(0, index - 12)
        window = "\n".join(lines[window_start:index + 1])
        if "SolveSeedLinearSystem" in window:
            continue
        total += 1
    return total


def count_producer_override_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    for index, line in enumerate(lines):
        if "bldifType = isSimilarityStation ? 0 : flowType" not in line:
            continue

        end = len(lines)
        for j in range(index + 1, len(lines)):
            if "if (bldifType == 0)" in lines[j]:
                end = j
                break

        for current in lines[index + 1:end]:
            stripped = current.strip()
            if not stripped or stripped.startswith("//"):
                continue
            if "bldifType = isSimilarityStation ? 0 : flowType" in current:
                continue
            if PRODUCER_OVERRIDE_PATTERN.search(current):
                total += 1

    return total


def count_blkin_shortcut_hits(text: str) -> int:
    if "ComputeFiniteDifferences" not in text or "ComputeKinematicParameters" not in text:
        return 0
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if BLKIN_SHORTCUT_PATTERN.search(line):
            total += 1
    return total


def count_derivative_fastpath_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    for index, line in enumerate(lines):
        if not FASTPATH_CONDITION_PATTERN.search(line):
            continue

        brace_depth = line.count("{") - line.count("}")
        branch_found = 0
        for j in range(index + 1, len(lines)):
            current = lines[j]
            stripped = current.strip()
            if stripped and not stripped.startswith("//") and DERIVATIVE_ZERO_PATTERN.search(current):
                branch_found += 1

            brace_depth += current.count("{") - current.count("}")
            if "{" in line and brace_depth <= 0:
                break
            if "{" not in line and j >= index + 10:
                break

        total += branch_found

    return total


def count_hvrat_direct_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "private const double DefaultHvRat" in line or "private const double LegacyHvRat" in line:
            continue
        if "GetHvRat(" in line or "return useLegacyPrecision ? LegacyHvRat : DefaultHvRat" in line:
            continue
        if HVRAT_DIRECT_PATTERN.search(line):
            total += 1
    return total


def count_legacy_gamma_staging_hits(text: str) -> int:
    if "useLegacyPrecision" not in text and "UseLegacyBoundaryLayerInitialization" not in text:
        return 0

    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath.GammaMinusOne(" in line:
            continue
        if "const double Gm1 = Gamma - 1.0" in line:
            total += 1
            continue
        if "gm1bl" in line and any(op in line for op in ("*", "/", "+", "-")):
            total += 1
            continue
        if "Gm1" in line and any(op in line for op in ("*", "/", "+", "-")):
            total += 1

    return total


def count_trace_recomposition_hits(text: str) -> int:
    if "useLegacyPrecision" not in text and "UseLegacyBoundaryLayerInitialization" not in text:
        return 0

    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath" in line:
            continue
        if TRACE_RECOMPOSITION_PATTERN.search(line) and any(op in line for op in ("*", "+", "-", "/")):
            total += 1

    return total


def count_trace_precision_hits(path: Path, text: str) -> int:
    if path.name != "JsonlTraceWriter.cs":
        return 0
    return 0 if TRACE_FLOAT_CONVERTER_PATTERN.search(text) else 1


def count_parity_row_combine_hits(text: str) -> int:
    if "useLegacyPrecision" not in text:
        return 0

    total = 0
    depth = 0
    legacy_stack: list[int] = []
    lines = text.splitlines()
    for line in lines:
        stripped = line.strip()
        entering_legacy_branch = re.search(r"\bif\s*\(\s*useLegacyPrecision\b", line) is not None
        inside_legacy_branch = bool(legacy_stack)

        if (
            stripped
            and not stripped.startswith("//")
            and not inside_legacy_branch
            and not entering_legacy_branch
            and "LegacyPrecisionMath" not in line
            and PARITY_ROW_COMBINE_PATTERN.search(line)
            and any(op in line for op in ("+", "-", "*", "/"))
        ):
            total += 1

        depth += line.count("{") - line.count("}")
        if entering_legacy_branch and "{" in line:
            legacy_stack.append(depth)

        while legacy_stack and depth < legacy_stack[-1]:
            legacy_stack.pop()

    return total


def count_secondary_override_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    for index, line in enumerate(lines):
        if not SECONDARY_OVERRIDE_PATTERN.search(line):
            continue

        found_secondary = False
        for j in range(index, min(index + 8, len(lines))):
            if "station1SecondaryOverride" in lines[j]:
                found_secondary = True
                break
            if ");" in lines[j]:
                break

        if not found_secondary:
            total += 1

    return total


def count_legacy_wrapper_override_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    for index, line in enumerate(lines):
        if not LEGACY_WRAPPER_CALL_PATTERN.search(line):
            continue
        if SIGNATURE_START_PATTERN.search(line) and not line.lstrip().startswith(("if ", "if(")):
            continue

        snippet_lines = [line]
        for j in range(index + 1, len(lines)):
            snippet_lines.append(lines[j])
            if ");" in lines[j]:
                break

        snippet = "\n".join(snippet_lines)
        if "UseLegacyBoundaryLayerInitialization" not in snippet and "useLegacyPrecision:" not in snippet:
            continue
        if "isSimi: true" in snippet:
            continue
        if "station1KinematicOverride:" in snippet and "station1SecondaryOverride:" in snippet:
            continue
        total += 1

    return total


def count_legacy_snapshot_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped_line = line.lstrip()
        if not SIGNATURE_START_PATTERN.search(line) or stripped_line.startswith("if ") or stripped_line.startswith("if("):
            i += 1
            continue

        signature_lines = [line]
        while "{" not in signature_lines[-1] and i + 1 < len(lines):
            i += 1
            signature_lines.append(lines[i])

        signature = "\n".join(signature_lines)
        brace_depth = signature.count("{") - signature.count("}")
        body_lines: list[str] = []
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            body_lines.append(current)
            brace_depth += current.count("{") - current.count("}")
            i += 1

        if "BoundaryLayerSystemState blState" not in signature:
            continue

        body = "\n".join(body_lines)
        if not LEGACY_SNAPSHOT_CALL_PATTERN.search(body):
            continue
        if "UseLegacyBoundaryLayerInitialization" not in body:
            continue

        has_kinematic_store = re.search(r"LegacyKinematic\s*\[[^]]+\]\s*=", body) is not None
        has_secondary_store = re.search(r"LegacySecondary\s*\[[^]]+\]\s*=", body) is not None
        if not (has_kinematic_store and has_secondary_store):
            total += 1

    return total


def count_legacy_ternary_affine_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath" in line or "MathF.FusedMultiplyAdd" in line:
            continue
        if LEGACY_TERNARY_AFFINE_PATTERN.search(line):
            total += 1
    return total


def count_legacy_snapshot_refresh_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped_line = line.lstrip()
        if not SIGNATURE_START_PATTERN.search(line) or stripped_line.startswith("if ") or stripped_line.startswith("if("):
            i += 1
            continue

        signature_lines = [line]
        while "{" not in signature_lines[-1] and i + 1 < len(lines):
            i += 1
            signature_lines.append(lines[i])

        signature = "\n".join(signature_lines)
        brace_depth = signature.count("{") - signature.count("}")
        body_lines: list[str] = []
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            body_lines.append(current)
            brace_depth += current.count("{") - current.count("}")
            i += 1

        if "BoundaryLayerSystemState blState" not in signature:
            continue
        if "RefreshLegacy" in signature:
            continue

        body = "\n".join(body_lines)
        if not LEGACY_SNAPSHOT_CALL_PATTERN.search(body):
            continue
        if "UseLegacyBoundaryLayerInitialization" not in body:
            continue
        if LEGACY_REFRESH_CALL_PATTERN.search(body):
            continue
        total += 1

    return total


def count_legacy_snapshot_reassembly_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped_line = line.lstrip()
        if not SIGNATURE_START_PATTERN.search(line) or stripped_line.startswith("if ") or stripped_line.startswith("if("):
            i += 1
            continue

        signature_lines = [line]
        while "{" not in signature_lines[-1] and i + 1 < len(lines):
            i += 1
            signature_lines.append(lines[i])

        signature = "\n".join(signature_lines)
        brace_depth = signature.count("{") - signature.count("}")
        body_lines: list[str] = []
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            body_lines.append(current)
            brace_depth += current.count("{") - current.count("}")
            i += 1

        if "RefreshLegacy" in signature:
            continue

        body = "\n".join(body_lines)
        total += len(LEGACY_REFRESH_CALL_PATTERN.findall(body))

    return total


def count_legacy_kinematic_refresh_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped_line = line.lstrip()
        if not SIGNATURE_START_PATTERN.search(line) or stripped_line.startswith("if ") or stripped_line.startswith("if("):
            i += 1
            continue

        signature_lines = [line]
        while "{" not in signature_lines[-1] and i + 1 < len(lines):
            i += 1
            signature_lines.append(lines[i])

        signature = "\n".join(signature_lines)
        if "RefreshLegacyKinematicSnapshot" in signature:
            i += 1
            continue

        brace_depth = signature.count("{") - signature.count("}")
        body_lines: list[str] = []
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            body_lines.append(current)
            brace_depth += current.count("{") - current.count("}")
            i += 1

        body = "\n".join(body_lines)
        total += len(LEGACY_KINEMATIC_REFRESH_CALL_PATTERN.findall(body))

    return total


def count_legacy_float_combine_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        if not re.search(r"\bif\s*\(\s*useLegacyPrecision\b", line):
            i += 1
            continue

        brace_depth = line.count("{") - line.count("}")
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            stripped = current.strip()
            if (
                stripped
                and not stripped.startswith("//")
                and "LegacyPrecisionMath" not in current
                and "MathF.FusedMultiplyAdd" not in current
                and LEGACY_FLOAT_COMBINE_PATTERN.search(current)
            ):
                total += 1
            brace_depth += current.count("{") - current.count("}")
            i += 1

    return total


def count_legacy_chain_widening_hits(text: str) -> int:
    if "useLegacyPrecision" not in text and "UseLegacyBoundaryLayerInitialization" not in text:
        return 0

    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath" in line or "MathF.FusedMultiplyAdd" in line:
            continue
        if LEGACY_CHAIN_WIDENING_PATTERN.search(line):
            total += 1
    return total


def count_legacy_recurrence_update_hits(text: str) -> int:
    if (
        "useLegacyPrecision" not in text
        and "UseLegacyBoundaryLayerInitialization" not in text
        and "GAUSS" not in text
        and "Solve(float[" not in text
    ):
        return 0

    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath" in line or "MathF.FusedMultiplyAdd" in line:
            continue
        if LEGACY_RECURRENCE_UPDATE_PATTERN.search(line):
            total += 1
    return total


def count_legacy_weighted_blend_hits(text: str) -> int:
    if "useLegacyPrecision" not in text and "UseLegacyBoundaryLayerInitialization" not in text:
        return 0

    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath.WeightedProductBlend" in line or "MathF.FusedMultiplyAdd" in line:
            continue
        if LEGACY_WEIGHTED_BLEND_PATTERN.search(line):
            total += 1
    return total


def count_legacy_product_difference_hits(text: str) -> int:
    if "useLegacyPrecision" not in text and "UseLegacyBoundaryLayerInitialization" not in text:
        return 0

    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath.DifferenceOfProducts" in line or "MathF.FusedMultiplyAdd" in line:
            continue
        if LEGACY_PRODUCT_DIFFERENCE_PATTERN.search(line):
            total += 1
    return total


def count_legacy_grouped_update_hits(text: str) -> int:
    if "useLegacyPrecision" not in text and "UseLegacyBoundaryLayerInitialization" not in text:
        return 0

    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath" in line or "MathF.FusedMultiplyAdd" in line:
            continue
        if LEGACY_GROUPED_UPDATE_PATTERN.search(line):
            total += 1
    return total


def count_legacy_product_sum_plus_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyPrecisionMath" in line or "MathF.FusedMultiplyAdd" in line:
            continue
        if LEGACY_PRODUCT_SUM_PLUS_PATTERN.search(line):
            total += 1
    return total


def count_legacy_raw_pow_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if "LegacyLibm.Pow" in line or "LegacyPrecisionMath.Pow" in line:
            continue
        if LEGACY_RAW_POW_PATTERN.search(line):
            total += 1
    return total


def count_legacy_literal_affine_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if (
            "MathF.FusedMultiplyAdd" in line
            or "LegacyPrecisionMath.FusedMultiplyAdd" in line
            or "LegacyPrecisionMath.MultiplyAdd" in line
            or "LegacyPrecisionMath.MultiplySubtract" in line
        ):
            continue
        if LEGACY_LITERAL_AFFINE_PATTERN.search(line):
            total += 1
    return total


def count_legacy_fma_review_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if LEGACY_FMA_REVIEW_PATTERN.search(line):
            total += 1
    return total


def count_legacy_sumprod_review_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if LEGACY_SUMPROD_REVIEW_PATTERN.search(line):
            total += 1
    return total


def count_legacy_grouped_halfsum_hits(text: str) -> int:
    return len(LEGACY_GROUPED_HALFSUM_PATTERN.findall(text))


def count_legacy_refresh_trace_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped_line = line.lstrip()
        if not SIGNATURE_START_PATTERN.search(line) or stripped_line.startswith("if ") or stripped_line.startswith("if("):
            i += 1
            continue

        signature_lines = [line]
        while "{" not in signature_lines[-1] and i + 1 < len(lines):
            i += 1
            signature_lines.append(lines[i])

        signature = "\n".join(signature_lines)
        brace_depth = signature.count("{") - signature.count("}")
        body_lines: list[str] = []
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            body_lines.append(current)
            brace_depth += current.count("{") - current.count("}")
            i += 1

        if "RefreshLegacy" not in signature:
            continue

        body = "\n".join(body_lines)
        if not LEGACY_SNAPSHOT_CALL_PATTERN.search(body) and "AssembleStationSystem(" not in body:
            continue
        if TRACE_SUSPEND_PATTERN.search(body):
            continue
        total += 1

    return total


def count_legacy_gauss_fma_hits(text: str) -> int:
    if "GAUSS" not in text and "xsolve.f" not in text:
        return 0
    return len(LEGACY_GAUSS_FMA_PATTERN.findall(text))


def count_shared_preamble_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped_line = line.lstrip()
        if not SIGNATURE_START_PATTERN.search(line) or stripped_line.startswith("if ") or stripped_line.startswith("if("):
            i += 1
            continue

        signature_lines = [line]
        while "{" not in signature_lines[-1] and i + 1 < len(lines):
            i += 1
            signature_lines.append(lines[i])

        signature = "\n".join(signature_lines)
        if "useLegacyPrecision" not in signature:
            i += 1
            continue

        brace_depth = signature.count("{") - signature.count("}")
        branch_seen = False
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            stripped = current.strip()
            if brace_depth == 1 and re.search(r"\bif\s*\(\s*useLegacyPrecision\b", current):
                branch_seen = True
            elif (
                brace_depth == 1
                and not branch_seen
                and stripped
                and not stripped.startswith("//")
                and "LegacyPrecisionMath" not in current
                and ARITH_ASSIGNMENT_PATTERN.search(current)
            ):
                total += 1

            brace_depth += current.count("{") - current.count("}")
            i += 1

    return total


def count_legacy_raw_derivative_arith_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped_line = line.lstrip()
        if not SIGNATURE_START_PATTERN.search(line) or stripped_line.startswith("if ") or stripped_line.startswith("if("):
            i += 1
            continue

        signature_lines = [line]
        while "{" not in signature_lines[-1] and i + 1 < len(lines):
            i += 1
            signature_lines.append(lines[i])

        signature = "\n".join(signature_lines)
        if "useLegacyPrecision" not in signature:
            i += 1
            continue

        brace_depth = signature.count("{") - signature.count("}")
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            stripped = current.strip()
            if (
                stripped
                and not stripped.startswith("//")
                and "LegacyPrecisionMath" not in current
                and LEGACY_RAW_DERIVATIVE_ARITH_PATTERN.search(current)
            ):
                total += 1
            brace_depth += current.count("{") - current.count("}")
            i += 1

    return total


def count_legacy_hidden_prodsub_hits(text: str) -> int:
    total = 0
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//"):
            continue
        if LEGACY_HIDDEN_PRODSUB_PATTERN.search(line):
            total += 1
    return total


def count_branchless_legacy_methods(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped_line = line.lstrip()
        if not SIGNATURE_START_PATTERN.search(line) or stripped_line.startswith("if ") or stripped_line.startswith("if("):
            i += 1
            continue

        signature_lines = [line]
        while "{" not in signature_lines[-1] and i + 1 < len(lines):
            i += 1
            signature_lines.append(lines[i])

        signature = "\n".join(signature_lines)
        if "useLegacyPrecision" not in signature:
            i += 1
            continue

        brace_depth = signature.count("{") - signature.count("}")
        has_branch = False
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            if re.search(r"\bif\s*\(\s*useLegacyPrecision\b", current):
                has_branch = True
            brace_depth += current.count("{") - current.count("}")
            i += 1

        if not has_branch:
            total += 1

    return total


def count_legacy_raw_sumprod_hits(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        if not re.search(r"\bif\s*\(\s*useLegacyPrecision\b", line):
            i += 1
            continue

        brace_depth = line.count("{") - line.count("}")
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            stripped = current.strip()
            if (
                stripped
                and not stripped.startswith("//")
                and "LegacyPrecisionMath" not in current
                and current.count("*") >= 2
                and "+" in current
            ):
                total += 1
            brace_depth += current.count("{") - current.count("}")
            i += 1

    return total


def count_legacy_branch_without_return(text: str) -> int:
    lines = text.splitlines()
    total = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        if not re.search(r"\bif\s*\(\s*useLegacyPrecision\b", line):
            i += 1
            continue

        brace_depth = line.count("{") - line.count("}")
        has_return = False
        i += 1
        while i < len(lines) and brace_depth > 0:
            current = lines[i]
            if re.search(r"\breturn\b", current):
                has_return = True
            brace_depth += current.count("{") - current.count("}")
            i += 1

        if not has_return:
            total += 1

    return total


def collect(root: Path) -> list[FileStats]:
    rows: list[FileStats] = []
    for path in root.rglob("*.cs"):
        try:
            text = path.read_text()
        except OSError:
            continue

        numeric_hits = len(NUMERIC_PATTERN.findall(text))
        if numeric_hits == 0:
            continue

        rows.append(
            FileStats(
                path=path,
                numeric_hits=numeric_hits,
                legacy_hits=len(LEGACY_PATTERN.findall(text)),
                fortran_hits=len(FORTRAN_PATTERN.findall(text)),
                shared_preamble_hits=count_shared_preamble_hits(text),
                bare_hkin_hits=len(KINEMATIC_HELPER_PATTERN.findall(text)),
                seed_bypass_hits=(
                    count_seed_bypass_hits(text)
                    if SEED_TRACE_PATTERN.search(text)
                    else 0
                ),
                derivative_chain_hits=count_derivative_chain_hits(text),
                producer_override_hits=count_producer_override_hits(text),
                blkin_shortcut_hits=count_blkin_shortcut_hits(text),
                derivative_fastpath_hits=count_derivative_fastpath_hits(text),
                hvrat_direct_hits=count_hvrat_direct_hits(text),
                legacy_gamma_staging_hits=count_legacy_gamma_staging_hits(text),
                trace_recomposition_hits=count_trace_recomposition_hits(text),
                trace_precision_hits=count_trace_precision_hits(path, text),
                parity_row_combine_hits=count_parity_row_combine_hits(text),
                secondary_override_hits=count_secondary_override_hits(text),
                legacy_wrapper_override_hits=count_legacy_wrapper_override_hits(text),
                legacy_snapshot_hits=count_legacy_snapshot_hits(text),
                legacy_ternary_affine_hits=count_legacy_ternary_affine_hits(text),
                legacy_snapshot_refresh_hits=count_legacy_snapshot_refresh_hits(text),
                legacy_snapshot_reassembly_hits=count_legacy_snapshot_reassembly_hits(text),
                legacy_kinematic_refresh_hits=count_legacy_kinematic_refresh_hits(text),
                legacy_refresh_trace_hits=count_legacy_refresh_trace_hits(text),
                legacy_gauss_fma_hits=count_legacy_gauss_fma_hits(text),
                legacy_float_combine_hits=count_legacy_float_combine_hits(text),
                legacy_chain_widening_hits=count_legacy_chain_widening_hits(text),
                legacy_raw_derivative_arith_hits=count_legacy_raw_derivative_arith_hits(text),
                legacy_hidden_prodsub_hits=count_legacy_hidden_prodsub_hits(text),
                legacy_recurrence_update_hits=count_legacy_recurrence_update_hits(text),
                legacy_weighted_blend_hits=count_legacy_weighted_blend_hits(text),
                legacy_product_difference_hits=count_legacy_product_difference_hits(text),
                legacy_grouped_update_hits=count_legacy_grouped_update_hits(text),
                legacy_product_sum_plus_hits=count_legacy_product_sum_plus_hits(text),
                legacy_raw_pow_hits=count_legacy_raw_pow_hits(text),
                legacy_literal_affine_hits=count_legacy_literal_affine_hits(text),
                legacy_fma_review_hits=count_legacy_fma_review_hits(text),
                legacy_sumprod_review_hits=count_legacy_sumprod_review_hits(text),
                legacy_grouped_halfsum_hits=count_legacy_grouped_halfsum_hits(text),
                branchless_legacy_methods=count_branchless_legacy_methods(text),
                legacy_raw_sumprod_hits=count_legacy_raw_sumprod_hits(text),
                legacy_branch_without_return=count_legacy_branch_without_return(text),
            )
        )

    rows.sort(key=lambda row: (-row.numeric_hits, str(row.path)))
    return rows


def main() -> int:
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("/Users/slava/Agents/XFoilSharp/src")
    rows = collect(root)

    print(f"Scanned root: {root}")
    print(f"Files with numeric hotspots: {len(rows)}")
    print("hits legacy fortran shared hkin seed deriv prod blkin fast0 hvrat gm1 traceRe traceFmt rowCombine secOverride wrapOverride snapshot ternaryFma refresh reassemble kRefresh refreshTrace gaussFma fCombine chainWide rawDeriv prodSub recurUpdate wBlend pDiff grpUpd pSumAdd powf litFma fmaReview sumprodReview halfSum nobranch sumprod noreturn")
    for row in rows:
        print(
            f"{row.numeric_hits:4d} {row.legacy_hits:6d} {row.fortran_hits:7d} {row.shared_preamble_hits:6d} {row.bare_hkin_hits:5d} {row.seed_bypass_hits:5d} {row.derivative_chain_hits:5d} {row.producer_override_hits:4d} {row.blkin_shortcut_hits:5d} {row.derivative_fastpath_hits:5d} {row.hvrat_direct_hits:5d} {row.legacy_gamma_staging_hits:4d} {row.trace_recomposition_hits:7d} {row.trace_precision_hits:8d} {row.parity_row_combine_hits:10d} {row.secondary_override_hits:11d} {row.legacy_wrapper_override_hits:12d} {row.legacy_snapshot_hits:8d} {row.legacy_ternary_affine_hits:10d} {row.legacy_snapshot_refresh_hits:7d} {row.legacy_snapshot_reassembly_hits:10d} {row.legacy_kinematic_refresh_hits:8d} {row.legacy_refresh_trace_hits:12d} {row.legacy_gauss_fma_hits:8d} {row.legacy_float_combine_hits:8d} {row.legacy_chain_widening_hits:9d} {row.legacy_raw_derivative_arith_hits:8d} {row.legacy_hidden_prodsub_hits:7d} {row.legacy_recurrence_update_hits:11d} {row.legacy_weighted_blend_hits:6d} {row.legacy_product_difference_hits:5d} {row.legacy_grouped_update_hits:6d} {row.legacy_product_sum_plus_hits:7d} {row.legacy_raw_pow_hits:4d} {row.legacy_literal_affine_hits:6d} {row.legacy_fma_review_hits:9d} {row.legacy_sumprod_review_hits:13d} {row.legacy_grouped_halfsum_hits:7d} {row.branchless_legacy_methods:8d} {row.legacy_raw_sumprod_hits:7d} {row.legacy_branch_without_return:8d} {row.path}"
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
