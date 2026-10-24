using VigilWatt.Core.Settings;

namespace VigilWatt.Core.Devices;

/// <summary>
/// 功 能： 黑名单查询入口：插件与 watcher 在扫描阶段调用，命中则连句柄都不开、帧也不发。
/// 2026/10/20 20:25:00 20211
/// 名单只有 DeviceManager 会写，这里读 AppSettings 的进程内缓存。
/// </summary>
public static class DeviceBlocklist
{
    public static bool IsBlocked(string deviceId)
    {
        var settings = AppSettings.Load();
        return settings.BlockedDevices.ContainsKey(settings.CanonicalDeviceId(deviceId));
    }
}
