using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 解析跨连接通道的罗技物理身份并汇总独立通道状态。
/// 2026/10/24 14:07:39 Administrator
/// 协议出处与身份迁移边界见 docs/hardware/logitech.md §1。
/// </summary>
internal sealed class HidppDeviceIdentity
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (bool Direct, DeviceStatus Status)> _channels = new();

    public static string? ParseDeviceId(ReadOnlySpan<byte> response)
    {
        if (response.Length < 15) return null;
        var unit = response.Slice(3, 4);
        var model = response.Slice(9, 6);
        if (IsEmpty(unit) || IsEmpty(model)) return null;
        return "logitech:device:" + PluginUtils.StableHash(Convert.ToHexString(unit) + Convert.ToHexString(model));
    }

    private static bool IsEmpty(ReadOnlySpan<byte> value)
    {
        bool zero = true, ones = true;
        foreach (byte b in value) { zero &= b == 0; ones &= b == 0xFF; }
        return zero || ones;
    }

    public static string[] LegacyIds(string path, bool direct, byte slot)
    {
        var prefix = "logitech:" + PluginUtils.StableDeviceHash(path) + ":";
        return direct
            ? Enumerable.Range(1, 6).Append(0xFF).Select(i => prefix + i).ToArray()
            : new[] { prefix + slot };
    }

    public DeviceStatus Update(string channelId, bool direct, DeviceStatus status)
    {
        DeviceStatus snapshot;
        lock (status) snapshot = status.Clone();
        lock (_gate)
        {
            _channels[channelId] = (direct, snapshot);
            var peers = _channels.Values.Where(c => c.Status.Id == snapshot.Id).ToArray();
            var selected = peers.OrderByDescending(c => c.Status.IsOnline)
                .ThenByDescending(c => c.Status.BatteryPercent.HasValue)
                .ThenByDescending(c => c.Status.IsOnline && c.Direct)
                .ThenByDescending(c => c.Status.LastUpdated).First().Status;
            var result = new DeviceStatus
            {
                Id = selected.Id, Name = selected.Name, Source = selected.Source, Kind = selected.Kind,
                Protocol = selected.Protocol, BatteryPercent = selected.BatteryPercent, Charging = selected.Charging,
                LastUpdated = selected.LastUpdated, IsOnline = selected.IsOnline,
                PreviousIds = peers.SelectMany(p => p.Status.PreviousIds).Distinct(StringComparer.Ordinal).ToArray(),
            };
            return result;
        }
    }

    public void Clear()
    {
        lock (_gate) _channels.Clear();
    }

    public DeviceStatus Remove(string channelId, DeviceStatus status)
    {
        lock (_gate)
        {
            var result = Update(channelId, false, status);
            _channels.Remove(channelId);
            return result;
        }
    }
}
