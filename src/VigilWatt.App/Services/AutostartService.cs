using Microsoft.Win32;
using VigilWatt.Core.Utils;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 开机自启：HKCU Run 注册表键，非打包应用用注册表最省事。
/// 2026/10/06 15:53:36 xliu
/// 一律不抛：受限令牌下 Run 键只读，异常逸出 UI 事件会被熔断器累计并最终杀死进程（2026-10-20 崩溃）。
/// </summary>
public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VigilWatt";

    /// <summary>读失败按"未启用"返回：设置页只是显示开关状态，不该因为读不到就崩。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            Log.Warn("读取开机自启失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>写失败只回传原因：调用点都在 UI 事件与卸载回调里，抛出去等于让进程退出。</summary>
    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null)
            {
                error = Strings.Current.Get("S_0d3b32cb90");
                return false;
            }

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            Log.Warn("写入开机自启失败: " + error);
            return false;
        }
    }
}
