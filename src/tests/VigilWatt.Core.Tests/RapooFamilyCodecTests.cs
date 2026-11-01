using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Rapoo;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 雷柏各族帧解析的回归测试 —— 0xBB / 键盘 user-data / GCOME_AP，以及鼠标 user-data 的双 report ID。
/// 2026/11/01 22:40:00 xliu
/// 用例帧按官方 AHUB 2.0 解析器的字段偏移反推合成，**不是真机帧**，见 docs/hardware/rapoo-catalog.md §2、§8。
/// </summary>
public sealed class RapooFamilyCodecTests
{
    // ---------- Telink 8273（MT 系列，report 0xBB） ----------

    /// <summary>`BB B0 <mode> <cpi lo hi> <valid=1> <percent>`。</summary>
    private static readonly byte[] T8273Frame =
        Convert.FromHexString("BBB0004006014F");

    [Fact]
    public void Telink8273解析电量且充电态Unknown()
    {
        var r = RapooTelink8273Codec.Parse(T8273Frame);
        Assert.NotNull(r);
        Assert.Equal(79, r!.Value.Percent);
        // 0xBB 没有充电位（官方 batStatus 是由有效位/有线推导的），不能瞎猜
        Assert.Equal(ChargingState.Unknown, r.Value.Charging);
    }

    [Fact]
    public void Telink8273有效位为0时丢弃而不是报0()
    {
        var frame = Convert.FromHexString("BBB0004006004F");
        Assert.Null(RapooTelink8273Codec.Parse(frame));
    }

    [Fact]
    public void Telink8273标识不是B0时丢弃()
        => Assert.Null(RapooTelink8273Codec.Parse(Convert.FromHexString("BB00104006014F")));

    // ---------- TelinkKeyboard（A83 Air 等，载荷 [0]=0x01） ----------

    /// <summary>零售布局：`[1]` 标记、`[2]` profile、`[3..8]` 灯光/休眠/回报率、`[9]` 电量。</summary>
    private static readonly byte[] TelinkKbFrame =
        Convert.FromHexString("000110050403FF06014F");

    [Fact]
    public void Telink键盘按零售布局取电量()
    {
        var r = RapooKeyboardCodec.ParseTelink(TelinkKbFrame, 0x4814);
        Assert.NotNull(r);
        Assert.Equal(79, r!.Value.Percent);
    }

    /// <summary>V700diy 少一档：电量在 `[8]`（PID 决定走哪个布局）。</summary>
    [Fact]
    public void Telink键盘V700变体偏移差一位()
    {
        var v700 = Convert.FromHexString("000110050403FF024F");
        Assert.Equal(79, RapooKeyboardCodec.ParseTelink(v700, 0x4805)!.Value.Percent);
        // 同一个 PID 集合之外仍按零售布局解析，偏移对不上就被 0..100 闸挡掉
        Assert.Null(RapooKeyboardCodec.ParseTelink(v700, 0x4814));
    }

    [Fact]
    public void Telink键盘标记不对时不解析()
        => Assert.Null(RapooKeyboardCodec.ParseTelink(Convert.FromHexString("000210050403FF06014F"), 0x4814));

    // ---------- CH585M（ESK900 系列，布局与 Telink 不同） ----------

    /// <summary>`[1]` 标记、`[2..7]` profile/灯光/休眠/回报率、`[8..9]` 位域、`[10]` 电量。</summary>
    private static readonly byte[] Ch585mFrame =
        Convert.FromHexString("000110050403FF0601024F");

    [Fact]
    public void CH585M按自己的偏移取电量()
    {
        var r = RapooKeyboardCodec.ParseCh585m(Ch585mFrame);
        Assert.NotNull(r);
        Assert.Equal(79, r!.Value.Percent);
    }

    [Fact]
    public void CH585M电量越界时丢弃()
        => Assert.Null(RapooKeyboardCodec.ParseCh585m(Convert.FromHexString("000110050403FF060102FF")));

    // ---------- GCOME_AP（Ace 68 Turbo / VT68） ----------

    [Fact]
    public void GcomeAp解析充电中()
    {
        var r = RapooGcomeApCodec.Parse(Convert.FromHexString("FAFB0700014F"));
        Assert.NotNull(r);
        Assert.Equal(79, r!.Value.Percent);
        Assert.Equal(ChargingState.Charging, r.Value.Charging);
    }

    [Fact]
    public void GcomeAp充电码为0时判放电()
    {
        var r = RapooGcomeApCodec.Parse(Convert.FromHexString("FAFB0700004F"));
        Assert.Equal(ChargingState.Discharging, r!.Value.Charging);
    }

    [Fact]
    public void GcomeAp满电一律判充满()
    {
        var r = RapooGcomeApCodec.Parse(Convert.FromHexString("FAFB07000164"));
        Assert.Equal(ChargingState.Full, r!.Value.Charging);
    }

    [Fact]
    public void GcomeAp帧头不对时不解析()
        => Assert.Null(RapooGcomeApCodec.Parse(Convert.FromHexString("FAFB0800014F")));

    // ---------- 鼠标 user-data 的双 report ID ----------

    /// <summary>同一个鼠标 user-data 布局，`VT_CH584` 放在 report 0x08 上（VT1 SE / VT7 SE）。</summary>
    private static readonly byte[] MouseFrameReport8 =
        Convert.FromHexString("08100500190019014F000301001246010202CD");

    [Fact]
    public void 鼠标状态帧同时认07与08()
    {
        var a = RapooCodec.Parse(MouseFrameReport8, RapooCodec.StatusReportId, 0x08);
        Assert.Equal(RapooFrameKind.Status, a.Kind);
        Assert.Equal(79, a.Percent);
    }

    [Fact]
    public void 只给07时08帧不算状态帧()
    {
        var a = RapooCodec.Parse(MouseFrameReport8, RapooCodec.StatusReportId, 0);
        Assert.Equal(RapooFrameKind.NotStatus, a.Kind);
    }
}
