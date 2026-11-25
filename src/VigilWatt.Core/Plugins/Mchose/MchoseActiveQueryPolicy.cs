using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从主动查询的目录白名单——只有目录声明 batteryRead=supported 的型号/通道才允许写帧。
/// 2026/11/24 22:30:00 20211
/// 未登记或未验证的接口只能被动监听，见 docs/hardware/safety.md §3 与 docs/architecture/active-query-safety.md。
/// </summary>
internal static class MchoseActiveQueryPolicy
{
    /// <summary>协议名 + 接口形态 + 目录能力三者同时命中才放行；共享指纹的 3554:FA09 走设备目录能力。</summary>
    public static bool Allows(HidDeviceInfo info, string protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol)) return false;

        // 传输通道：PID 只说明“是哪颗 dongle”，还要确认当前接口形态属于该协议。
        if (MchoseDeviceCatalog.TryGetTransport(info.VendorId, info.ProductId, out var transport)
            && transport.Protocol == protocol
            && transport.Capabilities.Of(DeviceCapability.BatteryRead) == CapabilityState.Supported
            && MchoseInterfaceClassifier.KindOfProtocol(protocol) == MchoseInterfaceClassifier.Classify(
                info.VendorId, info.ProductId, info.DevicePath, info.UsagePage, info.Usage,
                info.InputReportLength, info.OutputReportLength, info.FeatureReportLength, info.OutputReportId))
            return true;

        // 本体目录：3554:FA09 与 AULA 共用指纹，分类器故意不认它，故这里按目录身份放行。
        return MchoseDeviceCatalog.TryGet(info.VendorId, info.ProductId, out var device)
               && device.Protocol == protocol
               && device.Capabilities.Of(DeviceCapability.BatteryRead) == CapabilityState.Supported;
    }
}
