using System.Collections.Concurrent;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 罗技 2.4G 插件：支持 Unifying / Bolt / Lightspeed / Nano 接收器、USB 直连设备（HID++ 协议）。
/// 2026/10/06 15:53:36 xliu
/// 电量读取级联：Unified Battery 0x1004 → Battery Status 0x1000 → Battery Voltage 0x1001 → ADC Measurement 0x1F20 → HID++ 1.0 寄存器 0x07/0x0D（协议细节见 docs/hardware/logitech.md）。
/// </summary>
public sealed class LogitechPlugin : IBatteryPlugin
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

    /// <summary>插件开关的键，与 <c>AppSettings.PluginEnabled</c> 对应（插件开关即写入开关，见 <see cref="WritesEnabled"/>）。</summary>
    public const string SettingsKey = "Logitech";

    public string Name => SettingsKey;
    public string DisplayName => "罗技";
    public string DisplayNameEn => "LOGITECH";

    /// <summary>罗技接收器同时管鼠标和键盘，按型号分不出来 → Unknown，UI 退回按名字猜。</summary>
    public DeviceKind Kind => DeviceKind.Unknown;

    /// <summary>HID++ 探测要写帧（ping / GetFeature），不参与 15 秒快速发现节拍，避免写入频率上升。</summary>
    public bool SupportsFastDiscovery => false;

    /// <summary>同理不参与：HID++ 的发现要 ping 7 个槽位（睡着时约 10 秒），每 15 秒跑一遍代价太高；
    /// 罗技靠自发的 HID++ 通知做实时更新，不需要加快发现节拍。</summary>
    public bool SupportsDiscoveryProbe => false;

    /// <summary>HID++ 自发电量/充电通知 → DeviceManager 即时 Upsert（对照浏览器扩展的实时更新）。</summary>
    public event Action<DeviceStatus>? StatusPushed;

    /// <summary>
    /// 写入许可 = **插件开关**（<c>PluginEnabled["Logitech"]</c>）。罗技的发现、电量读取、名字/配对信息全靠写帧，
    /// 没有可用的只读模式——"插件开着但关掉写入许可"是没有任何使用场景的组合，只会让用户以为功能坏了（CR-37）。
    /// 闸门本身仍是唯一写点内的 <c>HidppIO.WritesAllowed</c>（CR-01）。
    /// </summary>
    public static bool WritesEnabled =>
        AppSettings.Load().PluginEnabled.TryGetValue(SettingsKey, out var on) && on;

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
        public Action<byte, byte[]>? NotifyHandler;
        public CancellationTokenSource? ListenCts;
        public Task? ListenTask;
    }

    public Task<IReadOnlyList<DeviceStatus>> AttachAsync(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
        => Task.Run(() => AttachCore(availableDevices, claims));

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
        claimed.AddRange(ReconcileRemovals(availableDevices));
        return claimed;
    }

    /// <summary>只拆"扫描结果里已经没有"的接收器会话：停监听、关句柄、把槽位设备标离线并**上报**。</summary>
    public IReadOnlyList<DeviceStatus> ReconcileRemovals(IReadOnlyList<HidDeviceInfo> availableDevices)
    {
        // 与基类同一处保护：空扫描是枚举失败，不是全部拔出。
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
                // 不上报的话拔出后条目永远在线（基类同一处坑的注释）。
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
        session.EmptySlotUntil.Remove(idx);
        if (Ping(session.Io, idx, out var major, FastSlotTimeout))
            AddDevice(session, idx, major);
        else
            session.EmptySlotUntil[idx] = DateTimeOffset.Now + EmptySlotCooldown;
    }

    /// <summary>设备 ID：接收器**端口无关**路径哈希 + 槽位（换 USB 口不产生新设备）。认领、黑名单、上报三处必须同一套规则。</summary>
    private static string SlotId(ReceiverSession session, byte idx)
        => $"logitech:{PluginUtils.StableDeviceHash(session.Info.DevicePath)}:{idx}";

    private void AddDevice(ReceiverSession session, byte idx, byte protocolMajor)
    {
        if (DeviceBlocklist.IsBlocked(SlotId(session, idx))) return;

        var io = session.Io!;
        string name = ReadDeviceName(io, idx) ?? $"罗技设备 {idx}";
        var kind = DeviceKind.Unknown;
        ushort? wpid = null;
        if (!session.DirectDevice &&
            TryReadPairingInfo(io, session.Info.ProductId, idx, out wpid, out var pairedKind))
        {
            kind = pairedKind;
        }
        if (kind == DeviceKind.Unknown)
            kind = DeviceKindGuesser.Guess(name);

        var status = new DeviceStatus
        {
            Id = SlotId(session, idx),
            Name = name,
            Source = DeviceSource.Hid24G,
            Kind = kind,
            Protocol = protocolMajor >= 2 ? "Logitech HID++ 2.0" : "Logitech HID++ 1.0",
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
        Log.Info($"发现罗技设备 devIdx={idx} proto={protocolMajor} kind={kind} wpid={(wpid is { } w ? w.ToString("X4") : "-")} name={name}");
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
            TryDiscoverSlot(session, devIdx);
            if (!session.Devices.TryGetValue(devIdx, out dev)) return;
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
            StatusPushed?.Invoke(dev.Status);
    }

    public IReadOnlyList<DeviceStatus> Poll()
    {
        // 写入许可关闭：不轮询（读数全靠写帧）。被动通知监听照常，即"降级为只被动监听"（safety.md §3 规则 2）。
        if (!WritesEnabled) return Array.Empty<DeviceStatus>();

        var updated = new List<DeviceStatus>();
        foreach (var session in _sessions.Values)
        {
            if (session.Io == null) continue;

            // 发现只在 Attach / 通知补槽里做；此处再扫会把空接收器的超时翻倍
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
    private static byte ResolveFeature(HidppIO io, byte devIdx, ushort featureId)
    {
        Span<byte> args = stackalloc byte[2] { (byte)(featureId >> 8), (byte)featureId };
        var resp = io.Request(devIdx, 0x0000, args);
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

    private static string? ReadDeviceName(HidppIO io, byte devIdx)
    {
        // Feature ID 0x0005 = DEVICE NAME；下标因固件而异，必须先 GetFeature，不能写死 0x05
        byte nameIdx = ResolveFeature(io, devIdx, 0x0005);
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

    public void Detach()
    {
        int released = _sessions.Count;
        foreach (var session in _sessions.Values)
        {
            StopListening(session);
            session.Io?.Dispose();
        }
        _sessions.Clear();
        if (released > 0) Log.Info($"罗技插件已卸载，释放 {released} 个会话");
    }
}
