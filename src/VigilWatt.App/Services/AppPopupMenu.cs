using VigilWatt.Core.Utils;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 贴片与小组件的右键菜单，走 **Win32 原生弹出菜单**（与托盘同一条路）：自带"点别处即关"。
/// 2026/10/12 13:38:41 xliu
/// 详见 docs/dev/runtime-pitfalls.md §18。
/// </summary>
internal static class AppPopupMenu
{
    private const uint IdOpen = 1;
    private const uint IdExit = 2;
    private const uint IdLock = 3;
    private const string ForegroundKey = "popup-menu-foreground";

    /// <summary>在屏幕坐标弹出"打开主窗口 / 退出"。**模态**：返回时用户已选中或已取消。
    /// 传 <paramref name="lockPosition"/> 时多一项可勾选的"锁定位置"（只有贴片有位置可锁）。</summary>
    public static void Show(IntPtr owner, int x, int y, bool? lockPosition = null, Action<bool>? onLockToggled = null)
    {
        var menu = Native.CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            if (lockPosition.HasValue)
            {
                uint flags = Native.MF_STRING | (lockPosition.Value ? Native.MF_CHECKED : 0);
                Native.AppendMenuW(menu, flags, (IntPtr)IdLock, "锁定位置");
                Native.AppendMenuW(menu, Native.MF_SEPARATOR, IntPtr.Zero, null);
            }
            Native.AppendMenuW(menu, Native.MF_STRING, (IntPtr)IdOpen, "打开主窗口");
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, IntPtr.Zero, null);
            Native.AppendMenuW(menu, Native.MF_STRING, (IntPtr)IdExit, "退出");

            // owner 必须是前台窗口，否则点菜单外不关闭。贴片的 WS_EX_NOACTIVATE 只挡"鼠标点击激活"，
            // 显式 SetForegroundWindow 仍有效；真失败时菜单会关不干净，记一条日志便于排查。
            if (Native.SetForegroundWindow(owner)) Log.ResetOnce(ForegroundKey);
            else Log.Once(LogLevel.Warn, ForegroundKey, "右键菜单：SetForegroundWindow 失败，点菜单外可能不关闭");

            var cmd = (uint)Native.TrackPopupMenuEx(menu,
                Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON, x, y, owner, IntPtr.Zero);

            // MSDN 托盘菜单样例的收尾：不补这一下，菜单偶发地关不干净
            Native.PostMessageW(owner, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd == IdOpen) TrayService.ShowMainWindow();
            else if (cmd == IdExit) TrayService.ExitApplication();
            else if (cmd == IdLock && lockPosition.HasValue) onLockToggled?.Invoke(!lockPosition.Value);
        }
        catch (Exception ex)
        {
            Log.Warn($"右键菜单弹出失败: {ex.Message}");
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }
}
