#!/usr/bin/env python3
"""Extract user-visible CJK literals from VigilWatt sources for i18n.

Usage:
    python scripts/i18n/extract_strings.py --out scripts/i18n/extracted.json

The output is a machine-readable inventory: every occurrence records its file,
line, source kind and literal text. Comments, log calls, protocol constants and
settings keys are excluded. Human review happens in the translation file.
"""

from __future__ import annotations

import argparse
import bisect
import json
import re
from collections import Counter
from pathlib import Path

CJK = re.compile(r"[\u4e00-\u9fff]")
CS_STRING = re.compile(r'(?P<quote>@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*")')
CS_TOKEN = re.compile(
    r'(?P<string>@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*")'
    r'|(?P<comment>//[^\r\n]*|/\*.*?\*/)',
    re.S,
)
XAML_ATTR = re.compile(r'(?P<name>[\w:.\-]+)\s*=\s*"(?P<value>[^"]*)"')
XAML_TEXT = re.compile(r">(?P<text>[^<>]+)<")
LOG_CALL = re.compile(r"\bLog\.(Info|Warn|Error|Debug|Trace|InfoOnChange)\b")
EXCLUDED_FILES = ("Comments",)
EXCLUDED_DIRS = ("bin", "obj", "publish", "dist", ".git", "Localization")


def strip_comments_cs(text: str) -> str:
    """Replace comment bodies with spaces, keeping offsets and line numbers."""
    def blank(m: re.Match[str]) -> str:
        if m.group("string"):
            return m.group(0)
        return re.sub(r"[^\r\n]", " ", m.group(0))

    return CS_TOKEN.sub(blank, text)


def unescape_cs(value: str) -> str:
    value = value[1:-1]
    return (
        value.replace("\\r\\n", "\n")
        .replace("\\n", "\n")
        .replace('\\"', '"')
        .replace("\\\\", "\\")
    )


def scan_cs(path: Path, root: Path) -> list[dict]:
    original = path.read_text(encoding="utf-8-sig")
    clean = strip_comments_cs(original)
    lines = clean.splitlines()
    newline_positions = [i for i, ch in enumerate(clean) if ch == "\n"]
    found = []
    for m in CS_STRING.finditer(clean):
        raw = m.group("quote")
        if not CJK.search(raw):
            continue
        line_no = bisect.bisect_left(newline_positions, m.start()) + 1
        line = lines[line_no - 1] if line_no <= len(lines) else ""
        if LOG_CALL.search(line):
            continue
        verbatim = raw.startswith("@")
        value = raw[2:-1].replace('""', '"') if verbatim else unescape_cs(raw)
        prefix = clean[max(0, m.start() - 2):m.start()]
        interpolated = "$" in prefix
        found.append(
            {
                "file": path.relative_to(root).as_posix(),
                "line": line_no,
                "kind": "cs-interpolated" if interpolated else "cs",
                "context": line.strip()[:150],
                "text": value,
            }
        )
    return found


def scan_xaml(path: Path, root: Path) -> list[dict]:
    text = path.read_text(encoding="utf-8-sig")
    text = re.sub(r"<!--.*?-->", lambda m: re.sub(r"[^\r\n]", " ", m.group(0)), text, flags=re.S)
    found = []
    for m in XAML_ATTR.finditer(text):
        value = m.group("value")
        if not CJK.search(value):
            continue
        name = m.group("name")
        if name in ("x:Name", "x:Class", "x:Key", "xmlns", "AutomationProperties.AutomationId"):
            continue
        line_no = text.count("\n", 0, m.start()) + 1
        found.append(
            {
                "file": path.relative_to(root).as_posix(),
                "line": line_no,
                "kind": "xaml-attr",
                "context": name,
                "text": value.strip(),
            }
        )
    for m in XAML_TEXT.finditer(text):
        value = m.group("text")
        if not CJK.search(value) or not value.strip():
            continue
        line_no = text.count("\n", 0, m.start()) + 1
        found.append(
            {
                "file": path.relative_to(root).as_posix(),
                "line": line_no,
                "kind": "xaml-text",
                "context": "text",
                "text": value.strip(),
            }
        )
    return found


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".", help="repository root")
    parser.add_argument("--out", default="scripts/i18n/extracted.json")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    occurrences: list[dict] = []
    targets = [
        root / "src" / "VigilWatt.App",
        root / "src" / "VigilWatt.Core" / "Models",
        root / "src" / "VigilWatt.Core" / "Settings",
    ]
    for base in targets:
        for path in sorted(base.rglob("*")):
            if not path.is_file() or path.suffix not in (".cs", ".xaml"):
                continue
            if any(part in EXCLUDED_DIRS for part in path.parts) or any(
                part in EXCLUDED_FILES for part in path.parts
            ):
                continue
            occurrences.extend(scan_cs(path, root) if path.suffix == ".cs" else scan_xaml(path, root))
    unique = Counter(o["text"] for o in occurrences)
    payload = {
        "occurrences": occurrences,
        "unique": [
            {"text": text, "count": count, "files": sorted({o["file"] for o in occurrences if o["text"] == text})}
            for text, count in unique.most_common()
        ],
    }
    out = Path(args.out)
    if not out.is_absolute():
        out = root / out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"occurrences={len(occurrences)} unique={len(unique)} out={out}")


if __name__ == "__main__":
    main()
