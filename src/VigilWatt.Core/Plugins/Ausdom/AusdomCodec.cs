using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Ausdom;

/// <summary>
/// 功 能： 解析 AUSDOM G04 接收器 report 0x0A 的状态帧（64 字节）。
/// 2026/10/31 22:50:34 Zen
/// 帧布局来自真机采集（vigilwatt-capture.zip），逐字节对齐见 docs/hardware/ausdom.md §1。
/// 充电标志未验证，一律返回 Unknown（同 AKG N9 的保守做法，见该文 §1.3）。
/// </summary>
internal static class AusdomCodec
{
    /// <summary>状态 input report 的 report ID。</summary>
    public const byte StatusReportId = 0x0A;

    /// <summary>状态帧的子类型（byte1）。</summary>
    private const byte StatusSubType = 0x12;

    /// <summary>信息帧的子类型（byte1）。空闲时的周期心拍，电量在 byte18。</summary>
    private const byte InfoSubType = 0x13;

    private const byte ModeNormal = 0x05;
    private const byte ModeOff = 0x02;

    /// <summary>长帧里的「电量就位」标志（byte5，仅 B4==0 的长帧有效）。</summary>
    private const byte LongBatteryPresent = 0x02;

    /// <summary>
    /// 解析一帧。返回 null = 不是本协议的电池帧（其余字段无意义）。
    /// 充电位：短帧 C4 的 byte6（0x01=充电，0x00=放电），依据真机实测修正（用户反馈第三次采集方向相反，见 ausdom.md §1.3）。
    /// 长帧 C6 / 信息帧 0x13 未观察到可靠充电位，仍报 Unknown。
    /// </summary>
    public static (int Percent, ChargingState Charging)? TryParse(byte[] input)
    {
        if (input == null || input.Length < 7) return null;
        if (input[0] != StatusReportId) return null;

        // 信息帧（0x13）：空闲周期心拍，电量固定在第 18 字节。
        // 采集里它每隔 ~3-4 秒发一次、内容稳定（MAC@B5-B10，电量@B18=94），是设备睡着时的唯一稳定信号源。
        // 该帧无充电位，保持 Unknown。
        if (input[1] == InfoSubType)
        {
            if (input.Length <= 18) return null;
            int infoPercent = input[18];
            if (infoPercent is < 1 or > 100) return null;
            return (infoPercent, ChargingState.Unknown);
        }

        if (input[1] != StatusSubType) return null;

        byte mode = input[2];
        // 只有正常态（0x05）携带电量；关机/子帧（0x02）等不报电量。
        if (mode != ModeNormal) return null;

        byte b3 = input[3];
        int percent;
        ChargingState charging;

        if (b3 == 0xC4)
        {
            // 短帧 C4：byte4=电量，byte6=充电位。
            // 真机实测（2026-11-01 用户反馈「充电状态相反」后修正）：byte6=0x01 → 充电，byte6=0x00 → 放电。
            percent = input[4];
            charging = input[6] switch
            {
                0x01 => ChargingState.Charging,
                0x00 => ChargingState.Discharging,
                _ => ChargingState.Unknown,
            };
        }
        else if (b3 == 0xC7)
        {
            // 短帧 C7：byte4 直接是百分比，未观察到可靠充电位。
            percent = input[4];
            charging = ChargingState.Unknown;
        }
        // 长帧（C6）：byte4==0 且 byte5==0x02 时，电量在 byte6；未观察到充电位。
        else if (b3 == 0xC6 && input[4] == 0 && input[5] == LongBatteryPresent)
        {
            percent = input[6];
            charging = ChargingState.Unknown;
        }
        else
        {
            return null;
        }

        // 过滤休眠瞬态 / 越界值（基类异常过滤器会再挡一次 94→1 的跳变，这里先剔除明显非电池帧）。
        if (percent is < 1 or > 100) return null;

        return (percent, charging);
    }
}
