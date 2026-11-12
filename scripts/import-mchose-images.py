#!/usr/bin/env python3
"""从迈从官方 cardList.json 批量导入全部键盘产品图，生成 device-images 清单。

用法（在仓库根执行）：
  python scripts/import-mchose-images.py
  python scripts/import-mchose-images.py --debug-data-dir src/VigilWatt.App/bin/x64/Debug/.../win-x64/data

脚本只读官方 CDN。默认只把缩略图写进项目 Assets（随包分发）；--with-full 时
才额外下载整机图到本机数据目录，供以后大图预览使用。
"""

import argparse
import base64
import collections
import concurrent.futures
import io
import json
import os
import re
import shutil
import sys
import tempfile
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

CARD_URL = "https://cdn.mchose.com.cn/configCenter/custom/cardList.json"
REPO = Path(__file__).resolve().parents[1]
DEFAULT_CATALOG = REPO / "src" / "VigilWatt.Core" / "Plugins" / "Mchose" / "mchose-catalog.json"
DEFAULT_ASSETS = REPO / "src" / "VigilWatt.App" / "Assets" / "device-images" / "mchose"


def safe(text: str) -> str:
    return re.sub(r"[^0-9A-Za-z._-]+", "_", text).strip("_") or "model"


def extension(url: str) -> str:
    ext = Path(url.split("?", 1)[0]).suffix.lower()
    return ext if ext in (".png", ".jpg", ".jpeg", ".webp") else ".png"


def fetch(url: str) -> bytes:
    parts = urllib.parse.urlsplit(url)
    url = urllib.parse.urlunsplit((
        parts.scheme, parts.netloc,
        urllib.parse.quote(parts.path, safe="/%"), parts.query, parts.fragment,
    ))
    request = urllib.request.Request(url, headers={"User-Agent": "VigilWatt-image-import/1.0"})
    with urllib.request.urlopen(request, timeout=45) as response:
        if response.status != 200:
            raise RuntimeError(f"HTTP {response.status}")
        return response.read()


def is_image(data: bytes) -> bool:
    return (
        data.startswith(b"\x89PNG\r\n\x1a\n")
        or data.startswith(b"\xff\xd8\xff")
        or (data.startswith(b"RIFF") and data[8:12] == b"WEBP")
    )


def load_card_list(url: str) -> list[dict]:
    payload = json.loads(fetch(url).decode("utf-8-sig"))
    archive = zipfile.ZipFile(io.BytesIO(base64.b64decode(payload["__compressBase64__"])))
    return json.loads(archive.read("data.json").decode("utf-8-sig"))["data"]


def build_groups(entries: list[dict], catalog: dict) -> dict:
    pid_model = {}
    for device in catalog.get("devices", []):
        try:
            pid_model[(int(device["vid"], 16), int(device["pid"], 16))] = device.get("model", "")
        except Exception:
            pass

    groups = collections.OrderedDict()
    for entry in entries:
        desc = entry.get("desc", "").replace(" 配置", "").strip()
        models, vendors, pids = [], set(), set()
        for identity in entry.get("identities", []):
            try:
                vid = int(identity["vendorId"], 16)
                pid = int(identity["productId"], 16)
                vendors.add(vid)
                pids.add(pid)
                if pid_model.get((vid, pid)):
                    models.append(pid_model[(vid, pid)])
            except Exception:
                pass

        primary = models[0] if models else desc
        group = groups.setdefault(primary, {
            "aliases": set(), "vendors": set(), "pids": set(), "colors": [],
        })
        group["aliases"].update((desc, primary))
        title = entry.get("homePresentation", {}).get("title")
        if title:
            group["aliases"].add(title.replace("\n", " ").strip())
        group["vendors"].update(vendors)
        group["pids"].update(pids)
        group["colors"].extend(entry.get("colorList", []))

    alias_count = collections.Counter(alias for group in groups.values() for alias in group["aliases"])
    for primary, group in groups.items():
        seen, colors = set(), []
        for color in group["colors"]:
            key = color.get("className")
            image_url = color.get("image")
            full_url = color.get("deviceImage") or image_url
            if not key or not image_url or key in seen:
                continue
            seen.add(key)
            colors.append((key, image_url, full_url))
        group["unique_colors"] = colors
        group["display_names"] = [
            name
            for alias in [primary, *sorted(group["aliases"])]
            if alias == primary or alias_count[alias] == 1
            for name in (f"迈从 {alias}", alias)
        ]
        group["display_names"] = list(dict.fromkeys(group["display_names"]))
    return groups


def write_pack(groups: dict, downloaded: dict, output: Path, include_full: bool) -> tuple[int, int]:
    models = colors_written = 0
    for primary, group in groups.items():
        folder = output / safe(primary)
        folder.mkdir(parents=True, exist_ok=True)
        colors = []
        for key, image_url, full_url in group["unique_colors"]:
            image = downloaded.get(image_url)
            full = downloaded.get(full_url) or image if include_full else None
            if image is None or (include_full and full is None):
                continue
            base = safe(key)
            thumb_name = f"{base}_thumb{extension(image_url)}"
            (folder / thumb_name).write_bytes(image)
            if include_full:
                full_name = f"{base}_full{extension(full_url)}"
                (folder / full_name).write_bytes(full)
            else:
                full_name = thumb_name
            colors.append({
                "key": key,
                "label": key.removeprefix("color-").replace("-", " "),
                "thumbnail": thumb_name,
                "image": full_name,
                "source": full_url if include_full else image_url,
            })
        if not colors:
            print(f"skip {primary}: no downloadable colors")
            continue

        match = {"displayNames": group["display_names"]}
        if len(group["vendors"]) == 1:
            match["vendorId"] = f"{next(iter(group['vendors'])):04X}"
            match["productIds"] = [f"{pid:04X}" for pid in sorted(group["pids"])]
        manifest = {
            "schemaVersion": 1,
            "brand": "MCHOSE",
            "model": primary,
            "match": match,
            "defaultColor": colors[0]["key"],
            "colors": colors,
            "source": "MCHOSE HUB cardList.json",
        }
        (folder / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
        models += 1
        colors_written += len(colors)
    return models, colors_written


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets-dir", default=str(DEFAULT_ASSETS))
    parser.add_argument("--data-dir", default=os.environ.get("VIGILWATT_DATA_DIR") or
                        str(Path(os.environ.get("LOCALAPPDATA", Path.home())) / "VigilWatt"))
    parser.add_argument("--debug-data-dir", default=None)
    parser.add_argument("--catalog", default=str(DEFAULT_CATALOG))
    parser.add_argument("--card-url", default=CARD_URL)
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--with-full", action="store_true")
    parser.add_argument("--keep-staging", action="store_true")
    args = parser.parse_args()

    entries = load_card_list(args.card_url)
    catalog = json.loads(Path(args.catalog).read_text(encoding="utf-8-sig"))
    groups = build_groups(entries, catalog)

    urls = {
        url
        for group in groups.values()
        for _, image_url, full_url in group["unique_colors"]
        for url in ((image_url, full_url) if args.with_full else (image_url,))
    }
    print(f"models={len(groups)} colors={sum(len(g['unique_colors']) for g in groups.values())} urls={len(urls)}")

    downloaded, failures = {}, []
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, args.workers)) as pool:
        futures = {pool.submit(fetch, url): url for url in urls}
        for index, future in enumerate(concurrent.futures.as_completed(futures), 1):
            url = futures[future]
            try:
                data = future.result()
                if not is_image(data):
                    raise RuntimeError("not an image")
                downloaded[url] = data
            except Exception as exc:
                failures.append((url, str(exc)))
            if index % 25 == 0:
                print(f"downloaded {index}/{len(urls)} failures={len(failures)}")

    assets_staging = Path(tempfile.mkdtemp(prefix="vigilwatt-mchose-assets-"))
    models, colors = write_pack(groups, downloaded, assets_staging, include_full=False)
    assets_target = Path(args.assets_dir)
    assets_target.mkdir(parents=True, exist_ok=True)
    shutil.copytree(assets_staging, assets_target, dirs_exist_ok=True)
    print(f"assets models={models} colors={colors} target={assets_target}")

    if args.with_full:
        full_staging = Path(tempfile.mkdtemp(prefix="vigilwatt-mchose-full-"))
        full_models, full_colors = write_pack(groups, downloaded, full_staging, include_full=True)
        target = Path(args.data_dir) / "device-images" / "mchose"
        target.mkdir(parents=True, exist_ok=True)
        shutil.copytree(full_staging, target, dirs_exist_ok=True)
        if args.debug_data_dir:
            debug_target = Path(args.debug_data_dir) / "device-images" / "mchose"
            debug_target.mkdir(parents=True, exist_ok=True)
            shutil.copytree(full_staging, debug_target, dirs_exist_ok=True)
        print(f"full models={full_models} colors={full_colors} target={target}")
        if args.keep_staging:
            print(f"staging={full_staging}")
        else:
            shutil.rmtree(full_staging, ignore_errors=True)

    if args.keep_staging:
        print(f"staging={assets_staging}")
    else:
        shutil.rmtree(assets_staging, ignore_errors=True)
    for url, reason in failures[:20]:
        print("FAIL", reason, url)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
