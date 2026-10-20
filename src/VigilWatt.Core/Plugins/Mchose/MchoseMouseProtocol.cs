using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从品牌插件内部的 A5 V2 Ultra 电量查询流程。
/// 2026/10/19 21:04:08 kaniu
/// </summary>
internal static class MchoseMouseProtocol
{
    public const string Name = "mchose-mouse-feature11";

    public static bool IsReceiver(HidDeviceInfo info)
        => info.VendorId == MchoseA5Codec.VendorId && info.ProductId == MchoseA5Codec.ProductId;

    public static bool IsCandidate(HidDeviceInfo info)
        => IsReceiver(info) && info.UsagePage == 0xFF01 && info.Usage == 0x0001
           && info.FeatureReportLength >= MchoseA5Codec.PayloadLength + 1;

    public static async Task<(int? Percent, ChargingState Charging)?> QueryAsync(
        HidDeviceHandle handle, bool writesEnabled)
    {
        const int length = MchoseA5Codec.PayloadLength + 1;
        // 同一 collection 含不同长度的 report；report 0x11 不能按最大长度 65 传输。
        if (!writesEnabled || !handle.SetFeature(MchoseA5Codec.ReportId,
                MchoseA5Codec.BuildBatteryQueryPayload(), length))
            return null;
        var deadline = DateTime.UtcNow.AddMilliseconds(600);
        while (DateTime.UtcNow < deadline)
        {
            var notification = await handle.ReadInputAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            if (notification == null || !MchoseA5Codec.IsBatteryNotification(notification)) continue;
            // 官方驱动在通知到达后才 GET；提前读取会拿到尚未填充的命令回显。
            var input = handle.GetFeature(MchoseA5Codec.ReportId, length);
            if (input != null && MchoseA5Codec.TryParse(input, out int percent, out var charging))
                return (percent, charging);
        }
        return null;
    }
}
