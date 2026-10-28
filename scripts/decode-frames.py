#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""解析 VigilWatt.ProtocolCapture 导出的 USB JSONL，还原厂商 HID 帧。

帧格式（墨将 E2B7 实测确认，35/35 帧校验通过）：
  [0xA5 短帧 | 0xA4 长帧] [总长度含头尾] [命令/寄存器] [载荷...] [校验]
  校验 = 前面所有字节之和 & 0xFF

只读解析，不碰设备。
用法: python decode-frames.py <capture.jsonl> [--device bus:1/device:3] [--all]
例:   python decode-frames.py captures\\mojhon-vendor-frames.jsonl
"""
import json
import sys
from collections import Counter

HEADER_SHORT = 0xA5
HEADER_LONG = 0xA4


def decode(buf: bytes):
    """把 64 字节缓冲拆成结构化帧；不是本格式则返回 None。"""
    if len(buf) < 3 or buf[0] not in (HEADER_SHORT, HEADER_LONG):
        return None
    length = buf[1]
    if not 3 <= length <= len(buf):
        return None
    body = buf[:length]
    return {
        "header": body[0],
        "length": length,
        "cmd": body[2],
        "payload": body[3:-1],
        "cks": body[-1],
        "cks_ok": (sum(body[:-1]) & 0xFF) == body[-1],
    }


def ascii_of(data: bytes) -> str:
    text = "".join(chr(b) if 0x20 <= b < 0x7F else "." for b in data)
    return text if any(0x20 <= b < 0x7F for b in data) else ""


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    path = sys.argv[1]
    device = None
    if "--device" in sys.argv:
        device = sys.argv[sys.argv.index("--device") + 1]

    frames = []
    for line in open(path, encoding="utf-8"):
        rec = json.loads(line)
        dev_id = rec.get("deviceId", "")
        if device and not dev_id.startswith(device):
            continue
        hexstr = rec.get("dataHex") or ""
        if len(hexstr) != 128:            # 只要完整 64 字节中断传输
            continue
        parsed = decode(bytes.fromhex(hexstr))
        if parsed is None:
            continue
        parsed["direction"] = "OUT" if "/endpoint:0x03" in dev_id else "IN"
        parsed["endpoint"] = dev_id.rsplit("/endpoint:", 1)[-1]
        parsed["time"] = rec["timestamp"][11:23]
        frames.append(parsed)

    if not frames:
        print("没有解析到帧。确认 JSONL 来自厂商通道（EP 0x03 / 0x83）且包含完整 64 字节传输。")
        return 1

    ok = sum(1 for f in frames if f["cks_ok"])
    print(f"=== 共 {len(frames)} 帧，校验通过 {ok} / 失败 {len(frames) - ok} ===")

    regs = Counter(f["cmd"] for f in frames)
    print("=== 出现过的寄存器 ===")
    print("  " + "  ".join(f"0x{r:02X}×{n}" for r, n in sorted(regs.items())))

    print("\n=== 时序（OUT=请求，IN=回复）===")
    pending = []
    for f in frames:
        flag = "" if f["cks_ok"] else "  !!校验失败"
        if f["direction"] == "OUT":
            pending.append(f)
            print(f"{f['time']} OUT reg=0x{f['cmd']:02X} len={f['length']:2d} "
                  f"{f['payload'].hex()}{flag}")
        else:
            # 同一寄存器最早一条未回复的请求
            idx = next((i for i, p in enumerate(pending) if p["cmd"] == f["cmd"]), None)
            if idx is not None:
                req = pending.pop(idx)
                gap = f"{f['time']}  ← {req['time']}"
            else:
                gap = f"{f['time']}  ← (无对应请求)"
            extra = ascii_of(f["payload"])
            print(f"{gap} IN  reg=0x{f['cmd']:02X} len={f['length']:2d} "
                  f"{f['payload'].hex()}{('  \"' + extra + '\"') if extra else ''}{flag}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
