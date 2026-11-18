using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Langtu;

/// <summary>
/// 功 能： 狼途 T87 2.4G 接收器的 GET_DEVICE_INFO 帧编解码——不碰句柄、不写日志，便于单测。
/// 2026/11/17 16:40:00 meish
/// 帧格式、自报 VID/PID 与真机样本见 docs/hardware/devices/langtu.md §2。
/// </summary>
internal static class LangtuT87Codec
{
    /// <summary>请求包头（响应包首字节为 0x55）。</summary>
    public const byte RequestHeader = 0xAA;

    public const byte ResponseHeader = 0x55;

    /// <summary>GET_DEVICE_INFO 命令号（新协议 0x10；旧协议 0xA0 在本机全零应答）。</summary>
    public const byte Command = 0x10;

    /// <summary>协议头长度：[0]=0x55 [1]=cmd [2]=len [3..4]=addr [5..7]=尾包标记。</summary>
    public const int HeaderLength = 8;

    public const int WorkModeOffset = 16;
    public const int BatteryOffset = 17;
    public const int ChargeOffset = 18;

    /// <summary>解析电量至少需要的 payload 长度（充电位在偏移 18）。</summary>
    public const int MinPayloadLength = ChargeOffset + 1;

    /// <summary>本协议族的厂商 ID（payload 自报，用于确认应答确实来自 0C45 设备）。</summary>
    public const ushort ReportedVendorId = 0x0C45;

    /// <summary>单次读数：电量 @17、充电位 @18、工作模式 @16，以及自报 PID（只记日志，不进判据）。</summary>
    public readonly record struct Reading(int Percent, bool Charging, byte WorkMode, ushort ReportedPid);

    /// <summary>单包数据字节数 = 输出报文长度 - report ID - 协议头（33 → 24）。</summary>
    public static int PacketSize(int outputLength) => outputLength - 1 - HeaderLength;

    /// <summary>[3] 写本包字节数（24）而非总长 56，写总量零应答——见 docs/hardware/devices/langtu.md §2.1。</summary>
    public static byte[] BuildInfoRequest(int outputLength)
    {
        var frame = new byte[outputLength];
        frame[1] = RequestHeader;
        frame[2] = Command;
        frame[3] = (byte)PacketSize(outputLength);
        return frame;
    }

    /// <summary>电量 @17、充电 @18；自报 VID 必须等于 0x0C45（PID 不校验，见 langtu.md §2.3）。</summary>
    public static bool TryParse(byte[] input, out Reading reading)
    {
        reading = default;
        int start = -1;
        if (input.Length > 0 && input[0] == ResponseHeader) start = 0;
        else if (input.Length > 1 && input[1] == ResponseHeader) start = 1;
        if (start < 0) return false;

        if (input.Length < start + HeaderLength + MinPayloadLength) return false;
        if (input[start + 1] != Command) return false;

        var payload = new ReadOnlySpan<byte>(input, start + HeaderLength, MinPayloadLength);
        int reportedVid = payload[4] | (payload[5] << 8);
        int reportedPid = payload[6] | (payload[7] << 8);
        byte workMode = payload[WorkModeOffset];
        byte battery = payload[BatteryOffset];
        byte charge = payload[ChargeOffset];

        // 全零 = 键盘休眠占位；VID 不符 = 结构合法但不是这套设备回的帧
        if (reportedVid != ReportedVendorId || battery > 100) return false;

        reading = new Reading(battery, charge != 0, workMode, (ushort)reportedPid);
        return true;
    }

    /// <summary>充电标志只有"是否充电"一档：收尾规则（未充电且 100% ⇒ 已充满）见 <see cref="ChargingStateMap"/>。</summary>
    public static ChargingState MapCharging(Reading reading)
        => ChargingStateMap.FromChargingFlag(reading.Charging, reading.Percent);

    /// <summary>接口是否属于狼途：同 VID/PID 下只能靠产品名分品牌（前行者 X87 共用 0C45:FEFE）。</summary>
    public static bool LooksLikeLangtu(string? product)
        => !string.IsNullOrEmpty(product)
           && product.Contains("langtu", StringComparison.OrdinalIgnoreCase);
}
