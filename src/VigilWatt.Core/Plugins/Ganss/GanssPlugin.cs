using System.Diagnostics;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Ganss;

/// <summary>
/// 功 能： GS3104T-Pro 2.4G 接收器电量查询插件。
/// 2026/10/18 20:20:00 路泽
/// 协议与验证范围见 docs/hardware/ganss.md。
/// </summary>
public sealed class GanssPlugin : BatteryPluginBase
{
    public override string Name => "Ganss";
    public override string DisplayName => "GANSS GS3104T-Pro 2.4G";
    public override DeviceKind Kind => DeviceKind.Keyboard;
    protected override int MinValidPercent => 1;
    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info)
        => GanssCodec.IsSupportedInterface(info.VendorId, info.ProductId, info.DevicePath,
            info.UsagePage, info.Usage, info.InputReportLength, info.OutputReportLength,
            info.OutputReportId);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var percent = await QueryAsync(handle).ConfigureAwait(false);
        return percent.HasValue ? ("ganss-gs3104t-pro-v1", percent, ChargingState.Unknown) : null;
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
        // 写点再次校验开关和完整接口，禁止把同 VID/PID 的其它集合当作厂商通道。
        if (!WritesEnabled || !IsCandidate(handle.Info)) return null;
        if (!handle.WriteOutput(GanssCodec.CreateQuery(), TimeSpan.FromMilliseconds(500)))
            return null;
        var elapsed = Stopwatch.StartNew();
        var window = TimeSpan.FromMilliseconds(750);
        while (elapsed.Elapsed < window)
        {
            var remaining = window - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var input = await handle.ReadInputAsync(remaining).ConfigureAwait(false);
            if (input == null) break;
            if (!GanssCodec.TryParse(input, out int percent))
            {
                Log.Debug($"GANSS 忽略非电量响应: {Log.Hex(input)}");
                continue;
            }
            Log.Info($"GANSS 电量查询: {percent}%");
            return percent;
        }
        Log.Warn("GANSS 电量查询未收到有效响应");
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "GANSS GS3104T-Pro";
}
