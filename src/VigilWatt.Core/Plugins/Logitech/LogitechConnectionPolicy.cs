using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 接收器槽位连接推断：仅有效槽位返回 Inferred Wireless24G，Direct/无效槽位保持 Unknown。
/// 2026/11/10 22:10:00 20211
/// </summary>
internal static class LogitechConnectionPolicy
{
    /// <summary>AddDevice 只在 Ping 成功后调用；这里只判断路径与槽位范围，不把推断升级成设备上报。</summary>
    public static TelemetryValue<ConnectionKind> For(
        bool directDevice, byte slotIndex, int maxSlots, DateTimeOffset observedAt)
        => !directDevice && maxSlots > 0 && slotIndex >= 1 && slotIndex <= maxSlots
            ? TelemetryValue<ConnectionKind>.Inferred(ConnectionKind.Wireless24G, observedAt)
            : TelemetryValue<ConnectionKind>.Unknown;
}
