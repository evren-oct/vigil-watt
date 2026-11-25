#!/usr/bin/env python3
"""从本机 BetopGame asar 导入北通产品缩略图。

官方客户端把型号卡片图放在 `/third/clientv2/img/handle_*.png`，老型号在
`/images/index/*.png`。脚本按型号表抽取、裁透明边、缩到 300px，写
`Assets/device-images/betop/<型号>/manifest.json`。

默认只读本机安装目录，不联网；`--asar` 可指向另一版资源包。
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import struct
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:  # pragma: no cover - 本机装了 Pillow；缺了给清晰提示
    Image = None

DEFAULT_ASAR = r"E:\APP Data\betopgame\3aiJsbhs\resources\app\20261111.asar"
DEFAULT_CATALOG = "src/VigilWatt.Core/Plugins/Betop/betop-catalog.json"
DEFAULT_OUT = "src/VigilWatt.App/Assets/device-images/betop"
SOURCE_LABEL = "BetopGame 2.6.2 (20261111.asar)"

# model 是目录/清单主名；models 是目录 JSON 里的 model 字段（可能多条）。
# images: (颜色 key, 显示标签, asar 路径)。
MODELS = [
    {
        "dir": "Kunpeng20White",
        "model": "鲲鹏20 绝尘白",
        "models": ["鲲鹏20 绝尘白"],
        "images": [("color-standard", "standard", "/third/clientv2/img/handle_kp20_img.png")],
    },
    {
        "dir": "Kunpeng20Ctrl",
        "model": "鲲鹏20智控游戏手柄",
        "models": ["鲲鹏20智控游戏手柄"],
        "images": [("color-standard", "standard", "/third/clientv2/img/handle_kp20_img.png")],
    },
    {
        "dir": "Kunpeng20Elite",
        "model": "鲲鹏20精英版-丁未",
        "models": ["鲲鹏20精英版-丁未"],
        "images": [("color-black", "black", "/third/clientv2/img/handle_kp20eb_img.png")],
    },
    {
        "dir": "Kunpeng20ZhanShuang",
        "model": "鲲鹏20战双帕弥什联名款",
        "models": ["鲲鹏20战双帕弥什联名款"],
        "images": [("color-camo", "camo", "/third/clientv2/img/handle_kp20zhs_img.png")],
    },
    {
        "dir": "Kunpeng40Mingmo",
        "model": "鲲鹏40 明末联动款",
        "models": ["鲲鹏40 明末联动款"],
        "images": [
            ("color-white-purple", "white-purple", "/third/clientv2/img/handle_kp40awakening_img.png"),
            ("color-blue-white", "blue-white", "/third/clientv2/img/handle_kp40bawakening_img.png"),
        ],
    },
    {
        "dir": "Kunpeng40Yukai",
        "model": "鲲鹏40 御铠白",
        "models": ["鲲鹏40 御铠白"],
        "images": [("color-white-red", "white-red", "/third/clientv2/img/handle_kp40ymh_img.png")],
    },
    {
        "dir": "Kunpeng40Fengchi",
        "model": "鲲鹏40智控游戏手柄 风驰白",
        "models": ["鲲鹏40智控游戏手柄 风驰白"],
        "images": [("color-white-gold", "white-gold", "/third/clientv2/img/handle_kp40fcb_img.png")],
    },
    {
        "dir": "Kunpeng40Dianche",
        "model": "鲲鹏40 电掣黑",
        "models": ["鲲鹏40 电掣黑"],
        "images": [("color-black", "black", "/third/clientv2/img/handle_kp40dch_img.png")],
    },
    {
        "dir": "Kunpeng40Elite",
        "model": "鲲鹏40 精英版",
        "models": ["鲲鹏40 精英版"],
        "images": [("color-white-black", "white-black", "/third/clientv2/img/handle_kp40e_img.png")],
    },
    {
        "dir": "Kunpeng40Mingchao",
        "model": "鲲鹏40鸣潮联名款",
        "models": ["鲲鹏40鸣潮联名款"],
        "images": [
            ("color-blue-white", "blue-white", "/third/clientv2/img/handle_kp40mbkt_img.png"),
            ("color-red-white", "red-white", "/third/clientv2/img/handle_kp40mhfl_img.png"),
        ],
    },
    {
        "dir": "Kunpeng50Yunyi",
        "model": "鲲鹏50 云翼白",
        "models": ["鲲鹏50 云翼白"],
        "images": [("color-white", "white", "/third/clientv2/img/handle_kp50B_img.png")],
    },
    {
        "dir": "Kunpeng50Xingqiong",
        "model": "鲲鹏50 星穹紫",
        "models": ["鲲鹏50 星穹紫"],
        "images": [("color-purple", "purple", "/third/clientv2/img/handle_kp50C_img.png")],
    },
    {
        "dir": "Kunpeng50Jiexianji",
        "model": "鲲鹏50智控游戏手柄 解限机联名款",
        "models": ["鲲鹏50智控游戏手柄 解限机联名款"],
        "images": [
            ("color-white-black", "white-black", "/third/clientv2/img/handle_kp50jxjb_img.png"),
            ("color-pink-black", "pink-black", "/third/clientv2/img/handle_kp50jxjf_img.png"),
        ],
    },
    {
        "dir": "Kunpeng70AI",
        "model": "鲲鹏70 AI旗舰",
        "models": ["鲲鹏70 AI旗舰"],
        "images": [("color-white", "white", "/third/clientv2/img/handle_kp70_img.png")],
    },
    {
        "dir": "Kunpeng70B",
        "model": "鲲鹏70AI智控旗舰手柄",
        "models": ["鲲鹏70AI智控旗舰手柄"],
        "images": [("color-colorful", "colorful", "/third/clientv2/img/handle_kp70bm_img.png")],
    },
    {
        "dir": "Kunpeng70C6D",
        "model": "鲲鹏70 6D升级款",
        "models": ["鲲鹏70 6D升级款"],
        "images": [("color-white-black", "white-black", "/third/clientv2/img/handle_kp70c_img.png")],
    },
    {
        "dir": "Kunpeng70Wuma",
        "model": "鲲鹏70 午马限定款",
        "models": ["鲲鹏70 午马限定款"],
        "images": [("color-red", "red", "/third/clientv2/img/handle_kp70a1_img.png")],
    },
    {
        "dir": "Kunpeng70Yihuan",
        "model": "鲲鹏70 异环联名款",
        "models": ["鲲鹏70 异环联名款"],
        "images": [
            ("color-red", "red", "/third/clientv2/img/handle_kp70cahcj_img.png"),
            ("color-orange", "orange", "/third/clientv2/img/handle_kp70cbhhd_img.png"),
            ("color-pink", "pink", "/third/clientv2/img/handle_kp70cnhlh_img.png"),
            ("color-black", "black", "/third/clientv2/img/handle_kp70cxhhd_img.png"),
        ],
    },
    {
        "dir": "ZeusPro",
        "model": "宙斯 Pro",
        "models": ["宙斯 Pro"],
        "images": [
            ("color-silver", "silver", "/images/index/t6pro_0.png"),
            ("color-blue", "blue", "/images/index/t6pro_1.png"),
            ("color-orange", "orange", "/images/index/t6pro_2.png"),
            ("color-purple", "purple", "/images/index/t6pro_3.png"),
            ("color-lavender", "lavender", "/images/index/t6pro_4.png"),
        ],
    },
    {
        "dir": "Zeus2",
        "model": "宙斯2",
        "models": ["宙斯2"],
        "aliases": ["T9"],
        "images": [
            ("color-standard", "standard", "/images/index/t9.png"),
            ("color-black-white", "black-white", "/third/clientv2/img/handle_zs2b_img.png"),
            ("color-black-gold", "black-gold", "/third/clientv2/img/handle_zs2h_img.png"),
        ],
    },
    {
        "dir": "YongJieWuJian",
        "model": "永劫无间",
        "models": ["永劫无间"],
        "images": [
            ("color-red-black", "red-black", "/images/index/t9_7.png"),
            ("color-white-red", "white-red", "/images/index/t9_8.png"),
        ],
    },
]


class Asar:
    def __init__(self, path: str):
        self.path = path
        with open(path, "rb") as f:
            _, header_pickle, _, header_len = struct.unpack("<4I", f.read(16))
            self.header = json.loads(f.read(header_len).decode("utf-8"))
            self.data_offset = 8 + header_pickle

    def read(self, asset_path: str) -> bytes:
        node = self.header
        for part in asset_path.strip("/").split("/"):
            node = node["files"][part]
        with open(self.path, "rb") as f:
            f.seek(self.data_offset + int(node["offset"]))
            return f.read(int(node["size"]))


def catalog_pids(catalog_path: str) -> dict[str, list[str]]:
    with open(catalog_path, encoding="utf-8") as f:
        data = json.load(f)
    result: dict[str, list[str]] = {}
    for device in data["devices"]:
        if device.get("vid", "").lower() != "0x20bc":
            continue
        result.setdefault(device.get("model", ""), []).append(device["pid"])
    return result


def save_thumb(data: bytes, dest: Path, max_edge: int) -> None:
    if Image is None:
        dest.write_bytes(data)
        return
    tmp = dest.with_suffix(".src.png")
    tmp.write_bytes(data)
    try:
        im = Image.open(tmp).convert("RGBA")
        bbox = im.getbbox()
        if bbox:
            im = im.crop(bbox)
        im.thumbnail((max_edge, max_edge), Image.LANCZOS)
        im.save(dest, "PNG", optimize=True)
    finally:
        tmp.unlink(missing_ok=True)


def build_manifest(entry: dict, pids: list[str], out_dir: Path, asar: Asar, max_edge: int) -> dict:
    colors = []
    sources = []
    for key, label, asset in entry["images"]:
        file_name = f"{key}_thumb.png"
        save_thumb(asar.read(asset), out_dir / file_name, max_edge)
        colors.append({
            "key": key,
            "label": label,
            "thumbnail": file_name,
            "image": file_name,
            "source": f"{SOURCE_LABEL} {asset}",
        })
        sources.append(asset)

    display_names = []
    for model in entry["models"]:
        display_names.extend([f"北通 {model}", model])
    display_names.extend(entry.get("aliases", []))

    manifest = {
        "schemaVersion": 1,
        "brand": "BETOP",
        "model": entry["model"],
        "match": {
            "vendorId": "20BC",
            "productIds": pids,
            "displayNames": list(dict.fromkeys(display_names)),
        },
        "defaultColor": colors[0]["key"],
        "colors": colors,
        "source": "; ".join(f"{SOURCE_LABEL} {s}" for s in sources),
        "capturedAt": "2026-11-18",
        "licenseNote": "厂商产品图，版权归厂商；当前按个人自用处理，缩略图随包分发。",
    }
    (out_dir / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--asar", default=DEFAULT_ASAR)
    parser.add_argument("--catalog", default=DEFAULT_CATALOG)
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("--max-edge", type=int, default=300)
    parser.add_argument("--clean", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--only-model", default=None)
    args = parser.parse_args()

    if not os.path.isfile(args.asar):
        print(f"找不到 asar: {args.asar}", file=sys.stderr)
        return 1
    if Image is None:
        print("警告: 未安装 Pillow，缩略图会原样复制", file=sys.stderr)

    pids_by_model = catalog_pids(args.catalog)
    asar = Asar(args.asar)
    out_root = Path(args.out)
    if args.clean and not args.dry_run:
        shutil.rmtree(out_root, ignore_errors=True)

    models = 0
    colors = 0
    files = 0
    for entry in MODELS:
        if args.only_model and entry["model"] != args.only_model:
            continue
        pids: list[str] = []
        for model in entry["models"]:
            pids.extend(pids_by_model.get(model, []))
        pids = list(dict.fromkeys(pids))
        pids = [pid[2:] if pid.lower().startswith("0x") else pid for pid in pids]
        if not pids:
            print(f"跳过 {entry['model']}: 目录里没有 20BC PID", file=sys.stderr)
            continue
        if args.dry_run:
            print(f"{entry['model']}: {len(pids)} PID / {len(entry['images'])} 色")
            models += 1
            colors += len(entry["images"])
            continue
        out_dir = out_root / entry["dir"]
        out_dir.mkdir(parents=True, exist_ok=True)
        build_manifest(entry, pids, out_dir, asar, args.max_edge)
        models += 1
        colors += len(entry["images"])
        files += 1 + len(entry["images"])

    print(f"models={models} colors={colors} files={files} out={out_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
