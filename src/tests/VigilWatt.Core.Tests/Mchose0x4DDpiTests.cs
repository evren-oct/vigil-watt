using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从 0x4D 的 DPI 只读解析（`0x0003`）与档位事件（`0x0600` bit0）回归。
/// 2026/11/08 21:40:00 xliu
/// 字段表来自官方 `dpiToObj` / `mcMonitor`，见 docs/hardware/mchose-dpi-report-rate-audit.md §2、§3。
/// 合成帧只钉解析口径，**不代表任何型号的真机验证**。
/// </summary>
public sealed class Mchose0x4DDpiTests
{
    /// <summary>按官方帧布局造应答：`4D 01 01 len cmdLo cmdHi 00 00 payload…`，补零到 64。</summary>
    private static byte[] Reply(ushort cmd, params byte[] payload)
    {
        var frame = new byte[Mchose0x4DCodec.FrameLength];
        frame[0] = Mchose0x4DCodec.ReportId;
        frame[1] = Mchose0x4DCodec.Version;
        frame[2] = Mchose0x4DCodec.Flags;
        frame[3] = (byte)payload.Length;
        frame[4] = (byte)(cmd & 0xFF);
        frame[5] = (byte)(cmd >> 8);
        payload.CopyTo(frame, 8);
        return frame;
    }

    /// <summary>DPI 应答 payload：5 个字段 + 6 档小端 16 位。</summary>
    private static byte[] DpiPayload(byte profile, byte direction, byte sum, byte active, byte hasY,
        params int[] values)
    {
        Assert.Equal(Mchose0x4DCodec.DpiStageCount, values.Length);
        var payload = new byte[5 + Mchose0x4DCodec.DpiStageCount * 2];
        payload[0] = profile;
        payload[1] = direction;
        payload[2] = sum;
        payload[3] = active;
        payload[4] = hasY;
        for (int i = 0; i < values.Length; i++)
        {
            payload[5 + i * 2] = (byte)(values[i] & 0xFF);
            payload[6 + i * 2] = (byte)(values[i] >> 8);
        }
        return payload;
    }

    private static readonly int[] Stages = [400, 800, 1600, 3200, 6400, 26000];

    // ── 0x0003 请求 ────────────────────────────────────────────────

    /// <summary>请求 payload 就是 <c>[profileIndex, direction]</c>，长度 2、命令 0x0003。</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    public void BuildDpiQueryCarriesProfileAndDirection(byte profile, byte direction)
    {
        var frame = Mchose0x4DCodec.BuildDpiQuery(profile, direction);

        Assert.Equal(Mchose0x4DCodec.FrameLength, frame.Length);
        Assert.Equal(Mchose0x4DCodec.ReportId, frame[0]);
        Assert.Equal(2, frame[3]);                       // payload 长度
        Assert.Equal(0x03, frame[4]);                    // cmdLo
        Assert.Equal(0x00, frame[5]);                    // cmdHi
        Assert.Equal(profile, frame[8]);
        Assert.Equal(direction, frame[9]);

        byte xor = 0;
        for (int i = 2; i < 8 + 2; i++) xor ^= frame[i];
        Assert.Equal(xor, frame[10]);
    }

    /// <summary>方向只认 0/1；其它值属于调用错误，直接抛，不发出去。</summary>
    [Fact]
    public void BuildDpiQueryRejectsUnknownDirection()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Mchose0x4DCodec.BuildDpiQuery(0, 2));

    // ── 0x0003 应答 ────────────────────────────────────────────────

    /// <summary>X 轴：6 档、活动档位、档数、hasY 各归各位。</summary>
    [Fact]
    public void ParsesXAxisDpi()
    {
        var frame = Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(2, 0, 6, 2, 1, Stages));

        Assert.True(Mchose0x4DCodec.TryParseDpi(frame, out var reading));
        Assert.Equal(2, reading.ProfileIndex);
        Assert.Equal(Mchose0x4DCodec.DirectionX, reading.Direction);
        Assert.Equal(6, reading.DpiSum);
        Assert.Equal(2, reading.ActiveIndex);
        Assert.True(reading.HasY);
        Assert.Equal(Stages, reading.Values);
    }

    /// <summary>Y 轴是**另一次请求**的结果，方向要如实保留。</summary>
    [Fact]
    public void ParsesYAxisDpi()
    {
        var y = new[] { 1200, 1200, 1200, 1200, 1200, 1200 };
        var frame = Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(0, 1, 6, 5, 1, y));

        Assert.True(Mchose0x4DCodec.TryParseDpi(frame, out var reading));
        Assert.Equal(Mchose0x4DCodec.DirectionY, reading.Direction);
        Assert.Equal(5, reading.ActiveIndex);
        Assert.Equal(y, reading.Values);
    }

    /// <summary>六档按小端 16 位读：26000 = 0x6590，字节序写反就会变成 0x9065。</summary>
    [Fact]
    public void ParsesSixStagesLittleEndian()
    {
        var payload = DpiPayload(0, 0, 6, 0, 0, Stages);
        Assert.Equal(0x90, payload[5 + 5 * 2]);      // 第 6 档低字节
        Assert.Equal(0x65, payload[6 + 5 * 2]);      // 第 6 档高字节

        Assert.True(Mchose0x4DCodec.TryParseDpi(Reply(Mchose0x4DCodec.CmdDpi, payload), out var reading));
        Assert.Equal(26000, reading[5]);
        Assert.Equal(400, reading[0]);
    }

    /// <summary>无 Y 轴：hasY=0 时只表示"没有独立 Y 配置"，不影响解析。</summary>
    [Fact]
    public void ParsesWithoutYAxis()
    {
        var frame = Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(1, 0, 6, 1, 0, Stages));

        Assert.True(Mchose0x4DCodec.TryParseDpi(frame, out var reading));
        Assert.False(reading.HasY);
        Assert.Equal(800, reading[1]);
    }

    /// <summary>活动档位越界（6 / 255）说明帧不可信，整条拒绝，不猜一个索引出来。</summary>
    [Theory]
    [InlineData(6)]
    [InlineData(255)]
    public void RejectsIllegalActiveIndex(byte active)
    {
        var frame = Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(0, 0, 6, active, 1, Stages));

        Assert.False(Mchose0x4DCodec.TryParseDpi(frame, out _));
    }

    /// <summary>payload 少一个字节（16 &lt; 17）就不够读 6 档，拒绝。</summary>
    [Fact]
    public void RejectsShortPayload()
    {
        var payload = DpiPayload(0, 0, 6, 0, 1, Stages)[..16];
        var frame = Reply(Mchose0x4DCodec.CmdDpi, payload);

        Assert.False(Mchose0x4DCodec.TryParseDpi(frame, out _));
    }

    /// <summary>别的命令的应答（比如 0x0900）不能被当成 DPI。</summary>
    [Fact]
    public void RejectsOtherCommands()
    {
        var frame = Reply(Mchose0x4DCodec.CmdDeviceInfo, DpiPayload(0, 0, 6, 0, 1, Stages));

        Assert.False(Mchose0x4DCodec.TryParseDpi(frame, out _));
    }

    /// <summary>当前档位的值是 0：这一档没配置，拿它当"当前 DPI"会误导，整条拒绝。</summary>
    [Fact]
    public void RejectsZeroAtActiveStage()
    {
        var values = new[] { 400, 800, 0, 3200, 6400, 26000 };
        var frame = Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(0, 0, 6, 2, 1, values));

        Assert.False(Mchose0x4DCodec.TryParseDpi(frame, out _));
    }

    /// <summary>不按型号上限裁剪：官方最高 50000，但这里只做结构校验，60000 照收——型号上限不在解析层猜。</summary>
    [Fact]
    public void DoesNotClampToModelMax()
    {
        var values = new[] { 400, 800, 1600, 3200, 6400, 60000 };
        var frame = Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(0, 0, 6, 5, 1, values));

        Assert.True(Mchose0x4DCodec.TryParseDpi(frame, out var reading));
        Assert.Equal(60000, reading[5]);
    }

    // ── ActiveDpi 的边界 ───────────────────────────────────────────

    /// <summary>读到的档位**不是**当前活动档时，不许算 ActiveDpi——这是本阶段最重要的一条边界。</summary>
    [Fact]
    public void DoesNotComputeActiveDpiForUnknownProfile()
    {
        Assert.True(Mchose0x4DCodec.TryParseDpi(
            Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(0, 0, 6, 2, 1, Stages)), out var reading));

        Assert.False(Mchose0x4DCodec.TryGetActiveDpi(reading, activeProfileIndex: 1, out int dpi));
        Assert.Equal(0, dpi);
    }

    /// <summary>确认是活动档时才用 <c>Values[ActiveIndex]</c>；不是 <c>Values[0]</c>。</summary>
    [Fact]
    public void ComputesActiveDpiForConfirmedProfile()
    {
        Assert.True(Mchose0x4DCodec.TryParseDpi(
            Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(2, 0, 6, 4, 1, Stages)), out var reading));

        Assert.True(Mchose0x4DCodec.TryGetActiveDpi(reading, activeProfileIndex: 2, out int dpi));
        Assert.Equal(6400, dpi);
        Assert.NotEqual(Stages[0], dpi);
    }

    // ── 要不要读 Y 轴 ──────────────────────────────────────────────

    /// <summary>只有"X 解析成功"且"设备声明有 Y 轴"两条同时成立才发第二条请求。</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]     // hasY=false：不读 Y
    [InlineData(false, true, false)]     // X 都没解析出来：不继续读 Y
    [InlineData(false, false, false)]
    public void DecidesWhetherToReadYAxis(bool xParsed, bool hasY, bool expected)
        => Assert.Equal(expected, Mchose0x4DCodec.ShouldReadYAxis(xParsed, hasY));

    /// <summary>Y 读取失败（长度不足）不产出半成品，也不污染已拿到的 X 结果。</summary>
    [Fact]
    public void FailedYReadKeepsXReading()
    {
        Assert.True(Mchose0x4DCodec.TryParseDpi(
            Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(0, 0, 6, 2, 1, Stages)), out var x));

        var shortY = Reply(Mchose0x4DCodec.CmdDpi, DpiPayload(0, 1, 6, 2, 1, Stages)[..10]);

        Assert.False(Mchose0x4DCodec.TryParseDpi(shortY, out var y));
        Assert.Null(y.Values);                                   // 失败不产出半成品
        Assert.Equal(Mchose0x4DCodec.DirectionX, x.Direction);   // X 仍是 X
        Assert.Equal(1600, x[2]);
    }

    // ── 0x0600 档位事件 ────────────────────────────────────────────

    /// <summary>只有档位位、payload 只有 3 字节：**不能**因为缺电量/充电被整条丢掉。</summary>
    [Fact]
    public void ParsesDpiOnlyEvent()
    {
        var frame = Reply(Mchose0x4DCodec.CmdStatusEvent, 0x01, 0x00, 0x02);

        Assert.True(Mchose0x4DCodec.TryParseStatusEvent(frame, out var evt));
        Assert.Equal((byte?)2, evt.DpiIndex);
        Assert.Null(evt.Percent);
        Assert.Null(evt.ChargeStatus);
    }

    /// <summary>只有电源位的事件里不该冒出档位值。</summary>
    [Fact]
    public void PowerOnlyEventCarriesNoDpi()
    {
        var frame = Reply(Mchose0x4DCodec.CmdStatusEvent, 0x02, 0x00, 0x00, 0x64);

        Assert.True(Mchose0x4DCodec.TryParseStatusEvent(frame, out var evt));
        Assert.Equal(100, evt.Percent);
        Assert.Null(evt.DpiIndex);
    }

    /// <summary>档位与电量同时带：两项都要，互不覆盖。</summary>
    [Fact]
    public void ParsesDpiAndBatteryTogether()
    {
        var frame = Reply(Mchose0x4DCodec.CmdStatusEvent, 0x03, 0x00, 0x03, 0x50);

        Assert.True(Mchose0x4DCodec.TryParseStatusEvent(frame, out var evt));
        Assert.Equal((byte?)3, evt.DpiIndex);
        Assert.Equal(80, evt.Percent);
        Assert.Null(evt.ChargeStatus);
    }

    /// <summary>档位与充电同时带（充电位在 payload[12]，比档位远）。</summary>
    [Fact]
    public void ParsesDpiAndChargeTogether()
    {
        var payload = new byte[13];
        payload[0] = 0x01;      // 档位位
        payload[1] = 0x02;      // 充电位
        payload[2] = 4;         // 档位
        payload[12] = 1;        // 充电中

        Assert.True(Mchose0x4DCodec.TryParseStatusEvent(Reply(Mchose0x4DCodec.CmdStatusEvent, payload),
            out var evt));
        Assert.Equal((byte?)4, evt.DpiIndex);
        Assert.Equal((byte?)1, evt.ChargeStatus);
        Assert.Null(evt.Percent);
    }

    /// <summary>纯档位事件**不带**电量与充电字段，所以不会覆盖已有的电池/充电状态。</summary>
    [Fact]
    public void DpiEventCarriesNothingToOverwritePower()
    {
        Assert.True(Mchose0x4DCodec.TryParseStatusEvent(
            Reply(Mchose0x4DCodec.CmdStatusEvent, 0x01, 0x00, 0x05), out var evt));

        Assert.Null(evt.Percent);
        Assert.Null(evt.ChargeStatus);
    }

    /// <summary>位图说带了档位、值却越界：丢这一项，不猜；只剩它就整条不算事件。</summary>
    [Theory]
    [InlineData(6)]
    [InlineData(255)]
    public void IgnoresOutOfRangeDpiIndex(byte stage)
        => Assert.False(Mchose0x4DCodec.TryParseStatusEvent(
            Reply(Mchose0x4DCodec.CmdStatusEvent, 0x01, 0x00, stage), out _));

    /// <summary>未知位（既不是档位也不是电量）一律忽略，不推断含义。</summary>
    [Fact]
    public void IgnoresUnknownBits()
        => Assert.False(Mchose0x4DCodec.TryParseStatusEvent(
            Reply(Mchose0x4DCodec.CmdStatusEvent, 0x04, 0x00, 0x00, 0x00), out _));

    /// <summary>
    /// 旧行为不变：电量位 + 电量值仍按原语义解析。0 是"还没算出来"的占位，
    /// 于是这一帧只剩一个不可信字段 → 整条不算事件（与 M2-3 之前完全一致）。
    /// </summary>
    [Fact]
    public void KeepsLegacyBatterySemantics()
    {
        Assert.False(Mchose0x4DCodec.TryParseStatusEvent(
            Reply(Mchose0x4DCodec.CmdStatusEvent, 0x02, 0x00, 0x00, 0x00), out _));

        Assert.True(Mchose0x4DCodec.TryParseStatusEvent(
            Reply(Mchose0x4DCodec.CmdStatusEvent, 0x02, 0x00, 0x00, 0x64), out var full));
        Assert.Equal(100, full.Percent);
        Assert.Null(full.DpiIndex);
    }
}
