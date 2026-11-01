using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 嵌入的迈从设备目录（本体 VID/PID → 型号 + 协议族），查不到的设备回落到接口形态判定。
/// 2026/11/01 20:45:00 xliu
/// 数据来自官方 M HUB WEB 的 cardList / Vb 表，见 docs/hardware/mchose-catalog.md §1；共用 dongle 不收录（靠形态判定）。
/// </summary>
internal static class MchoseDeviceCatalog
{
    /// <summary>目录条目：型号名可能为空串，协议族为 <c>MchoseInterfaceClassifier.KindFromProtocol</c> 认得的名字。</summary>
    public readonly record struct Entry(string Model, string Protocol);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<Dictionary<(ushort, ushort), Entry>> Data = new(Load);

    /// <summary>按本体 VID/PID 查型号与协议族；未收录返回 false，由调用方回落接口形态判定。</summary>
    public static bool TryGet(ushort vendorId, ushort productId, out Entry entry)
        => Data.Value.TryGetValue((vendorId, productId), out entry);

    private static Dictionary<(ushort, ushort), Entry> Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        const string name = "VigilWatt.Core.Plugins.Mchose.mchose-catalog.json";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"嵌入资源缺失: {name}");
        var dto = JsonSerializer.Deserialize<CatalogDto>(stream, JsonOptions)
            ?? throw new InvalidOperationException("mchose-catalog.json 解析失败");

        var map = new Dictionary<(ushort, ushort), Entry>();
        foreach (var d in dto.Devices ?? [])
        {
            if (!TryParseHex(d.Vid, out var vid) || !TryParseHex(d.Pid, out var pid)) continue;
            if (string.IsNullOrWhiteSpace(d.Protocol)) continue;
            map[(vid, pid)] = new Entry(d.Model?.Trim() ?? "", d.Protocol.Trim());
        }
        return map;
    }

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
    }

    private sealed class DeviceDto
    {
        [JsonPropertyName("vid")] public string? Vid { get; set; }
        [JsonPropertyName("pid")] public string? Pid { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    }
}
