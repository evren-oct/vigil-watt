using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>身份解析到哪一步。<see cref="Unknown"/> 是默认值：没有协议自报身份，不代表设备没有型号。</summary>
public enum MchoseIdentityStatus
{
    /// <summary>没拿到协议自报身份（设备在睡、未查询、应答太短）。</summary>
    Unknown = 0,

    /// <summary>协议自报了 VID/PID，但目录里没有这个型号。</summary>
    ReportedUnresolved,

    /// <summary>协议自报的 VID/PID 命中了目录型号。</summary>
    ReportedResolved,
}

/// <summary>
/// 功 能： 一台迈从设备的身份快照——USB 枚举身份与协议自报身份分开存，互不覆盖。
/// 2026/11/08 19:50:00 xliu
/// 0x0900 的 [0..3] 是配对**本体**的 VID/PID（不是 dongle），见 docs/hardware/mchose.md §3.3。
/// </summary>
public readonly record struct MchoseDeviceIdentity(
    ushort UsbVendorId,
    ushort UsbProductId,
    ushort? ReportedVendorId,
    ushort? ReportedProductId,
    string Model,
    MchoseIdentityStatus Status)
{
    /// <summary>传输通道 PID = USB 枚举到的那颗 dongle/本体。**不是型号标识**：0x1018 被多款共用。</summary>
    public ushort TransportPid => UsbProductId;

    /// <summary>拿到过协议自报身份。</summary>
    public bool HasReported => ReportedVendorId.HasValue && ReportedProductId.HasValue;

    /// <summary>只有 USB 身份时的快照：型号留空，状态 <see cref="MchoseIdentityStatus.Unknown"/>。</summary>
    public static MchoseDeviceIdentity FromUsb(HidDeviceInfo info)
        => new(info.VendorId, info.ProductId, null, null, "", MchoseIdentityStatus.Unknown);

    /// <summary>
    /// 补上协议自报身份。<paramref name="model"/> 为空表示目录没收录——**不猜型号**，
    /// 只把状态降级为 <see cref="MchoseIdentityStatus.ReportedUnresolved"/>。
    /// </summary>
    public MchoseDeviceIdentity WithReported(ushort vendorId, ushort productId, string? model)
        => this with
        {
            ReportedVendorId = vendorId,
            ReportedProductId = productId,
            Model = model ?? "",
            Status = string.IsNullOrEmpty(model)
                ? MchoseIdentityStatus.ReportedUnresolved
                : MchoseIdentityStatus.ReportedResolved,
        };

    /// <summary>转成统一快照；没拿到自报身份时返回 null——**不许拿 USB 身份冒充自报身份**。</summary>
    public ReportedIdentity? ToReportedIdentity()
        => HasReported
            ? new ReportedIdentity(ReportedVendorId!.Value, ReportedProductId!.Value,
                string.IsNullOrWhiteSpace(Model) ? null : Model)
            : null;
}
