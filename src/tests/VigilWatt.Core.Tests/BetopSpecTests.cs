using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Betop;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BETOP 协议 JSON 化后的帧解析——鲲鹏 0x15 与宙斯 0x04 位域。
/// 2026/11/18 22:40:00 20211
/// 金帧来自 BetopCodecTests 与 docs/hardware/brands/betop.md 的真机样本。
/// </summary>
public sealed class BetopSpecTests
{
    private const string ProtocolsResource = "VigilWatt.Core.Plugins.Betop.specs.betop.protocols.json";
    private const string CatalogResource = "VigilWatt.Core.Plugins.Betop.betop-catalog.json";

    private static readonly ProtocolEngine Engine = ProtocolEngine.Load(ProtocolsResource, CatalogResource);

    private static HidDeviceInfo Info(ushort pid, ushort vid = 0x20BC) => new()
    {
        DevicePath = $"test-{vid:X4}-{pid:X4}",
        VendorId = vid,
        ProductId = pid,
        UsagePage = pid == 0x5073 ? (ushort)0xFF00 : (ushort)0x00FF,
        Usage = 0x0003,
        InputReportLength = pid == 0x5073 ? (ushort)24 : (ushort)64,
        OutputReportLength = pid == 0x5073 ? (ushort)24 : (ushort)64,
    };

    private static PassiveOutcome? Parse(ushort pid, byte[] frame, ushort vid = 0x20BC)
    {
        var info = Info(pid, vid);
        var protocol = Engine.DeviceOf(info)!.Protocol;
        return Engine.Parse(new DeviceRef("test", info.DevicePath, info.VendorId, info.ProductId),
            protocol, frame);
    }

    private static byte[] Status15(byte percent, byte charge)
    {
        var frame = new byte[64];
        frame[0] = 0x02;
        frame[1] = 0x15;
        frame[2] = percent;
        frame[4] = charge;
        return frame;
    }

    private static byte[] Zeus(byte raw1, byte raw23)
    {
        var frame = new byte[24];
        frame[0] = 0x04;
        frame[1] = raw1;
        frame[23] = raw23;
        return frame;
    }

    [Fact]
    public void Status15_Low4_ChargingFlag()
    {
        Assert.Equal(ChargingState.Charging, Parse(0x5159, Status15(100, 0x51))!.Value.Charging);
        Assert.Equal(ChargingState.Discharging, Parse(0x5159, Status15(100, 0x50))!.Value.Charging);
        Assert.Equal(ChargingState.Charging, Parse(0x5159, Status15(93, 0x51))!.Value.Charging);
        Assert.Equal(ChargingState.Discharging, Parse(0x5159, Status15(93, 0x50))!.Value.Charging);
    }

    [Fact]
    public void Status15_HighNibbleIsIgnored()
    {
        Assert.Equal(ChargingState.Discharging, Parse(0x5159, Status15(93, 0xF0))!.Value.Charging);
        Assert.Equal(ChargingState.Charging, Parse(0x5159, Status15(93, 0x0F))!.Value.Charging);
    }

    [Fact]
    public void Status15_ZeroAndOutOfRangeAreNotReadings()
    {
        Assert.Null(Parse(0x5159, Status15(0, 0x50)));
        Assert.Null(Parse(0x5159, Status15(101, 0x50)));
    }

    [Fact]
    public void Status15_OtherHeaderIsNotStatus()
    {
        var frame = Status15(93, 0x50);
        frame[1] = 0x25;
        Assert.Null(Parse(0x5159, frame));
    }

    [Fact]
    public void Status15_NoReportIdLayoutIsAccepted()
    {
        var shifted = new byte[65];
        Array.Copy(Status15(93, 0x50), 0, shifted, 1, 64);
        Assert.Equal(93, Parse(0x5159, shifted)!.Value.Percent);
    }

    [Fact]
    public void Status15_NoChargeVariantStaysUnknownBelowFull()
    {
        var reading = Parse(0x5128, Status15(93, 0x51))!.Value;
        Assert.Equal(ChargingState.Unknown, reading.Charging);
        Assert.Equal(ChargingState.Full, BetopPlugin.FinalizeCharging(reading.Charging, 100));
        Assert.Equal(ChargingState.Unknown, BetopPlugin.FinalizeCharging(reading.Charging, 93));
    }

    [Fact]
    public void FinalizeCharging_PreservesChargingAtHundred()
    {
        Assert.Equal(ChargingState.Charging, BetopPlugin.FinalizeCharging(ChargingState.Charging, 100));
        Assert.Equal(ChargingState.Full, BetopPlugin.FinalizeCharging(ChargingState.Discharging, 100));
        Assert.Equal(ChargingState.Full, BetopPlugin.FinalizeCharging(ChargingState.Unknown, 100));
        Assert.Equal(ChargingState.Discharging, BetopPlugin.FinalizeCharging(ChargingState.Discharging, 93));
    }

    [Fact]
    public void Zeus_LevelAndLink()
    {
        var wake = Parse(0x5073, Zeus(0x02, 0x58))!.Value;
        Assert.Equal(80, wake.Percent);
        Assert.Equal(LinkState.Up, wake.Link);
        Assert.Equal(ChargingState.Discharging, wake.Charging);

        var full = Parse(0x5073, Zeus(0x03, 0x58))!.Value;
        Assert.Equal(100, full.Percent);
        Assert.Equal(ChargingState.Full, BetopPlugin.FinalizeCharging(full.Charging, full.Percent));
    }

    [Fact]
    public void Zeus_PlaceholderFramesAreDown()
    {
        Assert.Equal(LinkState.Down, Parse(0x5073, Zeus(0x02, 0x50))!.Value.Link);
        Assert.Equal(LinkState.Down, Parse(0x5073, Zeus(0x00, 0x10))!.Value.Link);
        Assert.Equal(LinkState.Up, Parse(0x5073, Zeus(0x02, 0x58))!.Value.Link);
    }

    [Fact]
    public void Zeus_UnknownLevelStaysOnlineWithoutPercent()
    {
        var reading = Parse(0x5073, Zeus(0x05, 0x58))!.Value;
        Assert.Null(reading.Percent);
        Assert.Equal(LinkState.Up, reading.Link);
    }

    [Fact]
    public void Zeus_OnlyReportIdFourIsStatusStream()
    {
        var frame = Zeus(0x02, 0x58);
        frame[0] = 0x05;
        Assert.Null(Parse(0x5073, frame));
        Assert.Null(Parse(0x5073, new byte[64]));
    }

    [Fact]
    public void Zeus_NoReportIdLayoutIsAccepted()
    {
        var shifted = new byte[25];
        Array.Copy(Zeus(0x02, 0x58), 0, shifted, 1, 24);
        Assert.Equal(80, Parse(0x5073, shifted)!.Value.Percent);
    }
}
