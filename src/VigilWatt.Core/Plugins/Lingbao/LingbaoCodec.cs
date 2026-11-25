namespace VigilWatt.Core.Plugins.Lingbao;

/// <summary>
/// 功 能： 凌豹 M5PRO Max 的状态查询帧编解码（64 字节 payload，report ID 0）。
/// 2026/10/24 17:42:00 L
/// 帧来自官方驱动运行时抓包与本机实测，见 docs/hardware/brands/lingbao.md §1。
/// </summary>
public static class LingbaoCodec
{
    /// <summary>该接口只有一个 feature report，report ID 恒为 0（65 字节 = ID + 64）。</summary>
    public const byte ReportId = 0x00;

    /// <summary>含 report ID 的 feature 报文长度（官方驱动发 67 字节，本机实测 65 字节同样有效）。</summary>
    public const int ReportLength = 65;

    /// <summary>状态查询命令：官方驱动每 10 秒发一次 <c>00 f7 00…</c>（2026-10-25 抓包确认，无校验和）。</summary>
    public const byte CmdGetStatus = 0xF7;

    /// <summary>响应头长度：report ID + 接收器 ack + 设备返回码，其后才是设备 payload。</summary>
    private const int ResponseHeaderLength = 3;

    /// <summary>充电位下标（[3]=电量、[4]=0x01、[5]=充电位，见 lingbao.md §1.3）。</summary>
    private const int ChargingIndex = 5;

    /// <summary>构造状态查询帧：只有首字节是命令，其余全 0。</summary>
    public static byte[] BuildStatusQuery()
    {
        var payload = new byte[ReportLength - 1];
        payload[0] = CmdGetStatus;
        return payload;
    }

    /// <summary>
    /// 解析状态。响应 = [0]ID [1]接收器 ack [2]设备返回码 [3]电量% [4]固定 0x01 [5]充电位。
    /// 任一字段越界都返回 false——宁可不上报，也不把配置块里的字节当成电量。
    /// </summary>
    public static bool TryParseStatus(byte[] report, out int percent, out bool charging)
    {
        percent = 0;
        charging = false;
        if (report.Length < ChargingIndex + 1) return false;
        if (report[0] != ReportId) return false;
        // 接收器 ack：0 = 未受理/缓存应答，1 = 已受理；其它值说明这不是本命令的响应
        if (report[1] is not (0 or 1)) return false;
        if (report[2] != 0) return false;

        int value = report[ResponseHeaderLength];
        // 与迈从键盘同一条经验：0/1 是休眠占位，真实电量到不了 1（低电阈值 20% 早该提醒）
        if (value is <= 1 or > 100) return false;

        percent = value;
        // 本机对照：插充电线时 0x01（90%），拔线后 0x00（95%）
        charging = report[ChargingIndex] == 1;
        return true;
    }
}
