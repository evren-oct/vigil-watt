using System.Collections.Concurrent;

namespace VigilWatt.Core.Plugins;

/// <summary>
/// 功 能： HID 接口认领注册表：同一物理接口同一时刻只归一个插件，防止两个插件同时开句柄/抢会话。
/// 2026/10/06 15:53:36 xliu
/// 由 DeviceManager 持有并传给各插件（轮询入口 RequestPollAsync / PollOnceAsync）。
/// </summary>
public sealed class DeviceClaimRegistry
{
    private readonly ConcurrentDictionary<string, string> _ownerByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>认领接口。返回 false = 已被其他插件持有。</summary>
    public bool TryClaim(string devicePath, string pluginName)
    {
        var owner = _ownerByPath.GetOrAdd(devicePath, pluginName);
        return owner.Equals(pluginName, StringComparison.Ordinal);
    }

    public bool IsClaimedBy(string devicePath, string pluginName)
        => _ownerByPath.TryGetValue(devicePath, out var owner) && owner == pluginName;

    /// <summary>释放接口（仅当持有者是该插件时生效）。</summary>
    public void Release(string devicePath, string pluginName)
    {
        if (_ownerByPath.TryGetValue(devicePath, out var owner) && owner == pluginName)
            _ownerByPath.TryRemove(devicePath, out _);
    }

    /// <summary>插件卸载时释放其全部认领。</summary>
    public void ReleaseAll(string pluginName)
    {
        foreach (var (path, owner) in _ownerByPath)
        {
            if (owner == pluginName) _ownerByPath.TryRemove(path, out _);
        }
    }
}
