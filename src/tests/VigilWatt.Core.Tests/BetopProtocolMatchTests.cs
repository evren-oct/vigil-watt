using System.Text.Json;
using System.Text.Json.Nodes;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： BETOP 两套协议的 HID 三元组判据——鲲鹏 0x00FF/0x03/64、宙斯族 0xFF00/01~03/24。
/// 2026/11/17 22:10:00 20211
/// 判据来源见 docs/hardware/reference/betop-official-client.md §2.1；协议匹配不等于遥测能力已验证。
/// </summary>
public sealed class BetopProtocolMatchTests
{
    private const string ProtocolsResource = "VigilWatt.Core.Plugins.Betop.specs.betop.protocols.json";
    private const string CatalogResource = "VigilWatt.Core.Plugins.Betop.betop-catalog.json";

    private static readonly ProtocolEngine Engine = ProtocolEngine.Load(ProtocolsResource, CatalogResource);

    private static HidDeviceInfo Info(ushort vid, ushort pid, ushort page, ushort usage, ushort inputLength)
        => new()
        {
            DevicePath = $"test-{vid:X4}-{pid:X4}-{page:X4}-{usage:X4}-{inputLength}",
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = usage,
            InputReportLength = inputLength,
            OutputReportLength = inputLength,
        };

    private static ushort Hex(string text) => Convert.ToUInt16(text[2..], 16);

    /// <summary>officialDevices 不参与加载，只能直接读同一份内嵌目录。</summary>
    private static JsonObject Catalog()
    {
        using var stream = typeof(BetopProtocolMatchTests).Assembly.GetManifestResourceStream(CatalogResource)
            ?? throw new InvalidOperationException($"嵌入资源缺失: {CatalogResource}");
        return JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new InvalidOperationException($"{CatalogResource} 根节点不是对象");
    }

    /// <summary>鲲鹏只认 0x00FF/0x03/64；24 字节宙斯接口必须路由到 t9，不能落进 status15。</summary>
    [Theory]
    [InlineData(0x00FF, 0x03, 64, "betop-status15")]
    [InlineData(0x00FF, 0x03, 24, null)]
    [InlineData(0x00FF, 0x02, 64, null)]
    [InlineData(0xFF00, 0x01, 24, "betop-t9")]
    [InlineData(0xFF00, 0x02, 24, "betop-t9")]
    [InlineData(0xFF00, 0x03, 24, "betop-t9")]
    [InlineData(0xFF00, 0x03, 64, null)]
    public void Status15_RequiresKunpengTriple(ushort page, ushort usage, ushort length, string? expected)
        => Assert.Equal(expected, Engine.Resolve(Info(0x20BC, 0x9999, page, usage, length))?.Name);

    /// <summary>宙斯族三条厂商集合（usage 1/2/3）都是 24 字节；长度或页不符一律不认。</summary>
    [Theory]
    [InlineData(0xFF00, 0x01, 24, "betop-t9")]
    [InlineData(0xFF00, 0x02, 24, "betop-t9")]
    [InlineData(0xFF00, 0x03, 24, "betop-t9")]
    [InlineData(0xFF00, 0x01, 64, null)]
    [InlineData(0xFF00, 0x02, 64, null)]
    [InlineData(0xFF00, 0x03, 64, null)]
    [InlineData(0x00FF, 0x03, 24, null)]
    [InlineData(0x00FF, 0x03, 64, "betop-status15")]
    public void T9_RequiresZeusTriple(ushort page, ushort usage, ushort length, string? expected)
        => Assert.Equal(expected, Engine.Resolve(Info(0x20BC, 0x9999, page, usage, length))?.Name);

    [Theory]
    [InlineData(0x00FF, 0x03, 64)]
    [InlineData(0xFF00, 0x01, 24)]
    public void NonBetopVendor_NeverMatches(ushort page, ushort usage, ushort length)
        => Assert.Null(Engine.Resolve(Info(0x1234, 0x9999, page, usage, length)));

    /// <summary>官方 12 个宙斯 PID 的每个接口单独判定：0xFF00/24 归 t9，0x00FF/24 谁都不归。</summary>
    [Fact]
    public void ZeusInterfaces_MatchOnlyZeusTriple()
    {
        var zeus = Catalog()["officialDevices"]!.AsArray()
            .Where(d => d!["protocolFamily"]!.GetValue<string>() == "zeus")
            .ToList();
        Assert.Equal(12, zeus.Count);

        foreach (var entry in zeus)
        {
            var node = entry!;
            var pid = Hex(node["pid"]!.GetValue<string>());
            foreach (var identity in node["usbIdentities"]!.AsArray())
            {
                var vid = Hex(identity!["vid"]!.GetValue<string>());
                foreach (var hid in identity["hidInterfaces"]!.AsArray())
                {
                    var page = Hex(hid!["usagePage"]!.GetValue<string>());
                    var usage = Hex(hid["usage"]!.GetValue<string>());
                    var length = (ushort)hid["reportLength"]!.GetValue<int>();
                    var expected = page == 0xFF00 ? "betop-t9" : null;
                    Assert.Equal(expected, Engine.Resolve(Info(vid, pid, page, usage, length))?.Name);
                }
            }
        }

        // 0x500b / 0x502f 的杂项集合是 0x00FF/0x03/24：既不是鲲鹏 64 字节，也不是宙斯 0xFF00。
        Assert.Null(Engine.Resolve(Info(0x20BC, 0x500B, 0x00FF, 0x03, 24)));
        Assert.Null(Engine.Resolve(Info(0x20BC, 0x502F, 0x00FF, 0x03, 24)));
        Assert.Equal("betop-t9", Engine.Resolve(Info(0x20BC, 0x500B, 0xFF00, 0x02, 24))?.Name);
        Assert.Equal("betop-t9", Engine.Resolve(Info(0x20BC, 0x502F, 0xFF00, 0x02, 24))?.Name);
    }

    /// <summary>运行目录 75 条按各自家族三元组仍可认领；T9 单独走 0xFF00/24。</summary>
    [Fact]
    public void RuntimeCatalogEntries_StillResolve()
    {
        var devices = Catalog()["devices"]!.AsArray();
        Assert.Equal(75, devices.Count);

        foreach (var device in devices)
        {
            var node = device!;
            var vid = Hex(node["vid"]!.GetValue<string>());
            var pid = Hex(node["pid"]!.GetValue<string>());
            var info = pid == 0x5073
                ? Info(vid, pid, 0xFF00, 0x01, 24)
                : Info(vid, pid, 0x00FF, 0x03, 64);

            Assert.NotNull(Engine.DeviceOf(info));
            Assert.True(Engine.Matches(info), $"{vid:X4}:{pid:X4} 未命中");
        }
    }

    /// <summary>协议命中 ≠ 能力验证：11 个未收录宙斯 PID 全部仍是 Unverified，且不在运行目录。</summary>
    [Fact]
    public void UnlistedZeusPids_MatchProtocolButStayUnverified()
    {
        var unlisted = Catalog()["officialDevices"]!.AsArray()
            .Where(d => d!["protocolFamily"]!.GetValue<string>() == "zeus")
            .Select(d => Hex(d!["pid"]!.GetValue<string>()))
            .Where(pid => pid != 0x5073)
            .ToList();
        Assert.Equal(11, unlisted.Count);

        foreach (var pid in unlisted)
        {
            var info = Info(0x20BC, pid, 0xFF00, 0x01, 24);
            Assert.Equal("betop-t9", Engine.Resolve(info)?.Name);
            Assert.Null(Engine.DeviceOf(info));
            Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
            Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.ChargingRead));
        }
    }
}
