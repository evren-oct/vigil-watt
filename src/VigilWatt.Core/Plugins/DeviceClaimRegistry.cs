using System.Collections.Concurrent;

namespace VigilWatt.Core.Plugins;

/// <summary>
/// 功 能： 认领者身份——<see cref="Id"/> 做互斥、<see cref="PluginName"/> 查设置页开关。
/// 2026/11/01 23:30:00 xliu
/// 两者必须分开：同一个品牌可以有两个插件类（迈从键鼠 + A5），共用一个开关但各有各的接口。
/// </summary>
public readonly record struct ClaimOwner(string Id, string PluginName)
{
    /// <summary>只有一个插件用这个名字时的默认身份。</summary>
    public static ClaimOwner Of(string pluginName) => new(pluginName, pluginName);
}

/// <summary>
/// 功 能： HID 接口认领注册表：同一物理接口同一时刻只归一个插件，防止两个插件同时开句柄/抢会话。
/// 2026/10/06 15:53:36 xliu
/// 由 DeviceManager 持有并传给各插件（轮询入口 RequestPollAsync / PollOnceAsync）。
/// </summary>
public sealed class DeviceClaimRegistry
{
    // 路径大小写不敏感：Windows 的 HID 路径会以 HID#A / hid#a 两种形式出现，
    // 按区分大小写存会让"同一个接口"被认领两次。
    private readonly ConcurrentDictionary<string, ClaimOwner> _ownerByPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>认领接口。返回 false = 已被其他认领者持有。</summary>
    public bool TryClaim(string devicePath, ClaimOwner owner)
    {
        var current = _ownerByPath.GetOrAdd(devicePath, owner);
        return current.Id.Equals(owner.Id, StringComparison.Ordinal);
    }

    /// <summary>只有插件名时的简化入口（身份 = 名字）。</summary>
    public bool TryClaim(string devicePath, string pluginName)
        => TryClaim(devicePath, ClaimOwner.Of(pluginName));

    public bool IsClaimedBy(string devicePath, ClaimOwner owner)
        => _ownerByPath.TryGetValue(devicePath, out var current) && current.Id == owner.Id;

    public bool IsClaimedBy(string devicePath, string claimId)
        => _ownerByPath.TryGetValue(devicePath, out var current) && current.Id == claimId;

    /// <summary>查某接口归哪个插件（插件名，用于查设置页开关）。null = 没有插件认领。</summary>
    public string? OwnerOf(string devicePath)
        => _ownerByPath.TryGetValue(devicePath, out var current) ? current.PluginName : null;

    /// <summary>释放接口（仅当持有者是该认领者时生效）。</summary>
    public void Release(string devicePath, ClaimOwner owner)
    {
        if (_ownerByPath.TryGetValue(devicePath, out var current) && current.Id == owner.Id)
            _ownerByPath.TryRemove(devicePath, out _);
    }

    public void Release(string devicePath, string claimId)
        => Release(devicePath, new ClaimOwner(claimId, claimId));

    /// <summary>插件卸载时释放其全部认领。</summary>
    public void ReleaseAll(string claimId)
    {
        foreach (var (path, owner) in _ownerByPath)
        {
            if (owner.Id == claimId) _ownerByPath.TryRemove(path, out _);
        }
    }
}
