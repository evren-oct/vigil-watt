#!/usr/bin/env python3
"""Merge the reviewed TSV translations into translations.json.

Usage:
    python scripts/i18n/merge_translations.py

The extractor inventory supplies the source text and its kind; ru-part*.tsv
supplies Russian. Interpolated strings are rewritten to numbered placeholders
so the generated Format("Key", ...) calls are stable.
"""

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
I18N = ROOT / "scripts" / "i18n"
TARGET = ROOT / "src" / "VigilWatt.App" / "Localization" / "translations.json"

# Fragments produced by the regex extractor when an interpolation contains a
# nested string literal. The real literals are added in MANUAL_EXTRA below.
SKIP_TEXTS = {
    '沙箱组={(sandbox.Length == 0 ? ',
    '预检 {_capabilities.Count} 项 · {string.Join(',
    '开始采集（含 Feature 读取 {string.Join(',
    "已开启 {enabled.Count} 个 · {string.Join(",
    "{NumberText(ThresholdBox.Value)}% · 估算{(RemainingTimeEstimateToggle.IsOn ? ",
    ")} · {SelectedInt(WidgetMaxCombo, WidgetMaxDevicesOptions.Default)} 台",
    ")} · {SelectedInt(TaskbarMaxCombo, TaskbarBatteryOptions.Default)} 台",
    ")} · {NumberText(LogRetentionBox.Value)} 天",
}

# Literals hidden from the extractor by nested quotes, plus nested labels.
MANUAL_EXTRA = {
    '沙箱组={(sandbox.Length == 0 ? "无" : string.Join("|", sandbox))}':
        'Группа песочницы: {(sandbox.Length == 0 ? "无" : string.Join("|", sandbox))}',
    '预检 {_capabilities.Count} 项 · {string.Join(" · ", counts)}':
        'Предварительная проверка: {_capabilities.Count} · {string.Join(" · ", counts)}',
    '开始采集（含 Feature 读取 {string.Join("、", featureIds.Select(i => $"0x{i:X2}"))}）。':
        'Начать сбор (включая чтение Feature: {string.Join("、", featureIds.Select(i => $"0x{i:X2}"))}).',
    '已开启 {enabled.Count} 个 · {string.Join("、", enabled.Take(3).Select(p => p.DisplayName))}':
        'Включено: {enabled.Count} · {string.Join("、", enabled.Take(3).Select(p => p.DisplayName))}',
    '{NumberText(ThresholdBox.Value)}% · 估算{(RemainingTimeEstimateToggle.IsOn ? "开" : "关")}':
        '{NumberText(ThresholdBox.Value)}% · оценка {(RemainingTimeEstimateToggle.IsOn ? "开" : "关")}',
    '{(WidgetToggle.IsOn ? "开" : "关")} · {SelectedInt(WidgetMaxCombo, WidgetMaxDevicesOptions.Default)} 台':
        '{(WidgetToggle.IsOn ? "开" : "关")} · {SelectedInt(WidgetMaxCombo, WidgetMaxDevicesOptions.Default)}',
    '{(TaskbarToggle.IsOn ? "开" : "关")} · {SelectedInt(TaskbarMaxCombo, TaskbarBatteryOptions.Default)} 台':
        '{(TaskbarToggle.IsOn ? "开" : "关")} · {SelectedInt(TaskbarMaxCombo, TaskbarBatteryOptions.Default)}',
    '{SelectedLabel(LogLevelCombo, "信息")} · {NumberText(LogRetentionBox.Value)} 天':
        '{SelectedLabel(LogLevelCombo, "信息")} · {NumberText(LogRetentionBox.Value)} дн',
    '{(live is not null ? "实时" : "最后记录")} · {last.Timestamp.LocalDateTime:HH:mm}':
        '{(live is not null ? "实时" : "最后记录")} · {last.Timestamp.LocalDateTime:HH:mm}',
    "无": "Нет",
    "实时": "В реальном времени",
    "最后记录": "Последняя запись",
}

CJK = re.compile(r"[\u4e00-\u9fff]")


@dataclass
class Hole:
    start: int
    end: int
    expr: str
    suffix: str


def parse_holes(text: str) -> list[Hole]:
    """Find interpolation holes, ignoring braces inside nested strings."""
    holes: list[Hole] = []
    i = 0
    while i < len(text):
        ch = text[i]
        # Skip nested string literals (regular, verbatim and char literals).
        if ch in ('"', "'") or (ch == "@" and i + 1 < len(text) and text[i + 1] == '"'):
            verbatim = ch == "@"
            i += 2 if verbatim else 1
            quote = '"'
            while i < len(text):
                if verbatim and text[i] == '"' and i + 1 < len(text) and text[i + 1] == '"':
                    i += 2
                    continue
                if text[i] == "\\" and not verbatim:
                    i += 2
                    continue
                if text[i] == quote:
                    i += 1
                    break
                i += 1
            continue
        if ch != "{":
            i += 1
            continue
        if i + 1 < len(text) and text[i + 1] == "{":
            i += 2
            continue
        depth = 1
        paren = 0
        bracket = 0
        j = i + 1
        expr_end = -1
        while j < len(text) and depth:
            c = text[j]
            if c in ('"', "'") or (c == "@" and j + 1 < len(text) and text[j + 1] == '"'):
                verbatim = c == "@"
                j += 2 if verbatim else 1
                quote = '"'
                while j < len(text):
                    if verbatim and text[j] == '"' and j + 1 < len(text) and text[j + 1] == '"':
                        j += 2
                        continue
                    if text[j] == "\\" and not verbatim:
                        j += 2
                        continue
                    if text[j] == quote:
                        j += 1
                        break
                    j += 1
                continue
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    break
            elif c == "(":
                paren += 1
            elif c == ")":
                paren -= 1
            elif c == "[":
                bracket += 1
            elif c == "]":
                bracket -= 1
            elif (
                c in (":", ",")
                and depth == 1
                and paren == 0
                and bracket == 0
                and expr_end < 0
            ):
                expr_end = j - (i + 1)
            j += 1
        if depth:
            raise ValueError(f"unbalanced hole in {text!r}")
        content = text[i + 1 : j]
        if expr_end < 0:
            expr_end = len(content)
        holes.append(Hole(i, j + 1, content[:expr_end].strip(), content[expr_end:]))
        i = j + 1
    return holes


def numeric(text: str, holes: list[Hole]) -> tuple[str, list[str]]:
    out: list[str] = []
    exprs: list[str] = []
    pos = 0
    for index, hole in enumerate(holes):
        out.append(text[pos : hole.start])
        out.append("{" + str(index) + hole.suffix + "}")
        exprs.append(hole.expr)
        pos = hole.end
    out.append(text[pos:])
    return "".join(out), exprs


def unescape_tsv(value: str) -> str:
    """TSV keeps newlines as the two characters \\n, like the source literal."""
    return value.replace("\\n", "\n").replace("\\r", "\r")


def key_for(text: str) -> str:
    return "S_" + hashlib.sha1(text.encode("utf-8")).hexdigest()[:10]


def main() -> None:
    inventory = json.loads((I18N / "extracted.json").read_text(encoding="utf-8"))
    kind_by_text: dict[str, str] = {}
    for occurrence in inventory["occurrences"]:
        text = occurrence["text"]
        if occurrence["kind"] == "cs-interpolated":
            kind_by_text[text] = "cs-interpolated"
        else:
            kind_by_text.setdefault(text, occurrence["kind"])

    translations: dict[str, dict[str, str]] = {}
    if TARGET.exists():
        translations = json.loads(TARGET.read_text(encoding="utf-8-sig"))
    key_by_zh = {entry["zh"]: key for key, entry in translations.items()}
    key_map_path = I18N / "key_map.json"
    if key_map_path.exists():
        key_by_zh.update(json.loads(key_map_path.read_text(encoding="utf-8")))

    tsv: dict[str, str] = {}
    for path in sorted(I18N.glob("ru-part*.tsv")):
        for line_no, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip():
                continue
            parts = line.split("\t")
            if len(parts) != 2:
                raise SystemExit(f"{path.name}:{line_no}: expected zh<TAB>ru")
            zh, ru = (unescape_tsv(part) for part in parts)
            if zh in SKIP_TEXTS:
                continue
            if zh in tsv and tsv[zh] != ru:
                raise SystemExit(f"conflicting translations for {zh!r}")
            tsv[zh] = ru
    tsv.update(MANUAL_EXTRA)

    missing = [
        item["text"]
        for item in inventory["unique"]
        if item["text"] not in tsv and item["text"] not in SKIP_TEXTS
    ]
    if missing:
        raise SystemExit(f"missing translations: {missing[:10]} (total {len(missing)})")

    generated: dict[str, dict[str, str]] = {}
    for text, ru in tsv.items():
        if not CJK.search(text):
            continue
        key = key_by_zh.get(text) or key_for(text)
        if kind_by_text.get(text) == "cs-interpolated" or "{" in text:
            zh_holes = parse_holes(text)
            ru_holes = parse_holes(ru)
            if len(zh_holes) != len(ru_holes):
                raise SystemExit(f"placeholder count mismatch for {text!r}")
            zh_value, zh_exprs = numeric(text, zh_holes)
            ru_value, ru_exprs = numeric(ru, ru_holes)
            if zh_exprs != ru_exprs:
                raise SystemExit(f"placeholder mismatch for {text!r}: {zh_exprs} != {ru_exprs}")
        else:
            zh_value, ru_value = text, ru
        if key in translations and translations[key]["zh"] != zh_value:
            raise SystemExit(f"key collision: {key}")
        generated[key] = {"zh": zh_value, "ru": ru_value}

    translations.update(generated)
    TARGET.write_text(
        json.dumps(translations, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    key_map = {zh: key_by_zh.get(zh) or key_for(zh) for zh in tsv if CJK.search(zh)}
    (I18N / "key_map.json").write_text(
        json.dumps(key_map, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"translations={len(translations)} generated={len(generated)} out={TARGET.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
