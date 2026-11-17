using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Betop;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BETOP 候选判定——未登记宙斯设备只识别不探测（P1-B1B）。
/// 2026/11/17 23:40:00 20211
/// 门控只认 0xFF00/usage 1~3/24 且目录未登记；鲲鹏 fallback 与已登记设备保持原样。
/// </summary>
public sealed class BetopCandidateTests
{
    private static readonly BetopPlugin Plugin = new();

    private static HidDeviceInfo Info(ushort vid, ushort pid, ushort page, ushort usage, ushort inputLength)
        => new()
        {
            DevicePath = $"test-{vid:X4}-{pid:X4}-{page:X4}-{usage:X4}-{inputLength}",
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = usage,
            InputReportLength = inputLength,
            OutputReportLength = inputLength,
        };

    /// <summary>未收录宙斯 PID 的三条厂商集合都必须被挡在主动探测之外。</summary>
    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x03)]
    public void UnregisteredZeus_IsRejected(ushort usage)
        => Assert.False(Plugin.IsCandidateForTest(Info(0x20BC, 0x501E, 0xFF00, usage, 24)));

    /// <summary>已登记的 5073/T9 仍按原路径进入探测。</summary>
    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x03)]
    public void RegisteredT9_IsAccepted(ushort usage)
        => Assert.True(Plugin.IsCandidateForTest(Info(0x20BC, 0x5073, 0xFF00, usage, 24)));

    /// <summary>未收录的鲲鹏 PID 仍保留 0x00FF/64 的 fallback。</summary>
    [Fact]
    public void UnknownKunpengFallback_IsKept()
        => Assert.True(Plugin.IsCandidateForTest(Info(0x20BC, 0x9999, 0x00FF, 0x03, 64)));

    [Fact]
    public void RegisteredKunpeng_IsAccepted()
        => Assert.True(Plugin.IsCandidateForTest(Info(0x20BC, 0x5158, 0x00FF, 0x03, 64)));

    /// <summary>非宙斯三元组不触发本次门控（判据本身仍由协议匹配负责）。</summary>
    [Theory]
    [InlineData(0xFF00, 0x03, 64)]   // 宙斯页 + 64 字节（鲲鹏50 副身份形态）
    [InlineData(0x00FF, 0x03, 24)]   // 宙斯杂项集合
    [InlineData(0x00FF, 0x02, 64)]   // usage 不符
    [InlineData(0xFF00, 0x01, 16)]   // 长度不符
    public void NonZeusTriple_DoesNotTriggerGate(ushort page, ushort usage, ushort length)
        => Assert.False(BetopPlugin.IsUnregisteredZeus(
            Info(0x20BC, 0x501E, page, usage, length), entry: null));

    /// <summary>非北通 VID 维持原有拒绝行为（不进入北通候选）。</summary>
    [Fact]
    public void NonBetopVendor_IsRejected()
        => Assert.False(Plugin.IsCandidateForTest(Info(0x1234, 0x501E, 0xFF00, 0x01, 24)));
}
