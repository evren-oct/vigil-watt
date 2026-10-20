using System.Text;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Haute;

/// <summary>
/// 功 能： Haute Adapter 4DS 固件识别与只读电量状态解析。
/// 2026/10/19 20:38:46 kaniu
/// </summary>
public static class HauteCodec
{
    public const byte FirmwareReportId = 0xF8;
    public const byte StatusReportId = 0xF9;
    public const byte DeviceInfoReportId = 0xFA;

    public static bool TryParseDeviceId(ReadOnlySpan<byte> frame, out string deviceId)
    {
        deviceId = "";
        if (frame.Length < 9 || frame[0] != DeviceInfoReportId) return false;
        var chipId = frame.Slice(1, 8);
        if (chipId.IndexOfAnyExcept((byte)0) < 0 || chipId.IndexOfAnyExcept((byte)0xFF) < 0) return false;
        deviceId = "haute:chip-" + Convert.ToHexString(chipId).ToLowerInvariant();
        return true;
    }

    public static bool IsLegacyPathId(string deviceId)
        => deviceId.Length == 22 && deviceId.StartsWith("haute:", StringComparison.OrdinalIgnoreCase)
            && deviceId.AsSpan(6).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    public static bool IsHauteFirmware(ReadOnlySpan<byte> frame)
        => frame.Length >= 9 && frame[0] == FirmwareReportId
           && Encoding.ASCII.GetString(frame[1..]).StartsWith("Haute42 ", StringComparison.Ordinal);

    public static bool TryParse(ReadOnlySpan<byte> frame, out int? percent, out ChargingState charging)
    {
        percent = null;
        charging = ChargingState.Unknown;
        if (frame.Length < 14 || frame[0] != StatusReportId) return false;
        if (frame[12] == 0xFF) return true;
        if (frame[12] > 100) return false;
        percent = frame[12];
        int charge = frame[13] & 0x0F;
        charging = charge == 2 ? ChargingState.Full
            : charge != 0 ? ChargingState.Charging : ChargingState.Discharging;
        return true;
    }
}
