using VigilWatt.Core.Plugins.Ganss;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 0x05AC:024F 键盘族（GANSS GS3104T-Pro / 狼蛛 F108 Pro）的抓包样本、边界与接口隔离回归测试。
/// 2026/10/19 19:45:00 xliu
/// 2026/10/21 扩展：加入狼蛛 F108 Pro 的真机样本与品牌识别用例。
/// </summary>
public sealed class GanssCodecTests
{
    private const string Path = @"\\?\hid#vid_05ac&pid_024f&mi_03#test";

    [Fact]
    public void QueryMatchesCapturedOfficialRequest()
    {
        var expected = Convert.FromHexString("002001000000000000000000000000000000000000000000000000000000000021");
        Assert.Equal(expected, GanssCodec.CreateQuery());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedResponseIs97Percent(bool hidPrefix)
    {
        var payload = Convert.FromHexString("2001006100000000000000000000000000000000000000000000000000000082");
        var input = hidPrefix ? new byte[] { 0 }.Concat(payload).ToArray() : payload;
        Assert.True(GanssCodec.TryParse(input, out int percent));
        Assert.Equal(97, percent);
    }

    /// <summary>
    /// 狼蛛 F108 Pro 真机应答（2026-10-21，05AC:024F，<c>F108Pro Dongle</c>，发送 CreateQuery 后 0.1 秒内回）：
    /// <c>00 20 01 00 64 00 … 00 85</c>，[3]=0x64=100%，末字节 0x85 = 0x20+0x01+0x64。
    /// </summary>
    [Fact]
    public void AulaF108CapturedResponseIs100Percent()
    {
        var report = new byte[GanssCodec.HidReportLength];
        report[1] = 0x20;
        report[2] = 0x01;
        report[4] = 0x64;   // 电量 100%
        report[^1] = 0x85;  // 0x20 + 0x01 + 0x64

        Assert.True(GanssCodec.TryParse(report, out int percent));
        Assert.Equal(100, percent);

        // 去掉 report ID 前缀的 32 字节形态同样要能解析
        Assert.True(GanssCodec.TryParse(report[1..], out percent));
        Assert.Equal(100, percent);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    [InlineData(255, false)]
    public void PercentMustBeNonzeroAndAtMost100(int percent, bool valid)
    {
        var input = Response(percent);
        Assert.Equal(valid, GanssCodec.TryParse(input, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(31)]
    public void CorruptPacketsAreRejected(int offset)
    {
        var input = Response(97);
        input[offset] ^= 1;
        Assert.False(GanssCodec.TryParse(input, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(34)]
    [InlineData(64)]
    public void WrongLengthsAreRejected(int length)
        => Assert.False(GanssCodec.TryParse(new byte[length], out _));

    [Fact]
    public void WrongHidReportIdIsRejected()
    {
        var report = new byte[] { 1 }.Concat(Response(97)).ToArray();
        Assert.False(GanssCodec.TryParse(report, out _));
    }

    [Theory]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 0x61, 33, 33, 0, true)]
    [InlineData(0x05AC, 0x024F, "&MI_03#", 0xFF60, 0x61, 33, 33, 0, true)]
    [InlineData(0x05AC, 0x024F, "&mi_00#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_04#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_030#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x0256, "&mi_03#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x046D, 0x024F, "&mi_03#", 0xFF60, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 1, 0x61, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 1, 33, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 0x61, 64, 33, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 0x61, 33, 64, 0, false)]
    [InlineData(0x05AC, 0x024F, "&mi_03#", 0xFF60, 0x61, 33, 33, 1, false)]
    public void OnlyCapturedVendorInterfaceIsEligible(ushort vid, ushort pid, string segment,
        ushort page, ushort usage, ushort input, ushort output, byte id, bool eligible)
        => Assert.Equal(eligible, GanssCodec.IsSupportedInterface(vid, pid,
            Path.Replace("&mi_03#", segment), "", page, usage, input, output, id));

    [Fact]
    public void UnknownReportIdIsRejected()
        => Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, Path, "", 0xFF60, 0x61, 33, 33, null));

    [Theory]
    [InlineData("GANSS GS3104T-Pro", true)]
    [InlineData("ganss gs3104t-pro", true)]
    [InlineData("GS3104T", true)]
    [InlineData("2.4G Wireless Receiver", false)]
    [InlineData("", false)]
    public void ProductNameCanReplaceInterfaceIndex(string product, bool eligible)
        => Assert.Equal(eligible, GanssCodec.IsSupportedInterface(0x05AC, 0x024F,
            @"\\?\hid#vid_05ac&pid_024f&mi_02#test", product, 0xFF60, 0x61, 33, 33, 0));

    /// <summary>
    /// 狼蛛 F108 Pro 的本机产品名 <c>F108Pro Dongle</c>：接口号漂移到别的 mi 位时靠它兜底，
    /// 这条正是"同 VID/PID/usage/长度 下唯一能区分品牌的证据"。
    /// </summary>
    [Theory]
    [InlineData("F108Pro Dongle", true)]
    [InlineData("f108pro dongle", true)]
    [InlineData("AULA F108 Pro", true)]
    [InlineData("2.4G Wireless Receiver", false)]
    public void AulaProductNameCanReplaceInterfaceIndex(string product, bool eligible)
        => Assert.Equal(eligible, GanssCodec.IsSupportedInterface(0x05AC, 0x024F,
            @"\\?\hid#vid_05ac&pid_024f&mi_04#test", product, 0xFF60, 0x61, 33, 33, 0));

    [Fact]
    public void BrandHintDistinguishesAulaFromGanss()
    {
        Assert.True(GanssCodec.LooksLikeAulaF108("F108Pro Dongle"));
        Assert.False(GanssCodec.LooksLikeAulaF108("GANSS GS3104T-Pro"));
        Assert.True(GanssCodec.LooksLikeGanss("GANSS GS3104T-Pro"));
        Assert.False(GanssCodec.LooksLikeGanss("F108Pro Dongle"));
    }

    [Fact]
    public void ProductNameDoesNotOverrideUsageLengthOrReportId()
    {
        const string VendorPath = @"\\?\hid#vid_05ac&pid_024f&mi_03#test";
        const string Ganss = "GANSS GS3104T-Pro";
        const string Aula = "F108Pro Dongle";
        Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, VendorPath, Ganss, 1, 0x61, 33, 33, 0));
        Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, VendorPath, Ganss, 0xFF60, 1, 33, 33, 0));
        Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, VendorPath, Ganss, 0xFF60, 0x61, 64, 33, 0));
        Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, VendorPath, Ganss, 0xFF60, 0x61, 33, 64, 0));
        Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, VendorPath, Ganss, 0xFF60, 0x61, 33, 33, 1));
        Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, VendorPath, Aula, 0xFF60, 0x61, 33, 64, 0));
        Assert.False(GanssCodec.IsSupportedInterface(0x05AC, 0x024F, VendorPath, Aula, 0xFF60, 0x61, 33, 33, 1));
    }

    private static byte[] Response(int percent)
    {
        var report = new byte[32];
        report[0] = 0x20;
        report[1] = 1;
        report[3] = (byte)percent;
        report[^1] = (byte)(0x21 + percent);
        return report;
    }
}
