using System.Diagnostics;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 V9 Pro 耳机 2.4G 电量查询插件。
/// 2026/10/19 00:32:00 路泽
/// 协议及验证边界见 docs/hardware/mchose-v9-pro.md。
/// </summary>
public sealed class MchoseV9ProPlugin : BatteryPluginBase
{
    public override string Name => "MchoseV9Pro";
    public override string DisplayName => "MCHOSE V9 Pro 2.4G 耳机";
    public override DeviceKind Kind => DeviceKind.Headset;
    protected override int MinValidPercent => 1;
    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info)
        => MchoseV9ProCodec.IsSupportedInterface(info.VendorId, info.ProductId, info.DevicePath,
            info.UsagePage, info.Usage, info.InputReportLength, info.OutputReportLength,
            info.FeatureReportLength, info.OutputReportId);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var percent = await QueryAsync(handle).ConfigureAwait(false);
        return percent.HasValue ? ("mchose-v9-pro-5565", percent, ChargingState.Unknown) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        var percent = QueryAsync(dev.Handle).GetAwaiter().GetResult();
        if (!percent.HasValue)
        {
            ReportPollFailure(dev);
            return null;
        }
        ReportPollAnswered(dev);
        return (percent, ChargingState.Unknown);
    }

    private async Task<int?> QueryAsync(HidDeviceHandle handle)
    {
        // 与键盘插件分开授权；写点再次验证完整接口，不能向其它音频或 HID 集合发帧。
        if (!WritesEnabled || !IsCandidate(handle.Info)) return null;
        if (!handle.WriteOutput(MchoseV9ProCodec.CreateQuery(), TimeSpan.FromMilliseconds(500)))
            return null;
        var elapsed = Stopwatch.StartNew();
        var window = TimeSpan.FromMilliseconds(750);
        while (elapsed.Elapsed < window)
        {
            var remaining = window - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var input = await handle.ReadInputAsync(remaining).ConfigureAwait(false);
            if (input == null) break;
            if (!MchoseV9ProCodec.TryParse(input, out int percent))
            {
                Log.Debug($"V9 Pro 忽略非电量响应: {Log.Hex(input)}");
                continue;
            }
            Log.Info($"V9 Pro 电量查询: {percent}% 标志=0x{input[3]:X2}");
            return percent;
        }
        Log.Warn("V9 Pro 电量查询未收到有效响应");
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "MCHOSE V9 Pro";
}
