using System.Collections.Concurrent;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 罗技品牌插件：PRO X 2 使用基类 Centurion 流程；其它设备保留既有 HID++ 流程。
/// 2026/10/06 15:53:36 xliu
/// 2026/10/23 20:58:09 nyamu
/// 电量读取级联：Unified Battery 0x1004 → Battery Status 0x1000 → Battery Voltage 0x1001 → ADC Measurement 0x1F20 → HID++ 1.0 寄存器 0x07/0x0D（协议细节见 docs/hardware/logitech.md）。
/// </summary>
public sealed class LogitechPlugin : BatteryPluginBase, IBatteryPlugin
{
    public const ushort LogitechVid = 0x046D;
    public const ushort HidppUsagePage = 0xFF43;

    /// <summary>HID 规范的厂商自定义页（罗技 HID++ 集合实际多用这个页）。</summary>
    public const ushort VendorUsagePage = 0xFF00;

    /// <summary>发现阶段 ping 超时。本机 Bolt 实测醒着约 1.0s 才回（400ms 会假阴性）；睡着时 7 槽 × 1.5s ≈ 10s（旧版短接口 3s×7 ≈ 21s），可接受。</summary>
    private static readonly TimeSpan DiscoverTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>已发现至少一台后，空槽快速扫过的超时。</summary>
    private static readonly TimeSpan FastSlotTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>空槽 Ping 超时后的负缓存：冷却期内不再写、不等（CR-62）。</summary>
    private static readonly TimeSpan EmptySlotCooldown = TimeSpan.FromMinutes(5);

    /// <summary>DPI / 回报率刷新间隔：只有用户按 DPI 键才会变，没必要跟着电量节拍发请求。</summary>
    private static readonly TimeSpan PointerRefreshInterval = TimeSpan.FromMinutes(2);

    /// <summary>指针 feature 探测超时：不支持的 feature 要等满超时，压短以免拖慢整轮轮询（电量读仍是 3 秒）。</summary>
    private static readonly TimeSpan PointerProbeTimeout = TimeSpan.FromMilliseconds(600);

    /// <summary>指针 feature 的重试预算：休眠时首次探测会假阴性，探不到先重试，超预算才认定不支持。</summary>
    private const int PointerProbeAttempts = 5;

    /// <summary>插件开关的键，与 <c>AppSettings.PluginEnabled</c> 对应（插件开关即写入开关，见 <see cref="WritesEnabled"/>）。</summary>
    public const string SettingsKey = "Logitech";

    public override string Name => SettingsKey;
    public override string DisplayName => "罗技";
    public override string DisplayNameEn => "LOGITECH";

    /// <summary>罗技接收器同时管鼠标和键盘，按型号分不出来 → Unknown，UI 退回按名字猜。</summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    /// <summary>HID++ 探测要写帧（ping / GetFeature），不参与 15 秒快速发现节拍，避免写入频率上升。</summary>
    public override bool SupportsFastDiscovery => false;

    /// <summary>同理不参与：HID++ 的发现要 ping 7 个槽位（睡着时约 10 秒），每 15 秒跑一遍代价太高；
    /// 罗技靠自发的 HID++ 通知做实时更新，不需要加快发现节拍。</summary>
    public override bool SupportsDiscoveryProbe => false;

    /// <summary>写入许可 = **插件开关**（默认关）。罗技没有只读模式，"开着却关写入"只会让人以为坏了（CR-37）。
    /// 两个写点同一道闸门：HID++ 经 <c>HidppIO.WritesAllowed</c> 读它，Centurion 在 <c>WriteOutput</c> 前直接查它（CR-01）。</summary>
    protected override bool WritesEnabled => IsWritesEnabled(SettingsKey);

    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false, MaxFailures = 4 };

    /// <summary>熔断阈值：连续这么多次查询无应答就停写。</summary>
    private const int MaxConsecutiveFailures = 3;

    /// <summary>
    /// 接口打开失败的退避期（固定值，与查询失败的递增退避无关）。接口**仍在位但打不开**
    /// （被别的会话占着 / 无权限）时，失败不能变成"永不重试"——那会让设备插着也永远读不到，
    /// 只有物理拔出重插才恢复（CR-17）。最坏每 10 分钟重试一次，代价可忽略。
    /// </summary>
    private static readonly TimeSpan OpenRetryBackoff = TimeSpan.FromMinutes(10);

    /// <summary>是否该再试一次打开：从没失败过，或退避期已过。</summary>
    private static bool CanRetryOpen(ReceiverSession session) =>
        !session.OpenFailed || DateTimeOffset.Now - session.OpenFailedAt >= OpenRetryBackoff;

    private static void MarkOpenFailed(ReceiverSession session)
    {
        session.OpenFailed = true;
        session.OpenFailedAt = DateTimeOffset.Now;
    }

    /// <summary>判断接口是否为 HID++ 兼容 vendor 通道。HIDP_CAPS 字段序早前写反过（已修），且各家把 vendor 页放在 usage_page 还是 usage 上不一，故对 0xFF00 / 0xFF43 两个候选值在**两个字段里都查**，避免因字段序差异漏掉设备。</summary>
    public static bool IsHidppInterface(HidDeviceInfo info) =>
        info.VendorId == LogitechVid &&
        (info.UsagePage == HidppUsagePage || info.Usage == HidppUsagePage ||
         info.UsagePage == VendorUsagePage || info.Usage == VendorUsagePage);

    private static bool IsCenturionCandidate(HidDeviceInfo info)
        => CenturionCodec.IsSupportedInterface(info.VendorId, info.ProductId, info.UsagePage, info.Usage,
            info.InputReportLength, info.OutputReportLength);

    private readonly ConcurrentDictionary<string, ReceiverSession> _sessions = new();

    /// <summary>认领表：Attach 时记下来，<see cref="ReconcileRemovals"/> 没有 claims 参数也要能释放。</summary>
    private DeviceClaimRegistry? _claims;

    private sealed class DevEntry
    {
        public required DeviceStatus Status;
        public byte ProtocolMajor = 2;
        public byte? UnifiedBatteryIdx;     // 0x1004 的 feature index
        public byte? BatteryStatusIdx;      // 0x1000 的 feature index
        public byte? BatteryVoltageIdx;     // 0x1001
        public byte? AdcMeasurementIdx;     // 0x1F20
        public byte? UnifiedWorkingFunc;    // 已验证可用的 Unified Battery func（0 或 1）
        public int? RegisterBattery;        // 1.0: 0x07 或 0x0D
        public ushort? WirelessPid;         // 配对寄存器 WPID，供电压曲线覆盖
        public byte? DpiIdx;                // 0x2201 / 0x2202 的 feature index；0 = 已确认不支持
        public bool DpiExtended;            // true = 走 0x2202（长报文）
        public byte? ReportRateIdx;         // 0x8060 / 0x8061 的 feature index；0 = 已确认不支持
        public bool ReportRateExtended;     // true = 走 0x8061（码表下标）
        public int DpiProbeMisses;          // DPI feature 探测失败次数（到预算才放弃）
        public int ReportRateProbeMisses;   // 回报率 feature 探测失败次数
        public DateTimeOffset NextPointerAt; // 下一次指针遥测刷新时刻（低频，独立于电量节拍）
        public int ConsecutiveFailures;
        public bool WritesDisabled;         // 熔断停写中
        public DateTimeOffset WritesDisabledAt;
        public int BreakerTrips;            // 熔断次数 = 递增退避档位
    }

    private sealed class ReceiverSession
    {
        public required HidDeviceInfo Info;
        public HidppIO? Io;
        public bool OpenFailed;
        public DateTimeOffset OpenFailedAt;             // 上次打开失败的时刻，供退避重试（CR-17）
        public bool DirectDevice;                       // true = 设备本体直连，false = 接收器
        public bool InitialScanDone;                    // 首次全槽扫描已完成
        public int NotReadyScans;                       // 连续"整轮一个都没应答"的次数（接收器刚插上时会这样）
        public readonly Dictionary<byte, DevEntry> Devices = new();
        public readonly Dictionary<byte, DateTimeOffset> EmptySlotUntil = new();

        /// <summary>通知里出现但还没认过的槽位。**只能登记、不能就地发现**——发现要发请求，
        /// 而通知回调就在等包循环里，就地发会把调用栈递归打穿（issue #55）。由监听线程下一拍处理。</summary>
        public readonly ConcurrentDictionary<byte, byte> PendingDiscover = new();

        public Action<byte, byte[]>? NotifyHandler;
        public CancellationTokenSource? ListenCts;
        public Task? ListenTask;
    }

    public new async Task<IReadOnlyList<DeviceStatus>> AttachAsync(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
    {
        var centurion = await base.AttachAsync(availableDevices, claims).ConfigureAwait(false);
        var legacy = await Task.Run(() => AttachCore(availableDevices, claims)).ConfigureAwait(false);
        return centurion.Concat(legacy).ToArray();
    }

    private IReadOnlyList<DeviceStatus> AttachCore(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
    {
        _claims = claims;
        var claimed = new List<DeviceStatus>();

        // 同 VID/PID 上按目录报文偏好跳过冗余接口（Bolt 跳短；Unifying 跳长）
        var hidppPeers = availableDevices
            .Where(IsHidppInterface)
            .Select(d => (d.VendorId, d.ProductId, d.OutputReportLength))
            .ToList();

        foreach (var info in availableDevices)
        {
            if (IsCenturionCandidate(info)) continue;
            if (!IsHidppInterface(info))
                continue;
            // 写入许可关闭：HID++ 的发现与读数全靠写帧，直接不认领（safety.md §3 规则 2）。
            if (!WritesEnabled) continue;
            var pref = HidppCatalog.ReportPreference(info.ProductId);
            if (HidppCodec.ShouldSkipInterface(
                    info.VendorId, info.ProductId, info.OutputReportLength, hidppPeers, pref))
            {
                if (_sessions.TryRemove(info.DevicePath, out var skipped))
                {
                    StopListening(skipped);
                    skipped.Io?.Dispose();
                    claims.Release(info.DevicePath, Name);
                }
                Log.Info($"罗技跳过冗余接口（偏好={pref}）: {info}");
                continue;
            }
            if (!claims.TryClaim(info.DevicePath, Name))
                continue;

            var session = _sessions.GetOrAdd(info.DevicePath,
                p => new ReceiverSession { Info = info });

            session.Info = info;

            if (session.Io == null && CanRetryOpen(session))
            {
                try
                {
                    session.Io = new HidppIO(info.Open());
                    session.Io.WritesAllowed = () => WritesEnabled;
                    Log.Info($"罗技 HID++ 接口打开: {info} report=0x{session.Io.ReportId:X2}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    MarkOpenFailed(session);
                    Log.Warn($"罗技接口受保护无法访问: {info.DevicePath} ({ex.Message})");
                }
                catch (Exception ex)
                {
                    MarkOpenFailed(session);
                    Log.Error("罗技接口打开失败", ex);
                }
            }

            if (session.Io != null)
            {
                // 空接收器也要听通知，否则只能靠每分钟空槽超时发现设备。
                EnsureListening(session);
                DiscoverDevices(session);
                if (session.Devices.Count > 0)
                    claimed.AddRange(session.Devices.Values.Select(d => d.Status));
            }
        }

        // 已拔出的接收器（与 ReconcileRemovals 同一套动作，别再各写一遍）
        claimed.AddRange(ReconcileLegacyRemovals(availableDevices));
        return claimed;
    }

    protected override bool IsCandidate(HidDeviceInfo info) => IsCenturionCandidate(info);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (!TryReadCenturionBattery(handle, "probe", out var percent, out var charging)) return null;
        return ("Logitech Centurion", percent, charging);
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        if (!TryReadCenturionBattery(dev.Handle, "poll", out var percent, out var charging))
        {
            ReportPollFailure(dev);
            return null;
        }
        ReportPollAnswered(dev);
        return (percent, charging);
    }

    protected override DeviceKind ResolveKind(string protocol)
        => protocol == "Logitech Centurion" ? DeviceKind.Headset : base.ResolveKind(protocol);

    protected override string BuildDisplayName(HidDeviceInfo info)
        => string.IsNullOrWhiteSpace(info.Product) ? "Logitech PRO X 2 LIGHTSPEED" : info.Product;

    private bool TryReadCenturionBattery(HidDeviceHandle handle, string purpose, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (!WritesEnabled) return false;

        var started = System.Diagnostics.Stopwatch.StartNew();
        var request = CenturionCodec.BuildBatteryRequest();
        if (!handle.WriteOutput(request, TimeSpan.FromMilliseconds(500)))
        {
            Log.Warn($"Logitech Centurion 电量查询无响应 purpose={purpose} stage=write elapsedMs={started.ElapsedMilliseconds}");
            return false;
        }
        for (int i = 0; i < 4; i++)
        {
            var response = handle.ReadInput(TimeSpan.FromMilliseconds(1500));
            if (CenturionCodec.TryParseBatteryResponse(response, out percent, out charging))
            {
                Log.Info($"Logitech Centurion 电量查询 purpose={purpose} percent={percent} charging={charging} elapsedMs={started.ElapsedMilliseconds}");
                return true;
            }
        }
        Log.Warn($"Logitech Centurion 电量查询无响应 purpose={purpose} stage=read-timeout elapsedMs={started.ElapsedMilliseconds}");
        return false;
    }

    public new IReadOnlyList<DeviceStatus> ReconcileRemovals(IReadOnlyList<HidDeviceInfo> availableDevices)
        => base.ReconcileRemovals(availableDevices).Concat(ReconcileLegacyRemovals(availableDevices)).ToArray();

    private IReadOnlyList<DeviceStatus> ReconcileLegacyRemovals(IReadOnlyList<HidDeviceInfo> availableDevices)
    {
        if (availableDevices.Count == 0) return Array.Empty<DeviceStatus>();
        var gone = new List<DeviceStatus>();
        foreach (var path in _sessions.Keys)
        {
            if (availableDevices.Any(d => d.DevicePath == path)) continue;
            if (!_sessions.TryRemove(path, out var session)) continue;
            StopListening(session);
            session.Io?.Dispose();
            foreach (var d in session.Devices.Values)
            {
                d.Status.IsOnline = false;
                gone.Add(d.Status);
            }
            _claims?.Release(path, Name);
            Log.Info($"罗技接口已移除: {path}");
        }
        return gone;
    }

    private void DiscoverDevices(ReceiverSession session)
    {
        var io = session.Io!;
        bool initial = !session.InitialScanDone;
        int probesThisPass = 0;

        // 永久移除的设备：不再 ping 它的槽位，已跟踪的也一并丢掉
        foreach (var idx in session.Devices.Keys.Where(k => DeviceBlocklist.IsBlocked(SlotId(session, k))).ToList())
        {
            session.Devices.Remove(idx);
            Log.Info($"罗技设备已永久移除，停止轮询 devIdx={idx}");
        }

        if (!session.DirectDevice)
        {
            int maxSlots = HidppCatalog.MaxSlots(session.Info.ProductId);
            for (byte idx = 1; idx <= maxSlots; idx++)
            {
                if (session.Devices.ContainsKey(idx)) continue;
                if (DeviceBlocklist.IsBlocked(SlotId(session, idx))) continue;

                if (!initial)
                {
                    if (session.EmptySlotUntil.TryGetValue(idx, out var until) &&
                        DateTimeOffset.Now < until)
                        continue;
                    // 冷却期满后每轮最多补探 1 个，单轮闸门占用封顶为一次超时。
                    if (probesThisPass >= 1) continue;
                }

                var t = session.Devices.Count > 0 ? FastSlotTimeout : DiscoverTimeout;
                if (Ping(io, idx, out var major, t))
                {
                    session.EmptySlotUntil.Remove(idx);
                    AddDevice(session, idx, major);
                }
                else
                {
                    session.EmptySlotUntil[idx] = DateTimeOffset.Now + EmptySlotCooldown;
                    if (!initial) probesThisPass++;
                }
            }
        }

        if (session.Devices.Count == 0)
        {
            bool allowDirect = initial
                || !session.EmptySlotUntil.TryGetValue(0xFF, out var until)
                || DateTimeOffset.Now >= until;
            if (allowDirect && (initial || probesThisPass < 1) && !DeviceBlocklist.IsBlocked(SlotId(session, 0xFF)))
            {
                if (Ping(io, 0xFF, out var major, DiscoverTimeout))
                {
                    session.EmptySlotUntil.Remove(0xFF);
                    session.DirectDevice = true;
                    AddDevice(session, 0xFF, major);
                }
                else
                {
                    session.EmptySlotUntil[0xFF] = DateTimeOffset.Now + EmptySlotCooldown;
                }
            }
        }

        // 刚插上的接收器可能整轮都不应答（实测 2026-10-21：接口打开后 12 秒内 7 次 ping 全超时）。
        // 这时不能按"空槽 5 分钟"缓存——那会让设备 5 分钟内不再被探；也不能复用这个句柄，
        // 早开出来的句柄可能根本不通。丢掉句柄、清掉冷却，下一轮重开重扫。
        // 连续 5 次都不应答才认命（可能是真的空接收器），避免无限重开。
        if (initial && session.Devices.Count == 0 && session.NotReadyScans < 5)
        {
            session.NotReadyScans++;
            // 必须先停监听再关句柄：否则 ListenTask 在已 Dispose 的句柄上空转（CR-75 同款），
            // 而且 ListenTask 非 null 会让 EnsureListening 之后永远不再启动——新句柄再也没有被动通知。
            StopListening(session);
            session.Io?.Dispose();
            session.Io = null;
            session.EmptySlotUntil.Clear();
            Log.Info($"罗技接收器整轮无应答（第 {session.NotReadyScans} 次），丢弃句柄稍后重开");
            return;   // InitialScanDone 保持 false，下一轮仍做全扫
        }

        session.InitialScanDone = true;
    }

    /// <summary>通知里出现未知槽位时补发现（不受空槽负缓存限制）。</summary>
    private void TryDiscoverSlot(ReceiverSession session, byte idx)
    {
        if (session.Io == null || session.DirectDevice) return;
        if (idx is 0 or 0xFF) return;
        if (session.Devices.ContainsKey(idx)) return;
        int maxSlots = HidppCatalog.MaxSlots(session.Info.ProductId);
        if (idx > maxSlots) return;
        if (DeviceBlocklist.IsBlocked(SlotId(session, idx))) return;
        // 空槽冷却对**所有**调用方都生效：曾经在这里先 Remove 再 Ping，等于通知路径完全绕过冷却，
        // 每来一条通知就重探一次（issue #55 的放大器之一）。
        if (session.EmptySlotUntil.TryGetValue(idx, out var until) && DateTimeOffset.Now < until) return;

        if (Ping(session.Io, idx, out var major, FastSlotTimeout))
        {
            session.EmptySlotUntil.Remove(idx);
            AddDevice(session, idx, major);
        }
        else
        {
            session.EmptySlotUntil[idx] = DateTimeOffset.Now + EmptySlotCooldown;
        }
    }

    /// <summary>处理通知登记下来的待发现槽位：一拍只探一个，避免把监听线程长时间占住。</summary>
    private void DrainPendingDiscover(ReceiverSession session)
    {
        if (session.PendingDiscover.IsEmpty) return;
        foreach (var idx in session.PendingDiscover.Keys)
        {
            if (!session.PendingDiscover.TryRemove(idx, out _)) continue;
            try
            {
                TryDiscoverSlot(session, idx);
            }
            catch (Exception ex)
            {
                Log.Warn($"罗技补发现槽位异常 devIdx={idx}: {ex.Message}");
            }
            return;
        }
    }

    /// <summary>设备 ID：接收器**端口无关**路径哈希 + 槽位（换 USB 口不产生新设备）。认领、黑名单、上报三处必须同一套规则。</summary>
    private static string SlotId(ReceiverSession session, byte idx)
        => $"logitech:{PluginUtils.StableDeviceHash(session.Info.DevicePath)}:{idx}";

    private void AddDevice(ReceiverSession session, byte idx, byte protocolMajor)
    {
        if (DeviceBlocklist.IsBlocked(SlotId(session, idx))) return;

        var io = session.Io!;
        // 0x0005 的下标因固件而异，两个调用（名字 / 类型）共用一次 GetFeature
        byte nameIdx = ResolveFeature(io, idx, 0x0005);
        string name = ReadDeviceName(io, idx, nameIdx) ?? $"罗技设备 {idx}";
        ushort? wpid = null;
        DeviceKind pairedKind = DeviceKind.Unknown;
        if (!session.DirectDevice)
            TryReadPairingInfo(io, session.Info.ProductId, idx, out wpid, out pairedKind);

        // 类型优先级：0x0005 营销类型（权威）> 配对寄存器 > 按名字猜
        var kind = ReadDeviceKind(io, idx, nameIdx);
        if (kind == DeviceKind.Unknown) kind = pairedKind;
        if (kind == DeviceKind.Unknown) kind = DeviceKindGuesser.Guess(name);

        var status = new DeviceStatus
        {
            Id = SlotId(session, idx),
            Name = name,
            Source = DeviceSource.Hid24G,
            Kind = kind,
            Protocol = protocolMajor >= 2 ? "Logitech HID++ 2.0" : "Logitech HID++ 1.0",
            // 认领到的是接收器接口（直连时才是本体）；两种都只表达"被认领接口"的身份
            Usb = new UsbIdentity(session.Info.VendorId, session.Info.ProductId,
                string.IsNullOrEmpty(session.Info.SerialNumber) ? null : session.Info.SerialNumber),
            Receiver = LogitechReceiverIdentityPolicy.For(session.DirectDevice, session.Info.ProductId),
            Connection = LogitechConnectionPolicy.For(
                session.DirectDevice, idx, HidppCatalog.MaxSlots(session.Info.ProductId), DateTimeOffset.Now),
        };
        var entry = new DevEntry
        {
            Status = status,
            ProtocolMajor = protocolMajor,
            WirelessPid = wpid,
        };
        // 预解析电量 feature 下标，后台通知才能按 feature 匹配（不必等下一轮 Poll）
        if (protocolMajor >= 2)
        {
            entry.UnifiedBatteryIdx = ResolveFeature(io, idx, 0x1004);
            if (entry.UnifiedBatteryIdx == 0)
            {
                entry.BatteryStatusIdx = ResolveFeature(io, idx, 0x1000);
                if (entry.BatteryStatusIdx == 0)
                {
                    entry.BatteryVoltageIdx = ResolveFeature(io, idx, HidppCodec.FeatureBatteryVoltage);
                    if (entry.BatteryVoltageIdx == 0)
                        entry.AdcMeasurementIdx = ResolveFeature(io, idx, HidppCodec.FeatureAdcMeasurement);
                }
            }
        }
        session.Devices[idx] = entry;
        Log.Info($"发现罗技设备 devIdx={idx} proto={protocolMajor} kind={kind} wpid={(wpid is { } w ? w.ToString("X4") : "-")} name={name} conn={status.Connection.Value?.ToString() ?? "Unknown"}/{status.Connection.Source}");
    }

    /// <summary>后台只读泵：零写入，收 Unified Battery 插拔电自发通知（与 Logi Battery Dashboard 同源）。</summary>
    private void EnsureListening(ReceiverSession session)
    {
        if (session.Io == null || session.ListenTask != null) return;

        var io = session.Io;
        session.NotifyHandler = (devIdx, data) => OnHidppNotification(session, devIdx, data);
        io.NotificationReceived += session.NotifyHandler;
        session.ListenCts = new CancellationTokenSource();
        var cts = session.ListenCts;
        session.ListenTask = Task.Run(() =>
        {
            Log.Info("罗技 HID++ 通知监听已启动（被动，零写入）");
            int fastNull = 0;
            while (!cts.IsCancellationRequested)
            {
                var started = Environment.TickCount64;
                try
                {
                    io.PumpNotifications(TimeSpan.FromMilliseconds(500));
                    // 通知回调只登记待发现槽位，真正的补发现在这里做（此时不在任何等包循环里）。
                    DrainPendingDiscover(session);
                }
                catch (Exception ex)
                {
                    if (!cts.IsCancellationRequested)
                        Log.Warn($"罗技通知监听异常: {ex.Message}");
                }

                // 正常静音会等满 500ms；连续几次几十毫秒返回说明句柄已失效（ReadInput 立即 null），
                // 不退避的话这个 while 会变成每秒几万次空转、烧满一个核（CR-75/76）。
                fastNull = Environment.TickCount64 - started < 100 ? fastNull + 1 : 0;
                if (fastNull >= 3) Thread.Sleep(200);
            }
        }, cts.Token);
    }

    private void StopListening(ReceiverSession session)
    {
        try { session.ListenCts?.Cancel(); } catch { /* ignore */ }
        if (session.Io != null && session.NotifyHandler != null)
            session.Io.NotificationReceived -= session.NotifyHandler;
        session.NotifyHandler = null;
        try { session.ListenTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
        session.ListenCts?.Dispose();
        session.ListenCts = null;
        session.ListenTask = null;
    }

    private void OnHidppNotification(ReceiverSession session, byte devIdx, byte[] data)
    {
        if (data.Length < 5) return;

        if (!session.Devices.TryGetValue(devIdx, out var dev))
        {
            // 未认过的槽位：只登记，绝不在这里发请求（见 ReceiverSession.PendingDiscover）。
            session.PendingDiscover.TryAdd(devIdx, 0);
            return;
        }

        byte feature = data[0];
        bool batteryFeature =
            (dev.UnifiedBatteryIdx is { } u && feature == u) ||
            (dev.BatteryStatusIdx is { } b && feature == b) ||
            (dev.BatteryVoltageIdx is { } v && feature == v) ||
            (dev.AdcMeasurementIdx is { } a && feature == a);
        if (!batteryFeature) return;

        if (dev.BatteryVoltageIdx is { } voltIdx && feature == voltIdx)
        {
            var curve = HidppCatalog.CurveFor(dev.WirelessPid, adcFeature: false);
            if (!HidppCodec.TryParseBatteryVoltage(data, curve, out var pctV, out var chgV, out _))
                return;
            PushIfChanged(dev, pctV, chgV);
            return;
        }

        if (dev.AdcMeasurementIdx is { } adcIdx && feature == adcIdx)
        {
            var curve = HidppCatalog.CurveFor(dev.WirelessPid, adcFeature: true);
            if (!HidppCodec.TryParseAdcMeasurement(data, curve, out var pctA, out var chgA, out _))
                return;
            PushIfChanged(dev, pctA, chgA);
            return;
        }

        if (!HidppCodec.TryParseBattery20(data, out var pct, out var chg)) return;
        PushIfChanged(dev, pct, chg);
    }

    private void PushIfChanged(DevEntry dev, int pct, ChargingState chg)
    {
        bool changed = dev.Status.BatteryPercent != pct
                    || dev.Status.Charging != chg
                    || !dev.Status.IsOnline;
        ApplyBattery(dev.Status, pct, chg);
        if (changed)
            RaiseStatusPushed(dev.Status);
    }

    public new IReadOnlyList<DeviceStatus> Poll()
    {
        var updated = base.Poll().ToList();
        if (!WritesEnabled) return updated;

        foreach (var session in _sessions.Values)
        {
            if (session.Io == null) continue;

            // 发现只在 Attach 与监听线程的补发现里做；此处再扫会把空接收器的超时翻倍
            foreach (var (idx, dev) in session.Devices)
            {
                try
                {
                    // 熔断停写：退避期满前不再查询，期满放行一轮（递增退避口径见 Plugins/WriteBackoff.cs）
                    if (dev.WritesDisabled)
                    {
                        if (!WriteBackoff.IsExpired(dev.BreakerTrips, dev.WritesDisabledAt, DateTimeOffset.Now)) continue;
                        dev.WritesDisabled = false;
                        Log.Info($"罗技 {dev.Status.DisplayName} 停写退避期满（{WriteBackoff.For(dev.BreakerTrips).TotalSeconds:0} 秒），恢复查询");
                    }

                    if (RefreshBattery(session, idx, dev))
                    {
                        updated.Add(dev.Status);
                        dev.ConsecutiveFailures = 0;
                        dev.BreakerTrips = 0;
                        RefreshPointer(session, idx, dev);
                    }
                    else
                    {
                        dev.ConsecutiveFailures++;
                        if (dev.ConsecutiveFailures >= MaxConsecutiveFailures)
                        {
                            dev.WritesDisabled = true;
                            dev.WritesDisabledAt = DateTimeOffset.Now;
                            dev.BreakerTrips++;
                            Log.Warn($"罗技 {dev.Status.DisplayName} 连续 {MaxConsecutiveFailures} 次查询失败，熔断停写（第 {dev.BreakerTrips} 次，退避 {WriteBackoff.For(dev.BreakerTrips).TotalSeconds:0} 秒）");
                            if (dev.Status.IsOnline)
                            {
                                dev.Status.IsOnline = false;
                                updated.Add(dev.Status);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"罗技电量轮询异常 devIdx={idx}", ex);
                }
            }
        }
        return updated;
    }

    private bool RefreshBattery(ReceiverSession session, byte idx, DevEntry dev)
    {
        var io = session.Io!;
        var status = dev.Status;

        if (dev.ProtocolMajor >= 2)
        {
            // 1) Unified Battery 0x1004
            //    func 1 = GetStatus（电量% + 充电态）—— MX Master 3S 真机：90% / charging=1
            //    func 0 = GetCapabilities（档位能力），**不能当电量**（真机会被误读成 15%）
            //    优先 func 1；仅当 1 无响应时才回退 func 0（极旧固件）
            var unifiedIdx = dev.UnifiedBatteryIdx ??= ResolveFeature(io, idx, 0x1004);
            if (unifiedIdx != 0)
            {
                byte[] funcs = dev.UnifiedWorkingFunc.HasValue
                    ? new byte[] { dev.UnifiedWorkingFunc.Value }
                    : new byte[] { 1, 0 };
                int? fallbackPct = null;
                ChargingState fallbackChg = ChargingState.Unknown;
                byte? fallbackFunc = null;
                foreach (byte func in funcs)
                {
                    var resp = io.Request(idx, HidppCodec.FeatureFunc(unifiedIdx, func), ReadOnlySpan<byte>.Empty);
                    if (resp == null || !HidppCodec.TryParseBattery20(resp, out var pct, out var chg))
                        continue;

                    // 已锁定工作 func：直接用
                    if (dev.UnifiedWorkingFunc.HasValue)
                    {
                        ApplyBattery(status, pct, chg);
                        return true;
                    }

                    // 探测期：func 1 立即采信；func 0 只作回退候选
                    if (func == 1)
                    {
                        dev.UnifiedWorkingFunc = 1;
                        ApplyBattery(status, pct, chg);
                        return true;
                    }

                    fallbackPct = pct;
                    fallbackChg = chg;
                    fallbackFunc = 0;
                }

                if (fallbackPct.HasValue)
                {
                    dev.UnifiedWorkingFunc = fallbackFunc;
                    ApplyBattery(status, fallbackPct.Value, fallbackChg);
                    return true;
                }
            }

            // 2) Battery Status 0x1000
            var statusIdx = dev.BatteryStatusIdx ??= ResolveFeature(io, idx, 0x1000);
            if (statusIdx != 0)
            {
                var resp = io.Request(idx, HidppCodec.FeatureFunc(statusIdx, 0), ReadOnlySpan<byte>.Empty);
                if (resp != null && HidppCodec.TryParseBattery20(resp, out var pct, out var chg))
                {
                    ApplyBattery(status, pct, chg);
                    return true;
                }
            }

            // 3) Battery Voltage 0x1001（G 系等只有毫伏、没有百分比特征）
            var voltIdx = dev.BatteryVoltageIdx ??= ResolveFeature(io, idx, HidppCodec.FeatureBatteryVoltage);
            if (voltIdx != 0)
            {
                var resp = io.Request(idx, HidppCodec.FeatureFunc(voltIdx, 0), ReadOnlySpan<byte>.Empty);
                var curve = HidppCatalog.CurveFor(dev.WirelessPid, adcFeature: false);
                if (resp != null &&
                    HidppCodec.TryParseBatteryVoltage(resp, curve, out var pct, out var chg, out _))
                {
                    ApplyBattery(status, pct, chg);
                    return true;
                }
            }

            // 4) ADC Measurement 0x1F20
            var adcIdx = dev.AdcMeasurementIdx ??= ResolveFeature(io, idx, HidppCodec.FeatureAdcMeasurement);
            if (adcIdx != 0)
            {
                var resp = io.Request(idx, HidppCodec.FeatureFunc(adcIdx, 0), ReadOnlySpan<byte>.Empty);
                var curve = HidppCatalog.CurveFor(dev.WirelessPid, adcFeature: true);
                if (resp != null &&
                    HidppCodec.TryParseAdcMeasurement(resp, curve, out var pct, out var chg, out _))
                {
                    ApplyBattery(status, pct, chg);
                    return true;
                }
            }
        }

        // 5) HID++ 1.0 寄存器（偏移已按参数区 resp[2..] 对齐，保持不动）
        int? register = dev.RegisterBattery;
        foreach (int reg in new[] { 0x07, 0x0D })
        {
            if (register.HasValue && register.Value != reg) continue;
            var resp = io.Request(idx, (ushort)(0x8100 | reg), ReadOnlySpan<byte>.Empty,
                patchSw: false);
            if (resp != null && resp.Length >= 3)
            {
                dev.RegisterBattery = reg;
                if (reg == 0x07)
                {
                    int percent = resp[2];
                    if (percent is > 0 and <= 100)
                    {
                        var state = (resp.Length >= 5 ? resp[4] : (byte)0) switch
                        {
                            0x30 => ChargingState.Discharging,
                            0x50 => ChargingState.Charging,
                            0x90 => ChargingState.Full,
                            _ => ChargingState.Unknown,
                        };
                        ApplyBattery(status, percent, state);
                        return true;
                    }
                }
                else // 0x0D：只有档位
                {
                    var (percent, state) = MapTierBattery(resp[2], resp.Length >= 4 ? resp[3] : (byte)0);
                    ApplyBattery(status, percent, state);
                    return true;
                }
            }
            if (!register.HasValue) break; // 第一次只试 0x07
        }

        return false;
    }

    /// <summary>低频读 DPI / 回报率。只读、失败不影响在线判定；不支持的 feature 记 0 后不再重探。</summary>
    private void RefreshPointer(ReceiverSession session, byte idx, DevEntry dev)
    {
        if (dev.ProtocolMajor < 2) return;
        var now = DateTimeOffset.Now;
        if (now < dev.NextPointerAt) return;
        dev.NextPointerAt = now + PointerRefreshInterval;

        var io = session.Io!;
        var patch = new PointerTelemetry();

        if (TryReadDpi(io, idx, dev, out int dpi))
            patch = patch with { DpiX = TelemetryValue<int>.Reported(dpi, now) };

        if (TryReadReportRate(io, idx, dev, out int hertz))
            patch = patch with { ReportRateSettingHz = TelemetryValue<int>.Reported(hertz, now) };

        if (!patch.HasAny) return;

        bool changed = PointerTelemetry.ValueDiffers(dev.Status.Pointer, patch);
        dev.Status.Pointer = PointerTelemetry.Merge(dev.Status.Pointer, patch);
        if (changed)
        {
            Log.Info($"罗技指针遥测更新: {dev.Status.DisplayName} dpi={dev.Status.Pointer.DpiX.Value?.ToString() ?? "-"} rate={dev.Status.Pointer.ReportRateSettingHz.Value?.ToString() ?? "-"}Hz");
            RaiseStatusPushed(dev.Status);
        }
    }

    /// <summary>0x2201 优先（短响应），不支持且接口是长报文时退回 0x2202（func 5，长响应）。</summary>
    private static bool TryReadDpi(HidppIO io, byte idx, DevEntry dev, out int dpi)
    {
        dpi = 0;
        if (!EnsureDpiFeature(io, idx, dev)) return false;

        byte func = dev.DpiExtended ? HidppPointerCodec.FuncGetDpiParameters : HidppPointerCodec.FuncGetSensorDpi;
        var resp = io.Request(idx, HidppCodec.FeatureFunc(dev.DpiIdx.GetValueOrDefault(), func), new byte[] { 0, 0, 0 },
            timeout: PointerProbeTimeout);
        if (resp == null) return false;
        return dev.DpiExtended
            ? HidppPointerCodec.TryParseExtendedDpi(resp, out dpi)
            : HidppPointerCodec.TryParseDpi(resp, out dpi);
    }

    /// <summary>0x8060 优先（毫秒），不支持时退回 0x8061（码表下标）。</summary>
    private static bool TryReadReportRate(HidppIO io, byte idx, DevEntry dev, out int hertz)
    {
        hertz = 0;
        if (!EnsureReportRateFeature(io, idx, dev)) return false;

        byte func = dev.ReportRateExtended
            ? HidppPointerCodec.FuncGetExtendedReportRate
            : HidppPointerCodec.FuncGetReportRate;
        var resp = io.Request(idx, HidppCodec.FeatureFunc(dev.ReportRateIdx.GetValueOrDefault(), func), new byte[] { 0, 0, 0 },
            timeout: PointerProbeTimeout);
        if (resp == null) return false;

        if (dev.ReportRateExtended)
            return HidppPointerCodec.TryParseExtendedReportRateHz(resp, out hertz);

        if (!HidppPointerCodec.TryParseReportRateMs(resp, out int ms)) return false;
        hertz = HidppPointerCodec.HertzFromMilliseconds(ms);
        return hertz > 0;
    }

    /// <summary>解析 DPI feature 下标。查不到不永久拉黑：前几次给重试，过了预算才认定不支持（休眠时首次探测会假阴性）。</summary>
    private static bool EnsureDpiFeature(HidppIO io, byte idx, DevEntry dev)
    {
        if (dev.DpiIdx is { } cached && (cached != 0 || dev.DpiProbeMisses >= PointerProbeAttempts))
            return cached != 0;

        byte index = ResolveFeature(io, idx, HidppPointerCodec.FeatureAdjustableDpi, PointerProbeTimeout);
        bool extended = false;
        if (index == 0 && io.IsLongMessage)
        {
            index = ResolveFeature(io, idx, HidppPointerCodec.FeatureExtendedAdjustableDpi, PointerProbeTimeout);
            extended = index != 0;
        }

        dev.DpiIdx = index;
        dev.DpiExtended = extended;
        dev.DpiProbeMisses = index != 0 ? 0 : dev.DpiProbeMisses + 1;
        return index != 0;
    }

    /// <summary>解析回报率 feature 下标；重试预算与 DPI 相同。</summary>
    private static bool EnsureReportRateFeature(HidppIO io, byte idx, DevEntry dev)
    {
        if (dev.ReportRateIdx is { } cached && (cached != 0 || dev.ReportRateProbeMisses >= PointerProbeAttempts))
            return cached != 0;

        byte index = ResolveFeature(io, idx, HidppPointerCodec.FeatureReportRate, PointerProbeTimeout);
        bool extended = false;
        if (index == 0)
        {
            index = ResolveFeature(io, idx, HidppPointerCodec.FeatureExtendedReportRate, PointerProbeTimeout);
            extended = index != 0;
        }

        dev.ReportRateIdx = index;
        dev.ReportRateExtended = extended;
        dev.ReportRateProbeMisses = index != 0 ? 0 : dev.ReportRateProbeMisses + 1;
        return index != 0;
    }

    private static void ApplyBattery(DeviceStatus status, int percent, ChargingState charging)
    {
        bool pctChanged = status.BatteryPercent != percent;
        bool chgChanged = status.Charging != charging;
        status.BatteryPercent = percent;
        status.Charging = charging;
        status.LastUpdated = DateTimeOffset.Now;
        if (!status.IsOnline)
        {
            status.IsOnline = true;
            Log.Info($"罗技设备重新上线: {status.DisplayName} {percent}% {charging}");
        }
        else if (pctChanged || chgChanged)
        {
            Log.Info($"罗技电量更新: {status.DisplayName} {percent}% {charging}");
        }
    }

    /// <summary>读接收器配对信息（HID++ 1.0 长寄存器 0xB5）：设备类型 + wireless PID。Unifying 子寄存器 0x20+(n-1)；Bolt 子寄存器 0x50+n。</summary>
    private static bool TryReadPairingInfo(
        HidppIO io, ushort receiverPid, byte deviceIndex,
        out ushort? wirelessPid, out DeviceKind kind)
    {
        wirelessPid = null;
        kind = DeviceKind.Unknown;
        var family = HidppCatalog.Family(receiverPid);
        byte subReg = family == ReceiverFamily.Bolt
            ? (byte)(0x50 + deviceIndex)
            : (byte)(0x20 + deviceIndex - 1);

        Span<byte> args = stackalloc byte[1] { subReg };
        // 0x83B5 = 长寄存器读 RECEIVER_INFO（0x2B5）
        var resp = io.Request(0xFF, 0x83B5, args, patchSw: false,
            timeout: TimeSpan.FromMilliseconds(800));
        if (resp == null) return false;

        if (family == ReceiverFamily.Bolt)
        {
            if (!HidppCodec.TryParseBoltPairing(resp, out var wpid, out kind))
                return false;
            wirelessPid = wpid == 0 ? null : wpid;
            return true;
        }

        if (HidppCodec.TryParseUnifyingPairing(resp, out var uWpid, out kind))
        {
            wirelessPid = uWpid == 0 ? null : uWpid;
            return true;
        }

        // 未知族：先试 Unifying 布局，再试 Bolt
        if (HidppCodec.TryParseBoltPairing(resp, out var bWpid, out kind))
        {
            wirelessPid = bWpid == 0 ? null : bWpid;
            return true;
        }
        return false;
    }

    /// <summary>Root.GetFeature：查询特性 ID 对应的 feature index，0 = 不支持。</summary>
    private static byte ResolveFeature(HidppIO io, byte devIdx, ushort featureId, TimeSpan? timeout = null)
    {
        Span<byte> args = stackalloc byte[2] { (byte)(featureId >> 8), (byte)featureId };
        var resp = io.Request(devIdx, 0x0000, args, timeout: timeout);
        return resp != null ? HidppCodec.ParseFeatureIndex(resp) : (byte)0;
    }

    /// <summary>Root.Ping（func 1，带随机标记）。major 取响应 data[2]。</summary>
    private static bool Ping(HidppIO io, byte devIdx, out byte protocolMajor, TimeSpan? timeout = null)
    {
        protocolMajor = 0;
        byte mark = (byte)(Random.Shared.Next(1, 255));
        Span<byte> args = stackalloc byte[3] { 0, 0, mark };
        var resp = io.Request(devIdx, 0x0010, args, timeout: timeout); // 0x0010|sw → feature 0x00, func 1
        if (resp != null && resp.Length >= 5 && resp[4] == mark)
        {
            protocolMajor = resp[2];
            return true;
        }
        return false;
    }

    /// <summary>Feature ID 0x0005 = DEVICE NAME/TYPE；<paramref name="nameIdx"/> 由调用方 ResolveFeature 得到（不要写死 0x05）。</summary>
    private static string? ReadDeviceName(HidppIO io, byte devIdx, byte nameIdx)
    {
        if (nameIdx == 0) return null;

        var resp = io.Request(devIdx, HidppCodec.FeatureFunc(nameIdx, 0), ReadOnlySpan<byte>.Empty); // GetCount
        if (resp == null || resp.Length < 3 || resp[2] is 0 or > 32) return null;
        int len = resp[2];
        var chars = new char[len];
        for (byte i = 0; i < len; i++)
        {
            var r = io.Request(devIdx, HidppCodec.FeatureFunc(nameIdx, 1), new byte[] { i }); // GetChar
            if (r == null || r.Length < 3) return null;
            chars[i] = (char)r[2];
        }
        var name = new string(chars).Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>0x0005 func 2 = getDeviceType，营销类型比配对寄存器准；读不到返回 Unknown 交给下一级兜底。</summary>
    private static DeviceKind ReadDeviceKind(HidppIO io, byte devIdx, byte nameIdx)
    {
        if (nameIdx == 0) return DeviceKind.Unknown;
        var resp = io.Request(devIdx, HidppCodec.FeatureFunc(nameIdx, 2), ReadOnlySpan<byte>.Empty);
        return resp == null || resp.Length < 3 ? DeviceKind.Unknown : HidppCodec.MapDeviceTypeKind(resp[2]);
    }

    private static (int Percent, ChargingState State) MapTierBattery(byte tier, byte chargeStatus)
    {
        var state = (chargeStatus & 0x21) == 0x21 ? ChargingState.Charging
            : (chargeStatus & 0x22) == 0x22 ? ChargingState.Full
            : ChargingState.Discharging;
        return tier switch
        {
            7 => (100, state),
            5 => (60, state),
            3 => (20, state),
            1 => (10, state),
            _ => (0, state),
        };
    }

    public override void Detach()
    {
        int released = _sessions.Count;
        foreach (var session in _sessions.Values)
        {
            StopListening(session);
            session.Io?.Dispose();
        }
        _sessions.Clear();
        base.Detach();
        if (released > 0) Log.Info($"罗技 HID++ 插件已卸载，释放 {released} 个接收器会话");
    }
}
