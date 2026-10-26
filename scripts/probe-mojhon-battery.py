#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""墨将（E2B7）厂商寄存器只读探针。

只重放 USBPcap 实录里官方助手用过的读请求帧 `A5 04 <reg> <cks>`，
不做命令枚举、不改字节、不写任何配置寄存器。
帧格式与出处见 docs/hardware/mojhon.md §6。

用法: python probe-mojhon-battery.py [轮数] [间隔秒]
例:   python probe-mojhon-battery.py 3 1
"""
import sys
import time

import hid

VID = 0xE2B7
VENDOR_PAGE = 0xFFAB
FRAME_LEN = 64

# 寄存器 → 说明（数值来自 2026-10-26 实测，见 docs/hardware/mojhon.md §6.7 / §6.8）
REGS = {
    0x04: "电量寄存器：payload[0]=百分比，payload[1]=充电标志",
    0x0B: "非电量（恒 0x61=97，电量变化时不变，已排除）",
    0xFA: "未知（实测回 0x00）",
}

# payload[1] 实测取值（只此两个，其他值不下结论）
CHARGING_FLAG = {
    0x00: "未插充电线（放电）",
    0x01: "正在充电",
}


def build(reg: int) -> bytes:
    """按实录格式构造读请求。

    ⚠️ 实测：hidapi 发 64 字节（帧原样）设备**不认**，必须在最前面补一个
    报文 ID `0x00` 凑成 65 字节才收得到回复。也就是说线上 OUT 传输里那
    64 字节是 HID 栈剥掉报文 ID 之后的形态。
    """
    head = bytes([0xA5, 0x04, reg])
    cks = sum(head) & 0xFF
    return (b"\x00" + head + bytes([cks])).ljust(FRAME_LEN + 1, b"\x00")


def parse(buf: bytes):
    if len(buf) < 3 or buf[0] not in (0xA5, 0xA4):
        return None
    length = buf[1]
    if not 3 <= length <= len(buf):
        return None
    body = buf[:length]
    return {
        "reg": body[2],
        "payload": body[3:-1],
        "cks_ok": (sum(body[:-1]) & 0xFF) == body[-1],
    }


def main() -> int:
    rounds = int(sys.argv[1]) if len(sys.argv) > 1 else 1
    interval = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0

    path = next((d["path"] for d in hid.enumerate(VID, 0)
                 if (d.get("usage_page") or 0) == VENDOR_PAGE), None)
    if path is None:
        print(f"!! 找不到 VID=0x{VID:04X} usage_page=0x{VENDOR_PAGE:04X} 的接口")
        return 1

    dev = hid.device()
    dev.open_path(path)
    print(f"已打开厂商接口 usage_page=0x{VENDOR_PAGE:04X}")

    for n in range(rounds):
        for reg, desc in REGS.items():
            frame = build(reg)
            try:
                sent = dev.write(frame)
            except OSError as exc:
                print(f"  写入失败 reg=0x{reg:02X}: {exc}")
                continue
            # 收 300ms 内的回复
            deadline = time.time() + 0.3
            got = False
            while time.time() < deadline and not got:
                data = dev.read(65, 100)
                if not data:
                    continue
                parsed = parse(bytes(data))
                if parsed and parsed["reg"] == reg:
                    mark = "" if parsed["cks_ok"] else "  !!校验失败"
                    payload = parsed["payload"]
                    extra = ""
                    if len(payload) == 1:
                        extra = f"  十进制={payload[0]}"
                    elif len(payload) >= 1:
                        extra = f"  首字节十进制={payload[0]}"
                    print(f"[{n + 1}] reg=0x{reg:02X} 发出 {sent}B → 收到 "
                          f"{payload.hex()}{extra}{mark}")
                    # 电量寄存器额外解出充电标志（定案依据见 docs §6.8）
                    if reg == 0x04 and len(payload) >= 2:
                        pct, flag = payload[0], payload[1]
                        state = CHARGING_FLAG.get(flag, "未实测过的值，不作判定")
                        print(f"      电量={pct}%  充电标志=0x{flag:02X}（{state}）")
                    print(f"      说明: {desc}")
                    got = True
            if not got:
                print(f"[{n + 1}] reg=0x{reg:02X} 发出 {sent}B → 无回复")
        if n + 1 < rounds:
            time.sleep(interval)

    dev.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
