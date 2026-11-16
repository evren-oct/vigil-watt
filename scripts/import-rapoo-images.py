#!/usr/bin/env python3
"""导入雷柏官方 AHUB 2.0 产品图（缩略图 + 配色）。

用法（在仓库根执行）：
  python scripts/import-rapoo-images.py --dry-run
  python scripts/import-rapoo-images.py
  python scripts/import-rapoo-images.py --models "VT3,VT2S Air MAX V2"
  python scripts/import-rapoo-images.py --source-js obj/ahub/ahub-main.js

图源：官方网页驱动 AHUB 2.0 主包。设备清单是编译期常量，主包文件名带 hash，
发版会换；脚本默认用 2026-11-16 的入口，404 时用 --source-js 指新包。

只导入 `implementation != none` 的设备（插件能认领的 98 台）。底座、OHID、
telink-3228c 不产生设备条目，不占安装包体积。V700diy 的 LRV 变体在官方
manifest 里 colors 为空，也没有通用缩略图，直接跳过。

图不在仓库里是刻意取舍：缩略图随包分发，整机图不入库；版权说明见
docs/project/device-images.md §10。
"""

import argparse
import io
import json
import re
import shutil
import sys
import tempfile
import urllib.parse
import urllib.request
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[1]
DEFAULT_ASSETS = REPO / "src" / "VigilWatt.App" / "Assets" / "device-images" / "rapoo"
CATALOG = REPO / "src" / "VigilWatt.Core" / "Plugins" / "Rapoo" / "rapoo-catalog.json"
PROTOCOLS = REPO / "src" / "VigilWatt.Core" / "Plugins" / "Rapoo" / "specs" / "rapoo.protocols.json"
DEFAULT_JS_URL = "https://hid.rapoo.cn/AHUB_WEB/dist/assets/js/index-BNoOd8Jm.js"
BASE_URL = "https://hid.rapoo.cn/AHUB_WEB/dist/"
USER_AGENT = "VigilWatt-image-import/1.0"


def safe(text: str) -> str:
    cleaned = re.sub(r"[^0-9A-Za-z._-]+", "_", text).strip("_")
    return cleaned[:80] or "model"


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=60) as response:
        if response.status != 200:
            raise RuntimeError(f"HTTP {response.status}")
        return response.read()


def parse_manifests(js: str) -> list[dict]:
    """按 `hardware.productId` 把官方 manifest 摊平；工厂函数生成的设备没有 colors，自然跳过。"""
    result = []
    for match in re.finditer(r'hardware:\{vendorId:(\d+),productId:(\[[^\]]*\]|\d+)', js):
        pids = [int(x) for x in re.findall(r"\d+", match.group(2))]
        next_hardware = js.find("hardware:{", match.end())
        window = js[match.end(): next_hardware if next_hardware != -1 else match.end() + 6000]

        assets = re.search(r'ui:\{assetsDir:"([^"]+)"', window)
        if not assets:
            continue
        current = re.search(r'colors:\{series:\[(.*?)\],current:"([^"]*)"', window, re.S)
        colors = []
        if current:
            for color in re.finditer(
                r'\{name:"([^"]*)",value:(?:"[^"]*"|\[[^\]]*\]),key:"([^"]*)"\}',
                current.group(1),
            ):
                colors.append({"name": color.group(1), "key": color.group(2)})

        result.append({
            "pids": pids,
            "assets_dir": assets.group(1),
            "current": current.group(2) if current else "",
            "colors": colors,
        })
    return result


def parse_color_labels(js: str) -> dict[str, dict[str, str]]:
    """官方 i18n 表里取中文配色名；取不到就退回英文 key。"""
    labels: dict[str, dict[str, str]] = {}
    for match in re.finditer(r"(\w+):\{([^{}]*)\}", js):
        group = match.group(1)
        for entry in re.finditer(r'(\w+):"([^"]*)"', match.group(2)):
            value = entry.group(2)
            if any(ord(ch) > 127 for ch in value):
                labels.setdefault(group, {})[entry.group(1)] = value
    return labels


def label_for(name: str, key: str, labels: dict[str, dict[str, str]]) -> str:
    parts = name.split(".")
    if len(parts) >= 4:
        group, color = parts[-2], parts[-1]
        if color in labels.get(group, {}):
            return labels[group][color]
    return key.replace("_", " ")


def implementation_of(protocols: dict, protocol: str) -> str:
    spec = protocols.get(protocol, {})
    seen = set()
    while "extends" in spec and spec["extends"] not in seen:
        seen.add(spec["extends"])
        spec = protocols.get(spec["extends"], {})
    return spec.get("implementation", "data")


def save_thumb(data: bytes, target: Path, max_edge: int) -> tuple[int, int, tuple[int, int]]:
    with Image.open(io.BytesIO(data)) as image:
        image = image.convert("RGBA")
        box = image.getbbox()   # 官方图透明边距很大，裁掉后 Hero 才填得满
        if box:
            image = image.crop(box)
        image.thumbnail((max_edge, max_edge), Image.LANCZOS)
        image.save(target, "PNG", optimize=True)
        return len(data), target.stat().st_size, image.size


def load_js(args: argparse.Namespace) -> str:
    if args.source_js:
        return Path(args.source_js).read_text(encoding="utf-8", errors="replace")

    try:
        data = fetch(args.js_url)
    except Exception as exc:
        raise SystemExit(
            f"下载 AHUB 主包失败: {exc}\n"
            f"主包文件名会随发版变化；从 AHUB 页面找到新的 index-*.js 后，"
            f"用 --js-url 或 --source-js 指定。"
        )

    with tempfile.NamedTemporaryFile(suffix=".js", delete=False) as handle:
        handle.write(data)
        path = Path(handle.name)
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    finally:
        path.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets-dir", default=str(DEFAULT_ASSETS))
    parser.add_argument("--source-js", help="本地 AHUB 主包路径；不填则联网下载")
    parser.add_argument("--js-url", default=DEFAULT_JS_URL)
    parser.add_argument("--models", help="只导这些型号，逗号分隔")
    parser.add_argument("--max-edge", type=int, default=280, help="缩略图最长边，默认 280")
    parser.add_argument("--only-default-color", action="store_true")
    parser.add_argument("--clean", action="store_true", help="先清空 rapoo 资产目录")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    js = load_js(args)
    manifests = parse_manifests(js)
    labels = parse_color_labels(js)
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    protocols = json.loads(PROTOCOLS.read_text(encoding="utf-8"))["protocols"]

    by_pid: dict[int, dict] = {}
    for manifest in manifests:
        for pid in manifest["pids"]:
            existing = by_pid.get(pid)
            if existing is None or (not existing["colors"] and manifest["colors"]):
                by_pid[pid] = manifest

    wanted = {x.strip().lower() for x in args.models.split(",")} if args.models else None
    targets: dict[str, tuple[dict, dict]] = {}
    skipped = []
    seen_models = set()
    for device in catalog["devices"]:
        model = device["model"]
        if wanted and model.lower() not in wanted:
            continue
        if model in seen_models:
            continue
        if implementation_of(protocols, device["protocol"]) == "none":
            continue
        seen_models.add(model)

        pid = int(device["pid"], 16)
        manifest = by_pid.get(pid)
        if manifest is None:
            skipped.append((model, "官方 manifest 未找到"))
            continue
        if not manifest["colors"]:
            skipped.append((model, "官方没有配色/缩略图"))
            continue
        targets[model] = (device, manifest)

    if args.dry_run:
        colors = sum(len(m["colors"]) for _, m in targets.values())
        print(f"would import {len(targets)} models / {colors} colors")
        for model, reason in skipped:
            print(f"skip {model}: {reason}")
        return 0

    output = Path(args.assets_dir)
    if args.clean and output.exists():
        assets_root = (REPO / "src" / "VigilWatt.App" / "Assets" / "device-images").resolve()
        resolved = output.resolve()
        if resolved == assets_root or not resolved.is_relative_to(assets_root):
            raise SystemExit(f"--clean 只允许清空 device-images 下的品牌子目录，拒绝: {resolved}")
        shutil.rmtree(output)
    output.mkdir(parents=True, exist_ok=True)

    models_written = colors_written = files_written = 0
    bytes_before = bytes_after = 0
    for model, (device, manifest) in targets.items():
        colors = manifest["colors"]
        default_key = manifest["current"] if any(c["key"] == manifest["current"] for c in colors) else colors[0]["key"]
        if args.only_default_color:
            colors = [c for c in colors if c["key"] == default_key]

        model_dir = output / safe(model)
        if model_dir.exists():
            shutil.rmtree(model_dir)
        model_dir.mkdir(parents=True)

        written = []
        for color in colors:
            key = color["key"]
            url = BASE_URL + urllib.parse.quote(f"{manifest['assets_dir']}/thumb_{key}.png", safe="/")
            try:
                data = fetch(url)
            except Exception as exc:
                print(f"  skip {model}/{key}: {exc}")
                continue
            if not data.startswith(b"\x89PNG\r\n\x1a\n"):
                print(f"  skip {model}/{key}: 不是 PNG")
                continue

            filename = f"{safe(key)}_thumb.png"
            before, after, size = save_thumb(data, model_dir / filename, args.max_edge)
            bytes_before += before
            bytes_after += after
            files_written += 1
            written.append({
                "key": key,
                "label": label_for(color["name"], key, labels),
                "thumbnail": filename,
                "image": filename,
                "source": url,
            })
            print(f"  {model}/{key}: {size[0]}x{size[1]}, {before // 1024}KB -> {after // 1024}KB")

        if not written:
            shutil.rmtree(model_dir)
            skipped.append((model, "所有配色都下载失败"))
            continue

        pids = [device["pid"]]
        if device.get("pairedPid"):
            pids.append(device["pairedPid"])
        if len(pids) == 2:
            pair = [device]
            pair.append(next(d for d in catalog["devices"]
                             if d["vid"] == device["vid"] and d["pid"] == device["pairedPid"]))
            wired = next(d for d in pair if d.get("wired"))
            wireless = next(d for d in pair if d.get("wired") is False)
            pids = [wired["pid"], wireless["pid"]]

        manifest_json = {
            "schemaVersion": 1,
            "brand": "RAPOO",
            "model": model,
            "match": {
                "vendorId": device["vid"][2:].upper(),
                "productIds": [pid[2:].upper() for pid in pids],
                "displayNames": [f"雷柏 {model}", model],
            },
            "defaultColor": default_key if any(c["key"] == default_key for c in written) else written[0]["key"],
            "colors": written,
            "source": "RAPOO AHUB 2.0 (hid.rapoo.cn/AHUB_WEB/dist)",
            "capturedAt": "2026-11-16",
            "licenseNote": "厂商产品图，版权归厂商；当前按个人自用处理，缩略图随包分发。",
        }
        (model_dir / "manifest.json").write_text(
            json.dumps(manifest_json, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        models_written += 1
        colors_written += len(written)

    print(
        f"models={models_written} colors={colors_written} files={files_written} "
        f"size={bytes_after / 1024 / 1024:.1f}MB (raw {bytes_before / 1024 / 1024:.1f}MB)"
    )
    for model, reason in skipped:
        print(f"skip {model}: {reason}")
    return 0 if models_written else 1


if __name__ == "__main__":
    sys.exit(main())
