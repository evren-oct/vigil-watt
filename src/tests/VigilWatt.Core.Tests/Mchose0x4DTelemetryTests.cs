using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从 0x4D 三项遥测（连接 / 充电 / 电量）的映射与能力声明回归。
/// 2026/11/08 21:10:00 xliu
/// 官方枚举（`1=有线 / 2=无线`、`0=未充 / 1=充电中 / 2=充满`）见 docs/hardware/brands/mchose.md §3.3；
/// 真机帧见 <see cref="Mchose0x4DCodecTests"/>，两份样本同源（2026-10-21 本机 3837:1018）。
/// </summary>
public sealed class Mchose0x4DTelemetryTests
{
    /// <summary>真机 0x0900 应答：connect=2、charge=0、battery=0x64。</summary>
    private const string DeviceInfoReply =
        "4D01011000090000" + "373826400400000000100200640008E4" + "EF";

    private static byte[] Frame(string hex, int totalLength = Mchose0x4DCodec.FrameLength)
    {
        var bytes = Convert.FromHexString(hex);
        var frame = new byte[Math.Max(totalLength, bytes.Length)];
        bytes.CopyTo(frame, 0);
        return frame;
    }

    // ── 连接方式 ────────────────────────────────────────────────────

    /// <summary>官方只给了 1 / 2 两档；2.4G 有真机样本（connect=2 时数据确实走 dongle）。</summary>
    [Theory]
    [InlineData(1, ConnectionKind.Wired)]
    [InlineData(2, ConnectionKind.Wireless24G)]
    public void MapsKnownConnectStatus(byte raw, ConnectionKind expected)
        => Assert.Equal(expected, Mchose0x4DCodec.ToConnectionKind(raw));

    /// <summary>0 与其它值官方没有语义：**必须 Unknown**，不能猜成"有线"或"未连接"。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(0xFF)]
    public void UnmappedConnectStatusStaysUnknown(byte raw)
        => Assert.Equal(ConnectionKind.Unknown, Mchose0x4DCodec.ToConnectionKind(raw));

    // ── 充电状态 ────────────────────────────────────────────────────

    /// <summary>0 = 未充电：非满电按放电、满电按"已充满"收尾（全项目共用 <see cref="ChargingStateMap"/>）。</summary>
    [Theory]
    [InlineData(50, ChargingState.Discharging)]
    [InlineData(100, ChargingState.Full)]
    public void MapsNotChargingStatus(int percent, ChargingState expected)
        => Assert.Equal(expected, Mchose0x4DCodec.ToChargingState(0, percent));

    /// <summary>1 = 充电中：即使已经 100% 也还是"充电中"，不是"已充满"。</summary>
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void MapsChargingStatus(int percent)
        => Assert.Equal(ChargingState.Charging, Mchose0x4DCodec.ToChargingState(1, percent));

    /// <summary>2 = 已充满（官方枚举，真机未留证）。</summary>
    [Fact]
    public void MapsFullStatus()
        => Assert.Equal(ChargingState.Full, Mchose0x4DCodec.ToChargingState(2, 100));

    /// <summary>未知充电码**不许默认成"未充电"**——那会把充电中的设备显示成放电。</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(0x10)]
    [InlineData(0xFF)]
    public void UnmappedChargeStatusStaysUnknown(byte raw)
        => Assert.Equal(ChargingState.Unknown, Mchose0x4DCodec.ToChargingState(raw, 50));

    // ── 真机帧端到端 ────────────────────────────────────────────────

    /// <summary>真机应答的三个字段各走各的映射：100% / 放电（charge=0 且已满 ⇒ Full）/ 2.4G。</summary>
    [Fact]
    public void RealReplyMapsToUnifiedTriple()
    {
        Assert.True(Mchose0x4DCodec.TryParseDeviceInfo(Frame(DeviceInfoReply),
            out int percent, out byte charge, out byte connect));

        Assert.Equal(100, percent);
        Assert.Equal(ChargingState.Full, Mchose0x4DCodec.ToChargingState(charge, percent));
        Assert.Equal(ConnectionKind.Wireless24G, Mchose0x4DCodec.ToConnectionKind(connect));
    }

    /// <summary>
    /// 三个字段索引隔离的加强用例：让 connect / charge / percent 取三个**互不相同且都合法**的值，
    /// 任一字段错位都会同时打破两条断言（原用例的 1 / 2 / 0x2A 组合也能过，但区分度不如这组）。
    /// </summary>
    [Fact]
    public void ThreeFieldsDoNotShiftAcrossIndexes()
    {
        // payload[10]=0x01(有线) payload[11]=0x02(充满) payload[12]=0x37(55%)
        var frame = Frame("4D01010E00090000" + "3738264004000000000001023700" + "00");

        Assert.True(Mchose0x4DCodec.TryParseDeviceInfo(frame, out int percent, out byte charge, out byte connect));

        Assert.Equal(0x37, percent);
        Assert.Equal(0x02, charge);
        Assert.Equal(0x01, connect);
        Assert.Equal(55, percent);
        Assert.Equal(ChargingState.Full, Mchose0x4DCodec.ToChargingState(charge, percent));
        Assert.Equal(ConnectionKind.Wired, Mchose0x4DCodec.ToConnectionKind(connect));
    }

    /// <summary>payload 长度不足 14（读不到 gameMode）时三项**一个都不许产出**，不能只解析前两个。</summary>
    [Fact]
    public void ShortPayloadYieldsNoTelemetry()
    {
        var shortFrame = Frame("4D01010D00090000" + "373826400400000000000102" + "00");

        Assert.False(Mchose0x4DCodec.TryParseDeviceInfo(shortFrame, out _, out _, out _));
    }

    // ── Catalog 能力声明 ────────────────────────────────────────────

    /// <summary>造一条接口信息：默认就是 0x4D 命令通道的指纹（`0xFF01/0x0001`、64/64）。</summary>
    private static HidDeviceInfo Dongle(ushort pid, ushort page = 0xFF01, ushort usage = 0x0001,
        ushort input = 64, ushort output = 64)
        => new()
        {
            DevicePath = @"\\?\hid#vid_3837&pid_test#unit",
            VendorId = 0x3837,
            ProductId = pid,
            UsagePage = page,
            Usage = usage,
            InputReportLength = input,
            OutputReportLength = output,
        };

    /// <summary>唯一有真机读数的通道：三项都 Supported。</summary>
    [Fact]
    public void VerifiedTransportDeclaresThreeCapabilities()
    {
        var caps = MchoseDeviceCatalog.CapabilitiesOf(Dongle(0x1018));

        Assert.Equal(CapabilityState.Supported, caps.Of(DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Supported, caps.Of(DeviceCapability.ConnectionRead));
        Assert.Equal(CapabilityState.Supported, caps.Of(DeviceCapability.ChargingRead));
    }

    /// <summary>真机没读过的项不许顺手声明成 Supported（DPI / 回报率都还没做）。</summary>
    [Fact]
    public void VerifiedTransportDoesNotClaimUnimplementedCapabilities()
    {
        var caps = MchoseDeviceCatalog.CapabilitiesOf(Dongle(0x1018));

        Assert.Equal(CapabilityState.Unverified, caps.Of(DeviceCapability.DpiRead));
        Assert.Equal(CapabilityState.Unverified, caps.Of(DeviceCapability.DpiLevelRead));
        Assert.Equal(CapabilityState.Unverified, caps.Of(DeviceCapability.RateRead));
        Assert.Equal(CapabilityState.Unverified, caps.Of(DeviceCapability.DpiWrite));
        Assert.Equal(CapabilityState.Unverified, caps.Of(DeviceCapability.RateWrite));
    }

    /// <summary>同一颗 dongle 的 61 字节静默接口（`0xFF0B/0x00C4`）不能借到命令通道的声明。</summary>
    [Fact]
    public void SilentSiblingInterfaceOfVerifiedDongleDeclaresNothing()
        => Assert.True(MchoseDeviceCatalog.CapabilitiesOf(
            Dongle(0x1018, 0xFF0B, 0x00C4, 61, 61)).IsEmpty);

    /// <summary>同代另外四颗共用 dongle **没有真机验证**，不许借 0x1018 的声明（不从协议族泛化）。</summary>
    [Theory]
    [InlineData(0x1014)]
    [InlineData(0x1016)]
    [InlineData(0x101C)]
    [InlineData(0x101D)]
    public void UnverifiedTransportsDeclareNothing(ushort pid)
        => Assert.True(MchoseDeviceCatalog.CapabilitiesOf(Dongle(pid)).IsEmpty);

    /// <summary>0x4D 本体 PID（K7 V2 四条）是**本体直连**路径，未真机验证，同样不声明。</summary>
    [Theory]
    [InlineData(0x4027)]
    [InlineData(0x4028)]
    [InlineData(0x403B)]
    [InlineData(0x403C)]
    public void UnverifiedDeviceEntriesDeclareNothing(ushort pid)
        => Assert.True(MchoseDeviceCatalog.CapabilitiesOf(Dongle(pid)).IsEmpty);
}
