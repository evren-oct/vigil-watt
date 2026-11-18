namespace VigilWatt.Core.Plugins.Razer;

/// <summary>
/// 功 能： 限定已验证的雷蛇鼠标与耳机电量接口。
/// 2026/10/24 19:30:14 Mzood
/// </summary>
internal static class RazerDeviceProfile
{
    public const ushort ViperV3ProSePid = 0x00DF;
    public const ushort ViperV3ProPid = 0x00C1;

    /// <summary>两款 Viper 共用同一套 91 字节 feature 协议，只是 PID 不同。</summary>
    public static bool IsViperWireless(ushort productId) =>
        productId is ViperV3ProSePid or ViperV3ProPid;

    public static bool Matches(ushort vendorId, ushort productId, ushort usagePage,
        ushort usage, ushort featureReportLength, string devicePath) =>
        vendorId == 0x1532 && IsViperWireless(productId)
        && usagePage == 0x0001 && usage == 0x0002
        && featureReportLength == RazerViperSeCodec.ReportLength
        && devicePath.Contains("&mi_00#", StringComparison.OrdinalIgnoreCase);
    public static bool MatchesHeadset(ushort vendorId, ushort productId, ushort usagePage,
        ushort usage, ushort inputLength, ushort outputLength, byte? reportId, string devicePath) =>
        vendorId == 0x1532 && productId == 0x0555 && usagePage == 0xFF00 && usage == 1
        && inputLength == RazerHeadsetCodec.ReportLength && outputLength == RazerHeadsetCodec.ReportLength
        && reportId == RazerHeadsetCodec.ReportId
        && devicePath.Contains("&mi_03&col02#", StringComparison.OrdinalIgnoreCase);
}
