using System.Text;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>0xE2 状态事件解析结果。</summary>
internal readonly record struct MouseStatus(int Percent, byte ChargeFlag, int PollingIndex, string Name);

/// <summary>
/// 功 能： 迈从 (MCHOSE) 鼠标 RealTek 0xFF01 协议的纯帧编解码（20 字节 feature 0x11 / 20 字节事件 0x13）。
/// 2026/10/19 19:50:00 Bamlax
/// 载荷逐字节 XOR 0xFF；0B AA 触发 0xE2 状态包，布局见 docs/hardware/protocols.md §7。
/// </summary>
internal static class MchoseMouseCodec
{
    public const byte CmdReportId = 0x11;
    public const byte EventReportId = 0x13;
    public const byte StatusEvent = 0xE2;
    public const byte XorKey = 0xFF;

    /// <summary>命令 payload（不含 report ID）= 20 字节；加 ID 共 21（描述符 0x11 feature = 20B）。</summary>
    public const int CmdPayloadLength = 20;
    public const int CmdTotalLength = 21;

    /// <summary>0xE2 事件最小载荷长度（[0]=E2、[3]=充电、[4]=电量、[6]=回报率档位）。</summary>
    public const int StatusMinLength = 17;

    public const byte ChargeDocked = 0x01;

    private static readonly int[] PollingRates = { 125, 500, 1000, 2000, 4000, 8000 };

    /// <summary>构造电量查询命令 payload：明文 0B AA 00×17，全体 XOR 0xFF。</summary>
    public static byte[] BuildStatusQueryPayload()
    {
        var payload = new byte[CmdPayloadLength];
        payload[0] = 0x0B;
        payload[1] = 0xAA;
        for (int i = 0; i < payload.Length; i++) payload[i] ^= XorKey;
        return payload;
    }

    /// <summary>解析 0x13 事件里的 0xE2 状态包；不是状态包、或休眠畸形包时返回 false。</summary>
    public static bool TryParseStatus(byte[] input, out MouseStatus status)
    {
        status = default;

        foreach (int off in new[] { 0, 1 })
        {
            // off 之后依次是 report ID + 至少 StatusMinLength 字节载荷
            if (input.Length < off + 1 + StatusMinLength) continue;
            if (input[off] != EventReportId) continue;

            int payloadLength = input.Length - off - 1;
            var decoded = new byte[payloadLength];
            for (int i = 0; i < payloadLength; i++)
                decoded[i] = (byte)(input[off + 1 + i] ^ XorKey);

            if (decoded[0] != StatusEvent) continue;

            int percent = decoded[4];
            byte charge = decoded[3];
            int polling = decoded[6];
            string name = ReadName(decoded, 9);

            // 唤醒瞬间会吐电量 0 / 回报率 0 / 名字为空的畸形包，丢弃继续等
            if (percent == 0 && polling == 0 && name.Length == 0) continue;
            if (percent < 1 || percent > 100) continue;

            status = new MouseStatus(percent, charge, polling, name);
            return true;
        }

        return false;
    }

    public static int? PollingRate(int index)
        => index >= 0 && index < PollingRates.Length ? PollingRates[index] : null;

    private static string ReadName(byte[] decoded, int offset)
    {
        var sb = new StringBuilder();
        for (int i = offset; i < decoded.Length; i++)
        {
            byte b = decoded[i];
            if (b is 0x00 or 0xFF) break;
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '?');
        }
        return sb.ToString().Trim();
    }
}
