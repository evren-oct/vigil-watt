# -*- coding: utf-8 -*-
"""墨将 E2B7 链路抖动测量（只读，10Hz 连读寄存器 0x04）。

目的：区分"USB 枚举层在反复插拔"和"厂商通道握手在丢包"。
只发实录里官方助手用过的 `A5 04 04 <cks>`，不写任何配置寄存器。

用法: python probe-mojhon-jitter.py [秒数] [毫秒间隔]
"""
import statistics
import sys
import time

import hid

VID = 0xE2B7
VENDOR_PAGE = 0xFFAB
FRAME_LEN = 65


def build() -> bytes:
    head = bytes([0xA5, 0x04, 0x04])
    return (b"\x00" + head + bytes([sum(head) & 0xFF])).ljust(FRAME_LEN, b"\x00")


def parse(buf):
    if len(buf) < 3 or buf[0] not in (0xA5, 0xA4):
        return None
    n = buf[1]
    if not 3 <= n <= len(buf):
        return None
    body = buf[:n]
    if body[2] != 0x04:
        return None
    if (sum(body[:-1]) & 0xFF) != body[-1]:
        return None
    return body[3:-1]


def main():
    seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 20.0
    interval = (float(sys.argv[2]) if len(sys.argv) > 2 else 100.0) / 1000.0

    path = next((d["path"] for d in hid.enumerate(VID, 0)
                 if (d.get("usage_page") or 0) == VENDOR_PAGE), None)
    if path is None:
        print("!! 找不到厂商接口（设备未接入？）")
        return 1

    dev = hid.device()
    dev.open_path(path)
    print(f"开始测量 {seconds:.0f}s，间隔 {interval * 1000:.0f}ms")
    print(f"接口: {path.decode(errors='replace') if isinstance(path, bytes) else path}\n")

    total = ok = 0
    timeline = []          # 每个采样的 (相对时刻, 成功?, 读数, 往返ms)
    latencies = []
    last_ok_ts = None
    max_gap = 0.0
    gap_at = None

    t0 = time.time()
    while time.time() - t0 < seconds:
        total += 1
        sent_at = time.time()
        try:
            dev.write(build())
        except OSError as exc:
            timeline.append((sent_at - t0, False, f"写失败: {exc}", 0))
            time.sleep(interval)
            continue

        payload = None
        deadline = sent_at + 0.4
        while time.time() < deadline:
            data = dev.read(FRAME_LEN, 60)
            if not data:
                continue
            payload = parse(bytes(data))
            if payload is not None:
                break

        elapsed = (time.time() - sent_at) * 1000
        if payload is not None:
            ok += 1
            latencies.append(elapsed)
            if last_ok_ts is not None:
                gap = time.time() - last_ok_ts
                if gap > max_gap:
                    max_gap, gap_at = gap, time.time() - t0
            last_ok_ts = time.time()
            timeline.append((sent_at - t0, True, payload.hex(), elapsed))
        else:
            timeline.append((sent_at - t0, False, "无回复", elapsed))

        time.sleep(interval)

    dev.close()

    # 打印失败点与时间线摘要
    fails = [(t, v) for t, good, v, _ in timeline if not good]
    print(f"采样 {total} 次：应答 {ok} 次，失败 {total - ok} 次"
          f"（丢包率 {(total - ok) / total * 100:.1f}%）")
    if latencies:
        print(f"往返延迟：中位 {statistics.median(latencies):.1f}ms  "
              f"最小 {min(latencies):.1f}ms  最大 {max(latencies):.1f}ms")
    if gap_at is not None:
        print(f"最长连续应答间隔：{max_gap * 1000:.0f}ms（出现在第 {gap_at:.1f}s）")
    if fails:
        print("\n失败时刻（相对秒 / 原因）：")
        for t, v in fails[:40]:
            print(f"  {t:6.2f}s  {v}")
        if len(fails) > 40:
            print(f"  ... 另有 {len(fails) - 40} 次")
    else:
        print("\n全程零失败：厂商通道握手稳定")

    # 读数变化序列（去重连续相同值）
    seq, prev = [], None
    for t, good, v, _ in timeline:
        if good and v != prev:
            seq.append((t, v))
            prev = v
    print("\n读数变化序列：")
    for t, v in seq:
        print(f"  {t:6.2f}s  {v}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
