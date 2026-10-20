using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： 狼蛛 L9 Ultra 鼠标 2.4G 插件，发一条状态查询读电量。
/// 2026/10/19 23:50:00 contributor
/// 接收器 4 个厂商接口只有 0xFFA8 应答；见 docs/hardware/protocols.md §14。
/// </summary>
public sealed class AulaL9Plugin : BatteryPluginBase
{
    public const ushort AulaVid = 0x38A6;
    public const ushort L9ReceiverPid = 0x28FD;

    public override string Name => "AulaL9";
    public override string DisplayName => "狼蛛 L9 Ultra";
    public override string DisplayNameEn => "AULA L9 Ultra";
    public override DeviceKind Kind => DeviceKind.Mouse;

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

    /// <summary>候选接口：38A6:28FD 的 0xFFA8 厂商集合——四个厂商接口里唯一应答的一条。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == AulaVid && info.ProductId == L9ReceiverPid
           && info.UsagePage == 0xFFA8
           && info.OutputReportLength >= 3
           && info.InputReportLength >= 11;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var read = await QueryAsync(handle, TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
        return read is { } value ? ("aula-l9-status85", value.Percent, value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = QueryAsync(dev.Handle, TimeSpan.FromMilliseconds(800)).GetAwaiter().GetResult();
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>发一条查询并在窗口内等状态回包。</summary>
    private static async Task<(int? Percent, ChargingState Charging)?> QueryAsync(
        HidDeviceHandle handle, TimeSpan window)
    {
        var report = AulaL9Codec.BuildStatusQuery(handle.Info.OutputReportLength);
        if (!handle.WriteOutput(report, TimeSpan.FromMilliseconds(200))) return null;

        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            if (!AulaL9Codec.TryParse(input, out int percent, out var charging)) continue;

            Log.InfoOnChange("aula-l9", $"{percent}|{charging}",
                $"狼蛛 L9 状态回包: {percent}% {charging} 原始={Log.Hex(input)}");
            return (percent, charging);
        }
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "狼蛛 L9 Ultra";
}
