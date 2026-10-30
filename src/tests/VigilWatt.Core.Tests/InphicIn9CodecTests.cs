using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Inphic;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 英菲克 IN9 feature 0x0C 快照帧的解析回归（真机帧字面量，含全 0 与截断边界）。
/// 2026/10/29 22:35:11 bitbad
/// 帧来源见 docs/hardware/inphic.md §2.2。
/// </summary>
public class InphicIn9CodecTests
{
    /// <summary>真机连续 6 次读取完全一致的那一帧（官方驱动显示 81%，放电）。</summary>
    private static byte[] Sample() =>
        Convert.FromHexString("0C012000010110004D3930393401002514FF5100FF010000B7B76238FFC767096D");

    [Fact]
    public void ParsesRealEightyOnePercentFrame()
    {
        Assert.True(InphicIn9Codec.TryParse(Sample(), out int percent, out byte flag));
        Assert.Equal(81, percent);
        Assert.Equal(0x01, flag);
        Assert.Equal(ChargingState.Discharging, InphicIn9Codec.ToChargingState(flag));
    }

    [Fact]
    public void ReadsModelStringForDiagnostics()
    {
        Assert.True(InphicIn9Codec.TryParseModel(Sample(), out string model));
        Assert.Equal("M9094", model);
    }

    [Fact]
    public void AllZeroFrameIsRejected()
    {
        // 有线本体（5B2E）与"发错刷新帧清空了寄存器"都返回全 0：采信就会显示 0%。
        Assert.False(InphicIn9Codec.TryParse(new byte[InphicIn9Codec.ReportLength], out _, out _));
        Assert.False(InphicIn9Codec.TryParse(new byte[InphicIn9Codec.ReportLength - 1], out _, out _));
    }

    [Fact]
    public void RejectsWrongReportIdTruncatedAndOutOfRange()
    {
        var frame = Sample();
        frame[0] = 0x0B;
        Assert.False(InphicIn9Codec.TryParse(frame, out _, out _));

        frame = Sample();
        Assert.False(InphicIn9Codec.TryParse(frame[..InphicIn9Codec.ChargeFlagOffset], out _, out _));

        frame = Sample();
        frame[InphicIn9Codec.PercentOffset] = 101;
        Assert.False(InphicIn9Codec.TryParse(frame, out _, out _));
    }

    [Fact]
    public void RejectsNullAndEmpty()
    {
        Assert.False(InphicIn9Codec.TryParse(null, out _, out _));
        Assert.False(InphicIn9Codec.TryParse([], out _, out _));
    }

    [Fact]
    public void RejectsZeroPercentAsNotYetFilled()
    {
        // 真机 2026-10-29：刷新帧发出后 4ms 就读，[18] 还是 0，被当成 0% 进了列表并触发低电量提醒。
        var frame = Sample();
        frame[InphicIn9Codec.PercentOffset] = 0;
        Assert.False(InphicIn9Codec.TryParse(frame, out _, out _));
    }

    [Theory]
    [InlineData(0x00, ChargingState.Unknown)]
    [InlineData(0x01, ChargingState.Discharging)]
    [InlineData(0x03, ChargingState.Charging)]
    [InlineData(0x07, ChargingState.Unknown)]
    public void MapsChargeFlagBytes(byte flag, ChargingState expected)
        => Assert.Equal(expected, InphicIn9Codec.ToChargingState(flag));

    [Fact]
    public void RefreshPayloadMatchesCapturedSetReport()
    {
        // 抓包（USBPcap，官方驱动打开瞬间）：SET_REPORT 33 字节 = 0C 01 20 00 <序号> + 28 个 0，
        // 首字节是 report ID 由句柄补，故载荷是 32 字节。载荷 [3]（帧 [4]）是**递增序号**：
        // 官方 `01`→`02`→`03`，发重复值时设备当重复请求忽略、`[8..]` 恒为 0。
        var payload = InphicIn9Codec.BuildRefreshPayload(0x02);
        Assert.Equal(InphicIn9Codec.ReportLength - 1, payload.Length);
        Assert.Equal(new byte[] { 0x01, 0x20, 0x00, 0x02 }, payload[..4]);
        Assert.All(payload[4..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void RefreshPayloadWritesCommandIntoSequenceSlot()
    {
        // 帧偏移 [4] 在载荷里是 [3]：写错位置等于发固定的 00，设备会当成没有命令。
        Assert.Equal(0x7F, InphicIn9Codec.BuildRefreshPayload(0x7F)[InphicIn9Codec.CommandOffset - 1]);
    }

    [Theory]
    [InlineData(1u, 0x01)]
    [InlineData(2u, 0x02)]
    [InlineData(255u, 0xFF)]
    [InlineData(256u, 0x01)]
    [InlineData(510u, 0xFF)]
    public void CommandCounterStartsAtOneAndSkipsZero(uint counter, byte expected)
        => Assert.Equal(expected, InphicIn9Codec.CommandFromCounter(counter));
}
