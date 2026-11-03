using System.Text.Json;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： ProtocolSpecLoader 字符串入口与资源入口的等价性——两份 JSON 必须走同一条加载核心。
/// 2026/11/02 22:56:00 xliu
/// 只验加载行为（继承 / override / 顺序 / 异常），帧解析由 RapooSpecTests 负责。
/// </summary>
public sealed class ProtocolSpecLoaderTests
{
    private const string RapooProtocols = "VigilWatt.Core.Plugins.Rapoo.specs.rapoo.protocols.json";
    private const string RapooDevices = "VigilWatt.Core.Plugins.Rapoo.rapoo-catalog.json";
    private const string MchoseProtocols = "VigilWatt.Core.Plugins.Mchose.specs.mchose.protocols.json";
    private const string MchoseDevices = "VigilWatt.Core.Plugins.Mchose.mchose-catalog.json";

    /// <summary>最小可用协议：一个鼠标协议，被动帧 19 字节、电量在 [8]。</summary>
    private const string MinimalProtocols = """
        {
          "vendors": ["0x1234"],
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

    private const string EmptyDevices = """{ "devices": [] }""";

    // ── 正常加载 ──────────────────────────────────────────────────────

    [Fact]
    public void LoadFromJson_MinimalProtocols_LoadsOneProtocol()
    {
        var bundle = ProtocolSpecLoader.LoadFromJson(MinimalProtocols, EmptyDevices);

        var spec = Assert.Single(bundle.Protocols).Value;
        Assert.Equal("p", spec.Name);
        Assert.Equal(DeviceKind.Mouse, spec.Kind);
        Assert.Equal(8, spec.Passive!.Percent!.Offset);
        Assert.Empty(bundle.Devices);
    }

    [Fact]
    public void LoadFromJson_SupportsCommentsAndTrailingCommas()
    {
        // 两份真实 JSON 都带注释与尾随逗号，字符串入口必须沿用同一份 JsonDocumentOptions
        const string protocols = """
            {
              // 厂商白名单
              "vendors": ["0x1234",],
              "protocols": {
                "p": {
                  "kind": "mouse",
                  "interface": { "usagePage": 1, },
                  "passive": {
                    "length": { "min": 19, },
                    "fields": { "percent": { "offset": 8, }, },
                  },
                },
              },
            }
            """;
        const string devices = """{ "devices": [], }""";

        var bundle = ProtocolSpecLoader.LoadFromJson(protocols, devices);

        Assert.Equal(8, Assert.Single(bundle.Protocols).Value.Passive!.Percent!.Offset);
    }

    // ── 异常行为与资源入口一致 ────────────────────────────────────────

    [Fact]
    public void LoadFromJson_EmptyProtocolsObject_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson("""{ "protocols": {} }""", EmptyDevices));

        Assert.Contains("缺少非空的 protocols 对象", ex.Message);
    }

    [Fact]
    public void LoadFromJson_MissingProtocols_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson("""{ "vendors": [] }""", EmptyDevices));

        Assert.Contains("缺少非空的 protocols 对象", ex.Message);
    }

    [Fact]
    public void LoadFromJson_ProtocolsNotObject_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson("""{ "protocols": [] }""", EmptyDevices));

        Assert.Contains("缺少非空的 protocols 对象", ex.Message);
    }

    [Fact]
    public void LoadFromJson_ProtocolsRootNotObject_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson("""[]""", EmptyDevices));

        Assert.Contains("<inline>/protocols.json 根节点不是对象", ex.Message);
    }

    [Fact]
    public void LoadFromJson_EmptyProtocolsJson_Throws()
    {
        // 空串不是"根节点不是对象"，而是解析阶段就失败——两者必须能区分
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson("", EmptyDevices));

        Assert.Contains("<inline>/protocols.json 不是合法 JSON", ex.Message);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);   // 实际是 JsonReaderException
    }

    [Fact]
    public void LoadFromJson_MalformedProtocolsJson_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson("""{"protocols": }""", EmptyDevices));

        Assert.Contains("<inline>/protocols.json 不是合法 JSON", ex.Message);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Fact]
    public void LoadFromJson_NullLiteralRoot_Throws()
    {
        // "null" 是合法 JSON 但值为 null：JsonNode.Parse 返回 null，应落到"根节点不是对象"
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson("null", EmptyDevices));

        Assert.Contains("<inline>/protocols.json 根节点不是对象", ex.Message);
    }

    [Fact]
    public void LoadFromJson_MissingDevicesArray_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson(MinimalProtocols, """{ "brand": "X" }"""));

        Assert.Contains("缺少 devices 数组", ex.Message);
    }

    [Fact]
    public void LoadFromJson_DevicesNotArray_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson(MinimalProtocols, """{ "devices": {} }"""));

        Assert.Contains("缺少 devices 数组", ex.Message);
    }

    [Fact]
    public void LoadFromJson_DevicesRootNotObject_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProtocolSpecLoader.LoadFromJson(MinimalProtocols, """[]"""));

        Assert.Contains("<inline>/devices.json 根节点不是对象", ex.Message);
    }

    // ── 继承 / override / 顺序 ────────────────────────────────────────

    [Fact]
    public void LoadFromJson_Extends_InheritsParentFieldsAndKind()
    {
        const string protocols = """
            {
              "protocols": {
                "parent": {
                  "kind": "mouse",
                  "interface": { "usagePage": 1 },
                  "passive": {
                    "length": { "min": 19 },
                    "fields": { "percent": { "offset": 8, "type": "u8" } }
                  }
                },
                "child": {
                  "extends": "parent",
                  "passive": {
                    "fields": {
                      "charge": { "offset": 7, "type": "u8", "map": { "1": "discharging", "2": "charging" } }
                    }
                  }
                }
              }
            }
            """;

        var bundle = ProtocolSpecLoader.LoadFromJson(protocols, EmptyDevices);

        var child = bundle.Protocols["child"];
        Assert.Equal(DeviceKind.Mouse, child.Kind);          // 从 parent 继承
        Assert.Equal(8, child.Passive!.Percent!.Offset);     // 从 parent 继承
        Assert.Equal(7, child.Passive!.Charge!.Offset);      // 子级新增
    }

    [Fact]
    public void LoadFromJson_DeviceOverride_MergesOntoExpandedSpec()
    {
        const string devices = """
            {
              "devices": [
                {
                  "vid": "0x1234",
                  "pid": "0x0001",
                  "model": "T",
                  "kind": "mouse",
                  "protocol": "p",
                  "override": { "passive": { "fields": { "percent": { "offset": 7 } } } }
                }
              ]
            }
            """;

        var bundle = ProtocolSpecLoader.LoadFromJson(MinimalProtocols, devices);

        var key = ((ushort)0x1234, (ushort)0x0001);
        var overridden = bundle.DeviceSpecs[key];
        Assert.Equal(7, overridden.Passive!.Percent!.Offset);          // override 生效
        Assert.Equal(8, bundle.Protocols["p"].Passive!.Percent!.Offset); // 协议本体不变
    }

    [Fact]
    public void LoadFromJson_ProtocolOrder_FollowsDeclarationOrder()
    {
        // 刻意不用字母序：若实现按名字排序，这里会变成 alpha/mu/zeta
        const string protocols = """
            {
              "protocols": {
                "zeta":  { "kind": "mouse", "interface": { "usagePage": 1 },
                           "passive": { "fields": { "percent": { "offset": 1 } } } },
                "alpha": { "kind": "mouse", "interface": { "usagePage": 1 },
                           "passive": { "fields": { "percent": { "offset": 1 } } } },
                "mu":    { "kind": "mouse", "interface": { "usagePage": 1 },
                           "passive": { "fields": { "percent": { "offset": 1 } } } }
              }
            }
            """;

        var bundle = ProtocolSpecLoader.LoadFromJson(protocols, EmptyDevices);

        Assert.Equal(new[] { "zeta", "alpha", "mu" }, bundle.Ordered.Select(s => s.Name));
    }

    // ── 内嵌资源回归（数量必须来自实际加载结果）────────────────────────

    [Fact]
    public void Load_RapooResources_Loads12ProtocolsAnd195Devices()
    {
        var bundle = ProtocolSpecLoader.Load(RapooProtocols, RapooDevices);

        Assert.Equal(12, bundle.Protocols.Count);
        Assert.Equal(195, bundle.Devices.Count);
        Assert.Equal("雷柏", bundle.Brand);
    }

    [Fact]
    public void Load_MchoseResources_Loads8ProtocolsAnd63Devices()
    {
        var bundle = ProtocolSpecLoader.Load(MchoseProtocols, MchoseDevices);

        Assert.Equal(8, bundle.Protocols.Count);
        Assert.Equal(63, bundle.Devices.Count);
    }
}
