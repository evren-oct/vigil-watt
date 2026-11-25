using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using VigilWatt.Core.Plugins.Rapoo;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 雷柏有线/无线归一与全系指针遥测——同一只鼠标两行、DPI/回报率只对 VT3 生效这两个回归。
/// 2026/11/16 12:00:00 20211
/// 协议级字段见 docs/hardware/brands/rapoo.md §1.1；配对规则见 §4.3。
/// </summary>
public sealed class RapooIdentityTests
{
    private const string ProtocolsResource = "VigilWatt.Core.Plugins.Rapoo.specs.rapoo.protocols.json";
    private const string DevicesResource = "VigilWatt.Core.Plugins.Rapoo.rapoo-catalog.json";

    private static readonly ProtocolEngine Engine = ProtocolEngine.Load(ProtocolsResource, DevicesResource);

    /// <summary>真机 VT3：档位 4、DPI 3200×3200、回报率码 1 = 1000Hz。</summary>
    private static readonly byte[] NormalFrame =
        Convert.FromHexString("071004800C800C015E000301001246010202C8");

    private static HidDeviceInfo Info(ushort pid, string? serial = null, ushort inputLength = 19)
        => new()
        {
            DevicePath = $@"\\?\hid#vid_24ae&pid_{pid:x4}#test",
            VendorId = 0x24AE,
            ProductId = pid,
            SerialNumber = serial ?? "",
            UsagePage = 0xFF00,
            Usage = 2,
            InputReportLength = inputLength,
        };

    private static DeviceRef Dev(ushort pid)
        => new("rapoo:test", $@"\\?\hid#vid_24ae&pid_{pid:x4}#test", 0x24AE, pid);

    [Fact]
    public void R45_同SN的有线与无线归一同一ID()
    {
        var wired = RapooDeviceIdentity.StableId(Engine, Info(0x4612, "2603-54LM20"), "rapoo");
        var wireless = RapooDeviceIdentity.StableId(Engine, Info(0x1412, "2603-54LM20"), "rapoo");

        Assert.NotNull(wired);
        Assert.Equal(wired, wireless);
    }

    [Fact]
    public void R46_不同SN或没有SN时不被并成一条()
    {
        var first = RapooDeviceIdentity.StableId(Engine, Info(0x1412, "SN-A"), "rapoo");
        var second = RapooDeviceIdentity.StableId(Engine, Info(0x1412, "SN-B"), "rapoo");

        Assert.NotEqual(first, second);
        Assert.Null(RapooDeviceIdentity.StableId(Engine, Info(0x1412), "rapoo"));
    }

    [Fact]
    public void R47_配对组键与有线标记来自官方PID顺序()
    {
        var wired = Engine.DeviceOf(Info(0x4612))!;
        var wireless = Engine.DeviceOf(Info(0x1412))!;

        Assert.True(wired.Wired);
        Assert.False(wireless.Wired);
        Assert.Equal(RapooDeviceIdentity.GroupKey(wired), RapooDeviceIdentity.GroupKey(wireless));

        // 反例：V700diy-75 LRV 的有线 PID 比无线大，不能按大小猜
        Assert.True(Engine.DeviceOf(Info(0x6204))!.Wired);
        Assert.False(Engine.DeviceOf(Info(0x7204))!.Wired);
    }

    [Fact]
    public void R48_有线可认领时摘掉同组无线接口()
    {
        var wired = Info(0x4612, "2603-54LM20");
        var wireless = Info(0x1412, "2603-54LM20");

        var skip = RapooDeviceIdentity.WirelessPathsToSkip(Engine, [wired, wireless]);
        Assert.Contains(wireless.DevicePath, skip);
        Assert.DoesNotContain(wired.DevicePath, skip);
    }

    [Fact]
    public void R49_有线接口认领不了时不能摘掉能读的接收器()
    {
        var wiredOnlyMouseCollection = Info(0x4612, "2603-54LM20", inputLength: 8);
        var wireless = Info(0x1412, "2603-54LM20");

        var skip = RapooDeviceIdentity.WirelessPathsToSkip(Engine, [wiredOnlyMouseCollection, wireless]);
        Assert.Empty(skip);
    }

    [Fact]
    public void R50_协议级遥测对同族其他型号也生效()
    {
        var r = Engine.Parse(Dev(0x1417), "status-report7", NormalFrame);

        Assert.NotNull(r);
        Assert.Equal(3200, r!.Value.Pointer.DpiX.Value);
        Assert.Equal(3200, r.Value.Pointer.DpiY.Value);
        Assert.Equal(4, r.Value.Pointer.DpiLevel.Value);
        Assert.Equal(1000, r.Value.Pointer.ReportRateSettingHz.Value);
        Assert.Equal(TelemetrySource.DeviceReported, r.Value.Pointer.DpiX.Source);
    }

    [Fact]
    public void R51_未收录型号也按同一套心跳解析遥测()
    {
        var r = Engine.Parse(Dev(0x9999), "status-report7", NormalFrame);
        Assert.Equal(3200, r!.Value.Pointer.DpiX.Value);
    }

    [Fact]
    public void R52_DPI上限放到60000不挡高档鼠标()
    {
        var frame = (byte[])NormalFrame.Clone();
        frame[3] = 0x60; frame[4] = 0xEA;   // 60000
        frame[5] = 0x60; frame[6] = 0xEA;

        var r = Engine.Parse(Dev(0x1412), "status-report7", frame);
        Assert.Equal(60000, r!.Value.Pointer.DpiX.Value);
    }

    [Fact]
    public void R53_未知回报率码不冒充有值()
    {
        var frame = (byte[])NormalFrame.Clone();
        frame[11] = 99;

        var r = Engine.Parse(Dev(0x1412), "status-report7", frame);
        Assert.False(r!.Value.Pointer.ReportRateSettingHz.HasValue);
    }
}
