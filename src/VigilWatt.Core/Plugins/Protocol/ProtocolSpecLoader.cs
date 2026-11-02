using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Protocol;

/// <summary>
/// 功 能： 从内嵌 JSON 加载协议规格与设备目录，做深度合并（extends / override）与加载期校验。
/// 2026/11/01 23:20:00 xliu
/// 校验失败直接抛异常并指明文件+条目+字段：宁可启动失败，不要静默不认领。写法见 docs/dev/protocol-json.md。
/// </summary>
internal static class ProtocolSpecLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>加载一套规格；两个参数都是完整的内嵌资源名（命名空间 + 目录 + 文件名）。</summary>
    public static SpecBundle Load(string protocolsResource, string devicesResource)
    {
        var protocolsRoot = ReadObject(protocolsResource);
        var devicesRoot = ReadObject(devicesResource);
        var prefix = protocolsResource;

        var rawProtocols = ReadProtocols(protocolsRoot, prefix);
        var effective = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var resolved = ResolveAll(rawProtocols, effective, prefix);
        // 注册顺序即"未收录设备"的探测优先级，必须按 JSON 里的书写顺序保留
        var order = (protocolsRoot["protocols"] as JsonObject)?.Select(kv => kv.Key).ToList() ?? [];

        var vendors = ReadVendors(protocolsRoot);
        var brand = devicesRoot["brand"]?.GetValue<string>() ?? "";
        var devices = new Dictionary<(ushort, ushort), DeviceEntry>();
        var deviceSpecs = new Dictionary<(ushort, ushort), ProtocolSpec>();
        foreach (var (entry, patch) in ReadDevices(devicesRoot, prefix))
        {
            var key = (entry.VendorId, entry.ProductId);
            if (!resolved.TryGetValue(entry.Protocol, out var baseSpec))
                throw new InvalidOperationException(
                    $"{prefix}: {entry.VendorId:X4}:{entry.ProductId:X4} 引用了不存在的协议 \"{entry.Protocol}\"");
            devices[key] = entry;
            // 设备级 override 叠在"已展开 extends"的协议对象上，否则会丢掉继承来的字段
            deviceSpecs[key] = patch is { } p
                ? BuildSpec(entry.Protocol, MergeObject(effective[entry.Protocol], p), prefix)
                : baseSpec;
        }

        return new SpecBundle(resolved, devices, deviceSpecs, vendors,
            order.Where(resolved.ContainsKey).Select(k => resolved[k]).ToList(), brand);
    }

    private static JsonObject ReadObject(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"嵌入资源缺失: {name}");
        var documentOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        return JsonNode.Parse(stream, nodeOptions: null, documentOptions) as JsonObject
            ?? throw new InvalidOperationException($"{name} 根节点不是对象");
    }

    private static Dictionary<string, JsonObject> ReadProtocols(JsonObject root, string prefix)
    {
        if (root["protocols"] is not JsonObject map || map.Count == 0)
            throw new InvalidOperationException($"{prefix}/protocols.json: 缺少非空的 protocols 对象");
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (key, value) in map)
        {
            result[key] = value as JsonObject
                ?? throw new InvalidOperationException($"{prefix}/protocols.json: 协议 \"{key}\" 不是对象");
        }
        return result;
    }

    private static HashSet<ushort> ReadVendors(JsonObject root)
    {
        var set = new HashSet<ushort>();
        if (root["vendors"] is not JsonArray list) return set;
        foreach (var item in list)
        {
            var text = item?.GetValue<string>();
            if (TryParseHex(text, out var vid)) set.Add(vid);
        }
        return set;
    }

    /// <summary>解析 <c>extends</c> 链：先展开父协议，再把子协议的 override 叠上去。</summary>
    private static Dictionary<string, ProtocolSpec> ResolveAll(
        Dictionary<string, JsonObject> raw, Dictionary<string, JsonObject> effective, string prefix)
    {
        var result = new Dictionary<string, ProtocolSpec>(StringComparer.Ordinal);
        foreach (var name in raw.Keys) ResolveOne(name, raw, effective, result, prefix, []);
        return result;
    }

    private static JsonObject ResolveOne(string name, Dictionary<string, JsonObject> raw,
        Dictionary<string, JsonObject> effective, Dictionary<string, ProtocolSpec> done,
        string prefix, HashSet<string> visiting)
    {
        if (raw[name] is not { } self) throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 不存在");
        if (!visiting.Add(name)) throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 extends 成环");
        if (done.ContainsKey(name)) return self;

        var merged = self;
        if (self["extends"]?.GetValue<string>() is { Length: > 0 } parentName)
        {
            if (!raw.ContainsKey(parentName))
                throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 extends 指向不存在的 \"{parentName}\"");
            var parent = ResolveOne(parentName, raw, effective, done, prefix, visiting);
            merged = MergeObject(parent, self);
        }

        effective[name] = merged;
        done[name] = BuildSpec(name, merged, prefix);
        visiting.Remove(name);
        return merged;
    }

    private static ProtocolSpec BuildSpec(string name, JsonObject node, string prefix)
    {
        ProtocolDto dto;
        try
        {
            dto = node.Deserialize<ProtocolDto>(JsonOptions)
                ?? throw new InvalidOperationException("反序列化得到 null");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" JSON 不合法: {ex.Message}", ex);
        }

        var kind = ParseKind(dto.Kind, name, prefix);
        var impl = (dto.Implementation ?? "data").ToLowerInvariant() switch
        {
            "data" => SpecImplementation.Data,
            "code" => SpecImplementation.Code,
            "none" => SpecImplementation.None,
            var other => throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 implementation \"{other}\" 非法"),
        };
        if (impl == SpecImplementation.Code && string.IsNullOrWhiteSpace(dto.CodeFamily))
            throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 声明 implementation=code 但没给 codeFamily");

        var spec = new ProtocolSpec
        {
            Name = name,
            Kind = kind,
            Implementation = impl,
            CodeFamily = dto.CodeFamily,
            Pollable = dto.Pollable,
            ChargeDebounce = dto.ChargeDebounce,
            Interface = BuildInterface(dto.Interface, name, prefix),
            Passive = dto.Passive is null ? null : BuildFrame(dto.Passive, name, prefix, "passive"),
            Active = dto.Active is null ? null : BuildActive(dto.Active, name, prefix),
            ProbeWindowMs = dto.ProbeWindowMs ?? 5000,
        };

        if (impl == SpecImplementation.Data && spec.Passive is null && spec.Active is null)
            throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 是 data 实现但既没有 passive 也没有 active");
        ValidateOffsets(spec, name, prefix);
        return spec;
    }

    private static DeviceKind ParseKind(string? text, string name, string prefix)
        => (text ?? "unknown").Trim().ToLowerInvariant() switch
        {
            "mouse" => DeviceKind.Mouse,
            "keyboard" => DeviceKind.Keyboard,
            "headset" => DeviceKind.Headset,
            "speaker" => DeviceKind.Speaker,
            "gamepad" => DeviceKind.Gamepad,
            "unknown" or "base" => DeviceKind.Unknown,
            var other => throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 kind \"{other}\" 非法"),
        };

    private static InterfaceSpec BuildInterface(InterfaceDto? dto, string name, string prefix)
    {
        if (dto is null) throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 缺少 interface");
        var groups = (dto.AnyOf ?? [])
            .Select(g => BuildInterface(g, name, prefix) with { AnyOf = [] })
            .ToArray();
        return new InterfaceSpec
        {
            UsagePage = ToMatch(dto.UsagePage, name, prefix, "interface.usagePage"),
            Usage = ToMatch(dto.Usage, name, prefix, "interface.usage"),
            InputLength = ToMatch(dto.InputLength, name, prefix, "interface.inputLength"),
            OutputLength = ToMatch(dto.OutputLength, name, prefix, "interface.outputLength"),
            FeatureLength = ToMatch(dto.FeatureLength, name, prefix, "interface.featureLength"),
            ReportId = ToMatch(dto.ReportId, name, prefix, "interface.reportId"),
            PathContains = dto.PathContains,
            AnyOf = groups,
        };
    }

    private static FrameSpec BuildFrame(FrameDto dto, string name, string prefix, string where)
    {
        var prefixBytes = (dto.Prefix ?? []).Select(p => (p.Offset, p.Value)).ToArray();
        return new FrameSpec
        {
            TryOffsets = dto.TryOffsets is { Length: > 0 } o ? o : [0, 1],
            ReportId = dto.ReportId,
            Xor = dto.Xor,
            Prefix = prefixBytes,
            Length = ToMatch(dto.Length, name, prefix, $"{where}.length"),
            Percent = dto.Fields?.Percent is { } p
                ? new PercentSpec { Offset = p.Offset, Type = ParseFieldType(p.Type, name, prefix) }
                : null,
            Charge = dto.Fields?.Charge is { } c
                ? BuildCharge(c, name, prefix)
                : null,
            Link = dto.Fields?.Link is { } l
                ? new LinkSpec
                {
                    Offset = l.Offset,
                    Bit = l.Bit ?? 7,
                    ConnectedWhenSet = l.ConnectedWhen != 0,
                    OnlyWhenMode = (l.OnlyWhenMode ?? "").Trim().ToLowerInvariant() switch
                    {
                        "" => null,
                        "wireless2g" => LinkMode.Wireless2G,
                        "wired" => LinkMode.Wired,
                        var other => throw new InvalidOperationException(
                            $"{prefix}: 协议 \"{name}\" link.onlyWhenMode \"{other}\" 非法"),
                    },
                }
                : null,
            Mode = dto.Fields?.LinkMode is { } m
                ? new ModeSpec
                {
                    Offset = m.Offset,
                    Mask = m.Mask ?? 0x0F,
                    Table = m.Map is null ? null : m.Map.ToDictionary(
                        kv => ParseKey(kv.Key, name, prefix),
                        kv => kv.Value.Trim().ToLowerInvariant() switch
                        {
                            "wireless2g" => LinkMode.Wireless2G,
                            "wired" => LinkMode.Wired,
                            var other => throw new InvalidOperationException(
                                $"{prefix}: 协议 \"{name}\" linkMode 映射值 \"{other}\" 非法"),
                        }),
                }
                : null,
            MinPercent = dto.ValidPercent?.Min ?? 1,
            MaxPercent = dto.ValidPercent?.Max ?? 100,
            FullAt = dto.FullAt,
        };
    }

    private static ChargeSpec BuildCharge(FieldDto c, string name, string prefix) => new()
    {
        Offset = c.Offset,
        Type = ParseFieldType(c.Type, name, prefix),
        NonZero = string.Equals(c.On, "nonzero", StringComparison.OrdinalIgnoreCase),
        Zero = string.Equals(c.On, "zero", StringComparison.OrdinalIgnoreCase),
        Table = c.Map?.ToDictionary(
            kv => ParseKey(kv.Key, name, prefix),
            kv => kv.Value.Trim().ToLowerInvariant() switch
            {
                "charging" => ChargingState.Charging,
                "discharging" => ChargingState.Discharging,
                "full" => ChargingState.Full,
                "unknown" => ChargingState.Unknown,
                var other => throw new InvalidOperationException(
                    $"{prefix}: 协议 \"{name}\" 充电映射值 \"{other}\" 非法"),
            }),
        OnMiss = (c.OnMiss ?? "unknown").Trim().ToLowerInvariant() switch
        {
            "charging" => ChargingState.Charging,
            "discharging" => ChargingState.Discharging,
            "full" => ChargingState.Full,
            _ => ChargingState.Unknown,
        },
    };

    private static ActiveSpec BuildActive(ActiveDto dto, string name, string prefix)
    {
        if (dto.Query is not { } q)
            throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 active 缺少 query");
        if (q.Length <= 0)
            throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 active.query.length 必须 > 0");
        if (dto.Response is null)
            throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 active 缺少 response");
        return new ActiveSpec
        {
            Query = BuildQueryBytes(q, name, prefix),
            WindowMs = dto.WindowMs ?? 1000,
            Attempts = dto.Attempts is > 0 ? dto.Attempts.Value : 1,
            Response = BuildFrame(dto.Response, name, prefix, "active.response"),
            RejectQueryEcho = dto.RejectQueryEcho,
        };
    }

    private static byte[] BuildQueryBytes(QueryDto q, string name, string prefix)
    {
        var frame = new byte[q.Length];
        if (q.Pad is { } pad) Array.Fill(frame, (byte)pad);
        if (string.IsNullOrWhiteSpace(q.Hex)) return frame;

        var parts = q.Hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (i >= frame.Length)
                throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 query.hex 超出 length={q.Length}");
            if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var b))
                throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 query.hex 第 {i} 段不是十六进制");
            frame[i] = b;
        }
        return frame;
    }

    private static FieldType ParseFieldType(string? text, string name, string prefix)
        => (text ?? "u8").Trim().ToLowerInvariant() switch
        {
            "u8" or "uint8" => FieldType.U8,
            "u16" or "uint16" => FieldType.U16,
            "u24" or "uint24" => FieldType.U24,
            "u32" or "uint32" => FieldType.U32,
            "bit4" => FieldType.Bit4,
            "bit" => FieldType.Bit,
            var other => throw new InvalidOperationException(
                $"{prefix}: 协议 \"{name}\" 的字段类型 \"{other}\" 不在闭类型集内"),
        };

    private static IntMatch? ToMatch(JsonNode? node, string name, string prefix, string where)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.TryGetValue<int>(out var single)) return new IntMatch(single, null, null, null, null);
        if (node is JsonArray arr)
        {
            var values = arr.Select(x => x!.GetValue<int>()).ToArray();
            return new IntMatch(null, null, null, values, null);
        }
        if (node is JsonObject obj)
        {
            int? Get(string key) => obj[key] is { } n && n.GetValueKind() == JsonValueKind.Number ? n.GetValue<int>() : null;
            int[]? GetArray(string key) => obj[key] is JsonArray a ? a.Select(x => x!.GetValue<int>()).ToArray() : null;

            var match = new IntMatch(Get("eq"), Get("min"), Get("max"), null, GetArray("not"));
            if (match == new IntMatch(null, null, null, null, null))
                throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 {where} 是空对象");
            return match;
        }
        throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的 {where} 既不是数字也不是数组/对象");
    }

    private static int ParseKey(string text, string name, string prefix)
    {
        if (int.TryParse(text, out var dec)) return dec;
        if (TryParseHex(text, out var hex)) return hex;
        throw new InvalidOperationException($"{prefix}: 协议 \"{name}\" 的映射表键 \"{text}\" 不是数字");
    }

    /// <summary>有确切帧长时校验偏移不越界——这是 JSON 最容易写错、又最不报错的地方。</summary>
    private static void ValidateOffsets(ProtocolSpec spec, string name, string prefix)
    {
        int? known = spec.Passive?.Length?.Eq ?? spec.Interface.InputLength?.Eq;
        if (known is not { } len) return;
        var frame = spec.Passive;
        if (frame is null) return;

        void Check(int offset, int width, string field)
        {
            if (offset < 0 || offset + width > len)
                throw new InvalidOperationException(
                    $"{prefix}: 协议 \"{name}\" 的 {field} 偏移 {offset}+{width} 超出帧长 {len}");
        }

        if (frame.Percent is { } p) Check(p.Offset, Width(p.Type), "percent");
        if (frame.Charge is { } c) Check(c.Offset, Width(c.Type), "charge");
        if (frame.Link is { } l) Check(l.Offset, 1, "link");
        if (frame.Mode is { } m) Check(m.Offset, 1, "linkMode");
        foreach (var (off, _) in frame.Prefix) Check(off, 1, "prefix");
    }

    private static int Width(FieldType type) => type switch
    {
        FieldType.U16 => 2,
        FieldType.U24 => 3,
        FieldType.U32 => 4,
        _ => 1,
    };

    private static List<(DeviceEntry Entry, JsonObject? Override)> ReadDevices(JsonObject root, string prefix)
    {
        if (root["devices"] is not JsonArray list)
            throw new InvalidOperationException($"{prefix}: 缺少 devices 数组");
        var result = new List<(DeviceEntry, JsonObject?)>(list.Count);
        foreach (var item in list)
        {
            var dto = item?.Deserialize<DeviceDto>(JsonOptions)
                ?? throw new InvalidOperationException($"{prefix}/devices.json: 有 null 条目");
            if (!TryParseHex(dto.Vid, out var vid) || !TryParseHex(dto.Pid, out var pid))
                throw new InvalidOperationException($"{prefix}/devices.json: {dto.Vid}:{dto.Pid} 不是合法 VID:PID");
            if (string.IsNullOrWhiteSpace(dto.Protocol))
                throw new InvalidOperationException($"{prefix}/devices.json: {dto.Vid}:{dto.Pid} 缺少 protocol");
            TryParseHex(dto.PairedPid, out var paired);
            result.Add((new DeviceEntry
            {
                VendorId = vid,
                ProductId = pid,
                Model = dto.Model?.Trim() ?? "",
                Kind = ParseKind(dto.Kind, $"{dto.Vid}:{dto.Pid}", prefix),
                Protocol = dto.Protocol.Trim(),
                PairedPid = paired,
            }, dto.Override));
        }
        return result;
    }

    private static JsonObject MergeObject(JsonObject target, JsonObject patch)
    {
        var result = (JsonObject)target.DeepClone();
        MergeInto(result, patch);
        return result;
    }

    private static void MergeInto(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (key is "extends" or "override") continue;
            if (value is JsonObject patchChild && target[key] is JsonObject targetChild)
                MergeInto(targetChild, patchChild);
            else
                target[key] = value?.DeepClone();
        }
    }

    /// <summary>解析 <c>0x24AE</c> / <c>24AE</c> 两种写法。</summary>
    public static bool TryParseHex(string? text, out ushort value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private sealed class ProtocolDto
    {
        public string? Kind { get; set; }
        public bool Pollable { get; set; }
        public bool ChargeDebounce { get; set; }
        public string? Implementation { get; set; }
        public string? CodeFamily { get; set; }
        public InterfaceDto? Interface { get; set; }
        public FrameDto? Passive { get; set; }
        public ActiveDto? Active { get; set; }
        public int? ProbeWindowMs { get; set; }
    }

    private sealed class InterfaceDto
    {
        public JsonNode? UsagePage { get; set; }
        public JsonNode? Usage { get; set; }
        public JsonNode? InputLength { get; set; }
        public JsonNode? OutputLength { get; set; }
        public JsonNode? FeatureLength { get; set; }
        public JsonNode? ReportId { get; set; }
        public string? PathContains { get; set; }
        public List<InterfaceDto>? AnyOf { get; set; }
    }

    private sealed class FrameDto
    {
        public int[]? TryOffsets { get; set; }
        public int[]? ReportId { get; set; }
        public int? Xor { get; set; }
        public PrefixDto[]? Prefix { get; set; }
        public FieldsDto? Fields { get; set; }
        public RangeDto? ValidPercent { get; set; }
        public JsonNode? Length { get; set; }
        public int? FullAt { get; set; }
    }

    private sealed class PrefixDto
    {
        public int Offset { get; set; }
        public int Value { get; set; }
    }

    private sealed class FieldsDto
    {
        public FieldDto? Percent { get; set; }
        public FieldDto? Charge { get; set; }
        public FieldDto? Link { get; set; }
        public FieldDto? LinkMode { get; set; }
    }

    private sealed class FieldDto
    {
        public int Offset { get; set; }
        public string? Type { get; set; }
        public int? Bit { get; set; }
        public int? ConnectedWhen { get; set; }
        public string? OnlyWhenMode { get; set; }
        public int? Mask { get; set; }
        public Dictionary<string, string>? Map { get; set; }
        public string? On { get; set; }
        public string? OnMiss { get; set; }
        public int? FullAt { get; set; }
    }

    private sealed class RangeDto
    {
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 100;
    }

    private sealed class ActiveDto
    {
        public QueryDto? Query { get; set; }
        public int? WindowMs { get; set; }
        public int? Attempts { get; set; }
        public FrameDto? Response { get; set; }
        public bool RejectQueryEcho { get; set; }
    }

    private sealed class QueryDto
    {
        public int Length { get; set; }
        public string? Hex { get; set; }
        public int? Pad { get; set; }
    }

    private sealed class DeviceDto
    {
        public string? Vid { get; set; }
        public string? Pid { get; set; }
        public string? Model { get; set; }
        public string? Kind { get; set; }
        public string? Protocol { get; set; }
        public string? PairedPid { get; set; }
        public JsonObject? Override { get; set; }
    }
}

/// <summary>一套规格加载后的只读视图。</summary>
internal sealed class SpecBundle
{
    public SpecBundle(
        Dictionary<string, ProtocolSpec> protocols,
        Dictionary<(ushort, ushort), DeviceEntry> devices,
        Dictionary<(ushort, ushort), ProtocolSpec> deviceSpecs,
        HashSet<ushort> vendors,
        List<ProtocolSpec> ordered,
        string brand)
    {
        Protocols = protocols;
        Devices = devices;
        DeviceSpecs = deviceSpecs;
        Vendors = vendors;
        Ordered = ordered;
        Brand = brand;
    }

    public Dictionary<string, ProtocolSpec> Protocols { get; }
    public Dictionary<(ushort, ushort), DeviceEntry> Devices { get; }
    public Dictionary<(ushort, ushort), ProtocolSpec> DeviceSpecs { get; }
    public HashSet<ushort> Vendors { get; }

    /// <summary>按 JSON 书写顺序排列的协议，即未收录设备的探测优先级。</summary>
    public List<ProtocolSpec> Ordered { get; }

    /// <summary>显示名前缀。</summary>
    public string Brand { get; }
}
