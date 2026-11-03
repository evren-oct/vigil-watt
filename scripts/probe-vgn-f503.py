"""F503 单次电量查询；需 --query 明确启用，协议见 docs/hardware/vgn.md §2。"""
import ctypes as c
from ctypes import wintypes as w
import json
import sys
import time
import uuid

k = c.WinDLL('kernel32', use_last_error=True)
hid = c.WinDLL('hid')
cfg = c.WinDLL('cfgmgr32')


class Overlapped(c.Structure):
    _fields_ = [('Internal', c.c_size_t), ('InternalHigh', c.c_size_t),
                ('Offset', w.DWORD), ('OffsetHigh', w.DWORD), ('hEvent', w.HANDLE)]


k.CreateFileW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, c.c_void_p, w.DWORD, w.DWORD, w.HANDLE]
k.CreateFileW.restype = w.HANDLE
k.CreateEventW.argtypes = [c.c_void_p, w.BOOL, w.BOOL, w.LPCWSTR]
k.CreateEventW.restype = w.HANDLE
k.ReadFile.argtypes = [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.POINTER(Overlapped)]
k.WriteFile.argtypes = k.ReadFile.argtypes
k.GetOverlappedResult.argtypes = [w.HANDLE, c.POINTER(Overlapped), c.POINTER(w.DWORD), w.BOOL]
k.CancelIoEx.argtypes = [w.HANDLE, c.POINTER(Overlapped)]
k.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
k.CloseHandle.argtypes = [w.HANDLE]
hid.HidD_GetPreparsedData.argtypes = [w.HANDLE, c.POINTER(c.c_void_p)]
hid.HidD_FreePreparsedData.argtypes = [c.c_void_p]
hid.HidP_GetCaps.argtypes = [c.c_void_p, c.c_void_p]
cfg.CM_Get_Device_Interface_List_SizeW.argtypes = [c.POINTER(w.ULONG), c.c_void_p, w.LPCWSTR, w.ULONG]
cfg.CM_Get_Device_Interface_ListW.argtypes = [c.c_void_p, w.LPCWSTR, w.LPWSTR, w.ULONG, w.ULONG]


def transfer(handle, payload=None):
    event = k.CreateEventW(None, True, False, None)
    if not event:
        raise c.WinError(c.get_last_error())
    ov = Overlapped(hEvent=event)
    buf = c.create_string_buffer(payload, 17) if payload else c.create_string_buffer(17)
    n = w.DWORD()
    try:
        operation = k.WriteFile if payload else k.ReadFile
        if not operation(handle, buf, 17, c.byref(n), c.byref(ov)):
            error = c.get_last_error()
            if error != 997:
                raise c.WinError(error)
            if k.WaitForSingleObject(event, 1500) != 0:
                k.CancelIoEx(handle, c.byref(ov))
                k.GetOverlappedResult(handle, c.byref(ov), c.byref(n), True)
                raise TimeoutError('HID timeout')
            if not k.GetOverlappedResult(handle, c.byref(ov), c.byref(n), False):
                raise c.WinError(c.get_last_error())
        if n.value != 17:
            raise RuntimeError(f'Unexpected length: {n.value}')
        return buf.raw[:n.value]
    finally:
        k.CloseHandle(event)


def decode(frame):
    if len(frame) != 17 or frame[:3] != bytes([8, 4, 0]) or sum(frame) & 255 != 0x55:
        return None
    if not 1 <= frame[6] <= 100:
        return None
    return dict(percent=frame[6], charging_flag=frame[7], voltage_mv=int.from_bytes(frame[8:10], 'big'))


def candidates():
    guid = (c.c_ubyte * 16).from_buffer_copy(uuid.UUID('4d1e55b2-f16f-11cf-88cb-001111000030').bytes_le)
    size = w.ULONG()
    if cfg.CM_Get_Device_Interface_List_SizeW(c.byref(size), guid, None, 0):
        raise RuntimeError('Cannot enumerate HID size')
    paths = c.create_unicode_buffer(size.value)
    if cfg.CM_Get_Device_Interface_ListW(guid, None, paths, size, 0):
        raise RuntimeError('Cannot enumerate HID paths')
    matches = []
    for path in paths[:].split('\0'):
        if 'vid_3554&pid_f503' not in path.lower():
            continue
        handle = k.CreateFileW(path, 0, 3, None, 3, 0, None)
        if handle == c.c_void_p(-1).value:
            raise c.WinError(c.get_last_error())
        preparsed = c.c_void_p()
        try:
            if not hid.HidD_GetPreparsedData(handle, c.byref(preparsed)):
                raise RuntimeError('Cannot read HID descriptor')
            caps = (w.USHORT * 32)()
            if hid.HidP_GetCaps(preparsed, caps) != 0x110000:
                raise RuntimeError('Cannot read HID caps')
            if tuple(caps[:4]) == (2, 0xFF02, 17, 17):
                matches.append(path)
        finally:
            if preparsed:
                hid.HidD_FreePreparsedData(preparsed)
            k.CloseHandle(handle)
    return matches


def main():
    assert decode(bytes.fromhex('08040000000250000FC800000000000020')) == dict(percent=80, charging_flag=0, voltage_mv=4040)
    assert decode(bytes(17)) is None
    if '--self-test' in sys.argv:
        print('Parser self-test OK')
        return
    matches = candidates()
    print(json.dumps({'eligible_interfaces': len(matches), 'paths': matches}), flush=True)
    if '--query' not in sys.argv:
        return
    if len(matches) != 1:
        raise RuntimeError('Expected exactly one F503 battery interface')
    handle = k.CreateFileW(matches[0], 0xC0000000, 3, None, 3, 0x40000000, None)
    if handle == c.c_void_p(-1).value:
        raise c.WinError(c.get_last_error())
    try:
        packet = bytes.fromhex('0804000000000000000000000000000049')
        transfer(handle, packet)
        print(json.dumps({'sent': packet.hex()}), flush=True)
        deadline = time.monotonic() + 2
        while time.monotonic() < deadline:
            response = transfer(handle)
            reading = decode(response)
            print(json.dumps({'received': response.hex(), 'reading': reading}), flush=True)
            if reading:
                return
        raise RuntimeError('No valid battery reply')
    finally:
        k.CloseHandle(handle)


if __name__ == '__main__':
    main()
