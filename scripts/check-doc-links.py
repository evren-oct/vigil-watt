#!/usr/bin/env python3
"""文档链接校验：Markdown 相对链接、代码里的 docs/ 路径、§小节锚点。

Markdown 坏链不报错，C# 注释里的死路径也不报错——重构目录前先跑本脚本拿基线，
改完再跑一次对比。退出码 1 表示有坏链。

用法：
    python scripts/check-doc-links.py          # 校验，坏链时退出码 1
    python scripts/check-doc-links.py -v       # 同时列出通过项统计
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SKIP_DIRS = {"bin", "obj", ".git", "publish", "dist", "node_modules", ".workbuddy", ".workbuddy-ai"}
SCAN_SUFFIXES = {".md", ".cs", ".py", ".ps1"}

# ](target) —— 只取不含协议的相对目标
MD_LINK = re.compile(r"\]\(([^)\s]+)\)")
# 正文/注释里的 docs/xxx.md 路径引用
DOC_PATH = re.compile(r"(?<![\w/.-])(docs/[A-Za-z0-9_./-]+\.md)")
# 紧跟路径或链接的 §2.4 小节引用
SECTION_REF = re.compile(r"§(\d+(?:\.\d+)*)")
# 标题里的编号：## 2.4 xxx / ### 2.4.1 xxx
HEADING_NUM = re.compile(r"^#{1,6}\s+(\d+(?:\.\d+)*)")

# 文档里用来说明"此处应写路径"的示例，不是真链接（AGENTS.md / comment-style.md 等）
PLACEHOLDER_TARGETS = {"docs/xxx.md", "docs/x.md"}

# 指向外部仓库自身文档的路径，不是本仓库文件
ALLOWED_EXTERNAL = {
    ("docs/hardware/brands/betop.md", "docs/protocol.md"),     # oitsukiii/betop-battery 的 docs/protocol.md
    ("docs/hardware/sources.md", "docs/protocol.md"),   # 同上
}

# 冻结的历史归档：文件存在性照查（搬家要能发现），小节号不查——旧编号已随协议文档拆分消失，
# 改它等于篡改历史记录。
NO_SECTION_CHECK = ("docs/reviews/archive/",)


def iter_files() -> list[Path]:
    out: list[Path] = []
    for p in ROOT.rglob("*"):
        if p.is_dir() or p.suffix not in SCAN_SUFFIXES:
            continue
        if any(part in SKIP_DIRS for part in p.parts):
            continue
        out.append(p)
    return out


_heading_cache: dict[Path, set[str]] = {}


def section_numbers(md: Path) -> set[str]:
    if md not in _heading_cache:
        nums: set[str] = set()
        for line in md.read_text(encoding="utf-8", errors="replace").splitlines():
            m = HEADING_NUM.match(line)
            if m:
                nums.add(m.group(1))
        _heading_cache[md] = nums
    return _heading_cache[md]


def check_tables(md: Path, lines: list[str]) -> list[str]:
    """表格完整性：被空行截断的残段、列数与表头不符。

    空行会终止 Markdown 表格，后续行没有表头就渲染成普通段落——不报错、只在页面里看得出来。
    """
    problems: list[str] = []
    sep = re.compile(r"^\|[\s:|-]+\|$")
    # 围栏代码块里的 `|| a.includes(...)` 会被误当成表格行，先剥掉代码块
    in_code = False
    body: list[str] = []
    for line in lines:
        if line.lstrip().startswith("```"):
            in_code = not in_code
            body.append("")
            continue
        body.append("" if in_code else line)
    lines = body

    i, n = 0, len(lines)
    while i < n:
        if not lines[i].strip().startswith("|"):
            i += 1
            continue
        start = i
        block: list[str] = []
        while i < n and lines[i].strip().startswith("|"):
            block.append(lines[i])
            i += 1

        def cells(row: str) -> int:
            s = row.strip()
            if s.startswith("|"):
                s = s[1:]
            if s.endswith("|") and not s.endswith("\\|"):
                s = s[:-1]
            # `\|` 是单元格内的转义管道，不算列分隔
            return len(s.replace("\\|", "\0").split("|"))

        if len(block) < 2 or not sep.match(block[1].strip()) or "-" not in block[1]:
            problems.append(
                f"TABLE    {md.relative_to(ROOT)}:{start + 1}  表格缺表头/分隔行（多被空行截断），不会渲染成表"
            )
            continue
        head_cols = cells(block[0])
        sep_cols = cells(block[1])
        if sep_cols != head_cols:
            problems.append(
                f"TABLE    {md.relative_to(ROOT)}:{start + 2}  分隔行 {sep_cols} 列 != 表头 {head_cols} 列"
                "（列数以分隔行为准，多出的列会被吞掉）"
            )
        for offset, row in enumerate(block[2:], start=2):
            if cells(row) != head_cols:
                problems.append(
                    f"TABLE    {md.relative_to(ROOT)}:{start + offset + 1}  列数 {cells(row)} != 表头 {head_cols}"
                )
    return problems


def main() -> int:
    verbose = "-v" in sys.argv
    broken: list[str] = []
    bad_sections: list[str] = []
    bad_tables: list[str] = []
    checked_links = 0
    checked_sections = 0

    for src in iter_files():
        if src.resolve() == Path(__file__).resolve():
            continue  # 本脚本的白名单里就写着这些路径字面量，扫自己会自证有罪
        try:
            text = src.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        lines = text.splitlines()

        if src.suffix == ".md":
            bad_tables.extend(check_tables(src, lines))

        for lineno, line in enumerate(lines, 1):
            # --- A. Markdown 相对链接 ---
            for m in MD_LINK.finditer(line):
                raw = m.group(1)
                if raw.startswith(("http://", "https://", "mailto:", "#")):
                    continue
                target = raw.split("#", 1)[0]
                if not target.endswith(".md"):
                    continue
                checked_links += 1
                resolved = (src.parent / target).resolve()
                if not resolved.exists():
                    broken.append(f"BROKEN   {src.relative_to(ROOT)}:{lineno}  -> {raw}")

            # --- B. 正文里的 docs/ 路径 ---
            for m in DOC_PATH.finditer(line):
                rel = m.group(1)
                if rel in PLACEHOLDER_TARGETS:
                    continue
                if (src.relative_to(ROOT).as_posix(), rel) in ALLOWED_EXTERNAL:
                    continue
                resolved = (ROOT / rel).resolve()
                checked_links += 1
                if not resolved.exists():
                    broken.append(f"BROKEN   {src.relative_to(ROOT)}:{lineno}  -> {rel}")
                    continue
                # --- C. 同一行紧跟的 §小节引用 ---
                if any(src.relative_to(ROOT).as_posix().startswith(p) for p in NO_SECTION_CHECK):
                    continue
                tail = line[m.end():]
                sm = SECTION_REF.match(tail.strip()) or SECTION_REF.search(tail[:8])
                if sm:
                    checked_sections += 1
                    num = sm.group(1)
                    if num not in section_numbers(resolved):
                        bad_sections.append(
                            f"SECTION  {src.relative_to(ROOT)}:{lineno}  -> {rel} §{num}（目标无此小节）"
                        )

    for item in broken:
        print(item)
    for item in bad_sections:
        print(item)
    for item in bad_tables:
        print(item)

    print()
    print(f"检查 {len(iter_files())} 个文件，{checked_links} 处路径引用，{checked_sections} 处小节锚点")
    print(f"坏链 {len(broken)} 处，失效小节 {len(bad_sections)} 处，表格问题 {len(bad_tables)} 处")
    if verbose and not broken and not bad_sections and not bad_tables:
        print("全部通过")
    return 1 if (broken or bad_sections or bad_tables) else 0


if __name__ == "__main__":
    sys.exit(main())
