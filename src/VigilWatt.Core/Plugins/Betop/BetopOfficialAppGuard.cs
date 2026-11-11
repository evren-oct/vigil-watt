using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Betop;

/// <summary>
/// 功 能： 观测北通官方软件（Electron，userData 为 %APPDATA%\BetopGame）是否在跑，只记状态变化。
/// 2026/10/25 19:40:12 xliu
/// 本轮只观测不拦认领：官方软件与 dongle 身份抖动是否相关尚无证据，见 docs/hardware/brands/betop.md §1.9。
/// </summary>
internal static class BetopOfficialAppGuard
{
    /// <summary>
    /// 进程名候选（忽略大小写、按包含匹配）：官方客户端是 Electron，userData 目录名 BetopGame；
    /// 确切 exe 名未经真机确认，故连厂商名一起认，命中时把真实进程名记进日志便于收窄。
    /// </summary>
    private static readonly string[] NameHints = { "betop", "beitong", "北通" };

    /// <summary>结果缓存时长：每轮轮询都查一次进程表太浪费，也要够快感知官方退出。</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

    private static readonly object Gate = new();
    private static bool _cachedRunning;
    private static DateTime _cachedAt = DateTime.MinValue;

    /// <summary>可替换的进程探测（单测用）：返回命中的进程名，null = 没在跑。</summary>
    internal static Func<string?> Probe { get; set; } = ProbeRealProcesses;

    /// <summary>观测入口：只按状态变化记一条日志，不参与认领判定。接线让位时改成读返回值。</summary>
    internal static void Observe() => _ = IsRunning();

    /// <summary>官方软件是否正在运行。查询异常按"未运行"处理，免得一次异常把观测永久停掉。</summary>
    internal static bool IsRunning()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _cachedAt < CacheFor) return _cachedRunning;

            string? hit = null;
            try
            {
                hit = Probe();
            }
            catch (Exception ex)
            {
                Log.Info($"北通 探测官方软件进程失败，按未运行处理: {ex.Message}");
            }

            bool running = hit != null;
            if (running != _cachedRunning)
            {
                Log.Info(running
                    ? $"北通 观测: 官方软件在跑（进程 {hit}），对照此后的设备变更广播看是否相关（betop.md §1.9）"
                    : "北通 观测: 官方软件已退出");
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

    private static string? ProbeRealProcesses()
    {
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                foreach (var hint in NameHints)
                {
                    if (p.ProcessName.Contains(hint, StringComparison.OrdinalIgnoreCase)) return p.ProcessName;
                }
            }
            catch (Exception)
            {
                // 进程在枚举与取名字之间退出：跳过它，不影响其它进程。
            }
            finally
            {
                p.Dispose();
            }
        }
        return null;
    }
}
