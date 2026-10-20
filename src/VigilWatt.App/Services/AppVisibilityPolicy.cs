using VigilWatt.Core.Settings;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 按后台入口设置决定主窗口能否隐藏。
/// 2026/10/19 21:10:32 kaniu
/// </summary>
public static class AppVisibilityPolicy
{
    public static bool CanHideMainWindow(AppSettings settings)
        => settings.TrayIconEnabled || settings.TaskbarBatteryEnabled || settings.WidgetEnabled;
}
