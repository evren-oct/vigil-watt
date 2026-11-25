using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： HidppCodec —— HID++ 响应解析与 report ID 选择。
/// 2026/10/08 11:27:03 xliu
/// 覆盖 Bolt 长接口必须发 0x11， 以及电量/feature 不得读成 feature 下标的回归。
/// </summary>
public class HidppCodecTests
{
    [Fact]
    public void R1_二十字节接口选出长报文0x11()
    {
        Assert.Equal(0x11, HidppCodec.SelectReportId(20, null));
        Assert.Equal(0x11, HidppCodec.SelectReportId(20, 0)); // 0 = 接口不用 ID，仍按长度推断
        Assert.Equal(0x11, HidppCodec.SelectReportId(20, 0x11));
        Assert.True(HidppCodec.IsLongMessage(0x11));
    }

    [Fact]
    public void R2_七字节接口选出短报文0x10()
    {
        Assert.Equal(0x10, HidppCodec.SelectReportId(7, null));
        Assert.Equal(0x10, HidppCodec.SelectReportId(7, 0x10));
        Assert.False(HidppCodec.IsLongMessage(0x10));
    }

    [Fact]
    public void R3_caps给出的reportId优先于长度推断()
    {
        // 异常固件：输出长度 20 但 caps 写明 0x10 —— 仍以 caps 为准（驱动只认描述符里的 ID）
        Assert.Equal(0x10, HidppCodec.SelectReportId(20, 0x10));
    }

    [Fact]
    public void F1_GetFeature下标在参数字节resp2()
    {
        // 真机形态：feature=0x00, func|sw=0x0B, index=0x07（Unified Battery 常见下标）
        byte[] resp = { 0x00, 0x0B, 0x07, 0x00, 0x00 };
        Assert.Equal(0x07, HidppCodec.ParseFeatureIndex(resp));
        // 旧 bug：读 resp[0] 会得到 0 → 永远判"不支持"
        Assert.NotEqual(0, HidppCodec.ParseFeatureIndex(resp));
    }

    [Fact]
    public void F2_GetFeature不支持时参数为0()
    {
        byte[] resp = { 0x00, 0x0B, 0x00, 0x00, 0x00 };
        Assert.Equal(0, HidppCodec.ParseFeatureIndex(resp));
        Assert.Equal(0, HidppCodec.ParseFeatureIndex(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void B1_UnifiedBattery百分比在resp2不是feature下标()
    {
        // featureIdx=0x07, func|sw, percent=67, reserved, charging=0(放电)
        byte[] resp = { 0x07, 0x0B, 67, 0x00, 0x00 };
        Assert.True(HidppCodec.TryParseUnifiedBattery(resp, out var pct, out var chg));
        Assert.Equal(67, pct);
        Assert.Equal(ChargingState.Discharging, chg);
        // 旧 bug：会把 0x07 当成 7%
        Assert.NotEqual(resp[0], pct);
    }

    [Fact]
    public void B2a_0x1004状态码_2慢充3满4故障()
    {
        Assert.True(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0, 80, 0, 1 }, out _, out var c1));
        Assert.Equal(ChargingState.Charging, c1);
        // 2 = 慢充：UI 只有四档，并到充电中
        Assert.True(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0, 99, 0, 2 }, out _, out var c2));
        Assert.Equal(ChargingState.Charging, c2);
        Assert.True(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0, 100, 0, 3 }, out var p3, out var c3));
        Assert.Equal(100, p3);
        Assert.Equal(ChargingState.Full, c3);
        // 4 = 充电故障：绝不能报"充电中"
        Assert.True(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0, 50, 0, 4 }, out _, out var c4));
        Assert.Equal(ChargingState.Unknown, c4);
        // 0 = 放电；未知码也归 Unknown，不冒充放电
        Assert.True(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0, 50, 0, 0 }, out _, out var c0));
        Assert.Equal(ChargingState.Discharging, c0);
        Assert.True(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0, 50, 0, 9 }, out _, out var c9));
        Assert.Equal(ChargingState.Unknown, c9);
    }

    [Fact]
    public void B2b_0x1000状态码_2几乎满4慢充5以上错误()
    {
        Assert.True(HidppCodec.TryParseBatteryStatus(new byte[] { 1, 0, 80, 0, 1 }, out _, out var c1));
        Assert.Equal(ChargingState.Charging, c1);
        // 2 = 几乎满、4 = 慢充：都并到充电中
        Assert.True(HidppCodec.TryParseBatteryStatus(new byte[] { 1, 0, 99, 0, 2 }, out _, out var c2));
        Assert.Equal(ChargingState.Charging, c2);
        Assert.True(HidppCodec.TryParseBatteryStatus(new byte[] { 1, 0, 50, 0, 4 }, out _, out var c4));
        Assert.Equal(ChargingState.Charging, c4);
        Assert.True(HidppCodec.TryParseBatteryStatus(new byte[] { 1, 0, 100, 0, 3 }, out _, out var c3));
        Assert.Equal(ChargingState.Full, c3);
        // 5 无效电池 / 6 过热 / 7 其它充电错误：既不是充电也不是放电
        foreach (byte code in new byte[] { 5, 6, 7 })
        {
            Assert.True(HidppCodec.TryParseBatteryStatus(new byte[] { 1, 0, 50, 0, code }, out _, out var c));
            Assert.Equal(ChargingState.Unknown, c);
        }
    }

    [Fact]
    public void B3_零电量与越界判无效()
    {
        Assert.False(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0, 0, 0, 0 }, out _, out _));
        Assert.False(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0, 101, 0, 0 }, out _, out _));
        Assert.False(HidppCodec.TryParseUnifiedBattery(new byte[] { 1, 0 }, out _, out _));
        Assert.False(HidppCodec.TryParseBatteryStatus(new byte[] { 1, 0, 0, 0, 0 }, out _, out _));
        Assert.False(HidppCodec.TryParseBatteryStatus(new byte[] { 1, 0, 101, 0, 0 }, out _, out _));
        Assert.False(HidppCodec.TryParseBatteryStatus(new byte[] { 1, 0 }, out _, out _));
    }

    [Fact]
    public void P1_Feature探测_首次无应答不写死不支持()
    {
        int misses = 0;
        // 解析成功：立刻采信并清零计数
        Assert.Equal((byte)0x07, HidppFeatureProbe.Next(null, 0x07, ref misses));
        Assert.Equal(0, misses);
        // 解析回 0（超时或错误帧）：先留 null，下轮再试
        Assert.Null(HidppFeatureProbe.Next(null, 0, ref misses));
        Assert.Equal(1, misses);
        Assert.Null(HidppFeatureProbe.Next(null, 0, ref misses));
        Assert.Equal(2, misses);
        // 连续第 3 次才认定不支持
        Assert.Equal((byte)0, HidppFeatureProbe.Next(null, 0, ref misses));
        Assert.Equal(3, misses);
    }

    [Fact]
    public void P2_Feature探测_已缓存的值不再计数()
    {
        int misses = 0;
        Assert.Equal((byte)0x0C, HidppFeatureProbe.Next(0x0C, 0, ref misses));
        Assert.Equal(0, misses);
        Assert.Equal((byte)0, HidppFeatureProbe.Next(0, 0x0C, ref misses));
        Assert.Equal(0, misses);
    }

    [Fact]
    public void S1_同接收器有长接口时跳过短接口()
    {
        var peers = new[] { (Vid: (ushort)0x046D, Pid: (ushort)0xC548, OutLen: (ushort)20) };
        Assert.True(HidppCodec.ShouldSkipShortInterface(0x046D, 0xC548, 7, peers));
        Assert.False(HidppCodec.ShouldSkipShortInterface(0x046D, 0xC548, 20, peers));
        Assert.False(HidppCodec.ShouldSkipShortInterface(0x046D, 0xC52B, 7, peers)); // 别的 PID
    }

    [Fact]
    public void S2_PreferLong跳短_PreferShort跳长_Both不跳()
    {
        var boltPeers = new[] { (Vid: (ushort)0x046D, Pid: (ushort)0xC548, OutLen: (ushort)20) };
        Assert.True(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC548, 7, boltPeers, HidppReportPreference.PreferLong));
        Assert.False(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC548, 7, boltPeers, HidppReportPreference.Both));

        var unifyingPeers = new[] { (Vid: (ushort)0x046D, Pid: (ushort)0xC52B, OutLen: (ushort)7) };
        Assert.True(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC52B, 20, unifyingPeers, HidppReportPreference.PreferShort));
        Assert.False(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC52B, 7, unifyingPeers, HidppReportPreference.PreferShort));
        // 未知 PID 的旧默认 = PreferLong：无长同伴时不跳短
        Assert.False(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC52B, 7, unifyingPeers, HidppReportPreference.PreferLong));
    }

    [Fact]
    public void R4_FeatureFunc把func放在高半字节()
    {
        // patchSw 前：func 1 必须是 0x..10，不能是 0x..01（后者会被抹成 func 0）
        Assert.Equal(0x0800, HidppCodec.FeatureFunc(8, 0));
        Assert.Equal(0x0810, HidppCodec.FeatureFunc(8, 1));
        Assert.NotEqual(0x0801, HidppCodec.FeatureFunc(8, 1));
    }

    [Fact]
    public void S3_三十二字节扩展集合在有二十字节同伴时跳过()
    {
        // C53F 真机：7 / 20 / 32 三个集合同时在场，只有 20 字节那条应答 HID++
        var peers = new[]
        {
            (Vid: (ushort)0x046D, Pid: (ushort)0xC53F, OutLen: (ushort)7),
            (Vid: (ushort)0x046D, Pid: (ushort)0xC53F, OutLen: (ushort)20),
            (Vid: (ushort)0x046D, Pid: (ushort)0xC53F, OutLen: (ushort)32),
        };
        Assert.True(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC53F, 7, peers, HidppReportPreference.PreferLong));
        Assert.False(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC53F, 20, peers, HidppReportPreference.PreferLong));
        Assert.True(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC53F, 32, peers, HidppReportPreference.PreferLong));

        // 只有扩展集合的接收器仍要认领（没有 20 字节同伴可退）
        var extendedOnly = new[] { (Vid: (ushort)0x046D, Pid: (ushort)0xC53F, OutLen: (ushort)32) };
        Assert.False(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC53F, 32, extendedOnly, HidppReportPreference.PreferLong));
        // 别的 PID 的 20 字节接口不能顶替
        var otherPid = new[] { (Vid: (ushort)0x046D, Pid: (ushort)0xC548, OutLen: (ushort)20) };
        Assert.False(HidppCodec.ShouldSkipInterface(
            0x046D, 0xC53F, 32, otherPid, HidppReportPreference.PreferLong));
    }

    [Fact]
    public void N1_设备名长度不再被32字符上限挡死()
    {
        // G304 真机自报 37 字符；旧上限 32 会把整条名字丢掉
        Assert.True(HidppCodec.TryParseDeviceNameLength(new byte[] { 0x05, 0x0B, 37 }, out int len));
        Assert.Equal(37, len);
        Assert.True(HidppCodec.TryParseDeviceNameLength(
            new byte[] { 0x05, 0x0B, HidppCodec.MaxDeviceNameLength }, out _));

        Assert.False(HidppCodec.TryParseDeviceNameLength(new byte[] { 0x05, 0x0B, 0 }, out _));
        Assert.False(HidppCodec.TryParseDeviceNameLength(
            new byte[] { 0x05, 0x0B, HidppCodec.MaxDeviceNameLength + 1 }, out _));
        Assert.False(HidppCodec.TryParseDeviceNameLength(new byte[] { 0x05, 0x0B }, out _));
        Assert.False(HidppCodec.TryParseDeviceNameLength(ReadOnlySpan<byte>.Empty, out _));
    }

    [Fact]
    public void N2_设备名按块读且末块按剩余长度截断()
    {
        // 长报文一帧 16 字符
        var longChunk = new byte[] { 0x05, 0x1B }.Concat("G304 Lightspeed"u8.ToArray()).ToArray();
        Assert.True(HidppCodec.TryParseDeviceNameChunk(longChunk, 16, out var chunk));
        Assert.Equal("G304 Lightspeed", chunk);

        // 末块只取剩余字符数，不能把报文尾部的 0 填充一起拼进名字
        var tail = new byte[] { 0x05, 0x1B }.Concat("Mouse\0\0\0"u8.ToArray()).ToArray();
        Assert.True(HidppCodec.TryParseDeviceNameChunk(tail, 5, out var last));
        Assert.Equal("Mouse", last);

        // 短报文一帧 3 字符
        var shortChunk = new byte[] { 0x05, 0x1B, (byte)'G', (byte)'3', (byte)'0' };
        Assert.True(HidppCodec.TryParseDeviceNameChunk(shortChunk, 3, out var s));
        Assert.Equal("G30", s);

        Assert.False(HidppCodec.TryParseDeviceNameChunk(new byte[] { 0x05, 0x1B }, 3, out _));
        Assert.False(HidppCodec.TryParseDeviceNameChunk(ReadOnlySpan<byte>.Empty, 3, out _));
    }

    private static readonly VoltagePoint[] LithiumCurve =
    {
        new(4186, 100), new(4067, 90), new(3989, 80), new(3922, 70),
        new(3859, 60), new(3811, 50), new(3778, 40), new(3751, 30),
        new(3717, 20), new(3671, 10), new(3646, 5), new(3579, 2), new(3500, 0),
    };

    [Fact]
    public void V1_毫伏插值到百分比()
    {
        Assert.Equal(100, HidppCodec.PercentFromMillivolts(4200, LithiumCurve));
        Assert.Equal(0, HidppCodec.PercentFromMillivolts(3400, LithiumCurve));
        Assert.Equal(50, HidppCodec.PercentFromMillivolts(3811, LithiumCurve));
        // 4067(90) 与 4186(100) 中点偏上 → 约 95
        int mid = HidppCodec.PercentFromMillivolts(4126, LithiumCurve);
        Assert.InRange(mid, 94, 96);
    }

    [Fact]
    public void V2_BatteryVoltage解析充电与毫伏()
    {
        // feature, func|sw, mv_hi, mv_lo, flags(bit7=charging, low3=1 → full)
        byte[] full = { 0x0A, 0x0B, 0x10, 0x5A, 0x81 }; // 4186 mV, charging+full
        Assert.True(HidppCodec.TryParseBatteryVoltage(full, LithiumCurve, out var pct, out var chg, out var mv));
        Assert.Equal(4186, mv);
        Assert.Equal(100, pct);
        Assert.Equal(ChargingState.Full, chg);

        byte[] disc = { 0x0A, 0x0B, 0x0E, 0xE3, 0x00 }; // 3811 mV, discharging
        Assert.True(HidppCodec.TryParseBatteryVoltage(disc, LithiumCurve, out pct, out chg, out mv));
        Assert.Equal(3811, mv);
        Assert.Equal(50, pct);
        Assert.Equal(ChargingState.Discharging, chg);
    }

    [Fact]
    public void V3_AdcMeasurement状态字节()
    {
        byte[] charging = { 0x0B, 0x0B, 0x0F, 0xBE, 0x03 }; // 4030 mV, 0x03=charging
        Assert.True(HidppCodec.TryParseAdcMeasurement(charging, LithiumCurve, out var pct, out var chg, out var mv));
        Assert.Equal(4030, mv);
        Assert.Equal(ChargingState.Charging, chg);
        Assert.True(pct > 0);

        byte[] full = { 0x0B, 0x0B, 0x0F, 0xBE, 0x07 };
        Assert.True(HidppCodec.TryParseAdcMeasurement(full, LithiumCurve, out _, out chg, out _));
        Assert.Equal(ChargingState.Full, chg);
    }

    [Fact]
    public void K1_配对设备类型字节映射()
    {
        Assert.Equal(DeviceKind.Keyboard, HidppCodec.MapPairingDeviceKind(0x01));
        Assert.Equal(DeviceKind.Mouse, HidppCodec.MapPairingDeviceKind(0x02));
        Assert.Equal(DeviceKind.Keyboard, HidppCodec.MapPairingDeviceKind(0x03)); // numpad
        Assert.Equal(DeviceKind.Mouse, HidppCodec.MapPairingDeviceKind(0x08)); // trackball
        Assert.Equal(DeviceKind.Gamepad, HidppCodec.MapPairingDeviceKind(0x0B));
        Assert.Equal(DeviceKind.Headset, HidppCodec.MapPairingDeviceKind(0x0D));
        Assert.Equal(DeviceKind.Unknown, HidppCodec.MapPairingDeviceKind(0x00));
        Assert.Equal(DeviceKind.Unknown, HidppCodec.MapPairingDeviceKind(0x04)); // presenter
    }

    [Fact]
    public void K2_Unifying与Bolt配对信息布局()
    {
        // Unifying：params[3..4]=WPID BE, params[7]=kind → resp[5..6], resp[9]
        byte[] uni = { 0x83, 0xB5, 0x20, 0x00, 0x00, 0x40, 0x80, 0x00, 0x00, 0x02 };
        Assert.True(HidppCodec.TryParseUnifyingPairing(uni, out var wpid, out var kind));
        Assert.Equal(0x4080, wpid);
        Assert.Equal(DeviceKind.Mouse, kind);

        // Bolt：params[1]=kind, params[2..3]=WPID LE → resp[3], resp[4..5]
        byte[] bolt = { 0x83, 0xB5, 0x51, 0x02, 0x80, 0x40 };
        Assert.True(HidppCodec.TryParseBoltPairing(bolt, out wpid, out kind));
        Assert.Equal(0x4080, wpid);
        Assert.Equal(DeviceKind.Mouse, kind);
    }

    /// <summary>0xB5/0x03 接收器信息：params[6] = 配对槽位容量 → resp[8]；1..6 之外视为"没读到"。</summary>
    [Fact]
    public void K4_接收器自报槽位容量()
    {
        // [0x83, 0xB5, sub, serial(4), ?, slots, ...]
        byte[] six = { 0x83, 0xB5, 0x03, 0x12, 0x34, 0x56, 0x78, 0x00, 0x06, 0x00 };
        Assert.True(HidppCodec.TryParseReceiverSlotCount(six, out var slots));
        Assert.Equal(6, slots);

        byte[] one = { 0x83, 0xB5, 0x03, 0, 0, 0, 0, 0, 0x01, 0 };
        Assert.True(HidppCodec.TryParseReceiverSlotCount(one, out slots));
        Assert.Equal(1, slots);

        // 越界：0 与 7 都不是合法容量（Unifying / Bolt 上限 6），交给目录兜底
        Assert.False(HidppCodec.TryParseReceiverSlotCount(
            new byte[] { 0x83, 0xB5, 0x03, 0, 0, 0, 0, 0, 0x00, 0 }, out _));
        Assert.False(HidppCodec.TryParseReceiverSlotCount(
            new byte[] { 0x83, 0xB5, 0x03, 0, 0, 0, 0, 0, 0x07, 0 }, out _));

        // 子寄存器回显不是 0x03：偏移与固件不符时宁可读不到，也不读出一位错位的容量
        Assert.False(HidppCodec.TryParseReceiverSlotCount(
            new byte[] { 0x83, 0xB5, 0x05, 0, 0, 0, 0, 0, 0x06, 0 }, out _));

        // 短缓冲
        Assert.False(HidppCodec.TryParseReceiverSlotCount(
            new byte[] { 0x83, 0xB5, 0x03, 0, 0, 0, 0, 0 }, out _));
    }

    /// <summary>0x0005 func 2 的营销类型枚举（键盘 0 / 小键盘 2 / 鼠标 3 / 触控板 4 / 轨迹球 5 / 耳机 8 / 手柄 12 / 音箱 14）。</summary>
    [Fact]
    public void K3_设备营销类型映射()
    {
        Assert.Equal(DeviceKind.Keyboard, HidppCodec.MapDeviceTypeKind(0));
        Assert.Equal(DeviceKind.Keyboard, HidppCodec.MapDeviceTypeKind(2));   // numpad
        Assert.Equal(DeviceKind.Mouse, HidppCodec.MapDeviceTypeKind(3));
        Assert.Equal(DeviceKind.Mouse, HidppCodec.MapDeviceTypeKind(5));      // trackball
        Assert.Equal(DeviceKind.Headset, HidppCodec.MapDeviceTypeKind(8));
        Assert.Equal(DeviceKind.Gamepad, HidppCodec.MapDeviceTypeKind(12));
        Assert.Equal(DeviceKind.Speaker, HidppCodec.MapDeviceTypeKind(14));

        // 没有对应 Kind 的类型回 Unknown，交给配对寄存器 / 名字兜底
        Assert.Equal(DeviceKind.Unknown, HidppCodec.MapDeviceTypeKind(4));    // trackpad
        Assert.Equal(DeviceKind.Unknown, HidppCodec.MapDeviceTypeKind(7));    // receiver
        Assert.Equal(DeviceKind.Unknown, HidppCodec.MapDeviceTypeKind(9));    // webcam
        Assert.Equal(DeviceKind.Unknown, HidppCodec.MapDeviceTypeKind(0xFF));
    }
}
