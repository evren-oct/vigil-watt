using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Ilovbee;

/// <summary>
/// 功 能： ilovbee b98 键盘插件——零写入，只听 2.4G 接收器主动推的 20 字节状态帧。
/// 2026/11/01 17:32:05 20211
/// 帧与校验见 docs/hardware/devices/ilovbee.md §1；编解码在 IlovbeeCodec。
/// </summary>
public sealed class IlovbeePlugin : BatteryPluginBase
{
    public override string Name => "Ilovbee";             // AppSettings.PluginEnabled 的 key
    public override string DisplayName => "ilovbee";
    public override string DisplayNameEn => "ILOVBEE";
    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>纯被动零写入 → 可参与快速发现节拍，键盘唤醒后不必干等 60 秒主轮询。</summary>
    public override bool SupportsFastDiscovery => true;
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);

    protected override bool WritesEnabled => false;
    protected override bool ProbeWrites => false;
    protected override bool SupportsPassiveListen => true;

    /// <summary>
    /// 5 分钟：键盘休眠时接口完全静音（issue #68 样本里静置 105 秒零帧），
    /// 阈值压太短会把"睡着"判成"拔了"。不发帧，故关掉失败记账。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.FromSeconds(300),
        CountPollFailures = false,
    };

    /// <summary>只认状态帧所在的厂商集合；接收器是 SINOWEALTH 通用 dongle，真正的门禁是帧校验。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == IlovbeeCodec.VendorId
           && info.ProductId == IlovbeeCodec.ReceiverPid
           && info.UsagePage == IlovbeeCodec.VendorUsagePage
           && info.Usage == IlovbeeCodec.VendorUsage
           && info.InputReportLength >= IlovbeeCodec.FrameLength;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    /// <summary>帧稀疏（静置十几秒一条），窗口给到 7 秒；等不到就本轮不认领，下轮再来。</summary>
    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(7);
        while (DateTimeOffset.Now < deadline)
        {
            var remaining = deadline - DateTimeOffset.Now;
            var slice = remaining < TimeSpan.FromMilliseconds(300) ? remaining : TimeSpan.FromMilliseconds(300);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            if (!IlovbeeCodec.TryParse(input, out var kind, out int percent, out _)) continue;
            return kind == IlovbeeCodec.FrameKind.Battery
                ? ("ilovbee-b98-v1", percent, ChargingState.Unknown)
                : ("ilovbee-b98-v1", null, ChargingState.Unknown);   // 关机帧：认领但无读数 → 标离线
        }
        return null;
    }

    /// <summary>零写入：不发查询帧，电量全靠被动推送。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice _) => null;

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (!IlovbeeCodec.TryParse(input, out var kind, out int percent, out bool chargingEdge)) return null;

        switch (kind)
        {
            case IlovbeeCodec.FrameKind.PowerOff:
                Log.InfoOnChange($"ilovbee-off:{dev.Status.Id}", "1", $"ilovbee b98 关机帧: 原始={Log.Hex(input)}");
                return (null, dev.Status.Charging, Offline: true);

            case IlovbeeCodec.FrameKind.PowerOn:
                return (dev.Status.BatteryPercent, dev.Status.Charging, Offline: false);
        }

        // 充电插入帧的 [6] 是占位值（实测 100%，前后帧 75%），只当事件、不采信电量。
        if (chargingEdge) return (dev.Status.BatteryPercent, dev.Status.Charging, Offline: false);

        Log.InfoOnChange($"ilovbee:{dev.Status.Id}", percent.ToString(),
            $"ilovbee b98 状态帧: 电量={percent}% 原始={Log.Hex(input)}");
        // [7] bit0 只在插充电器那一帧置 1、拔线无对应事件帧 → 不推断充电状态（ilovbee.md §1.2）
        return (percent, ChargingState.Unknown, Offline: false);
    }

    protected override string BuildDisplayName(HidDeviceInfo info)
        => string.IsNullOrWhiteSpace(info.Product) || info.Product.Contains("Receiver", StringComparison.OrdinalIgnoreCase)
            ? "ilovbee b98"
            : $"ilovbee {info.Product}";
}
