using Xunit;
using VigilWatt.App.Services;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 任务栏贴片「选择显示设备」的过滤规则。
/// 重点覆盖两类边界：**空选择 = 不过滤**（不是"什么都不显示"）、
/// 以及**名字大小写/空白不敏感**（设备名来自 HID 报文，大小写不固定）。
/// </summary>
public class TaskbarDeviceFilterTests
{
    private static readonly string[] Devices =
    {
        "狼蛛 F108 Pro", "VGN V1 鼠标", "雷柏 VT30",
    };

    [Fact]
    public void 空选择_不过滤_返回全部()
    {
        var result = TaskbarDeviceFilter.Apply(Devices, Array.Empty<string>());
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void null选择_不过滤_返回全部()
    {
        var result = TaskbarDeviceFilter.Apply(Devices, null);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void 选择全为空白_视为没选_返回全部()
    {
        var result = TaskbarDeviceFilter.Apply(Devices, new[] { "", "   " });
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void 选中一台_只返回那一台()
    {
        var result = TaskbarDeviceFilter.Apply(Devices, new[] { "VGN V1 鼠标" });
        Assert.Single(result);
        Assert.Equal("VGN V1 鼠标", result[0]);
    }

    [Fact]
    public void 选中多台_保持原顺序()
    {
        var result = TaskbarDeviceFilter.Apply(Devices, new[] { "雷柏 VT30", "狼蛛 F108 Pro" });
        Assert.Equal(2, result.Count);
        // 顺序跟设备列表走，不跟选择顺序走——贴片的排列是按电量排的，不该被勾选顺序打乱
        Assert.Equal("狼蛛 F108 Pro", result[0]);
        Assert.Equal("雷柏 VT30", result[1]);
    }

    [Fact]
    public void 名字大小写不同_仍能匹配()
    {
        var result = TaskbarDeviceFilter.Apply(Devices, new[] { "vgn v1 鼠标" });
        Assert.Single(result);
    }

    [Fact]
    public void 选中不存在的设备_返回空()
    {
        var result = TaskbarDeviceFilter.Apply(Devices, new[] { "不存在的设备" });
        Assert.Empty(result);
    }

    [Fact]
    public void 选中项含空串_空串不误伤其他设备()
    {
        var result = TaskbarDeviceFilter.Apply(Devices, new[] { "", "VGN V1 鼠标" });
        Assert.Single(result);
    }

    [Fact]
    public void 设备列表为空_返回空()
    {
        var result = TaskbarDeviceFilter.Apply(Array.Empty<string>(), new[] { "VGN V1 鼠标" });
        Assert.Empty(result);
    }

    [Fact]
    public void 不修改传入的设备列表()
    {
        var source = new List<string>(Devices);
        TaskbarDeviceFilter.Apply(source, new[] { "VGN V1 鼠标" });
        Assert.Equal(3, source.Count);
    }
}
