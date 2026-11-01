using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： 嵌入的雷柏设备目录（VID/PID → 型号 + 设备类型 + 协议族），查不到的设备回落帧形态判定。
/// 2026/11/01 22:20:00 xliu
/// 数据来自官方 AHUB 2.0 主包内嵌 manifest，见 docs/hardware/rapoo-catalog.md §1；底座也收录，但由族决定是否认领。
/// </summary>
internal static class RapooDeviceCatalog
{
    /// <summary>目录条目。<see cref="PairedPid"/> 是同一只设备的有线/无线另一条 PID（0 = 没有）。</summary>
    public readonly record struct Entry(string Model, DeviceKind Kind, string Protocol, ushort PairedPid);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<Dictionary<(ushort, ushort), Entry>> Data = new(Load);

    /// <summary>已收录的 VID:PID 条数（单测用）。</summary>
    public static int Count => Data.Value.Count;

    /// <summary>按 VID/PID 查型号、类型与协议族；未收录返回 false，由调用方回落帧形态判定。</summary>
    public static bool TryGet(ushort vendorId, ushort productId, out Entry entry)
        => Data.Value.TryGetValue((vendorId, productId), out entry);

    private static Dictionary<(ushort, ushort), Entry> Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        const string name = "VigilWatt.Core.Plugins.Rapoo.rapoo-catalog.json";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"嵌入资源缺失: {name}");
        var dto = JsonSerializer.Deserialize<CatalogDto>(stream, JsonOptions)
            ?? throw new InvalidOperationException("rapoo-catalog.json 解析失败");

        var map = new Dictionary<(ushort, ushort), Entry>();
        foreach (var d in dto.Devices ?? [])
        {
            if (!TryParseHex(d.Vid, out var vid) || !TryParseHex(d.Pid, out var pid)) continue;
            if (string.IsNullOrWhiteSpace(d.Protocol)) continue;
            TryParseHex(d.PairedPid, out var paired);
            map[(vid, pid)] = new Entry(d.Model?.Trim() ?? "", KindOf(d.Kind), d.Protocol.Trim(), paired);
        }
        return map;
    }

    /// <summary>目录里的 kind → <see cref="DeviceKind"/>；<c>base</c>（底座/接收器）不产电量，回落 Unknown。</summary>
    private static DeviceKind KindOf(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "mouse" => DeviceKind.Mouse,
        "keyboard" => DeviceKind.Keyboard,
        "headset" => DeviceKind.Headset,
        "gamepad" => DeviceKind.Gamepad,
        _ => DeviceKind.Unknown,
    };

    /// <summary>解析 <c>0x1412</c> / <c>1412</c> 两种写法；非法值返回 false 并跳过该条。</summary>
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
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("protocol")] public string? Protocol { get; set; }
        [JsonPropertyName("pairedPid")] public string? PairedPid { get; set; }
    }
}
