using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Media;

/// <summary>
/// 功 能： 读取本地设备图片包，按型号身份选出产品图。
/// 2026/11/12 19:50:00 20211
/// 不按设备 ID 匹配（插线/换口会变），也不联网；格式见 docs/project/device-images.md。
/// </summary>
public sealed class DeviceImageCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IReadOnlyList<Entry> _entries;

    private DeviceImageCatalog(IReadOnlyList<Entry> entries) => _entries = entries;

    public static DeviceImageCatalog Empty { get; } = new([]);

    public int Count => _entries.Count;

    /// <summary>扫描 <paramref name="rootDir"/> 下所有 manifest.json；坏清单只跳过，不拖垮其余型号。</summary>
    public static DeviceImageCatalog Load(string rootDir, Action<string>? onWarning = null)
    {
        if (!Directory.Exists(rootDir)) return Empty;

        string[] manifests;
        try
        {
            manifests = Directory.EnumerateFiles(rootDir, "manifest.json", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception ex)
        {
            onWarning?.Invoke($"设备图片包扫描失败: {ex.Message}");
            return Empty;
        }

        var entries = new List<Entry>();
        foreach (var path in manifests)
        {
            try
            {
                if (Entry.TryLoad(path, rootDir) is { } entry) entries.Add(entry);
            }
            catch (Exception ex)
            {
                onWarning?.Invoke($"设备图片清单跳过 {path}: {ex.Message}");
            }
        }
        return new DeviceImageCatalog(entries);
    }

    public bool TryResolve(DeviceStatus device, string? colorKey, out DeviceImageAsset asset)
        => TryResolve(device.Name, device.Reported, device.Usb, colorKey, out asset);

    /// <summary>同一型号的全部配色；没有匹配到型号时返回空列表。</summary>
    public bool TryGetOptions(DeviceStatus device, out IReadOnlyList<DeviceImageAsset> options)
    {
        var entry = FindBest(device.Name, device.Reported, device.Usb);
        if (entry is null)
        {
            options = [];
            return false;
        }

        options = entry.Options;
        return true;
    }

    /// <summary>
    /// 匹配顺序：显示名精确命中优先；显示名被改过时，只有“协议自报型号 + VID/PID”同时命中才认。
    /// 只有 VID/PID 不足以选图——同一颗 dongle 常被多款型号共用。
    /// </summary>
    public bool TryResolve(
        string? displayName, ReportedIdentity? reported, UsbIdentity? usb, string? colorKey,
        out DeviceImageAsset asset)
    {
        var entry = FindBest(displayName, reported, usb);
        if (entry is null)
        {
            asset = default;
            return false;
        }

        asset = entry.Pick(colorKey);
        return true;
    }

    private Entry? FindBest(string? displayName, ReportedIdentity? reported, UsbIdentity? usb)
    {
        var name = Normalize(displayName);
        var model = Normalize(reported?.Model);

        Entry? best = null;
        var bestScore = 0;
        foreach (var entry in _entries)
        {
            var score = entry.Score(name, model, reported, usb);
            if (score <= bestScore) continue;
            best = entry;
            bestScore = score;
        }

        return best;
    }

    /// <summary>只保留字母与数字并转小写：空格、连字符、下划线不参与型号比较。</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    private static bool TryParseHex(string? text, out ushort value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private sealed class Entry
    {
        private Entry(string model, HashSet<string> names, ushort vendorId,
            HashSet<ushort> productIds, IReadOnlyList<DeviceImageAsset> options, DeviceImageAsset defaultAsset)
        {
            Model = model;
            Names = names;
            VendorId = vendorId;
            ProductIds = productIds;
            Options = options;
            DefaultAsset = defaultAsset;
        }

        public string Model { get; }
        public HashSet<string> Names { get; }
        public ushort VendorId { get; }
        public HashSet<ushort> ProductIds { get; }
        public IReadOnlyList<DeviceImageAsset> Options { get; }
        public DeviceImageAsset DefaultAsset { get; }

        public DeviceImageAsset Pick(string? colorKey)
        {
            if (!string.IsNullOrWhiteSpace(colorKey))
            {
                foreach (var option in Options)
                    if (string.Equals(option.ColorKey, colorKey.Trim(), StringComparison.OrdinalIgnoreCase))
                        return option;
            }
            return DefaultAsset;
        }

        public int Score(string name, string model, ReportedIdentity? reported, UsbIdentity? usb)
        {
            var sameVidPid = MatchesVidPid(reported, usb);
            if (name.Length > 0 && Names.Contains(name)) return sameVidPid ? 110 : 100;
            if (model.Length > 0 && Normalize(Model) == model && sameVidPid) return 90;
            return 0;
        }

        private bool MatchesVidPid(ReportedIdentity? reported, UsbIdentity? usb)
        {
            if (reported is { IsValid: true } r && r.VendorId == VendorId && ProductIds.Contains(r.ProductId))
                return true;
            if (usb is { IsValid: true } u && u.VendorId == VendorId && ProductIds.Contains(u.ProductId))
                return true;
            return false;
        }

        public static Entry? TryLoad(string manifestPath, string rootDir)
        {
            var dir = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrEmpty(dir)) return null;

            var dto = JsonSerializer.Deserialize<ManifestDto>(File.ReadAllText(manifestPath), JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Model) || dto.Colors is not { Count: > 0 })
                return null;

            var names = new HashSet<string>(StringComparer.Ordinal);
            if (dto.Match?.DisplayNames is { } displayNames)
            {
                foreach (var displayName in displayNames)
                {
                    var normalized = Normalize(displayName);
                    if (normalized.Length > 0) names.Add(normalized);
                }
            }
            var model = dto.Model.Trim();
            var modelKey = Normalize(model);
            if (modelKey.Length > 0) names.Add(modelKey);

            var colors = new List<DeviceImageAsset>();
            foreach (var color in dto.Colors)
            {
                if (string.IsNullOrWhiteSpace(color.Key) || string.IsNullOrWhiteSpace(color.Image)) continue;
                var thumbnailPath = ResolveFile(dir, rootDir, color.Thumbnail);
                var imagePath = ResolveFile(dir, rootDir, color.Image) ?? thumbnailPath;
                if (imagePath is null) continue;
                colors.Add(new DeviceImageAsset(
                    model, color.Key.Trim(), color.Label?.Trim(), imagePath, thumbnailPath,
                    color.Source?.Trim() ?? dto.Source?.Trim()));
            }
            if (colors.Count == 0) return null;

            var defaultAsset = colors[0];
            if (!string.IsNullOrWhiteSpace(dto.DefaultColor))
            {
                foreach (var color in colors)
                {
                    if (!string.Equals(color.ColorKey, dto.DefaultColor.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                    defaultAsset = color;
                    break;
                }
            }

            var vendorId = TryParseHex(dto.Match?.VendorId, out var vid) ? vid : (ushort)0;
            var productIds = new HashSet<ushort>();
            if (dto.Match?.ProductIds is { } pids)
            {
                foreach (var pidText in pids)
                    if (TryParseHex(pidText, out var pid)) productIds.Add(pid);
            }

            return new Entry(model, names, vendorId, productIds, colors, defaultAsset);
        }

        /// <summary>清单里的相对路径必须落在图片包根目录内；越界路径直接忽略。</summary>
        private static string? ResolveFile(string dir, string rootDir, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            try
            {
                var root = Path.GetFullPath(rootDir);
                var full = Path.GetFullPath(Path.Combine(dir, relativePath));
                var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) return null;
                return File.Exists(full) ? full : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private sealed class ManifestDto
    {
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("match")] public MatchDto? Match { get; set; }
        [JsonPropertyName("defaultColor")] public string? DefaultColor { get; set; }
        [JsonPropertyName("colors")] public List<ColorDto>? Colors { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
    }

    private sealed class MatchDto
    {
        [JsonPropertyName("vendorId")] public string? VendorId { get; set; }
        [JsonPropertyName("productIds")] public List<string>? ProductIds { get; set; }
        [JsonPropertyName("displayNames")] public List<string>? DisplayNames { get; set; }
    }

    private sealed class ColorDto
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
        [JsonPropertyName("image")] public string? Image { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
    }
}

/// <summary>清单里的一条配色；路径是绝对路径，调用方只负责显示。</summary>
public readonly record struct DeviceImageAsset(
    string Model, string ColorKey, string? Label, string ImagePath, string? ThumbnailPath, string? Source);
