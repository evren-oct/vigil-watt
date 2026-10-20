namespace VigilWatt.Core.Plugins.Compx;

/// <summary>
/// 功 能： Compx 20 字节会话 ACK 的型号码解析（0x3554:FA09 这类公版接收器靠它区分品牌/型号）。
/// 2026/10/20 17:20:00 Bamlax
/// ACK 帧 `13 05 01 00 0A 03 00 00 00 &lt;modelHi&gt; &lt;modelLo&gt; 01 …`；见 docs/hardware/protocols.md §13。
/// </summary>
internal static class CompxSessionAck
{
    /// <summary>会话 ACK 的最小长度（report ID + 型号码两字节）。</summary>
    private const int MinLength = 12;

    /// <summary>
    /// 从输入帧里取型号码（如凌豹 K98 = 0x0212、AULA F99 Pro = 0x0158）。
    /// Windows 的 ReadInput 缓冲有时带 report ID 前缀，偏移 0/1 都试。
    /// </summary>
    public static bool TryGetModelCode(byte[] input, out ushort modelCode)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + MinLength) continue;
            if (input[off] != 0x13 || input[off + 1] != 0x05 || input[off + 2] != 0x01) continue;

            modelCode = (ushort)((input[off + 9] << 8) | input[off + 10]);
            return true;
        }

        modelCode = 0;
        return false;
    }
}
