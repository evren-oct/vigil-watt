using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从 0x4D 回报率只读解析（`0x0002`）与 1K/8K 表选择的回归。
/// 2026/11/08 22:05:00 xliu
/// 字段与两张表来自官方 `allInfoToObj` / `rateMap` / `mouseDiffConfig`，见 mchose-dpi-report-rate-audit.md §4、§6。
/// </summary>
public sealed class Mchose0x4DRateTests
{
    private static byte[] Reply(ushort cmd, params byte[] payload)
    {
        var frame = new byte[Mchose0x4DCodec.FrameLength];
        frame[0] = Mchose0x4DCodec.ReportId;
        frame[1] = Mchose0x4DCodec.Version;
        frame[2] = Mchose0x4DCodec.Flags;
        frame[3] = (byte)payload.Length;
        frame[4] = (byte)(cmd & 0xFF);
        frame[5] = (byte)(cmd >> 8);
        payload.CopyTo(frame, 8);
        return frame;
    }

    // ── 0x0002 请求 ────────────────────────────────────────────────

    /// <summary>请求无参数：<c>4D 01 01 00 02 00 00 00 03</c>，其余补零。</summary>
    [Fact]
    public void BuildPerformanceQueryIsFixedVector()
    {
        var frame = Mchose0x4DCodec.BuildPerformanceQuery();

        Assert.Equal(Mchose0x4DCodec.FrameLength, frame.Length);
        Assert.Equal("4D0101000200000003", Convert.ToHexString(frame[..9]));
        Assert.All(frame[9..], b => Assert.Equal(0, b));
    }

    // ── 0x0002 应答 ────────────────────────────────────────────────

    /// <summary>payload[1] 高半字节 = usbRateIndex，低半字节 = DPI 档位，两者不能串。</summary>
    [Fact]
    public void SplitsNibblesOfPayload1()
    {
        // 高半字节 0x3 → 线上 3 → 索引 2；低半字节 0x5 → DPI 档位 5
        var frame = Reply(Mchose0x4DCodec.CmdPerformance, 0x01, 0x35, 0x00);

        Assert.True(Mchose0x4DCodec.TryParsePerformance(frame, out var indices));
        Assert.Equal(1, indices.ProfileIndex);
        Assert.Equal(2, indices.UsbRateIndex);
        Assert.Equal(5, indices.DpiIndex);
    }

    /// <summary>payload[2] 高半字节是另一套（2.4G）index，和 usb 那套分开存。</summary>
    [Fact]
    public void ReadsSecondRateIndexFromPayload2()
    {
        var frame = Reply(Mchose0x4DCodec.CmdPerformance, 0x00, 0x20, 0x40);

        Assert.True(Mchose0x4DCodec.TryParsePerformance(frame, out var indices));
        Assert.Equal(1, indices.UsbRateIndex);
        Assert.Equal(3, indices.GRateIndex);
    }

    /// <summary>
    /// 线上编码是 index+1：写侧只会产生 0 或 ≥2，读侧 <c>v&gt;1 ? v-1 : v</c>。
    /// 所以 1 这个"写不出来"的值官方按 1 处理，这里与官方逐字一致，不自行修正。
    /// </summary>
    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x10, 1)]
    [InlineData(0x20, 1)]
    [InlineData(0x30, 2)]
    [InlineData(0x50, 4)]
    public void DecodesWireRateIndex(byte payload1, int expected)
    {
        Assert.True(Mchose0x4DCodec.TryParsePerformance(
            Reply(Mchose0x4DCodec.CmdPerformance, 0x00, payload1, 0x00), out var indices));
        Assert.Equal(expected, indices.UsbRateIndex);
    }

    /// <summary>别的命令的应答不能当成性能配置。</summary>
    [Fact]
    public void RejectsOtherCommands()
        => Assert.False(Mchose0x4DCodec.TryParsePerformance(
            Reply(Mchose0x4DCodec.CmdDeviceInfo, 0x00, 0x20, 0x00), out _));

    /// <summary>payload 只有 2 字节时读不到 <c>[2]</c> 的高半字节，整条拒绝。</summary>
    [Fact]
    public void RejectsShortPayload()
        => Assert.False(Mchose0x4DCodec.TryParsePerformance(
            Reply(Mchose0x4DCodec.CmdPerformance, 0x00, 0x20), out _));

    // ── 表选择 ────────────────────────────────────────────────────

    /// <summary>8K 表：index 5 → 8000 Hz。</summary>
    [Fact]
    public void MapsEightKiloTable()
    {
        Assert.True(MchoseDeviceCatalog.TryGetSettingHz(0x4028, 0x1018, 5, out int hz));
        Assert.Equal(8000, hz);
    }

    /// <summary>1K 表只有 3 档：index 2 → 1000 Hz。</summary>
    [Fact]
    public void MapsOneKiloTable()
    {
        Assert.True(MchoseDeviceCatalog.TryGetSettingHz(0x402A, 0x1016, 2, out int hz));
        Assert.Equal(1000, hz);
    }

    /// <summary>1K 表里 index 3 越界：**不默认成 125Hz**，也不回落到别的档。</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(255)]
    public void RejectsIndexBeyondTable(int index)
    {
        Assert.False(MchoseDeviceCatalog.TryGetSettingHz(0x402A, 0x1016, index, out int hz));
        Assert.Equal(0, hz);
    }

    /// <summary>本体 PID 与 dongle PID 不能混：V7 的槽位里没有 0x1018，查不到就是查不到。</summary>
    [Fact]
    public void DoesNotBorrowAnotherDonglesTable()
        => Assert.False(MchoseDeviceCatalog.TryGetSettingHz(0x402A, 0x1018, 0, out _));

    /// <summary>同一颗 dongle 上不同型号各自选表：0x1018 上 K7 V2 Ultra+ 是 8K，A5 V3 Ultra+ 也是 8K，但 V7 根本不在这颗上。</summary>
    [Theory]
    [InlineData(0x4028, 0x1018, 8000)]   // K7 V2 Ultra+ / 0x1018
    [InlineData(0x4026, 0x1018, 8000)]   // A5 V3 Ultra+ / 0x1018
    [InlineData(0x403B, 0x1014, 8000)]   // K7 V2 Pro / 0x1014
    public void SelectsTableByModelAndUsbPid(ushort model, ushort usbPid, int expected)
    {
        Assert.True(MchoseDeviceCatalog.TryGetSettingHz(model, usbPid, 5, out int hz));
        Assert.Equal(expected, hz);
    }

    /// <summary>型号没收录、或该型号只登记了别的 dongle，一律查不到（保持 Unknown）。</summary>
    [Theory]
    [InlineData(0x9999, 0x1018)]   // 未收录型号
    [InlineData(0x4040, 0x1014)]   // K5 V2 只登记 0x101C
    public void UnknownCombinationHasNoTable(ushort model, ushort usbPid)
        => Assert.False(MchoseDeviceCatalog.TryGetSettingHz(model, usbPid, 0, out _));

    // ── 遥测语义 ──────────────────────────────────────────────────

    /// <summary>读取失败（空 patch）不能把已有有效值洗掉。</summary>
    [Fact]
    public void EmptyPatchKeepsExistingSetting()
    {
        var stored = new PointerTelemetry
        {
            ReportRateSettingHz = TelemetryValue<int>.Inferred(1000, DateTimeOffset.Now),
        };

        var merged = PointerTelemetry.Merge(stored, PointerTelemetry.Empty);

        Assert.Equal(1000, merged.ReportRateSettingHz.Value);
        Assert.Equal(TelemetrySource.Inferred, merged.ReportRateSettingHz.Source);
    }

    /// <summary>只写设置值：实测值必须一直是 Unknown，不能拿配置 index 顶替。</summary>
    [Fact]
    public void SettingNeverFillsMeasured()
    {
        var patch = new PointerTelemetry
        {
            ReportRateSettingHz = TelemetryValue<int>.Inferred(8000, DateTimeOffset.Now),
        };

        var merged = PointerTelemetry.Merge(PointerTelemetry.Empty, patch);

        Assert.True(merged.ReportRateSettingHz.HasValue);
        Assert.False(merged.ReportRateMeasuredHz.HasValue);
        Assert.Equal(TelemetrySource.Unknown, merged.ReportRateMeasuredHz.Source);
    }

    /// <summary>表推导出来的 Hz 一律是 Inferred，不是 DeviceReported。</summary>
    [Fact]
    public void TableDerivedValueIsInferred()
    {
        Assert.True(MchoseDeviceCatalog.TryGetSettingHz(0x4028, 0x1018, 4, out int hz));

        var value = TelemetryValue<int>.Inferred(hz, DateTimeOffset.Now);

        Assert.Equal(4000, value.Value);
        Assert.Equal(TelemetrySource.Inferred, value.Source);
    }

    // ── 发布判定：两个 index 的一致性保护 ──────────────────────────

    /// <summary>两个 index 一致、型号与 USB PID 都查得到表：正常推导并发布。</summary>
    [Fact]
    public void PublishesWhenBothIndexesAgree()
    {
        var decision = MchoseRatePolicy.Decide(0x4028, 0x1018, usbIndex: 4, gRateIndex: 4,
            out int hz, out string table);

        Assert.Equal(MchoseRateDecision.Publish, decision);
        Assert.Equal(4000, hz);
        Assert.Equal("8k", table);
    }

    /// <summary>两个 index 不一致：不发布，也**不拿另一个 index 顶替**（语义未确认，不是协议错误）。</summary>
    [Fact]
    public void RefusesWhenIndexesDiffer()
    {
        var decision = MchoseRatePolicy.Decide(0x4028, 0x1018, usbIndex: 4, gRateIndex: 2,
            out int hz, out string table);

        Assert.Equal(MchoseRateDecision.IndicesDiffer, decision);
        Assert.Equal(0, hz);
        Assert.Equal("", table);
    }

    /// <summary>拒绝发布时已有有效值原样保留（插件只是不调用发布，不清值）。</summary>
    [Fact]
    public void RefusalLeavesExistingValueIntact()
    {
        var stored = new PointerTelemetry
        {
            ReportRateSettingHz = TelemetryValue<int>.Inferred(1000, DateTimeOffset.Now),
        };

        var decision = MchoseRatePolicy.Decide(0x4028, 0x1018, 4, 2, out _, out _);
        var merged = PointerTelemetry.Merge(stored, PointerTelemetry.Empty);

        Assert.Equal(MchoseRateDecision.IndicesDiffer, decision);
        Assert.Equal(1000, merged.ReportRateSettingHz.Value);
        Assert.Equal(TelemetrySource.Inferred, merged.ReportRateSettingHz.Source);
    }

    /// <summary>即便走到"发布"分支，也只写设置值，实测值仍为 Unknown。</summary>
    [Fact]
    public void PublishDecisionNeverTouchesMeasured()
    {
        Assert.Equal(MchoseRateDecision.Publish,
            MchoseRatePolicy.Decide(0x4028, 0x1018, 4, 4, out int hz, out _));

        var merged = PointerTelemetry.Merge(PointerTelemetry.Empty, new PointerTelemetry
        {
            ReportRateSettingHz = TelemetryValue<int>.Inferred(hz, DateTimeOffset.Now),
        });

        Assert.True(merged.ReportRateSettingHz.HasValue);
        Assert.False(merged.ReportRateMeasuredHz.HasValue);
    }

    /// <summary>另外两条"不猜"的分支：查不到表、index 越界。</summary>
    [Theory]
    [InlineData(0x9999, 0x1018, 0, MchoseRateDecision.NoTable)]              // 未收录型号
    [InlineData(0x402A, 0x1018, 0, MchoseRateDecision.NoTable)]              // 该型号没有这颗 dongle
    [InlineData(0x402A, 0x1016, 3, MchoseRateDecision.IndexOutOfRange)]      // 1K 表只有 3 档
    public void OtherRefusals(ushort model, ushort usbPid, byte index, MchoseRateDecision expected)
        => Assert.Equal(expected, MchoseRatePolicy.Decide(model, usbPid, index, index, out _, out _));
}
