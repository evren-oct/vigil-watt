using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Mchose;
using VigilWatt.Core.Settings;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从未登记/未验证接口的探测零写入回归。
/// 2026/11/24 22:40:00 20211
/// 用临时文件当 HID 输入，文件长度变化即“确实发过帧”，见 BetopActiveGateTests 同款做法。
/// </summary>
public sealed class MchoseProbeZeroWriteTests
{
    [Fact]
    public void UnknownMouseV2_ProbeDoesNotWrite()
    {
        var info = Info(0x3837, 0x4099, 0xFF00, 0x0002, 64, 64, 0, 0x4D);
        using var fake = new TrackingHandle(info);

        WithMchoseEnabled(() =>
        {
            Assert.Null(new MchosePlugin().ProbeForTest(fake.Handle));
            Assert.Equal(0, fake.Length);
        });
    }

    [Fact]
    public void UnknownKeyboardV1_ProbeDoesNotWrite()
    {
        var info = Info(0x3837, 0x2027, 0xFF02, 0x0002, 20, 20, 0, 0x13);
        using var fake = new TrackingHandle(info);

        WithMchoseEnabled(() =>
        {
            Assert.Null(new MchosePlugin().ProbeForTest(fake.Handle));
            Assert.Equal(0, fake.Length);
        });
    }

    [Fact]
    public void Unverified0x4DTransport_ProbeDoesNotWrite()
    {
        var info = Info(0x3837, 0x1014, 0xFF01, 0x0001, 64, 64, 0, 0x4D);
        using var fake = new TrackingHandle(info);

        WithMchoseEnabled(() =>
        {
            Assert.Null(new MchosePlugin().ProbeForTest(fake.Handle));
            Assert.Equal(0, fake.Length);
        });
    }

    [Fact]
    public void G3V2WrongInterface_ProbeDoesNotWrite()
    {
        var info = Info(0x3837, 0x4245, 0xFF06, 0x0002, 64, 64, 0, 0x00);
        using var fake = new TrackingHandle(info);

        WithMchoseEnabled(() =>
        {
            Assert.Null(new MchosePlugin().ProbeForTest(fake.Handle));
            Assert.Equal(0, fake.Length);
        });
    }

    [Fact]
    public void UnverifiedA5_ProbeDoesNotWrite()
    {
        var info = Info(0x3837, 0x100C, 0xFF01, 0x0001, 0, 0, 21, 0x00);
        using var fake = new TrackingHandle(info);

        WithMchoseEnabled(() =>
        {
            Assert.Null(new MchoseA5Plugin().ProbeForTest(fake.Handle));
            Assert.Equal(0, fake.Length);
        });
    }

    [Fact]
    public void VerifiedKeyboard_PluginDisabled_ProbeDoesNotWrite()
    {
        var info = Info(0x41E4, 0x2001, 0xFF02, 0x0002, 20, 20, 0, 0x13);
        using var fake = new TrackingHandle(info);

        WithMchoseDisabled(() =>
        {
            Assert.Null(new MchosePlugin().ProbeForTest(fake.Handle));
            Assert.Equal(0, fake.Length);
        });
    }

    [Fact]
    public void VerifiedKeyboard_PluginEnabled_ProbeStillWritesSessionFrame()
    {
        var info = Info(0x41E4, 0x2001, 0xFF02, 0x0002, 20, 20, 0, 0x13);
        using var fake = new TrackingHandle(info);

        WithMchoseEnabled(() =>
        {
            Assert.Null(new MchosePlugin().ProbeForTest(fake.Handle));
            Assert.True(fake.Length > 0, "已验证键盘在插件开启时应继续尝试写会话帧");
        });
    }

    private static void WithMchoseEnabled(Action action)
    {
        var settings = AppSettings.Load();
        settings.PluginEnabled.TryGetValue("Mchose", out bool previous);
        settings.PluginEnabled["Mchose"] = true;
        try
        {
            action();
        }
        finally
        {
            settings.PluginEnabled["Mchose"] = previous;
        }
    }

    private static void WithMchoseDisabled(Action action)
    {
        var settings = AppSettings.Load();
        settings.PluginEnabled.TryGetValue("Mchose", out bool previous);
        settings.PluginEnabled["Mchose"] = false;
        try
        {
            action();
        }
        finally
        {
            settings.PluginEnabled["Mchose"] = previous;
        }
    }

    private static HidDeviceInfo Info(
        ushort vid, ushort pid, ushort page, ushort usage,
        ushort input, ushort output, ushort feature, byte reportId)
        => new()
        {
            DevicePath = $"test-{vid:X4}-{pid:X4}-{page:X4}-{usage:X4}-{input}-{output}-{feature}",
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = usage,
            InputReportLength = input,
            OutputReportLength = output,
            FeatureReportLength = feature,
            OutputReportId = reportId,
        };

    private sealed class TrackingHandle : IDisposable
    {
        private readonly FileStream _stream;

        public TrackingHandle(HidDeviceInfo info)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.Read, 0, FileOptions.DeleteOnClose);
            Handle = new HidDeviceHandle(info, _stream);
        }

        public HidDeviceHandle Handle { get; }
        public long Length => _stream.Length;

        public void Dispose() => Handle.Dispose();
    }
}
