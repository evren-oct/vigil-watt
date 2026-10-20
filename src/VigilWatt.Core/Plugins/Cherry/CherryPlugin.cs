using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Cherry;

/// <summary>
/// 功 能： CHERRY MX 8.2 Xaga 的有界电量查询与被动响应监听。
/// 2026/10/19 20:38:39 kaniu
/// </summary>
public sealed class CherryPlugin : BatteryPluginBase
{
    public override string Name => "Cherry";
    public override string DisplayName => "樱桃";
    public override string DisplayNameEn => "CHERRY";
    public override DeviceKind Kind => DeviceKind.Keyboard;
    protected override int MinValidPercent => 0;
    protected override bool SupportsPassiveListen => true;
    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == CherryCodec.VendorId && info.ProductId == CherryCodec.ProductId
           && info.UsagePage == 0xFF1C && info.Usage == 0x0092
           && info.OutputReportId == CherryCodec.ReportId
           && info.InputReportLength == CherryCodec.ReportLength
           && info.OutputReportLength == CherryCodec.ReportLength;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => QueryAsync(handle).GetAwaiter().GetResult() is { } read
            ? ("cherry-xaga-report4", read.Percent, read.Charging) : null;

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
        => await QueryAsync(handle).ConfigureAwait(false) is { } read
            ? ("cherry-xaga-report4", read.Percent, read.Charging) : null;

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        var read = QueryAsync(dev.Handle).GetAwaiter().GetResult();
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
        => CherryCodec.TryParse(input, out int percent, out var charging)
            ? (percent, charging, false) : null;

    private async Task<(int? Percent, ChargingState Charging)?> QueryAsync(HidDeviceHandle handle)
    {
        if (!WritesEnabled || !handle.WriteOutput(CherryCodec.BuildBatteryQuery(), TimeSpan.FromMilliseconds(500)))
            return null;
        var deadline = DateTime.UtcNow.AddSeconds(1.2);
        while (DateTime.UtcNow < deadline)
        {
            var input = await handle.ReadInputAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            if (input != null && CherryCodec.TryParse(input, out int percent, out var charging))
                return (percent, charging);
        }
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "CHERRY MX BOARD 8.2 Xaga";
}
