#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""通用厂商页 HID 被动监听，零写入。

只打开可读的厂商页接口（usage page >= 0xFF00）收 input report，不发任何字节。
用法: python probe-vendor-passive.py <VID十六进制> [秒数]
例:   python probe-vendor-passive.py E2B7 45
"""
import sys
import time
import threading

import hid


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    vid = int(sys.argv[1], 16)
    seconds = int(sys.argv[2]) if len(sys.argv) > 2 else 45

    devices = [d for d in hid.enumerate(vid, 0) if d["vendor_id"] == vid]
    print(f"=== HID 枚举 VID=0x{vid:04X} 共 {len(devices)} 条 ===")
    targets = []
    for d in devices:
        page, usage = d.get("usage_page") or 0, d.get("usage") or 0
        print(f"  pid=0x{d['product_id']:04X} page=0x{page:04X} usage=0x{usage:04X} "
              f"in={d.get('input_report_length')} out={d.get('output_report_length')} "
              f"feat={d.get('feature_report_length')} prod={d.get('product_string')!r}")
        if page >= 0xFF00:
            targets.append(d)

    if not targets:
        print("!! 没有厂商页接口")
        return 1

    print(f"\n=== 被动监听 {len(targets)} 个厂商接口，{seconds} 秒，零写入 ===", flush=True)
    stop_at = time.time() + seconds
    lock, counts = threading.Lock(), {}

    def listen(dev):
        label = f"pid=0x{dev['product_id']:04X} page=0x{dev['usage_page']:04X}/0x{dev['usage']:04X}"
        try:
            h = hid.device()
            h.open_path(dev["path"])
        except OSError as e:
            print(f"  [跳过] {label} 打开失败: {e}", flush=True)
            return
        try:
            h.set_nonblocking(False)
            while time.time() < stop_at:
                try:
                    data = h.read(65, 250)
                except OSError:
                    break
                if not data:
                    continue
                hexs = " ".join(f"{b:02X}" for b in data)
                with lock:
                    counts[label] = counts.get(label, 0) + 1
                    n = counts[label]
                print(f"[{time.strftime('%H:%M:%S')}] #{n:<4} {label} len={len(data)} {hexs}", flush=True)
        finally:
            try:
                h.close()
            except Exception:
                pass

    threads = [threading.Thread(target=listen, args=(d,), daemon=True) for d in targets]
    for t in threads:
        t.start()
    for t in threads:
        t.join(timeout=seconds + 5)

    print("\n=== 汇总 ===")
    for label, n in sorted(counts.items()):
        print(f"  {label}: {n} 条")
    if not counts:
        print("  （零自发报文：电量需查询帧触发，须抓官方软件）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
