using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： 狼蛛 G9 耳机 2.4G 插件，只被动读 AB 47 4D 0x86 状态帧。
/// 2026/10/19 23:50:00 contributor
/// 零写入；帧布局与固件前置条件见 docs/hardware/protocols.md §13。
/// </summary>
public sealed class AulaG9Plugin : BatteryPluginBase
{
    public const ushort AulaVid = 0x38A6;
    public const ushort G9Pid = 0x0030;

    /// <summary>与内置的 F99 Pro 插件（"Aula"）区分：两者 VID 不同、协议无关。</summary>
    public override string Name => "AulaG9";
    public override string DisplayName => "狼蛛 G9 耳机";
    public override string DisplayNameEn => "AULA G9";
    public override DeviceKind Kind => DeviceKind.Headset;

    /// <summary>纯被动零写入，可进 15 秒发现节拍。</summary>
    public override bool SupportsFastDiscovery => true;

    /// <summary>不存在写入路径，置 false 作纵深防御。</summary>
    protected override bool WritesEnabled => false;

    /// <summary>探测只读，不发任何字节。</summary>
    protected override bool ProbeWrites => false;

    /// <summary>认领后后台只读状态帧，电量变化即时上报。</summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>G9 是可换电池机型，换电会跳几十个点，故不拦单步变化。</summary>
    protected override int MaxPlausibleStepPercent => 100;

    /// <summary>状态帧每秒一条，10 秒静音判离线；没有查询命令，轮询 null 不是失联。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.FromSeconds(10),
        CountPollFailures = false,
    };

    /// <summary>纯被动探测失败多因耳机没开机，5 秒后重试。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);

    /// <summary>候选接口：38A6:0030 的 0xFFAB 厂商集合（64 字节双向）。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == AulaVid && info.ProductId == G9Pid
           && info.UsagePage == 0xFFAB
           && info.InputReportLength >= 9;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    /// <summary>探测 = 被动等一帧（约 1 秒一条）。</summary>
    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var status = await ListenAsync(handle, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        return status is { } value ? ("aula-g9-status86", value.Percent, value.Charging) : null;
    }

    /// <summary>读数由后台被动监听负责。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice _) => null;

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (!AulaG9Codec.TryParse(input, out int? percent, out var charging)) return null;

        Log.InfoOnChange($"aula-g9:{dev.Status.Id}", $"{percent}|{charging}",
            $"狼蛛 G9 状态帧: 电量={percent?.ToString() ?? "无读数"}% 状态={charging} 原始={Log.Hex(input)}");
        return (percent, charging, Offline: false);
    }

    private static async Task<(int? Percent, ChargingState Charging)?> ListenAsync(
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
            if (AulaG9Codec.TryParse(input, out int? percent, out var charging)) return (percent, charging);
        }
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "狼蛛 G9 耳机";
}
