using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Betop;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BETOP 候选判定——未登记且只有被动协议的接口只识别不探测。
/// 2026/11/17 23:40:00 20211
/// 已登记设备按目录认领；未登记鲲鹏仍保留 0x00FF/64 fallback。
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

    /// <summary>未收录的被动协议 PID（501E / 5084）不得进入主动探测。</summary>
    [Theory]
    [InlineData(0x501E, 0x01)]
    [InlineData(0x501E, 0x02)]
    [InlineData(0x501E, 0x03)]
    [InlineData(0x5084, 0x02)]
    public void UnregisteredPassivePid_IsRejected(ushort pid, ushort usage)
        => Assert.False(Plugin.IsCandidateForTest(Info(0x20BC, pid, 0xFF00, usage, 24)));

    /// <summary>已登记的 5073/T9 与 9 个新宙斯 PID 仍按原路径进入被动探测。</summary>
    [Theory]
    [InlineData(0x5073)]
    [InlineData(0x500B)]
    [InlineData(0x502F)]
    [InlineData(0x5074)]
    [InlineData(0x5076)]
    [InlineData(0x5077)]
    [InlineData(0x5104)]
    [InlineData(0x5107)]
    public void RegisteredZeus_IsAccepted(ushort pid)
        => Assert.True(Plugin.IsCandidateForTest(Info(0x20BC, pid, 0xFF00, 0x02, 24)));

    /// <summary>未收录的鲲鹏 PID 仍保留 0x00FF/64 的 fallback。</summary>
    [Fact]
    public void UnknownKunpengFallback_IsKept()
        => Assert.True(Plugin.IsCandidateForTest(Info(0x20BC, 0x9999, 0x00FF, 0x03, 64)));

    [Fact]
    public void RegisteredKunpeng_IsAccepted()
        => Assert.True(Plugin.IsCandidateForTest(Info(0x20BC, 0x5158, 0x00FF, 0x03, 64)));

    /// <summary>宙斯通道由接收器承载，认领时推断为 2.4G。</summary>
    [Fact]
    public void ZeusChannel_InfersWireless24G()
        => Assert.Equal(ConnectionKind.Wireless24G,
            BetopPlugin.ProbeInferredConnection(Info(0x20BC, 0x5073, 0xFF00, 0x01, 24)));

    [Theory]
    [InlineData(0x00FF, 0x03, 64)]   // 鲲鹏广播 / 有线
    [InlineData(0x00FF, 0x03, 24)]   // 宙斯杂项集合
    [InlineData(0xFF00, 0x03, 64)]   // 非 24 字节的 0xFF00 接口
    public void NonZeusChannel_DoesNotInferConnection(ushort page, ushort usage, ushort length)
        => Assert.Null(BetopPlugin.ProbeInferredConnection(Info(0x20BC, 0x5158, page, usage, length)));

    /// <summary>非北通 VID 维持原有拒绝行为（不进入北通候选）。</summary>
    [Fact]
    public void NonBetopVendor_IsRejected()
        => Assert.False(Plugin.IsCandidateForTest(Info(0x1234, 0x501E, 0xFF00, 0x01, 24)));
}
