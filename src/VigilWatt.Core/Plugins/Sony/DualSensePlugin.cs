using System.Diagnostics;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Sony;

/// <summary>
/// 功 能： DualSense 蓝牙电量被动监听插件。
/// 2026/10/19 01:13:01 路泽
/// 不发送输出或 Feature 报告，验证边界见 docs/hardware/dualsense.md。
/// </summary>
public sealed class DualSensePlugin : BatteryPluginBase
{
    public override string Name => "DualSense";
    public override string DisplayName => "Sony DualSense 蓝牙手柄";
    public override DeviceKind Kind => DeviceKind.Gamepad;
    public override bool SupportsFastDiscovery => true;
    protected override DeviceSource Source => DeviceSource.BluetoothClassic;
    protected override bool WritesEnabled => false;
    protected override bool ProbeWrites => false;
    protected override bool SupportsPassiveListen => true;
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.FromSeconds(10),
        CountPollFailures = false,
    };

    protected override bool IsCandidate(HidDeviceInfo info)
        => DualSenseCodec.IsSupportedInterface(info.VendorId, info.ProductId, info.DevicePath,
            info.UsagePage, info.Usage, info.InputReportLength);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(2))
        {
            var input = await handle.ReadInputAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            if (input != null && DualSenseCodec.TryParse(input, out int percent, out var charging))
                return ("DualSense 蓝牙电量（约 10% 档位）", percent, charging);
        }
        return null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev) => null;

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
        => DualSenseCodec.TryParse(input, out int percent, out var charging)
            ? (percent, charging, false) : null;

    protected override string BuildDisplayName(HidDeviceInfo info) => "DualSense";
}
