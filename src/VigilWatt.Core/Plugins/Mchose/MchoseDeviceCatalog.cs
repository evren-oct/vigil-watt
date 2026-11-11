using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 嵌入的迈从设备目录（本体 VID/PID → 型号 + 协议族，传输通道 PID → 角色），查不到回落接口形态。
/// 2026/11/01 20:45:00 xliu
/// 数据来自官方 M HUB WEB 的 cardList / Vb 表，见 docs/hardware/mchose-catalog.md §1；共用 dongle 不收录（靠形态判定）。
/// </summary>
internal static class MchoseDeviceCatalog
{
    /// <summary>目录条目：型号名可能为空串，协议族为 <c>MchoseInterfaceClassifier.KindFromProtocol</c> 认得的名字。</summary>
    public readonly record struct Entry(string Model, string Protocol);

    /// <summary>传输通道条目：说明这颗 PID 是哪种 dongle 以及它承载的协议，**不携带型号**（同 PID 多型号共用）。</summary>
    public readonly record struct TransportEntry(
        string Role, string Protocol, string Note, CapabilitySet Capabilities,
        string? DisplayName = null, string? IconKey = null);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<CatalogDto> Catalog = new(LoadDto);
    private static readonly Lazy<Dictionary<(ushort, ushort), Entry>> Data = new(BuildDevices);
    private static readonly Lazy<Dictionary<(ushort, ushort), TransportEntry>> Transports = new(BuildTransports);
    private static readonly Lazy<Dictionary<string, int[]>> RateTables = new(BuildRateTables);
    private static readonly Lazy<Dictionary<ushort, Dictionary<ushort, string>>> RateProfiles = new(BuildRateProfiles);

    /// <summary>按本体 VID/PID 查型号与协议族；未收录返回 false，由调用方回落接口形态判定。</summary>
    public static bool TryGet(ushort vendorId, ushort productId, out Entry entry)
        => Data.Value.TryGetValue((vendorId, productId), out entry);

    /// <summary>按 VID/PID 查传输通道角色；未登记返回 false——**不按 dongle PID 推断型号**。</summary>
    public static bool TryGetTransport(ushort vendorId, ushort productId, out TransportEntry entry)
        => Transports.Value.TryGetValue((vendorId, productId), out entry);

    /// <summary>
    /// 传输通道的能力声明：只认目录写过的，且**这条接口得真能跑 0x4D**——同 dongle 的 61 字节静默接口
    /// 不能借到声明（PR-C1 同类）。没写、未验证的 PID 一律 Unverified，不按协议族外推。
    /// </summary>
    public static CapabilitySet CapabilitiesOf(HidDeviceInfo info)
        => Transports.Value.TryGetValue((info.VendorId, info.ProductId), out var entry)
           && Mchose0x4DCodec.IsVendorTransportInterface(info.UsagePage, info.Usage,
               info.InputReportLength, info.OutputReportLength)
            ? entry.Capabilities
            : CapabilitySet.Empty;

    private static CatalogDto LoadDto()
    {
        var asm = Assembly.GetExecutingAssembly();
        const string name = "VigilWatt.Core.Plugins.Mchose.mchose-catalog.json";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"嵌入资源缺失: {name}");
        return JsonSerializer.Deserialize<CatalogDto>(stream, JsonOptions)
            ?? throw new InvalidOperationException("mchose-catalog.json 解析失败");
    }

    private static Dictionary<(ushort, ushort), Entry> BuildDevices()
    {
        var map = new Dictionary<(ushort, ushort), Entry>();
        foreach (var d in Catalog.Value.Devices ?? [])
        {
            if (!TryParseHex(d.Vid, out var vid) || !TryParseHex(d.Pid, out var pid)) continue;
            if (string.IsNullOrWhiteSpace(d.Protocol)) continue;
            map[(vid, pid)] = new Entry(d.Model?.Trim() ?? "", d.Protocol.Trim());
        }
        return map;
    }

    /// <summary>
    /// 回报率表：先按**本体型号 PID** 找槽位表，再用**USB 枚举 PID** 选槽，最后按表名取 Hz。
    /// 两个 PID 不能混——dongle 的 PID 反推不出型号（官方也是先认型号、再按 PID 选槽）。
    /// </summary>
    public static bool TryGetRateTable(ushort modelPid, ushort usbPid, out IReadOnlyList<int> hz)
        => TryGetRateTable(modelPid, usbPid, out _, out hz);

    /// <summary>同上，并给出表名（日志取证用）。</summary>
    public static bool TryGetRateTable(ushort modelPid, ushort usbPid,
        out string tableName, out IReadOnlyList<int> hz)
    {
        tableName = "";
        hz = [];
        if (!RateProfiles.Value.TryGetValue(modelPid, out var slots)) return false;
        if (!slots.TryGetValue(usbPid, out var name)) return false;
        if (!RateTables.Value.TryGetValue(name, out var table)) return false;

        tableName = name;
        hz = table;
        return true;
    }

    /// <summary>index → Hz。表查不到或 index 超出该表范围都返回 false——**不默认成 125Hz**。</summary>
    public static bool TryGetSettingHz(ushort modelPid, ushort usbPid, int index, out int hz)
    {
        hz = 0;
        if (!TryGetRateTable(modelPid, usbPid, out var table)) return false;
        if (index < 0 || index >= table.Count) return false;

        hz = table[index];
        return true;
    }

    private static Dictionary<string, int[]> BuildRateTables()
    {
        var map = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in Catalog.Value.RateTables ?? [])
            if (values is { Length: > 0 }) map[name.Trim()] = values;
        return map;
    }

    private static Dictionary<ushort, Dictionary<ushort, string>> BuildRateProfiles()
    {
        var map = new Dictionary<ushort, Dictionary<ushort, string>>();
        foreach (var (modelText, slots) in Catalog.Value.RateProfiles ?? [])
        {
            if (!TryParseHex(modelText, out var model) || slots is null) continue;
            var byPid = new Dictionary<ushort, string>();
            foreach (var (pidText, table) in slots)
                if (TryParseHex(pidText, out var pid) && !string.IsNullOrWhiteSpace(table))
                    byPid[pid] = table.Trim();
            if (byPid.Count > 0) map[model] = byPid;
        }
        return map;
    }

    private static Dictionary<(ushort, ushort), TransportEntry> BuildTransports()
    {
        var map = new Dictionary<(ushort, ushort), TransportEntry>();
        foreach (var t in Catalog.Value.Transports ?? [])
        {
            if (!TryParseHex(t.Vid, out var vid) || !TryParseHex(t.Pid, out var pid)) continue;
            if (string.IsNullOrWhiteSpace(t.Role)) continue;
            map[(vid, pid)] = new TransportEntry(t.Role.Trim(), t.Protocol?.Trim() ?? "", t.Note?.Trim() ?? "",
                ReadCapabilities(t.Capabilities, vid, pid),
                string.IsNullOrWhiteSpace(t.RoleDisplay) ? null : t.RoleDisplay!.Trim(),
                string.IsNullOrWhiteSpace(t.RoleIcon) ? null : t.RoleIcon!.Trim());
        }
        return map;
    }

    /// <summary>
    /// 解析 <c>capabilities</c>。名字表与 devices.json 共用一份；拼错**必须报错**——
    /// 静默成 Unverified 等于把已验能力悄悄丢掉。
    /// </summary>
    private static CapabilitySet ReadCapabilities(Dictionary<string, string>? declared, ushort vid, ushort pid)
    {
        if (declared is null || declared.Count == 0) return CapabilitySet.Empty;

        var pairs = new List<KeyValuePair<DeviceCapability, CapabilityState>>(declared.Count);
        foreach (var (name, state) in declared)
        {
            if (!ProtocolSpecLoader.CapabilityNames.TryGetValue(name, out var capability))
                throw new InvalidOperationException(
                    $"mchose-catalog.json: 传输通道 {vid:X4}:{pid:X4} 的 capabilities.{name} 不是已知能力" +
                    $"（可选：{string.Join(" / ", ProtocolSpecLoader.CapabilityNames.Keys)}）");
            pairs.Add(new(capability, ParseCapabilityState(state, name, vid, pid)));
        }
        return CapabilitySet.Create(pairs);
    }

    private static CapabilityState ParseCapabilityState(string? text, string field, ushort vid, ushort pid)
        => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "supported" => CapabilityState.Supported,
            "unsupported" => CapabilityState.Unsupported,
            "unverified" => CapabilityState.Unverified,
            var other => throw new InvalidOperationException(
                $"mchose-catalog.json: 传输通道 {vid:X4}:{pid:X4} 的 capabilities.{field} 状态 \"{other}\" 非法" +
                "（supported / unsupported / unverified）"),
        };

    /// <summary>解析 <c>0x3837</c> / <c>3837</c> 两种写法；非法值返回 false 并跳过该条。</summary>
    private static bool TryParseHex(string? text, out ushort value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private sealed class CatalogDto
    {
        [JsonPropertyName("devices")] public List<DeviceDto>? Devices { get; set; }
        [JsonPropertyName("transports")] public List<TransportDto>? Transports { get; set; }
        [JsonPropertyName("rateTables")] public Dictionary<string, int[]>? RateTables { get; set; }
        [JsonPropertyName("rateProfiles")] public Dictionary<string, Dictionary<string, string>>? RateProfiles { get; set; }
    }

    private sealed class DeviceDto
    {
        [JsonPropertyName("vid")] public string? Vid { get; set; }
        [JsonPropertyName("pid")] public string? Pid { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    }

    private sealed class TransportDto
    {
        [JsonPropertyName("vid")] public string? Vid { get; set; }
        [JsonPropertyName("pid")] public string? Pid { get; set; }
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("roleDisplay")] public string? RoleDisplay { get; set; }
        [JsonPropertyName("roleIcon")] public string? RoleIcon { get; set; }
        [JsonPropertyName("protocol")] public string? Protocol { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("capabilities")] public Dictionary<string, string>? Capabilities { get; set; }
    }
}
