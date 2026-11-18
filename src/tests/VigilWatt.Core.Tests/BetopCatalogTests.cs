using System.Text.Json;
using System.Text.Json.Nodes;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Betop;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 北通品牌目录——PID 命中、型号歧义、显示名、身份隔离与能力声明。
/// 2026/11/16 12:00:00 20211
/// 目录来源与逐型号验证状态见 docs/hardware/reference/betop-catalog.md。
/// </summary>
public class BetopCatalogTests
{
    private const string CatalogResource = "VigilWatt.Core.Plugins.Betop.betop-catalog.json";

    private static readonly ProtocolEngine Engine = ProtocolEngine.Load(
        "VigilWatt.Core.Plugins.Betop.specs.betop.protocols.json",
        CatalogResource);

    /// <summary>P1-B2 之前的 33 条目录快照；只允许追加和改 20BC 的 protocol。</summary>
private static readonly string[] FrozenOriginalDevices =
    [
        "0x20bc|0x5073|宙斯2|gamepad|controller|betop-zeus-keyevent||||batteryRead=supported;chargingRead=unsupported|本机真机；Col04 被动状态流，无充电标志字节",
        "0x20bc|0x5125||gamepad|controller|betop-status15-nocharge|||||xpad 标 KP20A/KP40A 共用；官方注册还含 KUNPThirty（西樵山项目）；型号由产品串判定，电量未验证",
        "0x20bc|0x5126|鲲鹏20智控游戏手柄|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP20A；电量未验证",
        "0x20bc|0x5127||gamepad|controller|betop-status15-nocharge|||||xpad 标 KP20A/KP40A 共用；型号由产品串判定，电量未验证",
        "0x20bc|0x5128|鲲鹏20智控游戏手柄|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP20A；电量未验证",
        "0x20bc|0x5158|鲲鹏20 绝尘白|gamepad|controller|betop-status15|kunpeng20|0x5159|true|batteryRead=supported;chargingRead=supported|贡献者真机，有线 02 15 查询 78% 充电中",
        "0x20bc|0x5159|鲲鹏20 绝尘白|gamepad|controller|betop-status15|kunpeng20|0x5158|false|batteryRead=supported;chargingRead=supported|贡献者真机，2.4G 杂项页每秒广播",
        "0x20bc|0x5191|鲲鹏20精英版-丁未|gamepad|controller|betop-status15|||||KP20EB；上游实现（oitsukiii/betop-battery）在该型号真机验证 02 15 查询，本机未复测",
        "0x20bc|0x5145||gamepad|controller|betop-status15-nocharge|||||xpad 标 KP40A/KP40B 共用；型号由产品串判定，电量未验证",
        "0x20bc|0x5146||gamepad|controller|betop-status15-nocharge|||||xpad 标 KP40A/KP40B 共用；型号由产品串判定，电量未验证",
        "0x20bc|0x5154|鲲鹏40 明末联动款|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP40D；电量未验证",
        "0x20bc|0x5155|鲲鹏40 明末联动款|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP40D；电量未验证",
        "0x20bc|0x515b|鲲鹏40 御铠白|gamepad|controller|betop-status15|||||xpad 标 KP40D White；电量未验证",
        "0x20bc|0x515c|鲲鹏40 御铠白|gamepad|controller|betop-status15|||||xpad 标 KP40D White；电量未验证",
        "0x20bc|0x515d|鲲鹏40智控游戏手柄 风驰白|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP40F White；电量未验证",
        "0x20bc|0x515e|鲲鹏40智控游戏手柄 风驰白|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP40F White；电量未验证",
        "0x20bc|0x5169|鲲鹏40 电掣黑|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP40F Black；电量未验证",
        "0x20bc|0x516a|鲲鹏40 电掣黑|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP40F Black；电量未验证",
        "0x20bc|0x5133|鲲鹏50 云翼白|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP50B；电量未验证",
        "0x20bc|0x5149|鲲鹏50 星穹紫|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP50C；电量未验证",
        "0x20bc|0x5150|鲲鹏50智控游戏手柄 解限机联名款|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP50D；电量未验证",
        "0x20bc|0x5151|鲲鹏50智控游戏手柄 解限机联名款|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP50D；电量未验证",
        "0x20bc|0x5152|鲲鹏50智控游戏手柄 解限机联名款|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP50E；电量未验证",
        "0x20bc|0x5153|鲲鹏50智控游戏手柄 解限机联名款|gamepad|controller|betop-status15-nocharge|||||xpad 标 KP50E；电量未验证",
        "0x20bc|0x5134|鲲鹏50 云翼白|unknown|dongle|betop-status15-nocharge|||||xpad 标 KP50B XInput Dongle；电量未验证",
        "0x20bc|0x514a|鲲鹏50 星穹紫|unknown|dongle|betop-status15-nocharge|||||xpad 标 KP50C XInput Dongle；电量未验证",
        "0x20bc|0x512f|鲲鹏70 AI旗舰|gamepad|controller|betop-status15|||||xpad 标 KP70A；电量未验证",
        "0x20bc|0x5130|鲲鹏70 AI旗舰|gamepad|controller|betop-status15|||||xpad 标 KP70A XInput Dongle；用户日志广播 02 15 80%",
        "0x20bc|0x515f|鲲鹏70AI智控旗舰手柄|gamepad|controller|betop-status15|||||xpad 标 KP70A，官方注册为 BTP-KP70B（鲲鹏70AI智控旗舰手柄）；电量未验证",
        "0x20bc|0x5160|鲲鹏70AI智控旗舰手柄|gamepad|controller|betop-status15|||||xpad 标 KP70A，官方注册为 BTP-KP70B（鲲鹏70AI智控旗舰手柄）；电量未验证",
        "0x20dd|0x5128|鲲鹏20智控游戏手柄|unknown|dongle|betop-status15|||||KP20 DONGLE；只回 02 CD（cmd 0xD 全局配置，不是电量帧；CR-106 待结案）",
        "0x20dd|0x5130|鲲鹏70 AI旗舰|unknown|dock|betop-status15|||||BTP-KP70A DONGLE 底座；只回 02 CD（cmd 0xD 全局配置，不是电量帧；CR-106 待结案）",
        "0x20dd|0x515c|鲲鹏40 御铠白|unknown|dongle|betop-status15|||||xpad 的 KP40D HID receiver；电量未验证",
    ];

    private static HidDeviceInfo Info(
        ushort vid, ushort pid, ushort page, ushort usage = 0x0003, ushort length = 64, string product = "")
        => new()
        {
            DevicePath = $"test-{vid:X4}-{pid:X4}-{page:X4}-{usage:X4}-{length}",
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = usage,
            InputReportLength = length,
            OutputReportLength = length,
            Product = product,
        };

    /// <summary>officialDevices 不参与加载，只能直接读同一份内嵌目录。</summary>
    private static JsonObject Catalog()
    {
        using var stream = typeof(BetopCatalogTests).Assembly.GetManifestResourceStream(CatalogResource)
            ?? throw new InvalidOperationException($"嵌入资源缺失: {CatalogResource}");
        return JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new InvalidOperationException($"{CatalogResource} 根节点不是对象");
    }

    private static IEnumerable<JsonNode> Devices()
        => Catalog()["devices"]!.AsArray().Select(d => d!);

    private static ushort Hex(JsonNode? node) => Convert.ToUInt16(node!.GetValue<string>()[2..], 16);

    private static (ushort Page, ushort Usage, ushort Length) InterfaceOf(JsonNode device)
        => device["protocol"]!.GetValue<string>() == "betop-zeus-keyevent"
            ? ((ushort)0xFF00, (ushort)0x02, (ushort)24)
            : ((ushort)0x00FF, (ushort)0x03, (ushort)64);

    private static string DeviceKey(JsonNode? device)
        => device!["vid"]!.GetValue<string>() + ":" + device["pid"]!.GetValue<string>();

    /// <summary>一条目录记录的全部字段拼成一行，供"原有条目不许变"的快照比对。</summary>
    private static string Signature(JsonNode? device)
    {
        var node = device!.AsObject();
        string Text(string name) => node.TryGetPropertyValue(name, out var value) ? value!.GetValue<string>() : "";
        var wired = node.TryGetPropertyValue("wired", out var wiredNode)
            ? (wiredNode!.GetValue<bool>() ? "true" : "false")
            : "";
        var caps = node.TryGetPropertyValue("capabilities", out var capsNode)
            ? string.Join(";", capsNode!.AsObject().Select(p => $"{p.Key}={p.Value!.GetValue<string>()}"))
            : "";
        return string.Join("|", Text("vid"), Text("pid"), Text("model"), Text("kind"), Text("role"),
            Text("protocol"), Text("group"), Text("pairedPid"), wired, caps, Text("note"));
    }

    [Fact]
    public void Catalog_CountsAndProtocolsLoad()
    {
        Assert.Equal(110, Engine.DeviceCount);
        Assert.Equal(3, Engine.ProtocolCount);
        Assert.NotNull(Engine.SpecOf("betop-status15"));
        Assert.NotNull(Engine.SpecOf("betop-status15-nocharge"));
        Assert.NotNull(Engine.SpecOf("betop-zeus-keyevent"));
    }

    /// <summary>官方 50 个鲲鹏 PID 全部进运行目录，20BC/20DD 两条身份；20DD role 按官方映射。</summary>
    [Fact]
    public void Catalog_OfficialKunpengPidsCoveredOnBothVids()
    {
        var devices = Devices().ToList();
        Assert.Equal(110, devices.Count);
        Assert.Equal(devices.Count, devices.Select(d => (d["vid"]!.GetValue<string>(), d["pid"]!.GetValue<string>()))
            .Distinct().Count());

        var official = Catalog()["officialDevices"]!.AsArray();
        Assert.Equal(66, official.Count);
        var kunpeng = official.Where(d => d!["protocolFamily"]!.GetValue<string>() == "kunpeng").ToList();
        Assert.Equal(50, kunpeng.Count);

        foreach (var entry in kunpeng)
        {
            var node = entry!;
            var pid = node["pid"]!.GetValue<string>();
            var hits = devices.Where(d => d["pid"]!.GetValue<string>() == pid).ToList();
            Assert.Equal(2, hits.Count);

            var titles = node["officialTitles"]!.AsArray().Select(t => t!.GetValue<string>()).ToList();
            var model = titles.Count != 1 ? ""
                : titles[0].StartsWith("北通") ? titles[0]["北通".Length..] : titles[0];

            foreach (var hit in hits)
            {
                var vid = hit["vid"]!.GetValue<string>();
                var identity = Assert.Single(node["usbIdentities"]!.AsArray(),
                    i => i!["vid"]!.GetValue<string>() == vid);
                Assert.Equal(model, hit["model"]!.GetValue<string>());

                var hid = Assert.Single(identity!["hidInterfaces"]!.AsArray());
                Assert.Equal("0x00ff", hid!["usagePage"]!.GetValue<string>());
                Assert.Equal("0x03", hid["usage"]!.GetValue<string>());
                Assert.Equal(64, hid["reportLength"]!.GetValue<int>());

                if (vid == "0x20dd")
                {
                    Assert.Equal("unknown", hit["kind"]!.GetValue<string>());
                    var officialRole = identity["role"]!.GetValue<string>();
                    var expectedRole = pid == "0x5130"
                        ? "dock"
                        : officialRole == "wired" ? "controller" : officialRole;
                    Assert.Equal(expectedRole, hit["role"]!.GetValue<string>());
                    Assert.Null(hit["capabilities"]);
                    Assert.Null(hit["group"]);
                    Assert.Null(hit["pairedPid"]);
                }
            }
        }
    }

    /// <summary>原有 33 条：30 条 20BC 只改 protocol，3 条 20DD 完全不变。</summary>
    [Fact]
    public void Catalog_OriginalEntriesKeepTheirFields()
    {
        var devices = Catalog()["devices"]!.AsArray();
        Assert.Equal(33, FrozenOriginalDevices.Length);

        foreach (var expected in FrozenOriginalDevices)
        {
            var parts = expected.Split('|', 3);
            var key = parts[0] + ":" + parts[1];
            var hit = Assert.Single(devices, d => DeviceKey(d) == key);
            Assert.Equal(expected, Signature(hit));
        }
    }

    [Fact]
    public void Catalog_EveryPidResolves()
    {
        foreach (var device in Devices())
        {
            var vid = Hex(device["vid"]);
            var pid = Hex(device["pid"]);
            var (page, usage, length) = InterfaceOf(device);
            var info = Info(vid, pid, page, usage, length);
            var entry = Engine.DeviceOf(info);

            Assert.NotNull(entry);
            Assert.True(Engine.Matches(info), $"{vid:X4}:{pid:X4} 未命中");
            Assert.Equal(device["model"]!.GetValue<string>(), entry!.Model);
        }
    }

    [Fact]
    public void Catalog_SamePidDifferentVidIsDistinct()
    {
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5128, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5130, 0x00FF))!.Role);
        Assert.Equal("dock", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x515C, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x515C, 0x00FF))!.Role);

        Assert.Equal("鲲鹏20智控游戏手柄", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Model);
        Assert.Equal("鲲鹏70 AI旗舰", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Model);
        Assert.Equal("鲲鹏40 御铠白", Engine.DeviceOf(Info(0x20DD, 0x515C, 0x00FF))!.Model);

        // 新回填的 20DD：role 按官方 wired→controller / dongle→dongle。
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20DD, 0x5158, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x5159, 0x00FF))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20DD, 0x5125, 0x00FF))!.Role);
    }

    [Fact]
    public void Catalog_RolesLoad()
    {
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5073, 0xFF00, 0x02, 24))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5158, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20BC, 0x5134, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Role);
        Assert.Equal("dock", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20DD, 0x5158, 0x00FF))!.Role);
    }

    [Theory]
    [InlineData("BTP-KP20A/BTP-KP40A Controller", "鲲鹏20/40")]
    [InlineData("BTP-KP20A Controller", "鲲鹏20")]
    [InlineData("BTP-KP40A Controller", "鲲鹏40")]
    [InlineData("BETOP BTP-KP40D Controller (White)", "鲲鹏40")]
    [InlineData("BEITONG T9 For Keyboard & Mouse", "T9")]
    [InlineData("BETOP T90 Controller", null)]
    public void Display_AmbiguousPidUsesProductString(string product, string? expectedModel)
    {
        Assert.Equal(expectedModel, BetopDisplayName.ModelFromProduct(product));
    }

    [Fact]
    public void Display_CatalogModelWinsAndSuffixesDoNotBecomeModel()
    {
        var t9 = Info(0x20BC, 0x5073, 0xFF00, 0x02, 24, "BEITONG T9 For Keyboard & Mouse");
        Assert.Equal("北通 宙斯2", BetopDisplayName.For(t9, Engine.DeviceOf(t9)));

        var kp20 = Info(0x20BC, 0x5159, 0x00FF, 0x03, 64, "BTP-KP20D XINPUT DONGLE");
        Assert.Equal("北通 鲲鹏20 绝尘白", BetopDisplayName.For(kp20, Engine.DeviceOf(kp20)));

        var dongle = Info(0x20DD, 0x5128, 0x00FF, 0x03, 64, "KP20 DONGLE");
        Assert.Equal("北通 鲲鹏20智控游戏手柄", BetopDisplayName.For(dongle, Engine.DeviceOf(dongle)));

        var unknown = Info(0x20BC, 0x9999, 0xFF00, 0x03, 64, "BEITONG XF1 For Keyboard & Mouse");
        Assert.Equal("北通手柄 (VID_20BC&PID_9999)", BetopDisplayName.For(unknown, Engine.DeviceOf(unknown)));
    }

    [Fact]
    public void Identity_OnlyExplicitGroupIsMerged()
    {
        var wired = Info(0x20BC, 0x5158, 0x00FF);
        var wireless = Info(0x20BC, 0x5159, 0x00FF);
        Assert.Equal("betop:kunpeng20", BetopDeviceIdentity.GroupId(Engine, wired, "betop"));
        Assert.Equal("betop:kunpeng20", BetopDeviceIdentity.GroupId(Engine, wireless, "betop"));

        Assert.Null(BetopDeviceIdentity.GroupId(Engine, Info(0x20BC, 0x5126, 0x00FF), "betop"));
        Assert.Null(BetopDeviceIdentity.GroupId(Engine, Info(0x20BC, 0x5128, 0x00FF), "betop"));
        Assert.Null(BetopDeviceIdentity.GroupId(Engine, Info(0x20DD, 0x5128, 0x00FF), "betop"));
    }

    [Fact]
    public void Capabilities_OnlyVerifiedEntriesDeclareBatteryRead()
    {
        foreach (var device in Devices())
        {
            var vid = Hex(device["vid"]);
            var pid = Hex(device["pid"]);
            var (page, usage, length) = InterfaceOf(device);
            var info = Info(vid, pid, page, usage, length);
            var expected = vid == 0x20BC && pid is 0x5073 or 0x5158 or 0x5159
                ? CapabilityState.Supported
                : CapabilityState.Unverified;
            Assert.Equal(expected, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
        }

        Assert.Equal(CapabilityState.Unsupported,
            Engine.CapabilityOf(Info(0x20BC, 0x5073, 0xFF00, 0x02, 24), DeviceCapability.ChargingRead));
        Assert.Equal(CapabilityState.Supported,
            Engine.CapabilityOf(Info(0x20BC, 0x5158, 0x00FF), DeviceCapability.ChargingRead));
        Assert.Equal(CapabilityState.Supported,
            Engine.CapabilityOf(Info(0x20BC, 0x5159, 0x00FF), DeviceCapability.ChargingRead));
    }

    [Fact]
    public void UnknownPidFallsBackAndUnknownVendorIsRejected()
    {
        // 未收录 PID 仍按接口三元组回落：0x00FF/0x03/64 命中鲲鹏协议。
        var unknownBetop = Info(0x20BC, 0x9999, 0x00FF);
        Assert.Null(Engine.DeviceOf(unknownBetop));
        Assert.True(Engine.Matches(unknownBetop));
        Assert.Null(BetopDeviceIdentity.GroupId(Engine, unknownBetop, "betop"));

        Assert.False(Engine.Matches(Info(0x1234, 0x9999, 0xFF00)));
    }
}
