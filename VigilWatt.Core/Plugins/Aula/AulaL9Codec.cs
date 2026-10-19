using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： 解析狼蛛 L9 Ultra 鼠标 0x85 状态回包。
/// 2026/10/19 23:50:00 contributor
/// 帧布局与通道选择见 docs/hardware/protocols.md §14。
/// </summary>
internal static class AulaL9Codec
{
    /// <summary>发送用 report ID。</summary>
    public const byte OutReportId = 0x0B;

    /// <summary>接收用 report ID。</summary>
    public const byte InReportId = 0x0A;

    /// <summary>状态查询命令。</summary>
    private const byte CmdStatus = 0x85;

    /// <summary>报文含 reportId，故充电标志在下标 9、电量在下标 10。</summary>
    private const int ChargingIndex = 9;
    private const int LevelIndex = 10;

    /// <summary>组一条查询报文：reportId + 0x85 0x00。</summary>
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
        // 实测未上底座时该字节为 0；1 暂按充电中处理，待上底座复核
        charging = ChargingStateMap.FromChargingFlag(input[ChargingIndex] != 0, level);
        return true;
    }
}
