namespace VigilWatt.Core.Plugins.HyperX;

using VigilWatt.Core.Models;

/// <summary>
/// 功 能： HyperX CF2 状态帧的纯解析——不碰句柄、不写日志，便于单测。
/// 2026/10/20 22:08:00 Yang
/// 帧结构、充电阶段字段的证伪过程见 docs/hardware/protocols.md §6。
/// </summary>
internal static class Cf2Codec
{
    /// <summary>事件帧前缀（FB xx xx）。</summary>
    public const byte EventFrame = 0xFB;

    /// <summary>子命令：充电状态事件（插线 01 / 拔线 00）。</summary>
    public const byte SubCharge = 0x09;

    /// <summary>子命令：耳机离线(00) / 在线(01)。</summary>
    public const byte SubPower = 0x0A;

    /// <summary>子命令：电量帧（每变化 1% 推一帧）。</summary>
    public const byte SubBattery = 0x13;

    /// <summary>充电状态事件：进入充电。</summary>
    public const byte ChargeOn = 0x01;

    /// <summary>心跳帧首字节（常驻，无电量信息）。</summary>
    public const byte Heartbeat = 0x45;

    /// <summary>电量帧中百分比所在字节下标。</summary>
    public const int LevelOffset = 3;

    /// <summary>判断是否常驻心跳帧（45 02…，或官方软件通信往返 FF 01…44 xx）。</summary>
    public static bool IsHeartbeat(byte[]? input)
    {
        if (input is not { Length: >= 2 }) return false;
        if (input[0] == Heartbeat && input[1] == 0x02) return true;
        return input[0] == 0xFF && input.Length > 15 && input[14] == 0x44;
    }

    /// <summary>
    /// 解析事件帧。返回 null = 心跳或未识别的报文；
    /// <paramref name="carriesLevel"/> 为 true 时充电状态应沿用设备当前值（电量帧不携带充电状态）。
    /// </summary>
    public static (int? Percent, ChargingState Charging, bool Offline, bool CarriesLevel)? Parse(byte[]? input)
    {
        if (input is not { Length: >= 4 } || IsHeartbeat(input)) return null;

        switch (input[0])
        {
            case EventFrame when input[1] == SubCharge:
                return (null, input[2] == ChargeOn ? ChargingState.Charging : ChargingState.Discharging, false, false);

            case EventFrame when input[1] == SubPower:
                return input[2] switch
                {
                    0x00 => (null, ChargingState.Unknown, true, false),
                    0x01 => (null, ChargingState.Unknown, false, false),
                    _ => null,
                };

            case EventFrame when input[1] == SubBattery:
                int pct = input[LevelOffset];
                return pct is < 0 or > 100
                    ? null
                    : (pct, ChargingState.Unknown, false, true);

            default:
                return null;
        }
    }

    /// <summary>开机/重连时的完整状态帧 <c>51 02 &lt;pct&gt; …</c>；非本协议返回 null。</summary>
    public static int? ParsePowerOnLevel(byte[]? input)
    {
        if (input is not { Length: >= 3 }) return null;
        if (input[0] != 0x51 || input[1] != 0x02) return null;
        int pct = input[2];
        return pct is >= 0 and <= 100 ? pct : null;
    }
}