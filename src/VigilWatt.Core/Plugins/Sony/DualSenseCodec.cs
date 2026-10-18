using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Sony;

/// <summary>
/// 功 能： DualSense 蓝牙完整输入报告的校验及电量解析。
/// 2026/10/19 01:13:01 路泽
/// 协议来源与估算范围见 docs/hardware/dualsense.md。
/// </summary>
public static class DualSenseCodec
{
    public static bool IsSupportedInterface(ushort vendorId, ushort productId, string path,
        ushort usagePage, ushort usage, ushort inputLength)
        => vendorId == 0x054C && productId == 0x0CE6
           && usagePage == 1 && usage == 5 && inputLength == 78
           && path.Contains("{00001124-0000-1000-8000-00805f9b34fb}", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(ReadOnlySpan<byte> report, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (report.Length != 78 || report[0] != 0x31) return false;
        uint crc = UpdateCrc(0xFFFFFFFF, 0xA1);
        foreach (byte value in report[..74]) crc = UpdateCrc(crc, value);
        uint expected = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(report[74..]);
        if (~crc != expected) return false;
        int bucket = report[54] & 0x0F;
        int state = report[54] >> 4;
        if (bucket > 10 || state > 2) return false;
        percent = state == 2 ? 100 : Math.Min(bucket * 10 + 5, 100);
        charging = state switch
        {
            0 => ChargingState.Discharging,
            1 => ChargingState.Charging,
            _ => ChargingState.Full,
        };
        return true;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        return crc;
    }
}
