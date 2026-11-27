#!/usr/bin/env python3
"""Shared C# literal scanner for the i18n tooling.

Handles verbatim/interpolated strings and string literals nested inside
interpolation holes (the built-in extractor's simple regex splits those).
"""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class Literal:
    start: int
    end: int
    raw: str
    prefix: str
    nested: list["Literal"] = field(default_factory=list)

    @property
    def interpolated(self) -> bool:
        return "$" in self.prefix

    @property
    def verbatim(self) -> bool:
        return "@" in self.prefix

    def text(self, source: str) -> str:
        """Decoded source text, with C# escapes turned into runtime characters."""
        value = self.raw
        if self.verbatim:
            return value.replace('""', '"')
        return (
            value.replace("\\r\\n", "\n")
            .replace("\\n", "\n")
            .replace('\\"', '"')
            .replace("\\\\", "\\")
        )


def parse_literal(source: str, start: int) -> tuple[Literal, int]:
    i = start
    prefix = ""
    while source[i] in "$@":
        prefix += source[i]
        i += 1
    if source[i] != '"':
        raise ValueError(f"not a string literal at {start}")
    quote = i
    i += 1
    depth = 0
    nested: list[Literal] = []
    while i < len(source):
        ch = source[i]
        if ch == "\\" and "@" not in prefix:
            i += 2
            continue
        if ch == '"' and "@" in prefix and i + 1 < len(source) and source[i + 1] == '"':
            i += 2
            continue
        if ch == '"':
            if depth == 0:
                lit = Literal(start, i + 1, source[quote + 1 : i], prefix, nested)
                return lit, i + 1
            child, i = parse_literal(source, i)
            nested.append(child)
            continue
        if "$" in prefix and ch == "{":
            if i + 1 < len(source) and source[i + 1] == "{":
                i += 2
                continue
            depth += 1
            i += 1
            continue
        if "$" in prefix and ch == "}":
            if i + 1 < len(source) and source[i + 1] == "}":
                i += 2
                continue
            depth -= 1
            i += 1
            continue
        i += 1
    raise ValueError(f"unterminated string literal at {start}")


def scan_top_level(source: str) -> list[Literal]:
    """Return only literals outside interpolation holes (safe to replace in-place)."""
    found: list[Literal] = []
    i = 0
    while i < len(source):
        if source.startswith("//", i):
            end = source.find("\n", i)
            i = len(source) if end < 0 else end + 1
            continue
        if source.startswith("/*", i):
            end = source.find("*/", i + 2)
            i = len(source) if end < 0 else end + 2
            continue
        j = i
        while j < len(source) and source[j] in "$@":
            j += 1
        if j < len(source) and source[j] == '"':
            lit, i = parse_literal(source, i)
            found.append(lit)
            continue
        i += 1
    return found


def scan_literals(source: str) -> list[Literal]:
    """Return every string literal, including literals nested in holes."""
    found: list[Literal] = []

    def add(lit: Literal) -> None:
        found.append(lit)
        for child in lit.nested:
            add(child)

    for lit in scan_top_level(source):
        add(lit)
    return found
