using VigilWatt.Core.Utils;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 启动失败的兜底出口。
/// 2026/10/20 21:10 xliu
/// 以前 OnLaunched 里任何异常都会让进程静默死亡：XAML 把它包成 stowed exception，
/// 事件日志只留一条 Microsoft.UI.Xaml.dll / 0xc000027b，应用日志一行都写不出来
/// （Log 本身可能因为数据目录不可写而失败，也还没走到第一行日志）。用户看到的只有"点了没反应"。
/// 这里统一落日志 + 弹原生窗，把真实异常摆到台面上。
/// </summary>
internal static class StartupGuard
{
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_ICONWARNING = 0x00000030;
    private const uint MB_TOPMOST = 0x00040000;

    /// <summary>
    /// 记录并展示启动失败。<paramref name="stage"/> 用中文描述失败阶段（如"初始化服务与窗口"），
    /// 让用户一眼知道卡在哪一步。**本方法自身绝不抛异常**：兜底路径再抛就等于又静默了。
    /// </summary>
    public static void Fatal(string stage, Exception ex)
    {
        // 日志可能压根没初始化成功（数据目录不可写就是这种），再包一层
        try { Log.Error($"启动失败（{stage}）", ex); } catch { /* 兜底不能自己炸 */ }

        var text =
            Strings.Current.Format("S_e558ef0417", stage) +
            $"{ex.GetType().Name}: {ex.Message}\r\n\r\n" +
            Strings.Current.Format("S_5a07ebcbf3", AppPaths.RootDir) +
            Strings.Current.Get("S_4241f9400c") +
            $"{ex.StackTrace}";

        try
        {
            Native.MessageBoxW(IntPtr.Zero, text, Strings.Current.Get("S_dfb0259f8b"), MB_OK | MB_ICONERROR | MB_TOPMOST);
        }
        catch { /* 连弹窗都失败就只剩日志了 */ }
    }

    /// <summary>
    /// 非致命提示：应用还能跑，但某块能力不可用（典型是数据目录写不进去 → 日志/设置/历史全存不下）。
    /// 这类问题以前完全无声，用户只能看到"没日志、点保存没反应"。
    /// </summary>
    public static void Warn(string title, string message)
    {
        try
        {
            Native.MessageBoxW(IntPtr.Zero, message, title, MB_OK | MB_ICONWARNING | MB_TOPMOST);
        }
        catch { /* 忽略 */ }
    }
}
