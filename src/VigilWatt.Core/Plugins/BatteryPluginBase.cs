using VigilWatt.Core.Devices;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins;

/// <summary>
/// 功 能： "接口认领"型插件的基类：一台设备 = 一个长持有的 HID 接口句柄。
/// 2026/10/06 15:53:36 xliu
/// 链路全貌见 docs/project/code-flow.md §4。
/// </summary>
public abstract class BatteryPluginBase : IBatteryPlugin
{
    public abstract string Name { get; }
    public abstract string DisplayName { get; }
    public abstract string DisplayNameEn { get; }

    /// <summary>本插件处理的设备类型（UI 选图标用）。</summary>
    public abstract DeviceKind Kind { get; }

    /// <summary>新建设备时的来源。默认 2.4G；蓝牙 HID 插件自行覆写。</summary>
    protected virtual DeviceSource Source => DeviceSource.Hid24G;

    /// <summary>
    /// 见 <see cref="IBatteryPlugin.SupportsFastDiscovery"/>：是否参与 DeviceManager 的 15 秒快速发现节拍。
    /// 默认 false（会写帧的插件保持默认值）；**只有纯被动零写入插件**才该覆盖成 true。
    /// </summary>
    public virtual bool SupportsFastDiscovery => false;

    /// <summary>
    /// 见 <see cref="IBatteryPlugin.SupportsDiscoveryProbe"/>：探测要写帧、但仍要参与 15 秒发现节拍。
    /// 覆盖成 true 时**必须同时把 <see cref="ProbeCooldown"/> 压到 15 秒以下**，否则冷却会让实际
    /// 重试间隔翻倍（每两拍才轮到一次探测）。
    /// </summary>
    public virtual bool SupportsDiscoveryProbe => false;

    /// <summary>基类默认不推送；被动监听与子类实时事件走 <see cref="RaiseStatusPushed"/>。</summary>
    public event Action<DeviceStatus>? StatusPushed;

    /// <summary>供子类/基类被动监听推送轮询外的状态更新。</summary>
    protected void RaiseStatusPushed(DeviceStatus status) => StatusPushed?.Invoke(status);

    /// <summary>认领时没有读数是否标离线。默认 true，避免只剩 dongle 时假装在线。</summary>
    protected virtual bool NullPercentMeansOffline => true;

    /// <summary>
    /// 认领成功后是否启动后台被动监听（只读不写）。
    /// 雷柏心跳 / 迈从 0A 推送 / 北通 T9 流 等覆盖为 true，并实现 <see cref="TryParsePassiveInput"/>。
    ///
    /// 它只描述**机制事实**（我起一个后台只读线程），不隐含"轮询空读不算通信失败"——
    /// 那是 <see cref="OfflinePolicy.CountPollFailures"/> 的事，两者**必须分开**。
    /// </summary>
    protected virtual bool SupportsPassiveListen => false;

    /// <summary>
    /// 本插件的离线判定策略（第③层配置：机制由基类实现，插件只填参数，每插件一行）。
    /// 第①层（协议事实）与第②层（安全声明）仍必须 per-brand，见 docs/project/code-flow.md §4.2。
    /// </summary>
    protected virtual OfflinePolicy Offline { get; } = OfflinePolicy.Default;

    /// <summary>
    /// 解析一条被动 input report。返回 null = 不是本协议帧；Offline=true = 设备明确离线。
    /// <b>必须零写入</b>。
    /// </summary>
    protected virtual (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input) => null;

    /// <summary>
    /// 探测失败后的冷却时间（期间不再探测该接口）。
    ///
    /// 取值必须**明显小于轮询周期（60 秒）**：失败发生在轮询开始后几秒处，若冷却等于一整轮，
    /// 下一拍会被判"冷却未过"而白白跳过，实际重试间隔翻倍（雷柏/北通都踩过这个相位坑）。
    /// 默认 30 秒 = "每一轮都允许重探"；只有代价真的高、且不指望短期恢复的插件才调大。
    /// </summary>
    protected virtual TimeSpan ProbeCooldown => TimeSpan.FromSeconds(30);

    /// <summary>
    /// 熔断停写期间，两次"半开探活"的最小间隔（见 <see cref="NudgeHalfOpen"/>）。
    ///
    /// 为什么要这条独立通道：递增退避（30s→1m→2m→4m→5m）只该约束**正式轮询**的打扰频率，
    /// 而"设备睡醒了没有"是另一件事。只有退避一条路径时，设备每睡一次就把档位顶高一档，
    /// 用户真去用鼠标时正好撞在 4~5 分钟的退避里，看到的就是"电量卡死、得手动刷新"
    /// （2026-10-27 实机日志：18:51:33 熔断第 4 次 240 秒 → 18:56:02 才恢复）。
    ///
    /// 默认 <see cref="TimeSpan.Zero"/> = 关闭半开探活，行为与旧版完全一致——这条通道只对
    /// "纯查询型、且设备会频繁短睡"的插件有意义（目前是 ATK 鼠标/键盘），别默认给所有人开。
    /// </summary>
    protected virtual TimeSpan BreakerProbeInterval => TimeSpan.Zero;

    /// <summary>
    /// 是否允许向设备写探测/查询帧。**唯一授权入口是插件开关**（<c>AppSettings.PluginEnabled[Name]</c>）——
    /// 开插件即授权它工作所需的写入，不再有第二个"写入许可"开关（safety.md §3 规则 2）。
    /// 这里读插件开关而非直接返回 true，是给写点留一道纵深防御（R1：写入必须能被一个开关挡住）。
    /// 零写入插件（雷柏）覆盖为 false。
    /// </summary>
    protected virtual bool WritesEnabled => IsWritesEnabled(Name);

    /// <summary><see cref="WritesEnabled"/> 的静态形式：插件的探测/轮询辅助方法常写成 static，也需要读它。</summary>
    protected static bool IsWritesEnabled(string pluginName) =>
        AppSettings.Load().PluginEnabled.TryGetValue(pluginName, out var on) && on;

    /// <summary>设备 ID：插件名 + 设备路径哈希。认领、黑名单、上报三处必须同一套规则，故只此一处生成。</summary>
    protected virtual string IdFor(string devicePath) => $"{Name.ToLowerInvariant()}:{PluginUtils.StableDeviceHash(devicePath)}";

    /// <summary>
    /// 设备 ID 的设备维度形式：默认仍走 <see cref="IdFor"/>（路径哈希）。
    /// 需要芯片/机身身份的插件覆写它，换 USB 口才不会重复建条目。
    /// </summary>
    protected virtual string BuildDeviceId(HidDeviceInfo info) => IdFor(info.DevicePath);

    /// <summary>句柄维度：需要读芯片/机身标识的插件覆写这一个（默认退化成设备维度）。</summary>
    protected virtual string BuildDeviceId(HidDeviceHandle handle) => BuildDeviceId(handle.Info);

    /// <summary>打开句柄：测试覆写注入假句柄，生产路径走 <see cref="HidDeviceInfo.Open"/>。</summary>
    protected virtual HidDeviceHandle OpenDevice(HidDeviceInfo info) => info.Open();

    /// <summary>同一插件里不同型号代价不同时覆写（迈从鼠标 60 秒、键盘 10 秒）。</summary>
    protected virtual TimeSpan ResolveProbeCooldown(HidDeviceInfo info) => ProbeCooldown;

    /// <summary>
    /// <see cref="Probe"/> 是否会向设备写帧：写帧的插件（迈从会话帧 / 北通查询）保持默认 true，
    /// 零写入探测（雷柏、英菲克纯被动）置 false。这是**机制事实**声明，与用户开关无关。
    /// </summary>
    protected virtual bool ProbeWrites => true;

    /// <summary>仅走 Feature 报告时不申请输入读写权限，避免被鼠标集合的系统保护拒绝。</summary>
    protected virtual bool FeatureOnly(HidDeviceInfo info) => false;

    protected sealed class TrackedDevice
    {
        public required HidDeviceInfo Info;
        public required HidDeviceHandle Handle;
        public required DeviceStatus Status;
        public required string Protocol;
        public int ConsecutiveFailures;
        public bool WritesDisabled;
        public DateTimeOffset WritesDisabledAt;
        /// <summary>熔断累计次数 = 递增退避的档位（拿到应答即归零）。</summary>
        public int BreakerTrips;
        /// <summary>熔断期内"事件驱动核实"的最近一次放行时刻（限制放行频率）。</summary>
        public DateTimeOffset LastBypassAt;
        /// <summary>本轮核实正在放行写入（绕过熔断位，但不改熔断状态）。</summary>
        public bool BypassActive;
        /// <summary>本轮是"半开探活"（只为发现设备睡醒）：失败不记失败次数、不升退避档位。</summary>
        public bool HalfOpenProbe;
        /// <summary>每句柄一次性的动作标记（会话握手等），子类自行使用。</summary>
        public bool HandshakeSent;
        /// <summary>本设备的读数异常过滤器（基准 + 异常持续计时都封在里面）。</summary>
        public required BatteryReadingFilter Filter;
        /// <summary>子类在 PollDevice 里判定"设备已主动告知休眠/关机"时置位，基类随即标离线。</summary>
        public bool OfflineRequested;
        /// <summary>本轮刚由探测建立（探测返回值就是最新读数）：本轮 Poll 跳过、下一轮起正常轮询。</summary>
        public bool FreshlyClaimed;
        /// <summary>插件要求下一轮重新探测本接口（物理身份变了等）：先释放旧句柄与认领，再重探。</summary>
        public bool NeedsReprobe;
        /// <summary>与后台被动监听互斥，保护单悬空 ReadInput / 写读会话。</summary>
        public readonly object IoGate = new();
        /// <summary>最近一次被动监听收到**任何**报文的时间（用于沉默离线；不是"解析成功"的时间）。</summary>
        public DateTimeOffset LastPassiveAt;
        /// <summary>连续"读取立即返回 null"的次数：句柄失效的特征，用来触发退避（CR-75/76）。</summary>
        public int FastNullReads;
        /// <summary>
        /// 插件标记的"离线候选"起始时刻（<c>default</c> = 无候选）。候选持续超过
        /// <see cref="OfflinePolicy.Confirm"/> 才真正判离线。
        /// </summary>
        public DateTimeOffset OfflineCandidateSince;
        /// <summary>插件在被动帧里请求"立刻做一次核实查询"（见 <see cref="RequestConfirm"/>），由空闲 tick 消费。</summary>
        public bool ConfirmRequested;
        public CancellationTokenSource? ListenCts;
        public Task? ListenTask;
    }

    /// <summary>单个候选接口的探测产物（探测阶段只做 I/O，不碰非线程安全的字典）。</summary>
    private readonly record struct ProbeOutcome(
        HidDeviceInfo Info,
        HidDeviceHandle? Handle,
        (string Protocol, int? Percent, ChargingState Charging)? Result);

    /// <summary>熔断期内"事件驱动核实"的最小放行间隔：兜住设备狂推通知的极端情形，最坏约 1 帧/30 秒。</summary>
    private static readonly TimeSpan BypassMinInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 单次读数的最大合理变化幅度（百分点）：涨跌超过它就先判异常、本轮不采用。
    /// 起因：键盘休眠/关机瞬间 dongle 会推 0/1 之类伪读数。真掉电不会一分钟掉 20 点，
    /// 故该阈值只挡异常、不挡正常放电。判定逻辑见 <see cref="BatteryReadingFilter"/>。
    /// </summary>
    protected virtual int MaxPlausibleStepPercent => 20;

    /// <summary>默认过滤休眠占位值；协议确认 0/1 为真实电量时可放宽。</summary>
    protected virtual int MinValidPercent(HidDeviceInfo info) => 2;

    /// <summary>
    /// 异常读数需持续这么久才最终采信（期间读数一直偏离才算数）：既挡住休眠瞬间的伪读数，
    /// 又不会把真实的深度掉电永久锁死。
    /// </summary>
    protected virtual TimeSpan AnomalyPersistWindow => TimeSpan.FromMinutes(5);

    /// <summary>
    /// 把「同一物理设备的另一条接口」从本轮摘掉：插线后接收器与本体两条都在场，都认领会变成两行矛盾读数。
    /// 返回要跳过的接口路径；默认不跳过。
    /// </summary>
    protected virtual IReadOnlyCollection<string> SkipPathsForThisPass(
        IReadOnlyList<HidDeviceInfo> availableDevices) => Array.Empty<string>();

    // 大小写不敏感：Windows 的 HID 路径会以两种大小写形式出现（HID#A / hid#a），
    // 当成两条会重复建条目、还会漏释放句柄。
    private readonly Dictionary<string, TrackedDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _probeFailedAt = new(StringComparer.OrdinalIgnoreCase);
    private DeviceClaimRegistry _claims = null!;

    public virtual async Task<IReadOnlyList<DeviceStatus>> AttachAsync(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
    {
        _claims = claims;
        var claimed = new List<DeviceStatus>();
        var availablePaths = availableDevices
            .Select(d => d.DevicePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 先释放"这一轮扫描里已经不在"的接口：同轮重连（旧路径消失、新路径出现）时，
        // 旧条目必须先下线，否则新接口会被当成重复设备、或者与旧条目并成一条。
        // 空扫描视为枚举失败（同 ReconcileRemovals），不动任何条目。
        if (availableDevices.Count > 0)
        {
            foreach (var path in _devices.Keys.ToList())
            {
                if (availablePaths.Contains(path)) continue;
                if (!_devices.Remove(path, out var gone)) continue;
                StopPassiveListen(gone);
                gone.Handle.Dispose();
                gone.Status.IsOnline = false;
                gone.Status.LastUpdated = DateTimeOffset.Now;
                _claims.Release(path, Name);
                Log.Info($"{DisplayName} 设备已移除: {path}");
                claimed.Add(gone.Status);
            }
        }

        // 同一物理设备的多接口合并：先释放被跳过的接口，否则它会一直留在列表里当第二行。
        var skipPaths = SkipPathsForThisPass(availableDevices);
        var skipSet = new HashSet<string>(skipPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in skipSet)
        {
            if (!_devices.Remove(path, out var dropped)) continue;
            StopPassiveListen(dropped);
            dropped.Handle.Dispose();
            dropped.Status.IsOnline = false;
            _claims.Release(path, Name);
            Log.Info($"{DisplayName} 同一设备的另一条接口已接管，释放: {path}");
        }

        // 第一遍（快）：已有设备直接返回；筛出本轮需要新探测的接口（冷却中/被他人认领的跳过）
        var toProbe = new List<HidDeviceInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in availableDevices)
        {
            if (!seenPaths.Add(info.DevicePath)) continue;   // 同一次扫描里的路径大小写别名
            if (skipSet.Contains(info.DevicePath)) continue;
            if (!IsCandidate(info)) continue;

            // 永久移除的设备：连句柄都不开、帧也不发。只挡 Upsert 的话，插件照样会写帧。
            if (DeviceBlocklist.IsBlocked(IdFor(info.DevicePath)))
            {
                if (_devices.Remove(info.DevicePath, out var blocked))
                {
                    StopPassiveListen(blocked);
                    blocked.Handle.Dispose();
                    _claims.Release(info.DevicePath, Name);
                    Log.Info($"{DisplayName} 设备已永久移除，释放接口: {info.DevicePath}");
                }
                continue;
            }

            if (_devices.TryGetValue(info.DevicePath, out var known) && !known.NeedsReprobe)
            {
                known.Info = info;   // 重扫后元数据可能更新
                claimed.Add(known.Status);
                continue;
            }

            // 插件要求重探（物理身份变了）：先放掉旧句柄与认领，同一路径稍后重新探测。
            if (_devices.Remove(info.DevicePath, out var stale))
            {
                StopPassiveListen(stale);
                stale.Handle.Dispose();
                stale.Status.IsOnline = false;
                stale.Status.LastUpdated = DateTimeOffset.Now;
                claimed.Add(stale.Status);
                _claims.Release(info.DevicePath, Name);
            }

            if (_probeFailedAt.TryGetValue(info.DevicePath, out var failedAt) &&
                DateTimeOffset.Now - failedAt < ResolveProbeCooldown(info))
                continue;

            if (!claims.TryClaim(info.DevicePath, Name)) continue;   // 已被其他插件认领

            toProbe.Add(info);
        }

        // 第二遍（慢）：本插件内逐个探测；与其它插件的 AttachAsync 由 DeviceManager 并行。
        if (toProbe.Count > 0)
        {
            var outcomes = new ProbeOutcome[toProbe.Count];
            for (int i = 0; i < toProbe.Count; i++)
                outcomes[i] = await RunProbeAsync(toProbe[i]).ConfigureAwait(false);
            CommitProbes(outcomes, claimed);
        }

        // 已拔出的设备
        claimed.AddRange(ReconcileRemovals(availableDevices));
        return claimed;
    }

    /// <summary>只拆"扫描结果里已经没有"的设备：关句柄、停监听、释放认领，不探测也不写帧。</summary>
    public IReadOnlyList<DeviceStatus> ReconcileRemovals(IReadOnlyList<HidDeviceInfo> availableDevices)
    {
        // 空扫描 = CM 枚举失败，不是"设备全拔了"。当成全部拔出会让所有条目瞬间离线、句柄全关，
        // 下一轮再重认领——一次 API 抖动变成全局抖动，且现场很难查。
        if (availableDevices.Count == 0) return Array.Empty<DeviceStatus>();

        var gone = new List<DeviceStatus>();
        foreach (var path in _devices.Keys.ToList())
        {
            if (availableDevices.Any(d => StringComparer.OrdinalIgnoreCase.Equals(d.DevicePath, path))) continue;
            if (_devices.Remove(path, out var dev))
            {
                StopPassiveListen(dev);
                dev.Handle.Dispose();
                dev.Status.IsOnline = false;
                _claims.Release(path, Name);
                Log.Info($"{DisplayName} 设备已移除: {path}");
                // DeviceManager 只收返回列表；不上报则拔出后条目永远在线。
                gone.Add(dev.Status);
            }
        }
        return gone;
    }

    /// <summary>打开句柄并跑协议探测。成功返回持有句柄 + 探测值；失败/异常返回空句柄并已释放。</summary>
    private async Task<ProbeOutcome> RunProbeAsync(HidDeviceInfo info)
    {
        // 探测会写帧、而写入许可被关：跳过探测，一帧都不发（安全规则 R1，见 docs/hardware/safety.md §3）。
        if (ProbeWrites && !WritesEnabled)
            return new ProbeOutcome(info, null, null);

        HidDeviceHandle? handle = null;
        try
        {
            handle = FeatureOnly(info) ? info.OpenFeature() : OpenDevice(info);
            var probe = await ProbeAsync(handle).ConfigureAwait(false);
            if (probe == null)
            {
                handle.Dispose();
                return new ProbeOutcome(info, null, null);
            }
            return new ProbeOutcome(info, handle, probe);
        }
        catch (UnauthorizedAccessException)
        {
            handle?.Dispose();
            return new ProbeOutcome(info, null, null);
        }
        catch (Exception ex)
        {
            Log.Error($"{DisplayName} 探测异常: {info.DevicePath}", ex);
            handle?.Dispose();
            return new ProbeOutcome(info, null, null);
        }
    }

    /// <summary>发现路径用可等待探测；默认把同步 <see cref="Probe"/> 丢到线程池，避免堵死其它插件的 await。</summary>
    protected virtual Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(HidDeviceHandle handle)
        => Task.Run(() => Probe(handle));

    /// <summary>落地探测结果（写字典 + 发认领）。</summary>
    /// <summary>同一物理身份已挂在别的路径下时返回那条路径（用于释放重复接口）。</summary>
    private string? ExistingPathOfSameDevice(string id, string path)
    {
        foreach (var kv in _devices)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(kv.Key, path)) continue;
            if (string.Equals(kv.Value.Status.Id, id, StringComparison.Ordinal)) return kv.Key;
        }
        return null;
    }

    private void CommitProbes(ProbeOutcome[] outcomes, List<DeviceStatus> claimed)
    {
        foreach (var o in outcomes)
        {
            if (o.Result is not { } probe)
            {
                _claims.Release(o.Info.DevicePath, Name);
                _probeFailedAt[o.Info.DevicePath] = DateTimeOffset.Now;
                continue;
            }

            var (protocol, percent, charging) = probe;

            // 设备 ID 可能由插件按物理身份算（读芯片 ID 会失败）：失败就当这一轮探测没成，
            // 句柄与认领都要还回去，否则会留下一条没有 ID 的设备和一个占着接口的句柄。
            string id;
            try
            {
                id = BuildDeviceId(o.Handle!);
            }
            catch (Exception ex)
            {
                Log.Warn($"{DisplayName} 设备 ID 读取失败，本轮放弃: {o.Info.DevicePath}（{ex.Message}）");
                o.Handle!.Dispose();
                _claims.Release(o.Info.DevicePath, Name);
                _probeFailedAt[o.Info.DevicePath] = DateTimeOffset.Now;
                continue;
            }

            // 同一物理设备的另一条接口（插线后接收器 + 本体同时在场）：保留先认领的那条，
            // 后来的把句柄与认领都还回去，避免列表里出现两行矛盾读数。
            if (ExistingPathOfSameDevice(id, o.Info.DevicePath) is { } duplicateOf)
            {
                Log.Info($"{DisplayName} 同一设备的另一条接口已认领，释放: {o.Info.DevicePath}（同 {duplicateOf}）");
                o.Handle!.Dispose();
                _claims.Release(o.Info.DevicePath, Name);
                continue;
            }

            // 同一台设备在本轮里先下线又在新路径上认领回来（同轮重连）：只报新的一条，
            // 别让上层看到"离线 → 在线"两条同 ID 的快照。
            claimed.RemoveAll(d => string.Equals(d.Id, id, StringComparison.Ordinal));

            var status = new DeviceStatus
            {
                Id = id,
                Name = BuildDisplayName(o.Info),
                Source = Source,
                Kind = ResolveKind(protocol),
                Protocol = protocol,
                BatteryPercent = percent,
                Charging = charging,
                // 无读数默认离线。子类把 NullPercentMeansOffline 覆写为 false 时保持在线。
                IsOnline = percent.HasValue || !NullPercentMeansOffline,
            };
            var dev = new TrackedDevice
            {
                Info = o.Info, Handle = o.Handle!, Status = status, Protocol = protocol,
                Filter = CreateFilter(percent, o.Info), FreshlyClaimed = true,
                // 无读数但仍在线也要记下时刻，否则静音兜底永不触发。
                LastPassiveAt = status.IsOnline ? DateTimeOffset.Now : default,
            };
            _devices[o.Info.DevicePath] = dev;
            if (SupportsPassiveListen)
                StartPassiveListen(dev);
            Log.Info($"认领{DisplayName}设备: {o.Info} proto={protocol} 电量={percent} 充电={charging}");
            claimed.Add(status);
        }
    }

    private void StartPassiveListen(TrackedDevice dev)
    {
        if (dev.ListenTask != null) return;
        dev.ListenCts = new CancellationTokenSource();
        var cts = dev.ListenCts;
        dev.ListenTask = Task.Run(() =>
        {
            Log.Info($"{DisplayName} 被动监听已启动: {dev.Status.DisplayName}");
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    byte[]? input;
                    var started = Environment.TickCount64;
                    lock (dev.IoGate)
                        input = dev.Handle.ReadInput(TimeSpan.FromMilliseconds(500));
                    if (input == null)
                    {
                        // 正常静音要等满 500ms；连续几次几十毫秒就返回说明句柄已失效（ReadInput 立即 null）。
                        // 不退避的话这个 while 会变成每秒几万次空转、烧满一个核（CR-75/76）。
                        dev.FastNullReads = Environment.TickCount64 - started < 100 ? dev.FastNullReads + 1 : 0;
                        if (dev.FastNullReads >= 3) Thread.Sleep(200);

                        // 静音期不是无事可做：延迟离线确认、半开重试与"立刻核实"靠的都是这里的节拍（约 500ms 一次）。
                        CheckOfflineCandidate(dev);
                        CheckPassiveSilence(dev);
                        MaybeHalfOpenBreaker(dev);
                        RunPendingConfirm(dev);
                        continue;
                    }
                    dev.FastNullReads = 0;

                    // 收到**任何**报文都算"设备还活着"，不只是本协议能解析出来的帧（雷柏的活动心跳
                    // 0x07 一停手就静音，同一接口上还有大量 0x00/0x04/0x05 报文）。
                    dev.LastPassiveAt = DateTimeOffset.Now;

                    var parsed = TryParsePassiveInput(dev, input);
                    if (parsed == null) continue;

                    var (percent, charging, offline) = parsed.Value;
                    if (offline)
                    {
                        // 零歧义证据（如北通 T9 占位帧）：直接标离线，不等延迟确认窗口。
                        // RequestOffline 顺带让下一轮 Poll 清零失败计数，避免离线期间把失败攒到熔断。
                        RequestOffline(dev);
                        MarkOffline(dev, "被动帧告知离线");
                        continue;
                    }

                    if (TryApplyReading(dev, percent, charging, push: true))
                        Log.Info($"{DisplayName} 被动更新: {dev.Status.DisplayName} {percent}% {charging}");
                }
                catch (Exception ex)
                {
                    if (!cts.IsCancellationRequested)
                        Log.Warn($"{DisplayName} 被动监听异常: {ex.Message}");
                }
            }
        }, cts.Token);
    }

    private void StopPassiveListen(TrackedDevice dev)
    {
        try { dev.ListenCts?.Cancel(); } catch { /* ignore */ }
        try { dev.ListenTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
        dev.ListenCts?.Dispose();
        dev.ListenCts = null;
        dev.ListenTask = null;
    }

    /// <summary>
    /// 把设备标为离线——**插件内软离线（休眠/静音/占位帧）的唯一落地点**：翻 IsOnline、盖时间戳、记日志、立即推送。
    /// 拔出走 <see cref="ReconcileRemovals"/>：它不该盖时间戳（最后在线时刻就是那个值）。
    /// <b>幂等</b>：已经离线就什么都不做并返回 false。返回值只表示"本次真的翻转了"。
    /// </summary>
    private bool MarkOffline(TrackedDevice dev, string reason)
    {
        if (!dev.Status.IsOnline) return false;

        dev.Status.IsOnline = false;
        dev.Status.LastUpdated = DateTimeOffset.Now;
        Log.Info($"{DisplayName} {dev.Status.DisplayName} {reason}");
        RaiseStatusPushed(dev.Status);
        return true;
    }

    /// <summary>
    /// 被动监听空闲 tick 的延迟离线确认：候选持续超过 <see cref="OfflinePolicy.Confirm"/> 就标离线。
    /// 与"沉默兜底"（<see cref="OfflinePolicy.Silence"/>）是两条独立判据，谁先满足谁生效。
    /// 恢复在线不必在这里做——<see cref="TryApplyReading"/> 会自己把 IsOnline 翻回来。
    /// </summary>
    private void CheckOfflineCandidate(TrackedDevice dev)
    {
        if (!Offline.IsConfirmExpired(dev.OfflineCandidateSince, DateTimeOffset.Now)) return;
        MarkOffline(dev, $"被动离线候选持续 {Offline.Confirm.TotalSeconds:0}s，标记离线");
    }

    /// <summary>被动监听空闲节拍检查静默离线，避免等待 60 秒主轮询造成最多一整轮的额外延迟。</summary>
    private void CheckPassiveSilence(TrackedDevice dev)
    {
        if (Offline.Silence <= TimeSpan.Zero || dev.LastPassiveAt == default) return;
        if (DateTimeOffset.Now - dev.LastPassiveAt <= Offline.Silence) return;
        MarkOffline(dev, $"被动沉默超过 {Offline.Silence.TotalSeconds:0}s，标记离线");
    }

    /// <summary>
    /// 消费插件在被动帧里提交的"立刻核实"请求（见 <see cref="RequestConfirm"/>）：空闲 tick 上跑一次
    /// 与轮询同款的查询，插件照常自己报账。必须在这条路径上做——只有它是秒级节拍。
    /// **熔断期内也照跑**：它是设备自发信号触发的、不是轮询重试，挡住它等于把唤醒信号丢掉（CR-53）；
    /// 放行频率由 <see cref="BypassMinInterval"/> 兜住。
    /// </summary>
    private void RunPendingConfirm(TrackedDevice dev)
    {
        if (!dev.ConfirmRequested) return;

        // 未到最小间隔就**不消费请求**，留到下一拍再试——消费掉就等于丢信号。
        bool bypass = dev.WritesDisabled;
        if (bypass && DateTimeOffset.Now - dev.LastBypassAt < BypassMinInterval) return;

        dev.ConfirmRequested = false;
        if (bypass) dev.LastBypassAt = DateTimeOffset.Now;

        int failuresBefore = dev.ConsecutiveFailures;
        (int? Percent, ChargingState Charging)? read;
        // BypassActive 是跨线程共享的可变字段（60 秒轮询线程也会经 CanWrite 读它），故与 PollDevice 同锁读写（R3）。
        lock (dev.IoGate)
        {
            dev.BypassActive = bypass;
            try { read = PollDevice(dev); }
            finally { dev.BypassActive = false; }
        }

        if (read.HasValue)
        {
            if (TryApplyReading(dev, read.Value.Percent, read.Value.Charging, push: true))
                Log.Info($"{DisplayName} 核实查询确认在线: {dev.Status.DisplayName}");
        }
        else if (dev.ConsecutiveFailures > failuresBefore)
        {
            MarkOffline(dev, "核实查询无应答，判定离线");
        }

        ConsumeOfflineRequest(dev);
    }

    /// <summary>
    /// 退避期满就**立刻**排一次核实，而不是苦等下一个 60 秒轮询拍——否则"退避 5 分钟"实际会变成
    /// "5 分钟 + 最多 60 秒"。只置触发器，发帧与熔断解除仍走 <see cref="RunPendingConfirm"/>。
    /// </summary>
    private void MaybeHalfOpenBreaker(TrackedDevice dev)
    {
        if (!dev.WritesDisabled || dev.ConfirmRequested) return;
        if (!WriteBackoff.IsExpired(dev.BreakerTrips, dev.WritesDisabledAt, DateTimeOffset.Now)) return;
        RequestConfirm(dev);
    }

    /// <summary>
    /// 熔断期的"半开探活"：由 15 秒发现节拍驱动（<see cref="Devices.DeviceManager"/>），对每个停写退避中的
    /// 设备按 <see cref="BreakerProbeInterval"/> 节流地发**一帧**核实查询。
    ///
    /// 与 <see cref="RunPendingConfirm"/> 的分工：那条是"设备自发报文触发的核实"，只有开了被动监听的插件
    /// 才有节拍可跑；这条给**纯查询型插件**（ATK 鼠标、罗技等）用，它们没有被动监听线程，
    /// 退避期满前完全感知不到设备已经睡醒。两条都走 <see cref="HalfOpenProbe"/> 语义：
    /// **失败不记账、不升档**——设备在睡是预期结果。
    /// </summary>
    public void NudgeHalfOpen()
    {
        if (BreakerProbeInterval <= TimeSpan.Zero) return;

        foreach (var dev in _devices.Values)
        {
            if (!dev.WritesDisabled) continue;   // 没熔断就不用探活，正常轮询会覆盖
            if (DateTimeOffset.Now - dev.LastBypassAt < BreakerProbeInterval) continue;

            // 有被动监听线程的插件：置标志交给它 500ms 的空闲 tick，别在这里跟它抢 IoGate
            if (dev.ListenTask != null)
            {
                dev.ConfirmRequested = true;
                continue;
            }
            RunHalfOpenProbe(dev);
        }
    }

    /// <summary>半开探活的执行体：一帧核实，拿到应答就由插件自己的 <c>ReportPollAnswered</c> 解除熔断。</summary>
    private void RunHalfOpenProbe(TrackedDevice dev)
    {
        dev.LastBypassAt = DateTimeOffset.Now;

        (int? Percent, ChargingState Charging)? read;
        lock (dev.IoGate)
        {
            dev.HalfOpenProbe = true;
            try { read = PollDevice(dev); }
            finally { dev.HalfOpenProbe = false; }
        }

        if (!read.HasValue) return;   // 还没醒：保持离线与退避档位，等下一次探活

        if (TryApplyReading(dev, read.Value.Percent, read.Value.Charging, push: true))
            Log.Info($"{DisplayName} 半开探活成功: {dev.Status.DisplayName} 恢复在线");
        ConsumeOfflineRequest(dev);
    }

    /// <summary>
    /// 消费插件通过 <see cref="RequestOffline"/> 提交的"设备已休眠/关机"标记：立即标离线。
    /// 这是零歧义证据（如北通 T9 占位帧），**不计入通信失败**。
    /// </summary>
    private bool ConsumeOfflineRequest(TrackedDevice dev)
    {
        if (!dev.OfflineRequested) return false;
        dev.OfflineRequested = false;
        dev.ConsecutiveFailures = 0;
        MarkOffline(dev, "收到设备休眠/关机通知，立即标记离线");
        return true;
    }

    public IReadOnlyList<DeviceStatus> Poll()
    {
        var updated = new List<DeviceStatus>();
        foreach (var dev in _devices.Values)
        {
            try
            {
                // 本轮刚由探测建立：探测返回值就是最新读数，不再立刻重读一遍（首扫提速），只跳过一轮。
                if (dev.FreshlyClaimed)
                {
                    dev.FreshlyClaimed = false;
                    continue;
                }

                // 熔断停写中且探活间隔到了：本轮当"半开探活"发（失败不记账，档位不会被顶高）。
                // 没有这一步，纯查询型插件（ATK 鼠标）在退避期内一帧都不发，
                // 设备睡醒后最长要干等 5 分钟才被重新读到。
                bool halfOpen = dev.WritesDisabled
                                && BreakerProbeInterval > TimeSpan.Zero
                                && DateTimeOffset.Now - dev.LastBypassAt >= BreakerProbeInterval;

                (int? Percent, ChargingState Charging)? read;
                lock (dev.IoGate)
                {
                    if (halfOpen) dev.LastBypassAt = DateTimeOffset.Now;
                    dev.HalfOpenProbe = halfOpen;
                    try { read = PollDevice(dev); }
                    finally { dev.HalfOpenProbe = false; }
                }

                // 设备主动告知休眠/关机（协议层明确帧）：立即标离线，不等 3 次查询失败（那要 3 分钟）。
                if (ConsumeOfflineRequest(dev)) continue;

                if (read.HasValue)
                {
                    if (TryApplyReading(dev, read.Value.Percent, read.Value.Charging, push: false))
                        updated.Add(dev.Status);
                }
                else
                {
                    // 判据一：沉默兜底。心跳类协议（雷柏）接口彻底静音过久 → 离线（正常停手不算）。
                    if (Offline.Silence > TimeSpan.Zero &&
                        dev.LastPassiveAt != default &&
                        DateTimeOffset.Now - dev.LastPassiveAt > Offline.Silence)
                    {
                        if (MarkOffline(dev, $"被动沉默超过 {Offline.Silence.TotalSeconds:0}s，标记离线"))
                            updated.Add(dev.Status);
                    }

                    // 判据二：查询失败熔断。只给"轮询确实会发帧、且 null 就是失联"的插件开。
                    if (Offline.CountPollFailures)
                    {
                        dev.ConsecutiveFailures++;
                        // 只在首次跨过阈值时熔断：这里分不清"没应答"与"被熔断挡住没发帧"，
                        // 每轮都重新起算会把退避永远推后（见 TripBreaker 的注释）。
                        if (dev.ConsecutiveFailures >= Offline.MaxFailures && !dev.WritesDisabled)
                            TripBreaker(dev);
                        if (dev.ConsecutiveFailures >= Offline.MaxFailures &&
                            MarkOffline(dev, "连续查询失败，标记离线"))
                            updated.Add(dev.Status);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{DisplayName} 轮询异常: {dev.Status.DisplayName}", ex);
            }
        }
        return updated;
    }

    /// <summary>
    /// 统一应用读数（过滤 + 更新字段）。<paramref name="push"/> = true 时若有变化则 RaiseStatusPushed。
    /// 返回是否产生了应对外可见的状态变化。
    /// </summary>
    private bool TryApplyReading(TrackedDevice dev, int? percent, ChargingState charging, bool push)
    {
        if (percent.HasValue)
        {
            int baseline = dev.Filter.LastAcceptedPercent;
            var verdict = dev.Filter.Evaluate(percent.Value, DateTimeOffset.Now);
            if (verdict is ReadingVerdict.RejectAnomaly or ReadingVerdict.InvalidValue)
            {
                Log.Warn($"{DisplayName} {dev.Status.DisplayName} 读数异常已过滤：" +
                         $"{baseline}%→{percent}%，本轮不采用" +
                         (verdict == ReadingVerdict.InvalidValue
                             ? $"（数值无效，低于 {dev.Filter.MinValidPercent}%）"
                             : $"（变化超过 {dev.Filter.MaxStepPercent} 点，需持续 " +
                               $"{dev.Filter.PersistWindow.TotalMinutes:0} 分钟才采信）"));
                return false;
            }
            if (verdict == ReadingVerdict.AcceptConfirmed)
            {
                Log.Warn($"{DisplayName} {dev.Status.DisplayName} 读数 {baseline}%→{percent}% " +
                         $"已持续 {dev.Filter.PersistWindow.TotalMinutes:0} 分钟，采信为真实变化");
            }
        }

        bool changed = dev.Status.BatteryPercent != percent
                    || dev.Status.Charging != charging
                    || !dev.Status.IsOnline;

        dev.Status.BatteryPercent = percent;
        dev.Status.Charging = charging;
        dev.Status.LastUpdated = DateTimeOffset.Now;
        if (!dev.Status.IsOnline)
        {
            dev.Status.IsOnline = true;
            Log.Info($"{DisplayName} 设备重新上线: {dev.Status.DisplayName}");
            changed = true;
        }
        dev.ConsecutiveFailures = 0;

        if (changed && push)
            RaiseStatusPushed(dev.Status);
        return changed;
    }

    public virtual void Detach()
    {
        // 插件被禁用时 DeviceManager 每轮都会调一次 Detach（CR-22）：手上没设备就别打日志，
        // 否则禁用期间每 60 秒刷一条"插件已卸载"的假日志。
        int released = _devices.Count;
        foreach (var dev in _devices.Values)
        {
            StopPassiveListen(dev);
            dev.Handle.Dispose();
        }
        _devices.Clear();
        _claims?.ReleaseAll(Name);
        if (released > 0)
            Log.Info($"{DisplayName} 插件已卸载，释放 {released} 台设备");
    }

    /// <summary>该设备当前是否允许写入（总开关 + 熔断退避 + 事件驱动放行）。子类发帧前检查；退避期满自动恢复一轮。</summary>
    protected bool CanWrite(TrackedDevice dev)
    {
        if (!WritesEnabled) return false;
        // 设备自己发信号触发的核实不受熔断限制、也不改熔断状态——挡住它等于把唤醒信号丢掉（CR-53）。
        if (dev.BypassActive) return true;
        // 半开探活同理：它的唯一目的就是发现"设备睡醒了"，被自己的熔断挡住就永远发现不了。
        if (dev.HalfOpenProbe) return true;
        if (!dev.WritesDisabled) return true;
        if (WriteBackoff.IsExpired(dev.BreakerTrips, dev.WritesDisabledAt, DateTimeOffset.Now))
        {
            dev.WritesDisabled = false;
            Log.Info($"{DisplayName} {dev.Status.DisplayName} 停写退避期满（{WriteBackoff.For(dev.BreakerTrips).TotalSeconds:0} 秒），恢复查询");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 子类在 <see cref="PollDevice"/> 里判定"设备已进入休眠/关机"时调用：基类立即把设备标离线，
    /// 不等 <see cref="OfflinePolicy.MaxFailures"/> 次查询失败（那要几分钟）。
    /// 只认协议层**零歧义**的离线证据（宁可漏判不可误判）：迈从 0x02 状态通知帧就因语义存疑被降级为仅记录。
    /// </summary>
    protected static void RequestOffline(TrackedDevice dev) => dev.OfflineRequested = true;

    /// <summary>
    /// 插件在被动帧里发现"值得立刻核实"的信号时调用。它是**触发器不是证据**：基类不会因此标离线，
    /// 只会立刻跑一次 <see cref="PollDevice"/> 去问设备——有应答就照常更新，没应答才由插件自己的
    /// 失败记账定性（见 <see cref="RunPendingConfirm"/>）。
    /// 典型用法（迈从）：状态通知 payload=0x00 语义未定论，不能当离线证据，但能把"键盘真关机了"
    /// 的发现从分钟级压到秒级。
    /// </summary>
    protected static void RequestConfirm(TrackedDevice dev) => dev.ConfirmRequested = true;

    /// <summary>请求下一轮重新探测本接口（物理身份变了、句柄身份不符等）：先释放旧句柄与认领，再重探。</summary>
    protected static void RequestReprobe(TrackedDevice dev) => dev.NeedsReprobe = true;

    /// <summary>
    /// 标记"疑似离线"，交给 <see cref="OfflinePolicy.Confirm"/> 做延迟确认。
    /// <b>幂等</b>：候选已经开始就不刷新起始时刻——否则持续抖动会把判定无限往后推。
    /// 与 <see cref="RequestOffline"/> 的分工：那个是零歧义证据立即生效，这个是有歧义的证据等一等。
    /// </summary>
    protected static void MarkOfflineCandidate(TrackedDevice dev)
    {
        if (dev.OfflineCandidateSince == default)
            dev.OfflineCandidateSince = DateTimeOffset.Now;
    }

    /// <summary>撤销"疑似离线"标记（收到恢复正常的证据时调用）。</summary>
    protected static void ClearOfflineCandidate(TrackedDevice dev)
        => dev.OfflineCandidateSince = default;

    /// <summary>
    /// 主动查询型插件在 <see cref="PollDevice"/> 里"**发过帧**但设备没应答"时调用：计入连续失败，
    /// 满 <see cref="OfflinePolicy.MaxFailures"/> 次则熔断停写 + 标离线。
    ///
    /// 需要这个入口是因为基类的自动记账（<see cref="OfflinePolicy.CountPollFailures"/>）不适用于所有协议：
    /// 雷柏根本没有查询命令、迈从的 null 又与"没开写入 / 熔断中"不可区分。这些插件置 false 后改由本
    /// 入口自己报账，否则失败计数永远是 0，既不标离线也不熔断。**只有插件知道这一轮到底发帧了没有**，
    /// 未开写入 / 熔断中 / 纯推送的分支提前 return 即可。
    /// </summary>
    protected void ReportPollFailure(TrackedDevice dev)
    {
        // 半开探活失败是**预期结果**（设备在睡），一记账就会每探活一次升一档，退避一路爬到 5 分钟，
        // 设备真醒了还要干等——这正是 2026-10-27 用户复报的"唤醒后电量不刷新"。
        if (dev.HalfOpenProbe) return;
        if (++dev.ConsecutiveFailures < Offline.MaxFailures) return;
        // 每次都重新起算退避并升档（含"熔断期内半开重试又失败"）：否则退避会永远停在首次的 30 秒，等于没有递增。
        TripBreaker(dev);
        RequestOffline(dev);
    }

    /// <summary>
    /// 熔断落地：停写 + 升档 + 记一条 WARN。基类 <see cref="Poll"/> 的自动记账分不清"没应答"与
    /// "被熔断挡住没发帧"，故那条路径只在首次跨过阈值时调它，否则退避会被每轮重置、永远不到期。
    /// </summary>
    private void TripBreaker(TrackedDevice dev)
    {
        dev.WritesDisabled = true;
        dev.WritesDisabledAt = DateTimeOffset.Now;
        dev.BreakerTrips++;
        Log.Warn($"{DisplayName} {dev.Status.DisplayName} 连续 {Offline.MaxFailures} 次查询失败，熔断停写" +
                 $"（第 {dev.BreakerTrips} 次，退避 {WriteBackoff.For(dev.BreakerTrips).TotalSeconds:0} 秒）");
    }

    /// <summary>主动查询拿到应答 → 清零失败计数与退避档位；熔断中则提前解除（设备已回来，没有理由继续停写）。</summary>
    protected void ReportPollAnswered(TrackedDevice dev)
    {
        dev.ConsecutiveFailures = 0;
        dev.BreakerTrips = 0;
        if (!dev.WritesDisabled) return;
        dev.WritesDisabled = false;
        Log.Info($"{DisplayName} {dev.Status.DisplayName} 查询恢复应答，提前解除熔断停写");
    }

    /// <summary>新建一台设备的读数过滤器，并用探测读到的值建立初始基准。</summary>
    private BatteryReadingFilter CreateFilter(int? probedPercent, HidDeviceInfo info)
    {
        var filter = new BatteryReadingFilter(MaxPlausibleStepPercent, AnomalyPersistWindow, MinValidPercent(info));
        if (probedPercent.HasValue) filter.Seed(probedPercent.Value);
        return filter;
    }

    /// <summary>
    /// 按协议细分设备类型（<see cref="DeviceStatus.Kind"/> 的最终值），默认 = <see cref="Kind"/>。
    /// 一个插件管多种设备的品牌（迈从的键 + 鼠）覆盖它；只在认领落地时调用，只影响图标。
    /// </summary>
    protected virtual DeviceKind ResolveKind(string protocol) => Kind;

    /// <summary>接口是否属于本插件（按 VID/usage/报告长度严格过滤——多滤不会漏设备，少滤可能碰别家硬件）。</summary>
    protected abstract bool IsCandidate(HidDeviceInfo info);

    /// <summary>协议探测：成功返回 (协议名, 电量, 充电状态)，失败返回 null（进入冷却）。</summary>
    protected abstract (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle);

    /// <summary>单设备轮询：返回本次读数；null = 失败/无数据。
    /// 基类已在 <see cref="TrackedDevice.IoGate"/> 内调用——子类写读同一句柄时不必再加锁。
    /// 开启被动监听的纯心跳协议可返回 null（由后台负责读数）。</summary>
    protected abstract (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev);

    /// <summary>默认显示名：产品字符串，空则用品牌名 + VID/PID。</summary>
    protected virtual string BuildDisplayName(HidDeviceInfo info)
        => string.IsNullOrEmpty(info.Product) ? $"{DisplayName} ({info.VidPid})" : info.Product;
}
