namespace VigilWatt.Core.Plugins.Lingbao;

/// <summary>
/// 功 能： 凌豹 M5PRO Max 的 FEA 特征报文编解码（64 字节 payload，report ID 0）。
/// 2026/10/24 17:42:00 L
/// 帧来源、校验规则与本机实测记录见 docs/hardware/lingbao.md §1。
/// </summary>
public static class LingbaoCodec
{
    /// <summary>该接口只有一个 feature report，report ID 恒为 0（65 字节 = ID + 64）。</summary>
    public const byte ReportId = 0x00;

    /// <summary>含 report ID 的 feature 报文长度。</summary>
    public const int ReportLength = 65;

    /// <summary>官方驱动 JS 的 FEA_CMD_GET_BATTERY 取值。</summary>
    public const byte CmdGetBattery = 0x83;

    /// <summary>响应头长度：report ID + 接收器 ack + 设备返回码，其后才是设备 payload。</summary>
    private const int ResponseHeaderLength = 3;

    /// <summary>构造电量查询帧：payload[0]=命令，末字节=校验和。</summary>
    public static byte[] BuildBatteryQuery()
    {
        var payload = new byte[ReportLength - 1];
        payload[0] = CmdGetBattery;
        payload[^1] = Checksum(payload);
        return payload;
    }

    /// <summary>ROYUAN 家族公开协议：末字节 = 0xFF - (前 63 字节之和 &amp; 0xFF)，见 lingbao.md §1.2。</summary>
    public static byte Checksum(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2) return 0;
        int sum = 0;
        for (int i = 0; i < payload.Length - 1; i++) sum += payload[i];
        return (byte)(0xFF - (sum & 0xFF));
    }

    /// <summary>
    /// 解析电量。响应 = [0]ID [1]接收器 ack [2]设备返回码 [3]电量% [4]状态字节 [5]低电阈值。
    /// 任一字段越界都返回 false——宁可不上报，也不把配置块里的字节当成电量。充电位语义未校准，调用方不据此报充电。
    /// </summary>
    public static bool TryParseBattery(byte[] report, out int percent)
    {
        percent = 0;
        if (report.Length < ResponseHeaderLength + 2) return false;
        if (report[0] != ReportId) return false;
        // 接收器 ack：无应答时是 0x00，接受命令后置 1；其它值说明这不是本命令的响应
        if (report[1] is not (0 or 1)) return false;
        if (report[2] != 0) return false;

        int value = report[ResponseHeaderLength];
        // 与迈从键盘同一条经验：0/1 是休眠占位，真实电量到不了 1（低电阈值 20% 早该提醒）
        if (value is <= 1 or > 100) return false;
        percent = value;
        return true;
    }
}
