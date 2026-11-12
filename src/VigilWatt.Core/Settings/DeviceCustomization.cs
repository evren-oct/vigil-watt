using VigilWatt.Core.Models;

namespace VigilWatt.Core.Settings;

/// <summary>
/// 功 能： 保存单台设备的名称与图标覆盖值。
/// 2026/10/18 12:19:45 20211
/// </summary>
public sealed class DeviceCustomization
{
    public string? Name { get; set; }
    public DeviceKind? IconKind { get; set; }
    /// <summary>产品图配色 key（如 <c>color-pink-side</c>）；null = 用清单默认色。</summary>
    public string? ImageKey { get; set; }
}

public static class DeviceCustomizationResolver
{
    public static DeviceStatus ApplyName(DeviceStatus source, IReadOnlyDictionary<string, DeviceCustomization> customizations)
    {
        var result = source.Clone();
        if (customizations.TryGetValue(source.Id, out var custom) && !string.IsNullOrWhiteSpace(custom.Name))
            result.Name = custom.Name;
        return result;
    }

    public static DeviceStatus Apply(DeviceStatus source, IReadOnlyDictionary<string, DeviceCustomization> customizations)
    {
        var result = source.Clone();
        if (customizations.TryGetValue(source.Id, out var custom))
        {
            if (!string.IsNullOrWhiteSpace(custom.Name)) result.Name = custom.Name;
            if (custom.IconKind is { } kind) result.Kind = kind;
        }
        return result;
    }

    public static string DisplayName(string id, string fallback, IReadOnlyDictionary<string, DeviceCustomization> customizations) =>
        customizations.TryGetValue(id, out var custom) && !string.IsNullOrWhiteSpace(custom.Name)
            ? custom.Name
            : fallback;

    public static string? ImageKey(string id, IReadOnlyDictionary<string, DeviceCustomization> customizations) =>
        customizations.TryGetValue(id, out var custom) && !string.IsNullOrWhiteSpace(custom.ImageKey)
            ? custom.ImageKey
            : null;
}
