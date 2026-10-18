using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Delux;

/// <summary>
/// 功 能： DELUX M900 Pro 2.4G 鼠标插件，只被动读取 5 字节状态帧。
/// 2026/10/19 02:20:00 sqcxz
/// 帧布局与验证边界见 docs/hardware/protocols.md §7；通道 usage 0x000A/0x0000。
/// </summary>
public sealed class DeluxPlugin : BatteryPluginBase
{
    public const ushort DeluxVid = 0x1D57;
    public const ushort M900ProPid = 0xFA65;

    public override string Name => "Delux";
    public override string DisplayName => "DELUX";
    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>纯被动零写入，允许 15 秒发现节拍；鼠标睡前也能在下一拍重试。</summary>
    public override bool SupportsFastDiscovery => true;

    /// <summary>纯被动，不存在写入路径；置 false 作为纵深防御。</summary>
    protected override bool WritesEnabled => false;

    /// <summary>探测只读状态帧，不发任何字节——不受写入开关约束。</summary>
    protected override bool ProbeWrites => false;

    /// <summary>认领后后台只读 5 字节状态帧，电量/充电变化即时上报。</summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>状态帧约 2 秒一条；90 秒静音兜底。没有查询命令，Poll 返回 null 是形态不是失联。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Confirm = TimeSpan.FromSeconds(6),
        Silence = TimeSpan.FromSeconds(90),
        CountPollFailures = false,
    };

    /// <summary>纯被动探测失败只说明鼠标睡着，5 秒后再试；必须小于 15 秒发现节拍。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);

    /// <summary>候选接口：1D57:FA65 的 0x000A/0x0000 集合（5 字节输入、无输出、无 feature）。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == DeluxVid && info.ProductId == M900ProPid
           && info.UsagePage == 0x000A && info.Usage == 0x0000
           && info.InputReportLength >= DeluxCodec.FrameLength
           && info.OutputReportLength == 0
           && info.FeatureReportLength == 0;

    /// <summary>探测 = 被动听一帧（约 2 秒一条），听不到返回 null 进冷却。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var status = await ListenStatusAsync(handle, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return status is { } value ? ("compx-status5", value.Percent, value.Charging) : null;
    }

    /// <summary>读数由后台被动监听负责；轮询不再占 ReadInput。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice _) => null;

    /// <summary>解析 5 字节状态帧；非本协议帧返回 null。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (!DeluxCodec.TryParse(input, out int percent, out var charging, out byte chargeFlag)) return null;

        Log.InfoOnChange($"delux-status:{dev.Status.Id}", $"{chargeFlag}|{percent}",
            $"DELUX 状态帧: 标志=0x{chargeFlag:X2} 电量={percent}% 原始={Log.Hex(input)}");
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
            if (DeluxCodec.TryParse(input, out int percent, out var charging, out _))
                return (percent, charging);
        }
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "DELUX M900 Pro";
}
