namespace VigilWatt.Core.Plugins.Mojhon;

/// <summary>
/// 功 能： 墨将（MOJHON）A5 帧协议的纯编解码，不碰句柄、不写日志，便于单测。
/// 2026/10/26 00:45:00 workbuddy
///
/// 帧布局与全部取值出处见 docs/hardware/mojhon.md §6.3（USBPcap 实录，35/35 帧校验通过）：
///   [0xA5 短帧 | 0xA4 长帧] [总长度=含头尾] [命令/寄存器] [载荷...] [校验]
///   校验 = 帧内除末字节外所有字节之和 &amp; 0xFF
/// 读请求恒为 4 字节 `A5 04 &lt;寄存器&gt; &lt;校验&gt;`，设备回复时**原样回显寄存器号**。
/// </summary>
internal static class MojhonCodec
{
    /// <summary>短帧起始标记（本协议几乎全部帧都是它）。</summary>
    public const byte HeaderShort = 0xA5;

    /// <summary>长帧起始标记：只出现在 0xD6 分块配置转储里（载荷占满 64 字节）。</summary>
    public const byte HeaderLong = 0xA4;

    /// <summary>线上载荷长度：64 字节中断传输，帧不足部分补零。</summary>
    public const int FrameLength = 64;

    /// <summary>
    /// 电量寄存器。取值实测：官方助手显示 18% 时回 `12 00`，显示 17% 时回 `11 00`，
    /// payload[0] 即百分比（docs/hardware/mojhon.md §6.4 / §7）。
    /// </summary>
    public const byte RegisterBattery = 0x04;

    /// <summary>
    /// 设备型号标识寄存器：应答载荷形如 `58 52 00` + ASCII 标识串（实测本机回 `C4_SERVER`）。
    /// 官方助手用同一标识查型号目录（`MojhonAssistant.exe` 里的 devices[] 表把
    /// `C4_SERVER` 映射到 webName `GALE2`），见 docs/hardware/mojhon.md §2.1。
    /// </summary>
    public const byte RegisterDeviceName = 0xE2;

    /// <summary>
    /// 从型号标识应答里取 ASCII 标识串：跳过前两个字节，取首个 `0x00` 之后到下一个 `0x00`
    /// 之间的可打印字符。实测载荷 `58 52 00 43 34 5F 53 45 52 56 45 52` → `"C4_SERVER"`。
    /// </summary>
    public static bool TryParseDeviceMark(byte[] payload, out string mark)
    {
        mark = "";
        int start = Array.IndexOf(payload, (byte)0x00);
        if (start < 0 || start + 1 >= payload.Length) return false;

        int end = start + 1;
        while (end < payload.Length && payload[end] != 0x00) end++;

        var chars = new char[end - start - 1];
        for (int i = 0; i < chars.Length; i++)
        {
            byte b = payload[start + 1 + i];
            if (b is < 0x20 or > 0x7E) return false;
            chars[i] = (char)b;
        }
        if (chars.Length == 0) return false;

        mark = new string(chars);
        return true;
    }

    /// <summary>
    /// 构造一条读寄存器请求：`A5 04 &lt;寄存器&gt; &lt;校验&gt;`，补零到 64 字节。
    /// 只发实录里出现过的寄存器，不做枚举（docs/hardware/safety.md §3 规则 1）。
    /// </summary>
    public static byte[] BuildRead(byte register)
    {
        var frame = new byte[FrameLength];
        frame[0] = HeaderShort;
        frame[1] = 4;              // 帧长：头 + 长度 + 寄存器 + 校验
        frame[2] = register;
        frame[3] = Checksum(frame.AsSpan(0, 3));
        return frame;
    }

    /// <summary>和校验：帧内除末字节外所有字节之和取低 8 位。</summary>
    public static byte Checksum(ReadOnlySpan<byte> head)
    {
        int sum = 0;
        foreach (byte b in head) sum += b;
        return (byte)(sum & 0xFF);
    }

    /// <summary>
    /// 解析一条应答。**容忍两种回传形态**：报文首字节就是帧头，或首字节是报文 ID、
    /// 帧从下标 1 起（本机 hid 读回实测 64 字节、帧头在 [0]；caps 却报 65，故两种都要认）。
    /// </summary>
    public static bool TryParse(byte[] input, out byte register, out byte[] payload)
    {
        register = 0;
        payload = Array.Empty<byte>();
        if (input.Length < 4) return false;

        int offset = IsHeader(input[0]) ? 0
                   : input.Length > 1 && IsHeader(input[1]) ? 1
                   : -1;
        if (offset < 0) return false;

        int length = input[offset + 1];
        if (length < 4 || offset + length > input.Length) return false;

        var body = input.AsSpan(offset, length);
        if (Checksum(body[..^1]) != body[^1]) return false;

        register = body[2];
        payload = body[3..^1].ToArray();
        return true;
    }

    /// <summary>
    /// 充电标志：payload[1] = 0x00 表示未接充电线（放电），0x01 表示正在充电。
    /// 实测对照见 docs/hardware/mojhon.md §7.2：放电 6 次读数恒 0x00，插线后 120+ 次读数恒 0x01。
    /// 除这两个值外的取值从未观测到，映射为 <see cref="ChargingFlagUnknown"/>，不做外推。
    /// </summary>
    public const byte ChargingFlagDischarging = 0x00;

    /// <inheritdoc cref="ChargingFlagDischarging"/>
    public const byte ChargingFlagCharging = 0x01;

    /// <summary>未实测过的标志值（例如满电、故障）；插件据此上报 Unknown 而非猜测。</summary>
    public const byte ChargingFlagUnknown = 0xFF;

    /// <summary>
    /// 从电量寄存器应答里取百分比与充电标志。payload[0] 是百分比（超过 100 视为设备瞬态，判无效），
    /// payload[1] 是充电标志（见 <see cref="ChargingFlagDischarging"/>）。
    /// 只观测到 2 字节应答，payload 更短时按最少 1 字节容忍，此时标志报 Unknown。
    /// </summary>
    public static bool TryParseBattery(byte[] payload, out int percent, out byte chargingFlag)
    {
        percent = 0;
        chargingFlag = ChargingFlagUnknown;
        if (payload.Length < 1) return false;
        if (payload[0] > 100) return false;
        percent = payload[0];
        if (payload.Length >= 2) chargingFlag = payload[1];
        return true;
    }

    private static bool IsHeader(byte b) => b is HeaderShort or HeaderLong;
}
