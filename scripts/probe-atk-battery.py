# -*- coding: utf-8 -*-
"""ATK / VXE（373B）电量通道体检探针。

回答三个问题：
1. 当前在位的设备是哪个 PID、产品串是什么（接收器 or 插线本体）？
2. 电量口（usage_page 0xFF02）的 in/out 报告长度是否仍为 17 / 17？
3. `08 04 00×14 49` 这条**已知查询帧**在这个 PID 上还回不回电量？

安全性：只发送 docs/hardware/atk.md §2 记载、且已在 F1 Pro Max / F1 V3 / 两个
接收器上实测通过的那一条只读查询帧。命令字、字节、校验全部照抄，
不做命令枚举、不扫 report ID、不改任何配置。

用法：
    python probe-atk-battery.py              # 体检 + 连发 3 次查询
    python probe-atk-battery.py --caps       # 只列接口能力，不发任何帧
    python probe-atk-battery.py --pid 1278   # 只测指定 PID
"""
import sys
import time
from ctypes import wintypes

import ctypes
import hid

VID = 0x373B

# 本机 hidapi 不返回报告长度，caps 走 hid.dll 直读（与 probe-hid-caps.py 同口径）
_hid = ctypes.WinDLL('hid.dll')
_k32 = ctypes.WinDLL('kernel32', use_last_error=True)


class _CAPS(ctypes.Structure):
    # HidP_GetCaps 会写满整个 HIDP_CAPS，字段少了会越界写栈
    _fields_ = [('Usage', wintypes.USHORT), ('UsagePage', wintypes.USHORT),
                ('In', wintypes.USHORT), ('Out', wintypes.USHORT), ('Feat', wintypes.USHORT),
                ('Reserved', wintypes.USHORT * 17),
                ('NumberLinkCollectionNodes', wintypes.USHORT),
                ('NumberInputButtonCaps', wintypes.USHORT),
                ('NumberInputValueCaps', wintypes.USHORT),
                ('NumberInputDataIndices', wintypes.USHORT),
                ('NumberOutputButtonCaps', wintypes.USHORT),
                ('NumberOutputValueCaps', wintypes.USHORT),
                ('NumberOutputDataIndices', wintypes.USHORT),
                ('NumberFeatureButtonCaps', wintypes.USHORT),
                ('NumberFeatureValueCaps', wintypes.USHORT),
                ('NumberFeatureDataIndices', wintypes.USHORT)]


_k32.CreateFileW.restype = wintypes.HANDLE
_k32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                             ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
_k32.CloseHandle.argtypes = [wintypes.HANDLE]
_hid.HidD_GetPreparsedData.argtypes = [wintypes.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
_hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
_hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.c_void_p]


def report_lengths(path: bytes):
    """按设备接口路径读 (usage_page, in, out, feature)；失败返回 None。"""
    h = _k32.CreateFileW(path.decode(), 0x80000000, 3, None, 3, 0, None)
    if h == wintypes.HANDLE(-1).value or not h:
        return None
    try:
        preparsed = ctypes.c_void_p()
        if not _hid.HidD_GetPreparsedData(wintypes.HANDLE(h), ctypes.byref(preparsed)):
            return None
        try:
            caps = _CAPS()
            if not _hid.HidP_GetCaps(preparsed, ctypes.byref(caps)):
                return None
            return caps.UsagePage, caps.In, caps.Out, caps.Feat
        finally:
            _hid.HidD_FreePreparsedData(preparsed)
    finally:
        _k32.CloseHandle(wintypes.HANDLE(h))

# docs/hardware/atk.md §2：report ID 0x08 + 命令字 0x04 + 14 个 0 + 校验
# 校验 = (0x55 − 前 16 字节之和) & 0xFF
def build_query() -> bytes:
    frame = bytearray(17)
    frame[0] = 0x08
    frame[1] = 0x04
    frame[16] = (0x55 - sum(frame[:16])) & 0xFF
    return bytes(frame)


def checksum_valid(frame: bytes) -> bool:
    if len(frame) < 17:
        return False
    return frame[16] == (0x55 - sum(frame[:16])) & 0xFF


def decode(frame: bytes):
    """返回 (电量%, 充电位, 电压mV) 或 None（不是电量帧）。

    偏移口径与 AtkCodec.TryGetOpFrame 一致：含 report ID / 被剥掉 / 前置占位三种都试，
    以「命令字 + 校验和」同时成立为准（校验和对得上的回显帧电量位为 0，靠 pct 下界挡）。
    """
    for off in (0, 1, 2):
        seg = frame[off:off + 17]
        if len(seg) < 17:
            continue
        if seg[0] != 0x08 or seg[1] != 0x04:   # report ID + 电量命令字
            continue
        if not checksum_valid(seg):
            continue
        pct, chg = seg[6], seg[7]
        mv = (seg[8] << 8) | seg[9]
        if 1 < pct <= 100:
            return pct, chg, mv
    return None


def native_set_output(path: bytes, frame: bytes) -> bool:
    """HidD_SetOutputReport 后备：atk.md §5 记载该系列设备独占 WriteFile 不可用。"""
    h = _k32.CreateFileW(path.decode(), 0x40000000, 3, None, 3, 0, None)
    if not h or h == wintypes.HANDLE(-1).value:
        return False
    try:
        buf = (ctypes.c_ubyte * len(frame))(*frame)
        return bool(_hid.HidD_SetOutputReport(wintypes.HANDLE(h), buf, len(frame)))
    finally:
        _k32.CloseHandle(wintypes.HANDLE(h))


def listen(path: bytes, seconds: float, label: str):
    """纯被动：不发任何字节，只看设备自己推不推帧（证明鼠标已上电并与该接口配对）。"""
    try:
        dev = hid.device()
        dev.open_path(path)
        dev.set_nonblocking(0)
    except Exception as e:                                            # noqa: BLE001
        print(f'    {label}: 打开失败 {e}')
        return 0
    frames = 0
    end = time.time() + seconds
    try:
        while time.time() < end:
            try:
                data = dev.read(64, 300)
            except OSError:
                break                     # 键盘/鼠标集合口不接受这种读，正常
            if data:
                frames += 1
                if frames <= 3:
                    print(f'    {label}: {hexs(bytes(data))}')
    finally:
        dev.close()
    print(f'    {label}: {seconds:.0f}s 内被动收到 {frames} 帧')
    return frames


def hexs(b: bytes) -> str:
    return ' '.join(f'{x:02X}' for x in b)


def main():
    caps_only = '--caps' in sys.argv
    only_pid = None
    if '--pid' in sys.argv:
        only_pid = int(sys.argv[sys.argv.index('--pid') + 1], 16)

    devs = [d for d in hid.enumerate(VID, 0)]
    if not devs:
        print('没有找到 VID_373B 设备（未接入？）')
        return

    query = build_query()
    by_pid = {}
    for d in devs:
        by_pid.setdefault(d['product_id'], []).append(d)

    for pid, group in sorted(by_pid.items()):
        if only_pid is not None and pid != only_pid:
            continue
        print(f'=== PID 0x{pid:04X}   ({len(group)} 个接口) ===')
        products = {d['product_string'] for d in group if d['product_string']}
        print(f'  产品串: {products or "(空)"}')

        for d in sorted(group, key=lambda x: x['path']):
            caps = report_lengths(d['path']) or ('?', '?', '?', '?')
            print(f'  [{d["interface_number"]:>2}] page=0x{(d["usage_page"] or 0):04X} '
                  f'usage=0x{(d["usage"] or 0):04X}  IN={caps[1]} OUT={caps[2]} FEAT={caps[3]}  '
                  f'{d["path"].decode(errors="replace").split("#")[1]}')

        if caps_only:
            print()
            continue

        if '--listen' in sys.argv:
            secs = 4.0
            print('  纯被动监听（零写入）：')
            for d in group:
                listen(d['path'], secs, f'page=0x{(d["usage_page"] or 0):04X}')
            print()
            continue

        targets = [d for d in group if d['usage_page'] == 0xFF02]
        if not targets:
            print('  ⚠ 没有 0xFF02 电量口\n')
            continue

        for d in targets:
            print(f'  → 对 0xFF02 口连发 3 次查询: {hexs(query)}')
            try:
                dev = hid.device()
                dev.open_path(d['path'])
                dev.set_nonblocking(0)
            except Exception as e:                                    # noqa: BLE001
                print(f'    ✗ 打开失败: {e}\n')
                continue

            answers = 0
            try:
                for i in range(3):
                    how = 'WriteFile'
                    try:
                        written = dev.write(query)
                    except Exception as e:                            # noqa: BLE001
                        print(f'    ✗ 第 {i + 1} 次写入异常: {e}')
                        break
                    if written < 0:
                        how = 'HidD_SetOutputReport'
                        if not native_set_output(d['path'], query):
                            print(f'    ✗ 第 {i + 1} 次: WriteFile 与 SetOutputReport 均被拒')
                            break
                        print(f'    · 第 {i + 1} 次: WriteFile 被拒，已改走 SetOutputReport')
                    else:
                        print(f'    · 第 {i + 1} 次: {how} 写入 {written} 字节')

                    deadline = time.time() + 1.5
                    got = None
                    while time.time() < deadline:
                        data = dev.read(17, 100)
                        if data:
                            got = bytes(data)
                            break
                    if got is None:
                        print(f'    · 第 {i + 1} 次: 1.5s 内无响应')
                    else:
                        answers += 1
                        dec = decode(got)
                        tag = (f'电量={dec[0]}% 充电位=0x{dec[1]:02X} 电压={dec[2]}mV'
                               if dec else '不是电量帧（校验或命令字不符）')
                        print(f'    ✓ 第 {i + 1} 次: {hexs(got)}   {tag}')
                    time.sleep(0.3)
            finally:
                dev.close()

            print(f'    合计 {answers}/3 次有应答')
            print()
        print()

    print(f'查询帧: {hexs(query)}   校验自检: {"OK" if checksum_valid(query) else "FAIL"}')


main()
