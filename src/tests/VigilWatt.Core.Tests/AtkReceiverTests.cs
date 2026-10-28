using System.Reflection;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Atk;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 验证 ATK 接收器接口筛选和 VXE R1 Nearlink 名称映射，不发送硬件报文。
/// 2026/10/27 10:38:00 lu250
/// </summary>
public class AtkReceiverTests
{
    private static HidDeviceInfo Info(ushort pid, ushort page, ushort input, ushort output)
        => new()
        {
            DevicePath = $@"\\?\hid#vid_373b&pid_{pid:x4}&mi_01&col05#test",
            VendorId = 0x373B, ProductId = pid, UsagePage = page, Usage = 2,
            InputReportLength = input, OutputReportLength = output,
            Product = "NK mouse NANO dongle",
        };

    private static T Invoke<T>(AtkPlugin plugin, string method, object argument)
        => (T)typeof(AtkPlugin).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(plugin, [argument])!;

    [Theory]
    [InlineData(0x10C9, 0xFF02, 17, 17, true)]
    [InlineData(0x10C9, 0xFF02, 20, 20, false)]
    [InlineData(0x10C9, 0xFF02, 17, 20, false)]
    [InlineData(0x10C9, 0xFF02, 20, 17, false)]
    [InlineData(0x10C9, 0xFF04, 0, 0, false)]
    [InlineData(0x10C9, 0x0001, 8, 0, false)]
    [InlineData(0x10C7, 0xFF02, 17, 17, true)]
    [InlineData(0x10C7, 0xFF02, 20, 20, false)]
    [InlineData(0x101A, 0xFF02, 17, 17, true)]
    [InlineData(0x101B, 0xFF02, 17, 17, true)]
    [InlineData(0x1303, 0xFF02, 17, 17, true)]
    public void ReceiverClaimsOnlyBatteryInterface(int pid, int page, int input, int output, bool expected)
    {
        var plugin = new AtkPlugin();
        Assert.Equal(expected, Invoke<bool>(plugin, "IsCandidate",
            Info((ushort)pid, (ushort)page, (ushort)input, (ushort)output)));
    }

    [Fact]
    public void R1NkShowsModelInsteadOfGenericDongleName()
    {
        var plugin = new AtkPlugin();
        Assert.Equal("VXE R1 Nearlink", Invoke<string>(plugin, "BuildDisplayName", Info(0x10C9, 0xFF02, 17, 17)));
        Assert.Equal("VXE R1 Nearlink", Invoke<string>(plugin, "BuildDisplayName", Info(0x10C7, 0xFF02, 17, 17)));
    }

    [Fact]
    public void R1NkWiredBodyTakesOverOnlyItsReceiver()
    {
        var plugin = new AtkPlugin();
        var wireless = Info(0x10C9, 0xFF02, 17, 17);
        var wired = Info(0x10C7, 0xFF02, 17, 17);
        var other = Info(0x101B, 0xFF02, 17, 17);
        Assert.Equal(new[] { wireless.DevicePath }, Invoke<IReadOnlyCollection<string>>(plugin,
            "SkipPathsForThisPass", new[] { wireless, wired, other }));
        Assert.Empty(Invoke<IReadOnlyCollection<string>>(plugin,
            "SkipPathsForThisPass", new[] { wireless, other }));
        Assert.Empty(Invoke<IReadOnlyCollection<string>>(plugin,
            "SkipPathsForThisPass", new[] { wireless, Info(0x10C7, 0xFF02, 20, 20) }));
    }

    [Fact]
    public void R1NkKeepsSameIdentityAcrossWiredAndWireless()
    {
        var plugin = new AtkPlugin();
        var wireless = Invoke<string>(plugin, "IdFor", Info(0x10C9, 0xFF02, 17, 17).DevicePath);
        var wired = Invoke<string>(plugin, "IdFor", Info(0x10C7, 0xFF02, 17, 17).DevicePath);
        Assert.Equal(wireless, wired);
        Assert.NotEqual(wireless, Invoke<string>(plugin, "IdFor", Info(0x101B, 0xFF02, 17, 17).DevicePath));
    }
}
