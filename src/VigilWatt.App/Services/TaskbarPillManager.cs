using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using VigilWatt.Core.Utils;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 任务栏贴片的协调层：按配置选中目标屏幕，在其任务栏上创建/维持一个 <see cref="TaskbarPillWindow"/>。
/// 2026/10/20 15:22:00 xliu
/// 屏幕变化（插拔、显示模式、DPI、任务栏位置）通过「拆旧建新」处理；稳态下 Reconcile 幂等返回、不触发任何窗口重排。
/// </summary>
public sealed class TaskbarPillManager : IDisposable
{
    /// <summary>轮询节拍。切换显示模式（如"仅第二屏"→"仅电脑屏幕"）后最多 1 秒内跟到新任务栏。</summary>
    private const int ReconcileIntervalMs = 1000;

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _timer;
    private TaskbarPillWindow? _pill;

    // 缓存上一次成功 attach 的状态，用于快速路径去重
    private string? _cachedScreenId;
    private IntPtr _cachedTaskbar;
    private PixelRect _cachedRect;
    private uint _cachedDpi;
    private string? _lastNote;   // 降级原因只在变化时记一条，避免每秒刷日志
    private bool _disposed;

    public TaskbarPillManager()
    {
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(ReconcileIntervalMs);
        _timer.Tick += (_, _) => Reconcile();
        _timer.Start();
        Reconcile();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Detach();
    }

    /// <summary>设置保存后即时生效入口。</summary>
    public void RefreshFromSettings()
    {
        Reconcile();
        _pill?.RefreshFromSettings();
    }

    /// <summary>把贴片恢复到默认位置。</summary>
    public void ResetPosition()
    {
        _pill?.ResetPosition();
    }

    private void Reconcile()
    {
        if (_disposed) return;

        var settings = App.Services.Settings;
        if (!settings.TaskbarBatteryEnabled)
        {
            Detach();
            return;
        }

        // 1. 解析目标屏幕：配置的屏不在（拔了 / 换了显示模式）就贴主屏，不能让贴片整块消失
        var screens = MonitorCatalog.AllScreens();
        var target = MonitorSelection.Resolve(screens, settings.TaskbarBatteryScreen, out string note);
        NoteOnce(note);
        if (target == null)
        {
            Detach();
            return;
        }

        // 2. 在该屏幕上找任务栏句柄
        var taskbar = FindTaskbarOnScreen(target.Value);
        if (taskbar == IntPtr.Zero)
        {
            // 目标屏没有任务栏（"只在主显示器显示任务栏"）：宁可贴主屏也别消失
            var primary = MonitorSelection.Primary(screens);
            if (primary != null && primary.Value.Id != target.Value.Id)
            {
                taskbar = FindTaskbarOnScreen(primary.Value);
                if (taskbar != IntPtr.Zero)
                {
                    target = primary;
                    NoteOnce($"{target.Value.Label} 没有任务栏，改贴主屏");
                }
            }
        }
        if (taskbar == IntPtr.Zero)
        {
            Detach();
            return;
        }

        // 3. 读矩形与 DPI
        if (!Native.GetWindowRect(taskbar, out var rc))
        {
            Detach();
            return;
        }
        var rect = new PixelRect(rc.Left, rc.Top, rc.Width, rc.Height);
        uint dpi = Native.GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;

        // 4. 快速路径：状态未变则不动（稳态不触发 SetWindowPos）
        if (_pill != null && _pill.IsWindowValid &&
            _cachedScreenId == target.Value.Id &&
            _cachedTaskbar == taskbar &&
            _cachedRect == rect &&
            _cachedDpi == dpi)
        {
            return;
        }

        // 5. 重建
        Detach();
        _pill = new TaskbarPillWindow(taskbar, OnPillInvalidated);
        _cachedScreenId = target.Value.Id;
        _cachedTaskbar = taskbar;
        _cachedRect = rect;
        _cachedDpi = dpi;
        Log.Debug($"任务栏贴片：贴到 {target.Value.Label}（{target.Value.GdiName}）");
    }

    private void Detach()
    {
        _pill?.Dispose();
        _pill = null;
        _cachedScreenId = null;
        _cachedTaskbar = IntPtr.Zero;
        _cachedRect = default;
        _cachedDpi = 0;
    }

    /// <summary>贴片发现自身任务栏无效或 DPI 变化时回调，由协调层统一重建。</summary>
    private void OnPillInvalidated()
    {
        // 必须推迟到 Dispatcher 队列，避免在贴片的 WndProc/UpdateCore 里同步 Dispose 自己
        _dispatcher.TryEnqueue(() => Reconcile());
    }

    /// <summary>降级原因只在变化时记一条：Reconcile 每秒跑，重复写会把日志刷满。</summary>
    private void NoteOnce(string note)
    {
        if (note == _lastNote) return;
        _lastNote = note;
        if (note.Length > 0) Log.Info($"任务栏贴片：{note}");
    }

    private static IntPtr FindTaskbarOnScreen(MonitorScreen screen)
    {
        // 先查主任务栏（它在主屏或跟随主屏移动）
        var primary = Native.FindWindowW(Native.TaskbarClassName, null);
        if (primary != IntPtr.Zero && IsWindowOnScreen(primary, screen))
            return primary;

        // 再枚举所有副屏任务栏
        IntPtr prev = IntPtr.Zero;
        while (true)
        {
            var sec = Native.FindWindowExW(IntPtr.Zero, prev, Native.SecondaryTaskbarClassName, null);
            if (sec == IntPtr.Zero) break;
            if (IsWindowOnScreen(sec, screen))
                return sec;
            prev = sec;
        }

        return IntPtr.Zero;
    }

    private static bool IsWindowOnScreen(IntPtr hwnd, MonitorScreen screen)
    {
        var mon = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        if (mon == IntPtr.Zero) return false;
        var info = new Native.MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFOEX>() };
        if (!Native.GetMonitorInfoW(mon, ref info)) return false;
        return info.szDevice.Equals(screen.GdiName, StringComparison.OrdinalIgnoreCase);
    }
}
