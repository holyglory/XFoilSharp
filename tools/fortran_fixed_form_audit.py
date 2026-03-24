#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import re
import sys


def is_comment_or_blank(line: str) -> bool:
    if not line.strip():
        return True

    first = line[:1]
    return first in {"c", "C", "*", "!"}


def has_invalid_statement_column(line: str) -> bool:
    if len(line) < 6:
        return False
    if line[:5] != "     ":
        return False
    marker = line[5]
    return marker not in {" ", "0", "&"}


GENERATED_DEBUG_ROOTS = (
    Path("/Users/slava/Agents/XFoilSharp/tools/fortran-debug/build"),
    Path("/Users/slava/Agents/XFoilSharp/tools/fortran-debug/build-check"),
    Path("/Users/slava/Agents/XFoilSharp/tools/fortran-debug/build-isolate-dw2"),
)


def should_skip_generated_debug_path(path: Path, root: Path) -> bool:
    resolved_path = path.resolve()
    resolved_root = root.resolve()

    # When auditing a parent tree like tools/fortran-debug, skip generated
    # build folders to avoid duplicate noise. When auditing a build folder
    # directly, do not skip it, because fixed-form truncation must be checked
    # on the exact generated sources that will be compiled.
    return any(
        generated_root in resolved_path.parents and resolved_root != generated_root
        for generated_root in GENERATED_DEBUG_ROOTS
    )


IDENTIFIER_RE = re.compile(r"\b([A-Z][A-Z0-9_]*)\b", re.IGNORECASE)
SUBROUTINE_RE = re.compile(r"^\s{5}[ 0&]SUBROUTINE\b", re.IGNORECASE)
TRACE_CALL_RE = re.compile(r"\bCALL\s+TRACE_[A-Z0-9_]+\s*\(", re.IGNORECASE)
INCLUDE_RE = re.compile(r"^\s*INCLUDE\s+'([^']+)'", re.IGNORECASE)
DECLARATION_PREFIXES = (
    "REAL",
    "INTEGER",
    "LOGICAL",
    "CHARACTER",
    "DOUBLE PRECISION",
    "DIMENSION",
    "COMMON",
    "PARAMETER",
)
KNOWN_INTRINSICS = {
    "ABS",
    "MAX",
    "MIN",
    "EXP",
    "LOG",
    "SQRT",
    "SIN",
    "COS",
    "TANH",
    "ATAN",
    "ATAN2",
    "AMAX1",
    "AMIN1",
    "FLOAT",
    "INT",
    "NINT",
    "MOD",
}


def statement_text(lines: list[str], start: int) -> tuple[str, int]:
    pieces = [lines[start][6:72].rstrip("\n")]
    index = start + 1
    while index < len(lines):
        line = lines[index]
        if len(line) >= 6 and line[:5] == "     " and line[5] not in {" ", "0"}:
            pieces.append(line[6:72].rstrip("\n"))
            index += 1
            continue
        break

    return " ".join(piece.strip() for piece in pieces), index - 1


def split_arguments(argument_text: str) -> list[str]:
    args: list[str] = []
    current: list[str] = []
    depth = 0
    in_string = False
    string_delim = ""

    for char in argument_text:
        if in_string:
            current.append(char)
            if char == string_delim:
                in_string = False
            continue

        if char in {"'", '"'}:
            in_string = True
            string_delim = char
            current.append(char)
            continue

        if char == "(":
            depth += 1
            current.append(char)
            continue

        if char == ")":
            if depth > 0:
                depth -= 1
            current.append(char)
            continue

        if char == "," and depth == 0:
            args.append("".join(current).strip())
            current = []
            continue

        current.append(char)

    trailing = "".join(current).strip()
    if trailing:
        args.append(trailing)
    return args


def identifiers_in_expression(expression: str) -> set[str]:
    text = re.sub(r"'[^']*'", " ", expression)
    return {match.upper() for match in IDENTIFIER_RE.findall(text)}


def include_symbols(path: Path, cache: dict[Path, set[str]]) -> set[str]:
    resolved = path.resolve()
    if resolved in cache:
        return cache[resolved]

    symbols: set[str] = set()
    try:
        lines = resolved.read_text(errors="ignore").splitlines()
    except OSError:
        cache[resolved] = symbols
        return symbols

    index = 0
    while index < len(lines):
        line = lines[index]
        statement, end = statement_text(lines, index)
        upper = statement.upper()
        include_match = INCLUDE_RE.match(statement)
        if include_match:
            include_path = resolved.parent / include_match.group(1)
            symbols.update(include_symbols(include_path, cache))
        elif upper.startswith(DECLARATION_PREFIXES) or upper.startswith("COMMON/") or upper.startswith("COMMON /"):
            symbols.update(identifiers_in_expression(statement))
        index = end + 1

    cache[resolved] = symbols
    return symbols


def trace_argument_violations(path: Path, lines: list[str]) -> list[tuple[int, str]]:
    violations: list[tuple[int, str]] = []
    include_cache: dict[Path, set[str]] = {}
    index = 0

    while index < len(lines):
        line = lines[index]
        if not SUBROUTINE_RE.match(line):
            index += 1
            continue

        header, header_end = statement_text(lines, index)
        if "(" in header and ")" in header:
            header_args_text = header.split("(", 1)[1].rsplit(")", 1)[0]
            known_names = identifiers_in_expression(header_args_text)
        else:
            known_names = set()
        block_statements: list[tuple[int, str, str]] = []
        scan_index = header_end + 1
        while scan_index < len(lines):
            current = lines[scan_index]
            if SUBROUTINE_RE.match(current):
                break

            statement, end = statement_text(lines, scan_index)
            upper = statement.upper()
            block_statements.append((scan_index + 1, statement, upper))
            scan_index = end + 1

        seen_nontrace: set[str] = set()
        for _, statement, upper in block_statements:
            include_match = INCLUDE_RE.match(statement)
            if include_match:
                include_path = path.parent / include_match.group(1)
                known_names.update(include_symbols(include_path, include_cache))
            elif upper.startswith(DECLARATION_PREFIXES) or upper.startswith("COMMON/") or upper.startswith("COMMON /"):
                known_names.update(identifiers_in_expression(statement))
            else:
                assignment_match = re.match(r".*?\b([A-Z][A-Z0-9_]*)\s*=", upper)
                if assignment_match and not re.search(r"[<>/]=|\.EQ\.|\.NE\.|\.LT\.|\.LE\.|\.GT\.|\.GE\.", upper):
                    known_names.add(assignment_match.group(1))
                if not TRACE_CALL_RE.search(upper):
                    seen_nontrace.update(identifiers_in_expression(statement))

        for line_number, statement, upper in block_statements:
            match = TRACE_CALL_RE.search(upper)
            if not match:
                continue

            call_text = statement[match.start():]
            args_text = call_text.split("(", 1)[1].rsplit(")", 1)[0]
            unknowns: set[str] = set()
            for arg in split_arguments(args_text):
                for identifier in identifiers_in_expression(arg):
                    if identifier in KNOWN_INTRINSICS:
                        continue
                    if identifier in known_names or identifier in seen_nontrace:
                        continue
                    unknowns.add(identifier)
            if unknowns:
                violations.append((line_number, ",".join(sorted(unknowns))))

        index = scan_index

    return violations


def find_violations(root: Path) -> list[tuple[Path, int, str, str]]:
    violations: list[tuple[Path, int, str, str]] = []
    for path in root.rglob("*.f"):
        if should_skip_generated_debug_path(path, root):
            continue

        try:
            lines = path.read_text(errors="ignore").splitlines()
        except OSError:
            continue

        for line_number, line in enumerate(lines, 1):
            if is_comment_or_blank(line):
                continue
            if len(line) > 72:
                violations.append((path, line_number, "line-length", line))
            if has_invalid_statement_column(line):
                violations.append((path, line_number, "column-6", line))

        for line_number, unknowns in trace_argument_violations(path, lines):
            violations.append((path, line_number, "trace-arg-unknown", unknowns))

    return violations


def main(argv: list[str]) -> int:
    roots = [Path(arg) for arg in argv[1:]] or [
        Path("/Users/slava/Agents/XFoilSharp/f_xfoil/src"),
        Path("/Users/slava/Agents/XFoilSharp/tools/fortran-debug"),
    ]

    violations: list[tuple[Path, int, str, str]] = []
    for root in roots:
        violations.extend(find_violations(root))

    for path, line_number, violation_type, line in violations:
        print(f"{path}:{line_number}:{violation_type}:{line}")

    print(f"fixed-form violations: {len(violations)}")
    return 1 if violations else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
