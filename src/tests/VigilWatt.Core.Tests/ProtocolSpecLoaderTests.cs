using System.Text.Json;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： ProtocolSpecLoader 的加载行为与帧字段边界校验。
/// 2026/11/02 22:56:00 xliu
/// 帧解析由 RapooSpecTests 负责；这里只验加载期。规则见 docs/dev/protocol-json.md §4。
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
        Assert.Equal(new[] { 0, 1 }, spec.Passive.TryOffsets);
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

        var report = bundle.Protocols["status-report7"].Passive!;
        Assert.Equal(19, report.Length!.Value.Min);
        Assert.Equal(18, report.Link!.Offset);
        Assert.Equal(new[] { 0, 1 }, report.TryOffsets);
    }

    [Fact]
    public void Load_MchoseResources_Loads8ProtocolsAnd63Devices()
    {
        var bundle = ProtocolSpecLoader.Load(MchoseProtocols, MchoseDevices);

        Assert.Equal(8, bundle.Protocols.Count);
        Assert.Equal(63, bundle.Devices.Count);

        // Gmouse 的 response 没有 length，outputLength 只有 Min：加载必须成功，不能补造帧长
        var gmouse = bundle.Protocols["gmouse"];
        Assert.Null(gmouse.Active!.Response.Length);
        Assert.Equal(65, gmouse.Active.Query.Length);
        Assert.Null(gmouse.Interface.OutputLength!.Value.Eq);
        Assert.Equal(65, gmouse.Interface.OutputLength.Value.Min);
        Assert.Equal(new[] { 0, 1 }, gmouse.Active.Response.TryOffsets);
    }

    // ── 帧字段边界：只认 FrameSpec.Length，主分支 TryOffsets[0] ────────

    [Fact]
    public void LoadFromJson_PassiveWithinLengthMin_Loads()
    {
        var bundle = LoadPassive("""
            {
              "length": { "min": 8 },
              "fields": { "percent": { "offset": 7, "type": "u8" } }
            }
            """);

        var frame = bundle.Protocols["p"].Passive!;
        Assert.Equal(7, frame.Percent!.Offset);
        Assert.Equal(new[] { 0, 1 }, frame.TryOffsets);
    }

    [Fact]
    public void LoadFromJson_PassivePastLengthMin_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "length": { "min": 8 },
              "fields": { "percent": { "offset": 8, "type": "u8" } }
            }
            """);

        Assert.Contains("passive.percent", ex.Message);
        Assert.Contains("超出帧长", ex.Message);
    }

    [Fact]
    public void LoadFromJson_PassiveU16PastLengthEq_Throws()
    {
        // 偏移 7 的 u8 放得进 8 字节，u16 要占到下标 8
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 8 },
              "fields": { "percent": { "offset": 7, "type": "u16" } }
            }
            """);

        Assert.Contains("passive.percent", ex.Message);
        Assert.Contains("超出帧长", ex.Message);
    }

    [Fact]
    public void LoadFromJson_PassiveChargePastLength_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 8 },
              "fields": { "charge": { "offset": 8, "type": "u8", "on": "nonzero" } }
            }
            """);

        Assert.Contains("passive.charge", ex.Message);
    }

    [Fact]
    public void LoadFromJson_PassiveLinkPastLength_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 8 },
              "fields": { "link": { "offset": 8, "bit": 7, "connectedWhen": 1 } }
            }
            """);

        Assert.Contains("passive.link", ex.Message);
    }

    [Fact]
    public void LoadFromJson_PassiveLinkModePastLength_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 8 },
              "fields": { "linkMode": { "offset": 8, "map": { "0": "wireless2g" } } }
            }
            """);

        Assert.Contains("passive.linkMode", ex.Message);
    }

    [Fact]
    public void LoadFromJson_PassivePrefixPastLength_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 8 },
              "prefix": [ { "offset": 8, "value": 1 } ],
              "fields": { "percent": { "offset": 0, "type": "u8" } }
            }
            """);

        Assert.Contains("passive.prefix", ex.Message);
    }

    [Fact]
    public void LoadFromJson_LengthMin_IgnoresSmallerInputLength()
    {
        // inputLength.eq 更短，也不能把它当成帧长；下界是 length.min
        var bundle = LoadPassive(
            """
            {
              "length": { "min": 20 },
              "fields": { "percent": { "offset": 15, "type": "u8" } }
            }
            """,
            """{ "usagePage": 1, "inputLength": { "eq": 8 } }""");

        Assert.Equal(15, bundle.Protocols["p"].Passive!.Percent!.Offset);
    }

    [Fact]
    public void LoadFromJson_LengthEq_BeatsSmallerMin()
    {
        // Matches 先看 Eq。若误用 min=8 当帧长，偏移 15 会被拒绝
        var bundle = LoadPassive("""
            {
              "length": { "eq": 20, "min": 8 },
              "fields": { "percent": { "offset": 15, "type": "u8" } }
            }
            """);

        Assert.Equal(15, bundle.Protocols["p"].Passive!.Percent!.Offset);
        Assert.Equal(20, ProtocolSpecLoader.TryGetMinimumAcceptedValue(
            bundle.Protocols["p"].Passive!.Length!.Value));
    }

    [Fact]
    public void LoadFromJson_LengthAny_UsesShortestAcceptedFrame()
    {
        var ex = ThrowsPassive("""
            {
              "length": [8, 64],
              "fields": { "percent": { "offset": 8, "type": "u8" } }
            }
            """);

        Assert.Contains("超出帧长 8", ex.Message);
    }

    [Fact]
    public void LoadFromJson_NoFrameLength_SkipsBounds()
    {
        var bundle = LoadPassive("""
            { "fields": { "percent": { "offset": 100, "type": "u8" } } }
            """);

        var frame = bundle.Protocols["p"].Passive!;
        Assert.Null(frame.Length);
        Assert.Equal(100, frame.Percent!.Offset);
    }

    [Fact]
    public void LoadFromJson_InputLength_IsNotFrameBound()
    {
        var bundle = LoadPassive(
            """{ "fields": { "percent": { "offset": 100, "type": "u8" } } }""",
            """{ "usagePage": 1, "inputLength": { "eq": 8 } }""");

        Assert.Equal(100, bundle.Protocols["p"].Passive!.Percent!.Offset);
    }

    [Fact]
    public void LoadFromJson_LengthMaxOnly_SkipsBounds()
    {
        var bundle = LoadPassive("""
            {
              "length": { "max": 4 },
              "fields": { "percent": { "offset": 100, "type": "u8" } }
            }
            """);

        var frame = bundle.Protocols["p"].Passive!;
        Assert.Equal(100, frame.Percent!.Offset);
        Assert.Null(ProtocolSpecLoader.TryGetMinimumAcceptedValue(frame.Length!.Value));
    }

    [Fact]
    public void LoadFromJson_ActiveResponseWithinLength_Loads()
    {
        // inputLength 更短也不能把合法的 response 判死
        var bundle = LoadActive(
            ActiveOf(4, """
                {
                  "length": { "eq": 8 },
                  "fields": { "percent": { "offset": 7, "type": "u8" } }
                }
                """),
            """{ "usagePage": 1, "inputLength": { "eq": 2 } }""");

        Assert.Equal(7, bundle.Protocols["p"].Active!.Response.Percent!.Offset);
    }

    [Fact]
    public void LoadFromJson_ActiveResponsePastLength_Throws()
    {
        var ex = ThrowsActive(ActiveOf(4, """
            {
              "length": { "eq": 8 },
              "fields": { "percent": { "offset": 8, "type": "u8" } }
            }
            """));

        Assert.Contains("active.response.percent", ex.Message);
        Assert.Contains("超出帧长", ex.Message);
    }

    [Fact]
    public void LoadFromJson_ActiveResponsePastLengthMin_Throws()
    {
        var ex = ThrowsActive(ActiveOf(4, """
            {
              "length": { "min": 8 },
              "fields": { "percent": { "offset": 8, "type": "u8" } }
            }
            """));

        Assert.Contains("active.response.percent", ex.Message);
    }

    [Fact]
    public void LoadFromJson_ResponseWithoutLength_IgnoresInputLength()
    {
        var bundle = LoadActive(
            ActiveOf(4, """
                {
                  "tryOffsets": [0, 1],
                  "fields": { "percent": { "offset": 100, "type": "u8" } }
                }
                """),
            """{ "usagePage": 1, "inputLength": { "eq": 8 } }""");

        var response = bundle.Protocols["p"].Active!.Response;
        Assert.Null(response.Length);
        Assert.Equal(100, response.Percent!.Offset);
        Assert.Equal(new[] { 0, 1 }, response.TryOffsets);
    }

    [Fact]
    public void LoadFromJson_QueryWithinOutputLengthEq_Loads()
    {
        var bundle = LoadActive(
            ActiveOf(8, """{ "fields": { "percent": { "offset": 0, "type": "u8" } } }"""),
            """{ "usagePage": 1, "outputLength": { "eq": 8 } }""");

        Assert.Equal(8, bundle.Protocols["p"].Active!.Query.Length);
    }

    [Fact]
    public void LoadFromJson_QueryPastOutputLengthEq_Throws()
    {
        var ex = ThrowsActive(
            ActiveOf(9, """{ "fields": { "percent": { "offset": 0, "type": "u8" } } }"""),
            """{ "usagePage": 1, "outputLength": { "eq": 8, "min": 100 } }""");

        Assert.Contains("active.query", ex.Message);
        Assert.Contains("outputLength", ex.Message);
    }

    [Fact]
    public void LoadFromJson_OutputLengthMin_DoesNotBoundQuery()
    {
        var bundle = LoadActive(
            ActiveOf(65, """{ "fields": { "percent": { "offset": 8, "type": "u8" } } }"""),
            """{ "usagePage": 1, "outputLength": { "min": 8 }, "inputLength": { "eq": 8 } }""");

        Assert.Equal(65, bundle.Protocols["p"].Active!.Query.Length);
        Assert.Null(bundle.Protocols["p"].Interface.OutputLength!.Value.Eq);
    }

    [Fact]
    public void LoadFromJson_TryOffsetsPrimaryOutOfRange_Throws()
    {
        // 主分支是 1；若改去验更小的 0，偏移 7 会误放行
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 8 },
              "tryOffsets": [1, 0],
              "fields": { "percent": { "offset": 7, "type": "u8" } }
            }
            """);

        Assert.Contains("TryOffsets[0]=1", ex.Message);
        Assert.Contains("passive.percent", ex.Message);
    }

    [Fact]
    public void LoadFromJson_TryOffsetsFallbackOutOfRange_KeepsOrder()
    {
        // 回退分支 5 越界，主分支 0 不越界：必须放行，且顺序不动
        var bundle = LoadPassive("""
            {
              "length": { "eq": 8 },
              "tryOffsets": [0, 5],
              "fields": { "percent": { "offset": 7, "type": "u8" } }
            }
            """);

        Assert.Equal(new[] { 0, 5 }, bundle.Protocols["p"].Passive!.TryOffsets);
    }

    [Fact]
    public void LoadFromJson_TryOffsets_NotSortedOrDeduped()
    {
        var bundle = LoadPassive("""
            {
              "length": { "eq": 16 },
              "tryOffsets": [2, 0, 2],
              "fields": { "percent": { "offset": 1, "type": "u8" } }
            }
            """);

        Assert.Equal(new[] { 2, 0, 2 }, bundle.Protocols["p"].Passive!.TryOffsets);
    }

    [Fact]
    public void LoadFromJson_EmptyTryOffsets_UsesDefaultPair()
    {
        // [] 与缺省相同，不能原样留下，否则读取循环进不去
        var bundle = LoadPassive("""
            {
              "length": { "eq": 8 },
              "tryOffsets": [],
              "fields": { "percent": { "offset": 0, "type": "u8" } }
            }
            """);

        Assert.Equal(new[] { 0, 1 }, bundle.Protocols["p"].Passive!.TryOffsets);
    }

    [Fact]
    public void LoadFromJson_NegativeFieldOffset_ThrowsEvenWhenPrimaryCompensates()
    {
        // 主分支 5 把 -1 加成 4，仍在帧内；字段 offset 为负必须照旧拒绝
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 8 },
              "tryOffsets": [5],
              "fields": { "percent": { "offset": -1, "type": "u8" } }
            }
            """);

        Assert.Contains("passive.percent", ex.Message);
        Assert.Contains("偏移 -1 为负", ex.Message);
    }

    [Fact]
    public void LoadFromJson_NoLength_NegativePrimary_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "tryOffsets": [-1, 0],
              "fields": { "percent": { "offset": 0, "type": "u8" } }
            }
            """);

        Assert.Contains("TryOffsets[0]=-1", ex.Message);
        Assert.Contains("为负", ex.Message);
    }

    [Fact]
    public void LoadFromJson_NoLength_NegativeFieldOffset_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "fields": { "percent": { "offset": -1, "type": "u8" } }
            }
            """);

        Assert.Contains("passive.percent", ex.Message);
        Assert.Contains("偏移 -1 为负", ex.Message);
    }

    [Fact]
    public void LoadFromJson_EmptyAny_NegativePrimary_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "length": [],
              "tryOffsets": [-2, 0],
              "fields": { "percent": { "offset": 1, "type": "u8" } }
            }
            """);

        Assert.Contains("TryOffsets[0]=-2", ex.Message);
        Assert.Contains("为负", ex.Message);
    }

    [Fact]
    public void LoadFromJson_EmptyAny_NegativeFieldOffset_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "length": [],
              "tryOffsets": [0],
              "fields": { "percent": { "offset": -3, "type": "u8" } }
            }
            """);

        Assert.Contains("passive.percent", ex.Message);
        Assert.Contains("偏移 -3 为负", ex.Message);
    }

    [Fact]
    public void LoadFromJson_NegativeTryOffsetPrimary_Throws()
    {
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 8 },
              "tryOffsets": [-1, 0],
              "fields": { "percent": { "offset": 0, "type": "u8" } }
            }
            """);

        Assert.Contains("TryOffsets[0]=-1", ex.Message);
        Assert.Contains("为负", ex.Message);
    }

    [Fact]
    public void LoadFromJson_OffsetPlusWidthOverflow_Throws()
    {
        // int 相加会回绕成负数，比较「> 帧长」会放行
        var ex = ThrowsPassive("""
            {
              "length": { "eq": 2147483647 },
              "tryOffsets": [0],
              "fields": { "percent": { "offset": 2147483647, "type": "u8" } }
            }
            """);

        Assert.Contains("超出帧长", ex.Message);
    }

    [Fact]
    public void LoadFromJson_EmptyLengthArray_PreservesEmptyAny()
    {
        var bundle = LoadPassive("""
            {
              "length": [],
              "fields": { "percent": { "offset": 100, "type": "u8" } }
            }
            """);

        var length = bundle.Protocols["p"].Passive!.Length!.Value;
        Assert.NotNull(length.Any);
        Assert.Empty(length.Any);
        Assert.False(length.Matches(0));
        Assert.False(length.Matches(19));
        Assert.Null(ProtocolSpecLoader.TryGetMinimumAcceptedValue(length));
        Assert.Equal(100, bundle.Protocols["p"].Passive!.Percent!.Offset);
    }

    [Fact]
    public void LoadFromJson_ObjectFormAny_StillRejected()
    {
        var ex = ThrowsPassive("""
            {
              "length": { "any": [] },
              "fields": { "percent": { "offset": 1, "type": "u8" } }
            }
            """);

        Assert.Contains("空对象", ex.Message);
        Assert.Contains("passive.length", ex.Message);
    }

    [Fact]
    public void TryGetMinimumAcceptedValue_EmptyAny_ReturnsNull()
    {
        var match = new IntMatch(19, 1, 100, Array.Empty<int>(), null);

        Assert.Null(ProtocolSpecLoader.TryGetMinimumAcceptedValue(match));
        Assert.False(match.Matches(19));
    }

    [Fact]
    public void EmptyAny_AgreesWithIntMatchMatches()
    {
        var empty = new IntMatch(19, 1, 100, Array.Empty<int>(), null);
        Assert.False(empty.Matches(19));
        Assert.False(empty.Matches(1));
        Assert.False(empty.Matches(0));
        Assert.Null(ProtocolSpecLoader.TryGetMinimumAcceptedValue(empty));

        var any = new IntMatch(19, 1, 100, [40, 10], null);
        Assert.True(any.Matches(10));
        Assert.True(any.Matches(40));
        Assert.False(any.Matches(19));
        Assert.False(any.Matches(1));
        Assert.Equal(10, ProtocolSpecLoader.TryGetMinimumAcceptedValue(any));

        var eq = new IntMatch(19, 1, 100, null, null);
        Assert.True(eq.Matches(19));
        Assert.False(eq.Matches(1));
        Assert.Equal(19, ProtocolSpecLoader.TryGetMinimumAcceptedValue(eq));

        var minOnly = new IntMatch(null, 8, null, null, null);
        Assert.True(minOnly.Matches(8));
        Assert.False(minOnly.Matches(7));
        Assert.Equal(8, ProtocolSpecLoader.TryGetMinimumAcceptedValue(minOnly));
    }

    private static SpecBundle LoadPassive(string frame, string? interfaceJson = null)
    {
        var iface = interfaceJson ?? """{ "usagePage": 1 }""";
        var protocols = $$"""
            {
              "protocols": {
                "p": {
                  "kind": "mouse",
                  "interface": {{iface}},
                  "passive": {{frame}}
                }
              }
            }
            """;
        return ProtocolSpecLoader.LoadFromJson(protocols, EmptyDevices);
    }

    private static SpecBundle LoadActive(string active, string? interfaceJson = null)
    {
        var iface = interfaceJson ?? """{ "usagePage": 1 }""";
        var protocols = $$"""
            {
              "protocols": {
                "p": {
                  "kind": "mouse",
                  "interface": {{iface}},
                  "active": {{active}}
                }
              }
            }
            """;
        return ProtocolSpecLoader.LoadFromJson(protocols, EmptyDevices);
    }

    private static string ActiveOf(int queryLength, string response) => $$"""
        {
          "query": { "length": {{queryLength}}, "hex": "01" },
          "response": {{response}}
        }
        """;

    private static InvalidOperationException ThrowsPassive(string frame, string? interfaceJson = null)
        => Assert.Throws<InvalidOperationException>(() => LoadPassive(frame, interfaceJson));

    private static InvalidOperationException ThrowsActive(string active, string? interfaceJson = null)
        => Assert.Throws<InvalidOperationException>(() => LoadActive(active, interfaceJson));
}
