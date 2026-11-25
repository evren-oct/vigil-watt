using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从主动查询白名单——已验证型号保持查询，未登记/未验证接口一律拒绝。
/// 2026/11/24 22:35:00 20211
/// 白名单来自 mchose-catalog.json 的 capabilities.batteryRead，见 docs/hardware/safety.md §3。
/// </summary>
public sealed class MchoseActiveQueryPolicyTests
{
    [Fact]
    public void VerifiedKeyboardV1_RemainsAllowed()
    {
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x41E4, 0x2001, 0xFF02, 0x0002, 20, 20, 0, 0x13), "keyboard-v1"));
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x201B, 0xFF02, 0x0002, 20, 20, 0, 0x13), "keyboard-v1"));
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x3554, 0xFA09, 0xFF02, 0x0002, 20, 20, 0, 0x13), "keyboard-v1"));
    }

    [Fact]
    public void VerifiedCzAndV2_RemainAllowed()
    {
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x3033, 0xFF31, 0x0074, 65, 65, 0, 0x00), "cz-block"));
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x100A, 0xFF00, 0x0002, 65, 65, 0, 0x11), "mouse-v2"));
    }

    [Fact]
    public void Verified0x4D_GmouseAndHeadsets_RemainAllowed()
    {
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x1018, 0xFF01, 0x0001, 64, 64, 0, 0x4D), "mchose-0x4d"));
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x4245, 0xFF01, 0x0010, 65, 65, 0, 0x00), "gmouse"));
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x291D, 0x385D, 0xFF90, 0x0001, 64, 64, 0, 0x55), "headset-v9"));
        Assert.True(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x6045, 0xFF90, 0x0001, 64, 64, 0, 0x55), "headset-x9"));
    }

    [Fact]
    public void UnregisteredMouseV2_AllActiveProtocolsDenied()
    {
        var info = Info(0x3837, 0x4099, 0xFF00, 0x0002, 64, 64, 0, 0x4D);
        Assert.False(MchoseActiveQueryPolicy.Allows(info, "mouse-v2"));
        Assert.False(MchoseActiveQueryPolicy.Allows(info, "mchose-0x4d"));
        Assert.False(MchoseActiveQueryPolicy.Allows(info, "qhw"));
    }

    [Fact]
    public void UnverifiedTransportAndCatalogDevices_AreDenied()
    {
        Assert.False(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x1014, 0xFF01, 0x0001, 64, 64, 0, 0x4D), "mchose-0x4d"));
        Assert.False(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x2019, 0xFF00, 0x0001, 64, 64, 0, 0x4D), "qhw"));
        Assert.False(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x4028, 0xFF01, 0x0001, 64, 64, 0, 0x4D), "mchose-0x4d"));
        Assert.False(MchoseActiveQueryPolicy.Allows(
            Info(0x3837, 0x100C, 0xFF01, 0x0001, 0, 0, 21, 0x00), "mouse-a5"));
    }

    [Fact]
    public void ProtocolMismatch_IsDenied()
    {
        var cz = Info(0x3837, 0x3033, 0xFF31, 0x0074, 65, 65, 0, 0x00);
        Assert.False(MchoseActiveQueryPolicy.Allows(cz, "mchose-0x4d"));
        Assert.False(MchoseActiveQueryPolicy.Allows(cz, "mouse-v2"));
    }

    private static HidDeviceInfo Info(
        ushort vid, ushort pid, ushort page, ushort usage,
        ushort input, ushort output, ushort feature, byte reportId)
        => new()
        {
            DevicePath = $"test-{vid:X4}-{pid:X4}-{page:X4}-{usage:X4}-{input}-{output}-{feature}",
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = usage,
            InputReportLength = input,
            OutputReportLength = output,
            FeatureReportLength = feature,
            OutputReportId = reportId,
        };
}
