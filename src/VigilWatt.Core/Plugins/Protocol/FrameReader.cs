using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Protocol;

/// <summary>
/// 功 能： 按 <see cref="FrameSpec"/> 从一帧里取字段，纯函数不碰句柄。
/// 2026/11/01 23:20:00 xliu
/// 只回答"这帧里有什么值"，不判断该不该采信；判据与去抖在 <see cref="ProtocolEngine"/>。
/// </summary>
internal static class FrameReader
{
    /// <summary>解析一帧；结构不匹配返回 null，电量越界时 <see cref="FrameReading.Percent"/> 为 null。</summary>
    public static FrameReading? Read(FrameSpec spec, byte[] frame)
    {
        if (spec.Length is { } length && !length.Matches(frame.Length)) return null;

        var data = frame;
        if (spec.Xor is { } xor)
        {
            data = new byte[frame.Length];
            for (int i = 0; i < frame.Length; i++) data[i] = (byte)(frame[i] ^ xor);
        }

        foreach (var off in spec.TryOffsets)
        {
            if (off < 0 || off >= data.Length) continue;
            if (spec.ReportId is { Length: > 0 } ids && Array.IndexOf(ids, (int)data[off]) < 0) continue;
            if (!PrefixMatches(spec, data, off)) continue;
            return ReadFields(spec, data, off);
        }
        return null;
    }

    private static bool PrefixMatches(FrameSpec spec, byte[] data, int off)
    {
        foreach (var (offset, value) in spec.Prefix)
        {
            if (off + offset >= data.Length || data[off + offset] != (byte)value) return false;
        }
        return true;
    }

    private static FrameReading ReadFields(FrameSpec spec, byte[] data, int off)
    {
        int? percent = null;
        if (spec.Percent is { } p && TryRead(data, off + p.Offset, p.Type, out var rawPercent)
            && rawPercent >= spec.MinPercent && rawPercent <= spec.MaxPercent)
            percent = rawPercent;

        var charging = ReadCharging(spec.Charge, data, off, percent);
        if (spec.FullAt is { } fullAt && percent >= fullAt) charging = ChargingState.Full;
        var mode = ReadMode(spec.Mode, data, off);
        var link = ReadLink(spec.Link, data, off, mode);
        return new FrameReading(percent, charging, link, mode);
    }

    private static ChargingState ReadCharging(ChargeSpec? spec, byte[] data, int off, int? percent)
    {
        if (spec is null) return ChargingState.Unknown;
        // 电量越界说明这帧本身可疑，充电位不能单独采信
        if (percent is null) return ChargingState.Unknown;
        if (!TryRead(data, off + spec.Offset, spec.Type, out var raw)) return ChargingState.Unknown;
        if (spec.NonZero) return raw != 0 ? ChargingState.Charging : ChargingState.Discharging;
        if (spec.Zero) return raw == 0 ? ChargingState.Charging : ChargingState.Discharging;
        if (spec.Table is { } table && table.TryGetValue(raw, out var mapped)) return mapped;
        return spec.OnMiss;
    }

    private static LinkState ReadLink(LinkSpec? spec, byte[] data, int off, LinkMode mode)
    {
        if (spec is null) return LinkState.Unknown;
        // 该链路位只在指定连接方式下有效（有线时没有 RF 链路，bit 恒 0 属正常）
        if (spec.OnlyWhenMode is { } required && mode != required) return LinkState.Unknown;
        if (off + spec.Offset >= data.Length) return LinkState.Unknown;
        var set = (data[off + spec.Offset] >> spec.Bit & 1) != 0;
        return set == spec.ConnectedWhenSet ? LinkState.Up : LinkState.Down;
    }

    private static LinkMode ReadMode(ModeSpec? spec, byte[] data, int off)
    {
        if (spec is null) return LinkMode.Unknown;
        if (off + spec.Offset >= data.Length) return LinkMode.Unknown;
        var raw = data[off + spec.Offset] & spec.Mask;
        return spec.Table is { } table && table.TryGetValue(raw, out var mode) ? mode : LinkMode.Unknown;
    }

    private static bool TryRead(byte[] data, int offset, FieldType type, out int value)
    {
        value = 0;
        int width = type switch
        {
            FieldType.U16 => 2,
            FieldType.U24 => 3,
            FieldType.U32 => 4,
            _ => 1,
        };
        if (offset < 0 || offset + width > data.Length) return false;

        switch (type)
        {
            case FieldType.U16:
                value = data[offset] | (data[offset + 1] << 8);
                return true;
            case FieldType.U24:
                value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
                return true;
            case FieldType.U32:
                value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
                return true;
            case FieldType.Bit4:
                value = data[offset] & 0x0F;
                return true;
            case FieldType.Bit:
                value = data[offset] & 0x01;
                return true;
            default:
                value = data[offset];
                return true;
        }
    }
}
