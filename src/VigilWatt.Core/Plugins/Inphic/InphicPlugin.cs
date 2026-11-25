using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Inphic;

/// <summary>
/// 功 能： 英菲克 2.4G 鼠标插件，只被动读取状态帧。
/// 2026/10/14 20:41:17 20211
/// 帧布局与实测边界见 docs/hardware/brands/inphic.md §1。
/// </summary>
public sealed class InphicPlugin : BatteryPluginBase
{
    public const ushort InphicVid = 0x1D57;
    public const ushort ZhenPid = 0xFA65;

    public override string Name => "Inphic";
    public override string DisplayName => "英菲克";
    public override string DisplayNameEn => "INPHIC";
    public override DeviceKind Kind => DeviceKind.Mouse;
    public override bool SupportsFastDiscovery => true;
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);
    protected override bool WritesEnabled => false;
    protected override bool ProbeWrites => false;
    protected override bool SupportsPassiveListen => true;
    protected override OfflinePolicy Offline { get; } = new()
    {
        Confirm = TimeSpan.FromSeconds(6),
        Silence = TimeSpan.FromSeconds(90),
        CountPollFailures = false,
    };

    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == InphicVid && info.ProductId == ZhenPid
           && info.UsagePage == 0xFF00 && info.Usage == 0x0001
           && info.OutputReportId == InphicCodec.StatusReportId
           && info.InputReportLength >= 5;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var status = await ListenStatusAsync(handle, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return status is { } value ? ("inphic-status-report3", value.Percent, value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice _) => null;

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (!InphicCodec.TryParse(input, out int percent, out var charging, out byte chargeFlag)) return null;

        Log.InfoOnChange($"inphic-status:{dev.Status.Id}", $"{chargeFlag}|{percent}",
            $"英菲克 状态帧: 标志=0x{chargeFlag:X2} 电量={percent}% 原始={Log.Hex(input)}");
        return (percent, charging, Offline: false);
    }

    private static async Task<(int Percent, ChargingState Charging)?> ListenStatusAsync(
        HidDeviceHandle handle, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(400) ? remaining : TimeSpan.FromMilliseconds(400);
            if (slice <= TimeSpan.Zero) break;
            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            if (!InphicCodec.TryParse(input, out int percent, out var charging, out _)) continue;
            return (percent, charging);
        }
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info)
        => string.IsNullOrWhiteSpace(info.Product) ? "英菲克 ZHEN" : $"英菲克 {info.Product}";
}
