using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Razer;

/// <summary>
/// 功 能： 雷蛇 Kraken V4 接收器（1532:056C）电量/充电查询帧的构造与应答解析。
/// 2026/10/19 22:26:12 kaniu
/// 帧格式、命令号与真机样本见 docs/hardware/protocols.md §16。
/// </summary>
public static class RazerCodec
{
    public const ushort VendorId = 0x1532;
    public const ushort KrakenV4DonglePid = 0x056C;
    public const byte ReportId = 0x02;
    public const int ReportLength = 64;

    public const ushort UsagePage = 0xFF14;
    public const ushort Usage = 0x0001;

    public const byte GetBatteryStatus = 0x21;
    public const byte GetChargingStatus = 0x2A;

    public const string Protocol = "razer-kraken-v4-report2";

    // 以下是官方协议常量，不是可调参数：换一个值设备就不应答（docs/hardware/protocols.md §16.2）。
    private const byte TransactionId = 0x60;
    private const byte RequestDirection = 0x00;
    private const byte ResponseDirection = 0x01;
    private const byte DongleFlag = 0x80;
    private const byte SuccessStatus = 0x02;
    private const int PayloadOffset = 13;

    /// <summary>构造一条查询帧。请求头第三字节是携带的数据长度，查询命令不带数据。</summary>
    public static byte[] BuildQuery(byte command)
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[2] = TransactionId;
        frame[6] = 4;
        frame[9] = DongleFlag;
        frame[10] = command;
        frame[11] = RequestDirection;
        frame[12] = 0;
        frame[ReportLength - 2] = ChecksumOfRequest(frame);
        return frame;
    }

    /// <summary>解析应答；只有命令、方向、长度、状态与校验全部对上才返回 true。</summary>
    public static bool TryParseReply(ReadOnlySpan<byte> frame, byte command, out byte value)
    {
        value = 0;
        if (frame.Length != ReportLength
            || frame[0] != ReportId
            || frame[1] != SuccessStatus
            || frame[2] != TransactionId
            || frame[10] != command
            || frame[11] != ResponseDirection
            || frame[12] != 1
            || frame[ReportLength - 2] != ChecksumOfReply(frame))
            return false;

        value = frame[PayloadOffset];
        return true;
    }

    /// <summary>电量应答的百分比（0~100）。超出范围说明不是电量帧。</summary>
    public static bool TryParseBattery(ReadOnlySpan<byte> frame, out int percent)
    {
        percent = 0;
        if (!TryParseReply(frame, GetBatteryStatus, out byte value) || value > 100) return false;
        percent = value;
        return true;
    }

    /// <summary>充电应答：0 = 未充电、1 = 充电中，其余值协议未定义，交给 <see cref="ChargingState.Unknown"/>。</summary>
    public static bool TryParseCharging(ReadOnlySpan<byte> frame, out byte chargingFlag)
        => TryParseReply(frame, GetChargingStatus, out chargingFlag) && chargingFlag <= 1;

    public static ChargingState ToChargingState(byte chargingFlag, int? percent) => chargingFlag switch
    {
        1 => ChargingState.Charging,
        0 when percent.HasValue => ChargingStateMap.FromChargingFlag(false, percent.Value),
        0 => ChargingState.Unknown,
        _ => ChargingState.Unknown,
    };

    /// <summary>请求校验和覆盖 [0..61]：官方 Synapse 的 `_createDataSendGeneral` 就是从 0 起算的。</summary>
    private static byte ChecksumOfRequest(ReadOnlySpan<byte> frame)
    {
        byte crc = 0;
        for (int i = 0; i < ReportLength - 2; i++) crc ^= frame[i];
        return crc;
    }

    /// <summary>应答校验和从 [1] 起算（不含报告 ID）——真机样本逐字节验证，见 protocols.md §16.3。</summary>
    private static byte ChecksumOfReply(ReadOnlySpan<byte> frame)
    {
        byte crc = 0;
        for (int i = 1; i < ReportLength - 2; i++) crc ^= frame[i];
        return crc;
    }
}
