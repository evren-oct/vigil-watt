using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从设备身份（0x0900 的 [0..3] = 配对本体 VID/PID）与传输通道语义的回归测试。
/// 2026/11/08 20:05:00 xliu
/// 帧样本取自 mchose.md §3.3 真机应答；型号与通道映射见 mchose-catalog.json。
/// </summary>
public sealed class MchoseIdentityTests
{
    /// <summary>真机 0x0900 应答：payload[0..3] = 37 38 26 40 → VID 0x3837、PID 0x4026。</summary>
    private const string DeviceInfoReply =
        "4D01011000090000" + "373826400400000000100200640008E4" + "EF";

    /// <summary>真机常态应答：鼠标在睡，只回 1 字节 0xFF（payload 长度 1）。</summary>
    private const string SleepingReply = "4D010101000900" + "00FF";

    private static byte[] Frame(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        var frame = new byte[Math.Max(Mchose0x4DCodec.FrameLength, bytes.Length)];
        bytes.CopyTo(frame, 0);
        return frame;
    }

    private static HidDeviceInfo Usb(ushort vendorId, ushort productId) => new()
    {
        DevicePath = @"\\?\hid#vid_test&pid_test#test",
        VendorId = vendorId,
        ProductId = productId,
    };

    /// <summary>真机帧：VID/PID 按小端解析。</summary>
    [Fact]
    public void ReportedIdentityIsLittleEndian()
    {
        Assert.True(Mchose0x4DCodec.TryParseDeviceIdentity(Frame(DeviceInfoReply), out ushort vid, out ushort pid));

        Assert.Equal(0x3837, vid);
        Assert.Equal(0x4026, pid);
    }

    /// <summary>字节序写反必须得到不同的值，证明不是"按大端碰巧也能读"。</summary>
    [Fact]
    public void ByteOrderIsLittleEndianNotBigEndian()
    {
        var swapped = "4D01011000090000" + "383740260400000000100200640008E4" + "00";

        Assert.True(Mchose0x4DCodec.TryParseDeviceIdentity(Frame(swapped), out ushort vid, out ushort pid));

        Assert.Equal(0x3738, vid);
        Assert.Equal(0x2640, pid);
    }

    /// <summary>长度不足 4 不产生身份：设备在睡只回 1 字节，或声明长度只有 3。</summary>
    [Theory]
    [InlineData(SleepingReply)]
    [InlineData("4D01010300090000" + "373826" + "00")]   // 声明 len=3，装不下 PID
    public void ShortPayloadHasNoIdentity(string hex)
    {
        Assert.False(Mchose0x4DCodec.TryParseDeviceIdentity(Frame(hex), out ushort vid, out ushort pid));
        Assert.Equal(0, vid);
        Assert.Equal(0, pid);
    }

    /// <summary>USB 枚举身份（dongle）与协议自报身份（本体）各存各的，互不覆盖。</summary>
    [Fact]
    public void UsbAndReportedIdentityCoexist()
    {
        var identity = MchoseDeviceIdentity.FromUsb(Usb(0x3837, 0x1018))
            .WithReported(0x3837, 0x4026, "K7 V2 Ultra+");

        Assert.Equal(0x3837, identity.UsbVendorId);
        Assert.Equal(0x1018, identity.UsbProductId);
        Assert.Equal(0x1018, identity.TransportPid);
        Assert.Equal((ushort?)0x3837, identity.ReportedVendorId);
        Assert.Equal((ushort?)0x4026, identity.ReportedProductId);
        Assert.Equal("K7 V2 Ultra+", identity.Model);
        Assert.Equal(MchoseIdentityStatus.ReportedResolved, identity.Status);
        Assert.True(identity.HasReported);
    }

    /// <summary>只有 USB 身份时，状态必须是 Unknown、型号留空——不能拿 dongle PID 冒充型号。</summary>
    [Fact]
    public void UsbOnlyIdentityHasNoModel()
    {
        var identity = MchoseDeviceIdentity.FromUsb(Usb(0x3837, 0x1018));

        Assert.False(identity.HasReported);
        Assert.Empty(identity.Model);
        Assert.Equal(MchoseIdentityStatus.Unknown, identity.Status);
        Assert.Equal(0x1018, identity.TransportPid);
    }

    /// <summary>通用 dongle PID 只是通道：目录里查不到型号，自报它自己也只能是 Unresolved。</summary>
    [Fact]
    public void GenericDonglePidIsNotAModel()
    {
        Assert.False(MchoseDeviceCatalog.TryGet(0x3837, 0x1018, out _));
        Assert.True(MchoseDeviceCatalog.TryGetTransport(0x3837, 0x1018, out var transport));
        Assert.Equal("dongle-wireless", transport.Role);

        var identity = MchoseDeviceIdentity.FromUsb(Usb(0x3837, 0x1018))
            .WithReported(0x3837, 0x1018, null);

        Assert.Equal(MchoseIdentityStatus.ReportedUnresolved, identity.Status);
        Assert.Empty(identity.Model);
    }

    /// <summary>目录未收录的 PID 不推断型号。</summary>
    [Fact]
    public void UnknownPidDoesNotInferModel()
    {
        Assert.False(MchoseDeviceCatalog.TryGet(0x3837, 0x4099, out _));

        var identity = MchoseDeviceIdentity.FromUsb(Usb(0x3837, 0x4099))
            .WithReported(0x3837, 0x4099, null);

        Assert.Equal(MchoseIdentityStatus.ReportedUnresolved, identity.Status);
        Assert.Empty(identity.Model);
    }

    /// <summary>
    /// 真机样本的配对本体是 0x4026 → 目录里是 A5 V3 Ultra+，**不是** K7 V2 Ultra+（0x4028）。
    /// 这条钉住审计结论：同一个 dongle PID 不能反推型号（mchose.md §3.3）。
    /// </summary>
    [Fact]
    public void K7SampleResolvesToA5V3UltraPlusNotK7()
    {
        Assert.True(Mchose0x4DCodec.TryParseDeviceIdentity(Frame(DeviceInfoReply), out ushort vid, out ushort pid));

        Assert.True(MchoseDeviceCatalog.TryGet(vid, pid, out var entry));
        Assert.Equal("A5 V3 Ultra+", entry.Model);

        Assert.True(MchoseDeviceCatalog.TryGet(0x3837, 0x4028, out var k7));
        Assert.Equal("K7 V2 Ultra+", k7.Model);
    }

    /// <summary>CZ 通用 dongle 也是传输通道，不是型号。</summary>
    [Fact]
    public void CzDongleIsATransportNotAModel()
    {
        Assert.False(MchoseDeviceCatalog.TryGet(0x3837, 0x3033, out _));
        Assert.True(MchoseDeviceCatalog.TryGetTransport(0x3837, 0x3033, out var transport));
        Assert.Equal("dongle-cz", transport.Role);
    }

    /// <summary>
    /// 认领路由跟着目录走：`0x1018` 走精确指纹；其余 0x4D 传输 PID 走目录登记 + 宽松形态；
    /// 非 0x4D 的通道（A5 / CZ）与标准键盘页不得被误认。
    /// </summary>
    [Fact]
    public void ClaimRoutesFollowCatalogTransports()
    {
        const string path = @"\\?\hid#vid_test&pid_test&mi_02&col02#test";

        // 0x1018：唯一有真机指纹的一颗
        Assert.Equal(MchoseInterfaceKind.Mouse0x4D,
            MchoseInterfaceClassifier.Classify(0x3837, 0x1018, path, 0xFF01, 0x0001, 64, 64, 0, 0x4D));

        // 0x1014 / 0x1016 / 0x101C / 0x101D：目录登记为 mchose-0x4d，按厂商页 + 64/64 认
        foreach (ushort pid in new ushort[] { 0x1014, 0x1016, 0x101C, 0x101D })
            Assert.Equal(MchoseInterfaceKind.Mouse0x4D,
                MchoseInterfaceClassifier.Classify(0x3837, pid, path, 0xFF01, 0x0001, 64, 64, 0, 0x4D));

        // 同 PID 但落在标准键盘页：不是厂商通道，不能认成 0x4D
        Assert.Equal(MchoseInterfaceKind.None,
            MchoseInterfaceClassifier.Classify(0x3837, 0x1014, path, 0x0001, 0x0006, 20, 20, 0, 0x00));

        // A5 的 Feature 通道仍归 MchoseA5Plugin，不能被 0x4D 抢走
        Assert.Equal(MchoseInterfaceKind.MouseA5,
            MchoseInterfaceClassifier.Classify(0x3837, 0x100C, path, 0xFF01, 0x0001, 0, 0, 21, 0x00));

        // CZ 通用 dongle 仍按 ≥60 字节兜底（它不认 0x4D，走 cz-block）
        Assert.Equal(MchoseInterfaceKind.MouseV2,
            MchoseInterfaceClassifier.Classify(0x3837, 0x3033, path, 0xFF00, 0x0002, 65, 65, 0, 0x4D));

        // 本体 0x4028 靠目录命中，判据不变
        Assert.Equal(MchoseInterfaceKind.Mouse0x4D,
            MchoseInterfaceClassifier.Classify(0x3837, 0x4028, path, 0xFF01, 0x0001, 64, 64, 0, 0x4D));
    }
}
