using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： HID++ 指针遥测解析（0x2201 / 0x2202 DPI、0x8060 / 0x8061 回报率）的字节布局与越界拒绝。
/// 2026/11/10 22:45:00 20211
/// </summary>
public class HidppPointerCodecTests
{
    [Fact]
    public void P1_调整DPI响应解析大端DPI()
    {
        // [0]=feature index、[1]=func+sw、[2]=传感器回显、[3..4]=DPI
        byte[] resp = { 0x0A, 0x2B, 0x00, 0x06, 0x40 };

        Assert.True(HidppPointerCodec.TryParseDpi(resp, out int dpi));
        Assert.Equal(1600, dpi);
    }

    [Fact]
    public void P2_调整DPI响应过短不解析()
    {
        byte[] resp = { 0x0A, 0x2B, 0x00, 0x06 };

        Assert.False(HidppPointerCodec.TryParseDpi(resp, out int dpi));
        Assert.Equal(0, dpi);
    }

    [Theory]
    [InlineData(0x00, 0x00)]
    [InlineData(0x00, 0x10)]
    [InlineData(0x9C, 0x40)]
    public void P3_调整DPI零值与离谱值丢弃(byte high, byte low)
    {
        byte[] resp = { 0x0A, 0x2B, 0x00, high, low };

        Assert.False(HidppPointerCodec.TryParseDpi(resp, out int dpi));
        Assert.Equal(0, dpi);
    }

    [Fact]
    public void P4_扩展DPI长报文取X轴()
    {
        byte[] resp = new byte[18];
        resp[0] = 0x0B;
        resp[1] = 0x5B;
        resp[2] = 0x00;
        resp[3] = 0x0C;
        resp[4] = 0x80;
        resp[5] = 0x0B;
        resp[6] = 0xB8;

        Assert.True(HidppPointerCodec.TryParseExtendedDpi(resp, out int dpi));
        Assert.Equal(3200, dpi);
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(4, 250)]
    [InlineData(8, 125)]
    public void P5_回报间隔换算赫兹(byte ms, int expectedHz)
    {
        byte[] resp = { 0x0C, 0x1B, ms };

        Assert.True(HidppPointerCodec.TryParseReportRateMs(resp, out int parsed));
        Assert.Equal(ms, parsed);
        Assert.Equal(expectedHz, HidppPointerCodec.HertzFromMilliseconds(parsed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void P6_回报间隔越界不解析(byte ms)
    {
        byte[] resp = { 0x0C, 0x1B, ms };

        Assert.False(HidppPointerCodec.TryParseReportRateMs(resp, out int parsed));
        Assert.Equal(0, parsed);
        Assert.Equal(0, HidppPointerCodec.HertzFromMilliseconds(ms));
    }

    [Theory]
    [InlineData(0, 125)]
    [InlineData(3, 1000)]
    [InlineData(6, 8000)]
    public void P7_扩展回报率码表下标(byte index, int expectedHz)
    {
        byte[] resp = { 0x0D, 0x2B, index };

        Assert.True(HidppPointerCodec.TryParseExtendedReportRateHz(resp, out int hertz));
        Assert.Equal(expectedHz, hertz);
    }

    [Fact]
    public void P8_扩展回报率下标越界不解析()
    {
        byte[] resp = { 0x0D, 0x2B, 0x07 };

        Assert.False(HidppPointerCodec.TryParseExtendedReportRateHz(resp, out int hertz));
        Assert.Equal(0, hertz);
    }
}
