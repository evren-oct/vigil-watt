using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Langtu;

/// <summary>
/// 功 能： 狼途 T98 键盘插件——零写入，只听 2.4G 接收器主动推的状态帧。
/// 2026/10/31 20:32:00 20211
/// 帧与休眠实测见 docs/hardware/langtu.md §1；编解码在 LangtuT98Codec。
/// </summary>
public sealed class LangtuT98Plugin : BatteryPluginBase
{
    public override string Name => "Langtu";              // AppSettings.PluginEnabled 的 key
    public override string DisplayName => "狼途";
    public override string DisplayNameEn => "LANGTU";
    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>纯被动零写入 → 可参与快速发现节拍，键盘唤醒后不必干等 60 秒主轮询。</summary>
    public override bool SupportsFastDiscovery => true;
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);

    protected override bool WritesEnabled => false;
    protected override bool ProbeWrites => false;
    protected override bool SupportsPassiveListen => true;

    /// <summary>
    /// 5 分钟而非基类默认值：设备休眠时接口**完全静音**（issue #40 实测连续 4 分钟零帧），
    /// 阈值低于 4 分钟会把"睡着"判成"拔了"。真正关机才会无限静音，5 分钟足够兜住。
    /// 不发帧，故关掉失败记账。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.FromSeconds(300),
        CountPollFailures = false,
    };

    /// <summary>只认状态帧所在的厂商集合；同一接收器的 0xFF01 通知集合实测全程静音，认了会白等。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == LangtuT98Codec.VendorId
           && info.ProductId == LangtuT98Codec.ReceiverPid
           && info.UsagePage == LangtuT98Codec.VendorUsagePage
           && info.Usage == LangtuT98Codec.VendorUsage
           && info.InputReportLength >= LangtuT98Codec.FrameLength;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    /// <summary>帧约 5.5 秒一条，窗口给到 7 秒才保证至少见到一条；等不到就本轮不认领，下轮再来。</summary>
    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(7);
        while (DateTimeOffset.Now < deadline)
        {
            var remaining = deadline - DateTimeOffset.Now;
            var slice = remaining < TimeSpan.FromMilliseconds(400) ? remaining : TimeSpan.FromMilliseconds(400);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            if (!LangtuT98Codec.TryParse(input, out int percent)) continue;
            return ("langtu-t98-status", percent, ChargingState.Unknown);
        }
        return null;
    }

    /// <summary>零写入：不发查询帧，电量全靠被动推送。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice _) => null;

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (!LangtuT98Codec.TryParse(input, out int percent)) return null;

        Log.InfoOnChange($"langtu-t98:{dev.Status.Id}", percent.ToString(),
            $"狼途 T98 状态帧: 电量={percent}% 原始={Log.Hex(input)}");
        // [7] 恒为 0x01、插拔充电线都不变，不是充电位 → 不推断充电状态（langtu.md §1.3）
        return (percent, ChargingState.Unknown, Offline: false);
    }

    protected override string BuildDisplayName(HidDeviceInfo info)
        => string.IsNullOrWhiteSpace(info.Product) ? "狼途 T98" : $"狼途 {info.Product}";
}
