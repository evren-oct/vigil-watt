using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Akg;

/// <summary>
/// 功 能： 经 AKG N9 Hybrid 接收器查询耳机电量。
/// 2026/10/23 21:07:10 30149
/// 充电状态未验证，保留 Unknown；边界见 docs/hardware/akg.md §2。
/// </summary>
public sealed class AkgPlugin : BatteryPluginBase
{
    public override string Name => "Akg";
    public override string DisplayName => "爱科技";
    public override string DisplayNameEn => "AKG";
    public override DeviceKind Kind => DeviceKind.Headset;
    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info)
        => AkgN9Codec.IsSupportedInterface(info.VendorId, info.ProductId, info.DevicePath,
            info.UsagePage, info.Usage, info.InputReportLength, info.OutputReportLength,
            info.FeatureReportLength, info.OutputReportId);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var percent = Query(handle, () => WritesEnabled);
        return percent.HasValue ? ("akg-n9-hybrid", percent, ChargingState.Unknown) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        var percent = Query(dev.Handle, () => CanWrite(dev));
        if (!percent.HasValue)
        {
            if (WritesEnabled) ReportPollFailure(dev);
            return null;
        }
        ReportPollAnswered(dev);
        return (percent, ChargingState.Unknown);
    }

    private int? Query(HidDeviceHandle handle, Func<bool> enabled)
    {
        var read = AkgN9Transport.Query(enabled,
            report => enabled() && IsCandidate(handle.Info)
                && handle.WriteOutput(report, TimeSpan.FromMilliseconds(500)),
            () => handle.GetInputReport(AkgN9Codec.InputReportId), Thread.Sleep,
            Log.IsEnabled(LogLevel.Debug) ? Log.Debug : null);
        if (read is { } result)
        {
            if (Log.IsEnabled(LogLevel.Info))
                Log.InfoOnChange($"akg-battery:{handle.Info.DevicePath}", result.Percent.ToString(),
                    $"AKG N9 Hybrid 电量查询: {result.Percent}% 原始={Log.Hex(result.Message)}");
        }
        else if (enabled()) Log.Warn("AKG N9 Hybrid 电量查询未收到有效结果");
        return read?.Percent;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "AKG N9 Hybrid";
}
