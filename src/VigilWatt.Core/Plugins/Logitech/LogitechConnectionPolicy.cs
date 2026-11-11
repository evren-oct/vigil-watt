using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 接收器槽位连接推断：仅目录确认为 Bolt（当前仅 C548）的有效槽位返回 Inferred Wireless24G，其余一律 Unknown。
/// 2026/11/10 22:10:00 20211
/// </summary>
internal static class LogitechConnectionPolicy
{
    /// <summary>证据必须来自目录记录本身（FindReceiver），不用 MaxSlots() 的默认 6 槽——未知 PID 没有证据，只能 Unknown。</summary>
    public static TelemetryValue<ConnectionKind> For(
        bool directDevice, ReceiverInfo? receiver, byte slotIndex, DateTimeOffset observedAt)
        => !directDevice
            && receiver is { Family: ReceiverFamily.Bolt, MaxSlots: > 0 } r
            && slotIndex >= 1 && slotIndex <= r.MaxSlots
            ? TelemetryValue<ConnectionKind>.Inferred(ConnectionKind.Wireless24G, observedAt)
            : TelemetryValue<ConnectionKind>.Unknown;
}
