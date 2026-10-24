using System.Collections.Concurrent;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace VigilWatt.Core.Ble;

/// <summary>
/// 功 能： BLE 电量监视器：DeviceWatcher 监听系统缓存电量 + 主动 GATT 直读 0x180F/0x2A19 兜底（详见 docs/project/code-flow.md §3）。
/// 2026/10/06 15:53:36 xliu
/// </summary>
public sealed class BleBatteryWatcher : IDisposable
{
    private DeviceWatcher? _watcher;
    private bool _started;
    private readonly ConcurrentDictionary<string, DeviceStatus> _devices = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastGattAttempt = new();
    private readonly SemaphoreSlim _gattGate = new(1, 1);

    public event Action<DeviceStatus>? DeviceUpdated;

    /// <summary>不同 Windows 版本的电量属性名不同，逐个尝试，成功的组合记下来。</summary>
    private static readonly string[][] BatteryPropCandidates =
    {
        new[] { WatcherProps.Name, "System.Devices.AepBattery_Level", WatcherProps.Connected },
        new[] { WatcherProps.Name, "System.Devices.Aep.Battery_Level", WatcherProps.Connected },
        new[] { WatcherProps.Name, WatcherProps.Connected },
    };

    private string? _batteryProp;

    public void Start()
    {
        if (_started) return;
        _started = true;

        var selector = BluetoothLEDevice.GetDeviceSelector();
        DeviceWatcher? watcher = null;
        foreach (var props in BatteryPropCandidates)
        {
            try
            {
                watcher = DeviceInformation.CreateWatcher(selector, props, DeviceInformationKind.AssociationEndpoint);
                if (props.Length == 3) _batteryProp = props[1];
                Log.Info($"BLE watcher 属性集可用: {string.Join(", ", props)}");
                break;
            }
            catch (Exception ex)
            {
                Log.Warn($"BLE watcher 属性集不可用 [{string.Join(", ", props)}]: {ex.Message}");
                watcher = null;
            }
        }
        if (watcher == null)
        {
            Log.Error("BLE DeviceWatcher 无法创建，BLE 监控不可用");
            return;
        }

        watcher.Added += (w, info) => HandleDevice(info.Id, info.Properties, info.Name);
        watcher.Updated += (w, update) => HandleDevice(update.Id, update.Properties, null);
        watcher.Removed += (w, update) => HandleRemoved(update.Id);
        watcher.Start();
        _watcher = watcher;
        Log.Info("BLE DeviceWatcher 已启动");
    }

    private void HandleDevice(string id, IReadOnlyDictionary<string, object> props, string? name)
    {
        // 永久移除的设备：不再跟踪、不再 GATT 直读（否则每次 Updated 都会白读一轮）
        if (DeviceBlocklist.IsBlocked($"ble:{id}"))
        {
            if (_devices.TryRemove(id, out var blocked))
                Log.Info($"BLE 设备已永久移除，停止跟踪: {blocked.DisplayName}");
            return;
        }

        // Updated 只带变化的属性：IsConnected 缺失时必须沿用现状，不能默认 false
        // （真机 2026-10-08：戴尔 WK717 已连接却被后续 Updated 误判离线，与经典蓝牙同坑）
        bool? connected = WatcherProps.ReadConnected(props);

        int? percent = WatcherProps.ReadBattery(props, _batteryProp);

        if (connected == false)
        {
            if (_devices.TryGetValue(id, out var tracked) && tracked.IsOnline)
            {
                tracked.IsOnline = false;
                tracked.LastUpdated = DateTimeOffset.Now;
                Log.Info($"BLE 设备离线: {tracked.DisplayName}");
                DeviceUpdated?.Invoke(tracked);
            }
            return;
        }

        // 未跟踪过、且本次既无电量也未明确在线 → 邻居设备，忽略
        bool known = _devices.ContainsKey(id);
        if (!known && percent == null && connected != true)
            return;

        var status = _devices.GetOrAdd(id, _ => new DeviceStatus
        {
            Id = $"ble:{id}",
            Name = name ?? "",
            Source = DeviceSource.Ble,
            Protocol = "BLE GATT",
        });
        ApplyName(status, name, props);
        if (status.Kind == DeviceKind.Unknown)
            status.Kind = DeviceKindGuesser.Guess(status.Name);

        // 明确在线，或读到电量，或本来就在列表里且本次未说断开 → 保持/标为在线
        bool wasOffline = !status.IsOnline;
        status.IsOnline = true;
        status.LastUpdated = DateTimeOffset.Now;

        if (percent.HasValue)
        {
            bool batteryChanged = status.BatteryPercent != percent;
            status.BatteryPercent = percent;
            if (wasOffline || batteryChanged || !known)
                DeviceUpdated?.Invoke(status);
            return;
        }

        if (wasOffline || !known)
            DeviceUpdated?.Invoke(status);

        // 系统没给电量：主动 GATT 读一次（限流 2 分钟）
        if (!_lastGattAttempt.TryGetValue(id, out var last) ||
            DateTimeOffset.Now - last > TimeSpan.FromMinutes(2))
        {
            _lastGattAttempt[id] = DateTimeOffset.Now;
            _ = ReadBatteryViaGattAsync(id, status);
        }
    }

    private void HandleRemoved(string id)
    {
        if (_devices.TryRemove(id, out var tracked) && tracked.IsOnline)
        {
            tracked.IsOnline = false;
            tracked.LastUpdated = DateTimeOffset.Now;
            Log.Info($"BLE 设备移除: {tracked.DisplayName}");
            DeviceUpdated?.Invoke(tracked);
        }
    }

    private static void ApplyName(
        DeviceStatus status, string? infoName, IReadOnlyDictionary<string, object> props)
    {
        if (!string.IsNullOrEmpty(infoName))
        {
            status.Name = infoName!;
            return;
        }
        if (props.TryGetValue(WatcherProps.Name, out var nameObj))
        {
            var n = nameObj?.ToString();
            if (!string.IsNullOrEmpty(n)) status.Name = n!;
        }
    }

    private async Task ReadBatteryViaGattAsync(string aepId, DeviceStatus status)
    {
        await _gattGate.WaitAsync();
        try
        {
            var ble = await OpenDeviceAsync(aepId);
            if (ble == null) return;
            using (ble)
            {
                // FromIdAsync 成功且能读 GATT = 设备实际可达，纠正假离线
                if (ble.ConnectionStatus != BluetoothConnectionStatus.Connected)
                    return;

                var svcResult = await ble
                    .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(8));
                if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0) return;

                var chrResult = await svcResult.Services[0]
                    .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(8));
                if (chrResult.Status != GattCommunicationStatus.Success || chrResult.Characteristics.Count == 0) return;

                var read = await chrResult.Characteristics[0]
                    .ReadValueAsync(BluetoothCacheMode.Uncached)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(8));
                if (read.Status != GattCommunicationStatus.Success) return;

                var reader = DataReader.FromBuffer(read.Value);
                int percent = reader.ReadByte();
                if (percent is > 0 and <= 100)
                {
                    bool wasOffline = !status.IsOnline;
                    status.IsOnline = true;
                    status.BatteryPercent = percent;
                    status.LastUpdated = DateTimeOffset.Now;
                    if (status.Kind == DeviceKind.Unknown)
                        status.Kind = DeviceKindGuesser.Guess(status.Name);
                    DeviceUpdated?.Invoke(status);
                    Log.Info(wasOffline
                        ? $"BLE GATT 直读(恢复在线): {status.DisplayName} {percent}%"
                        : $"BLE GATT 直读: {status.DisplayName} {percent}%");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"BLE GATT 读取失败 {aepId}: {ex.Message}");
        }
        finally
        {
            _gattGate.Release();
        }
    }

    /// <summary>周期轮询已跟踪设备（含曾被误标离线的，便于自愈）。</summary>
    public async Task PollConnectedAsync()
    {
        foreach (var (id, status) in _devices.ToArray())
        {
            if (!_lastGattAttempt.TryGetValue(id, out var last) ||
                DateTimeOffset.Now - last > TimeSpan.FromMinutes(2))
            {
                _lastGattAttempt[id] = DateTimeOffset.Now;
                await ReadBatteryViaGattAsync(id, status);
            }
        }
    }

    private static async Task<BluetoothLEDevice?> OpenDeviceAsync(string aepId)
    {
        try
        {
            var task = BluetoothLEDevice.FromIdAsync(aepId).AsTask();
            if (await Task.WhenAny(task, Task.Delay(8000)) != task) return null;
            return task.Result;
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<DeviceStatus> Snapshot() => _devices.Values.ToList();

    public void Dispose()
    {
        _watcher?.Stop();
        _gattGate.Dispose();
    }
}
