using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 验证罗技充电通道切换、身份校验及旧槽位映射。
/// 2026/10/24 14:10:40 Administrator
/// </summary>
public sealed class HidppDeviceIdentityTests
{
    private static readonly byte[] DeviceInfo = { 0x09, 0x0B, 0x01, 0x12, 0x34, 0x56, 0x78, 0x00,
        0x0C, 0x40, 0x0A, 0xC0, 0x9A, 0x00, 0x00 };

    [Theory]
    [InlineData(0xC09A, false)]
    [InlineData(0xC543, true)]
    [InlineData(0xC548, true)]
    [InlineData(0xC32B, false)]
    public void Discovery_DistinguishesUsbDeviceFromReceiver(int pid, bool receiver)
        => Assert.Equal(receiver, HidppCatalog.IsReceiver((ushort)pid));

    [Fact]
    public void Pro2Receiver_HasOneSlot_AndBoltRetainsSix()
    {
        Assert.Equal(1, HidppCatalog.MaxSlots(0xC543));
        Assert.Equal(6, HidppCatalog.MaxSlots(0xC548));
    }

    [Fact]
    public void Identity_IsIndependentOfFeatureIndex_ButDistinguishesPhysicalUnits()
    {
        var wireless = (byte[])DeviceInfo.Clone();
        wireless[0] = 0x0A;
        var other = (byte[])DeviceInfo.Clone();
        other[6] = 0x79;
        Assert.NotNull(HidppDeviceIdentity.ParseDeviceId(DeviceInfo));
        Assert.Equal(HidppDeviceIdentity.ParseDeviceId(DeviceInfo), HidppDeviceIdentity.ParseDeviceId(wireless));
        Assert.NotEqual(HidppDeviceIdentity.ParseDeviceId(DeviceInfo), HidppDeviceIdentity.ParseDeviceId(other));
    }

    [Theory]
    [InlineData(3, 4, 0)]
    [InlineData(3, 4, 255)]
    [InlineData(9, 6, 0)]
    [InlineData(9, 6, 255)]
    public void Identity_RejectsUnusableUnitOrModel(int start, int count, int value)
    {
        var response = (byte[])DeviceInfo.Clone();
        Array.Fill(response, (byte)value, start, count);
        Assert.Null(HidppDeviceIdentity.ParseDeviceId(response));
        Assert.Null(HidppDeviceIdentity.ParseDeviceId(response.AsSpan(0, 14)));
    }

    private static DeviceStatus Status(string id, int? percent, bool online, ChargingState charging,
        string oldId, int minute = 0) => new()
    {
        Id = id, Name = "同型号鼠标", Source = DeviceSource.Hid24G,
        BatteryPercent = percent, IsOnline = online, Charging = charging,
        PreviousIds = new[] { oldId }, LastUpdated = DateTimeOffset.UnixEpoch.AddMinutes(minute),
    };

    [Fact]
    public void Charging_UsesWiredReading_AndUnpluggingKeepsWirelessOnline()
    {
        var identity = new HidppDeviceIdentity();
        var wireless = Status("unit1", 96, true, ChargingState.Discharging, "receiver:1");
        var wired = Status("unit1", 98, true, ChargingState.Charging, "usb:255");
        identity.Update("wireless", false, wireless);
        var charging = identity.Update("wired", true, wired);
        Assert.Equal(98, charging.BatteryPercent);
        Assert.Equal(ChargingState.Charging, charging.Charging);
        Assert.Equal(new[] { "receiver:1", "usb:255" }, charging.PreviousIds.OrderBy(x => x));
        wired.IsOnline = false;
        var unplugged = identity.Remove("wired", wired);
        Assert.True(unplugged.IsOnline);
        Assert.Equal(96, unplugged.BatteryPercent);
        Assert.Equal(ChargingState.Discharging, unplugged.Charging);
        wireless.IsOnline = false;
        var disconnected = identity.Remove("wireless", wireless);
        Assert.False(disconnected.IsOnline);
        Assert.DoesNotContain("usb:255", disconnected.PreviousIds);
    }

    [Fact]
    public void OfflineReceiver_CannotOverwriteOnlineChargingDevice()
    {
        var identity = new HidppDeviceIdentity();
        identity.Update("wired", true, Status("unit1", 91, true, ChargingState.Charging, "usb"));
        var result = identity.Update("receiver", false, Status("unit1", 83, false, ChargingState.Unknown, "radio"));
        Assert.True(result.IsOnline);
        Assert.Equal(91, result.BatteryPercent);
        Assert.Equal(ChargingState.Charging, result.Charging);
    }

    [Fact]
    public void SameNameDifferentIdentity_IsNotMerged_AndSnapshotsAreIndependent()
    {
        var identity = new HidppDeviceIdentity();
        var first = Status("unit1", 80, true, ChargingState.Discharging, "first");
        identity.Update("a", false, first);
        first.BatteryPercent = 10;
        var other = identity.Update("b", true, Status("unit2", 90, true, ChargingState.Charging, "second"));
        Assert.Single(other.PreviousIds);
        var saved = identity.Update("c", false, Status("unit1", null, false, ChargingState.Unknown, "third"));
        Assert.Equal(80, saved.BatteryPercent);
        Assert.DoesNotContain("second", saved.PreviousIds);
    }

    [Fact]
    public void DirectUsb_MapsSixMistakenSlots_WhileReceiverOnlyMapsItsOwnSlot()
    {
        const string path = @"\\?\HID#VID_046D&PID_C09A&MI_02&Col02#B&123456&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        var legacy = HidppDeviceIdentity.LegacyIds(path, true, 0xFF);
        Assert.Equal(7, legacy.Length);
        Assert.Contains(legacy, id => id.EndsWith(":6"));
        Assert.Contains(legacy, id => id.EndsWith(":255"));
        Assert.Single(HidppDeviceIdentity.LegacyIds(path, false, 1));
    }

    [Fact]
    public void NewlyDetectedUsbWithoutReading_DoesNotHideValidWirelessReading()
    {
        var identity = new HidppDeviceIdentity();
        var original = Status("unit1", 96, true, ChargingState.Discharging, "radio");
        identity.Update("radio", false, original);
        var result = identity.Update("usb", true, Status("unit1", null, true, ChargingState.Unknown, "usb"));
        Assert.Equal(96, result.BatteryPercent);
        var clone = original.Clone("canonical");
        Assert.Equal("canonical", clone.Id);
        Assert.Equal(original.PreviousIds, clone.PreviousIds);
        Assert.NotSame(original.PreviousIds, clone.PreviousIds);
    }
}
