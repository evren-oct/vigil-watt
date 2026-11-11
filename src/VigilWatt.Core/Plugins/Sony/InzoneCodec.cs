using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Sony;

/// <summary>
/// 功 能： 索尼 INZONE H9 II（接收器模式）电量帧编解码。
/// 2026/10/24 23:45:00 imx1n
/// 真机抓包 + 双向验证。
///
/// 传输：0xFF04 接口，输入/输出**共用 report ID 0x02**，64 字节帧。
/// **必须走控制传输 SET_REPORT（HidD_SetOutputReport）**；写中断 OUT 端点时设备只回显，
/// 会把假数据当成真读数（这条坑值一整晚）。
///
/// 帧格式见 docs/hardware/devices/inzone-h9.md §2。
/// </summary>
public static class InzoneCodec
{
    public const ushort SonyVid = 0x054C;
    public const ushort H9Pid = 0x0FA8;

    public const byte ReportId = 0x02;
    public const int FrameLength = 64;

    /// <summary>请求类别：响应里会变成 0x14。</summary>
    public const byte RequestClassStatus = 0x41;
    /// <summary>请求类别：响应里会变成 0x12。</summary>
    public const byte RequestClassInfo = 0x21;

    private const byte ResponseClassStatus = 0x14;
    private const byte ResponseClassInfo = 0x12;

    private const byte FlagResponse = 0x10;
    private const byte FlagNotify = 0xA0;

    /// <summary>电量查询（响应 0x14/0x04，电量在 frame[14]）。</summary>
    public static byte[] BuildBatteryRequest() => Build(RequestClassStatus, 0x04);

    /// <summary>状态查询（响应 0x14/0x06；同一帧里还有音量，留着给将来做"音量同步"用）。</summary>
    public static byte[] BuildStatusRequest() => Build(RequestClassStatus, 0x06);

    private static byte[] Build(byte requestClass, byte commandId)
    {
        var f = new byte[FrameLength];
        f[0] = ReportId;
        f[1] = 0x0C;
        f[2] = 0x01;
        f[4] = 0xFC;
        f[5] = 0x08;
        f[6] = 0x96;
        f[7] = 0xC3;
        f[8] = requestClass;
        f[9] = commandId;
        f[10] = 0x01;
        f[11] = 0x01;
        f[13] = Checksum(requestClass, commandId);
        return f;
    }

    /// <summary>
    /// 校验字节 = 基值 + 命令号。0x41 类基值 0x9C（0x04 → 0xA0），0x21 类基值 0x7C（0x01 → 0x7D）。
    /// 真机帧反推所得，样本有限；只用于构造固定命令，别外推到别的命令号。
    /// </summary>
    public static byte Checksum(byte requestClass, byte commandId)
        => (byte)((requestClass == RequestClassStatus ? 0x9C : 0x7C) + commandId);

    /// <summary>
    /// 解析电量响应。接受 0x10（查询应答）与 0xA0（设备通知）两种来源标志。
    /// </summary>
    public static bool TryParseBattery(byte[]? frame, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (frame == null || frame.Length < FrameLength) return false;
        if (frame[0] != ReportId) return false;
        if (frame[8] != ResponseClassStatus || frame[9] != 0x04) return false;
        if (frame[10] != FlagResponse && frame[10] != FlagNotify) return false;

        // 0x14/0x04 的 payload 从 [12] 起：00 <charging> <level> <checksum>
        byte raw = frame[14];
        if (raw > 100) return false;   // 未见 0-255 编码；越界按无效处理，避免把偏移量当百分比

        percent = raw;
        charging = frame[13] switch
        {
            0 => ChargingState.Discharging,
            1 => ChargingState.Charging,
            _ => ChargingState.Unknown,
        };
        return true;
    }

    /// <summary>认领判据用：0xFF04 接口、64 字节输入输出。</summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort productId, ushort usagePage, ushort outputLength)
        => vendorId == SonyVid
           && productId == H9Pid
           && usagePage == 0xFF04
           && outputLength == FrameLength;
}
