"""Read-only HID++ 1.0 receiver-info probe for Logitech Bolt (046D:C548).

Only sends the read requests VigilWatt already uses:
Root.Ping, RECEIVER_INFO 0xB5/0x03, BOLT_PAIRING 0xB5/0x51,
BOLT_CODENAME 0xB5/0x61. No configuration writes, no unknown commands.
"""
import sys
import time

try:
    import hid
except ImportError:
    print("pip install hidapi 后再跑")
    sys.exit(1)

VID, PID = 0x046D, 0xC548


def open_long():
    for d in hid.enumerate(VID, PID):
        if (d.get("usage_page"), d.get("usage")) == (0xFF00, 2):
            h = hid.device()
            h.open_path(d["path"])
            return h
    raise SystemExit("未找到 usage FF00/0002 长接口")


def send(h, dev, sub, reg, args, timeout=3.0, label=""):
    pkt = bytearray(20)
    pkt[0] = 0x11
    pkt[1] = dev
    pkt[2] = sub
    pkt[3] = reg
    for i, b in enumerate(args):
        pkt[4 + i] = b

    print(f"\n[{label}] write {bytes(pkt[:8]).hex()} ...")
    h.write(pkt)
    t0 = time.time()
    while time.time() - t0 < timeout:
        data = h.read(20, timeout_ms=100)
        if not data:
            continue
        raw = bytes(data)
        print(f"  +{time.time() - t0:.3f}s raw={raw.hex()}")
        if raw[2] == sub and raw[3] == reg:
            print("  -> MATCH")
            return raw
        if raw[2] in (0x8F, 0xFF):
            print("  -> ERROR REPLY")
            return raw
    print("  -> no match within timeout")
    return None


def main():
    h = open_long()
    try:
        send(h, 0x01, 0x00, 0x1B, bytes([0x00, 0x00, 0x5A]), 2.0, "Root.Ping dev1")
        send(h, 0xFF, 0x83, 0xB5, bytes([0x03, 0x00, 0x00]), 3.0, "RECEIVER_INFO 0xB5/0x03")
        send(h, 0xFF, 0x83, 0xB5, bytes([0x51, 0x00, 0x00]), 3.0, "BOLT_PAIRING 0xB5/0x51")
        send(h, 0xFF, 0x83, 0xB5, bytes([0x61, 0x01, 0x00]), 3.0, "BOLT_CODENAME 0xB5/0x61")
        send(h, 0xFF, 0x83, 0xB5, bytes([0x03, 0x00, 0x00]), 3.0, "RECEIVER_INFO retry")
    finally:
        h.close()


if __name__ == "__main__":
    main()
