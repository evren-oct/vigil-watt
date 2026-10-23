using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Darkrim;

/// <summary>
/// 功 能： 判断官方 DARKRIM 客户端是否在跑，在跑就让出 dongle 队列。
/// 2026/10/23 19:05:00 zc165
/// 官方客户端常驻托盘（HKCU Run 带 --startup-minimized），句柄一直开着；
/// 两边撞同一个应答队列会让 dongle 重置链路、耳机断连。见 protocols.md §13.6。
/// </summary>
internal static class OfficialAppGuard
{
    /// <summary>官方客户端的进程名（不带 .exe，比较时忽略大小写）。</summary>
    private const string ProcessName = "DARKRIM";

    /// <summary>结果缓存时长：每轮轮询都查一次进程太浪费，也要够快感知官方退出。</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

    private static readonly object Gate = new();
    private static bool _cachedRunning;
    private static DateTime _cachedAt = DateTime.MinValue;

    /// <summary>可替换的进程探测（单测用）：默认查真实进程表。</summary>
    internal static Func<bool> Probe { get; set; } = ProbeRealProcesses;

    /// <summary>官方客户端是否正在运行。查询异常按"未运行"处理，免得一次异常把插件永久关掉。</summary>
    internal static bool IsRunning()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _cachedAt < CacheFor) return _cachedRunning;

            bool running;
            try
            {
                running = Probe();
            }
            catch (Exception ex)
            {
                Log.Info($"寸渊影弑 探测官方客户端进程失败，按未运行处理: {ex.Message}");
                running = false;
            }

            if (running != _cachedRunning)
            {
                Log.Info(running
                    ? "寸渊影弑 检测到官方 DARKRIM 客户端，本插件让出设备队列"
                    : "寸渊影弑 官方客户端已退出，本插件恢复接管");
            }

            _cachedRunning = running;
            _cachedAt = DateTime.UtcNow;
            return running;
        }
    }

    /// <summary>清掉缓存（单测用）。</summary>
    internal static void ResetCache()
    {
        lock (Gate)
        {
            _cachedAt = DateTime.MinValue;
            _cachedRunning = false;
        }
    }

    private static bool ProbeRealProcesses()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName(ProcessName);
        try
        {
            return procs.Length > 0;
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }
}
