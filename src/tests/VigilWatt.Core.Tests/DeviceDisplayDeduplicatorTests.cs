using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： DeviceDisplayDeduplicator —— 同一只设备多个条目时的展示去重。
/// 2026/10/21
///
/// 真实场景（用户截图）：蜻蜓 F2 大师+ 插线充电时，Windows 同时枚举无线接收器与插线接口，
/// 两条各自成条目；插线那条在线、无线那条变成离线历史占位，界面上就出现"同一只鼠标两行"。
/// </summary>
public sealed class DeviceDisplayDeduplicatorTests
{
    private static DeviceStatus Dev(string id, string name, bool online, int minutesAgo = 0)
        => new()
        {
            Id = id,
            Name = name,
            Source = DeviceSource.Hid24G,
            Kind = DeviceKind.Mouse,
            IsOnline = online,
            LastUpdated = DateTimeOffset.Now.AddMinutes(-minutesAgo),
        };

    [Fact]
    public void OnlineEntryHidesItsOfflineTwin()
    {
        var list = new List<DeviceStatus>
        {
            Dev("vgn:wired", "Dragonfly F2 Master+", online: true),
            Dev("vgn:wireless", "Dragonfly F2 Master+", online: false, minutesAgo: 500),
        };

        var result = DeviceDisplayDeduplicator.Dedupe(list);

        Assert.Single(result);
        Assert.Equal("vgn:wired", result.First().Id);
    }

    [Fact]
    public void TwoOnlineEntriesWithSameNameBothStay()
    {
        // 用户真有两台同型号设备：都在线时不能合并
        var list = new List<DeviceStatus>
        {
            Dev("vgn:a", "Dragonfly F2 Master+", online: true),
            Dev("vgn:b", "Dragonfly F2 Master+", online: true),
        };

        Assert.Equal(2, DeviceDisplayDeduplicator.Dedupe(list).Count);
    }

    [Fact]
    public void AllOfflineKeepsTheMostRecentOne()
    {
        var list = new List<DeviceStatus>
        {
            Dev("vgn:old", "Dragonfly F2 Master+", online: false, minutesAgo: 500),
            Dev("vgn:new", "Dragonfly F2 Master+", online: false, minutesAgo: 10),
        };

        var result = DeviceDisplayDeduplicator.Dedupe(list);

        Assert.Single(result);
        Assert.Equal("vgn:new", result.First().Id);
    }

    [Fact]
    public void DifferentNamesAreNeverMerged()
    {
        var list = new List<DeviceStatus>
        {
            Dev("a", "狼蛛 F108 Pro", online: true),
            Dev("b", "Dragonfly F2 Master+", online: true),
            Dev("c", "迈从 K7 V2 Ultra+", online: false),
        };

        Assert.Equal(3, DeviceDisplayDeduplicator.Dedupe(list).Count);
    }

    [Fact]
    public void NameMatchIsCaseInsensitive()
    {
        var list = new List<DeviceStatus>
        {
            Dev("a", "Dragonfly F2 Master+", online: true),
            Dev("b", "dragonfly f2 master+", online: false),
        };

        Assert.Single(DeviceDisplayDeduplicator.Dedupe(list));
    }

    [Fact]
    public void OrderOfInputIsPreserved()
    {
        var list = new List<DeviceStatus>
        {
            Dev("a", "A 设备", online: true),
            Dev("b", "B 设备", online: true),
            Dev("c", "C 设备", online: true),
        };

        var result = DeviceDisplayDeduplicator.Dedupe(list).ToList();

        Assert.Equal(new[] { "a", "b", "c" }, result.Select(d => d.Id));
    }
}
