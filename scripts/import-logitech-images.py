#!/usr/bin/env python3
"""从本机 Logitech Options+ 目录导入设备产品图，生成 device-images 清单。

用法（在仓库根执行）：
  python scripts/import-logitech-images.py                    # 全量
  python scripts/import-logitech-images.py --model "MX Master 3S"
  python scripts/import-logitech-images.py --dry-run          # 只统计不落盘

图源是 Options+ 自己的设备目录，不联网：
  <ProgramData>\\LogiOptionsPlus\\devices.json         设备名 / VID_WPID / 缩略图名
  <ProgramData>\\LogiOptionsPlus\\depots\\*\\logioptionsplus\\thumbnails\\*.png

Options+ 只管办公线；G 系列归 G HUB，本脚本不覆盖。
字段语义与匹配规则见 docs/project/device-images.md。
"""

import argparse
import collections
import json
import os
import re
import shutil
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEFAULT_ASSETS = REPO / "src" / "VigilWatt.App" / "Assets" / "device-images" / "logitech"
DEFAULT_SPEC = REPO / "src" / "VigilWatt.Core" / "Plugins" / "Logitech" / "logitech-hidpp.json"

try:
    from PIL import Image

    HAS_PIL = True
except ImportError:  # 没装 Pillow 就只复制不缩放，体积会大一些
    HAS_PIL = False


def safe(text: str) -> str:
    """目录名：只留 ASCII 字母数字与 ._-，其余折成下划线（斜杠进不了 Windows 路径）。"""
    cleaned = re.sub(r"[^0-9A-Za-z._-]+", "_", text).strip("_")
    return cleaned[:60] or "model"


def normalize(text: str) -> str:
    """与 DeviceImageCatalog.Normalize 同规则：只留字母数字转小写，空格斜杠不参与比较。"""
    return "".join(ch.lower() for ch in text if ch.isalnum())


def find_thumbnails(program_data: Path) -> Path | None:
    """Options+ 的 depot 目录名是流水号，按文件数取最大的那个。"""
    best, best_count = None, 0
    for candidate in (program_data / "depots").glob("*/logioptionsplus/thumbnails"):
        if not candidate.is_dir():
            continue
        count = sum(1 for _ in candidate.glob("*.png"))
        if count > best_count:
            best, best_count = candidate, count
    return best


def load_receiver_pids(spec_path: Path) -> set[str]:
    """接收器 PID 是所有设备共用的传输通道，写进 manifest 会把图挂到别的型号上。"""
    if not spec_path.exists():
        return set()
    spec = json.loads(spec_path.read_text(encoding="utf-8-sig"))
    return {r["pid"].upper() for r in spec.get("receivers", []) if r.get("pid")}


def build_groups(devices: list[dict], thumbs: Path, receiver_pids: set[str]) -> dict:
    groups = collections.OrderedDict()
    for entry in devices:
        name = (entry.get("displayName") or "").strip()
        key = normalize(name)
        if not key:
            continue
        files = []
        primary = Path(entry.get("thumbnail") or "").name
        if primary and (thumbs / primary).exists():
            files.append(primary)
        # 主图缺失时退到任意一张 derivative；同型号多张图只取第一张能用的（每型号一张图）
        for mode in entry.get("modes", []):
            for interface in mode.get("interfaces", []):
                for derivative in interface.get("derivatives") or []:
                    candidate = Path(derivative.get("thumbnail") or "").name
                    if candidate and (thumbs / candidate).exists() and candidate not in files:
                        files.append(candidate)
        if not files:
            continue  # 接收器、软件事件这类条目没有产品图

        # 同一型号常有多种写法（"M336 / M337" 与 "M336/M337"）；归一化后同键就合并成一条清单，
        # 否则校验脚本会报 duplicateDisplayNames。
        group = groups.setdefault(key, {"names": [], "pids": [], "files": [], "models": []})
        group["names"].append(name)
        group["files"].extend(f for f in files if f not in group["files"])
        group["models"].append(entry.get("modelId"))
        for mode in entry.get("modes", []):
            for interface in mode.get("interfaces", []):
                raw = (interface.get("id") or "").lower()
                if not raw.startswith("046d_"):
                    continue
                pid = raw[5:].upper()
                if pid not in receiver_pids:
                    group["pids"].append(pid)
    return groups


def shrink(src: Path, dst: Path, max_width: int) -> tuple[int, int]:
    """只缩不放：窄图直接复制，避免无谓重编码。"""
    original = src.stat().st_size
    if not HAS_PIL or max_width <= 0:
        shutil.copyfile(src, dst)
        return original, dst.stat().st_size
    with Image.open(src) as image:
        if image.width <= max_width:
            shutil.copyfile(src, dst)
            return original, dst.stat().st_size
        height = round(image.height * max_width / image.width)
        resized = image.resize((max_width, height), Image.LANCZOS)
        resized.save(dst, "PNG", optimize=True)
    return original, dst.stat().st_size


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--devices-json", default=None)
    parser.add_argument("--thumbnails", default=None)
    parser.add_argument("--assets-dir", default=str(DEFAULT_ASSETS))
    parser.add_argument("--spec", default=str(DEFAULT_SPEC))
    parser.add_argument("--model", action="append", default=None, help="只导这些型号（可多次）")
    parser.add_argument("--max-width", type=int, default=300, help="缩略图目标宽度，0 = 不缩放")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    program_data = Path(os.environ.get("PROGRAMDATA", r"C:\ProgramData")) / "LogiOptionsPlus"
    devices_json = Path(args.devices_json) if args.devices_json else program_data / "devices.json"
    if not devices_json.exists():
        print(f"missing {devices_json}（本机没装 Logitech Options+？）")
        return 1

    thumbs = Path(args.thumbnails) if args.thumbnails else find_thumbnails(program_data)
    if thumbs is None or not thumbs.is_dir():
        print("missing Options+ thumbnails（depots/*/logioptionsplus/thumbnails）")
        return 1

    devices = json.loads(devices_json.read_text(encoding="utf-8-sig")).get("devices", [])
    receiver_pids = load_receiver_pids(Path(args.spec))
    groups = build_groups(devices, thumbs, receiver_pids)

    if args.model:
        wanted = {normalize(m) for m in args.model}
        groups = {k: v for k, v in groups.items() if k in wanted}
        if not groups:
            print(f"no model matched: {args.model}")
            return 1

    output = Path(args.assets_dir)
    slugs = collections.defaultdict(list)
    for key, group in groups.items():
        slugs[safe(min(group["names"], key=len))].append(key)
    clashes = {slug: names for slug, names in slugs.items() if len(names) > 1}
    if clashes:
        print("slug 冲突（目录名重复），先改 safe() 或补后缀：")
        for slug, names in clashes.items():
            print(f"  {slug} <- {names}")
        return 1

    if not args.dry_run:
        if output.exists():
            shutil.rmtree(output)
        output.mkdir(parents=True)

    total_before = total_after = models = files_written = 0
    for key, group in groups.items():
        name = min(group["names"], key=len)  # 同一型号多种写法时取最短的那个
        folder = output / safe(name)
        thumbnail = group["files"][0]
        source = thumbs / thumbnail
        if args.dry_run:
            total_before += source.stat().st_size
            continue
        folder.mkdir(parents=True, exist_ok=True)
        before, after = shrink(source, folder / thumbnail, args.max_width)
        total_before += before
        total_after += after
        files_written += 1

        manifest = {
            "schemaVersion": 1,
            "brand": "Logitech",
            "model": name,
            "match": {
                "vendorId": "046D",
                "productIds": sorted(dict.fromkeys(group["pids"])),
                "displayNames": [name],
            },
            "defaultColor": "default",
            "colors": [{
                "key": "default",
                "label": "默认",
                "thumbnail": thumbnail,
                "image": thumbnail,
                "source": f"LogiOptions+ devices.json / {thumbnail}",
            }],
            "source": "LogiOptions+ devices.json",
        }
        (folder / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
        models += 1

    if args.dry_run:
        print(f"models={len(groups)} raw={total_before / 1024 / 1024:.1f}MB（dry-run，未落盘）")
        return 0

    print(f"models={models} files={files_written} "
          f"size={total_after / 1024 / 1024:.1f}MB (raw {total_before / 1024 / 1024:.1f}MB)")
    if not HAS_PIL and args.max_width > 0:
        print("Pillow 未安装，图片原样复制（pip install pillow 可缩到约 1/5）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
