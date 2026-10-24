using System.Collections.Concurrent;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace VigilWatt.Core.Ble;

/// <summary>
/// 功 能： 经典蓝牙（BR/EDR）监视器：单开一路 watcher（BLE watcher 覆盖不到音频设备），提供连接状态 + 电量 + 设备类型。
/// 2026/10/06 15:53:36 xliu
/// 详见 docs/project/code-flow.md §3、docs/dev/runtime-pitfalls.md §3.1。
/// </summary>
public sealed class BluetoothClassicWatcher : IDisposable
{
    private DeviceWatcher? _watcher;
    private bool _started;
    private readonly ConcurrentDictionary<string, DeviceStatus> _devices = new();

    public event Action<DeviceStatus>? DeviceUpdated;

    /// <summary>电量属性候选：AepBattery_Level 在本机 BLE selector 下不可用（runtime-pitfalls §3），
    /// 经典蓝牙 selector 下值得实测一次；抛异常就自动回退到只显示连接状态，无副作用。</summary>
    private static readonly string[][] BatteryPropCandidates =
    {
        new[] { WatcherProps.Name, "System.Devices.AepBattery_Level", WatcherProps.Connected },
        new[] { WatcherProps.Name, WatcherProps.Connected },
    };

    private string? _batteryProp;

    public void Start()
    {
        if (_started) return;
        _started = true;

        var selector = BluetoothDevice.GetDeviceSelector();
        DeviceWatcher? watcher = null;
        foreach (var props in BatteryPropCandidates)
        {
            try
            {
                watcher = DeviceInformation.CreateWatcher(selector, props, DeviceInformationKind.AssociationEndpoint);
                if (props.Length == 3) _batteryProp = props[1];
                Log.Info($"经典蓝牙 watcher 属性集可用: {string.Join(", ", props)}");
                break;
            }
            catch (Exception ex)
            {
                Log.Warn($"经典蓝牙 watcher 属性集不可用 [{string.Join(", ", props)}]: {ex.Message}");
                watcher = null;
            }
        }
        if (watcher == null)
        {
            Log.Error("经典蓝牙 DeviceWatcher 无法创建，经典蓝牙监控不可用");
            return;
        }

        watcher.Added += (w, info) => HandleDevice(info.Id, info.Properties, info.Name);
        watcher.Updated += (w, update) => HandleDevice(update.Id, update.Properties, null);
        watcher.Removed += (w, update) => HandleRemoved(update.Id);
        watcher.Start();
        _watcher = watcher;
        Log.Info("经典蓝牙 DeviceWatcher 已启动");
    }

    private void HandleDevice(
        string id, IReadOnlyDictionary<string, object> props, string? name, bool connectionVerified = false)
    {
        // 永久移除的设备：不再跟踪、不再读设备节点（否则每次 Updated 都会白查一轮注册表）
        if (DeviceBlocklist.IsBlocked($"btc:{id}"))
        {
            if (_devices.TryRemove(id, out var blocked))
                Log.Info($"经典蓝牙设备已永久移除，停止跟踪: {blocked.DisplayName}");
            return;
        }

        // Updated 事件只带变化的属性：IsConnected 缺失时沿用现状，不能当成断开
        bool? connected = WatcherProps.ReadConnected(props);

        if (connected == false)
        {
            if (_devices.TryGetValue(id, out var tracked) && tracked.IsOnline)
            {
                tracked.IsOnline = false;
                tracked.LastUpdated = DateTimeOffset.Now;
                Log.Info($"经典蓝牙设备离线: {tracked.DisplayName}");
                DeviceUpdated?.Invoke(tracked);
            }
            return;
        }

        // 新增设备必须"确认真的连着"才进列表：配对但已断开的耳机，其 HFP 网关节点上仍留着上次的电量
        // （真机 W820NB 明明没连，却以 50% 在线出现在列表里）。
        bool isNew = !_devices.TryGetValue(id, out var status);
        if (isNew && !connectionVerified)
        {
            _ = VerifyConnectionAndAddAsync(id, props, name);
            return;
        }

        // 设备节点信息一次取全：电量 + Windows 设备类别 + 服务 UUID（类型判定要用；整表 5 秒缓存）
        var nodeInfo = TryGetDevNodeInfo(id);

        int? percent = WatcherProps.ReadBattery(props, _batteryProp);

        // AEP 属性拿不到时，走 HFP 网关设备节点的 DEVPKEY_Device_BatteryLevel
        percent ??= nodeInfo?.BatteryPercent;

        if (status == null)
        {
            status = new DeviceStatus
            {
                Id = $"btc:{id}",
                Name = "",
                Source = DeviceSource.BluetoothClassic,
                // ⚠️ 不预设成 Headset（本 watcher 也收鼠标/键盘/手机），类型交给 ApplyKindAndProtocol
                Kind = DeviceKind.Unknown,
                Protocol = "经典蓝牙",
            };
            _devices[id] = status;
        }

        ApplyName(status, id, name, props);
        bool kindChanged = ApplyKindAndProtocol(status, nodeInfo);

        // 只在有意义的变化时通知：新上线 / 从离线恢复 / 电量数值变化 / 类型刚解析出来
        bool wasOffline = !status.IsOnline;
        bool batteryChanged = percent.HasValue && status.BatteryPercent != percent;
        bool shouldNotify = isNew || wasOffline || batteryChanged || kindChanged;

        status.IsOnline = true;
        status.LastUpdated = DateTimeOffset.Now;
        if (percent.HasValue) status.BatteryPercent = percent;

        if (shouldNotify)
        {
            if (isNew) Log.Info($"经典蓝牙设备发现: {status.DisplayName} 电量={(percent.HasValue ? $"{percent}%" : "--")}");
            else if (wasOffline) Log.Info($"经典蓝牙设备上线: {status.DisplayName}");
            DeviceUpdated?.Invoke(status);
        }
    }

    /// <summary>新增设备前的连接确认：<c>Added</c> 首帧的 <c>IsConnected</c> 可能缺失，不能只看它；直接问系统要"当前已连接"集合（只读枚举，不建立连接）。</summary>
    private async Task VerifyConnectionAndAddAsync(
        string id, IReadOnlyDictionary<string, object> props, string? name)
    {
        bool? aep = WatcherProps.ReadConnected(props);
        if (aep == false)   // AEP 明说断开，不用再问
        {
            Log.Info($"经典蓝牙设备未连接，不进列表: {id}");
            return;
        }

        bool connected;
        try
        {
            var list = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                new[] { WatcherProps.Connected }, DeviceInformationKind.AssociationEndpoint);
            connected = list.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            // 枚举失败时退回 AEP 属性；两者都说不出"在线"就不进列表
            Log.Warn($"经典蓝牙连接状态查询失败，退回 AEP 属性: {id} {ex.Message}");
            if (aep != true) return;
            connected = true;
        }

        if (!connected)
        {
            Log.Info($"经典蓝牙设备未连接，不进列表: {id}（AEP IsConnected={aep?.ToString() ?? "缺"}）");
            return;
        }

        HandleDevice(id, props, name, connectionVerified: true);
    }

    /// <summary>取显示名，来源按可靠性排序：WinRT 名字 → <c>ItemNameDisplay</c> → 注册表配对名 → 格式化 MAC；每个候选都过 <see cref="ClassicDeviceName.IsPlaceholder"/>（WinRT 在友好名未解析时会把 AEP 设备 ID 当名字返回）。</summary>
    private static void ApplyName(
        DeviceStatus status, string aepId, string? infoName, IReadOnlyDictionary<string, object> props)
    {
        if (!ClassicDeviceName.IsPlaceholder(infoName))
        {
            status.Name = infoName!.Trim();
            return;
        }

        if (props.TryGetValue(WatcherProps.Name, out var nameObj) &&
            !ClassicDeviceName.IsPlaceholder(nameObj?.ToString()))
        {
            status.Name = nameObj!.ToString()!.Trim();
            return;
        }

        // 已经解析出真名了就别覆盖（Updated 事件会反复进来）
        if (!ClassicDeviceName.IsPlaceholder(status.Name)) return;

        string? mac = ClassicDevNodeReader.MacFromAepId(aepId);
        var paired = ClassicPairedNames.TryGet(mac);
        status.Name = string.IsNullOrEmpty(paired) ? ClassicDeviceName.FallbackName(mac) : paired!;
    }

    private void HandleRemoved(string id)
    {
        // 设备不再匹配 selector（断开/取消配对）：发离线而不是静默删除，保持 DeviceManager 里有据可查
        if (_devices.TryRemove(id, out var tracked) && tracked.IsOnline)
        {
            tracked.IsOnline = false;
            tracked.LastUpdated = DateTimeOffset.Now;
            Log.Info($"经典蓝牙设备移除: {tracked.DisplayName}");
            DeviceUpdated?.Invoke(tracked);
        }
    }

    /// <summary>按 AEP ID 里的设备 MAC 取该设备的经典蓝牙节点信息（电量 / 设备类别 / 服务 UUID）；查不到返回 null。</summary>
    private static ClassicDevNodeInfo? TryGetDevNodeInfo(string aepId)
    {
        var mac = ClassicDevNodeReader.MacFromAepId(aepId);
        if (mac == null) return null;
        return ClassicDevNodeReader.ReadByMac().TryGetValue(mac, out var info) ? info : null;
    }

    /// <summary>解析设备类型与协议描述，返回是否有变化。名字/类别常后到，所以只在 Unknown 时重试，非 Unknown 即锁定。</summary>
    private static bool ApplyKindAndProtocol(DeviceStatus status, ClassicDevNodeInfo? info)
    {
        if (status.Kind != DeviceKind.Unknown) return false;

        var kind = ClassicDeviceKindResolver.Resolve(status.Name, info?.Categories, info?.ServiceUuids);
        if (kind == DeviceKind.Unknown) return false;

        status.Kind = kind;
        status.Protocol = ClassicDeviceKindResolver.DescribeProtocol(info?.ServiceUuids);
        Log.Info($"经典蓝牙设备类型: {status.DisplayName} → {kind}（{status.Protocol}）");
        return true;
    }

    /// <summary>周期刷新（轮询时调用）：watcher 只在属性变化时推事件，这里主动重读一次，有变化才通知。</summary>
    public void RefreshBattery()
    {
        if (!_started) return;
        foreach (var (id, status) in _devices.ToArray())
        {
            if (!status.IsOnline) continue;

            // 名字可能是后来才解析出来的（WinRT 首次 Added 常常只给设备 ID），每轮补一次
            if (ClassicDeviceName.IsPlaceholder(status.Name))
            {
                var mac = ClassicDevNodeReader.MacFromAepId(id);
                var paired = ClassicPairedNames.TryGet(mac);
                if (!string.IsNullOrEmpty(paired))
                {
                    status.Name = paired!;
                    status.LastUpdated = DateTimeOffset.Now;
                    Log.Info($"经典蓝牙设备名补全: {status.DisplayName}");
                    DeviceUpdated?.Invoke(status);
                }
            }

            var nodeInfo = TryGetDevNodeInfo(id);
            bool changed = ApplyKindAndProtocol(status, nodeInfo);

            if (nodeInfo?.BatteryPercent is { } percent && status.BatteryPercent != percent)
            {
                status.BatteryPercent = percent;
                Log.Info($"经典蓝牙电量(HFP 节点): {status.DisplayName} {percent}%");
                changed = true;
            }

            if (!changed) continue;

            status.LastUpdated = DateTimeOffset.Now;
            DeviceUpdated?.Invoke(status);
        }
    }

    public IReadOnlyList<DeviceStatus> Snapshot() => _devices.Values.ToList();

    public void Dispose()
    {
        _watcher?.Stop();
    }
}
