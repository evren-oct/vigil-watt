#!/usr/bin/env python3
"""文档健康检查：孤儿文档、标题编号、重复日期、体积与支持矩阵覆盖。

用法：
    python scripts/check-doc-health.py

退出码 1 表示有硬错误；体积超预算只警告，不阻断。
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
SKIP_HEADING_ORDER = {"release-notes.md"}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def check_orphans(files: list[Path]) -> list[str]:
    texts = {p: read(p) for p in files}
    problems = []
    for path in files:
        if not any(
            path.name in text
            for other, text in texts.items()
            if other != path and other.name != "INDEX.md"
        ):
            problems.append(f"孤儿文档（没有入站引用）：{path.relative_to(ROOT)}")
    return problems


def check_headings(files: list[Path]) -> list[str]:
    problems = []
    for path in files:
        if path.name in SKIP_HEADING_ORDER:
            continue
        nums = []
        for line in read(path).splitlines():
            m = re.match(r"^## (\d+)\.", line)
            if m:
                nums.append(int(m.group(1)))
        if len(nums) < 2:
            continue
        if nums != sorted(nums):
            problems.append(f"顶层章节编号乱序：{path.relative_to(ROOT)} -> {nums}")
        dup = sorted({n for n in nums if nums.count(n) > 1})
        if dup:
            problems.append(f"顶层章节编号重复：{path.relative_to(ROOT)} -> {dup}")
    return problems


def check_duplicate_dates(files: list[Path]) -> list[str]:
    problems = []
    for path in files:
        dates = []
        for line in read(path).splitlines():
            m = re.match(r"^## (\d{4}-\d{2}-\d{2})$", line)
            if m:
                dates.append(m.group(1))
        dup = sorted({d for d in dates if dates.count(d) > 1})
        if dup:
            problems.append(f"日期小节重复：{path.relative_to(ROOT)} -> {dup}")
    return problems


def check_sizes(files: list[Path]) -> tuple[list[str], list[str]]:
    errors, warnings = [], []
    for path in files:
        lines = len(read(path).splitlines())
        size_kb = path.stat().st_size / 1024
        rel = path.relative_to(ROOT)
        if lines > 1500 or size_kb > 150:
            errors.append(f"文档过大：{rel}（{lines} 行 / {size_kb:.1f} KB）")
        elif lines > 800 or size_kb > 80:
            warnings.append(f"文档偏大：{rel}（{lines} 行 / {size_kb:.1f} KB）")
    return errors, warnings


def check_matrix_coverage() -> list[str]:
    matrix = DOCS / "hardware" / "support-matrix.md"
    if not matrix.exists():
        return ["缺少 docs/hardware/support-matrix.md"]
    text = read(matrix)
    problems = []
    for folder in ("brands", "devices"):
        for path in sorted((DOCS / "hardware" / folder).glob("*.md")):
            if path.name not in text:
                problems.append(f"支持矩阵没有引用：{path.relative_to(ROOT)}")
    return problems


def check_backlog_ids() -> list[str]:
    open_path = DOCS / "reviews" / "BACKLOG.md"
    closed_path = DOCS / "reviews" / "archive" / "BACKLOG-CLOSED.md"
    if not open_path.exists() or not closed_path.exists():
        return []
    open_ids = re.findall(r"^\| \*\*(CR-\d+|D\d+)\*\*", read(open_path), re.M)
    closed_ids = re.findall(r"^\| (CR-\d+|D\d+) \|", read(closed_path), re.M)
    warnings = []
    for item in sorted(set(open_ids) & set(closed_ids)):
        warnings.append(f"BACKLOG 编号与归档撞号：{item}")
    for label, ids in (("BACKLOG", open_ids), ("BACKLOG-CLOSED", closed_ids)):
        for item in sorted({i for i in ids if ids.count(i) > 1}):
            warnings.append(f"{label} 内部编号重复：{item}")
    return warnings


def main() -> int:
    files = sorted(DOCS.rglob("*.md"))
    errors = []
    errors += check_orphans(files)
    errors += check_headings(files)
    errors += check_duplicate_dates(files)
    size_errors, size_warnings = check_sizes(files)
    errors += size_errors
    errors += check_matrix_coverage()
    errors += check_backlog_ids()

    if size_warnings:
        print("警告：")
        for item in size_warnings:
            print(f"  - {item}")
    if errors:
        print("错误：")
        for item in errors:
            print(f"  - {item}")
        return 1
    print(f"文档健康检查通过：{len(files)} 份 Markdown")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
