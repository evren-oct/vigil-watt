# -*- coding: utf-8 -*-
"""墨将（E2B7）有线/接收器 双形态体检探针（只读）。

回答三个问题：
1. 有线直连时设备是否还是 `E2B7:122C`？还是换成了另一个 PID？
2. 厂商通道（usage_page >= 0xFF00 且双向报文长度足够）是否仍然存在？
3. 同一条 `A5 04 <reg> <cks>` 读请求在有线通道上还能不能读到电量 / 型号标识？

安全性：只发送 USBPcap 实录中官方助手用过的读请求（reg=0x04 电量、reg=0xE2 型号），
寄存器号、字节、校验全部照抄 docs/hardware/mojhon.md §6，不做任何写配置、
不做命令枚举、不改字节。

用法：
    python probe-mojhon-wired.py            # 体检 + 读电量 + 读型号
    python probe-mojhon-wired.py --caps     # 只列接口能力，不发任何帧

输出按 PID 分组，便于与 2.4G 接收器（122C）对照。
"""
import ctypes
import sys
import time
from ctypes import wintypes

import hid

VID = 0xE2B7
FRAME_LEN = 64  # 帧长（协议层），实际写入长度由接口 caps 的 OUT 长度决定

hid_dll = ctypes.WinDLL('hid.dll')
setupapi = ctypes.WinDLL('setupapi.dll')
kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x1
FILE_SHARE_WRITE = 0x2
OPEN_EXISTING = 3
DIGCF_PRESENT = 0x2
DIGCF_DEVICEINTERFACE = 0x10
IOCTL_HID_GET_REPORT_DESCRIPTOR = 0x000B0003


class GUID(ctypes.Structure):
    _fields_ = [('Data1', wintypes.DWORD), ('Data2', wintypes.WORD),
                ('Data3', wintypes.WORD), ('Data4', ctypes.c_ubyte * 8)]


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [('cbSize', wintypes.DWORD), ('InterfaceClassGuid', GUID),
                ('Flags', wintypes.DWORD), ('Reserved', ctypes.POINTER(wintypes.ULONG))]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [('Usage', wintypes.USHORT), ('UsagePage', wintypes.USHORT),
                ('InputReportByteLength', wintypes.USHORT),
                ('OutputReportByteLength', wintypes.USHORT),
                ('FeatureReportByteLength', wintypes.USHORT),
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


setupapi.SetupDiGetClassDevsW.restype = ctypes.c_void_p
setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p,
                                          ctypes.c_void_p, wintypes.DWORD]
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                                 ctypes.c_void_p, wintypes.DWORD,
                                                 ctypes.c_void_p]
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [ctypes.c_void_p]
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                                      ctypes.c_void_p, wintypes.DWORD,
                                                      ctypes.c_void_p, ctypes.c_void_p]
hid_dll.HidD_GetPreparsedData.argtypes = [wintypes.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
hid_dll.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
hid_dll.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
kernel32.CreateFileW.restype = wintypes.HANDLE
kernel32.CreateFileW.argtypes = [ctypes.c_wchar_p, wintypes.DWORD, wintypes.DWORD,
                                 ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD,
                                 ctypes.c_void_p]
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.DeviceIoControl.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.c_void_p,
                                     wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD,
                                     ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]

# payload[1] 实测取值（docs/hardware/mojhon.md §6.8，仅有这两个）
CHARGING_FLAG = {0x00: "未插充电线（放电）", 0x01: "正在充电"}


def hid_interface_paths():
    guid = GUID(0x4D1E55B2, 0xF16F, 0x11CF,
                (ctypes.c_ubyte * 8)(0x88, 0xCB, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30))
    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None,
                                         DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if not hdev or hdev == 0xFFFFFFFFFFFFFFFF:
        raise ctypes.WinError(ctypes.get_last_error())
    did = SP_DEVICE_INTERFACE_DATA()
    did.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
    out, i = [], 0
    while setupapi.SetupDiEnumDeviceInterfaces(hdev, None, ctypes.byref(guid), i, ctypes.byref(did)):
        need = wintypes.DWORD(0)
        setupapi.SetupDiGetDeviceInterfaceDetailW(hdev, ctypes.byref(did), None, 0,
                                                  ctypes.byref(need), None)
        buf = ctypes.create_string_buffer(need.value)
        ctypes.cast(buf, ctypes.POINTER(wintypes.DWORD))[0] = ctypes.sizeof(wintypes.DWORD) * 2
        if setupapi.SetupDiGetDeviceInterfaceDetailW(hdev, ctypes.byref(did), buf, need.value,
                                                    ctypes.byref(need), None):
            out.append(ctypes.wstring_at(ctypes.addressof(buf) + 4))
        i += 1
    setupapi.SetupDiDestroyDeviceInfoList(hdev)
    return out


def read_caps(path):
    """返回 (usage_page, usage, in_len, out_len, feat_len) 或 None。"""
    h = kernel32.CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                             FILE_SHARE_READ | FILE_SHARE_WRITE, None,
                             OPEN_EXISTING, 0, None)
    if h == wintypes.HANDLE(-1).value:
        return None
    try:
        preparsed = ctypes.c_void_p()
        if not hid_dll.HidD_GetPreparsedData(h, ctypes.byref(preparsed)):
            return None
        try:
            caps = HIDP_CAPS()
            if not hid_dll.HidP_GetCaps(preparsed, ctypes.byref(caps)):
                return None
            return (caps.UsagePage, caps.Usage, caps.InputReportByteLength,
                    caps.OutputReportByteLength, caps.FeatureReportByteLength)
        finally:
            hid_dll.HidD_FreePreparsedData(preparsed)
    finally:
        kernel32.CloseHandle(h)


def read_descriptor(path):
    """读 HID 报告描述符原文（IOCTL_HID_GET_REPORT_DESCRIPTOR），失败返回 None。"""
    h = kernel32.CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                             FILE_SHARE_READ | FILE_SHARE_WRITE, None,
                             OPEN_EXISTING, 0, None)
    if h == wintypes.HANDLE(-1).value:
        return None
    try:
        buf = ctypes.create_string_buffer(4096)
        returned = wintypes.DWORD(0)
        ok = kernel32.DeviceIoControl(h, IOCTL_HID_GET_REPORT_DESCRIPTOR, None, 0,
                                      buf, 4096, ctypes.byref(returned), None)
        return buf.raw[:returned.value] if ok else None
    finally:
        kernel32.CloseHandle(h)


def build(reg, write_len):
    """按实录格式构造读请求并补齐到接口要求的写入长度。

    ⚠️ 见 docs/hardware/mojhon.md §6.6：写入缓冲必须比 64 字节帧多 1 个前导
    `0x00` 填充字节，帧从下标 1 起；把帧放进下标 0（同样 65 字节）设备毫无反应。
    故这里是 `00 | A5 04 <reg> <cks> | 0…0`，不是 `A5 04 <reg> <cks> | 0…0`。
    """
    head = bytes([0xA5, 0x04, reg])
    cks = sum(head) & 0xFF
    lead = b"\x00" if write_len > 64 else b""
    return (lead + head + bytes([cks])).ljust(write_len, b"\x00")


def parse(buf):
    if len(buf) < 3 or buf[0] not in (0xA5, 0xA4):
        return None
    length = buf[1]
    if not 3 <= length <= len(buf):
        return None
    body = buf[:length]
    return {'reg': body[2], 'payload': body[3:-1],
            'cks_ok': (sum(body[:-1]) & 0xFF) == body[-1]}


def ask(dev, reg, write_len, read_len, window=0.6):
    """发一帧读请求并等回复，返回 (payload, cks_ok) 或 None。"""
    try:
        dev.write(build(reg, write_len))
    except OSError as exc:
        print(f"    写入失败 reg=0x{reg:02X}: {exc}")
        return None
    deadline = time.time() + window
    while time.time() < deadline:
        data = dev.read(read_len, 80)
        if not data:
            continue
        parsed = parse(bytes(data))
        if parsed and parsed['reg'] == reg:
            return parsed['payload'], parsed['cks_ok']
    return None


def main():
    caps_only = '--caps' in sys.argv

    paths = [p for p in hid_interface_paths() if f'VID_{VID:04X}' in p.upper()]
    if not paths:
        print(f"!! 没有 VID_{VID:04X} 的 HID 接口在线（设备未接入？）")
        return 1

    # 按 PID 分组
    groups = {}
    for p in paths:
        pid = 0
        for seg in p.upper().split('&'):
            if seg.startswith('PID_'):
                pid = int(seg[4:8], 16)
        groups.setdefault(pid, []).append(p)

    print(f"=== 在线墨将接口：{len(paths)} 条，PID {len(groups)} 个 ===\n")

    for pid in sorted(groups):
        print(f"--- PID 0x{pid:04X}（{len(groups[pid])} 条接口）---")
        for p in groups[pid]:
            caps = read_caps(p)
            short = p.split('#')[1] if '#' in p else p
            if not caps:
                print(f"  [打不开] {short}")
                continue
            up, usage, ilen, olen, flen = caps
            vendor = up >= 0xFF00
            print(f"  page=0x{up:04X} usage=0x{usage:04X}  IN={ilen}B OUT={olen}B FEAT={flen}B"
                  f"  {'<厂商通道>' if vendor else ''}")
            print(f"      {short}")
            if caps_only or not vendor:
                continue
            if ilen < 5 or olen < 5:
                print("      双向报文过短，不可能承载 A5 帧，跳过")
                continue
            desc = read_descriptor(p)
            if desc:
                print(f"      报告描述符 {len(desc)}B: {desc.hex()[:120]}"
                      f"{'...' if len(desc) > 60 else ''}")

            dev = hid.device()
            try:
                dev.open_path(p.encode() if isinstance(p, str) else p)
            except OSError as exc:
                print(f"      打开失败: {exc}")
                continue
            try:
                r = ask(dev, 0x04, olen, ilen)
                if r is None:
                    print("      reg=0x04 电量 → 无回复")
                else:
                    payload, cks_ok = r
                    mark = "" if cks_ok else "  !!校验失败"
                    print(f"      reg=0x04 电量 → {payload.hex()}{mark}")
                    if len(payload) >= 2:
                        pct, flag = payload[0], payload[1]
                        print(f"         电量={pct}%  充电标志=0x{flag:02X}"
                              f"（{CHARGING_FLAG.get(flag, '未实测过的值，不作判定')}）")

                r = ask(dev, 0xE2, olen, ilen)
                if r is None:
                    print("      reg=0xE2 型号 → 无回复")
                else:
                    payload, cks_ok = r
                    text = payload.split(b'\x00', 1)[-1]
                    try:
                        mark_txt = text.decode('ascii', 'replace')
                    except Exception:
                        mark_txt = repr(text)
                    print(f"      reg=0xE2 型号 → {payload.hex()}"
                          f"{'' if cks_ok else '  !!校验失败'}  文本={mark_txt!r}")
            finally:
                dev.close()
        print()

    return 0


if __name__ == '__main__':
    sys.exit(main())
