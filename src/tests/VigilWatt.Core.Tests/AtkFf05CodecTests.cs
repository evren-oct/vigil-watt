using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Atk;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： AtkFf05Codec —— ATK 新固件 FF05 通道 64 字节寄存器读（report 0x08 / 寄存器 0x0107）。
/// 2026/11/01 17:05 xliu
/// 帧样本来自社区 373B:1216 实测（2026-10-31），本机无该设备；判据见 docs/hardware/brands/atk.md 的 FF05 一节。
/// </summary>
public class AtkFf05CodecTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    /// <summary>线上帧固定 64 字节，短样本尾部补 0。</summary>
    private static byte[] Pad64(string s)
    {
        var frame = new byte[AtkFf05Codec.ReportLength];
        var head = Hex(s);
        Array.Copy(head, frame, head.Length);
        return frame;
    }

    /// <summary>实测（2026-10-31）：53% + 3804 mV。</summary>
    private static readonly byte[] Discharging53 = Pad64("08 72 00 05 00 07 01 35 DC 0E");

    /// <summary>实测（同日另一轮）：54% + 3813 mV。</summary>
    private static readonly byte[] Discharging54 = Pad64("08 72 00 05 00 07 01 36 E5 0E");

    /// <summary>实测的充电位寄存器 0x010F 响应：地址回显是 0x0F，不是 0x07。</summary>
    private static readonly byte[] ChargeRegisterReply = Pad64("08 72 00 05 00 0F 01 00 00 00");

    /// <summary>鼠标普通移动报文——不能被当成电量帧。</summary>
    private static readonly byte[] MouseMotionFrame = Pad64("08 01 02 03 04 05 06 07 08 09 0A 0B");

    [Fact]
    public void F1_构造查询帧与实测请求一致()
    {
        var q = AtkFf05Codec.BuildQuery();

        Assert.Equal(AtkFf05Codec.ReportLength, q.Length);
        Assert.Equal("087D720200010701", Convert.ToHexString(q, 0, 8));
    }

    [Fact]
    public void F2_解析实测帧得53和3804毫伏()
    {
        var r = AtkFf05Codec.TryParse(Discharging53);

        Assert.NotNull(r);
        Assert.Equal(53, r!.Value.Percent);
        Assert.Equal(3804, r.Value.Millivolts);
        Assert.Equal(ChargingState.Discharging, r.Value.Charging);
    }

    [Fact]
    public void F3_解析另一条实测帧得54和3813毫伏()
    {
        var r = AtkFf05Codec.TryParse(Discharging54);

        Assert.NotNull(r);
        Assert.Equal(54, r!.Value.Percent);
        Assert.Equal(3813, r.Value.Millivolts);
    }

    [Fact]
    public void F4_充电位寄存器的响应不被误认成电量帧()
    {
        // 地址回显 0x0F ≠ 0x07：只看标记 0x72 就采信，会把充电位的 0 读成 0%
        Assert.Null(AtkFf05Codec.TryParse(ChargeRegisterReply));
    }

    [Fact]
    public void F5_普通鼠标报文不采信()
    {
        Assert.Null(AtkFf05Codec.TryParse(MouseMotionFrame));
        Assert.Null(AtkFf05Codec.TryParse(null));
    }

    [Fact]
    public void F6_电量占位值判为无效()
    {
        // 0 与 1 是休眠/关机占位，不是真实电量
        Assert.Null(AtkFf05Codec.TryParse(Pad64("08 72 00 05 00 07 01 00 DC 0E")));
        Assert.Null(AtkFf05Codec.TryParse(Pad64("08 72 00 05 00 07 01 01 DC 0E")));
    }

    [Fact]
    public void F7_满电判充满()
    {
        var r = AtkFf05Codec.TryParse(Pad64("08 72 00 05 00 07 01 64 DC 0E"));

        Assert.NotNull(r);
        Assert.Equal(100, r!.Value.Percent);
        Assert.Equal(ChargingState.Full, r.Value.Charging);
    }

    [Fact]
    public void F8_reportID被剥掉的63字节帧同样可解析()
    {
        // 部分读路径不带 report ID，解析按"标记 + 寄存器回显"搜索，不写死下标
        var stripped = Discharging53[1..];

        var r = AtkFf05Codec.TryParse(stripped);

        Assert.NotNull(r);
        Assert.Equal(53, r!.Value.Percent);
        Assert.Equal(3804, r.Value.Millivolts);
    }

    [Theory]
    [InlineData(0xFF05, 0x1216, 64, 64, true)]    // 本机实测那只
    [InlineData(0xFF05, 0x1216, 64, 63, false)]   // 长度不符
    [InlineData(0xFF05, 0x10C9, 64, 64, false)]   // VXE R1 的接收器 PID，不走这条
    [InlineData(0xFF02, 0x1216, 64, 64, false)]   // 老口
    [InlineData(0xFFB3, 0x1216, 64, 64, false)]   // 同机存在的另一条厂商口
    public void F9_接口判据要求usagePage与PID双精确匹配(
        ushort usagePage, ushort productId, ushort inLen, ushort outLen, bool expected)
    {
        Assert.Equal(expected, AtkFf05Codec.IsSupportedInterface(usagePage, productId, inLen, outLen));
    }
}
