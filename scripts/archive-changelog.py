#!/usr/bin/env python3
"""把 changelog.md 里「上月及更早」的日期小节整段搬进 archive/changelog-YYYY-MM.md。

用法：
    python scripts/archive-changelog.py            # 实际搬迁
    python scripts/archive-changelog.py --dry-run  # 只打印打算搬哪些

为什么需要它：主文件只留当月条目，跨月时必须整月搬走。只写一条散文规则靠人记会漏——
2026-11 之前主文件已经长到 504 行 / 42 个日期小节。搬迁只搬不改，内容原样保留。

幂等：只搬主文件里还在的日期小节；归档按「新→旧」排列，后搬的更老条目接在末尾。
"""

from __future__ import annotations

import re
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MAIN = ROOT / "docs" / "project" / "changelog.md"
ARCHIVE_DIR = ROOT / "docs" / "project" / "archive"

SECTION_RE = re.compile(r"^## (\d{4}-\d{2}-\d{2})")
POINTER_RE = re.compile(r"^> 更早的条目见.*$", re.M)

# 主文件行数预算，与 check-doc-health.py 的 check_changelog() 保持一致
BUDGET = 250


def split(text: str) -> tuple[list[str], list[tuple[str, list[str]]], list[str]]:
    """拆成：头部 / 日期小节列表（保持原顺序）/ 尾部（从 `## 版本` 起）。"""
    lines = text.splitlines()
    starts = [i for i, line in enumerate(lines) if SECTION_RE.match(line)]
    if not starts:
        return lines, [], []

    preamble = lines[: starts[0]]
    trailer_at = next((i for i in range(starts[-1], len(lines)) if lines[i].startswith("## 版本")), len(lines))
    sections = []
    for k, start in enumerate(starts):
        end = starts[k + 1] if k + 1 < len(starts) else trailer_at
        sections.append((SECTION_RE.match(lines[start]).group(1), lines[start:end]))
    return preamble, sections, lines[trailer_at:]


def rel(path: Path) -> str:
    try:
        return str(path.relative_to(ROOT))
    except ValueError:   # 归档目录被指到仓库外（单测/临时副本）时不值得为打印崩掉
        return str(path)


def archive_header(month: str) -> list[str]:
    return [
        f"# 变更历史归档 · {month}",
        "",
        f"> `docs/project/changelog.md` 的按月归档，只搬不改。更早的月份见同目录其他文件。",
        "> 由 `scripts/archive-changelog.py` 生成，不要手工搬；要改内容直接改这里，归档是正文。",
        "",
    ]


def main() -> int:
    dry_run = "--dry-run" in sys.argv[1:]
    text = MAIN.read_text(encoding="utf-8")
    preamble, sections, trailer = split(text)
    if not sections:
        print("changelog.md 里没有日期小节，什么都没做")
        return 0

    # 以「今天」和「最新一条」里较晚的月份为准：分支上停在旧月份时不会把当月也搬走
    keep_month = max(date.today().strftime("%Y-%m"), sections[0][0][:7])
    moving = [s for s in sections if s[0][:7] < keep_month]
    keeping = [s for s in sections if s[0][:7] >= keep_month]

    # 只清掉旧的指针行和尾部空行；标题与引用块之间的空行是 markdown 语法，不能一起吃掉
    body = POINTER_RE.sub("", "\n".join(preamble)).rstrip("\n").splitlines()
    while body and not body[-1].strip():
        body.pop()

    by_month: dict[str, list[tuple[str, list[str]]]] = {}
    for item in moving:
        by_month.setdefault(item[0][:7], []).append(item)

    # 整月搬完仍超预算时继续搬当月最老的几天：保证「check-doc-health 报超长 → 跑这个脚本」一定有解
    while len(body) + 3 + sum(len(lines) for _, lines in keeping) + len(trailer) > BUDGET and len(keeping) > 1:
        oldest = keeping.pop()
        by_month.setdefault(oldest[0][:7], []).append(oldest)
        print(f"预算 {BUDGET} 行不够，额外搬走 {oldest[0]}")

    if not by_month:
        print(f"无需归档：主文件最早一条 {sections[-1][0]}，保留月份 {keep_month}")
        return 0

    for month, items in sorted(by_month.items()):
        path = ARCHIVE_DIR / f"changelog-{month}.md"
        items.sort(key=lambda item: item[0], reverse=True)   # 归档与主文件一样：新的在前
        print(f"{'[dry-run] ' if dry_run else ''}归档 {len(items)} 个小节 → {rel(path)}"
              f"（{items[0][0]} … {items[-1][0]}）")
        if dry_run:
            continue

        existing = path.read_text(encoding="utf-8") if path.exists() else ""
        known = set(SECTION_RE.findall(existing))
        fresh = [lines for day, lines in items if day not in known]
        if fresh:
            text = existing.rstrip("\n") if existing else "\n".join(archive_header(month)).rstrip("\n")
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text + "\n\n" + "\n\n".join("\n".join(f).rstrip("\n") for f in fresh) + "\n",
                            encoding="utf-8")

    if dry_run:
        return 0

    months = sorted({p.stem.removeprefix("changelog-") for p in ARCHIVE_DIR.glob("changelog-*.md")})
    pointer = "> 更早的条目见 `docs/project/archive/`：" + "、".join(f"`changelog-{m}.md`" for m in months)

    out = body + ["", pointer, ""] + [line for _, lines in keeping for line in lines] + trailer
    MAIN.write_text("\n".join(out).rstrip("\n") + "\n", encoding="utf-8")
    print(f"主文件剩 {len(keeping)} 个小节（{keeping[-1][0] if keeping else '无'} … {keeping[0][0]}），{len(out)} 行")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
