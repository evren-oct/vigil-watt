using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 接收器槽位连接推断：非 Direct、目录族属于 2.4G 体系、且槽位在容量内，记 Inferred Wireless24G。
/// 2026/11/11 15:30:00 20211
/// 判据只有"目录族 + 槽位容量"两项，不读 `0xB5` 配对寄存器——那是设备类型/WPID 的事，与链路无关（CR-153）。
/// 2026/11/11 16:20:00 20211 容量改为"接收器自报优先、目录常量兜底"（`0xB5/0x03`，见 CR-156）。
/// </summary>
internal static class LogitechConnectionPolicy
{
    /// <summary>27MHz 不是 2.4G 频段、ConnectionKind 无对应值；Unknown 族没有证据。两者都不推断。</summary>
    private static bool Is24G(ReceiverFamily family)
        => family is ReceiverFamily.Bolt or ReceiverFamily.Unifying
            or ReceiverFamily.Lightspeed or ReceiverFamily.Nano;

    /// <summary>
    /// 槽位容量：接收器自报值优先，读不到（或越界）才用目录常量。
    /// 目录是静态表、可能过时（C518 只登记 1 槽）；自报值是这颗固件的事实，证据更强——Solaar 与 OpenLogi 都按这个优先级。
    /// </summary>
    private static int Capacity(ReceiverInfo receiver, int? reportedSlotCount)
        => reportedSlotCount is > 0 and <= 6 ? reportedSlotCount.Value : receiver.MaxSlots;

    /// <summary>容量必须 > 0：未知 PID 没有目录证据、也没自报值，只能 Unknown。</summary>
    public static TelemetryValue<ConnectionKind> For(
        bool directDevice, ReceiverInfo? receiver, byte slotIndex, DateTimeOffset observedAt,
        int? reportedSlotCount = null)
    {
        if (directDevice || receiver is not { } r) return TelemetryValue<ConnectionKind>.Unknown;

        int capacity = Capacity(r, reportedSlotCount);
        return Is24G(r.Family) && capacity > 0 && slotIndex >= 1 && slotIndex <= capacity
            ? TelemetryValue<ConnectionKind>.Inferred(ConnectionKind.Wireless24G, observedAt)
            : TelemetryValue<ConnectionKind>.Unknown;
    }
}
