using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Langtu;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 狼途 T87 2.4G GET_DEVICE_INFO 的回归用例（真机样本见 docs/hardware/devices/langtu.md §2.2）。
/// 2026/11/17 16:40:00 meish
/// </summary>
public class LangtuT87CodecTests
{
    /// <summary>2026-11-17 真机（键盘唤醒、2.4G）：workMode=2、电量 0x1D=29%、未充电、自报 VID=0C45。</summary>
    private static readonly byte[] Awake29Percent =
        Convert.FromHexString("55101800000000004000000C450C06801601FFFF09000300021D0000000000");

    [Fact]
    public void RealAwakeFrameParses29PercentWorkMode2()
    {
        Assert.True(LangtuT87Codec.TryParse(Awake29Percent, out var reading));
        Assert.Equal(29, reading.Percent);
        Assert.False(reading.Charging);
        Assert.Equal(2, reading.WorkMode);
        Assert.Equal(0x8006, reading.ReportedPid);   // 键盘本体 PID，只记日志
        Assert.Equal(ChargingState.Discharging, LangtuT87Codec.MapCharging(reading));
    }

    [Fact]
    public void WithReportIdPrefixAlsoParses()
    {
        var withId = new byte[Awake29Percent.Length + 1];
        withId[0] = 0x00;
        Array.Copy(Awake29Percent, 0, withId, 1, Awake29Percent.Length);

        Assert.True(LangtuT87Codec.TryParse(withId, out var reading));
        Assert.Equal(29, reading.Percent);
    }

    [Fact]
    public void SleepingAllZeroPayloadRejected()
    {
        // 键盘休眠/关机时 dongle 仍回 0x55 结构帧，但身份与电量全零——不能当成 0%
        var sleeping = Convert.FromHexString("5510180000000000000000000000000000000000000000000000000000000000");
        Assert.False(LangtuT87Codec.TryParse(sleeping, out _));
    }

    [Fact]
    public void RequestFrameUsesPacketSizeNotTotalLength()
    {
        var frame = LangtuT87Codec.BuildInfoRequest(33);
        Assert.Equal(33, frame.Length);
        Assert.Equal(0xAA, frame[1]);
        Assert.Equal(0x10, frame[2]);
        Assert.Equal(24, frame[3]);   // 33 - reportID - 8B 头；写 56 会零应答
        Assert.Equal(0, frame[4]);
        Assert.Equal(0, frame[5]);
        Assert.Equal(0, frame[7]);
    }

    [Fact]
    public void ChargeFlagAndFullMapping()
    {
        var charging = (byte[])Awake29Percent.Clone();
        charging[8 + LangtuT87Codec.ChargeOffset] = 0x01;
        Assert.True(LangtuT87Codec.TryParse(charging, out var onCharge));
        Assert.Equal(ChargingState.Charging, LangtuT87Codec.MapCharging(onCharge));

        var full = (byte[])Awake29Percent.Clone();
        full[8 + LangtuT87Codec.BatteryOffset] = 100;
        Assert.True(LangtuT87Codec.TryParse(full, out var atFull));
        Assert.Equal(ChargingState.Full, LangtuT87Codec.MapCharging(atFull));
    }

    [Fact]
    public void Non0C45ReportedVidRejected()
    {
        // 结构合法（0x55 / 0x10 / 长度够 / 电量在范围内）但不是 0x0C45 设备回的：不能当电量
        var foreign = (byte[])Awake29Percent.Clone();
        foreign[8 + 4] = 0x51;   // 自报 VID 低字节改成 0x51（→ 0x0C51）
        foreign[8 + 5] = 0x0C;
        Assert.False(LangtuT87Codec.TryParse(foreign, out _));

        // 反向：只改自报 PID 不影响解析（型号差异不在判据里）
        var otherModel = (byte[])Awake29Percent.Clone();
        otherModel[8 + 6] = 0x07;
        otherModel[8 + 7] = 0x80;
        Assert.True(LangtuT87Codec.TryParse(otherModel, out var reading));
        Assert.Equal(0x8007, reading.ReportedPid);
        Assert.Equal(29, reading.Percent);
    }

    [Fact]
    public void ProductStringRecognitionAndCleanup()
    {
        Assert.True(LangtuT87Codec.LooksLikeLangtu("Langtu T87-2.4G"));
        Assert.False(LangtuT87Codec.LooksLikeLangtu("2.4G Dongle"));
        Assert.False(LangtuT87Codec.LooksLikeLangtu(""));
    }

    [Fact]
    public void NonProtocolAndTruncatedFramesRejected()
    {
        Assert.False(LangtuT87Codec.TryParse(Convert.FromHexString("0F0101110000"), out _));
        Assert.False(LangtuT87Codec.TryParse(Awake29Percent[..20], out _));

        var wrongCmd = (byte[])Awake29Percent.Clone();
        wrongCmd[1] = 0xA0;
        Assert.False(LangtuT87Codec.TryParse(wrongCmd, out _));
    }
}
