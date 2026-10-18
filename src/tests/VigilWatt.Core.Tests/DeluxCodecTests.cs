using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Delux;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： DeluxCodec —— DELUX M900 Pro 5 字节状态帧的纯解析。
/// 2026/10/19 02:20:00 sqcxz
/// 用例帧来自官方驱动实测：03 30 40 01 59（89% 放电）/ 03 30 40 03 59（89% 充电）。
/// </summary>
public class DeluxCodecTests
{
    private static readonly byte[] DischargingFrame = Convert.FromHexString("0330400159");
    private static readonly byte[] ChargingFrame = Convert.FromHexString("0330400359");
    private static readonly byte[] FullFrame = Convert.FromHexString("0330400264");

    [Fact]
    public void D1_真机放电帧解析出89和放电()
    {
        Assert.True(DeluxCodec.TryParse(DischargingFrame, out int percent, out var charging, out byte flag));
        Assert.Equal(89, percent);
        Assert.Equal(ChargingState.Discharging, charging);
        Assert.Equal(0x01, flag);
    }

    [Fact]
    public void D2_真机充电帧解析出89和充电()
    {
        Assert.True(DeluxCodec.TryParse(ChargingFrame, out int percent, out var charging, out byte flag));
        Assert.Equal(89, percent);
        Assert.Equal(ChargingState.Charging, charging);
        Assert.Equal(0x03, flag);
    }

    [Fact]
    public void D3_满电标志解析为Full()
    {
        Assert.True(DeluxCodec.TryParse(FullFrame, out int percent, out var charging, out byte flag));
        Assert.Equal(100, percent);
        Assert.Equal(ChargingState.Full, charging);
        Assert.Equal(0x02, flag);
    }

    [Fact]
    public void D4_100percent且充电中优先判满电()
    {
        var frame = Convert.FromHexString("0330400364");
        Assert.True(DeluxCodec.TryParse(frame, out int percent, out var charging, out _));
        Assert.Equal(100, percent);
        Assert.Equal(ChargingState.Full, charging);
    }

    [Fact]
    public void D10_100percent但仍在放电保持Discharging()
    {
        var frame = Convert.FromHexString("0330400164");
        Assert.True(DeluxCodec.TryParse(frame, out int percent, out var charging, out _));
        Assert.Equal(100, percent);
        Assert.Equal(ChargingState.Discharging, charging);
    }

    [Fact]
    public void D5_不带reportID的4字节帧也能解析()
    {
        var frame = Convert.FromHexString("30400159");
        Assert.True(DeluxCodec.TryParse(frame, out int percent, out var charging, out _));
        Assert.Equal(89, percent);
        Assert.Equal(ChargingState.Discharging, charging);
    }

    [Fact]
    public void D6_标记不符不算本协议帧()
    {
        var frame = Convert.FromHexString("0331400159");
        Assert.False(DeluxCodec.TryParse(frame, out _, out _, out _));
    }

    [Fact]
    public void D7_长度不足不算本协议帧()
    {
        var frame = Convert.FromHexString("033040");
        Assert.False(DeluxCodec.TryParse(frame, out _, out _, out _));
    }

    [Fact]
    public void D8_电量越界不算本协议帧()
    {
        var frame = Convert.FromHexString("0330400165");
        Assert.False(DeluxCodec.TryParse(frame, out _, out _, out _));
    }

    [Fact]
    public void D9_未知充电标志返回Unknown()
    {
        var frame = Convert.FromHexString("0330400059");
        Assert.True(DeluxCodec.TryParse(frame, out int percent, out var charging, out byte flag));
        Assert.Equal(89, percent);
        Assert.Equal(ChargingState.Unknown, charging);
        Assert.Equal(0x00, flag);
    }
}
