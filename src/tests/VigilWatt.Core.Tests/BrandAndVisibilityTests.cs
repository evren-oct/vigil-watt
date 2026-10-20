using System.Text.Json;
using VigilWatt.App.Services;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Plugins.Mchose;
using VigilWatt.Core.Settings;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 品牌开关迁移、精确设备匹配与隐藏窗口入口的回归测试。
/// 2026/10/19 21:11:40 kaniu
/// </summary>
public class BrandAndVisibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MigrationPreservesBrandPermissionsAndRemovesObsoleteToggles(bool brandEnabled)
    {
        var settings = new AppSettings
        {
            PluginEnabled = new() { ["Mchose"] = brandEnabled, ["Rapoo"] = false, ["MchoseMouse"] = true, ["Other"] = true }
        };
        settings.NormalizePluginSettings();
        Assert.Equal(brandEnabled, settings.PluginEnabled["Mchose"]);
        Assert.False(settings.PluginEnabled.ContainsKey("MchoseMouse"));
        Assert.False(settings.PluginEnabled.ContainsKey("Other"));
        Assert.False(settings.PluginEnabled["Rapoo"]);
        Assert.False(settings.PluginEnabled["Cherry"]);
        Assert.False(settings.PluginEnabled["Razer"]);
        settings.NormalizePluginSettings();
        Assert.Equal(brandEnabled, settings.PluginEnabled["Mchose"]);
        Assert.False(settings.PluginEnabled["Rapoo"]);
        Assert.False(settings.PluginEnabled.ContainsKey("Other"));
    }

    [Fact]
    public void ModelPermissionAloneNeverEnablesBrandWrites()
    {
        var settings = new AppSettings { PluginEnabled = new() { ["MchoseMouse"] = true } };
        settings.NormalizePluginSettings();
        Assert.False(settings.PluginEnabled["Mchose"]);
    }

    [Theory]
    [InlineData("mchosemouse:ABC", "Mchose")]
    [InlineData("MCHOSEMOUSE:ABC", "Mchose")]
    [InlineData("mchose:ABC", "mchose")]
    [InlineData("haute:ABC", "haute")]
    [InlineData("", "")]
    [InlineData("ABC", "")]
    public void LegacyDeviceIdsResolveToTheirBrand(string id, string owner)
        => Assert.Equal(owner, PluginUtils.OwnerOf(id));

    [Fact]
    public void TrayVisibilitySurvivesJsonRoundTripIndependentlyOfPill()
    {
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{}")!.TrayIconEnabled);
        var original = new AppSettings { TrayIconEnabled = false, TaskbarBatteryEnabled = true };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original))!;
        Assert.False(loaded.TrayIconEnabled);
        Assert.True(loaded.TaskbarBatteryEnabled);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public void ABackgroundEntryIsRequiredToHideWindow(bool tray, bool pill, bool widget, bool expected)
        => Assert.Equal(expected, AppVisibilityPolicy.CanHideMainWindow(new AppSettings
        { TrayIconEnabled = tray, TaskbarBatteryEnabled = pill, WidgetEnabled = widget }));

    private static HidDeviceInfo Device(ushort vid, ushort pid, ushort page = 0xFF01,
        ushort usage = 1, ushort length = 64, ushort feature = 65)
        => new() { DevicePath = "test", VendorId = vid, ProductId = pid, UsagePage = page,
            Usage = usage, InputReportLength = length, OutputReportLength = length, FeatureReportLength = feature };

    [Fact]
    public void MouseRequiresItsConfigurationCollectionAndFeatureLength()
    {
        Assert.True(MchoseMouseProtocol.IsCandidate(Device(0x3837, 0x100C)));
        Assert.False(MchoseMouseProtocol.IsCandidate(Device(0x3837, 0x100C, page: 0xFF0B, usage: 0x104)));
        Assert.False(MchoseMouseProtocol.IsCandidate(Device(0x3837, 0x100C, feature: 20)));
        Assert.False(MchoseMouseProtocol.IsCandidate(Device(0x3837, 0x3033)));
    }

    [Fact]
    public async Task DisabledMousePermissionReturnsBeforeTouchingNativeHandle()
    {
        Assert.Null(await MchoseMouseProtocol.QueryAsync(null!, false));
    }
}
