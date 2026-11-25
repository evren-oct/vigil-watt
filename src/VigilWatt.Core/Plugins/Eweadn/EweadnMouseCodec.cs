namespace VigilWatt.Core.Plugins.Eweadn;

/// <summary>
/// 功 能： 前行者 S9 鼠标（贝盈 beiyin_uuid 协议）的 feature report 电量帧编解码——纯函数，便于单测。
/// 2026/10/18 13:40:00 Admin
/// 帧结构、CRC 与字段偏移见 docs/hardware/brands/eweadn.md §1.2。
/// </summary>
internal static class EweadnMouseCodec
{
    /// <summary>feature report ID（该接口描述符里唯一的 feature 集合）。</summary>
    public const byte FeatureReportId = 0x03;

    /// <summary>帧长 63 字节（协议基类 payloadLength）。</summary>
    public const int FrameLength = 63;

    /// <summary>命令类型固定 0x50（官方 wn 表里所有鼠标命令的第 2 字节）。</summary>
    public const byte CmdType = 0x50;

    /// <summary>取设备基础信息（含电量）的主命令 0x4F、子命令 0x81。</summary>
    public const byte MainBasics = 0x4F;

    public const byte SubBasics = 0x81;

    /// <summary>
    /// 电量字节在**原始响应**里的绝对偏移：官方 decodeGetBasics 读 t.getUint8(6)，
    /// 实测 t[6]=0x49=73%、t[10]+t[11]*255=25899≈S9 Pro 的 26000 DPI，交叉验证成立。
    /// </summary>
    public const int BatteryOffset = 6;

    /// <summary>电量字节的充电标志位（bit7）。</summary>
    public const byte ChargingBit = 0x80;

    /// <summary>电量掩码（低 7 位）。</summary>
    public const byte PercentMask = 0x7F;

    /// <summary>构造 BASICS 查询帧：63 字节，[0]=CRC，[1..5]=50 00 02 4F 81。</summary>
    public static byte[] BuildBasicsQuery()
    {
        var frame = new byte[FrameLength];
        frame[1] = CmdType;
        frame[2] = 0x00;
        frame[3] = 0x02;
        frame[4] = MainBasics;
        frame[5] = SubBasics;
        frame[0] = Checksum(frame);
        return frame;
    }

    /// <summary>CRC = 下标 1..62 求和取低 8 位（官方 generateCRC 从 i=1 起算）。</summary>
    public static byte Checksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 1; i < frame.Length; i++) sum += frame[i];
        return (byte)(sum & 0xFF);
    }

    /// <summary>
    /// 解析 BASICS 应答：返回 (电量%, 是否充电)；非本协议应答或电量越界返回 null。
    /// 判据只用「首字节是 report ID 0x03 + 次字节是命令类型 0x50」——响应里 [4][5] 是数据区，
    /// 不是请求的 4F 81 回显（实测响应为 03 50 00 0B 02 01 49 …）。
    /// </summary>
    public static (int Percent, bool Charging)? ParseBasics(byte[] response)
    {
        if (response.Length < BatteryOffset + 1) return null;
        if (response[0] != FeatureReportId || response[1] != CmdType) return null;

        byte raw = response[BatteryOffset];
        int percent = raw & PercentMask;
        // 鼠标休眠时 dongle 回结构合法但电量字节为 0 的帧；真 0% 不存在（自动关机），按无读数处理
        if (percent == 0) return null;
        if (percent > 100) return null;
        return (percent, (raw & ChargingBit) != 0);
    }
}
