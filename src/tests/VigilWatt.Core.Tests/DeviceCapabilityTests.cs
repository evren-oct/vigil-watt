using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： CapabilitySet 的三态语义、非法值拒绝，以及"能力属于设备而非协议"的隔离性。
/// 2026/11/04 21:05:00 xliu
/// 加载期校验在 ProtocolSpecLoaderTests，真实目录在 RapooSpecTests。规则见 docs/dev/protocol-json.md §2.1。
/// </summary>
public sealed class DeviceCapabilityTests
{
    private const string Protocols = """
        {
          "protocols": {
            "p": {
              "kind": "mouse",
              "interface": { "usagePage": 1 },
              "passive": {
                "length": { "min": 19 },
                "fields": { "percent": { "offset": 8, "type": "u8" } }
              }
            }
          }
        }
        """;

    private static CapabilitySet Set(params (DeviceCapability Capability, CapabilityState State)[] declared)
        => CapabilitySet.Create(declared.Select(d =>
            new KeyValuePair<DeviceCapability, CapabilityState>(d.Capability, d.State)));

    // ── 三态读取 ────────────────────────────────────────────────────

    [Fact]
    public void Supported_能正常读出()
    {
        var set = Set((DeviceCapability.BatteryRead, CapabilityState.Supported));

        Assert.Equal(CapabilityState.Supported, set.Of(DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Supported, set[DeviceCapability.BatteryRead]);
    }

    [Fact]
    public void Unsupported_能正常读出_且与Unverified区分()
    {
        var set = Set(
            (DeviceCapability.DpiWrite, CapabilityState.Unsupported),
            (DeviceCapability.RateWrite, CapabilityState.Unverified));

        Assert.Equal(CapabilityState.Unsupported, set.Of(DeviceCapability.DpiWrite));
        Assert.Equal(CapabilityState.Unverified, set.Of(DeviceCapability.RateWrite));
        Assert.NotEqual(set.Of(DeviceCapability.DpiWrite), set.Of(DeviceCapability.RateWrite));
    }

    [Fact]
    public void 未声明的能力一律Unverified()
    {
        var set = Set((DeviceCapability.BatteryRead, CapabilityState.Supported));

        Assert.Equal(CapabilityState.Unverified, set.Of(DeviceCapability.DpiRead));
        Assert.Equal(CapabilityState.Unverified, set.Of(DeviceCapability.DpiLevelRead));
        Assert.Equal(CapabilityState.Unverified, set.Of(DeviceCapability.RateRead));
        Assert.Equal(CapabilityState.Unverified, set.Of(DeviceCapability.DpiWrite));
        Assert.Equal(CapabilityState.Unverified, set.Of(DeviceCapability.RateWrite));
    }

    [Fact]
    public void 空集合六项全是Unverified()
    {
        foreach (var capability in Enum.GetValues<DeviceCapability>())
            Assert.Equal(CapabilityState.Unverified, CapabilitySet.Empty.Of(capability));

        Assert.True(CapabilitySet.Empty.IsEmpty);
        Assert.Empty(CapabilitySet.Empty.Declared);
    }

    [Fact]
    public void Empty可安全共享_且Create空序列返回同一实例()
    {
        var again = CapabilitySet.Create([]);

        Assert.Same(CapabilitySet.Empty, again);
    }

    [Fact]
    public void 默认态不是Supported也不是Unsupported()
    {
        // 三态的第一个值必须是 Unverified：default(CapabilityState) 不能表达"已验证"
        Assert.Equal(0, (int)CapabilityState.Unverified);
        Assert.Equal(CapabilityState.Unverified, default(CapabilityState));
    }

    // ── 非法值不许绕过校验 ──────────────────────────────────────────

    [Fact]
    public void 非法枚举值不会被当成未声明()
    {
        var set = Set((DeviceCapability.BatteryRead, CapabilityState.Supported));

        Assert.Throws<ArgumentOutOfRangeException>(() => set.Of((DeviceCapability)6));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.Of((DeviceCapability)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CapabilitySet.Empty.Of((DeviceCapability)99));
    }

    [Fact]
    public void Create拒绝非法枚举值与非法状态()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Set(((DeviceCapability)99, CapabilityState.Supported)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Set((DeviceCapability.BatteryRead, (CapabilityState)7)));
    }

    [Fact]
    public void Create拒绝重复声明()
    {
        // 重复时静默取其一，等于把"标错"变成"看不出标错"
        Assert.Throws<ArgumentException>(() => Set(
            (DeviceCapability.BatteryRead, CapabilityState.Supported),
            (DeviceCapability.BatteryRead, CapabilityState.Unsupported)));
    }

    [Fact]
    public void 已创建的集合不能被外部改写()
    {
        var set = Set((DeviceCapability.BatteryRead, CapabilityState.Supported));

        // Declared 是 ReadOnlyDictionary：强转回 IDictionary 也只能读，写入必须抛
        var mutable = Assert.IsAssignableFrom<IDictionary<DeviceCapability, CapabilityState>>(set.Declared);
        Assert.Throws<NotSupportedException>(
            () => mutable[DeviceCapability.BatteryRead] = CapabilityState.Unsupported);
        Assert.Throws<NotSupportedException>(
            () => mutable.Add(DeviceCapability.DpiWrite, CapabilityState.Supported));

        Assert.Equal(CapabilityState.Supported, set.Of(DeviceCapability.BatteryRead));
    }

    // ── 设备隔离：能力属于设备，不属于协议 ──────────────────────────

    [Fact]
    public void 共用同一协议的两台设备能力互不串用()
    {
        const string devices = """
            {
              "devices": [
                { "vid": "0x1234", "pid": "0x0001", "model": "A", "kind": "mouse", "protocol": "p",
                  "capabilities": { "batteryRead": "supported" } },
                { "vid": "0x1234", "pid": "0x0002", "model": "B", "kind": "mouse", "protocol": "p" }
              ]
            }
            """;

        var bundle = ProtocolSpecLoader.LoadFromJson(Protocols, devices);
        var keyA = ((ushort)0x1234, (ushort)0x0001);
        var keyB = ((ushort)0x1234, (ushort)0x0002);

        // 前提：两台设备确实共用同一个 ProtocolSpec 实例——能力若挂在协议上，A 的声明会漏给 B
        Assert.Same(bundle.DeviceSpecs[keyA], bundle.DeviceSpecs[keyB]);
        Assert.Same(bundle.Protocols["p"], bundle.DeviceSpecs[keyA]);

        Assert.Equal(CapabilityState.Supported, bundle.Devices[keyA].Capabilities.Of(DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Unverified, bundle.Devices[keyB].Capabilities.Of(DeviceCapability.BatteryRead));
        Assert.True(bundle.Devices[keyB].Capabilities.IsEmpty);
    }

    [Fact]
    public void 设备override不影响另一台设备的能力()
    {
        // override 只改协议规格（偏移之类），能力声明不该被它带着走
        const string devices = """
            {
              "devices": [
                { "vid": "0x1234", "pid": "0x0001", "model": "A", "kind": "mouse", "protocol": "p",
                  "capabilities": { "dpiRead": "supported" },
                  "override": { "passive": { "fields": { "percent": { "offset": 7 } } } } },
                { "vid": "0x1234", "pid": "0x0002", "model": "B", "kind": "mouse", "protocol": "p" }
              ]
            }
            """;

        var bundle = ProtocolSpecLoader.LoadFromJson(Protocols, devices);

        Assert.NotSame(bundle.DeviceSpecs[((ushort)0x1234, (ushort)0x0001)],
            bundle.DeviceSpecs[((ushort)0x1234, (ushort)0x0002)]);
        Assert.Equal(CapabilityState.Supported,
            bundle.Devices[((ushort)0x1234, (ushort)0x0001)].Capabilities.Of(DeviceCapability.DpiRead));
        Assert.Equal(CapabilityState.Unverified,
            bundle.Devices[((ushort)0x1234, (ushort)0x0002)].Capabilities.Of(DeviceCapability.DpiRead));
    }
}
