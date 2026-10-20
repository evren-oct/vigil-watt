using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Vgn;

/// <summary>
/// 功 能： VGN V98 Pro 键盘 2.4G 插件，发一条 7 字节查询读电量。
/// 2026/10/19 23:50:00 contributor
/// 键盘不发心跳，必须主动查询；见 docs/hardware/protocols.md §15。
/// </summary>
public sealed class VgnPlugin : BatteryPluginBase
{
    public const ushort VgnVid = 0x320F;
    public const ushort V98ProDonglePid = 0x5088;

    /// <summary>厂商私有集合 usagePage（64 字节双向）。</summary>
    public const ushort VendorUsagePage = 0xFF1C;

    public override string Name => "Vgn";
    public override string DisplayName => "VGN";
    public override string DisplayNameEn => "VGN";
    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>要发查询帧，写入留在 60 秒主轮询。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>有查询帧要发，探测即写入。</summary>
    protected override bool ProbeWrites => true;

    /// <summary>没有周期性心跳，沉默无意义；失败由插件自己报账。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>候选接口：320F:5088 的 0xFF1C/0x0092 集合。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == VgnVid && info.ProductId == V98ProDonglePid
           && info.UsagePage == VendorUsagePage
           && info.Usage == 0x0092
           && info.OutputReportLength >= 17;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var read = await QueryAsync(handle, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        return read is { } value ? ("vgn-v98pro-1a", value.Percent, value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = QueryAsync(dev.Handle, TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>发一条电量查询并在窗口内等 0x1A 回包。</summary>
    private static async Task<(int? Percent, ChargingState Charging)?> QueryAsync(
        HidDeviceHandle handle, TimeSpan window)
    {
        var report = VgnCodec.BuildQueryReport(handle.Info.OutputReportLength);
        if (!handle.WriteOutput(report, TimeSpan.FromMilliseconds(300))) return null;

        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            if (!VgnCodec.TryParse(input, out int percent, out var charging)) continue;

            Log.InfoOnChange("vgn-battery", $"{percent}|{charging}",
                $"VGN 电量回包: {percent}% {charging} 原始={Log.Hex(input)}");
            return (percent, charging);
        }
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "VGN V98 Pro";
}
