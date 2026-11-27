using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VigilWatt.Core.Media;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 把本地设备图片包接到详情页：解析型号、缓存解码后的位图。
/// 2026/11/12 20:05:00 20211
/// 图片只在详情页用；列表、托盘、贴片和小组件仍走 DeviceIconFactory 的几何图标。清单首次用到时才解析。
/// </summary>
public sealed class DeviceImageService
{
    private readonly Lazy<DeviceImageCatalog> _builtIn;
    private readonly Lazy<DeviceImageCatalog> _user;
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DeviceImageService()
    {
        var builtInRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "device-images");
        var userRoot = Path.Combine(AppPaths.RootDir, "device-images");
        _builtIn = new Lazy<DeviceImageCatalog>(() => Load(Strings.Current.Get("S_09ceea7644"), builtInRoot));
        _user = new Lazy<DeviceImageCatalog>(() => Load(Strings.Current.Get("S_9ba763ea34"), userRoot));
    }

    /// <summary>315 个 manifest 常驻不到 1MB，但启动期没必要读 315 次盘；首次打开详情页时再付。</summary>
    private static DeviceImageCatalog Load(string label, string root)
    {
        var catalog = DeviceImageCatalog.Load(root, message => Log.Warn(message));
        Log.Info($"设备图片包（{label}）: {catalog.Count} 个型号");
        return catalog;
    }

    public bool TryResolve(DeviceStatus device, string? colorKey, out DeviceImageAsset asset)
        => _user.Value.TryResolve(device, colorKey, out asset)
           || _builtIn.Value.TryResolve(device, colorKey, out asset);

    public IReadOnlyList<DeviceImageAsset> GetOptions(DeviceStatus device)
    {
        if (_user.Value.TryGetOptions(device, out var options)) return options;
        return _builtIn.Value.TryGetOptions(device, out options) ? options : [];
    }

    /// <summary>同一路径只解码一次；详情页在设备间来回切换时不会反复读盘。</summary>
    public ImageSource GetImage(DeviceImageAsset asset, int decodeWidth)
    {
        // 详情页只有一块小牌子：优先用构图更紧凑的缩略图，整机图留给以后的大图预览。
        var path = asset.ThumbnailPath ?? asset.ImagePath;
        var key = $"{path}|{decodeWidth}";
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        var image = new BitmapImage
        {
            DecodePixelWidth = decodeWidth,
            UriSource = new Uri(path),
        };
        lock (_cache)
        {
            _cache[key] = image;
        }
        return image;
    }
}
