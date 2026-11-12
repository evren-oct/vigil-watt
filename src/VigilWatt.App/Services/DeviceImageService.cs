using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VigilWatt.Core.Media;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 把本地设备图片包接到详情页：解析型号、缓存解码后的位图。
/// 2026/11/12 20:05:00 20211
/// 图片只在详情页用；列表、托盘、贴片和小组件仍走 DeviceIconFactory 的几何图标。
/// </summary>
public sealed class DeviceImageService
{
    private readonly DeviceImageCatalog _builtIn;
    private readonly DeviceImageCatalog _user;
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DeviceImageService()
    {
        var builtInRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "device-images");
        var userRoot = Path.Combine(AppPaths.RootDir, "device-images");
        _builtIn = DeviceImageCatalog.Load(builtInRoot, message => Log.Warn(message));
        _user = DeviceImageCatalog.Load(userRoot, message => Log.Warn(message));
        Log.Info($"设备图片包: 内置 {_builtIn.Count} 个型号 / 用户 {_user.Count} 个型号");
    }

    public bool TryResolve(DeviceStatus device, string? colorKey, out DeviceImageAsset asset)
        => _user.TryResolve(device, colorKey, out asset)
           || _builtIn.TryResolve(device, colorKey, out asset);

    public IReadOnlyList<DeviceImageAsset> GetOptions(DeviceStatus device)
    {
        if (_user.TryGetOptions(device, out var options)) return options;
        return _builtIn.TryGetOptions(device, out options) ? options : [];
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
