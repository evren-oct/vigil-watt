using VigilWatt.Core.Plugins.Vgn;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： VgnDevicePairing —— 同一只鼠标"无线接收器 + 插线"两条接口的配对规则。
/// 2026/10/21
///
/// 真机样本（3554:FB56 无线 与 3554:FB57 插线，同一只 F2 大师+）：
/// 两者 Product 都是 <c>Dragonfly F2 Master+</c>，Serial 都是 <c>541505796617</c>。
/// </summary>
public sealed class VgnDevicePairingTests
{
    private const string WirelessPath = @"\\?\hid#vid_3554&pid_fb56&mi_01&col05#wireless";
    private const string WiredPath = @"\\?\hid#vid_3554&pid_fb57&mi_01&col05#wired";
    private const string Sn = "541505796617";

    [Fact]
    public void SameDeviceWithAndWithoutCableSkipsWireless()
    {
        var devices = new List<VgnInterfaceRef>
        {
            new(WirelessPath, 0x3554, 0xFB56, "Dragonfly F2 Master+", Sn),
            new(WiredPath, 0x3554, 0xFB57, "Dragonfly F2 Master+", Sn),
        };

        Assert.Equal(new[] { WirelessPath }, VgnDevicePairing.WirelessPathsToSkip(devices));
    }

    [Fact]
    public void WirelessOnlyIsNotSkipped()
    {
        var devices = new List<VgnInterfaceRef>
        {
            new(WirelessPath, 0x3554, 0xFB56, "Dragonfly F2 Master+", Sn),
        };
        Assert.Empty(VgnDevicePairing.WirelessPathsToSkip(devices));
    }

    [Fact]
    public void WiredOnlyIsNotSkipped()
    {
        var devices = new List<VgnInterfaceRef>
        {
            new(WiredPath, 0x3554, 0xFB57, "Dragonfly F2 Master+", Sn),
        };
        Assert.Empty(VgnDevicePairing.WirelessPathsToSkip(devices));
    }

    /// <summary>两台同型号鼠标：SN 不同，不能被合并（否则 B 机会凭空消失）。</summary>
    [Fact]
    public void TwoIdenticalMiceWithDifferentSerialsAreNotMerged()
    {
        var devices = new List<VgnInterfaceRef>
        {
            new(WirelessPath, 0x3554, 0xFB56, "Dragonfly F2 Master+", "111111111111"),
            new(WiredPath, 0x3554, 0xFB57, "Dragonfly F2 Master+", "222222222222"),
        };
        Assert.Empty(VgnDevicePairing.WirelessPathsToSkip(devices));
    }

    /// <summary>设备不报序列号时宁可多显示一行，也不能把两台设备合并成一台。</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("", Sn)]
    [InlineData(Sn, "")]
    public void MissingSerialNeverMerges(string wirelessSn, string wiredSn)
    {
        var devices = new List<VgnInterfaceRef>
        {
            new(WirelessPath, 0x3554, 0xFB56, "Dragonfly F2 Master+", wirelessSn),
            new(WiredPath, 0x3554, 0xFB57, "Dragonfly F2 Master+", wiredSn),
        };
        Assert.Empty(VgnDevicePairing.WirelessPathsToSkip(devices));
    }

    /// <summary>F1 V5 同理：391D:1A23 无线 / 391D:1026 插线。</summary>
    [Fact]
    public void F1FamilyAlsoPairs()
    {
        var devices = new List<VgnInterfaceRef>
        {
            new(@"\\?\hid#vid_391d&pid_1a23#wireless", 0x391D, 0x1A23, "Dragonfly F1 V5 Turbo+", Sn),
            new(@"\\?\hid#vid_391d&pid_1026#wired", 0x391D, 0x1026, "Dragonfly F1 V5 Turbo+", Sn),
        };
        Assert.Equal(new[] { @"\\?\hid#vid_391d&pid_1a23#wireless" },
            VgnDevicePairing.WirelessPathsToSkip(devices));
    }

    [Fact]
    public void OtherBrandsAreUntouched()
    {
        var devices = new List<VgnInterfaceRef>
        {
            new(@"\\?\hid#vid_05ac&pid_024f&mi_03#k", 0x05AC, 0x024F, "F108Pro Dongle", Sn),
            new(@"\\?\hid#vid_3554&pid_fb56#w", 0x3554, 0xFB56, "Dragonfly F2 Master+", Sn),
        };
        Assert.Empty(VgnDevicePairing.WirelessPathsToSkip(devices));
    }

    [Fact]
    public void SameSerialRequiresBothNonEmpty()
    {
        var a = new VgnInterfaceRef("a", 0x3554, 0xFB56, "x", "");
        var b = new VgnInterfaceRef("b", 0x3554, 0xFB57, "x", "");
        Assert.False(VgnDevicePairing.SameSerial(a, b));
        Assert.False(VgnDevicePairing.SameSerial(a, a));
        Assert.True(VgnDevicePairing.SameSerial(
            a with { Serial = Sn }, b with { Serial = Sn }));
    }
}
