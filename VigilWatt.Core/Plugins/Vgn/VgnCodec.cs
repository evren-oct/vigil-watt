using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Vgn;

/// <summary>
/// 功 能： 解析 VGN V98 Pro 键盘的 0x1A 电量回包。
/// 2026/10/19 23:50:00 contributor
/// 帧布局与"JS 命令表不等于实际协议"的教训见 docs/hardware/protocols.md §15。
/// </summary>
internal static class VgnCodec
{
    /// <summary>该通道的 report ID。</summary>
    public const byte ReportId = 0x04;

    /// <summary>电量查询帧（7 字节、无校验）。</summary>
    private static readonly byte[] BatteryQuery = { 0x00, 0x00, 0x1A, 0x06, 0x00, 0x00, 0x00 };

    /// <summary>回包里的电量帧标志：载荷第 3 字节等于 0x1A。</summary>
    private const int TagIndex = 3;

    /// <summary>报文含 reportId，故电量在下标 8、充电标志在下标 9。</summary>
    private const int LevelIndex = 8;
    private const int ChargingIndex = 9;

    /// <summary>组一条发送报文：reportId + 7 字节查询帧。</summary>
    public static byte[] BuildQueryReport(int reportLength)
    {
        int len = Math.Max(reportLength, BatteryQuery.Length + 1);
        var buffer = new byte[len];
        buffer[0] = ReportId;
        Array.Copy(BatteryQuery, 0, buffer, 1, BatteryQuery.Length);
        return buffer;
    }

    /// <summary>解析一条输入报文。返回 false = 不是电量帧（命令回显、灯效/键位数据都会落到这里）。</summary>
    public static bool TryParse(byte[] input, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;

        if (input.Length <= ChargingIndex) return false;
        if (input[0] != ReportId || input[TagIndex] != 0x1A) return false;

        byte level = input[LevelIndex];
        if (level > 100) return false;

        percent = level;
        charging = ChargingStateMap.FromChargingFlag(input[ChargingIndex] != 0, level);
        return true;
    }
}
