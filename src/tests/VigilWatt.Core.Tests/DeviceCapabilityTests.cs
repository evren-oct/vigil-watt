using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： CapabilitySet 的三态语义、非法值拒绝，"能力属于设备而非协议"的隔离性，以及查询的接口边界。
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
    public void 空集合各项全是Unverified()
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

        Assert.Throws<ArgumentOutOfRangeException>(() => set.Of((DeviceCapability)8));
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

    // ── 查询边界：能力只对「本插件认领的那条接口」成立（PR-C1）────────

    private const string BoundaryProtocols = """
        {
          "vendors": ["0x1234"],
          "protocols": {
            "data-p": {
              "kind": "mouse",
              "interface": { "usagePage": 65280, "usage": 2, "inputLength": 19 },
              "passive": {
                "length": { "min": 19 },
                "fields": { "percent": { "offset": 8, "type": "u8" } }
              }
            },
            "code-p": {
              "kind": "mouse",
              "implementation": "code",
              "codeFamily": "SomeFamily",
              "interface": { "usagePage": 65280, "usage": 2, "inputLength": 19 }
            },
            "none-p": {
              "kind": "mouse",
              "implementation": "none",
              "interface": { "usagePage": 65280, "usage": 2, "inputLength": 19 }
            }
          }
        }
        """;

    private const string BoundaryDevices = """
        {
          "devices": [
            { "vid": "0x1234", "pid": "0x0001", "model": "Data", "kind": "mouse", "protocol": "data-p",
              "capabilities": { "batteryRead": "supported" } },
            { "vid": "0x1234", "pid": "0x0002", "model": "Code", "kind": "mouse", "protocol": "code-p",
              "capabilities": { "batteryRead": "supported" } },
            { "vid": "0x1234", "pid": "0x0003", "model": "None", "kind": "mouse", "protocol": "none-p",
              "capabilities": { "batteryRead": "supported" } }
          ]
        }
        """;

    private static readonly ProtocolEngine BoundaryEngine =
        ProtocolEngine.FromJson(BoundaryProtocols, BoundaryDevices);

    /// <summary>默认参数就是目录里那条接口的形态；改任一项即"同一 VID/PID 的另一条 collection"。</summary>
    private static HidDeviceInfo Info(ushort pid,
        ushort usagePage = 0xFF00, ushort usage = 0x0002, ushort inputLength = 19) => new()
    {
        DevicePath = $@"\\?\hid#vid_1234&pid_{pid:x4}#test",
        VendorId = 0x1234,
        ProductId = pid,
        UsagePage = usagePage,
        Usage = usage,
        InputReportLength = inputLength,
    };

    [Fact]
    public void 接口匹配且实现为Data时返回设备声明()
    {
        Assert.Equal(CapabilityState.Supported, BoundaryEngine.CapabilityOf(Info(0x0001), DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Unverified, BoundaryEngine.CapabilityOf(Info(0x0001), DeviceCapability.DpiRead));
    }

    [Fact]
    public void 接口匹配且实现为Code时返回设备声明()
        => Assert.Equal(CapabilityState.Supported,
            BoundaryEngine.CapabilityOf(Info(0x0002), DeviceCapability.BatteryRead));

    [Fact]
    public void UsagePage不符时返回未验证()
        => Assert.Equal(CapabilityState.Unverified,
            BoundaryEngine.CapabilityOf(Info(0x0001, usagePage: 0x0001), DeviceCapability.BatteryRead));

    [Fact]
    public void Usage不符时返回未验证()
        => Assert.Equal(CapabilityState.Unverified,
            BoundaryEngine.CapabilityOf(Info(0x0001, usage: 0x0001), DeviceCapability.BatteryRead));

    [Fact]
    public void 报告长度不符时返回未验证()
        => Assert.Equal(CapabilityState.Unverified,
            BoundaryEngine.CapabilityOf(Info(0x0001, inputLength: 32), DeviceCapability.BatteryRead));

    [Fact]
    public void 同VIDPID的另一条接口不继承目录声明()
    {
        // 同一只鼠标既有厂商集合也有鼠标集合：只有前者被认领，后者不能"因为 VID/PID 相同"拿到 Supported
        var vendor = Info(0x0001);
        var mouse = Info(0x0001, usagePage: 0x0001, usage: 0x0002, inputLength: 8);

        Assert.NotNull(BoundaryEngine.Resolve(vendor));
        Assert.Null(BoundaryEngine.Resolve(mouse));
        Assert.Equal(CapabilityState.Supported, BoundaryEngine.CapabilityOf(vendor, DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Unverified, BoundaryEngine.CapabilityOf(mouse, DeviceCapability.BatteryRead));
    }

    [Fact]
    public void 未收录设备返回未验证()
        => Assert.Equal(CapabilityState.Unverified,
            BoundaryEngine.CapabilityOf(Info(0xFFFF), DeviceCapability.BatteryRead));

    [Fact]
    public void 实现为None的目录条目即使声明了能力也返回未验证()
    {
        // 目录收录但没适配：接口形态完全相符也不能把声明当"已验证"
        Assert.Null(BoundaryEngine.Resolve(Info(0x0003)));
        Assert.Equal(CapabilityState.Unverified,
            BoundaryEngine.CapabilityOf(Info(0x0003), DeviceCapability.BatteryRead));
    }

    [Fact]
    public void 非法枚举值在三种边界下都抛()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BoundaryEngine.CapabilityOf(Info(0x0001), (DeviceCapability)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BoundaryEngine.CapabilityOf(Info(0x0003), (DeviceCapability)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BoundaryEngine.CapabilityOf(Info(0x0001, usagePage: 0x0001), (DeviceCapability)99));
    }
}
