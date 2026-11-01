using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Rapoo;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： RapooDeviceCatalog —— 内嵌雷柏设备目录（VID/PID → 型号 + 类型 + 协议族）的加载与取值。
/// 2026/11/01 22:20:00 xliu
/// 数据来自官方 AHUB 2.0 内嵌 manifest，见 docs/hardware/rapoo-catalog.md §1、§3。
/// </summary>
public sealed class RapooDeviceCatalogTests
{
    /// <summary>官方主包解析出的 195 条 VID:PID；数字变了说明目录被重抓过，需要同步文档。</summary>
    private const int ExpectedEntries = 195;

    [Fact]
    public void 目录条数与官方解析结果一致()
        => Assert.Equal(ExpectedEntries, RapooDeviceCatalog.Count);

    [Fact]
    public void 本机VT3按无线PID命中型号与协议族()
    {
        Assert.True(RapooDeviceCatalog.TryGet(0x24AE, 0x1412, out var entry));
        Assert.Equal("VT3", entry.Model);
        Assert.Equal(DeviceKind.Mouse, entry.Kind);
        Assert.Equal("status-report7", entry.Protocol);
        // 有线 PID 是配对的那条：同一只鼠标两个 PID，插线/无线各枚举一次（rapoo.md §4.3）
        Assert.Equal(0x4612, entry.PairedPid);
    }

    [Fact]
    public void VT2SAirMaxV2两条PID互相指向()
    {
        Assert.True(RapooDeviceCatalog.TryGet(0x24AE, 0x1B13, out var wireless));
        Assert.True(RapooDeviceCatalog.TryGet(0x24AE, 0x4B13, out var wired));
        Assert.Equal("VT2S Air MAX V2", wireless.Model);
        Assert.Equal("VT2S Air MAX V2", wired.Model);
        Assert.Equal(0x4B13, wireless.PairedPid);
        Assert.Equal(0x1B13, wired.PairedPid);
    }

    [Fact]
    public void 底座收录但类型回落Unknown()
    {
        Assert.True(RapooDeviceCatalog.TryGet(0x24AE, 0x4827, out var entry));
        Assert.Equal("P5 PLUS", entry.Model);
        // 底座没有电量通道，DeviceKind 里也没有 base；认不认领由协议族决定（rapoo-catalog.md §8）
        Assert.Equal(DeviceKind.Unknown, entry.Kind);
        Assert.Equal("telink-base", entry.Protocol);
    }

    [Fact]
    public void 键盘按目录给Keyboard()
    {
        Assert.True(RapooDeviceCatalog.TryGet(0x24AE, 0x4814, out var entry));
        Assert.Equal("A83 Air", entry.Model);
        Assert.Equal(DeviceKind.Keyboard, entry.Kind);
        Assert.Equal("telink-keyboard", entry.Protocol);
    }

    [Fact]
    public void 未收录的PID返回false()
        => Assert.False(RapooDeviceCatalog.TryGet(0x24AE, 0xFFFF, out _));

    [Fact]
    public void 其它厂商的相同PID不会被误命中()
        => Assert.False(RapooDeviceCatalog.TryGet(0x3837, 0x1412, out _));

    /// <summary>目录里的协议族名必须落在已知集合内——防止重抓主包时把拼写差异带进代码。</summary>
    [Theory]
    [InlineData(0x24AE, 0x1412, "status-report7")]
    [InlineData(0x24AE, 0x1445, "status-report7-base")]
    [InlineData(0x24AE, 0x4506, "telink-8273")]
    [InlineData(0x24AE, 0x482A, "ch585m")]
    [InlineData(0x24AE, 0x4A11, "vt-ch584")]
    [InlineData(0x24AE, 0x4B20, "lm20rt")]
    [InlineData(0x24AE, 0x4672, "telink-3228c")]
    [InlineData(0x24AE, 0x4832, "gcome-ap")]
    [InlineData(0x2E3C, 0x5745, "ohid")]
    [InlineData(0x24AE, 0x4825, "telink-base")]
    public void 协议族取值与官方driver对应(ushort vid, ushort pid, string protocol)
    {
        Assert.True(RapooDeviceCatalog.TryGet(vid, pid, out var entry));
        Assert.Equal(protocol, entry.Protocol);
    }
}
