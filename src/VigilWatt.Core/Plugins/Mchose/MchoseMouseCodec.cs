namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>E2 状态帧的解析结果。Kind 决定其余字段是否有意义。</summary>
internal enum MouseV2PushKind
{
    /// <summary>不是 E2 状态帧。</summary>
    NotStatus,
    /// <summary>有效状态帧（电量在合理范围）。</summary>
    Status,
    /// <summary>是 E2 帧但电量越界（≤1 是关机/休眠占位）。</summary>
    StatusInvalid,
}

/// <summary>
/// 功 能： 迈从鼠标 V2 世代（接收器 0x3837）的纯帧编解码，不碰句柄、不写日志。
/// 2026/10/19 10:45:30 wuzih
/// 帧与型号密码表逆向自官方 M HUB WEB，见 docs/hardware/mchose.md §1.6。
/// </summary>
internal static class MchoseMouseCodec
{
    /// <summary>状态帧取反后的首字节（网页版 _onInvertedFrame 的 lo[0]==0xe2）。</summary>
    public const byte StatusCmd = 0xE2;

    /// <summary>触发状态上报的 feature 命令（官方 MOUSE_LEGACY：11 06 00×19，字节取反写入）。</summary>
    public const byte PokeReportId = 0x11;
    public const byte PokeCmd = 0x06;

    /// <summary>
    /// 触发帧（官方 MOUSE_LEGACY = rid 0x11 + "06 00×19" 逐字节取反）。
    /// WebHID 的 sendFeatureReport(rid, data) 自动加 rid、data 不含 rid；Windows 的 WriteOutput 则要求
    /// buffer[0] = report ID 本身（不取反），其后才是取反后的数据。照抄网页版把 rid 一起取反必写失败。
    /// </summary>
    public static byte[] BuildPokeFrame(int outputLength)
    {
        var frame = new byte[outputLength];
        frame[0] = PokeReportId;               // report ID 原样
        frame[1] = (byte)(PokeCmd ^ 0xFF);     // 数据从 rid 之后逐字节取反（00^FF=FF, 06^FF=F9）
        for (int i = 2; i < frame.Length; i++) frame[i] = 0xFF;
        return frame;
    }

    /// <summary>
    /// 解析 E2 状态帧。Windows ReadInput 缓冲 [0] 是 report ID 前缀（不参与取反），偏移 0/1 都试。
    /// 每帧 payload 全部 ^0xFF 后：[0]=0xE2、[3]=充电（1=充电中）、[4]=电量%。
    /// </summary>
    public static MouseV2PushKind ParseStatus(byte[] input, out int percent, out bool charging)
    {
        percent = 0;
        charging = false;
        if (input.Length < 6) return MouseV2PushKind.NotStatus;

        foreach (int off in new[] { 1, 0 })
        {
            if (input.Length < off + 5) continue;
            var lo = new byte[input.Length - off];
            for (int i = 0; i < lo.Length; i++) lo[i] = (byte)(input[off + i] ^ 0xFF);
            if (lo[0] != StatusCmd) continue;

            percent = lo[4];
            charging = lo[3] == 1;
            if (percent is > 0 and <= 100) return MouseV2PushKind.Status;
            return MouseV2PushKind.StatusInvalid;
        }
        return MouseV2PushKind.NotStatus;
    }

    /// <summary>接收器密码 → 型号名。逆向自官方 getPassWord 表（battery-monitor KB_PW2MODEL）。</summary>
    public static string? ModelFromPassword(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6) return null;
        var key = Convert.ToHexString(payload[..6]);   // 大写无分隔
        return key switch
        {
            "03000000021C" => "G87",
            "030000000077" => "K99",
            "0300000001DF" => "K99 V2",
            "030000000132" => "G98",
            "0300000000D6" => "X75",
            "0A0000000003" => "G98 Pro",
            "0300000001C6" => "X75 V2",
            "030000000115" => "G75 Pro",
            "0A0000000018" => "G98 V2",
            "0A000000002B" => "UT98",
            "0A0000000001" => "Z75",
            "0A0000000009" => "Z75S",
            "0300000001C7" => "KX75",
            "030000000174" => "GX87",
            "0300000001ED" => "GX87 V2",
            "030000000135" => "K87",
            "030000000335" => "G75 V2",
            "0A0000000034" => "G87 V2",
            _ => null,
        };
    }

    /// <summary>
    /// 从会话 ACK（13 05 01 …）的 payload 里提"密码"。
    /// 接收器代际在密码前差一个填充字节（G87 世代在 [6]，G75 世代在 [5]），两处都试，命中即返回。
    /// </summary>
    public static string? ModelFromSessionAck(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + 12) continue;
            if (input[off] != MchoseV1Codec.ReportId || input[off + 1] != MchoseV1Codec.SessionCmd) continue;
            foreach (int start in new[] { 6, 5 })
            {
                var model = ModelFromPassword(input.AsSpan(off + start, 6));
                if (model != null) return model;
            }
        }
        return null;
    }
}
