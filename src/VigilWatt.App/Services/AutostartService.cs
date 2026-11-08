using Microsoft.Win32;
using VigilWatt.Core.Utils;
using Windows.ApplicationModel;

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
    private const string StartupTaskId = "VigilWattStartup";

    public static async Task<bool> ReadEnabledAsync()
    {
        if (!TileIntegration.HasPackage) return IsEnabled();
        try
        {
            return (await StartupTask.GetAsync(StartupTaskId)).State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception ex)
        {
            Log.Warn("读取 MSIX 自启失败: " + ex.Message);
            return false;
        }
    }

    public static async Task<string?> SetEnabledAsync(bool enabled)
    {
        if (!TileIntegration.HasPackage) return TrySetEnabled(enabled, out var error) ? null : error;
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                var state = task.State == StartupTaskState.Disabled ? await task.RequestEnableAsync() : task.State;
                return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy
                    ? null : "请在 Windows 的启动应用设置中启用 VigilWatt";
            }

            if (task.State == StartupTaskState.Enabled) task.Disable();
            return task.State == StartupTaskState.EnabledByPolicy ? "Windows 策略要求启用 VigilWatt 自启" : null;
        }
        catch (Exception ex)
        {
            Log.Warn("设置 MSIX 自启失败: " + ex.Message);
            return ex.Message;
        }
    }

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
                error = "无法打开 HKEY_CURRENT_USER 的 Run 键";
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
