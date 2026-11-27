#!/usr/bin/env python3
"""Apply the reviewed translation table to C# and XAML sources.

Usage:
    python scripts/i18n/apply_localization.py

Run merge_translations.py and generate_strings.py first. The script is
idempotent: already-localized literals are left alone.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
I18N = ROOT / "scripts" / "i18n"
sys.path.insert(0, str(I18N))

from cs_scan import Literal, scan_top_level  # noqa: E402
from merge_translations import parse_holes  # noqa: E402

APP_ROOT = ROOT / "src" / "VigilWatt.App"
CORE_ROOTS = [ROOT / "src" / "VigilWatt.Core" / "Models", ROOT / "src" / "VigilWatt.Core" / "Settings"]

CJK = re.compile(r"[\u4e00-\u9fff]")
LOG_CALL = re.compile(r"\bLog\.(Info|Warn|Error|Debug|Trace|InfoOnChange)\b")
PLACEHOLDER = re.compile(r"\{(\d+)")
XAML_ATTR = re.compile(r'(?P<name>[\w:.\-]+)\s*=\s*"(?P<value>[^"]*)"')
XAML_SKIP_NAMES = {"x:Name", "x:Class", "x:Key", "xmlns", "AutomationProperties.AutomationId"}

# Detection keywords and protocol constants: deliberately not user-visible.
SKIP_CS_FILES = {"DeviceKindGuesser.cs"}
SKIP_XAML_NAMES = XAML_SKIP_NAMES

KEY_MAP = json.loads((I18N / "key_map.json").read_text(encoding="utf-8"))
TRANSLATIONS = json.loads(
    (APP_ROOT / "Localization" / "translations.json").read_text(encoding="utf-8-sig")
)

stats = {"cs": 0, "xaml": 0, "datacontext": 0, "skipped": 0}
unknown: list[str] = []


def read_source(path: Path) -> tuple[str, bool]:
    raw = path.read_bytes()
    bom = raw.startswith(b"\xef\xbb\xbf")
    return raw.decode("utf-8-sig"), bom


def write_source(path: Path, text: str, bom: bool) -> None:
    data = text.encode("utf-8")
    path.write_bytes((b"\xef\xbb\xbf" + data) if bom else data)


def cs_string(value: str) -> str:
    return (
        '"'
        + value.replace("\\", "\\\\")
        .replace('"', '\\"')
        .replace("\r", "\\r")
        .replace("\n", "\\n")
        + '"'
    )


def ensure_using(text: str, name: str) -> str:
    if f"using {name};" in text:
        return text
    match = re.search(r"^namespace\s", text, re.M)
    if match:
        return text[: match.start()] + f"using {name};\n" + text[match.start() :]
    return f"using {name};\n" + text


def placeholder_count(value: str) -> int:
    indexes = [int(m.group(1)) for m in PLACEHOLDER.finditer(value)]
    return max(indexes) + 1 if indexes else 0


def decode_raw(raw: str, verbatim: bool) -> str:
    if verbatim:
        return raw.replace('""', '"')
    return (
        raw.replace("\\r\\n", "\n")
        .replace("\\n", "\n")
        .replace('\\"', '"')
        .replace("\\\\", "\\")
    )


def localize_literal(lit: Literal, source: str, layer: str) -> str | None:
    """Render one literal (including nested literals in its holes)."""
    original = lit.text(source)
    if not CJK.search(original):
        return None
    key = KEY_MAP.get(original)
    if not key:
        unknown.append(f"{original!r} has no key")
        return None

    raw_start = lit.start + len(lit.prefix) + 1
    raw = lit.raw
    for child in sorted(lit.nested, key=lambda item: item.start, reverse=True):
        child_expr = localize_literal(child, source, layer)
        if child_expr is None:
            continue
        start = child.start - raw_start
        end = child.end - raw_start
        raw = raw[:start] + child_expr + raw[end:]

    translated = decode_raw(raw, lit.verbatim)
    entry = TRANSLATIONS[key]

    if not lit.interpolated:
        if layer == "core":
            return f'UiText.Get({cs_string(key)}, {cs_string(entry["zh"])})'
        return f"Strings.Current.Get({cs_string(key)})"

    holes = parse_holes(translated)
    args = [hole.expr for hole in holes]
    expected = placeholder_count(entry["zh"])
    if len(args) != expected:
        unknown.append(f"{original!r}: {len(args)} args, {expected} placeholders")
        return None
    arg_text = ", ".join(args)
    if layer == "core":
        return (
            f"UiText.Format({cs_string(key)}, {cs_string(entry['zh'])}"
            + (f", {arg_text}" if arg_text else "")
            + ")"
        )
    return (
        f"Strings.Current.Format({cs_string(key)}"
        + (f", {arg_text}" if arg_text else "")
        + ")"
    )


def process_cs(path: Path, layer: str) -> None:
    if path.name in SKIP_CS_FILES or "Localization" in path.parts:
        return
    text, bom = read_source(path)
    lines = text.splitlines()
    replacements: list[tuple[int, int, str]] = []
    used_layer = False

    for lit in scan_top_level(text):
        line_no = text.count("\n", 0, lit.start) + 1
        if line_no <= len(lines) and LOG_CALL.search(lines[line_no - 1]):
            stats["skipped"] += 1
            continue
        original = lit.text(text)
        if not CJK.search(original):
            continue
        # Initial status lives in a static field initialized before Strings exists.
        if path.name == "App.xaml.cs" and original == "启动时检查更新":
            key = KEY_MAP[original]
            expr = f'UiText.Get({cs_string(key)}, {cs_string(original)})'
            replacements.append((lit.start, lit.end, expr))
            stats["cs"] += 1
            continue
        expr = localize_literal(lit, text, layer)
        if expr is None:
            stats["skipped"] += 1
            continue
        replacements.append((lit.start, lit.end, expr))
        used_layer = True
        stats["cs"] += 1

    if replacements:
        for start, end, expr in sorted(replacements, reverse=True):
            text = text[:start] + expr + text[end:]
        if used_layer:
            text = ensure_using(
                text,
                "VigilWatt.App.Localization" if layer == "app" else "VigilWatt.Core.Localization",
            )
        if path.name == "App.xaml.cs":
            text = ensure_using(text, "VigilWatt.Core.Localization")

    # const string cannot call a translator; make these lazy properties.
    text = re.sub(
        r"(\b)(const)(\s+string\s+\w+\s*)=\s*((?:Strings\.Current|UiText)\.(?:Get|Format)\([^\n]*?\));",
        r"\1static\3=> \4;",
        text,
    )
    write_source(path, text, bom)


def process_xaml(path: Path) -> None:
    text, bom = read_source(path)

    def replace(match: re.Match[str]) -> str:
        name = match.group("name")
        value = match.group("value")
        if name in SKIP_XAML_NAMES or not CJK.search(value):
            return match.group(0)
        if "{Binding" in value or "{x:Bind" in value:
            return match.group(0)
        key = KEY_MAP.get(value.strip())
        if not key:
            unknown.append(f"XAML {path.name}: {value!r} has no key")
            return match.group(0)
        stats["xaml"] += 1
        return f'{name}="{{Binding {key}, Mode=OneWay}}"'

    text = XAML_ATTR.sub(replace, text)
    write_source(path, text, bom)


def set_datacontexts() -> None:
    for xaml in sorted(APP_ROOT.rglob("*.xaml")):
        code = xaml.with_suffix(xaml.suffix + ".cs")
        if not code.exists() or xaml.name == "App.xaml":
            continue
        text, bom = read_source(code)
        if "DataContext = Strings.Current;" in text:
            continue
        if "InitializeComponent();" not in text:
            continue
        text = ensure_using(text, "VigilWatt.App.Localization")
        text = text.replace(
            "InitializeComponent();",
            "InitializeComponent();\n        DataContext = Strings.Current;",
            1,
        )
        write_source(code, text, bom)
        stats["datacontext"] += 1


def main() -> None:
    for base, layer in ((APP_ROOT, "app"), (CORE_ROOTS[0], "core"), (CORE_ROOTS[1], "core")):
        for path in sorted(base.rglob("*.cs")):
            if any(part in ("bin", "obj", "publish", "Comments") for part in path.parts):
                continue
            process_cs(path, layer)
    for base in (APP_ROOT, *CORE_ROOTS):
        for path in sorted(base.rglob("*.xaml")):
            if any(part in ("bin", "obj", "publish") for part in path.parts):
                continue
            process_xaml(path)
    set_datacontexts()
    print(
        f"cs={stats['cs']} xaml={stats['xaml']} datacontext={stats['datacontext']} "
        f"skipped={stats['skipped']} unknown={len(unknown)}"
    )
    for item in unknown[:30]:
        print("UNKNOWN", item)


if __name__ == "__main__":
    main()
