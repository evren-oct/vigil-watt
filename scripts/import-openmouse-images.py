#!/usr/bin/env python3
"""从 OpenMouse 的公共图床补罗技 G 系列产品图（Options+ 不覆盖游戏线）。

用法（在仓库根执行）：
  python scripts/import-openmouse-images.py
  python scripts/import-openmouse-images.py --dry-run

图源 `https://img.openmouse.app/<file>`（Cloudflare R2，公开读）。图不在它的仓库里，
也不在 AGPL 范围内；README 自述是厂商渲染图、版权归厂商，与本项目其它产品图同一处理。

映射表是 OpenMouse `src/shared/device-images.ts` 名字正则的反推结果（2026-11-13 快照）。
它按显示名匹配、不按 VID:PID——共用接收器 PID 无法区分型号，与本项目判据一致。

只补 G / PRO 系列：MX 系列已有 Options+ 的官方图，不覆盖。
"""

import argparse
import json
import re
import sys
import urllib.request
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[1]
DEFAULT_ASSETS = REPO / "src" / "VigilWatt.App" / "Assets" / "device-images" / "logitech"
BASE_URL = "https://img.openmouse.app/"
MAX_EDGE = 300

# (文件名, 型号, 显示名变体)。变体按 DeviceImageCatalog 的归一化规则去重后写进清单：
# 空格与大小写不参与比较，所以 "G502 X Plus" 与 "G502 X PLUS" 只留一个。
DEVICES = [
    ("logitech-g502.png", "G502",
     ["G502", "G502 HERO", "G502 LIGHTSPEED", "G502 SE", "G502 Proteus Spectrum", "G502 Proteus Core"]),
    ("logitech-g502-x.png", "G502 X", ["G502 X", "G502 X LIGHTSPEED"]),
    ("logitech-g502-x-plus.png", "G502 X Plus", ["G502 X Plus"]),
    ("logitech-g703.png", "G703", ["G703", "G703 HERO", "G703 LIGHTSPEED"]),
    ("logitech-g903.png", "G903", ["G903", "G903 LIGHTSPEED"]),
    ("logitech-g-pro.png", "G PRO", ["G PRO", "G PRO HERO"]),
    ("logitech-g-pro-2.png", "G PRO 2", ["G PRO 2"]),
    ("logitech-g203.png", "G203", ["G203", "G203 LIGHTSYNC", "G203 PRODIGY", "G102", "G102 LIGHTSYNC"]),
    ("logitech-g303.png", "G303", ["G303", "G303 Daedalus Apex", "G303 Shroud Edition"]),
    ("logitech-g305.png", "G305", ["G305", "G304"]),
    ("logitech-g309.png", "G309", ["G309"]),
    ("logitech-g402.png", "G402", ["G402", "G402 Hyperion Fury"]),
    ("logitech-g403.png", "G403", ["G403", "G403 HERO"]),
    # 原版 Superlight 走 HID++ 时自报 "PRO X Wireless"；2 DEX 是另一套外壳，故意不收。
    ("logitech-pro-x-superlight-2c.png", "PRO X SUPERLIGHT",
     ["PRO X SUPERLIGHT", "PRO X SUPERLIGHT 2", "PRO X SUPERLIGHT 2 SE", "PRO X Wireless"]),
    ("logitech-pro-x2-superstrike.png", "PRO X2 SUPERSTRIKE", ["PRO X2 SUPERSTRIKE"]),
    ("logitech-pro-x3-superstrike.png", "PRO X3 SUPERSTRIKE", ["PRO X3 SUPERSTRIKE"]),
]


def safe(text: str) -> str:
    cleaned = re.sub(r"[^0-9A-Za-z._-]+", "_", text).strip("_")
    return cleaned[:60] or "model"


def normalize(text: str) -> str:
    return "".join(ch.lower() for ch in text if ch.isalnum())


def fetch(name: str) -> bytes:
    request = urllib.request.Request(BASE_URL + name, headers={"User-Agent": "VigilWatt-image-import/1.0"})
    with urllib.request.urlopen(request, timeout=45) as response:
        if response.status != 200:
            raise RuntimeError(f"HTTP {response.status}")
        return response.read()


def normalize_image(data: bytes, target: Path) -> tuple[int, int, tuple[int, int]]:
    """裁掉透明边距再等比缩放：部分图是超大画布，鼠标只占中间一小块。"""
    import io

    with Image.open(io.BytesIO(data)) as image:
        image = image.convert("RGBA")
        box = image.getbbox()  # 内容边界；全透明时返回 None
        if box:
            image = image.crop(box)
        image.thumbnail((MAX_EDGE, MAX_EDGE), Image.LANCZOS)
        image.save(target, "PNG", optimize=True)
        return len(data), target.stat().st_size, image.size


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets-dir", default=str(DEFAULT_ASSETS))
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    output = Path(args.assets_dir)
    if not args.dry_run:
        output.mkdir(parents=True, exist_ok=True)

    written = skipped = 0
    total_before = total_after = 0
    for filename, model, names in DEVICES:
        folder = output / safe(model)
        try:
            data = fetch(filename)
        except Exception as exc:
            print(f"skip {model}: {filename} -> {exc}")
            skipped += 1
            continue
        if args.dry_run:
            print(f"would write {model}: {len(data) // 1024}KB")
            continue
        folder.mkdir(parents=True, exist_ok=True)
        before, after, size = normalize_image(data, folder / filename)
        total_before += before
        total_after += after
        manifest = {
            "schemaVersion": 1,
            "brand": "Logitech",
            "model": model,
            "match": {
                "displayNames": list(dict.fromkeys(names)),
            },
            "defaultColor": "default",
            "colors": [{
                "key": "default",
                "label": "默认",
                "thumbnail": filename,
                "image": filename,
                "source": BASE_URL + filename,
            }],
            "source": "OpenMouse device artwork (img.openmouse.app)",
        }
        (folder / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"{model:22s} {size[0]:4d}x{size[1]:<4d} {after // 1024:4d}KB  {folder.name}")
        written += 1

    if not args.dry_run:
        print(f"written={written} skipped={skipped} "
              f"size={total_after / 1024 / 1024:.2f}MB (raw {total_before / 1024 / 1024:.2f}MB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
