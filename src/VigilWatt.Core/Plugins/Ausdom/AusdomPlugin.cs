using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Ausdom;

/// <summary>
/// 功 能： AUSDOM（阿斯盾）G04 系列 2.4G 接收器插件 —— 纯被动实现，不向设备写任何字节。
/// 2026/10/31 22:50:34 Zen
/// 帧布局、型号识别与未验证的充电位见 docs/hardware/ausdom.md §1。
/// </summary>
public sealed class AusdomPlugin : BatteryPluginBase
{
    public const ushort AusdomVid = 0x363C;
    public const ushort G04Pid = 0xFF9D;

    public override string Name => "Ausdom";
    public override string DisplayName => "阿斯盾";
    public override string DisplayNameEn => "AUSDOM";
    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>
    /// 纯被动零写入：探测失败只是「鼠标睡着了」，不是错误；必须配合 15 秒快速发现节拍重探。
    /// 与雷柏一致（见 BatteryPluginBase.ProbeCooldown 注释与 Rapoo 取值）。
    /// </summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);

    /// <summary>纯被动零写入 → 允许 DeviceManager 用 15 秒节拍做发现（不发任何帧，无风险）。</summary>
    public override bool SupportsFastDiscovery => true;

    /// <summary>本插件不存在任何写入路径；置 false 作为纵深防御（CanWrite 恒 false）。</summary>
    protected override bool WritesEnabled => false;

    /// <summary>探测只被动听状态帧、不发任何字节——不受写入开关约束，否则关掉写入会连设备都发现不了。</summary>
    protected override bool ProbeWrites => false;

    /// <summary>认领后后台只读状态帧，电量变化立即 StatusPushed。</summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>
    /// 离线判定策略。
    /// <b>Silence = 0</b>：0x0A 推送很稀疏（仅状态变化/交互时才有，静置时固件不发帧），
    /// 沉默毫无信息量——静置 90 秒会被误判离线、动一下又「重新上线」，反复抖动。
    /// 真关机/没电的兜底改由 <see cref="OfflinePolicy.Confirm"/> 的延迟确认承担。
    /// <b>CountPollFailures = false</b>：零写入，<c>PollDevice</c> 恒返回 null 是协议形态而非失联。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Confirm = TimeSpan.FromSeconds(6),
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>
    /// 候选接口：VID/PID 精确匹配 G04 接收器，且 usagePage=0xFFA0（厂商页）、输入长度正好 64 字节。
    /// 严格多滤：本机该接收器另有多个集合（0x0001/0x0002、0x0001/0x0006、0x000C、0xFFB1 等），
    /// 只有 0xFFA0/0x0002 这条 64 字节输入通道携带电量帧（扫描日志实测），故按 usage+长度收敛。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != AusdomVid) return false;
        if (info.ProductId != G04Pid) return false;
        if (info.UsagePage != 0xFFA0) return false;
        if (info.Usage != 0x0002) return false;
        return info.InputReportLength == 64;
    }

    /// <summary>
    /// 探测 = 被动听 5 秒状态帧，听不到 = 设备睡着，返回 null 进冷却，下一轮再试。
    /// 窗口 5 秒 = 短帧约每 1 秒一发的 1.6 倍余量。探测本身不发任何字节。
    /// </summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var read = await ListenStatusAsync(handle, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (!read.HasValue) return null;
        return ("ausdom-status-0a", read.Value.Percent, read.Value.Charging);
    }

    /// <summary>读数由后台被动监听负责；轮询不再占 ReadInput（避免与监听抢同一悬空读）。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice _) => null;

    /// <summary>解析被动 input：0x0A 状态帧 → 电量/充电，其它帧忽略。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        var parsed = AusdomCodec.TryParse(input);
        if (parsed == null) return null;

        Log.InfoOnChange($"ausdom-status:{dev.Status.Id}",
            $"{parsed.Value.Percent}|{parsed.Value.Charging}",
            $"阿斯盾 状态帧: 电量={parsed.Value.Percent}% 充电={parsed.Value.Charging} 原始={Log.Hex(input)}");
        return (parsed.Value.Percent, parsed.Value.Charging, Offline: false);
    }

    /// <summary>在截止时间前收 input report，解析 0x0A 状态帧。</summary>
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
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"阿斯盾 收到报文: {Log.Hex(input)}");

            var parsed = AusdomCodec.TryParse(input);
            if (parsed.HasValue) return (parsed.Value.Percent, parsed.Value.Charging);
        }
        return null;
    }

    /// <summary>显示名：优先用产品串（如「G04 Ultra 8K Dongle」），空则退回品牌加 VID/PID。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
        => string.IsNullOrWhiteSpace(info.Product) ? $"阿斯盾 ({info.VidPid})" : $"阿斯盾 {info.Product}";
}
