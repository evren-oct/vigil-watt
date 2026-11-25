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
        // 连接方式按证据强度合并：设备上报压过推断，反向不成立；没带值不清洗已知链路（轮询应答与墓碑都不带）
        status.Connection = MergeConnection(status.Connection, incoming.Connection);
        // 能力是认领期常量：离线墓碑等来源没有能力，空集不能把已声明的覆盖成 Unverified
        if (!incoming.Capabilities.IsEmpty) status.Capabilities = incoming.Capabilities;
        // 身份同理：轮询应答与离线墓碑都不带身份，null 必须保留已有值，否则详情页身份会被洗空
        status.Usb = UsbIdentity.Merge(status.Usb, incoming.Usb);
        status.Reported = ReportedIdentity.Merge(status.Reported, incoming.Reported);
        status.Receiver = ReceiverIdentity.Merge(status.Receiver, incoming.Receiver);
    }

    /// <summary>
    /// 连接专用合并：Unknown 是"没测到"而不是一条链路，带来源的 Unknown 也先归成空遥测——否则它会压掉已知值。
    /// </summary>
    private static TelemetryValue<ConnectionKind> MergeConnection(
        TelemetryValue<ConnectionKind> stored, TelemetryValue<ConnectionKind> incoming)
    {
        if (incoming.HasValue && incoming.Value.GetValueOrDefault() == ConnectionKind.Unknown)
            incoming = TelemetryValue<ConnectionKind>.Unknown;
        return TelemetryValue<ConnectionKind>.MergePreferTrusted(stored, incoming);
    }
}
