using System.Text.Json;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Settings;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 验证设备别名持久化、用户配置保留及失败回滚。
/// 2026/10/24 14:10:40 Administrator
/// </summary>
public sealed class DeviceIdentityMigrationTests
{
    [Fact]
    public void Migration_PreservesCustomizationAndRemovalFlags_AndIsIdempotent()
    {
        var settings = new AppSettings();
        var custom = new DeviceCustomization { Name = "我的鼠标" };
        settings.DeviceCustomizations["old"] = custom;
        settings.BlockedDevices["old"] = new BlockedDevice { Name = "鼠标" };
        settings.DismissedDevices["old"] = 123;
        int saved = 0;
        Assert.True(DeviceIdentityMigration.TryApply(settings, "unit", new[] { "old" }, () => { saved++; return true; }));
        Assert.Equal("unit", settings.CanonicalDeviceId("old"));
        Assert.Same(custom, settings.DeviceCustomizations["unit"]);
        Assert.True(settings.BlockedDevices.ContainsKey("unit"));
        Assert.Equal(123, settings.DismissedDevices["unit"]);
        Assert.False(settings.DeviceCustomizations.ContainsKey("old"));
        Assert.True(DeviceIdentityMigration.TryApply(settings, "unit", new[] { "old" }, () => { saved++; return true; }));
        Assert.Equal(1, saved);
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("unit", restored.CanonicalDeviceId("old"));
        Assert.Equal("我的鼠标", restored.DeviceCustomizations["unit"].Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSave_RestoresAllOriginalEntries_AndDoesNotLeaveAliases(bool throws)
    {
        var settings = new AppSettings();
        var custom = new DeviceCustomization { Name = "保留" };
        settings.DeviceCustomizations["old"] = custom;
        settings.DismissedDevices["old"] = 1;
        settings.BlockedDevices["old"] = new BlockedDevice { Name = "保留屏蔽" };
        if (throws)
            Assert.Throws<IOException>(() => DeviceIdentityMigration.TryApply(settings, "unit",
                new[] { "old", "other" }, () => throw new IOException("test failure")));
        else
            Assert.False(DeviceIdentityMigration.TryApply(settings, "unit", new[] { "old", "other" }, () => false));
        Assert.Empty(settings.DeviceAliases);
        Assert.Same(custom, settings.DeviceCustomizations["old"]);
        Assert.False(settings.DeviceCustomizations.ContainsKey("unit"));
        Assert.Equal(1, settings.DismissedDevices["old"]);
        Assert.False(settings.DismissedDevices.ContainsKey("unit"));
        Assert.True(settings.BlockedDevices.ContainsKey("old"));
        Assert.False(settings.BlockedDevices.ContainsKey("unit"));
    }

    [Fact]
    public void AliasConflict_DoesNotCombineDifferentPhysicalUnits()
    {
        var settings = new AppSettings();
        settings.DeviceAliases["old"] = "first-unit";
        Assert.Throws<InvalidOperationException>(() => DeviceIdentityMigration.TryApply(
            settings, "second-unit", new[] { "old" }, () => true));
        Assert.Equal("first-unit", settings.CanonicalDeviceId("old"));
        Assert.Single(settings.DeviceAliases);
    }

    [Fact]
    public void ExistingCanonicalCustomization_TakesPrecedence_AndOldJsonStillLoads()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"DeviceCustomizations":{"old":{"Name":"旧名称"},"unit":{"Name":"新名称"}}}
            """)!;
        Assert.True(DeviceIdentityMigration.TryApply(settings, "unit", new[] { "old" }, () => true));
        Assert.Equal("新名称", settings.DeviceCustomizations["unit"].Name);
    }

    [Fact]
    public void IdentityBecomingAvailable_CanUpgradeFallbackWithoutAliasChains()
    {
        var settings = new AppSettings();
        settings.DeviceCustomizations["usb:1"] = new DeviceCustomization { Name = "保留名称" };
        Assert.True(DeviceIdentityMigration.TryApply(settings, "usb:255", new[] { "usb:1", "usb:6" }, () => true));
        Assert.True(DeviceIdentityMigration.TryApply(settings, "unit", new[] { "usb:255" }, () => true));
        Assert.Equal("unit", settings.CanonicalDeviceId("usb:1"));
        Assert.Equal("unit", settings.CanonicalDeviceId("usb:6"));
        Assert.Equal("unit", settings.CanonicalDeviceId("usb:255"));
        Assert.Equal("保留名称", settings.DeviceCustomizations["unit"].Name);
    }
}
