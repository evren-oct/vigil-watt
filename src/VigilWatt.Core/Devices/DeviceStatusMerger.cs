using VigilWatt.Core.Models;

namespace VigilWatt.Core.Devices;

/// <summary>
/// 功 能： DeviceStatus 的原地合并规则（纯逻辑，可单测；DeviceManager.Upsert 与"墓碑接管"都走这里）。
/// 2026/10/06 22:54:19 xliu
/// 详见 docs/project/architecture.md §6。
/// </summary>
public static class DeviceStatusMerger
{
    /// <summary>
    /// 把 <paramref name="incoming"/> 的字段同步进已存在的 <paramref name="status"/>（原地改）。⚠️ Kind / Protocol 是"声明覆盖猜测"（非 Unknown / 非空才覆盖）：既防墓碑的错误猜测冻结到永远，也防未声明来源洗掉已知类型——详见 docs/project/architecture.md §6。
    /// </summary>
    public static void Apply(DeviceStatus status, DeviceStatus incoming)
    {
        status.Name = incoming.Name;
        if (incoming.Kind != DeviceKind.Unknown) status.Kind = incoming.Kind;
        status.BatteryPercent = incoming.BatteryPercent;
        status.Charging = incoming.Charging;
        if (incoming.Protocol.Length > 0) status.Protocol = incoming.Protocol;
        status.LastUpdated = incoming.LastUpdated;
        status.IsOnline = incoming.IsOnline;
        // 电量是无条件覆盖（既有语义）；Pointer 相反：incoming 没带值就不覆盖，否则轮询会抹掉被动帧读到的值
        status.Pointer = PointerTelemetry.Merge(status.Pointer, incoming.Pointer);
    }
}
