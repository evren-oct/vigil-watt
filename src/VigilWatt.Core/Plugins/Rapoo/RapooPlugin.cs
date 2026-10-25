using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： 雷柏 (Rapoo) 2.4G 插件 —— 纯被动实现，不向设备写任何字节。
/// 2026/10/06 15:53:36 xliu
/// 帧布局、充电标志编码与离线两条腿见 <see cref="RapooCodec"/> 及 docs/hardware/rapoo.md §1。
/// </summary>
public sealed class RapooPlugin : BatteryPluginBase
{
    public const ushort RapooVid = 0x24AE;

    public override string Name => "Rapoo";
    public override string DisplayName => "雷柏";
    public override string DisplayNameEn => "RAPOO";

    /// <summary>
    /// 声明 Unknown：雷柏的电量帧在键盘集合上，按名字猜会猜错；图标交给 UI 按显示名推断
    /// （同迈从）。见 docs/hardware/rapoo.md §4.2。
    /// </summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    /// <summary>含 "Mouse" 产品串的 VID:PID 集合（由 <see cref="OnDevicesScanned"/> 每轮刷新）。</summary>
    private readonly ConcurrentDictionary<string, bool> _mousePids = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按设备路径隔离的充电去抖状态（值类型，读写原子）。**必须按设备隔离**：插件实例
    /// 同时持有多台雷柏设备，共用一份会让两台的帧互相串味（R3）。
    /// </summary>
    private readonly ConcurrentDictionary<string, (byte Flag, bool Stable)> _chargeState =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 纯被动零写入，探测失败**不是错误**，只说明"鼠标睡着了"（休眠时 dongle 全静音，一动即恢复）。
    /// 必须配合 15 秒快速发现节拍（<see cref="SupportsFastDiscovery"/>）保证每拍都能重探：
    /// 旧值 3 分钟是"键盘出来了、鼠标不出来"的根因（日志实测鼠标要 4 分 09 秒才进列表）。
    /// </summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);

    /// <summary>纯被动零写入 → 允许 DeviceManager 用 15 秒节拍做发现（不发任何帧，无风险）。</summary>
    public override bool SupportsFastDiscovery => true;

    /// <summary>本插件不存在任何写入路径；置 false 作为纵深防御（CanWrite 恒 false）。</summary>
    protected override bool WritesEnabled => false;

    /// <summary>探测只被动听心跳、不发任何字节——不受写入开关约束，否则关掉写入会连鼠标都发现不了。</summary>
    protected override bool ProbeWrites => false;

    /// <summary>认领后后台只读心跳帧，插拔电/电量变化立即 StatusPushed。</summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>
    /// 本插件的离线判定策略（见 <see cref="OfflinePolicy"/>）。
    ///
    /// <b>Confirm = 6 秒</b>：心跳间隔实测 3.04 秒，6 秒 ≈ 2 个周期。不取 3 秒是因为那只有 1 个周期，
    /// 正常丢一帧就会误判，且"断链后无新帧"与"停手静音"在被动路径上无法区分。恢复帧一到候选立即撤销。
    /// <b>Silence = 90 秒</b>：**兜底，不是主要判据**。0x07 是活动驱动的心跳，一停手就静音，
    /// 4 分钟内实测有 5 次 &gt;10 秒的正常空档，短阈值必然误报；90 秒只留给"真关机/没电"。
    /// <b>CountPollFailures = false</b>：零写入，<see cref="BatteryPluginBase.PollDevice"/> 恒返回 null
    /// 是**协议形态**（没有查询命令），不是失联。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Confirm = TimeSpan.FromSeconds(6),
        Silence = TimeSpan.FromSeconds(90),
        CountPollFailures = false,
    };

    /// <summary>
    /// 候选接口：vendor 通道（雷柏把 0xFF00 放在 UsagePage 或 Usage 字段，两边都查），
    /// 且输入长度**正好 19 字节**（状态报表 0x07 的长度）。
    /// 收敛到 <c>==19</c> 而非"≥19 就试试"：本机 dongle 的 32B/33B 两条 vendor 通道从不发 0x07，
    /// 放行它们只会每次首扫各白等一整个探测窗口。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != RapooVid) return false;
        if (info.UsagePage != 0xFF00 && info.Usage != 0xFF00) return false;
        return info.InputReportLength == RapooCodec.StatusReportLength;
    }

    /// <summary>
    /// 探测 = 被动听 5 秒心跳，听不到 = 鼠标睡着，返回 null 进冷却，下一轮再试。
    /// 窗口 5 秒 = 实测心跳间隔 3.04 秒的 1.6 倍余量。探测本身不发任何字节。
    /// </summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var read = await ListenStatusAsync(handle, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (!read.HasValue) return null;
        return ("status-report7", read.Value.Percent, read.Value.Charging);
    }

    /// <summary>
    /// 读数由后台被动监听负责；轮询不再占 ReadInput（避免与监听抢同一悬空读）。
    /// 沉默离线见 <see cref="OfflinePolicy.Silence"/>。
    /// </summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice _) => null;

    /// <summary>解析被动 input：0x07 状态帧 → 电量/充电，并维护 RF 断链候选；其它帧忽略。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        var status = RapooCodec.Parse(input);
        if (status.Kind == RapooFrameKind.NotStatus) return null;

        // CR-28：心跳约 3 秒一条，逐帧 Info + Hex 会冲到约 2.8 万行/天、把 2MB 滚动历史冲掉。
        // 改成只在链路/充电/电量变化时记一条；原始帧不再进 Info（协议排错看 Debug 档的"收到报文"）。
        // **key 里带 RF 字节**：否则断链帧（bit7 由 1 变 0）不会触发记录，
        // 只在日志里看到"标记离线"却找不到断链那一帧，排查时抓瞎。
        var linkNote = status.LinkMode == RapooLinkMode.Wired
            ? "有线(无RF链路,bit7不适用)"
            : $"bit7连接={!status.LinkDown}";
        Log.InfoOnChange($"rapoo-status:{dev.Status.Id}",
            $"{status.Link}|{status.ChargeFlag}|{status.Percent}|{status.RfByte}",
            $"雷柏 状态帧: 连接={status.Link} 充电标志={status.ChargeFlag} 电量={status.Percent}% " +
            $"RF=0x{status.RfByte:X2}({linkNote} RSSI={status.RfByte & 0x7F})");

        // 链路位与电量无关，先处理——电量越界的帧（休眠瞬态）同样要能标记/撤销候选。
        // 有线模式 LinkDown 恒 false：没有 RF 链路，bit7=0 是正常语义（见 RapooStatus.LinkDown）。
        if (status.LinkDown) MarkOfflineCandidate(dev);
        else ClearOfflineCandidate(dev);

        if (status.Kind == RapooFrameKind.StatusInvalidPercent)
        {
            if (Log.IsEnabled(LogLevel.Debug))
                Log.Debug($"雷柏 电量越界/归零（疑似休眠瞬态），丢弃: {Log.Hex(input)}");
            return null;
        }

        return (status.Percent, ResolveCharging(dev, status), Offline: false);
    }

    /// <summary>充电状态去抖，规则见 <see cref="RapooCodec.ResolveCharging"/>；状态按设备路径存于 <see cref="_chargeState"/>。</summary>
    private ChargingState ResolveCharging(TrackedDevice dev, RapooStatus status)
    {
        var key = dev.Info.DevicePath;
        var (flag, stable) = _chargeState.GetOrAdd(key, (0xFF, false));
        var d = RapooCodec.ResolveCharging(status.ChargeFlag, flag, stable, status.Percent);
        _chargeState[key] = (d.LastFlag, d.Stable);
        return d.Charging;
    }

    /// <summary>设备移除时清掉它的去抖状态（否则随插拔累积，R3 累积型泄漏）。</summary>
    private void PruneChargeState(IReadOnlyList<HidDeviceInfo> present)
    {
        var alive = present.Select(d => d.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _chargeState.Keys.Where(k => !alive.Contains(k)).ToList())
            _chargeState.TryRemove(key, out _);
    }

    /// <summary>在截止时间前收 input report，解析 0x07 状态帧。</summary>
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
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"雷柏 收到报文: {Log.Hex(input)}");

            var status = RapooCodec.Parse(input);
            if (status.Kind == RapooFrameKind.Status) return (status.Percent, status.Charging);
        }
        return null;
    }

    /// <summary>显示名：雷柏的电量帧挂在键盘集合上，靠"是不是鼠标"改名字。见 docs/hardware/rapoo.md §4.2。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
        => RapooCodec.DisplayName(info.Product, SiblingIsMouse(info), info.VidPid);

    /// <summary>同 <c>VID:PID</c> 下是否存在标准鼠标集合（usage 0001:0002）的兄弟接口。</summary>
    private bool SiblingIsMouse(HidDeviceInfo info)
        => _mousePids.ContainsKey($"{info.VendorId:X4}:{info.ProductId:X4}");

    /// <summary>
    /// 认领前先重建"哪些 VID:PID 带鼠标集合"的索引。判据用 **usage 0001:0002**（不是产品串）：
    /// 接收器模式的产品串是设备级的 "Rapoo Gaming Device"，不含 Mouse。
    /// </summary>
    public override async Task<IReadOnlyList<DeviceStatus>> AttachAsync(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
    {
        _mousePids.Clear();
        foreach (var d in availableDevices)
        {
            if (d.VendorId != RapooVid) continue;
            if (d.UsagePage == 0x0001 && d.Usage == 0x0002)
                _mousePids[$"{d.VendorId:X4}:{d.ProductId:X4}"] = true;
        }
        PruneChargeState(availableDevices);
        return await base.AttachAsync(availableDevices, claims).ConfigureAwait(false);
    }

    /// <summary>插件被禁用/卸载时清空按设备保存的状态。</summary>
    public override void Detach()
    {
        _chargeState.Clear();
        base.Detach();
    }
}
