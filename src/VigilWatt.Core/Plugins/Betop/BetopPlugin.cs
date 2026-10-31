using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Betop;

/// <summary>
/// 功 能： 北通 (BETOP) 手柄 2.4G 接收器插件。
/// 2026/10/06 15:53:36 xliu
/// 帧结构、电量编码、T9 被动流与"未连接占位帧"判定见 <see cref="BetopCodec"/> 及 docs/hardware/betop.md §1。
/// </summary>
public sealed class BetopPlugin : BatteryPluginBase
{
    /// <summary>北通（BETOP）旧接收器的 USB 厂商 ID；新批次 dongle 是 <see cref="BetopVidNew"/>。</summary>
    public const ushort BetopVid = BetopCodec.VendorId;

    /// <summary>新批次 dongle 的厂商 ID（`KP20 DONGLE`、`BTP-KP70A DONGLE` 底座）。</summary>
    public const ushort BetopVidNew = BetopCodec.VendorIdNew;

    /// <summary>回退写入长度（接口未声明输出长度时使用；鲲鹏20 厂商接口为 64）。</summary>
    private const int FallbackWriteLength = 64;

    public override string Name => "Betop";
    public override string DisplayName => "北通";
    public override string DisplayNameEn => "BETOP";
    public override DeviceKind Kind => DeviceKind.Gamepad;

    /// <summary>
    /// T9 等型号在 Col04 上持续推状态流，认领后后台只读即可实时更新电量/离线；
    /// 鲲鹏等仍靠 Poll 写 0x15 查询（被动听不到时无影响）。
    /// </summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>
    /// 本插件的离线判定策略（见 <see cref="OfflinePolicy"/>）。
    ///
    /// <b>Silence = 0</b>：沉默在这里不是可靠判据（鲲鹏无推流，T9 停流也可能是句柄异常）；
    /// 手柄"没连上"有明确占位帧，走 <c>RequestOffline</c> 立即生效。
    /// <b>CountPollFailures = false</b>：与迈从同理——<c>PollDevice</c> 返回 null 有三种可能
    /// （真的没应答 / 用户关了探测 / 熔断停写中），基类分不出来。当前离线只由占位帧与拔插触发。
    ///
    /// 待确认：类头"手柄休眠时无响应 ⇒ 本插件表现为离线"其实不会发生——本插件没有像迈从那样
    /// 在发过帧的分支自报失败。要不要补上得先有真机休眠样本，避免改成误报。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>
    /// 接收器常在、手柄可能休眠：探测失败后 30 秒再试，等用户按键唤醒。
    /// 必须小于主轮询周期（60 秒）——失败发生在轮询开始后约 6 秒处，冷却等于一整轮会让下一拍
    /// 被判"冷却未过"而白白跳过（实测首次发现要 2 分钟）。
    /// </summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(30);

    /// <summary>
    /// 候选接口：北通 VID（`0x20BC` 旧 / `0x20DD` 新批次）+ 电量页（`0xFF00` 厂商页 / `0x00FF` 广播页）。
    /// 接收器的鼠标/键盘/手柄接口都在 usage_page 0x0001，据此天然排除（判据见 betop.md §1.8）。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        // 观测期（2026-10-25）：官方软件是否参与 dongle 身份抖动尚无证据，这里只让它记状态变化，不拦认领。
        BetopOfficialAppGuard.Observe();

        if (BetopCodec.IsVendorInterface(info.VendorId, info.UsagePage, info.Usage)) return true;

        // 判据不符也要留痕：CR-89 那次用户只看到"没有设备"，日志里一个字都没有
        if (BetopCodec.IsBetopVendor(info.VendorId))
            Log.InfoThrottled($"betop-nomatch:{info.DevicePath}",
                $"北通设备在、但接口不对（未认领）: {info} path={info.DevicePath}", TimeSpan.FromMinutes(10));
        return false;
    }

    /// <summary>杂项广播页（`0x00FF`）承载鲲鹏20/70 电量：dongle 每秒自推、有线静默须查询，见 betop.md §1.8。</summary>
    private static bool IsBroadcastChannel(HidDeviceInfo info)
        => BetopCodec.IsBroadcastInterface(info.VendorId, info.UsagePage, info.Usage);

    /// <summary>广播通道的读窗口：广播每秒一帧，3 秒留丢一帧的余量。</summary>
    private static readonly TimeSpan BroadcastListenWindow = TimeSpan.FromSeconds(3);

    /// <summary>鲲鹏20 的 dongle 与有线本体归一同一设备位（先例 AtkPlugin 配对组），表与缺点见 <see cref="BetopDeviceIdentity"/>。</summary>
    protected override string IdFor(string devicePath)
        => BetopDeviceIdentity.KunpengId(devicePath, Name.ToLowerInvariant()) ?? base.IdFor(devicePath);

    /// <summary>
    /// 探测：杂项页先纯听、静默再查询兜底（探测写受基类探测闸门）；厂商页发查询。收不到 = 手柄休眠进冷却。
    /// dongle 报"未连接占位帧"时**也算探测成功**（协议名 + 电量 null）：基类据此认领设备、
    /// 但按"无读数 = 离线"标离线，列表显示「北通 T9 · 离线 · --%」，而不是手柄关了还显示"67% 在线"。
    /// </summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var read = IsBroadcastChannel(handle.Info)
            ? Listen(handle, BroadcastListenWindow, dev: null)
                ?? Query(handle, TimeSpan.FromSeconds(2), dev: null)
            : Query(handle, TimeSpan.FromSeconds(2), dev: null);
        if (read == null) return null;
        if (!read.Value.Linked) return (ProtocolName, null, ChargingState.Unknown);
        return (ProtocolName, read.Value.Percent, MapCharging(read.Value.Percent, read.Value.Charging));
    }

    /// <summary>
    /// 轮询：杂项页先纯听（dongle 每秒自推必命中、零写入），听不到且 CanWrite 才查询兜底；厂商页直接发查询。
    /// 写入被开关挡住时返回 null（计入失败，但不再写；熔断对北通当前不生效，见 betop.md §1.7）。
    /// </summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (IsBroadcastChannel(dev.Handle.Info))
        {
            var listen = Listen(dev.Handle, TimeSpan.FromSeconds(1.5), dev)
                ?? (CanWrite(dev) ? Query(dev.Handle, TimeSpan.FromSeconds(1.5), dev) : null);
            if (listen == null) return null;
            if (!listen.Value.Linked)
            {
                RequestOffline(dev);
                return null;
            }
            return (listen.Value.Percent, MapCharging(listen.Value.Percent, listen.Value.Charging));
        }

        if (!CanWrite(dev)) return null;
        var read = Query(dev.Handle, TimeSpan.FromSeconds(1.5), dev);
        if (read == null) return null;

        // dongle 明确告知"手柄没连上"：立即标离线（省掉 3 次失败 = 3 分钟），本轮不产生读数。
        if (!read.Value.Linked)
        {
            RequestOffline(dev);
            return null;
        }

        return (read.Value.Percent, MapCharging(read.Value.Percent, read.Value.Charging));
    }

    /// <summary>被动解析 T9 状态流 / 偶发 0x15 状态报告；未连接占位帧 → Offline=true。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        var t9 = BetopCodec.ParseT9Status(input);
        if (t9.HasValue)
        {
            if (!t9.Value.Linked)
                return (null, ChargingState.Unknown, Offline: true);
            // 开机瞬态（0x18 = 20%，只存在 29~218 ms）不采信，否则低电量提醒和异常过滤器都会拿它当基准。
            if (!IsSettled(dev.Info.DevicePath, t9.Value.Percent)) return null;
            return (t9.Value.Percent, MapCharging(t9.Value.Percent, t9.Value.Charging), Offline: false);
        }

        var parsed = BetopCodec.ParseStatus(input);
        if (parsed.HasValue)
            return (parsed.Value.Percent, MapCharging(parsed.Value.Percent, parsed.Value.Charging), Offline: false);

        return null;
    }

    /// <summary>协议名（电量通道本身；在线/离线由 IsOnline + 电量是否为空表达）。</summary>
    private const string ProtocolName = "betop-status15";

    /// <summary>一条查询窗口的结果：读数 / dongle 说"手柄未连接" / 什么都没读到。</summary>
    /// <param name="Percent">电量百分比；<paramref name="Linked"/> 为 false 时为 0（无意义）。</param>
    /// <param name="Charging">是否充电中。</param>
    /// <param name="Linked">手柄是否真的连着（false = dongle 推的是"未连接占位帧"）。</param>
    private readonly record struct QueryResult(int Percent, bool Charging, bool Linked);

    /// <summary>
    /// 发一条状态查询并在窗口内等响应。写一次（长度/report ID 取自接口的报告描述符），
    /// 读窗口见 <see cref="Listen"/>。<paramref name="dev"/> 只用于决定"未连接"日志要不要打（避免每分钟刷屏）。
    /// </summary>
    private static QueryResult? Query(HidDeviceHandle handle, TimeSpan window, TrackedDevice? dev)
    {
        var plan = ResolvePlan(handle);
        if (plan == null) return null;
        var (reportId, length, hasReportId) = plan.Value;

        var frame = hasReportId
            ? BetopCodec.BuildQuery(length, reportId)
            : BetopCodec.BuildQueryWithoutReportId(length);

        if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
        {
            Log.Info($"北通 查询写失败（report_id=0x{reportId:X2} len={length}）: {handle.Info.DevicePath}");
            return null;
        }

        return Listen(handle, window, dev);
    }

    /// <summary>T9 流 250Hz：开机瞬态低值只存在 29~218 ms，同一读数须稳定这么久才采信（betop.md §1.4.3）。</summary>
    private static readonly TimeSpan T9SettleWindow = TimeSpan.FromMilliseconds(500);

    private sealed class SettleState
    {
        public int Percent;
        public DateTimeOffset Since = DateTimeOffset.MinValue;
    }

    private static readonly ConcurrentDictionary<string, SettleState> SettleByPath = new();

    /// <summary>该读数是否已稳定（换值重新计时）。只用于被动推流；探测/轮询每次只有一帧，判不了。</summary>
    internal static bool IsSettled(string path, int percent) => IsSettled(path, percent, DateTimeOffset.UtcNow);

    /// <summary><paramref name="now"/> 可注入：单测不必 sleep 真实时间。</summary>
    internal static bool IsSettled(string path, int percent, DateTimeOffset now)
    {
        var state = SettleByPath.GetOrAdd(path, _ => new SettleState());
        lock (state)
        {
            if (state.Since == DateTimeOffset.MinValue || state.Percent != percent)
            {
                state.Percent = percent;
                state.Since = now;
                return false;
            }
            return now - state.Since >= T9SettleWindow;
        }
    }

    /// <summary>
    /// 纯读窗口：不发任何帧，窗口内持续收 dongle 自推的帧直到解析成功
    /// （广播通道全程只走这里；查询路径在写完后也走这里等响应）。返回 null = 窗口内没读到可识别帧。
    /// </summary>
    private static QueryResult? Listen(HidDeviceHandle handle, TimeSpan window, TrackedDevice? dev)
    {
        var deadline = DateTime.UtcNow + window;
        int logged = 0;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            // 有些厂商集合是 250Hz 的持续流（手柄状态），逐帧打日志会刷爆 app.log——只留前几帧。
            if (logged < 4)
            {
                Log.Debug($"北通 收到报文: {Log.Hex(input)}");
                logged++;
            }
            else if (logged == 4)
            {
                Log.Info("北通 该接口在持续推流（非状态响应），后续报文不再逐条记录");
                logged++;
            }

            var parsed = BetopCodec.ParseStatus(input);
            if (parsed.HasValue)
            {
                Log.Info($"北通 状态报告: 电量={parsed.Value.Percent}% 充电={parsed.Value.Charging} 原始={Log.Hex(input)}");
                return new QueryResult(parsed.Value.Percent, parsed.Value.Charging, Linked: true);
            }

            // T9 不响应 0x15 查询，改解析 dongle 在 Col04 上持续推的状态流（byte23 = 电量）。
            var t9 = BetopCodec.ParseT9Status(input);
            if (t9.HasValue)
            {
                if (!t9.Value.Linked)
                {
                    // 占位帧：手柄并不在线。只在"第一次发现"或"从在线掉到离线"时记一行，避免刷屏。
                    if (dev == null || dev.Status.IsOnline)
                        Log.Info("北通 T9 手柄未连接（dongle 占位帧/空帧，手柄已关机或未配对；dongle 仍在）: " +
                                 Log.Hex(input));
                    return new QueryResult(0, Charging: false, Linked: false);
                }

                Log.Info($"北通 T9 状态流: 电量={t9.Value.Percent}% 原始={Log.Hex(input)}");
                return new QueryResult(t9.Value.Percent, t9.Value.Charging, Linked: true);
            }
        }
        return null;
    }

    /// <summary>查询参数（report ID / 写长度 / 是否带 report ID 前缀），按接口路径缓存。</summary>
    private readonly record struct QueryPlan(byte ReportId, int Length, bool HasReportId);

    private static readonly ConcurrentDictionary<string, QueryPlan> PlanByPath = new();

    /// <summary>
    /// 从接口的 HID caps 解析出真实 report ID 与写入长度。
    /// **report ID 由设备/型号自己的描述符决定**，硬编码必然写失败——HID 驱动对该接口没有的
    /// report ID 直接报 ERROR_INVALID_FUNCTION。
    /// </summary>
    private static QueryPlan? ResolvePlan(HidDeviceHandle handle)
    {
        var path = handle.Info.DevicePath;
        if (PlanByPath.TryGetValue(path, out var cached)) return cached;

        var rid = handle.GetOutputReportId();
        if (rid == null)
        {
            Log.Info($"北通 读不到输出 report ID: {path}");
            return null;
        }

        // HidP_GetCaps 的 OutputReportByteLength 已含 report ID 那一字节（T9 Col04 实测 16 = 15+1）。
        int length = handle.Info.OutputReportLength >= 2 ? handle.Info.OutputReportLength : FallbackWriteLength;

        bool hasReportId = rid.Value != 0;   // 0 = 该接口不使用 report ID，报文首字节即数据
        var plan = new QueryPlan(rid.Value, length, hasReportId);
        Log.Info($"北通 接口参数: report_id=0x{rid.Value:X2} 写长度={length} 带ID={hasReportId}");
        PlanByPath[path] = plan;
        return plan;
    }

    /// <summary>充电标志非 0 = 充电中；否则 100% 判充满、其余放电（充电时手柄自报 100%，故先看标志）。</summary>
    /// <summary>充电标志只有"是否充电"一档：收尾规则（未充电且 100% ⇒ 已充满）见 <see cref="ChargingStateMap"/>。</summary>
    private static ChargingState MapCharging(int percent, bool charging)
        => ChargingStateMap.FromChargingFlag(charging, percent);

    /// <summary>插件被禁用时顺手复位稳定判定表（Detach 幂等，禁用期间每轮都会被调，见 CR-22）。</summary>
    public override void Detach()
    {
        base.Detach();
        SettleByPath.Clear();
    }

    /// <summary>鲲鹏20 组定死组名（两通道产品串不同会互相顶名）；组外按产品串收拾成「北通 &lt;型号&gt;」。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        if (BetopDeviceIdentity.KunpengGroups.ContainsKey(info.ProductId))
            return "北通 鲲鹏20";

        var model = BetopCodec.CleanProductName(info.Product);
        return string.IsNullOrEmpty(model) ? $"北通手柄 ({info.VidPid})" : $"北通 {model}";
    }
}
