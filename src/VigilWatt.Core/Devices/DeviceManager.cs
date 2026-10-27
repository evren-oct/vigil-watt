using System.Collections.Concurrent;
using VigilWatt.Core.Ble;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Power;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Storage;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Devices;

/// <summary>删除离线设备的结果（UI 按它决定提示文案）。</summary>
public enum DeviceRemoveResult
{
    Removed,
    NotFound,
    /// <summary>确认框开着时设备上线了：不能删，否则等于把在线设备藏起来。</summary>
    Online,
    SystemDevice,
    /// <summary>名单写不进 settings.json：状态一个都没动。</summary>
    SaveFailed,
}

/// <summary>
/// 功 能： 统一设备管理（中枢）：合并 BLE + 经典蓝牙 + 2.4G 插件状态，写历史、发变更事件；插件反射自动发现，接口认领经 DeviceClaimRegistry 防抢。
/// 2026/10/06 15:53:36 xliu
/// 详见 docs/project/architecture.md §1。
/// </summary>
public sealed class DeviceManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly HistoryStore _history;
    private readonly BleBatteryWatcher _ble = new();
    private readonly BluetoothClassicWatcher _classic = new();
    private readonly SystemBatteryWatcher _system = new();
    private readonly List<IBatteryPlugin> _plugins;
    private readonly DeviceClaimRegistry _claims = new();
    private readonly ConcurrentDictionary<string, DeviceStatus> _devices = new();
    private readonly ConcurrentDictionary<string, RemainingTimeEstimateResult> _remainingTimeEstimates = new();

    /// <summary>每台设备"是否已就低电量提醒过"。电量回到阈值之上（或充电）就复位，下次跌破再提醒一次；
    /// 设备条目被移除时也一并清掉（见 <see cref="ExpireTombstones"/> / <see cref="DropDevicesOf"/>，CR-23）。</summary>
    private readonly ConcurrentDictionary<string, bool> _lowBatteryNotified = new();
    /// <summary>由历史记录补出的离线占位条目（见 <see cref="RestoreOfflineTombstones"/>）；过期剔除时只能删这些，不能误删"在位但读不到"的真实设备。
    /// 用并发字典是因为删除设备在 UI 线程也会摘它，与轮询线程的增删并发。</summary>
    private readonly ConcurrentDictionary<string, byte> _tombstoneIds = new();
    private System.Threading.Timer? _pollTimer;
    private System.Threading.Timer? _sampleTimer;
    private System.Threading.Timer? _discoveryTimer;
    private System.Threading.Timer? _cleanupTimer;
    private readonly object _pollSync = new();
    /// <summary>所有 pass（整轮轮询 / 快速发现）串行执行——插件内部的字典不是线程安全的。</summary>
    private readonly SemaphoreSlim _passGate = new(1, 1);
    private Task _currentPoll = Task.CompletedTask;
    private Task _currentDiscovery = Task.CompletedTask;
    private Task _currentReconcile = Task.CompletedTask;

    /// <summary>单台设备状态有更新（含低电量提醒触发）。</summary>
    public event Action<DeviceStatus>? DeviceUpdated;

    /// <summary>设备列表可能增删（全量刷新 UI 用）。</summary>
    public event Action? DevicesChanged;

    /// <summary>低于阈值触发提醒（App 层弹 Toast/托盘变色）。</summary>
    public event Action<DeviceStatus>? LowBatteryDetected;

    /// <summary>已发现的插件（设置页动态生成开关用）。</summary>
    public IReadOnlyList<IBatteryPlugin> Plugins => _plugins;

    public DeviceManager(AppSettings settings, HistoryStore history)
    {
        _settings = settings;
        _history = history;
        _plugins = DiscoverPlugins();
        _ble.DeviceUpdated += d => Upsert(d);
        _classic.DeviceUpdated += Upsert;
        _system.DeviceUpdated += Upsert;
        // 罗技等插件的 HID++ 自发通知（插拔电）不走 60 秒轮询，直接推送
        foreach (var p in _plugins)
            p.StatusPushed += Upsert;
    }

    /// <summary>反射发现 Core 程序集里的全部插件。新增品牌 = 加一个 IBatteryPlugin 实现类文件，无需改这里。</summary>
    private static List<IBatteryPlugin> DiscoverPlugins()
    {
        var list = new List<IBatteryPlugin>();
        foreach (var type in typeof(IBatteryPlugin).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(IBatteryPlugin).IsAssignableFrom(type)) continue;
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
            {
                Log.Warn($"插件 {type.Name} 缺少无参构造，已跳过");
                continue;
            }
            try
            {
                list.Add((IBatteryPlugin)ctor.Invoke(null));
            }
            catch (Exception ex)
            {
                Log.Error($"插件 {type.Name} 实例化失败", ex);
            }
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Log.Info($"插件发现完成: {string.Join(", ", list.Select(p => p.Name))}");
        return list;
    }

    /// <summary>对外可见的设备。忽略的在这里滤掉——设备页、托盘、贴片、小组件、统计页都读这个属性，一处生效。</summary>
    public IReadOnlyCollection<DeviceStatus> Devices => _devices.Values
        .Where(d => !IsIgnored(d.Id))
        .OrderByDescending(d => d.IsOnline).ThenBy(d => d.DisplayName).ToList();

    /// <summary>忽略只影响显示与历史写入，不影响入库：条目留在 <c>_devices</c> 里，恢复后立刻可见。</summary>
    private bool IsIgnored(string id) => _settings.IgnoredDevices.ContainsKey(id);

    public DeviceStatus? Find(string id) => _devices.TryGetValue(id, out var d) ? d : null;

    /// <summary>查某 HID 接口当前被哪个插件认领。采集页据此区分「已适配」与「未适配」。</summary>
    public string? GetClaimOwner(string devicePath) => _claims.OwnerOf(devicePath);

    public RemainingTimeEstimateResult GetRemainingTimeEstimate(DeviceStatus device)
    {
        if (!_settings.RemainingTimeEstimateEnabled)
            return new(null, RemainingTimeUnavailableReason.Disabled);
        if (!device.IsOnline)
            return new(null, RemainingTimeUnavailableReason.Offline);
        if (device.Charging == ChargingState.Charging)
            return new(null, RemainingTimeUnavailableReason.Charging);
        if (device.Charging == ChargingState.Full || device.BatteryPercent >= 100)
            return new(null, RemainingTimeUnavailableReason.Full);
        if (!device.BatteryPercent.HasValue)
            return new(null, RemainingTimeUnavailableReason.MissingReading);
        if (device.BatteryPercent is <= 0 or > 100)
            return new(null, RemainingTimeUnavailableReason.InvalidReading);
        return _remainingTimeEstimates.TryGetValue(device.Id, out var result)
            ? result
            : new(null, RemainingTimeUnavailableReason.CollectingSamples);
    }

    public void RefreshPresentation()
    {
        foreach (var device in Devices) DeviceUpdated?.Invoke(device);
        DevicesChanged?.Invoke();
    }

    public void Start()
    {
        _ble.Start();
        _classic.Start();
        if (_settings.MonitorSystemBattery) _system.Start();
        RestoreOfflineTombstones();
        StartTimers();
        Log.Info("DeviceManager 已启动");
    }

    /// <summary>离线占位（墓碑）保留时长。存在的理由（蓝牙设备消失时从没进过列表、总数恒等无信息量）见 docs/project/code-flow.md §5.1。</summary>
    private static readonly TimeSpan TombstoneWindow = TimeSpan.FromHours(24);

    /// <summary>把"最近 <see cref="TombstoneWindow"/> 内有历史、当前不在位"的设备补成离线条目；只在启动后跑一次。</summary>
    private void RestoreOfflineTombstones()
    {
        try
        {
            var cutoff = DateTimeOffset.Now - TombstoneWindow;
            int added = 0;
            foreach (var stat in _history.GetDeviceStats())
            {
                if (stat.Last < cutoff) continue;
                if (_devices.ContainsKey(stat.DeviceId)) continue;
                // 用户删掉/屏蔽的设备不要再从历史补回来（历史通常已随删除清掉，这里是纵深防御）
                if (_settings.BlockedDevices.ContainsKey(stat.DeviceId)) continue;
                if (_settings.DismissedDevices.ContainsKey(stat.DeviceId)) continue;
                // 忽略的设备同样不补：它本来就不该出现在列表里
                if (_settings.IgnoredDevices.ContainsKey(stat.DeviceId)) continue;

                // 用户主动关掉的品牌插件，不要靠历史记录把它"复活"成离线条目——
                // 那会让"关了开关却还显示"的老问题换个形式回来。
                var owner = OwnerOf(stat.DeviceId);
                if (owner.Length > 0 && !IsPluginEnabledByName(owner, defaultOn: true)) continue;

                // 本机电量同理：开关关着时，历史里的系统电量记录不该把它复活成离线占位。
                if (stat.DeviceId == SystemBatteryWatcher.DeviceId && !_settings.MonitorSystemBattery) continue;

                // 2.4G 插件的设备 ID 带插件名前缀；离线占位优先复用插件声明的类型，不从品牌产品名猜。
                var pluginKind = _plugins
                    .FirstOrDefault(p => string.Equals(p.Name, owner, StringComparison.OrdinalIgnoreCase))?.Kind
                    ?? DeviceKind.Unknown;

                Upsert(new DeviceStatus
                {
                    Id = stat.DeviceId,
                    Name = stat.Name,
                    Source = SourceFromId(stat.DeviceId),
                    Kind = stat.Kind != DeviceKind.Unknown ? stat.Kind
                        : pluginKind != DeviceKind.Unknown ? pluginKind
                        : DeviceKindGuesser.Guess(stat.Name),
                    Protocol = "历史记录",
                    BatteryPercent = null,   // 不拿历史值冒充当前电量
                    Charging = ChargingState.Unknown,
                    LastUpdated = stat.Last,
                    IsOnline = false,
                });
                _tombstoneIds.TryAdd(stat.DeviceId, 0);
                added++;
            }
            if (added > 0)
                Log.Info($"已补 {added} 台离线设备占位（最近 {TombstoneWindow.TotalHours:0} 小时内有历史、当前不在位）");
        }
        catch (Exception ex)
        {
            Log.Error("恢复离线设备占位失败", ex);
        }
    }

    private static DeviceSource SourceFromId(string id) => DeviceSourceInfo.FromId(id);

    /// <summary>剔除超期的离线占位。真实设备（哪怕离线）不在此列——它们由插件继续维护。</summary>
    private void ExpireTombstones()
    {
        if (_tombstoneIds.Count == 0) return;
        var cutoff = DateTimeOffset.Now - TombstoneWindow;
        var expired = new List<string>();
        foreach (var id in _tombstoneIds.Keys)
        {
            if (_devices.TryGetValue(id, out var d) && (d.IsOnline || d.LastUpdated > cutoff)) continue;
            expired.Add(id);
        }
        if (expired.Count == 0) return;

        foreach (var id in expired)
            DropEntry(id);
        Log.Info($"离线占位已过期移除 {expired.Count} 台");
        DevicesChanged?.Invoke();
    }

    private void StartTimers()
    {
        _pollTimer?.Dispose();
        _sampleTimer?.Dispose();
        _discoveryTimer?.Dispose();
        // 首扫尽快跑（探测已是并行/短路，不再需要 2 秒缓冲）
        _pollTimer = new Timer(_ => _ = RequestPollAsync(), null,
            TimeSpan.FromMilliseconds(300), TimeSpan.FromMinutes(1));
        // 快速发现节拍：见 RequestDiscoveryAsync（设备一醒/开机就十几秒内进列表，而非苦等 60 秒主轮询）
        _discoveryTimer = new Timer(_ => _ = RequestDiscoveryAsync(), null,
            FastDiscoveryInterval, FastDiscoveryInterval);
        _sampleTimer = new Timer(_ => SafeSample(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(Math.Max(1, _settings.SampleIntervalMinutes)));
        // 历史保留定时执行：以前只在手动保存设置时清理，UI 文案写"自动清理"却是空话（CR-04）。
        // 启动后 30 秒首跑（避开启动高峰），之后每 6 小时一次。
        _cleanupTimer = new Timer(_ => CleanupHistoryNow(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromHours(6));
    }

    /// <summary>快速发现节拍。15 秒 = 雷柏心跳(3.04s)的约 5 倍（发现时延上界 ~15s）；迈从 ProbeCooldown 必须 &lt; 15s 才能每拍重探。</summary>
    private static readonly TimeSpan FastDiscoveryInterval = TimeSpan.FromSeconds(15);

    /// <summary>设置变化后调用（阈值/采样间隔/插件开关）。</summary>
    public void ApplySettings()
    {
        ApplySystemBatterySetting();
        StartTimers();
        RefreshPresentation();
        // 立刻跑一轮：否则"关掉插件"要等最多 60 秒的下一个轮询才撤掉设备，
        // 期间列表/托盘仍显示着它，用户会以为开关没生效。
        _ = RequestPollAsync();
    }

    /// <summary>同步"监控本机电量"开关：关掉时不只是停止读取，还要撤掉设备条目（理由同 <see cref="DropDevicesOf"/>：留条目会带着旧电量继续显示）。</summary>
    private void ApplySystemBatterySetting()
    {
        if (_settings.MonitorSystemBattery)
        {
            _system.Start();
            return;
        }

        _system.Stop();
        if (_devices.TryRemove(SystemBatteryWatcher.DeviceId, out _))
        {
            _remainingTimeEstimates.TryRemove(SystemBatteryWatcher.DeviceId, out _);
            _tombstoneIds.TryRemove(SystemBatteryWatcher.DeviceId, out _);
            _lowBatteryNotified.TryRemove(SystemBatteryWatcher.DeviceId, out _);
            Log.Info("已关闭本机电量监控，移除系统电量条目");
            DevicesChanged?.Invoke();
        }
    }

    /// <summary>手动触发一轮立即轮询（UI 刷新按钮）。返回的 Task 在整轮结束后完成（含 BLE 异步补读）；已有轮次在跑时复用其 Task，不静默丢弃。</summary>
    public Task RequestPollAsync()
    {
        lock (_pollSync)
        {
            if (!_currentPoll.IsCompleted) return _currentPoll;
            _currentPoll = RunExclusiveAsync(PollOnceAsync);
            return _currentPoll;
        }
    }

    /// <summary>
    /// 快速发现：让两类插件跑一遍**只认领、不轮询电量**的发现探测（不碰 BLE / 经典蓝牙）：
    /// SupportsFastDiscovery = 纯被动零写入（雷柏）；SupportsDiscoveryProbe = 要写帧但由 ProbeCooldown 兜住（迈从）。
    /// 把这类设备的发现时延从 ~60s 压到 ~15s；主轮询在跑时直接返回它。详见 docs/project/code-flow.md §2.1。
    /// </summary>
    public Task RequestDiscoveryAsync()
    {
        lock (_pollSync)
        {
            if (!_currentPoll.IsCompleted) return _currentPoll;
            if (!_currentDiscovery.IsCompleted) return _currentDiscovery;
            _currentDiscovery = RunExclusiveAsync(DiscoverOnlyAsync);
            return _currentDiscovery;
        }
    }

    /// <summary>
    /// 插拔事件：对所有启用插件做一遍"消失检测"（零写入），拔出的设备立即下线。
    /// **只拆不认领**——插入交给 15 秒/60 秒节拍或主界面手动刷新（<see cref="RequestPollAsync"/> 会 Attach）。见 code-flow.md §2.3。
    /// </summary>
    public Task RequestReconcileAsync()
    {
        lock (_pollSync)
        {
            if (!_currentPoll.IsCompleted) return _currentPoll;   // 完整轮询本来就会拆
            if (!_currentReconcile.IsCompleted) return _currentReconcile;
            _currentReconcile = RunExclusiveAsync(ReconcileOnceAsync);
            return _currentReconcile;
        }
    }

    private Task ReconcileOnceAsync()
    {
        try
        {
            ReconcileAllEnabled(HidDeviceScanner.Scan());
        }
        catch (Exception ex)
        {
            Log.Error("插拔移除检测异常", ex);
        }
        return Task.CompletedTask;
    }

    /// <summary>对所有启用的插件跑一遍"消失检测"（零写入），返回是否有设备被拆。</summary>
    private bool ReconcileAllEnabled(IReadOnlyList<HidDeviceInfo> hidDevices)
    {
        bool changed = false;
        foreach (var plugin in _plugins)
        {
            if (!IsPluginEnabled(plugin)) continue;
            try
            {
                foreach (var d in plugin.ReconcileRemovals(hidDevices))
                {
                    Upsert(d);
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"插件 {plugin.Name} 移除检测失败", ex);
            }
        }
        return changed;
    }

    /// <summary>把一段 pass 放到线程池并**串行**执行（闸门保证同一时刻只有一段 pass 在动插件）；用 Task.Run 因调用方可能是 UI 线程而 HID 探测是阻塞 I/O。</summary>
    private Task RunExclusiveAsync(Func<Task> body) => Task.Run(async () =>
    {
        try
        {
            await _passGate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;   // 正在退出，不再执行
        }

        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            try { _passGate.Release(); } catch (ObjectDisposedException) { /* 退出时竞态，忽略 */ }
        }
    });

    private bool IsPluginEnabled(IBatteryPlugin plugin)
        => IsPluginEnabledByName(plugin.Name, defaultOn: false);

    /// <summary>按插件名查开关（大小写不敏感）。<paramref name="defaultOn"/> = 没有该开关时的兜底：插件缺 = 关；蓝牙监视器（ble:/btc:）无开关必须放行，否则墓碑恢复会把蓝牙设备全挡掉。</summary>
    private bool IsPluginEnabledByName(string pluginName, bool defaultOn)
    {
        foreach (var kv in _settings.PluginEnabled)
            if (string.Equals(kv.Key, pluginName, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return defaultOn;
    }

    /// <summary>设备 Id 的归属前缀（<c>{插件名小写}:</c>），用来判断条目属于哪个插件，省一套归属账本。</summary>
    private static string OwnerOf(string deviceId)
    {
        int i = deviceId.IndexOf(':');
        return i <= 0 ? "" : deviceId[..i];
    }

    /// <summary>插件被禁用后，把它此前认领的设备从列表里撤掉。`_devices` 从不移除条目、`Detach()` 又不上报状态，不显式撤就会带着旧电量继续显示（2026-10-06 用户复报）。</summary>
    private void DropDevicesOf(string pluginName)
    {
        var prefix = pluginName.ToLowerInvariant() + ":";
        var dropped = _devices.Keys
            .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        if (dropped.Count == 0) return;

        foreach (var id in dropped)
            DropEntry(id);
        Log.Info($"插件 {pluginName} 已禁用，移除其 {dropped.Count} 台设备");
        DevicesChanged?.Invoke();
    }

    /// <summary>把一台设备从内存各表里摘干净。不碰插件、不碰历史：设备物理上还在，插件要继续盯着它何时恢复。</summary>
    private void DropEntry(string id)
    {
        _devices.TryRemove(id, out _);
        _remainingTimeEstimates.TryRemove(id, out _);
        _tombstoneIds.TryRemove(id, out _);
        _lowBatteryNotified.TryRemove(id, out _);
    }

    /// <summary>已屏蔽设备快照（「已屏蔽设备」列表用），按屏蔽时间倒序。</summary>
    public IReadOnlyList<(string Id, BlockedDevice Info)> BlockedDevices => _settings.BlockedDevices
        .OrderByDescending(kv => kv.Value.BlockedAt)
        .Select(kv => (kv.Key, kv.Value))
        .ToList();

    /// <summary>
    /// 删除一台离线设备：**名单先落盘再摘条目**，反过来会被插件那一轮重报插进空档；历史由调用方删（要 Task.Run）。
    /// <paramref name="block"/> = 永久移除（写黑名单，以后不再扫描）。见 docs/project/code-flow.md §5.2。
    /// </summary>
    public DeviceRemoveResult RemoveOfflineDevice(string id, bool block)
    {
        if (!_devices.TryGetValue(id, out var device)) return DeviceRemoveResult.NotFound;
        if (device.Source == DeviceSource.System) return DeviceRemoveResult.SystemDevice;
        if (device.IsOnline) return DeviceRemoveResult.Online;

        bool hadDismiss = _settings.DismissedDevices.TryGetValue(id, out long priorDismiss);
        _settings.BlockedDevices.TryGetValue(id, out var priorBlock);

        if (block)
        {
            _settings.BlockedDevices[id] = new BlockedDevice { Name = device.DisplayName, BlockedAt = DateTimeOffset.Now };
            _settings.DismissedDevices.TryRemove(id, out _);   // 黑名单优先，留着删除标记只会在解除屏蔽后继续挡
        }
        else
        {
            _settings.DismissedDevices[id] = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        if (!_settings.Save())
        {
            // 名单没落盘就不能动状态：否则重启后设备回来，用户以为删掉了
            if (priorBlock == null) _settings.BlockedDevices.TryRemove(id, out _);
            else _settings.BlockedDevices[id] = priorBlock;
            if (hadDismiss) _settings.DismissedDevices[id] = priorDismiss;
            else _settings.DismissedDevices.TryRemove(id, out _);
            Log.Error($"删除设备失败：settings.json 写入失败 {device.DisplayName}");
            return DeviceRemoveResult.SaveFailed;
        }

        DropEntry(id);
        DevicesChanged?.Invoke();
        Log.Info(block
            ? $"已永久移除设备: {device.DisplayName} ({id})"
            : $"已删除离线设备: {device.DisplayName} ({id})");
        return DeviceRemoveResult.Removed;
    }

    /// <summary>解除屏蔽；设备在位时立刻排一轮轮询，让它尽快回到列表。</summary>
    public bool UnblockDevice(string id)
    {
        if (!_settings.BlockedDevices.TryRemove(id, out var removed)) return false;
        _settings.DismissedDevices.TryRemove(id, out _);
        if (!_settings.Save())
        {
            _settings.BlockedDevices[id] = removed;
            Log.Error($"解除屏蔽失败：settings.json 写入失败 {id}");
            return false;
        }
        Log.Info($"已解除屏蔽: {removed.Name} ({id})");
        _ = RequestPollAsync();
        return true;
    }

    /// <summary>已忽略设备快照（「已忽略设备」列表用），按忽略时间倒序。</summary>
    public IReadOnlyList<(string Id, IgnoredDevice Info)> IgnoredDevices => _settings.IgnoredDevices
        .OrderByDescending(kv => kv.Value.IgnoredAt)
        .Select(kv => (kv.Key, kv.Value))
        .ToList();

    /// <summary>忽略一台设备：在线也能用（直连电源的设备永远在线，删除那条路走不到）。
    /// 名单先落盘再发事件，写盘失败就回滚——否则内存里忽略了、重启又冒出来。</summary>
    public bool IgnoreDevice(string id)
    {
        if (!_devices.TryGetValue(id, out var device)) return false;
        _settings.IgnoredDevices[id] = new IgnoredDevice
        {
            Name = device.DisplayName,
            IgnoredAt = DateTimeOffset.Now,
        };
        if (!_settings.Save())
        {
            _settings.IgnoredDevices.TryRemove(id, out _);
            Log.Error($"忽略设备失败：settings.json 写入失败 {device.DisplayName}");
            return false;
        }
        Log.Info($"已忽略设备: {device.DisplayName} ({id})");
        DevicesChanged?.Invoke();
        return true;
    }

    /// <summary>恢复被忽略的设备。数据一直在 <c>_devices</c> 里，摘掉名单就立刻回到列表。</summary>
    public bool UnignoreDevice(string id)
    {
        if (!_settings.IgnoredDevices.TryRemove(id, out var removed)) return false;
        if (!_settings.Save())
        {
            _settings.IgnoredDevices[id] = removed;
            Log.Error($"恢复忽略失败：settings.json 写入失败 {id}");
            return false;
        }
        Log.Info($"已恢复设备: {removed.Name} ({id})");
        DevicesChanged?.Invoke();
        return true;
    }

    /// <summary>只认领、不读电量（见 RequestDiscoveryAsync）。各插件慢探测并行，Upsert 仍在本线程。</summary>
    private async Task DiscoverOnlyAsync()
    {
        try
        {
            var hidDevices = HidDeviceScanner.Scan();

            // 先对所有启用的插件做"消失检测"（零写入）。不进 15 秒节拍的插件（罗技/北通/AULA/GANSS）
            // 本来要等 60 秒主轮询才拆会话，拔掉后条目一直显示在线。
            ReconcileAllEnabled(hidDevices);

            var tasks = new List<Task<IReadOnlyList<DeviceStatus>>>();
            var nudgeable = new List<IBatteryPlugin>();
            foreach (var plugin in _plugins)
            {
                // 零写入插件（雷柏）与"写入由 ProbeCooldown 兜住"的插件（迈从）都参与；
                // 其余会写帧的插件仍只吃 60 秒主轮询，避免成倍抬高写入频率。
                if (!plugin.SupportsFastDiscovery && !plugin.SupportsDiscoveryProbe) continue;
                if (!IsPluginEnabled(plugin)) continue;
                nudgeable.Add(plugin);
                tasks.Add(AttachSafeAsync(plugin, hidDevices, "快速发现"));
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (var claimed in results)
            {
                foreach (var d in claimed)
                    Upsert(d);
            }

            // 认领之后再半开探活：刚重连的设备由上面的 Upsert 处理，这里只捞"还在停写退避里"的。
            // 没有被动监听的纯查询型插件（ATK 鼠标）靠它感知"设备睡醒了"，否则最长要干等 5 分钟退避。
            foreach (var plugin in nudgeable)
            {
                try
                {
                    plugin.NudgeHalfOpen();
                }
                catch (Exception ex)
                {
                    Log.Error($"插件 {plugin.Name} 半开探活失败", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("快速发现周期异常", ex);
        }
    }

    private async Task PollOnceAsync()
    {
        try
        {
            var hidDevices = HidDeviceScanner.Scan();

            var attachTasks = new List<Task<(IBatteryPlugin Plugin, IReadOnlyList<DeviceStatus> Claimed)>>();
            foreach (var plugin in _plugins)
            {
                bool enabled = IsPluginEnabled(plugin);
                try
                {
                    if (!enabled)
                    {
                        plugin.Detach();
                        _claims.ReleaseAll(plugin.Name);
                        DropDevicesOf(plugin.Name);
                        continue;
                    }
                    attachTasks.Add(AttachLabeledAsync(plugin, hidDevices));
                }
                catch (Exception ex)
                {
                    Log.Error($"插件 {plugin.Name} 轮询失败", ex);
                }
            }

            var attached = await Task.WhenAll(attachTasks).ConfigureAwait(false);
            foreach (var (plugin, claimed) in attached)
            {
                try
                {
                    // Attach 返回值立即 Upsert：新设备即时进 UI；FreshlyClaimed 本轮 Poll 跳过。
                    foreach (var d in claimed)
                        Upsert(d);
                    foreach (var d in plugin.Poll())
                        Upsert(d);
                }
                catch (Exception ex)
                {
                    Log.Error($"插件 {plugin.Name} 轮询失败", ex);
                }
            }

            // 必须 await：以前 fire-and-forget 会让"刷新"在 BLE 侧还没跑完就返回
            await _ble.PollConnectedAsync();
            _classic.RefreshBattery();
            if (_settings.MonitorSystemBattery) _system.Refresh();
            DevicesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("轮询周期异常", ex);
        }
    }

    private async Task<IReadOnlyList<DeviceStatus>> AttachSafeAsync(
        IBatteryPlugin plugin, IReadOnlyList<HidDeviceInfo> hidDevices, string label)
    {
        try
        {
            return await plugin.AttachAsync(hidDevices, _claims).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error($"插件 {plugin.Name} {label}失败", ex);
            return Array.Empty<DeviceStatus>();
        }
    }

    private async Task<(IBatteryPlugin Plugin, IReadOnlyList<DeviceStatus> Claimed)> AttachLabeledAsync(
        IBatteryPlugin plugin, IReadOnlyList<HidDeviceInfo> hidDevices)
    {
        var claimed = await AttachSafeAsync(plugin, hidDevices, "认领").ConfigureAwait(false);
        return (plugin, claimed);
    }

    /// <summary>立即按保留天数清理历史。设置保存后调用，也由 6 小时定时任务调用。</summary>
    public void CleanupHistoryNow()
    {
        try
        {
            var removed = _history.CleanupOlderThan(DateTimeOffset.Now.AddDays(-_settings.HistoryRetentionDays));
            // 定时任务大多数时候删 0 条，只有真删了才记一行，避免每 6 小时刷一条无信息量日志
            if (removed > 0) Log.Info($"历史清理完成，删除 {removed} 条");
        }
        catch (Exception ex)
        {
            Log.Error("历史清理失败", ex);
        }
    }

    private void SafeSample()
    {
        try
        {
            foreach (var d in _devices.Values)
            {
                if (d.IsOnline && d.BatteryPercent.HasValue && !IsIgnored(d.Id))
                    _history.Insert(DeviceCustomizationResolver.ApplyName(d, _settings.DeviceCustomizations));
                RefreshRemainingTimeEstimate(d, notify: true);
            }
            ExpireTombstones();
        }
        catch (Exception ex)
        {
            Log.Error("历史采样失败", ex);
        }
    }

    private void Upsert(DeviceStatus incoming)
    {
        // 同一台设备的经典/LE 端点会各报一次（手机最明显），先收敛成一条再走后面的流程
        if (AbsorbDualChannel(ref incoming)) return;

        // 删除过的设备每轮都会被插件重新上报（接口还在位），黑名单与删除标记在这里一刀切。
        if (DeviceRemoval.ShouldDrop(_settings.BlockedDevices, _settings.DismissedDevices, incoming.Id, incoming.IsOnline))
            return;
        if (DeviceRemoval.ShouldClearDismissal(_settings.DismissedDevices, incoming.Id, incoming.IsOnline))
        {
            _settings.DismissedDevices.TryRemove(incoming.Id, out _);
            _settings.Save();
            Log.Info($"设备重新上线，已解除删除标记: {incoming.DisplayName}");
        }

        // 数据源（插件/watcher）会复用并原地改写同一个 DeviceStatus 实例。直接把它存进 _devices，
        // 后续再比较就变成"同一对象和自己比"，changed 恒为假（历史即写 / DeviceUpdated / 骤降保护全失效）。
        // 所以先快照 incoming 当"新值"，库里存的始终是独立副本当"旧值"——详见 docs/project/architecture.md §6。
        var snapshot = incoming.Clone();
        var status = _devices.GetOrAdd(incoming.Id, snapshot);
        bool isNew = ReferenceEquals(status, snapshot);

        // 新设备立即通知 UI（watcher/插件首轮上报不等下一分钟轮询）
        if (isNew)
        {
            DevicesChanged?.Invoke();
            DeviceUpdated?.Invoke(status);
        }

        int? previousPercent = isNew ? null : status.BatteryPercent;
        var previousKind = status.Kind;

        bool changed = DeviceChangeDetector.HasChanged(status, snapshot);
        // 原地同步（Kind/Protocol 的"声明覆盖猜测"规则见 DeviceStatusMerger）
        DeviceStatusMerger.Apply(status, snapshot);

        // 占位条目一旦被真实来源接管（重新上线），就不再按占位规则过期剔除
        if (status.IsOnline) _tombstoneIds.TryRemove(status.Id, out _);

        // 忽略的设备照常入库（恢复要立刻回来），但不再产生新采样、不再提醒
        bool ignored = IsIgnored(status.Id);

        if (changed)
        {
            // 电量变化立即写一条历史（含从离线恢复）
            if (status.BatteryPercent.HasValue && !ignored)
                _history.Insert(DeviceCustomizationResolver.ApplyName(status, _settings.DeviceCustomizations));
            DeviceUpdated?.Invoke(status);
        }
        else if (status.Kind != previousKind)
        {
            // 类型后解析出来（Unknown → Mouse）：电量没变也要换图标，但不写历史
            DeviceUpdated?.Invoke(status);
        }

        if (!ignored) CheckLowBattery(status, previousPercent);
        RefreshRemainingTimeEstimate(status, notify: true);
    }

    /// <summary>同一台设备的经典与 LE 端点会各报一次（手机最明显），按 MAC 收敛到经典那条；返回 true 表示本条已被吸收、不再入库。</summary>
    private bool AbsorbDualChannel(ref DeviceStatus incoming)
    {
        var mac = DualChannelId.Mac(incoming.Id);
        if (mac == null) return false;

        if (DualChannelId.IsBle(incoming.Id))
        {
            var owner = FindPeer(mac, classic: true);
            // 没有经典端点（BLE 鼠标/耳机），LE 就是唯一一条，正常入库
            if (owner == null) return false;
            // 经典条目已在列表里：LE 端点只贡献电量，不单独占一行
            if (!incoming.BatteryPercent.HasValue) return true;

            incoming = new DeviceStatus
            {
                Id = owner.Id,
                Name = owner.Name,
                Source = owner.Source,
                Kind = owner.Kind,
                Protocol = owner.Protocol,
                BatteryPercent = incoming.BatteryPercent,
                Charging = incoming.Charging,
                IsOnline = true,
                LastUpdated = incoming.LastUpdated,
            };
            return false;
        }

        // 经典端点优先：摘掉可能先一步入库的 LE 端点
        var ble = FindPeer(mac, classic: false);
        if (ble != null && _devices.TryRemove(ble.Id, out _))
        {
            _tombstoneIds.TryRemove(ble.Id, out _);
            _remainingTimeEstimates.TryRemove(ble.Id, out _);
            _lowBatteryNotified.TryRemove(ble.Id, out _);
            Log.Info($"蓝牙双通道合并: {incoming.DisplayName}（丢弃 LE 端点）");
            DevicesChanged?.Invoke();
        }
        return false;
    }

    private DeviceStatus? FindPeer(string mac, bool classic)
    {
        foreach (var d in _devices.Values)
        {
            if (DualChannelId.IsClassic(d.Id) != classic) continue;
            if (DualChannelId.Mac(d.Id) == mac) return d;
        }
        return null;
    }

    private void RefreshRemainingTimeEstimate(DeviceStatus device, bool notify)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var samples = _history.Query(device.Id, now - RemainingTimeEstimator.Window, now);
            var result = device.BatteryPercent is { } percent
                ? RemainingTimeEstimator.Evaluate(samples, percent, now)
                : new(null, RemainingTimeUnavailableReason.MissingReading);
            bool changed = !_remainingTimeEstimates.TryGetValue(device.Id, out var prior) || prior != result;
            _remainingTimeEstimates[device.Id] = result;
            if (notify && changed) DeviceUpdated?.Invoke(device);
        }
        catch (Exception ex)
        {
            Log.Error($"剩余时间估算失败: {device.DisplayName}", ex);
            var failure = new RemainingTimeEstimateResult(null, RemainingTimeUnavailableReason.InvalidHistory);
            bool changed = !_remainingTimeEstimates.TryGetValue(device.Id, out var prior) || prior != failure;
            _remainingTimeEstimates[device.Id] = failure;
            if (notify && changed) DeviceUpdated?.Invoke(device);
        }
    }

    /// <summary>低电量提醒的骤降保护阈值（百分点）：降幅超过它先暂缓提醒，等下次读数确认。</summary>
    private const int ImplausibleDropPoints = 20;

    private void CheckLowBattery(DeviceStatus status, int? previousPercent)
    {
        int threshold = _settings.LowBatteryThreshold;
        if (threshold <= 0)
        {
            // 关掉提醒时复位，重新开启后第一条低电量读数就能正常提醒
            _lowBatteryNotified.TryRemove(status.Id, out _);
            return;
        }
        if (!status.IsOnline) return;
        if (status.BatteryPercent is not { } percent) return;

        // 回到阈值之上（或充电/满电）→ 复位"已提醒"标记，下次跌破再提醒一次。
        // 旧实现记的是"精确百分比且永不复位"，导致 20% 提醒后 19% 又提醒（逐 1% 刷屏），
        // 而充满到 100% 再放电到 20% 反而不提醒（CR-15）。
        if (percent > threshold || DeviceStatusFormatter.IsChargingOrFull(status.Charging))
        {
            _lowBatteryNotified.TryRemove(status.Id, out _);
            return;
        }

        // 骤降保护：键盘休眠/关机瞬间的伪读数（1%/0%）会让电量瞬间"掉"到阈值以下。
        // 真掉电不可能一次掉 20 个点，所以这种降幅先不提醒，等下次读数确认。
        if (DeviceChangeDetector.IsImplausibleDrop(previousPercent, percent, ImplausibleDropPoints))
        {
            Log.Warn($"低电量提醒暂缓: {status.DisplayName} {previousPercent}%→{percent}% 降幅异常，等下次读数确认");
            return;
        }

        // 跨过阈值只提醒一次（TryAdd 兼任"未提醒过"的判定，天然幂等）
        if (_lowBatteryNotified.TryAdd(status.Id, true))
        {
            LowBatteryDetected?.Invoke(status);
            Log.Info($"低电量提醒: {status.DisplayName} {percent}%");
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _sampleTimer?.Dispose();
        _discoveryTimer?.Dispose();
        _cleanupTimer?.Dispose();
        foreach (var p in _plugins)
        {
            p.StatusPushed -= Upsert;
            p.Detach();
        }
        _ble.Dispose();
        _classic.Dispose();
        _system.Dispose();
        _passGate.Dispose();
    }
}
