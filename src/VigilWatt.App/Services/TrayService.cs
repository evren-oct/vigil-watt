using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 托盘图标，纯 Win32 实现（Shell_NotifyIcon + 原生弹出菜单）：
/// 2026/10/06 15:53:36 xliu
/// - 图标实时渲染最低电量设备的百分比数字 - 左键单击/双击打开主窗口，右键弹菜单（设备列表 + 打开主窗口 + 退出） - 低电量用 NIF_INFO 气泡提醒
/// </summary>
public sealed class TrayService : IDisposable
{
    private const uint CallbackMessage = 0x8C40; // WM_APP 区间内任意未占用值
    private const uint IdOpen = 1;
    private const uint IdExit = 2;
    private const uint IdDeviceBase = 10;

    private static TrayService? _instance;

    private IntPtr _hwnd;
    private bool _iconAdded;
    private IntPtr _currentHIcon;
    private uint _taskbarCreatedMessage;
    /// <summary>刷新请求是否已排队（1 = 已排）。设备事件每轮会连打好几次，靠它合并成一次。</summary>
    private int _updateQueued;

    private readonly AppServices _services;

    public TrayService(AppServices services)
    {
        _services = services;
        _instance = this;
    }

    public void Initialize()
    {
        CreateMessageWindow();
        UpdateIcon();
        Log.Info(_iconAdded ? "托盘图标已创建" : "托盘图标未显示");
    }

    /// <summary>
    /// 请求刷新托盘图标与提示文字。
    ///
    /// 设备事件来自后台线程且一轮里可能连打十几次，所以这里：① **合并**——同一时刻只排一次队，
    /// 多余请求直接丢掉（下一次渲染本来就包含最新状态）；② **回 UI 线程**——避免和 Dispose 抢 _currentHIcon。
    ///
    /// 必须由设备事件驱动：UpdateIcon 以前只在 Initialize 和 explorer 重启时各调一次，
    /// 而启动那一刻设备还没扫出来，于是托盘提示永远停在"暂无在线设备"。
    /// </summary>
    public void RequestUpdate()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 1) return;   // 已排队，合并掉

        var queue = _services.MainWindow?.DispatcherQueue;
        if (queue == null)
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            UpdateIcon();                       // 窗口还没建好（极早期），就地渲染
            return;
        }

        if (!queue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _updateQueued, 0);
                UpdateIcon();
            }))
        {
            Interlocked.Exchange(ref _updateQueued, 0);   // 队列已关闭（退出中），忽略本次
        }
    }

    // ---------- 消息窗口 ----------

    private static readonly Native.WndProcDele WndProcDele = WndProc;

    private void CreateMessageWindow()
    {
        var className = "VigilWatt_TrayWnd_" + Guid.NewGuid().ToString("N");
        var hInstance = Native.GetModuleHandleW(null);

        var wc = new Native.WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDele),
            hInstance = hInstance,
            lpszClassName = className,
        };
        Native.RegisterClassW(ref wc);

        // 普通的隐藏顶层窗口（从不 Show），比 message-only 窗口兼容性更稳
        _hwnd = Native.CreateWindowExW(0, className, "VigilWattTray", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            Log.Error($"托盘消息窗口创建失败: {Marshal.GetLastWin32Error()}");
            return;
        }
        Log.Info($"托盘消息窗口 hwnd=0x{_hwnd:X} class={className}");

        _taskbarCreatedMessage = Native.RegisterWindowMessageW("TaskbarCreated");
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _instance;
        if (self != null)
        {
            if (msg == CallbackMessage)
            {
                self.HandleTrayMessage((uint)lParam);
                return IntPtr.Zero;
            }
            // explorer 重启后任务栏重建，重新挂图标
            if (self._taskbarCreatedMessage != 0 && msg == self._taskbarCreatedMessage)
            {
                self._iconAdded = false;
                self.UpdateIcon();
                return IntPtr.Zero;
            }
        }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void HandleTrayMessage(uint mouseMessage)
    {
        Log.Debug($"收到托盘回调: 0x{mouseMessage:X4}");
        if (mouseMessage == Native.WM_LBUTTONUP || mouseMessage == Native.WM_LBUTTONDBLCLK
            || mouseMessage == Native.NIN_BALLOONUSERCLICK)
        {
            ShowMainWindow();
        }
        else if (mouseMessage == Native.WM_RBUTTONUP)
        {
            ShowContextMenu();
        }
    }

    // ---------- 图标渲染 ----------

    private (IntPtr HIcon, string Tip) Render()
    {
        DeviceStatus? lowest = null;
        foreach (var d in _services.Manager.Devices)
        {
            if (!d.IsOnline || d.BatteryPercent is not { } p) continue;
            // 本机电量不参与托盘的"最低电量"取值（用户 2026-10-06 明确）：笔记本正常用到 30% 是常态，
            // 让它把托盘染红会盖掉"外设快没电了"这个真正的信号。它仍进设备列表/小组件/历史统计。
            if (d.Source == DeviceSource.System) continue;
            if (lowest == null || p < lowest.BatteryPercent) lowest = DevicePresentation.Apply(d, _services.Settings);
        }

        int? percent = lowest?.BatteryPercent;
        bool charging = lowest != null && DeviceStatusFormatter.IsCharging(lowest.Charging);

        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(System.Drawing.Color.Transparent);

            var color = UiPalette.ToDrawing(
                UiPalette.BatteryLevel(percent, charging));

            using var brush = new System.Drawing.SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);

            string text = charging ? "⚡" : percent.HasValue
                ? percent.Value.ToString()
                : "–";
            using var font = new System.Drawing.Font("Segoe UI", text.Length >= 3 ? 11f : 14f,
                System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            var size = g.MeasureString(text, font);
            using var white = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            g.DrawString(text, font, white, 16 - size.Width / 2, 16 - size.Height / 2 + 1);
        }

        var tip = lowest == null
            ? "VigilWatt · 暂无在线设备"
            : $"VigilWatt · {lowest.DisplayName} {lowest.BatteryPercent}%";
        return (bmp.GetHicon(), tip);
    }

    public void UpdateIcon()
    {
        if (!_services.Settings.TrayIconEnabled)
        {
            RemoveIcon();
            return;
        }
        try
        {
            var (hIcon, tip) = Render();
            var oldIcon = _currentHIcon;

            var data = new Native.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1,
            };
            if (!_iconAdded)
            {
                data.uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP;
                data.uCallbackMessage = CallbackMessage;
                data.hIcon = hIcon;
                data.szTip = tip;
                _iconAdded = Native.Shell_NotifyIconW(Native.NIM_ADD, ref data);
            }
            else
            {
                data.uFlags = Native.NIF_ICON | Native.NIF_TIP;
                data.hIcon = hIcon;
                data.szTip = tip;
                Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
            }

            if (_iconAdded)
            {
                _currentHIcon = hIcon;
                if (oldIcon != IntPtr.Zero) Native.DestroyIcon(oldIcon);
                // 只在提示真正变化时打日志（轮询期间会频繁刷新），而"提示变了没有"正是排查托盘问题
                // 最想知道的信息。去重状态由 Log.InfoOnChange 按 key 维护。
                Log.InfoOnChange("tray-tip", tip, $"托盘提示已更新: {tip}");
            }
            else
            {
                Native.DestroyIcon(hIcon);
                Log.Warn("Shell_NotifyIcon 失败");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"托盘图标更新失败: {ex.Message}");
        }
    }

    // ---------- 右键菜单 ----------

    private void ShowContextMenu()
    {
        Log.Info("托盘右键菜单打开");
        var menu = Native.CreatePopupMenu();
        try
        {
            uint deviceIndex = 0;
            Native.AppendMenuW(menu, Native.MF_STRING, (IntPtr)IdOpen, "打开主窗口");
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, IntPtr.Zero, null);

            foreach (var raw in _services.Manager.Devices)
            {
                var d = DevicePresentation.Apply(raw, _services.Settings);
                deviceIndex++;
                var text = d.IsOnline
                    ? $"{d.DisplayName}　{DeviceStatusFormatter.PercentText(d.BatteryPercent, "?")}" +
                      (DeviceStatusFormatter.IsCharging(d.Charging) ? " ⚡" : "")
                    : $"{d.DisplayName}　离线";
                Native.AppendMenuW(menu, Native.MF_STRING | Native.MF_GRAYED,
                    (IntPtr)(IdDeviceBase + deviceIndex), text);
            }

            Native.AppendMenuW(menu, Native.MF_SEPARATOR, IntPtr.Zero, null);
            Native.AppendMenuW(menu, Native.MF_STRING, (IntPtr)IdExit, "退出");

            // TrackPopupMenu 前必须 SetForegroundWindow，否则点击菜单外区域不会关闭
            Native.SetForegroundWindow(_hwnd);
            var pos = Native.GetCursorPos();
            var cmd = (uint)Native.TrackPopupMenuEx(menu,
                Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON, pos.X, pos.Y, _hwnd, IntPtr.Zero);

            if (cmd == IdOpen) ShowMainWindow();
            else if (cmd == IdExit) Exit();
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    // ---------- 窗口与退出 ----------

    public static void ShowMainWindow()
    {
        Log.Info("打开主窗口被调用");
        var window = App.Services.MainWindow;
        if (window == null) return;
        window.AppWindow.Show();
        window.Activate();
        Native.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
    }

    private static void Exit()
    {
        Log.Info("用户退出应用");
        _instance?.Dispose();
        // 释放 HID 句柄 / Timer / SQLite 连接——不释放会把 dongle 会话占死，之后必须重插拔（safety.md §2 事故二）。
        // 以前 AppServices.Dispose 全仓无人调用（CR-07），这里是它唯一的真实退出路径。
        try { App.Services.Dispose(); } catch (Exception ex) { Log.Warn($"退出时资源释放失败: {ex.Message}"); }
        // 退出收尾：释放单实例互斥量 + 刷盘并释放 app.log 句柄。与 ProcessExit 兜底互为保险。
        App.ShutdownLogging();
        Environment.Exit(0);
    }

    /// <summary>
    /// 供其它入口（小组件右键菜单）调用的退出通道：与托盘右键"退出"同一条路，保证都会先摘掉
    /// 托盘图标再结束进程，避免任务栏残留一个点不动的图标。
    /// </summary>
    public static void ExitApplication() => Exit();

    // ---------- 低电量气泡 ----------

    public void ShowLowBattery(DeviceStatus device)
    {
        if (!_services.Settings.TrayIconEnabled || !_iconAdded) return;
        try
        {
            var data = new Native.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = Native.NIF_INFO,
                szInfo = $"{device.DisplayName} 电量仅剩 {device.BatteryPercent}%，请及时充电",
                szInfoTitle = "电量不足",
                dwInfoFlags = Native.NIIF_WARNING,
            };
            Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
        }
        catch (Exception ex)
        {
            Log.Warn($"低电量气泡失败: {ex.Message}");
        }
    }

    public void ShowUpdateAvailable(string version)
    {
        ShowBalloon("发现 VigilWatt 更新",
            $"版本 {version} 已就绪。打开 VigilWatt 后可选择下载并安装。");
    }

    public void ShowUpdateDownloading(string version) =>
        ShowBalloon("正在下载更新", $"正在下载 VigilWatt {version}…");

    public void ShowUpdateDownloadDone(string version) =>
        ShowBalloon("下载完成", $"VigilWatt {version} 下载完成，即将重启安装");

    public void ShowUpdateDownloadFailed(string detail)
    {
        var msg = string.IsNullOrWhiteSpace(detail) ? "请稍后重试或到设置页查看原因。" : TrimBalloon(detail);
        ShowBalloon("更新下载失败", msg);
    }

    private void ShowBalloon(string title, string body)
    {
        if (!_services.Settings.TrayIconEnabled || !_iconAdded) return;
        try
        {
            var data = new Native.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = Native.NIF_INFO,
                szInfo = TrimBalloon(body),
                szInfoTitle = TrimBalloon(title, 63),
                dwInfoFlags = Native.NIIF_INFO,
            };
            Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
        }
        catch (Exception ex)
        {
            Log.Warn($"更新提示气泡失败: {ex.Message}");
        }
    }

    /// <summary>NOTIFYICONDATA 标题/正文有固定缓冲，超出会截断或失败。</summary>
    private static string TrimBalloon(string text, int max = 255)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    public void Dispose()
        => RemoveIcon();

    private void RemoveIcon()
    {
        if (_iconAdded && _hwnd != IntPtr.Zero)
        {
            var data = new Native.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1,
            };
            Native.Shell_NotifyIconW(Native.NIM_DELETE, ref data);
            _iconAdded = false;
        }
        if (_currentHIcon != IntPtr.Zero)
        {
            Native.DestroyIcon(_currentHIcon);
            _currentHIcon = IntPtr.Zero;
        }
    }
}
