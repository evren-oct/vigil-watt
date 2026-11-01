using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Ausdom;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： AusdomCodec —— 阿斯盾 G04 接收器 report 0x0A 状态帧的纯解析。
/// 2026/10/31 22:50:34 Zen
/// 用例全部取自真机采集（vigilwatt-capture.zip）；短帧 C4 的 byte6 为充电位（0x01=充电、0x00=放电，见 ausdom.md §1.3）。
/// </summary>
public class AusdomCodecTests
{
    /// <summary>把前导字节（hex）补零到 64 字节，构造真机同形的 report 0x0A 帧。避免手写 128 位 hex 串错位。</summary>
    private static byte[] Frame(string prefixHex)
    {
        var frame = new byte[64];
        var prefix = Convert.FromHexString(prefixHex);
        Array.Copy(prefix, frame, prefix.Length);
        return frame;
    }

    /// <summary>真机（tsMono 11447）：正常短帧，94%，byte6=0x00（放电态）。</summary>
    private static readonly byte[] Short94 = Frame("0A1205C45E");

    /// <summary>真机（tsMono 68705）：正常短帧，95%，byte6=0x00（放电态）。</summary>
    private static readonly byte[] Short95 = Frame("0A1205C45F");

    /// <summary>短帧 C4，91%，byte6=0x01（充电）。</summary>
    private static readonly byte[] C4Discharging = Frame("0A1205C45B0001");

    /// <summary>短帧 C4，93%，byte6=0x00（放电）。</summary>
    private static readonly byte[] C4Charging = Frame("0A1205C45D0000");

    /// <summary>真机（tsMono 28249）：长帧 C6，B4=0/B5=0x02/B6=94。</summary>
    private static readonly byte[] Long94 = Frame("0A1205C600025E");

    /// <summary>真机（tsMono 83543）：长帧 C6，B4=0/B5=0x02/B6=95。</summary>
    private static readonly byte[] Long95 = Frame("0A1205C600025F");

    /// <summary>真机（tsMono 68162）：短帧 C6，B4=0x01（非电池子消息，应忽略）。</summary>
    private static readonly byte[] SubC6 = Frame("0A1205C601");

    /// <summary>真机（tsMono 28249）：短帧 C7，B4=0x00（非电池子消息，应忽略）。</summary>
    private static readonly byte[] SubC7 = Frame("0A1205C700");

    /// <summary>真机（tsMono 94216）：B2=0x02 关机/子帧，不报电量（应忽略）。</summary>
    private static readonly byte[] PowerOff = Frame("0A1202C300");

    /// <summary>真机（tsMono 42037）：0x13 info 帧（MAC@B5-B10 + 电量@B18=94），空闲周期心拍。</summary>
    private static readonly byte[] InfoFrame = Frame("0A13370005FEF1E51C0CB0150215020201005E");

    /// <summary>真机（tsMono 42241）：0x14 空帧（应忽略）。</summary>
    private static readonly byte[] EmptyFrame = Frame("0A140300");

    private static byte[] WithByte(byte[] frame, int index, byte value)
    {
        var copy = (byte[])frame.Clone();
        copy[index] = value;
        return copy;
    }

    [Fact]
    public void A1_短帧解析出94()
    {
        var r = AusdomCodec.TryParse(Short94);
        Assert.Equal(94, r!.Value.Percent);
        // byte6=0x00 → 放电（真机实测修正）。
        Assert.Equal(ChargingState.Discharging, r.Value.Charging);
    }

    [Fact]
    public void A2_短帧解析出95()
    {
        var r = AusdomCodec.TryParse(Short95);
        Assert.Equal(95, r!.Value.Percent);
    }

    [Fact]
    public void A3_长帧C6解析出94()
    {
        var r = AusdomCodec.TryParse(Long94);
        Assert.Equal(94, r!.Value.Percent);
    }

    [Fact]
    public void A4_长帧C6解析出95()
    {
        var r = AusdomCodec.TryParse(Long95);
        Assert.Equal(95, r!.Value.Percent);
    }

    [Fact]
    public void A5_短帧C6子消息B4为1_忽略()
        => Assert.Null(AusdomCodec.TryParse(SubC6));

    [Fact]
    public void A6_短帧C7子消息B4为0_忽略()
        => Assert.Null(AusdomCodec.TryParse(SubC7));

    [Fact]
    public void A7_关机子帧B2为2_忽略()
        => Assert.Null(AusdomCodec.TryParse(PowerOff));

    [Fact]
    public void A8_info帧0x13_解析电量B18为94()
    {
        var r = AusdomCodec.TryParse(InfoFrame);
        Assert.Equal(94, r!.Value.Percent);
        Assert.Equal(ChargingState.Unknown, r.Value.Charging);
    }

    [Fact]
    public void A9_空帧0x14_忽略()
        => Assert.Null(AusdomCodec.TryParse(EmptyFrame));

    [Fact]
    public void A10_非0x0A_reportId_忽略()
    {
        var other = WithByte(Short94, 0, 0x13);
        Assert.Null(AusdomCodec.TryParse(other));
    }

    [Fact]
    public void A11_长度不足_忽略()
    {
        var shortFrame = new byte[AusdomCodec.StatusReportId == 0 ? 1 : 6];
        shortFrame[0] = AusdomCodec.StatusReportId;
        Assert.Null(AusdomCodec.TryParse(shortFrame));
    }

    [Fact]
    public void A12_电量越界_忽略()
    {
        var over = WithByte(Short94, 4, 101);
        Assert.Null(AusdomCodec.TryParse(over));
    }

    [Fact]
    public void A13_关机瞬间相邻帧_电量不串入()
    {
        // 关机帧（0x02）前后紧挨着正常帧：解析器对每个帧独立判断，不会把关机态误当电量。
        Assert.Null(AusdomCodec.TryParse(PowerOff));
        Assert.Equal(95, AusdomCodec.TryParse(Short95)!.Value.Percent);
    }

    [Fact]
    public void A14_短帧C4_byte6映射充电位()
    {
        // 真机实测修正：byte6=0x01=充电，byte6=0x00=放电。
        Assert.Equal(ChargingState.Charging, AusdomCodec.TryParse(C4Discharging)!.Value.Charging);
        Assert.Equal(ChargingState.Discharging, AusdomCodec.TryParse(C4Charging)!.Value.Charging);

        // 长帧 C6 的 byte6 是电量而非充电位，无可靠充电信息，保持 Unknown。
        Assert.Equal(ChargingState.Unknown, AusdomCodec.TryParse(Long94)!.Value.Charging);
        Assert.Equal(ChargingState.Unknown, AusdomCodec.TryParse(Long95)!.Value.Charging);
    }

    [Fact]
    public void A15_info帧电量越界_忽略()
    {
        // 信息帧 B18 越界（如 0 / 101）不应被当作电量。
        var over = WithByte(InfoFrame, 18, 101);
        Assert.Null(AusdomCodec.TryParse(over));
        var zero = WithByte(InfoFrame, 18, 0);
        Assert.Null(AusdomCodec.TryParse(zero));
    }

    [Fact]
    public void A16_info帧长度不足18_忽略()
    {
        var shortInfo = new byte[18];
        shortInfo[0] = AusdomCodec.StatusReportId;
        shortInfo[1] = 0x13;
        Assert.Null(AusdomCodec.TryParse(shortInfo));
    }

    [Fact]
    public void A17_短帧C4_byte6异常值_充电态Unknown()
    {
        // byte6 只观察到 0x00/0x01 两个值；其它值视为未知，不影响电量解析。
        var weird = WithByte(C4Discharging, 6, 0x7F);
        var r = AusdomCodec.TryParse(weird);
        Assert.Equal(91, r!.Value.Percent);
        Assert.Equal(ChargingState.Unknown, r.Value.Charging);
    }

    [Fact]
    public void A18_信息帧_充电态Unknown()
    {
        // 信息帧 0x13 只报电量（B18），无充电位，保持 Unknown。
        var r = AusdomCodec.TryParse(InfoFrame);
        Assert.Equal(94, r!.Value.Percent);
        Assert.Equal(ChargingState.Unknown, r.Value.Charging);
    }
}
