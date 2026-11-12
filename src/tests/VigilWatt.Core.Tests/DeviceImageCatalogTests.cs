using VigilWatt.Core.Media;
using VigilWatt.Core.Models;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： DeviceImageCatalog —— 本地设备图片包的清单解析与型号匹配。
/// 2026/11/12 20:10:00 20211
/// 匹配错会把别的型号的图挂到设备上，所以“名字不匹配不猜图”和路径越界都要钉住。
/// </summary>
public class DeviceImageCatalogTests
{
    private const string G87Manifest = """
        {
          "schemaVersion": 1,
          "brand": "MCHOSE",
          "model": "G87",
          "match": {
            "vendorId": "41E4",
            "productIds": ["2001"],
            "displayNames": ["迈从 G87", "MCHOSE 2.4G Wireless-G87"]
          },
          "defaultColor": "color-white",
          "colors": [
            { "key": "color-white", "label": "白色", "image": "G87_white.png" },
            { "key": "color-black", "label": "黑色", "image": "G87_black.png" }
          ]
        }
        """;

    [Fact]
    public void D1_显示名精确命中_取默认配色()
    {
        var root = CreatePack(G87Manifest, "G87_white.png", "G87_black.png");
        try
        {
            var catalog = DeviceImageCatalog.Load(root);
            var device = Device("迈从 G87");

            Assert.True(catalog.TryResolve(device, null, out var asset));
            Assert.Equal("color-white", asset.ColorKey);
            Assert.EndsWith("G87_white.png", asset.ImagePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void D2_显示名被改过_自报型号加VIDPID仍能命中()
    {
        var root = CreatePack(G87Manifest, "G87_white.png");
        try
        {
            var catalog = DeviceImageCatalog.Load(root);
            var device = Device("我的键盘");
            device.Reported = new ReportedIdentity(0x41E4, 0x2001, "G87");

            Assert.True(catalog.TryResolve(device, null, out var asset));
            Assert.Equal("G87", asset.Model);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void D3_只有同dongle的VIDPID_不猜图()
    {
        var root = CreatePack(G87Manifest, "G87_white.png");
        try
        {
            var catalog = DeviceImageCatalog.Load(root);
            var device = Device("G87 V2");
            device.Usb = new UsbIdentity(0x41E4, 0x2001, null);

            Assert.False(catalog.TryResolve(device, null, out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void D4_默认配色缺失_回退第一项()
    {
        var manifest = G87Manifest.Replace("\"defaultColor\": \"color-white\"", "\"defaultColor\": \"missing\"");
        var root = CreatePack(manifest, "G87_white.png", "G87_black.png");
        try
        {
            var catalog = DeviceImageCatalog.Load(root);

            Assert.True(catalog.TryResolve(Device("迈从 G87"), null, out var asset));
            Assert.Equal("color-white", asset.ColorKey);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void D5_清单路径越界_整条清单不生效()
    {
        var manifest = G87Manifest.Replace("\"G87_white.png\"", "\"../../../outside.png\"");
        var root = CreatePack(manifest, "G87_white.png");
        var outside = Path.Combine(Path.GetDirectoryName(root)!, "outside.png");
        File.WriteAllBytes(outside, [0x89, 0x50, 0x4E, 0x47]);
        try
        {
            var catalog = DeviceImageCatalog.Load(root);

            Assert.False(catalog.TryResolve(Device("迈从 G87"), null, out _));
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void D6_用户指定配色优先于默认色()
    {
        var root = CreatePack(G87Manifest, "G87_white.png", "G87_black.png");
        try
        {
            var catalog = DeviceImageCatalog.Load(root);

            Assert.True(catalog.TryResolve(Device("迈从 G87"), "color-black", out var asset));
            Assert.Equal("color-black", asset.ColorKey);
            Assert.EndsWith("G87_black.png", asset.ImagePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static DeviceStatus Device(string name) => new()
    {
        Id = "mchose:test",
        Name = name,
        Source = DeviceSource.Hid24G,
    };

    private static string CreatePack(string manifest, params string[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "vigilwatt-images-" + Guid.NewGuid().ToString("N"));
        var modelDir = Path.Combine(root, "mchose", "G87");
        Directory.CreateDirectory(modelDir);
        foreach (var file in files)
            File.WriteAllBytes(Path.Combine(modelDir, file), [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(modelDir, "manifest.json"), manifest);
        return root;
    }
}
