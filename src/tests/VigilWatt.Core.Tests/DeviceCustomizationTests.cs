using VigilWatt.Core.Models;
using VigilWatt.Core.Settings;
using System.Text.Json;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 覆盖设备自定义值解析及原始状态隔离。
/// 2026/10/18 12:19:45 20211
/// </summary>
public sealed class DeviceCustomizationTests
{
    [Fact]
    public void Apply_OverridesPresentationWithoutMutatingSource()
    {
        var source = new DeviceStatus
        {
            Id = "ble:device-1", Name = "原始名称", Source = DeviceSource.Ble, Kind = DeviceKind.Headset,
        };
        var customizations = new Dictionary<string, DeviceCustomization>
        {
            [source.Id] = new() { Name = "书房耳机", IconKind = DeviceKind.Unknown },
        };

        var result = DeviceCustomizationResolver.Apply(source, customizations);

        Assert.NotSame(source, result);
        Assert.Equal("书房耳机", result.DisplayName);
        Assert.Equal(DeviceKind.Unknown, result.Kind);
        Assert.Equal("原始名称", source.DisplayName);
        Assert.Equal(DeviceKind.Headset, source.Kind);
    }

    [Fact]
    public void DisplayName_UsesOverrideAndFallsBackForUncustomizedDevice()
    {
        var customizations = new Dictionary<string, DeviceCustomization>
        {
            ["device-1"] = new() { Name = "桌面鼠标" },
        };

        Assert.Equal("桌面鼠标", DeviceCustomizationResolver.DisplayName("device-1", "Mouse", customizations));
        Assert.Equal("Keyboard", DeviceCustomizationResolver.DisplayName("device-2", "Keyboard", customizations));
    }

    [Fact]
    public void ApplyName_KeepsOriginalDeviceKindForHistorySamples()
    {
        var source = new DeviceStatus
        {
            Id = "device-1", Name = "Headset", Source = DeviceSource.Ble, Kind = DeviceKind.Headset,
        };
        var customizations = new Dictionary<string, DeviceCustomization>
        {
            [source.Id] = new() { Name = "Desk", IconKind = DeviceKind.Mouse },
        };

        var result = DeviceCustomizationResolver.ApplyName(source, customizations);

        Assert.Equal("Desk", result.Name);
        Assert.Equal(DeviceKind.Headset, result.Kind);
    }

    [Fact]
    public void ImageKey_UsesOverrideAndFallsBack()
    {
        var customizations = new Dictionary<string, DeviceCustomization>
        {
            ["device-1"] = new() { ImageKey = "color-pink-side" },
            ["device-2"] = new() { ImageKey = "  " },
        };

        Assert.Equal("color-pink-side", DeviceCustomizationResolver.ImageKey("device-1", customizations));
        Assert.Null(DeviceCustomizationResolver.ImageKey("device-2", customizations));
        Assert.Null(DeviceCustomizationResolver.ImageKey("device-3", customizations));
    }

    [Fact]
    public void Customization_RoundTripsThroughSettingsJsonShape()
    {
        var value = new Dictionary<string, DeviceCustomization>
        {
            ["device-1"] = new()
            {
                Name = "Desk", IconKind = DeviceKind.Mouse, ImageKey = "color-pink-side",
                OriginalName = "MX Master 3S",
            },
        };

        var json = JsonSerializer.Serialize(value);
        var restored = JsonSerializer.Deserialize<Dictionary<string, DeviceCustomization>>(json)!;

        Assert.Equal("Desk", restored["device-1"].Name);
        Assert.Equal(DeviceKind.Mouse, restored["device-1"].IconKind);
        Assert.Equal("color-pink-side", restored["device-1"].ImageKey);
        Assert.Equal("MX Master 3S", restored["device-1"].OriginalName);
    }
}
