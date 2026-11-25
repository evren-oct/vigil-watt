using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Darkrim;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 寸渊影弑 (DARKRIM) 帧解析回归——用真机抓到的原始帧当用例。
/// 2026/10/19 19:45:00 zc165
/// 帧来源见 docs/hardware/brands/darkrim.md §1.2。
/// </summary>
public class DarkrimCodecTests
{
    /// <summary>真机帧：设备名通知（cmd=0x11 / 子类型 0x03），载荷含 ASCII 型号串。</summary>
    private static byte[] IdentityFrame() => Convert.FromHexString(
        "FE0011000300001000030E000101010930312E30312E303500" + "7ADC");

    /// <summary>真机帧：电量通知（cmd=0x11 / 子类型 0x01），单值形态 0x5A = 90%。</summary>
    private static byte[] BatteryFrame90() => Convert.FromHexString("FE00110003000003000101" + "5A98B4");

    /// <summary>真机帧：GetVersion 应答（cmd=0x10），不含电量。</summary>
    private static byte[] VersionFrame() => Convert.FromHexString(
        "FE0010000200000E00FF0C010208230202030003020100" + "3AEF");

    /// <summary>官方抓到的 GetVersion 请求帧（主机→dongle），用来校准帧构造器。</summary>
    private static byte[] OfficialGetVersionRequest() => Convert.FromHexString(
        "FE0410000200000300090100" + "B37B");

    [Fact]
    public void BuildFrame_ReproducesOfficialGetVersionRequestByteForByte()
    {
        // 构造器必须能逐字节复刻官方发出的帧，否则发出去的查询设备不认。
        var built = DarkrimCodec.BuildFrame(
            seq: 0x04, payload: [0x09, 0x01, 0x00], cmd: 0x10, source: 0x02);

        Assert.Equal(OfficialGetVersionRequest(), built);
    }

    [Fact]
    public void BuildGetBasicInfoRequest_MatchesCapturedBytes()
    {
        // 真机验证过的查询帧：发这条后 dongle 立即回 0x11/01 电量（0x5A = 90%）。
        var built = DarkrimCodec.BuildGetBasicInfoRequest();

        Assert.Equal(Convert.FromHexString("FE0010000200000300090100" + "E23C"), built);
    }

    [Fact]
    public void BuildGetBasicInfoRequest_HasValidCrc()
    {
        Assert.True(DarkrimCodec.IsCrcValid(DarkrimCodec.BuildGetBasicInfoRequest()));
    }

    [Fact]
    public void IsEchoOfRequest_DetectsReportIdPrefixedEcho()
    {
        // 实测 MI_03 回显：00 + 请求帧（前面多一个 report ID 字节）。
        var request = DarkrimCodec.BuildGetBasicInfoRequest();
        var echo = new byte[request.Length + 1];
        echo[0] = 0x00;
        request.CopyTo(echo, 1);

        Assert.True(DarkrimCodec.IsEchoOfRequest(echo));
    }

    [Fact]
    public void IsEchoOfRequest_DoesNotFlagRealBatteryNotify()
    {
        // 真正的电量通知是 0x11 方向，不能被当成回显丢掉——丢了就永远读不到电量。
        Assert.False(DarkrimCodec.IsEchoOfRequest(BatteryFrame90()));
    }

    [Fact]
    public void IsEchoOfRequest_DoesNotFlagIdentityFrame()
    {
        Assert.False(DarkrimCodec.IsEchoOfRequest(IdentityFrame()));
    }

    /// <summary>本应用经 FileStream 裸读时拿到的形态：前面多一个 report ID 字节（实测 0x00）。</summary>
    private static byte[] WithReportIdPrefix(byte[] frame)
    {
        var buf = new byte[frame.Length + 1];
        buf[0] = 0x00;
        frame.CopyTo(buf, 1);
        return buf;
    }

    [Fact]
    public void TryParseBattery_HandlesReportIdPrefixedBuffer()
    {
        // 回归：hidapi 会剥掉 report ID，而本应用走 FileStream 裸读会带着它。
        // 只认下标 0 会让真机所有帧解析失败（2026-10-19 实测踩到）。
        var prefixed = WithReportIdPrefix(BatteryFrame90());

        Assert.True(DarkrimCodec.TryReadHeader(prefixed, out var h));
        Assert.Equal(1, h.BaseOffset);
        Assert.True(DarkrimCodec.TryParseBattery(prefixed, out var report));
        Assert.Equal(90, report.Single);
    }

    [Fact]
    public void IsCrcValid_HandlesReportIdPrefixedBuffer()
    {
        Assert.True(DarkrimCodec.IsCrcValid(WithReportIdPrefix(BatteryFrame90())));
        Assert.True(DarkrimCodec.IsCrcValid(WithReportIdPrefix(IdentityFrame())));
    }

    [Fact]
    public void TryParseLink_HandlesReportIdPrefixedBuffer()
    {
        var up = BuildNotify(subtype: 0x04, data: new byte[] { 0x01 });
        Assert.True(DarkrimCodec.TryParseLink(WithReportIdPrefix(up), out bool linked));
        Assert.True(linked);
    }

    [Fact]
    public void Crc16_MatchesKnownVector()
    {
        // "123456789" 的 CRC-16/CCITT-FALSE 标准校验值 = 0x29B1。
        Assert.Equal(0x29B1, DarkrimCodec.Crc16("123456789"u8));
    }

    [Theory]
    [InlineData("FE0011000300001000030E000101010930312E30312E303500" + "7ADC")]
    [InlineData("FE00110003000003000101" + "5A98B4")]
    [InlineData("FE00100002000008000B0624A9E4B0C061" + "1AB4")]
    public void RealFrames_PassCrcCheck(string hex)
    {
        var frame = Convert.FromHexString(hex);
        Assert.True(DarkrimCodec.IsCrcValid(frame));
    }

    /// <summary>
    /// 作者给的一条状态帧：尾部 CRC 与算法不符（同批另三条都对，这条实算 0x5737、帧里是 0x3AEF）。
    /// 疑似抓包转录错误，待作者真机复核；**不要为了让用例变绿去改帧里的 CRC 值**（BACKLOG CR-83）。
    /// </summary>
    [Fact(Skip = "CR-83：该帧 CRC 与算法不符，待作者真机复核后再定是转录错误还是另有口径")]
    public void RealFrames_CrcMismatch_PendingAuthorCheck()
    {
        var frame = Convert.FromHexString("FE0010000200000E00FF0C010208230202030003020100" + "3AEF");
        Assert.True(DarkrimCodec.IsCrcValid(frame));
    }

    [Fact]
    public void CorruptedFrame_FailsCrcCheck()
    {
        var frame = BatteryFrame90();
        frame[5] ^= 0xFF;
        Assert.False(DarkrimCodec.IsCrcValid(frame));
    }

    [Fact]
    public void TryReadHeader_RejectsForeignMagic()
    {
        var frame = BatteryFrame90();
        frame[0] = 0x00;
        Assert.False(DarkrimCodec.TryReadHeader(frame, out _));
    }

    [Fact]
    public void TryReadHeader_RejectsTruncatedFrame()
    {
        var frame = BatteryFrame90();
        Assert.False(DarkrimCodec.TryReadHeader(frame.AsSpan(0, 8), out _));
    }

    [Fact]
    public void TryParseBattery_ReadsRealSingleValueFrame()
    {
        // 真机帧 01 01 5A：子类型 0x01、剩余长度 0x01、电量 0x5A = 90%。
        Assert.True(DarkrimCodec.TryParseBattery(BatteryFrame90(), out var report));
        Assert.Equal(90, report.Single);
        Assert.Equal(90, DarkrimCodec.Combine(report));
    }

    [Fact]
    public void TryParseBattery_IgnoresIdentityFrame()
    {
        Assert.False(DarkrimCodec.TryParseBattery(IdentityFrame(), out _));
    }

    [Fact]
    public void TryParseBattery_IgnoresVersionFrame()
    {
        Assert.False(DarkrimCodec.TryParseBattery(VersionFrame(), out _));
    }

    [Fact]
    public void TryParseBattery_ThreeSlotLayout_UsesLowerEar()
    {
        // L=88 R=79 C=100：双耳取较低的那只（79）。
        var frame = BuildNotify(subtype: 0x01, data: new byte[] { 88, 79, 100 });

        Assert.True(DarkrimCodec.TryParseBattery(frame, out var report));
        Assert.Equal(88, report.Left);
        Assert.Equal(79, report.Right);
        Assert.Equal(100, report.Case);
        Assert.Equal(79, DarkrimCodec.Combine(report));
    }

    [Fact]
    public void TryParseBattery_SingleEarInUse_TreatsZeroSideAsAbsent()
    {
        // 单耳使用：对侧报 0（不是 0%），不能被当成电量 0 去误报低电量。
        var frame = BuildNotify(subtype: 0x01, data: new byte[] { 64, 0, 0 });

        Assert.True(DarkrimCodec.TryParseBattery(frame, out var report));
        Assert.Equal(64, report.Left);
        Assert.Null(report.Right);
        Assert.Null(report.Case);
        Assert.Equal(64, DarkrimCodec.Combine(report));
    }

    [Fact]
    public void TryParseBattery_AllZeroSlots_IsStillABatteryFrame()
    {
        // 三路全 0 = 耳机没连上。这仍是合法的电量通知帧（不是"别的帧"），
        // 由调用方按"无读数"处理，避免与"帧型不符"混为一谈。
        var frame = BuildNotify(subtype: 0x01, data: new byte[] { 0, 0, 0 });

        Assert.True(DarkrimCodec.TryParseBattery(frame, out var report));
        Assert.False(report.HasAny);
        Assert.Null(DarkrimCodec.Combine(report));
    }

    [Fact]
    public void TryParseBattery_RejectsOutOfRangePercent()
    {
        // 0x7F：bit7 未置位（不是充电位），低 7 位 = 127 > 100 → 无效。
        var frame = BuildNotify(subtype: 0x01, data: new byte[] { 0x7F });
        Assert.False(DarkrimCodec.TryParseBattery(frame, out _));
    }

    [Fact]
    public void TryParseBattery_ChargingBitIsStrippedFromPercent()
    {
        // 真机充电样本：01 01 DA → 0x80 | 0x5A = 充电中 90%（官方界面同时显示"充电中"）。
        var frame = BuildNotify(subtype: 0x01, data: new byte[] { 0xDA });

        Assert.True(DarkrimCodec.TryParseBattery(frame, out var report));
        Assert.Equal(90, report.Single);
        Assert.True(report.Charging);
        Assert.Equal(ChargingState.Charging, DarkrimCodec.MapCharging(90, report.Charging));
    }

    [Fact]
    public void TryParseBattery_DischargingSampleHasNoChargeBit()
    {
        // 真机放电样本：01 01 5A → 90% 放电。
        Assert.True(DarkrimCodec.TryParseBattery(BatteryFrame90(), out var report));
        Assert.Equal(90, report.Single);
        Assert.False(report.Charging);
        Assert.Equal(ChargingState.Discharging, DarkrimCodec.MapCharging(90, report.Charging));
    }

    [Fact]
    public void TryParseBattery_RejectsUnknownSubtype()
    {
        var frame = BuildNotify(subtype: 0x09, data: new byte[] { 50 });
        Assert.False(DarkrimCodec.TryParseBattery(frame, out _));
    }

    [Fact]
    public void TryParseBattery_DoesNotTreatBatterySubtypeAsLink()
    {
        // 回归：真机 01 01 5A 曾被误读成链路帧（子类型 0x01 不是 0x04）。
        Assert.False(DarkrimCodec.TryParseLink(BatteryFrame90(), out _));
    }

    [Fact]
    public void TryParseLink_ReadsConstructedLinkFrame()
    {
        // 链路通知按官方日志 "Notify 0x11/0x04 dongle-headset link=%1" 构造（真机样本待补）。
        var up = BuildNotify(subtype: 0x04, data: new byte[] { 0x01 });
        Assert.True(DarkrimCodec.TryParseLink(up, out bool linked));
        Assert.True(linked);

        var down = BuildNotify(subtype: 0x04, data: new byte[] { 0x00 });
        Assert.True(DarkrimCodec.TryParseLink(down, out bool stillLinked));
        Assert.False(stillLinked);
    }

    [Fact]
    public void MapCharging_ChargingFlagWins()
    {
        Assert.Equal(ChargingState.Charging, DarkrimCodec.MapCharging(50, charging: true));
        Assert.Equal(ChargingState.Full, DarkrimCodec.MapCharging(100, charging: false));
        Assert.Equal(ChargingState.Discharging, DarkrimCodec.MapCharging(50, charging: false));
    }

    /// <summary>按实测帧格式造一条 0x11 通知帧（含正确 CRC），用于边界用例。</summary>
    private static byte[] BuildNotify(byte subtype, byte[] data)
    {
        int payloadLen = 2 + data.Length;
        var frame = new byte[DarkrimCodec.HeaderLength + payloadLen + DarkrimCodec.CrcLength];
        frame[0] = DarkrimCodec.Magic;
        frame[1] = 0x00;                       // seq
        frame[2] = DarkrimCodec.CmdNotify;
        frame[3] = 0x00;                       // 方向：dongle → 主机
        frame[4] = 0x00;                       // 来源
        frame[5] = 0x00;                       // 块号
        frame[6] = 0x01;                       // 块总数
        frame[7] = (byte)payloadLen;
        frame[8] = 0x00;
        frame[9] = subtype;
        frame[10] = (byte)data.Length;
        data.CopyTo(frame, 11);

        ushort crc = DarkrimCodec.Crc16(frame.AsSpan(0, DarkrimCodec.HeaderLength + payloadLen));
        frame[^2] = (byte)(crc >> 8);
        frame[^1] = (byte)(crc & 0xFF);
        return frame;
    }
}
