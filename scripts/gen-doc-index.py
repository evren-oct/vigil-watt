#!/usr/bin/env python3
"""生成 docs/INDEX.md：文档标题、状态与最后修改日期。

用法：
    python scripts/gen-doc-index.py          # 写入 docs/INDEX.md
    python scripts/gen-doc-index.py --check  # 只校验，过期时退出码 1
"""

from __future__ import annotations

import subprocess
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
INDEX = DOCS / "INDEX.md"


def git_date(path: Path) -> str:
    try:
        out = subprocess.run(
            ["git", "log", "-1", "--format=%cs", "--", str(path.relative_to(ROOT))],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()
        return out or date.fromtimestamp(path.stat().st_mtime).isoformat()
    except Exception:
        return date.fromtimestamp(path.stat().st_mtime).isoformat()


def title_of(path: Path) -> str:
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("# "):
            return line[2:].strip()
    return path.stem


def status_of(path: Path) -> str:
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        text = line.lstrip("> ").strip()
        if text.startswith("**状态："):
            return text.split("：", 1)[1].split("。", 1)[0].strip("* ")
        if text.startswith("状态："):
            return text.split("：", 1)[1].split("。", 1)[0].strip()
    return "—"


def build() -> str:
    files = sorted(p for p in DOCS.rglob("*.md") if p != INDEX)
    groups: dict[str, list[Path]] = {}
    for path in files:
        rel = path.relative_to(DOCS)
        group = "/".join(rel.parts[:-1]) if len(rel.parts) > 1 else "根目录"
        groups.setdefault(group, []).append(path)

    lines = [
        "# 文档索引",
        "",
        "> 本文件由 `python scripts/gen-doc-index.py` 生成，不要手改。",
        "> 新增/移动文档后重跑；`--check` 会在过期时退出码 1。日期取 git 最后提交，未入库文件用文件时间。",
        f"> 共 {len(files)} 份 Markdown；最后生成：{date.today().isoformat()}。",
        "",
    ]
    for group in sorted(groups, key=lambda g: (g == "根目录", g)):
        lines.extend([f"## {group}", "", "| 文档 | 标题 | 状态 | 最后提交 |", "|---|---|---|---|"])
        for path in groups[group]:
            rel = path.relative_to(ROOT).as_posix()
            lines.append(f"| [{path.name}]({rel[len('docs/'):]}) | {title_of(path)} | {status_of(path)} | {git_date(path)} |")
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    content = build()
    if "--check" in sys.argv:
        if not INDEX.exists() or INDEX.read_text(encoding="utf-8") != content:
            print("docs/INDEX.md 已过期，请运行 python scripts/gen-doc-index.py")
            return 1
        print("docs/INDEX.md 是最新的")
        return 0
    INDEX.write_text(content, encoding="utf-8")
    print(f"wrote {INDEX.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
