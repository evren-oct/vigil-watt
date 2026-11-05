# -*- coding: utf-8 -*-
"""Gmouse 世代接收器电量验证脚本（真机对照用，会向设备写 1 条查询帧）。

用途：适配新接收器时，不用装官方软件就能确认"这条通道吃不吃 Gmouse 查询帧、电量/充电对不对"。
它做的事和 `MchosePlugin.QueryGmouse` 完全一致：对 0xFF01/0x0010 双向 65 字节接口写查询帧，
等 AA 30 应答，解析 [off+8] = 电量、[off+9] = 充电标志。

用法：
  python verify-gmouse-battery.py A8A5 2255            # 只发查询（实时读一次）
  python verify-gmouse-battery.py A8A5 2255 --watch    # 每 3 秒读一次，插拔充电线看 [9] 是否翻转
  python verify-gmouse-battery.py 3837 4245 --probe    # 先补一帧对照组：不发帧听 1 秒，再发帧

判据（见 docs/hardware/mchose.md §4）：
  - 对照组必须**零报文**，否则"应答"可能是无条件广播/回显，属假阳性；
  - 电池 1..100 合法，0 是"未就绪"占位；
  - [9] 非 0 = 充电中（蝰蛇 X11 已真机对照：拔线 0x00 / 插线 0x01）。
"""
import sys
import time

import hid

QUERY = bytes([0x00, 0x55, 0x30, 0xA5, 0x0B, 0x2E, 0x01, 0x01, 0x01]) + bytes(65 - 9)
PAGE, USAGE = 0xFF01, 0x0010


def find_channel(vid, pid):
    """按完整指纹找命令通道：usagePage/usage 命中且进出都 >= 65 字节。"""
    for d in hid.enumerate(vid, 0):
        if d["product_id"] != pid:
            continue
        if (d.get("usage_page") or 0) != PAGE or (d.get("usage") or 0) != USAGE:
            continue
        return d
    return None


def read_once(handle, wait=1.2):
    handle.write(QUERY)
    stop_at = time.time() + wait
    while time.time() < stop_at:
        data = handle.read(65, 250)
        if not data:
            continue
        off = 1 if data[0] == 0x00 and len(data) > 1 and data[1] == 0xAA else 0
        if data[off] != 0xAA or data[off + 1] != 0x30:
            print(f"  忽略非 AA 30 响应: {' '.join(f'{b:02X}' for b in data)}")
            continue
        percent, charging = data[off + 8], data[off + 9] != 0
        ok = 1 <= percent <= 100
        print(f"  {time.strftime('%H:%M:%S')} 电量={percent}%{'（占位，未就绪）' if not ok else ''} "
              f"充电={'是' if charging else '否'} 原始={' '.join(f'{b:02X}' for b in data[:12])} …")
        return percent if ok else None
    print(f"  {time.strftime('%H:%M:%S')} 无应答（设备关机/休眠，或这条通道不吃 Gmouse 帧）")
    return None


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    vid, pid = int(sys.argv[1], 16), int(sys.argv[2], 16)
    watch = "--watch" in sys.argv
    probe = "--probe" in sys.argv

    d = find_channel(vid, pid)
    if d is None:
        print(f"没有找到 0x{vid:04X}:0x{pid:04X} 的 {PAGE:#06x}/{USAGE:#06x} 接口")
        return 1
    print(f"命令通道: {d['path']}")

    h = hid.device()
    h.open_path(d["path"])
    h.set_nonblocking(False)
    try:
        if probe:
            print("[对照组] 不发帧听 1 秒（应零报文）")
            stop_at = time.time() + 1.0
            silent = True
            while time.time() < stop_at:
                if h.read(65, 250):
                    silent = False
            print(f"  对照结果: {'安静，可以信任后面的应答' if silent else '!! 有自发报文，应答可能是广播/回显'}")

        if not watch:
            read_once(h)
            return 0

        print("每 3 秒读一次，Ctrl+C 结束；插拔充电线看「充电」列是否翻转")
        while True:
            read_once(h)
            time.sleep(3)
    except KeyboardInterrupt:
        return 0
    finally:
        h.close()


if __name__ == "__main__":
    sys.exit(main())
