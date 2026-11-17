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

    /// <summary>P1-B2 新增的 21 个鲲鹏 PID（官方 50 个减去原运行目录 29 个）。</summary>
    private static readonly string[] NewKunpengPids =
    [
        "0x5163", "0x5164", "0x5165", "0x5166", "0x516d", "0x516e", "0x516f", "0x5170",
        "0x5173", "0x5174", "0x518a", "0x518b", "0x518c", "0x518d", "0x5190", "0x5194",
        "0x5195", "0x519b", "0x519c", "0x519d", "0x519e",
    ];

    /// <summary>P1-B2 之前的 33 条目录快照；只允许追加，任何字段变化都要在这里显式改。</summary>
    private static readonly string[] FrozenOriginalDevices =
    [
        "0x20bc|0x5073|宙斯2|gamepad|controller|betop-t9||||batteryRead=supported;chargingRead=unsupported|本机真机；Col04 被动状态流，无充电标志字节",
        "0x20bc|0x5125||gamepad|controller|betop-status15|||||xpad 标 KP20A/KP40A 共用；官方注册还含 KUNPThirty（西樵山项目）；型号由产品串判定，电量未验证",
        "0x20bc|0x5126|鲲鹏20智控游戏手柄|gamepad|controller|betop-status15|||||xpad 标 KP20A；电量未验证",
        "0x20bc|0x5127||gamepad|controller|betop-status15|||||xpad 标 KP20A/KP40A 共用；型号由产品串判定，电量未验证",
        "0x20bc|0x5128|鲲鹏20智控游戏手柄|gamepad|controller|betop-status15|||||xpad 标 KP20A；电量未验证",
        "0x20bc|0x5158|鲲鹏20 绝尘白|gamepad|controller|betop-status15|kunpeng20|0x5159|true|batteryRead=supported;chargingRead=supported|贡献者真机，有线 02 15 查询 78% 充电中",
        "0x20bc|0x5159|鲲鹏20 绝尘白|gamepad|controller|betop-status15|kunpeng20|0x5158|false|batteryRead=supported;chargingRead=supported|贡献者真机，2.4G 杂项页每秒广播",
        "0x20bc|0x5191|鲲鹏20精英版-丁未|gamepad|controller|betop-status15|||||KP20EB；上游实现（oitsukiii/betop-battery）在该型号真机验证 02 15 查询，本机未复测",
        "0x20bc|0x5145||gamepad|controller|betop-status15|||||xpad 标 KP40A/KP40B 共用；型号由产品串判定，电量未验证",
        "0x20bc|0x5146||gamepad|controller|betop-status15|||||xpad 标 KP40A/KP40B 共用；型号由产品串判定，电量未验证",
        "0x20bc|0x5154|鲲鹏40 明末联动款|gamepad|controller|betop-status15|||||xpad 标 KP40D；电量未验证",
        "0x20bc|0x5155|鲲鹏40 明末联动款|gamepad|controller|betop-status15|||||xpad 标 KP40D；电量未验证",
        "0x20bc|0x515b|鲲鹏40 御铠白|gamepad|controller|betop-status15|||||xpad 标 KP40D White；电量未验证",
        "0x20bc|0x515c|鲲鹏40 御铠白|gamepad|controller|betop-status15|||||xpad 标 KP40D White；电量未验证",
        "0x20bc|0x515d|鲲鹏40智控游戏手柄 风驰白|gamepad|controller|betop-status15|||||xpad 标 KP40F White；电量未验证",
        "0x20bc|0x515e|鲲鹏40智控游戏手柄 风驰白|gamepad|controller|betop-status15|||||xpad 标 KP40F White；电量未验证",
        "0x20bc|0x5169|鲲鹏40 电掣黑|gamepad|controller|betop-status15|||||xpad 标 KP40F Black；电量未验证",
        "0x20bc|0x516a|鲲鹏40 电掣黑|gamepad|controller|betop-status15|||||xpad 标 KP40F Black；电量未验证",
        "0x20bc|0x5133|鲲鹏50 云翼白|gamepad|controller|betop-status15|||||xpad 标 KP50B；电量未验证",
        "0x20bc|0x5149|鲲鹏50 星穹紫|gamepad|controller|betop-status15|||||xpad 标 KP50C；电量未验证",
        "0x20bc|0x5150|鲲鹏50智控游戏手柄 解限机联名款|gamepad|controller|betop-status15|||||xpad 标 KP50D；电量未验证",
        "0x20bc|0x5151|鲲鹏50智控游戏手柄 解限机联名款|gamepad|controller|betop-status15|||||xpad 标 KP50D；电量未验证",
        "0x20bc|0x5152|鲲鹏50智控游戏手柄 解限机联名款|gamepad|controller|betop-status15|||||xpad 标 KP50E；电量未验证",
        "0x20bc|0x5153|鲲鹏50智控游戏手柄 解限机联名款|gamepad|controller|betop-status15|||||xpad 标 KP50E；电量未验证",
        "0x20bc|0x5134|鲲鹏50 云翼白|unknown|dongle|betop-status15|||||xpad 标 KP50B XInput Dongle；电量未验证",
        "0x20bc|0x514a|鲲鹏50 星穹紫|unknown|dongle|betop-status15|||||xpad 标 KP50C XInput Dongle；电量未验证",
        "0x20bc|0x512f|鲲鹏70 AI旗舰|gamepad|controller|betop-status15|||||xpad 标 KP70A；电量未验证",
        "0x20bc|0x5130|鲲鹏70 AI旗舰|gamepad|controller|betop-status15|||||xpad 标 KP70A XInput Dongle；用户日志广播 02 15 80%",
        "0x20bc|0x515f|鲲鹏70AI智控旗舰手柄|gamepad|controller|betop-status15|||||xpad 标 KP70A，官方注册为 BTP-KP70B（鲲鹏70AI智控旗舰手柄）；电量未验证",
        "0x20bc|0x5160|鲲鹏70AI智控旗舰手柄|gamepad|controller|betop-status15|||||xpad 标 KP70A，官方注册为 BTP-KP70B（鲲鹏70AI智控旗舰手柄）；电量未验证",
        "0x20dd|0x5128|鲲鹏20智控游戏手柄|unknown|dongle|betop-status15|||||KP20 DONGLE；只回 02 CD（cmd 0xD 全局配置，不是电量帧；CR-106 待结案）",
        "0x20dd|0x5130|鲲鹏70 AI旗舰|unknown|dock|betop-status15|||||BTP-KP70A DONGLE 底座；只回 02 CD（cmd 0xD 全局配置，不是电量帧；CR-106 待结案）",
        "0x20dd|0x515c|鲲鹏40 御铠白|unknown|dongle|betop-status15|||||xpad 的 KP40D HID receiver；电量未验证",
    ];

    private static readonly (ushort Vid, ushort Pid, ushort Page, string Model)[] Rows =
    [
        (0x20BC, 0x5073, 0xFF00, "宙斯2"),
        (0x20BC, 0x5125, 0x00FF, ""),
        (0x20BC, 0x5126, 0x00FF, "鲲鹏20智控游戏手柄"),
        (0x20BC, 0x5127, 0x00FF, ""),
        (0x20BC, 0x5128, 0x00FF, "鲲鹏20智控游戏手柄"),
        (0x20BC, 0x5158, 0x00FF, "鲲鹏20 绝尘白"),
        (0x20BC, 0x5159, 0x00FF, "鲲鹏20 绝尘白"),
        (0x20BC, 0x5191, 0x00FF, "鲲鹏20精英版-丁未"),
        (0x20BC, 0x5145, 0x00FF, ""),
        (0x20BC, 0x5146, 0x00FF, ""),
        (0x20BC, 0x5154, 0x00FF, "鲲鹏40 明末联动款"),
        (0x20BC, 0x5155, 0x00FF, "鲲鹏40 明末联动款"),
        (0x20BC, 0x515B, 0x00FF, "鲲鹏40 御铠白"),
        (0x20BC, 0x515C, 0x00FF, "鲲鹏40 御铠白"),
        (0x20BC, 0x515D, 0x00FF, "鲲鹏40智控游戏手柄 风驰白"),
        (0x20BC, 0x515E, 0x00FF, "鲲鹏40智控游戏手柄 风驰白"),
        (0x20BC, 0x5169, 0x00FF, "鲲鹏40 电掣黑"),
        (0x20BC, 0x516A, 0x00FF, "鲲鹏40 电掣黑"),
        (0x20BC, 0x5133, 0x00FF, "鲲鹏50 云翼白"),
        (0x20BC, 0x5149, 0x00FF, "鲲鹏50 星穹紫"),
        (0x20BC, 0x5150, 0x00FF, "鲲鹏50智控游戏手柄 解限机联名款"),
        (0x20BC, 0x5151, 0x00FF, "鲲鹏50智控游戏手柄 解限机联名款"),
        (0x20BC, 0x5152, 0x00FF, "鲲鹏50智控游戏手柄 解限机联名款"),
        (0x20BC, 0x5153, 0x00FF, "鲲鹏50智控游戏手柄 解限机联名款"),
        (0x20BC, 0x5134, 0x00FF, "鲲鹏50 云翼白"),
        (0x20BC, 0x514A, 0x00FF, "鲲鹏50 星穹紫"),
        (0x20BC, 0x512F, 0x00FF, "鲲鹏70 AI旗舰"),
        (0x20BC, 0x5130, 0x00FF, "鲲鹏70 AI旗舰"),
        (0x20BC, 0x515F, 0x00FF, "鲲鹏70AI智控旗舰手柄"),
        (0x20BC, 0x5160, 0x00FF, "鲲鹏70AI智控旗舰手柄"),
        (0x20DD, 0x5128, 0x00FF, "鲲鹏20智控游戏手柄"),
        (0x20DD, 0x5130, 0x00FF, "鲲鹏70 AI旗舰"),
        (0x20DD, 0x515C, 0x00FF, "鲲鹏40 御铠白"),
        (0x20BC, 0x516F, 0x00FF, "鲲鹏20战双帕弥什联名款"),
        (0x20BC, 0x5170, 0x00FF, "鲲鹏20战双帕弥什联名款"),
        (0x20BC, 0x5190, 0x00FF, "鲲鹏20精英版-丁未"),
        (0x20BC, 0x5163, 0x00FF, "鲲鹏40鸣潮联名款"),
        (0x20BC, 0x5164, 0x00FF, "鲲鹏40鸣潮联名款"),
        (0x20BC, 0x5165, 0x00FF, "鲲鹏40鸣潮联名款"),
        (0x20BC, 0x5166, 0x00FF, "鲲鹏40鸣潮联名款"),
        (0x20BC, 0x5194, 0x00FF, "鲲鹏40 精英版"),
        (0x20BC, 0x5195, 0x00FF, "鲲鹏40 精英版"),
        (0x20BC, 0x516D, 0x00FF, "鲲鹏70 午马限定款"),
        (0x20BC, 0x516E, 0x00FF, "鲲鹏70 午马限定款"),
        (0x20BC, 0x5173, 0x00FF, "鲲鹏70 6D升级款"),
        (0x20BC, 0x5174, 0x00FF, "鲲鹏70 6D升级款"),
        (0x20BC, 0x518A, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20BC, 0x518B, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20BC, 0x518C, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20BC, 0x518D, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20BC, 0x519B, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20BC, 0x519C, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20BC, 0x519D, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20BC, 0x519E, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20DD, 0x5163, 0x00FF, "鲲鹏40鸣潮联名款"),
        (0x20DD, 0x5164, 0x00FF, "鲲鹏40鸣潮联名款"),
        (0x20DD, 0x5165, 0x00FF, "鲲鹏40鸣潮联名款"),
        (0x20DD, 0x5166, 0x00FF, "鲲鹏40鸣潮联名款"),
        (0x20DD, 0x516D, 0x00FF, "鲲鹏70 午马限定款"),
        (0x20DD, 0x516E, 0x00FF, "鲲鹏70 午马限定款"),
        (0x20DD, 0x516F, 0x00FF, "鲲鹏20战双帕弥什联名款"),
        (0x20DD, 0x5170, 0x00FF, "鲲鹏20战双帕弥什联名款"),
        (0x20DD, 0x5173, 0x00FF, "鲲鹏70 6D升级款"),
        (0x20DD, 0x5174, 0x00FF, "鲲鹏70 6D升级款"),
        (0x20DD, 0x518A, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20DD, 0x518B, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20DD, 0x518C, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20DD, 0x518D, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20DD, 0x5190, 0x00FF, "鲲鹏20精英版-丁未"),
        (0x20DD, 0x5194, 0x00FF, "鲲鹏40 精英版"),
        (0x20DD, 0x5195, 0x00FF, "鲲鹏40 精英版"),
        (0x20DD, 0x519B, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20DD, 0x519C, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20DD, 0x519D, 0x00FF, "鲲鹏70 异环联名款"),
        (0x20DD, 0x519E, 0x00FF, "鲲鹏70 异环联名款"),
    ];

    public static IEnumerable<object[]> CatalogPids
        => Rows.Select(row => new object[] { row.Vid, row.Pid, row.Page, row.Model });

    private static HidDeviceInfo Info(
        ushort vid, ushort pid, ushort page = 0xFF00, string product = "", ushort inputLength = 64)
        => new()
        {
            DevicePath = $"test-{vid:X4}-{pid:X4}-{page:X4}-{inputLength}",
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = 0x0003,
            InputReportLength = inputLength,
            OutputReportLength = inputLength,
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
        Assert.Equal(75, Engine.DeviceCount);
        Assert.Equal(2, Engine.ProtocolCount);
        Assert.NotNull(Engine.SpecOf("betop-status15"));
        Assert.NotNull(Engine.SpecOf("betop-t9"));
    }

    /// <summary>
    /// P1-B2：官方鲲鹏 50 个 PID 全部进运行目录，新增 21 个 PID 各有 20BC/20DD 两条身份；
    /// 身份三元组必须与 officialDevices 一致，型号按官方标题（去掉开头「北通」）。
    /// </summary>
    [Fact]
    public void Catalog_OfficialKunpengPidsCoveredOnBothVids()
    {
        var catalog = Catalog();
        var devices = catalog["devices"]!.AsArray()
            .Select(d => (
                Vid: d!["vid"]!.GetValue<string>(),
                Pid: d!["pid"]!.GetValue<string>(),
                Model: d!["model"]!.GetValue<string>(),
                Kind: d!["kind"]!.GetValue<string>(),
                Role: d!["role"]!.GetValue<string>(),
                HasCapabilities: d!["capabilities"] is not null,
                HasGroup: d!["group"] is not null,
                HasPairedPid: d!["pairedPid"] is not null))
            .ToList();

        Assert.Equal(75, devices.Count);
        Assert.Equal(devices.Count, devices.Select(d => (d.Vid, d.Pid)).Distinct().Count());

        var official = catalog["officialDevices"]!.AsArray();
        Assert.Equal(66, official.Count);
        var kunpeng = official
            .Where(d => d!["protocolFamily"]!.GetValue<string>() == "kunpeng")
            .ToList();
        Assert.Equal(50, kunpeng.Count);

        foreach (var entry in kunpeng)
        {
            var node = entry!;
            var pid = node["pid"]!.GetValue<string>();
            var hits = devices.Where(d => d.Pid == pid).ToList();
            Assert.NotEmpty(hits);

            var titles = node["officialTitles"]!.AsArray().Select(t => t!.GetValue<string>()).ToList();
            var model = titles.Count != 1 ? ""
                : titles[0].StartsWith("北通") ? titles[0]["北通".Length..] : titles[0];

            foreach (var hit in hits)
            {
                var identity = Assert.Single(node["usbIdentities"]!.AsArray(),
                    i => i!["vid"]!.GetValue<string>() == hit.Vid);
                Assert.Equal(model, hit.Model);

                var hid = Assert.Single(identity!["hidInterfaces"]!.AsArray());
                Assert.Equal("0x00ff", hid!["usagePage"]!.GetValue<string>());
                Assert.Equal("0x03", hid!["usage"]!.GetValue<string>());
                Assert.Equal(64, hid!["reportLength"]!.GetValue<int>());
            }
        }

        foreach (var pid in NewKunpengPids)
        {
            var pair = devices.Where(d => d.Pid == pid).ToList();
            Assert.Equal(new[] { "0x20bc", "0x20dd" },
                pair.Select(d => d.Vid).OrderBy(v => v, StringComparer.Ordinal).ToArray());
            // kind/role 按目录词表：20BC 本体 = gamepad/controller，20DD 第二身份 = unknown/dongle
            //（词表见 docs/hardware/reference/betop-catalog.md §3）。
            Assert.All(pair, d =>
            {
                Assert.Equal(d.Vid == "0x20bc" ? "gamepad" : "unknown", d.Kind);
                Assert.Equal(d.Vid == "0x20bc" ? "controller" : "dongle", d.Role);
                Assert.False(d.HasCapabilities);
                Assert.False(d.HasGroup);
                Assert.False(d.HasPairedPid);
            });
        }
    }

    /// <summary>P1-B2 只允许追加：原有 33 条目录逐字段冻结（note 也算字段）。</summary>
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

    [Theory]
    [MemberData(nameof(CatalogPids))]
    public void Catalog_EveryPidResolves(ushort vid, ushort pid, ushort page, string model)
    {
        var info = Info(vid, pid, page, inputLength: ReportLength(vid, pid));
        var entry = Engine.DeviceOf(info);

        Assert.NotNull(entry);
        Assert.True(Engine.Matches(info));
        Assert.Equal(model, entry!.Model);
    }

    /// <summary>运行目录里只有 T9 走宙斯流（24 字节），其余都是鲲鹏 0x15 报告（64 字节）。</summary>
    private static ushort ReportLength(ushort vid, ushort pid)
        => (vid, pid) is (0x20BC, 0x5073) ? (ushort)24 : (ushort)64;

    [Fact]
    public void Catalog_SamePidDifferentVidIsDistinct()
    {
        // 2026-11-16 起 devices[].model 用官方标题，同一 PID 的两套身份同名 → 改用 role 区分身份。
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5128, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5130, 0x00FF))!.Role);
        Assert.Equal("dock", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x515C, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x515C, 0x00FF))!.Role);

        Assert.Equal("鲲鹏20智控游戏手柄", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Model);
        Assert.Equal("鲲鹏70 AI旗舰", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Model);
        Assert.Equal("鲲鹏40 御铠白", Engine.DeviceOf(Info(0x20DD, 0x515C, 0x00FF))!.Model);
    }

    [Fact]
    public void Catalog_RolesLoad()
    {
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5073, 0xFF00))!.Role);
        Assert.Equal("controller", Engine.DeviceOf(Info(0x20BC, 0x5158, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20BC, 0x5134, 0x00FF))!.Role);
        Assert.Equal("dongle", Engine.DeviceOf(Info(0x20DD, 0x5128, 0x00FF))!.Role);
        Assert.Equal("dock", Engine.DeviceOf(Info(0x20DD, 0x5130, 0x00FF))!.Role);
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
        var t9 = Info(0x20BC, 0x5073, 0xFF00, "BEITONG T9 For Keyboard & Mouse");
        Assert.Equal("北通 宙斯2", BetopDisplayName.For(t9, Engine.DeviceOf(t9)));

        var kp20 = Info(0x20BC, 0x5159, 0x00FF, "BTP-KP20D XINPUT DONGLE");
        Assert.Equal("北通 鲲鹏20 绝尘白", BetopDisplayName.For(kp20, Engine.DeviceOf(kp20)));

        var dongle = Info(0x20DD, 0x5128, 0x00FF, "KP20 DONGLE");
        Assert.Equal("北通 鲲鹏20智控游戏手柄", BetopDisplayName.For(dongle, Engine.DeviceOf(dongle)));

        var unknown = Info(0x20BC, 0x9999, 0xFF00, "BEITONG XF1 For Keyboard & Mouse");
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
        foreach (var row in Rows)
        {
            var info = Info(row.Vid, row.Pid, row.Page, inputLength: ReportLength(row.Vid, row.Pid));
            var expected = (row.Vid, row.Pid) is (0x20BC, 0x5073) or (0x20BC, 0x5158) or (0x20BC, 0x5159)
                ? CapabilityState.Supported
                : CapabilityState.Unverified;
            Assert.Equal(expected, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
        }

        Assert.Equal(CapabilityState.Unsupported,
            Engine.CapabilityOf(Info(0x20BC, 0x5073, 0xFF00, inputLength: 24), DeviceCapability.ChargingRead));
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
