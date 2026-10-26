using VigilWatt.Core.Plugins.Atk;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： AtkDevicePairing —— 同一只 ATK 鼠标"接收器 + 插线"两条接口的配对规则。
/// 2026/10/26 22:40:03 ranxue
///
/// 真机样本（373B:1278 GEM 8K 接收器 与 373B:1293 本体，同一只 A9 Air 大师版+）：
/// 两者 USB 序列号都是 <c>541505796617</c>；但 HidDeviceScanner 目前不采 SN，
/// 插件侧拿到的是空串，所以配对主要靠"型号归一组"，SN 只在两边都有时当否决条件。
/// </summary>
public sealed class AtkDevicePairingTests
{
    private const string A9ReceiverPath = @"\\?\hid#vid_373b&pid_1278&mi_02&col01#receiver";
    private const string A9BodyPath = @"\\?\hid#vid_373b&pid_1293&mi_02&col01#body";
    private const string V3ReceiverPath = @"\\?\hid#vid_373b&pid_1303&mi_02&col01#v3dongle";
    private const string V3BodyPath = @"\\?\hid#vid_373b&pid_12a9&mi_02&col01#v3body";
    private const string Promax1KPath = @"\\?\hid#vid_373b&pid_101a&mi_02&col01#1k";
    private const string Sn = "541505796617";

    /// <summary>就是本次实测踩到的那一幕：接收器没拔就插了数据线。</summary>
    [Fact]
    public void A9AirDualConnectionSkipsReceiver()
    {
        var devices = new List<AtkInterfaceRef>
        {
            new(A9ReceiverPath, 0x1278, "Wireless mouse 8k dongle-L", Sn),
            new(A9BodyPath, 0x1293, "ATK A9 Air+", Sn),
        };

        Assert.Equal(new[] { A9ReceiverPath }, AtkDevicePairing.ReceiverPathsToSkip(devices));
    }

    /// <summary>扫描器当前不填 SN，两边都是空串时也必须能配对（这是实际运行时的情形）。</summary>
    [Fact]
    public void EmptySerialStillPairs()
    {
        var devices = new List<AtkInterfaceRef>
        {
            new(A9ReceiverPath, 0x1278, "Wireless mouse 8k dongle-L", ""),
            new(A9BodyPath, 0x1293, "ATK A9 Air+", ""),
        };

        Assert.Equal(new[] { A9ReceiverPath }, AtkDevicePairing.ReceiverPathsToSkip(devices));
    }

    /// <summary>平时只用 2.4G：接收器照常认领，不能被摘掉。</summary>
    [Fact]
    public void ReceiverOnlyIsNotSkipped()
    {
        var devices = new List<AtkInterfaceRef>
        {
            new(A9ReceiverPath, 0x1278, "Wireless mouse 8k dongle-L", Sn),
        };
        Assert.Empty(AtkDevicePairing.ReceiverPathsToSkip(devices));
    }

    /// <summary>只插线、接收器已经拔掉：本体自己一条。</summary>
    [Fact]
    public void BodyOnlyIsNotSkipped()
    {
        var devices = new List<AtkInterfaceRef>
        {
            new(A9BodyPath, 0x1293, "ATK A9 Air+", Sn),
        };
        Assert.Empty(AtkDevicePairing.ReceiverPathsToSkip(devices));
    }

    /// <summary>两只同型号鼠标（一只无线、一只插线）：SN 不同，不能把无线那只摘掉。</summary>
    [Fact]
    public void TwoIdenticalMiceWithDifferentSerialsAreNotMerged()
    {
        var devices = new List<AtkInterfaceRef>
        {
            new(A9ReceiverPath, 0x1278, "Wireless mouse 8k dongle-L", "111111111111"),
            new(A9BodyPath, 0x1293, "ATK A9 Air+", "222222222222"),
        };

        Assert.Empty(AtkDevicePairing.ReceiverPathsToSkip(devices));
    }

    /// <summary>接收器与本体属于**不同组**时不能互相摘（F1 V3 的接收器 + A9 的本体）。</summary>
    [Fact]
    public void DifferentGroupsDoNotCrossPair()
    {
        var devices = new List<AtkInterfaceRef>
        {
            new(V3ReceiverPath, 0x1303, "Wireless mouse 8k dongle-M", ""),
            new(A9BodyPath, 0x1293, "ATK A9 Air+", ""),
        };

        Assert.Empty(AtkDevicePairing.ReceiverPathsToSkip(devices));
    }

    /// <summary>同一只 F1 Pro Max 的 8K 与 1K 接收器同时被插（罕见）：两只都该让位给本体。</summary>
    [Fact]
    public void BothReceiversOfSameGroupAreSkipped()
    {
        const string ProMaxBodyPath = @"\\?\hid#vid_373b&pid_1015&mi_02&col01#promaxbody";
        var devices = new List<AtkInterfaceRef>
        {
            new(A9ReceiverPath, 0x101B, "ATK Mouse 8K Dongle", ""),
            new(Promax1KPath, 0x101A, "ATK Mouse Dongle", ""),
            new(ProMaxBodyPath, 0x1015, "ATK F1 Pro Max", ""),
        };

        var skip = AtkDevicePairing.ReceiverPathsToSkip(devices);
        Assert.Equal(2, skip.Count);
        Assert.Contains(A9ReceiverPath, skip);
        Assert.Contains(Promax1KPath, skip);
    }

    [Fact]
    public void GroupOfMapsEveryKnownPid()
    {
        Assert.Equal("a9air", AtkDevicePairing.GroupOf(0x1278));
        Assert.Equal("a9air", AtkDevicePairing.GroupOf(0x1293));
        Assert.Equal("v3", AtkDevicePairing.GroupOf(0x1303));
        Assert.Equal("v3", AtkDevicePairing.GroupOf(0x12A9));
        Assert.Equal("promax", AtkDevicePairing.GroupOf(0x101B));
        Assert.Equal("promax", AtkDevicePairing.GroupOf(0x101A));
        Assert.Equal("promax", AtkDevicePairing.GroupOf(0x1015));
        Assert.Null(AtkDevicePairing.GroupOf(0x9999));
    }

    [Fact]
    public void IsReceiverOnlyTrueForDongles()
    {
        Assert.True(AtkDevicePairing.IsReceiver(0x1278));
        Assert.True(AtkDevicePairing.IsReceiver(0x1303));
        Assert.True(AtkDevicePairing.IsReceiver(0x101B));
        Assert.True(AtkDevicePairing.IsReceiver(0x101A));
        Assert.False(AtkDevicePairing.IsReceiver(0x1293));   // A9 Air+ 本体
        Assert.False(AtkDevicePairing.IsReceiver(0x12A9));   // F1 V3 本体
        Assert.False(AtkDevicePairing.IsReceiver(0x1015));   // F1 Pro Max 本体
    }
}
