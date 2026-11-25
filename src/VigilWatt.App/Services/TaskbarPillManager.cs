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
    /// <summary>轮询节拍。切换显示模式（如"仅第二屏"→"仅电脑屏幕"）后最多 2 秒内跟到新任务栏（去抖要 2 拍）。</summary>
    private const int ReconcileIntervalMs = 1000;

    /// <summary>重建去抖拍数：切显示配置时任务栏句柄与 DPI 会连抖几拍，稳定 2 拍再动手。</summary>
    private const int StableTicksRequired = 2;

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _timer;
    private TaskbarPillWindow? _pill;

    // 缓存上一次成功 attach 的状态，用于快速路径去重
    private string? _cachedScreenId;
    private IntPtr _cachedTaskbar;
    private uint _cachedDpi;

    // 去抖用：待定状态与它连续出现的拍数
    private string? _pendingScreenId;
    private IntPtr _pendingTaskbar;
    private uint _pendingDpi;
    private int _pendingTicks;
    private int _invalidateQueued;

    private string? _lastNote;   // 降级原因只在变化时记一条，避免每秒刷日志
    private bool _disposed;

    public TaskbarPillManager()
    {
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(ReconcileIntervalMs);
        _timer.Tick += (_, _) => Reconcile();
        _timer.Start();
        Reconcile(immediate: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Detach();
    }

    /// <summary>设置保存后即时生效入口。</summary>
    public void RefreshFromSettings() => Reconcile(immediate: true);

    /// <summary>把贴片恢复到默认位置。</summary>
    public void ResetPosition()
    {
        _pill?.ResetPosition();
    }

    /// <summary>锁定/解锁贴片位置（右键菜单与设置页共用，只有这里落盘）。</summary>
    public void SetLockPosition(bool locked)
    {
        var s = App.Services.Settings;
        if (s.TaskbarBatteryLockPosition == locked) return;
        s.TaskbarBatteryLockPosition = locked;
        s.Save();
        _pill?.SetLockPosition(locked);
        Log.Debug($"任务栏贴片：位置{(locked ? "已锁定" : "已解锁")}");
    }

    /// <summary>稳态幂等；<paramref name="immediate"/> 跳过去抖（启动与设置保存要当场生效）。</summary>
    private void Reconcile(bool immediate = false)
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

        // 3. 读 DPI（矩形读不出说明任务栏正在被拆，这一拍别动）
        if (!Native.GetWindowRect(taskbar, out _))
        {
            Detach();
            return;
        }
        uint dpi = Native.GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;

        // 4. 稳态：屏幕、任务栏、DPI 都没变就不动。**任务栏矩形变化不重建**——窗口自己每拍重算尺寸和位置
        if (_pill != null && _pill.IsWindowValid &&
            _cachedScreenId == target.Value.Id &&
            _cachedTaskbar == taskbar &&
            _cachedDpi == dpi)
        {
            _pendingTicks = 0;
            return;
        }

        // 5. 去抖：过渡帧里任务栏句柄与 DPI 会连续抖（实测 250ms 内抖两次），每抖一次就拆建一次窗口；
        //    漏掉销毁的那个没人驱动、会冻在屏幕上（"贴片多一个并重叠"）。稳定 2 拍再重建。
        bool urgent = immediate || _pill == null || !_pill.IsWindowValid;
        if (!urgent)
        {
            if (_pendingScreenId == target.Value.Id && _pendingTaskbar == taskbar && _pendingDpi == dpi)
            {
                _pendingTicks++;
            }
            else
            {
                _pendingScreenId = target.Value.Id;
                _pendingTaskbar = taskbar;
                _pendingDpi = dpi;
                _pendingTicks = 1;
            }
            if (_pendingTicks < StableTicksRequired) return;
        }

        // 6. 重建
        Detach();
        _pill = new TaskbarPillWindow(taskbar, OnPillInvalidated);
        _cachedScreenId = target.Value.Id;
        _cachedTaskbar = taskbar;
        _cachedDpi = dpi;
        Log.Debug($"任务栏贴片：贴到 {target.Value.Label}（{target.Value.GdiName}），任务栏 0x{taskbar.ToInt64():X}，dpi={dpi}");
    }

    private void Detach()
    {
        _pill?.Dispose();
        _pill = null;
        _cachedScreenId = null;
        _cachedTaskbar = IntPtr.Zero;
        _cachedDpi = 0;
        _pendingScreenId = null;
        _pendingTaskbar = IntPtr.Zero;
        _pendingDpi = 0;
        _pendingTicks = 0;
    }

    /// <summary>贴片发现自身任务栏无效或 DPI 变化时回调，由协调层统一重建。</summary>
    private void OnPillInvalidated()
    {
        // 一拍内可能被多个来源要求重建：合并成一次，避免同拍连拆连建
        if (Interlocked.Exchange(ref _invalidateQueued, 1) == 1) return;

        // 必须推迟到 Dispatcher 队列，避免在贴片的 WndProc/UpdateCore 里同步 Dispose 自己
        if (!_dispatcher.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _invalidateQueued, 0);
                Reconcile();
            }))
        {
            Interlocked.Exchange(ref _invalidateQueued, 0);   // 排队失败要复位，否则之后再也不会重建
        }
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
