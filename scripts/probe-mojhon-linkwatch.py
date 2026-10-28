# -*- coding: utf-8 -*-
"""墨将 E2B7 链路联合监控（只读）：同时记录 HID 接口集合变化 + 协议握手成败。

用途：把"手柄断连重连"定位到具体层——
  * HID 接口集合变化 = USB 层在重新枚举（拔插/复位）；
  * 集合不变但握手失败 = USB 在、协议通道丢了（手柄休眠/模式切换）；
  * 两者都正常 = 现象来自上层软件（官方助手自绘状态、XInput 等）。

只发送实录里官方助手用过的 `A5 04 04 <cks>` 读电量帧，不写任何配置寄存器。

用法: python probe-mojhon-linkwatch.py [秒数]
"""
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


def snapshot():
    """返回 (厂商通道路径集合, 全部墨将 HID 路径集合)。"""
    vendor, allp = set(), set()
    for d in hid.enumerate(VID, 0):
        p = d["path"]
        allp.add(p)
        if (d.get("usage_page") or 0) == VENDOR_PAGE:
            vendor.add(p)
    return vendor, allp


def short(path):
    """把长设备路径压成 `MI_02#8&xxxx` 这种可读形式。"""
    s = path.decode(errors="replace") if isinstance(path, bytes) else path
    s = s.split("#")
    return "#".join(s[1:3]) if len(s) > 2 else s


def main():
    seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 60.0
    events = []           # (t, 类别, 文本)
    t0 = time.time()

    prev_vendor, prev_all = snapshot()
    events.append((0.0, "INIT", f"厂商通道 {len(prev_vendor)} 条，墨将 HID 共 {len(prev_all)} 条"))

    last_vendor = next(iter(prev_vendor), None)
    reads_ok = reads_fail = 0
    last_state = None

    while time.time() - t0 < seconds:
        now = time.time() - t0

        # --- 每 250ms 检查一次接口集合 ---
        vendor, allp = snapshot()
        if vendor != prev_vendor:
            added = [short(p) for p in vendor - prev_vendor]
            gone = [short(p) for p in prev_vendor - vendor]
            events.append((now, "USB",
                           f"厂商通道变化  +{added or '无'}  -{gone or '无'}"))
            prev_vendor = vendor
        if allp != prev_all:
            events.append((now, "USB", f"墨将 HID 接口数 {len(prev_all)} → {len(allp)}"))
            prev_all = allp

        # --- 每轮做一次协议读 ---
        if vendor:
            path = next(iter(vendor))
            try:
                dev = hid.device()
                dev.open_path(path)
                try:
                    dev.write(build())
                    payload = None
                    deadline = time.time() + 0.35
                    while time.time() < deadline:
                        data = dev.read(FRAME_LEN, 60)
                        if not data:
                            continue
                        payload = parse(bytes(data))
                        if payload is not None:
                            break
                finally:
                    dev.close()
                if payload is not None:
                    reads_ok += 1
                    if payload.hex() != last_state:
                        events.append((now, "READ", f"读数 {payload.hex()}"))
                        last_state = payload.hex()
                else:
                    reads_fail += 1
                    events.append((now, "READ", "无回复（USB 在、协议无应答）"))
            except OSError as exc:
                reads_fail += 1
                events.append((now, "READ", f"打开/写入失败: {exc}"))
        else:
            reads_fail += 1
            events.append((now, "READ", "厂商通道不在（USB 层已消失）"))

        time.sleep(0.25)

    print(f"=== 监控 {seconds:.0f}s 结果 ===")
    print(f"协议读：成功 {reads_ok} 次，失败 {reads_fail} 次\n")
    print("时间线（秒 / 层 / 事件）：")
    prev_txt = None
    for t, kind, txt in events:
        if txt == prev_txt and kind == "READ":
            continue
        print(f"  {t:6.2f}s  [{kind}]  {txt}")
        prev_txt = txt
    return 0


if __name__ == "__main__":
    sys.exit(main())
