using VigilWatt.Core.Models;

namespace VigilWatt.Core.Devices;

/// <summary>
/// 功 能： 设备变化判定与骤降保护（纯逻辑，可单测）。
/// 2026/10/10 17:44:33 xliu
/// DeviceManager.Upsert 靠它在"存快照"与"原地合并"之间做决策，详见 docs/project/architecture.md §6。
/// </summary>
public static class DeviceChangeDetector
{
    /// <summary>电量 / 充电 / 在线三项任一变化即算变化。充电标志必须参与：拔线瞬间电量常停在原值（见 docs/project/changelog.md 2026-10-06）。</summary>
    public static bool HasChanged(DeviceStatus stored, DeviceStatus incoming) =>
        stored.BatteryPercent != incoming.BatteryPercent
        || stored.Charging != incoming.Charging
        || stored.IsOnline != incoming.IsOnline;

    /// <summary>Pointer 是否带来新值（只比 Value + Source，不看 ObservedAt）。**不能并进 HasChanged**：那会让 DPI 变化写出一条电量采样。</summary>
    public static bool HasPointerChanged(DeviceStatus stored, DeviceStatus incoming)
        => PointerTelemetry.ValueDiffers(stored.Pointer, incoming.Pointer);

    /// <summary>
    /// 连接方式是否变了。**同样不并进 HasChanged**：插拔线不该产生电量采样，只该刷新徽标；Unknown 不算值，来源与时刻也不参与。
    /// </summary>
    public static bool HasConnectionChanged(DeviceStatus stored, DeviceStatus incoming)
        => incoming.Connection.HasValue
           && incoming.Connection.Value.GetValueOrDefault() != ConnectionKind.Unknown
           && stored.Connection.Value != incoming.Connection.Value;

    /// <summary>降幅超过 <paramref name="maxDropPoints"/> 视为不可信（休眠/关机的伪读数），先不提醒、等下次读数确认。</summary>
    public static bool IsImplausibleDrop(int? previousPercent, int currentPercent, int maxDropPoints) =>
        previousPercent is { } prev && prev - currentPercent > maxDropPoints;
}
