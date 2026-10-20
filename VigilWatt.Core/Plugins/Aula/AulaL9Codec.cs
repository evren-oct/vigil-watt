using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： 解析狼蛛 L9 Ultra 鼠标 0x81 状态回包的电量。
/// 2026/10/19 23:50:00 contributor
/// 帧布局见 docs/hardware/protocols.md §14；电量在 0x81 回包而非 0x85。
/// </summary>
internal static class AulaL9Codec
{
    /// <summary>发送用 report ID。</summary>
    public const byte OutReportId = 0x0B;

    /// <summary>接收用 report ID。</summary>
    public const byte InReportId = 0x0A;

    /// <summary>状态查询命令（回包里含电量）。</summary>
    private const byte CmdStatus = 0x81;

    /// <summary>报文含 reportId，充电标志在下标 17、电量在下标 18。</summary>
    private const int ChargingIndex = 17;
    private const int LevelIndex = 18;

    /// <summary>组一条查询报文：reportId + 0x81 0x00。</summary>
    public static byte[] BuildStatusQuery(int reportLength)
    {
        int len = Math.Max(reportLength, 3);
        var buffer = new byte[len];
        buffer[0] = OutReportId;
        buffer[1] = CmdStatus;
        return buffer;
    }

    /// <summary>解析一条状态回包。返回 false = 不是本协议帧。</summary>
    public static bool TryParse(byte[] input, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;

        if (input.Length <= LevelIndex) return false;
        if (input[0] != InReportId || input[1] != CmdStatus) return false;

        byte level = input[LevelIndex];
        if (level > 100) return false;

        percent = level;
        // 实测：充电标志 0=放电、1=充电中（放底座充电时 00→01）
        charging = ChargingStateMap.FromChargingFlag(input[ChargingIndex] != 0, level);
        return true;
    }
}
