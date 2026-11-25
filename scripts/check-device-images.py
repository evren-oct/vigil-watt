#!/usr/bin/env python3
"""校验内置设备产品图资产：清单、图片文件、显示名冲突与体积。"""

import argparse
import collections
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEFAULT_ROOT = REPO / "src" / "VigilWatt.App" / "Assets" / "device-images"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=str(DEFAULT_ROOT))
    args = parser.parse_args()
    root = Path(args.root)
    if not root.exists():
        print(f"missing root: {root}")
        return 1

    manifests = sorted(root.rglob("manifest.json"))
    names = collections.defaultdict(list)
    missing, models, colors = [], 0, 0
    for manifest_path in manifests:
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        except Exception as exc:
            missing.append((str(manifest_path), f"JSON: {exc}"))
            continue
        model = manifest.get("model", "?")
        models += 1
        for name in manifest.get("match", {}).get("displayNames", []):
            names[name].append(model)
        for color in manifest.get("colors", []):
            colors += 1
            for key in ("thumbnail", "image"):
                relative = color.get(key)
                if relative and not (manifest_path.parent / relative).exists():
                    missing.append((model, f"{color.get('key')}.{key}: {relative}"))

    duplicates = {name: models for name, models in names.items() if len(models) > 1}
    files = [path for path in root.rglob("*") if path.is_file()]
    size_mb = sum(path.stat().st_size for path in files) / 1024 / 1024
    print(f"models={models} colors={colors} files={len(files)} size={size_mb:.1f}MB")
    print(f"missing={len(missing)} duplicateDisplayNames={len(duplicates)}")
    for item in missing[:20]:
        print("MISS", item)
    for name, owners in list(duplicates.items())[:20]:
        print("DUP", name, owners)
    return 1 if missing or duplicates else 0


if __name__ == "__main__":
    sys.exit(main())
