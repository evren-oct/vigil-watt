namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： HID++ 指针遥测的纯解析：DPI（0x2201 / 0x2202）与回报率（0x8060 / 0x8061）。
/// 2026/11/10 22:40:00 20211
/// 字节布局依据 OpenLogi 的 openlogi-hidpp feature 实现与 Solaar settings_templates.py，见 docs/hardware/brands/logitech.md §4。
/// </summary>
internal static class HidppPointerCodec
{
    public const ushort FeatureAdjustableDpi = 0x2201;
    public const ushort FeatureExtendedAdjustableDpi = 0x2202;
    public const ushort FeatureReportRate = 0x8060;
    public const ushort FeatureExtendedReportRate = 0x8061;

    /// <summary>0x2201 func 2 = getSensorDpi；0x2202 func 5 = getSensorDpiParameters（长报文）。</summary>
    public const byte FuncGetSensorDpi = 2;

    public const byte FuncGetDpiParameters = 5;

    /// <summary>0x8060 func 1 = getReportRate；0x8061 func 2 = getExtendedReportRate。</summary>
    public const byte FuncGetReportRate = 1;

    public const byte FuncGetExtendedReportRate = 2;

    /// <summary>0x8061 的下标码表（OpenLogi 与 Solaar 一致）：8ms / 4ms / 2ms / 1ms / 500µs / 250µs / 125µs。</summary>
    private static readonly int[] ExtendedReportRateHz = { 125, 250, 500, 1000, 2000, 4000, 8000 };

    /// <summary>0x2201 func 2 响应：[2]=传感器回显，[3..4]=当前 DPI（大端）。回显不校验（Options+ 同样不校验）。</summary>
    public static bool TryParseDpi(ReadOnlySpan<byte> resp, out int dpi)
    {
        dpi = 0;
        if (resp.Length < 5) return false;
        return TryAcceptDpi((resp[3] << 8) | resp[4], out dpi);
    }

    /// <summary>0x2202 func 5 长报文响应：[2]=传感器回显，[3..4]=dpi_x（大端）。只取 X 轴，单传感器设备两轴一致。</summary>
    public static bool TryParseExtendedDpi(ReadOnlySpan<byte> resp, out int dpi)
    {
        dpi = 0;
        if (resp.Length < 5) return false;
        return TryAcceptDpi((resp[3] << 8) | resp[4], out dpi);
    }

    /// <summary>0x8060 func 1 响应：[2]=当前回报间隔（毫秒，1..8）。越界视为无效，不当成速率。</summary>
    public static bool TryParseReportRateMs(ReadOnlySpan<byte> resp, out int milliseconds)
    {
        milliseconds = 0;
        if (resp.Length < 3) return false;
        int ms = resp[2];
        if (ms is < 1 or > 8) return false;
        milliseconds = ms;
        return true;
    }

    /// <summary>0x8061 func 2 响应：[2]=码表下标。下标越界视为无效。</summary>
    public static bool TryParseExtendedReportRateHz(ReadOnlySpan<byte> resp, out int hertz)
    {
        hertz = 0;
        if (resp.Length < 3) return false;
        int index = resp[2];
        if (index < 0 || index >= ExtendedReportRateHz.Length) return false;
        hertz = ExtendedReportRateHz[index];
        return true;
    }

    /// <summary>毫秒间隔 → 设置值（Hz）：8ms=125Hz。越界返回 0，调用方据此保持 Unknown。</summary>
    public static int HertzFromMilliseconds(int milliseconds)
        => milliseconds is >= 1 and <= 8 ? 1000 / milliseconds : 0;

    /// <summary>DPI 合理区间：0 与离谱值（<100 或 >32000）一律丢弃，避免把占位字节当读数。</summary>
    private static bool TryAcceptDpi(int raw, out int dpi)
    {
        dpi = 0;
        if (raw is < 100 or > 32000) return false;
        dpi = raw;
        return true;
    }
}
