using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;
using Gdi = System.Drawing;
using GdiImaging = System.Drawing.Imaging;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 任务栏电量贴片：**真嵌进任务栏**——一个原生分层窗口，<c>SetParent</c> 挂成任务栏 （<c>Shell_TrayWnd</c>）的子窗口，内容用 <c>UpdateLayeredWindow</c> 提交 32 位 ARGB 位图。
/// 2026/10/12 17:20:14 xliu
/// 三道锁（SetParent / WS_EX_LAYERED / UpdateLayeredWindow）见 docs/dev/runtime-pitfalls.md §16。
/// </summary>
public sealed class TaskbarPillWindow : IDisposable
{
    private const string ClassName = "VigilWattTaskbarPill";
    private const string TipClassName = "VigilWattTaskbarPillTip";
    private const string WindowTitle = "VigilWatt 任务栏电量";
    private const int FollowIntervalMs = 1000;

    /// <summary>拖动轮询节拍；手停住时退到 <see cref="DragIdleTickMs"/>，见 docs/dev/runtime-pitfalls.md §16.5。</summary>
    private const int DragTickMs = 16;
    private const int DragIdleTickMs = 100;
    private const int DragIdleTicks = 3;

    private const double MinHeightDip = 22, MaxHeightDip = 40;
    private const double TaskbarMarginDip = 6;
    private const double EdgeGapDip = 6;
    private const double TipGapDip = 4;

    /// <summary>交给非托管代码的回调必须静态压住，否则被 GC 回收后窗口再来消息就是访问已释放内存。</summary>
    private static readonly Native.WndProcDele StaticProc = WndProc;
    private static readonly Native.WndProcDele TipProc = TipWndProc;

    private static bool _classRegistered;
    private static bool _tipClassRegistered;

    /// <summary>同一时刻只允许一个贴片；窗口过程是静态的，靠它转回实例。</summary>
    private static TaskbarPillWindow? _current;

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _dragTimer;

    private IntPtr _hwnd, _taskbar, _tipHwnd;
    private IntPtr _memDc, _dib, _oldDib, _dibBits;
    private IntPtr _tipMemDc, _tipDib, _tipOldDib, _tipDibBits;
    private int _dibW, _dibH;
    private int _tipDibW, _tipDibH;
    private Gdi.Bitmap? _bitmap;
    private int _pillW, _pillH;

    /// <summary>上次渲染用的缩放。缩放变了即使高度没变也要重画（字宽、圆角都按缩放算）。</summary>
    private float _renderScale;
    private int _offsetPx;
    private int _offsetDpi;
    private uint _windowDpi;
    private bool _dragging;
    private bool _positionLocked;
    private int _dragStartCursor, _dragStartOffset;
    private int _dragIdleTicks, _pollX, _pollY;
    private bool _btnWasDown;   // 上轮询左键状态，用于检测按下边缘
    private bool _taskbarHorizontal = true;
    private PixelRect _taskbarRect, _trayRect;
    private int _refreshQueued;
    private int _rebuildQueued;
    private bool _needRender = true;
    private bool _disposed;
    private IReadOnlyList<TaskbarPillRenderer.HitChip> _hits = Array.Empty<TaskbarPillRenderer.HitChip>();
    private string? _hoverId;
    private string? _shownTipId;
    private bool _tipVisible;
    private bool _hoverArmed;
    private readonly Action? _onInvalidate;

    /// <summary>窗口仍有效（未销毁）。供协调层快速路径判断。</summary>
    public bool IsWindowValid => _hwnd != IntPtr.Zero && Native.IsWindow(_hwnd);

    public TaskbarPillWindow(IntPtr taskbarHwnd, Action? onInvalidate = null)
    {
        _current = this;
        _taskbar = taskbarHwnd;
        _onInvalidate = onInvalidate;
        LoadOffsetFromSettings();

        // 拖动轮询表常驻运行（20Hz）：Win11 任务栏 XAML 输入管理器会吃掉子窗口的鼠标消息，
        // 不能靠 WM_LBUTTONDOWN 启动拖动，只能轮询检测（runtime-pitfalls.md §16.5.1）。
        _dragTimer = _dispatcher.CreateTimer();
        _dragTimer.Interval = TimeSpan.FromMilliseconds(DragIdleTickMs);
        _dragTimer.Tick += (_, _) => OnDragTick();
        _dragTimer.Start();

        EnsureWindow();

        // 可反复开/关（设置页每次保存都可能 Dispose 后置 null），订阅必须用命名方法并在 Dispose 里解绑
        App.Services.Manager.DeviceUpdated += OnDeviceUpdated;
        App.Services.Manager.DevicesChanged += OnDevicesChanged;

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(FollowIntervalMs);
        _timer.Tick += (_, _) => Update();
        _timer.Start();

        Update();
    }

    private void LoadOffsetFromSettings()
    {
        var s = App.Services.Settings;
        _offsetPx = s.TaskbarBatteryOffsetX;
        _offsetDpi = s.TaskbarBatteryOffsetDpi;
        _positionLocked = s.TaskbarBatteryPositionLocked;
    }

    private void OnDeviceUpdated(DeviceStatus _) => RequestRefresh();

    private void OnDevicesChanged() => RequestRefresh();

    /// <summary>合并刷新请求：一轮轮询会对 N 台设备各发一次事件，逐个排队就是 N 次全量重建。</summary>
    private void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            _needRender = true;
            Update();
        });
    }

    /// <summary>设置保存后的即时生效入口（开关/显示内容/最多台数可能都变了）。</summary>
    public void RefreshFromSettings()
    {
        EndDrag();
        LoadOffsetFromSettings();
        _btnWasDown = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
        _needRender = true;
        Update();
    }

    /// <summary>重置到默认位置：offset=0，按当前 DPI 绑定。</summary>
    public void ResetPosition()
    {
        uint dpi = TaskbarDpi();
        if (dpi == 0) dpi = 96;
        _offsetPx = 0;
        _offsetDpi = (int)dpi;
        var s = App.Services.Settings;
        s.TaskbarBatteryOffsetX = 0;
        s.TaskbarBatteryOffsetDpi = _offsetDpi;
        s.Save();
        EndDrag();
        _needRender = true;
        Update();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _dragTimer.Stop();
        EndDrag();
        App.Services.Manager.DeviceUpdated -= OnDeviceUpdated;
        App.Services.Manager.DevicesChanged -= OnDevicesChanged;
        HideTip();
        ReleaseWindow();
        _taskbar = IntPtr.Zero;

        if (ReferenceEquals(_current, this)) _current = null;
    }

    // ---------- 窗口 ----------

    private static void RegisterClassOnce()
    {
        if (_classRegistered) return;
        _classRegistered = true;

        var wc = new Native.WNDCLASSW
        {
            style = Native.CS_DBLCLKS,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(StaticProc),
            hInstance = Native.GetModuleHandleW(null),
            hCursor = Native.LoadCursorW(IntPtr.Zero, (IntPtr)Native.IDC_ARROW),
            hbrBackground = IntPtr.Zero,
            lpszClassName = ClassName,
        };
        Native.RegisterClassW(ref wc);
    }

    /// <summary>建窗口并挂进任务栏。窗口被父窗口带走（Explorer 重启）后由调用方重新调它。</summary>
    private void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero && Native.IsWindow(_hwnd)) return;
        ReleaseWindow();

        RegisterClassOnce();
        var hInstance = Native.GetModuleHandleW(null);
        _hwnd = Native.CreateWindowExW(
            (uint)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE),
            ClassName, WindowTitle, Native.WS_POPUP,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            Log.Warn($"任务栏贴片：窗口创建失败，err={Marshal.GetLastWin32Error()}");
            return;
        }

        AttachToTaskbar();
        EnsureTipWindow();
        _windowDpi = TaskbarDpi();
        Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
    }

    private static void RegisterTipClassOnce()
    {
        if (_tipClassRegistered) return;
        _tipClassRegistered = true;
        var wc = new Native.WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(TipProc),
            hInstance = Native.GetModuleHandleW(null),
            hCursor = Native.LoadCursorW(IntPtr.Zero, (IntPtr)Native.IDC_ARROW),
            hbrBackground = IntPtr.Zero,
            lpszClassName = TipClassName,
        };
        Native.RegisterClassW(ref wc);
    }

    /// <summary>自绘置顶分层提示：不挂任务栏、不用 TOOLTIPS_CLASS（§16.10）。</summary>
    private void EnsureTipWindow()
    {
        if (_tipHwnd != IntPtr.Zero && Native.IsWindow(_tipHwnd)) return;
        ReleaseTipWindow();
        RegisterTipClassOnce();

        var hInstance = Native.GetModuleHandleW(null);
        _tipHwnd = Native.CreateWindowExW(
            (uint)(Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE),
            TipClassName, "", Native.WS_POPUP,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_tipHwnd == IntPtr.Zero)
            Log.Warn($"任务栏贴片：提示窗创建失败，err={Marshal.GetLastWin32Error()}");
    }

    private void ReleaseTipWindow()
    {
        HideTip();
        ReleaseTipDib();
        if (_tipHwnd != IntPtr.Zero)
        {
            if (Native.IsWindow(_tipHwnd)) Native.DestroyWindow(_tipHwnd);
            _tipHwnd = IntPtr.Zero;
        }
    }

    private static IntPtr TipWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        Native.DefWindowProcW(hwnd, msg, wParam, lParam);

    /// <summary>
    /// 挂成任务栏的子窗口。**SetParent 不会自动改 WS_CHILD/WS_POPUP**（MSDN 明确要求自己同步），
    /// 不同步的话坐标仍按桌面算、裁剪也不对。
    /// </summary>
    private void AttachToTaskbar()
    {
        if (_taskbar == IntPtr.Zero || _hwnd == IntPtr.Zero) return;

        Native.SetParent(_hwnd, _taskbar);
        var style = Native.GetWindowLongPtr(_hwnd, Native.GWL_STYLE).ToInt64();
        style = (style & ~(long)Native.WS_POPUP) | (long)Native.WS_CHILD;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_STYLE, new IntPtr(style));
    }

    /// <summary>DPI/显示配置变了要 Destroy+重建；不能在 WndProc 里 DestroyWindow（§16.8）。</summary>
    private void RequestRebuild()
    {
        if (Interlocked.Exchange(ref _rebuildQueued, 1) == 1) return;
        _dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _rebuildQueued, 0);
            if (_disposed) return;
            if (_onInvalidate != null)
            {
                _onInvalidate();
                return;
            }
            EndDrag();
            ReleaseWindow();
            _taskbarRect = default;
            _trayRect = default;
            EnsureWindow();
            _needRender = true;
            Update();
        });
    }

    private void ReleaseWindow()
    {
        EndDrag();
        ReleaseTipWindow();
        if (_hwnd != IntPtr.Zero)
        {
            if (Native.IsWindow(_hwnd)) Native.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        _windowDpi = 0;
        ReleaseDib();
        _bitmap?.Dispose();
        _bitmap = null;
        _pillW = _pillH = 0;
        _hits = Array.Empty<TaskbarPillRenderer.HitChip>();
        _hoverId = null;
        _hoverArmed = false;
        _needRender = true;
    }

    // ---------- 更新 ----------

    private void Update()
    {
        if (_disposed) return;
        // 本方法经 DispatcherQueue 回调调用，回调里未捕获的异常会绕过 Application.UnhandledException
        // 直接 fail-fast，必须整体兜底
        try
        {
            UpdateCore();
        }
        catch (Exception ex)
        {
            Log.Warn($"任务栏贴片更新失败: {ex.Message}");
        }
    }

    private void UpdateCore()
    {
        EnsureWindow();
        if (_hwnd == IntPtr.Zero) return;

        // 协调层传入的任务栏已失效：通知上层重建，自己不再尝试找新任务栏
        if (_taskbar == IntPtr.Zero || !Native.IsWindow(_taskbar))
        {
            _onInvalidate?.Invoke();
            return;
        }

        // Explorer 重启或父窗口被拆：重新挂
        if (Native.GetParent(_hwnd) != _taskbar)
            AttachToTaskbar();

        // 任务栏 DPI 变了：分层子窗口坐标空间跟创建时绑死，只重算矩形不够
        uint dpi = TaskbarDpi();
        if (dpi == 0) return;   // 句柄正在重建：这一拍别按 100% 算尺寸，等下一拍
        if (_windowDpi == 0) _windowDpi = dpi;   // 建窗时没读到 DPI：以当前为基准，否则之后的变化永远检测不到
        if (dpi != _windowDpi)
        {
            _onInvalidate?.Invoke();
            return;
        }

        if (!TryGetTaskbar(out var rect, out var tray, out bool hidden)) return;
        // 任务栏自动隐藏时只剩约 2px 厚的边：子窗口会自己跟着父窗口走，别按 2px 重算尺寸位置
        if (hidden && _pillH > 0) return;

        if (rect != _taskbarRect || tray != _trayRect)
        {
            _taskbarRect = rect;
            _trayRect = tray;
            _taskbarHorizontal = TaskbarLayout.IsHorizontal(rect);
        }

        // 尺寸每拍按当前矩形与当前缩放重算，不能只在"矩形变了"时算：换线（Type-C / 扩展坞）后
        // 缩放会变而任务栏像素尺寸可能不变，那时贴片会一直用旧尺寸。
        float scale = dpi / 96f;
        int height = TaskbarLayout.PillHeight(rect, scale, TaskbarMarginDip, MinHeightDip, MaxHeightDip);
        if (height != _pillH || scale != _renderScale)
        {
            Log.Debug($"任务栏贴片：尺寸重算 {_pillW}×{height}，scale={scale:0.00}，任务栏={rect.Width}×{rect.Height}");
            _pillH = height;
            _renderScale = scale;
            _needRender = true;
        }

        if (_needRender || _bitmap == null) Render();
        ApplyPosition();
    }

    /// <summary>读当前任务栏与托盘矩形（物理像素、屏幕坐标）；<paramref name="hidden"/> 表示自动隐藏已收起。</summary>
    private bool TryGetTaskbar(out PixelRect taskbar, out PixelRect tray, out bool hidden)
    {
        taskbar = default;
        tray = default;
        hidden = false;

        if (_taskbar == IntPtr.Zero || !Native.GetWindowRect(_taskbar, out var rect)) return false;

        taskbar = new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
        if (TaskbarLayout.IsTaskbarHidden(taskbar))
        {
            hidden = true;
            return true;
        }

        // 托盘找不到不算失败：退回用任务栏自身边缘定位，贴片照样显示
        var trayHwnd = Native.FindWindowExW(_taskbar, IntPtr.Zero, Native.TrayNotifyClassName, null);
        if (trayHwnd != IntPtr.Zero && Native.GetWindowRect(trayHwnd, out var trayRect))
            tray = new PixelRect(trayRect.Left, trayRect.Top, trayRect.Width, trayRect.Height);
        return true;
    }

    private void Render()
    {
        if (_pillH <= 0) return;   // 还没算出任务栏厚度，等下一拍

        var devices = SelectDevices(out var overflowNames);
        var bmp = TaskbarPillRenderer.Render(
            devices, overflowNames,
            devices.ToDictionary(d => d.Id, d => App.Services.Manager.GetRemainingTimeEstimate(d)),
            TaskbarPillRenderer.SkinFor(IsLightTheme()), _pillH, Scale,
            out int width, out var hits);

        _bitmap?.Dispose();
        _bitmap = bmp;
        _pillW = width;
        _hits = hits;
        _needRender = false;

        // 电量刷新很勤：同一台仍在鼠标下则只重摆位置，不重新计悬停
        if (_tipVisible && _shownTipId != null)
            RepositionTipIfStillHovering();
    }

    /// <summary>
    /// 取要显示的设备。**本机电量恒定排除**：Windows 任务栏自己已经有一个电池图标，
    /// 再在贴片上重复一遍只是噪音（与托盘"最低电量"不统计本机是同一个理由）。
    /// </summary>
    private static List<DeviceStatus> SelectDevices(out IReadOnlyList<string> overflowNames)
    {
        overflowNames = Array.Empty<string>();
        var all = App.Services.Manager.Devices
            .Where(d => d.IsOnline && d.Source != DeviceSource.System)
            .Select(d => DevicePresentation.Apply(d, App.Services.Settings))
            .OrderBy(d => DeviceStatusFormatter.IsChargingOrFull(d.Charging))
            .ThenBy(d => d.BatteryPercent ?? 999)
            .ThenBy(d => d.DisplayName)
            .ToList();

        if (TaskbarBatteryMode.Normalize(App.Services.Settings.TaskbarBatteryMode) == TaskbarBatteryMode.Lowest)
            return all.Count > 0 ? [all[0]] : [];

        int max = TaskbarBatteryOptions.Clamp(App.Services.Settings.TaskbarBatteryMaxDevices);
        if (all.Count <= max) return all;
        overflowNames = all.Skip(max).Select(d => d.DisplayName).ToList();
        return all.GetRange(0, max);
    }

    private void ApplyPosition()
    {
        if (_bitmap == null || _pillW == 0 || _pillH == 0) return;

        int gap = (int)Math.Round(EdgeGapDip * Scale);
        int placeOffset = OffsetForCurrentDpi(out _);
        var (x, y) = TaskbarLayout.Place(_taskbarRect, _trayRect, _pillW, _pillH, placeOffset, gap, out int applied);
        // 过渡帧（拔插显示器时任务栏/托盘矩形短暂错位）也会夹紧；只有拖动中才把夹紧结果写回记忆，
        // 否则分辨率往返后用户偏移会被污染。反推写回语义见 docs/dev/runtime-pitfalls.md §16.5。
        if (_dragging) _offsetPx = applied;

        // 子窗口的坐标是**相对父窗口客户区**的，定位数学算出来的是屏幕坐标，这里换算一次
        var pt = new Native.POINT { X = x, Y = y };
        Native.ScreenToClient(_taskbar, ref pt);
        Upload(pt.X, pt.Y);
    }

    /// <summary>把记下的偏移换算成当前任务栏 DPI 下的物理像素；旧配置 Dpi=0 时首次只绑定、不改像素。</summary>
    private int OffsetForCurrentDpi(out uint currentDpi)
    {
        currentDpi = TaskbarDpi();
        if (currentDpi == 0) currentDpi = 96;
        if (_offsetDpi <= 0)
        {
            _offsetDpi = (int)currentDpi;
            return _offsetPx;
        }
        if (_dragging) return _offsetPx;
        return TaskbarLayout.ScaleOffset(_offsetPx, (uint)_offsetDpi, currentDpi);
    }

    // ---------- 位图提交 ----------

    private void EnsureDib(int w, int h)
    {
        if (_dib != IntPtr.Zero && _dibW == w && _dibH == h) return;
        ReleaseDib();

        var bmi = new Native.BITMAPINFO
        {
            bmiHeader = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,   // 负高度 = 自上而下，与 Bitmap 的行序一致
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            },
        };
        _memDc = Native.CreateCompatibleDC(IntPtr.Zero);
        _dib = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out _dibBits, IntPtr.Zero, 0);
        _oldDib = Native.SelectObject(_memDc, _dib);
        _dibW = w;
        _dibH = h;
    }

    private void ReleaseDib()
    {
        if (_memDc != IntPtr.Zero)
        {
            if (_oldDib != IntPtr.Zero) Native.SelectObject(_memDc, _oldDib);
            Native.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero)
        {
            Native.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        _oldDib = IntPtr.Zero;
        _dibBits = IntPtr.Zero;
        _dibW = _dibH = 0;
    }

    private void Upload(int x, int y)
    {
        if (_bitmap == null) return;
        EnsureDib(_pillW, _pillH);
        if (_dib == IntPtr.Zero) return;

        CopyToDib(_bitmap, _pillW, _pillH);

        var dst = new Native.POINT { X = x, Y = y };
        var size = new Native.SIZE { cx = _pillW, cy = _pillH };
        var src = new Native.POINT { X = 0, Y = 0 };
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = Native.AC_SRC_ALPHA,
        };
        if (!Native.UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA))
            Log.Warn($"任务栏贴片：UpdateLayeredWindow 失败，err={Marshal.GetLastWin32Error()}");
    }

    private void CopyToDib(Gdi.Bitmap bmp, int w, int h)
    {
        var data = bmp.LockBits(new Gdi.Rectangle(0, 0, w, h), GdiImaging.ImageLockMode.ReadOnly, GdiImaging.PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                Marshal.Copy(row, 0, _dibBits + y * w * 4, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    // ---------- 交互 ----------

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _current;
        if (self == null)
            return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        switch (msg)
        {
                case Native.WM_LBUTTONDOWN:
                    self.OnLButtonDown();
                    return IntPtr.Zero;
                case Native.WM_LBUTTONDBLCLK:
                    self.OnDoubleClick();
                    return IntPtr.Zero;
                case Native.WM_MOUSEMOVE:
                    self.OnMouseMove(lParam);
                    return IntPtr.Zero;
                case Native.WM_MOUSEHOVER:
                    self.OnMouseHover();
                    return IntPtr.Zero;
                case Native.WM_MOUSELEAVE:
                    self.OnMouseLeave();
                    return IntPtr.Zero;
                case Native.WM_MOUSEACTIVATE:
                {
                    // 让 DefWindowProcW 先处理：子窗口默认会把消息发给父窗口（Shell_TrayWnd）。
                    // 只有父窗口要"吃掉消息"时才干预，否则保留原返回值（支持双击等正常行为）。
                    int ret = Native.DefWindowProcW(hwnd, msg, wParam, lParam).ToInt32();
                    if (ret == Native.MA_ACTIVATEANDEAT)
                        return (IntPtr)Native.MA_NOACTIVATE;
                    return (IntPtr)ret;
                }
                case Native.WM_LBUTTONUP:
                    self.OnLButtonUp();
                    return IntPtr.Zero;
                case Native.WM_RBUTTONUP:
                    self.OnRightClick();
                    return IntPtr.Zero;
                case Native.WM_DISPLAYCHANGE:
                case Native.WM_DPICHANGED:
                    // 重建可能 DestroyWindow，绝不能在本过程里同步做——推迟到队列
                    self.RequestRebuild();
                    return IntPtr.Zero;
                case Native.WM_SETTINGCHANGE:
                    self._needRender = true;
                    self._dispatcher.TryEnqueue(self.Update);
                    return IntPtr.Zero;
            }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnLButtonDown()
    {
        if (_positionLocked || _dragging) return;

        HideTip();
        _hoverId = null;
        _hoverArmed = false;

        // 用当帧可见（已夹紧）的偏移做起点：屏幕变窄贴片贴边时，一拖不会跳回越界前的记忆值
        int gap = (int)Math.Round(EdgeGapDip * Scale);
        int placeOffset = OffsetForCurrentDpi(out uint dpi);
        TaskbarLayout.Place(_taskbarRect, _trayRect, _pillW, _pillH, placeOffset, gap, out int visible);
        _offsetPx = visible;
        _offsetDpi = (int)dpi;

        _dragging = true;
        _dragStartOffset = visible;

        var p = Native.GetCursorPos();
        _dragStartCursor = _taskbarHorizontal ? p.X : p.Y;
        _pollX = p.X;
        _pollY = p.Y;
        _dragIdleTicks = 0;

        _dragTimer.Interval = TimeSpan.FromMilliseconds(DragTickMs);
    }

    /// <summary>
    /// 常驻轮询（20Hz 起步，拖动中切 60Hz）：Win11 任务栏 XAML 会吃掉子窗口鼠标消息，
    /// 不能靠 WM_LBUTTONDOWN 启动拖动，只能直接读全局按键状态（runtime-pitfalls.md §16.5.1）。
    /// </summary>
    private void OnDragTick()
    {
        if (_disposed) return;

        bool btnDown = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
        if (_positionLocked)
        {
            EndDrag();
            _btnWasDown = btnDown;
            return;
        }
        var p = Native.GetCursorPos();

        if (!_dragging)
        {
            // 检测按下边缘：上一轮没按、这一轮按了，且光标落在贴片内 → 启动拖动
            if (btnDown && !_btnWasDown && IsCursorInPill(p))
            {
                OnLButtonDown();
            }
        }
        else
        {
            if (!btnDown)
            {
                EndDrag();
                _btnWasDown = false;
                return;
            }

            bool moved = p.X != _pollX || p.Y != _pollY;
            _pollX = p.X;
            _pollY = p.Y;

            // 手停住就退到 20Hz：只有真在拖时才需要 60Hz，松手最多晚 50ms 发现
            if (!moved)
            {
                if (++_dragIdleTicks >= DragIdleTicks)
                    _dragTimer.Interval = TimeSpan.FromMilliseconds(DragIdleTickMs);
                _btnWasDown = true;
                return;
            }

            _dragIdleTicks = 0;
            _dragTimer.Interval = TimeSpan.FromMilliseconds(DragTickMs);

            int next = _dragStartOffset + ((_taskbarHorizontal ? p.X : p.Y) - _dragStartCursor);
            if (next == _offsetPx)
            {
                _btnWasDown = true;
                return;
            }
            _offsetPx = next;
            ApplyPosition();
        }

        _btnWasDown = btnDown;
    }

    /// <summary>光标当前是否落在贴片矩形内（屏幕坐标）。</summary>
    private bool IsCursorInPill(Native.POINT p)
    {
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return false;
        return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    /// <summary>结束拖动：偏移真变了才写盘（单击一下不该产生一次磁盘写）。</summary>
    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _dragTimer.Interval = TimeSpan.FromMilliseconds(DragIdleTickMs);

        if (_offsetPx == _dragStartOffset) return;
        var s = App.Services.Settings;
        s.TaskbarBatteryOffsetX = _offsetPx;
        s.TaskbarBatteryOffsetDpi = _offsetDpi;
        s.Save();
        Log.Debug($"任务栏贴片：拖动结束，offset={_offsetPx} dpi={_offsetDpi}");
    }

    private void OnMouseMove(IntPtr lParam)
    {
        if (_dragging && !_positionLocked)
        {
            var p = Native.GetCursorPos();
            // 只沿任务栏方向拖：横向任务栏走 X，竖向走 Y（斜着拖不会把贴片拽离任务栏）
            _offsetPx = _dragStartOffset + ((_taskbarHorizontal ? p.X : p.Y) - _dragStartCursor);
            ApplyPosition();
            return;
        }

        long lp = lParam.ToInt64();
        int x = unchecked((short)(lp & 0xFFFF));
        string? id = HitIdAt(x);

        if (id != _hoverId)
        {
            HideTip();
            _hoverId = id;
            _hoverArmed = false;
        }

        // 已在等 WM_MOUSEHOVER 时不要再 Track——会把悬停计时清零
        if (_hoverArmed) return;

        // 悬停用 TME_HOVER（WndProc 里启 DispatcherQueueTimer 不可靠）；一次 Track 同时带上 LEAVE
        ArmMouseTracking(hover: _hoverId != null && !_tipVisible);
    }

    private void OnMouseHover()
    {
        _hoverArmed = false;
        if (_disposed || _dragging || _hoverId == null) return;
        int i = IndexOfHit(_hoverId);
        if (i < 0) return;
        ShowTip(_hits[i]);
        ArmMouseTracking(hover: false);
    }

    private void OnMouseLeave()
    {
        _hoverArmed = false;
        _hoverId = null;
        HideTip();
    }

    private void ArmMouseTracking(bool hover)
    {
        if (_hwnd == IntPtr.Zero) return;
        var tme = new Native.TRACKMOUSEEVENT
        {
            cbSize = (uint)Marshal.SizeOf<Native.TRACKMOUSEEVENT>(),
            dwFlags = hover ? Native.TME_HOVER | Native.TME_LEAVE : Native.TME_LEAVE,
            hwndTrack = _hwnd,
            dwHoverTime = HoverDelayMs(),
        };
        if (Native.TrackMouseEvent(ref tme) && hover)
            _hoverArmed = true;
    }

    private static uint HoverDelayMs()
    {
        uint ms = 400;
        Native.SystemParametersInfoW(Native.SPI_GETMOUSEHOVERTIME, 0, ref ms, 0);
        return ms == 0 ? 400u : ms;
    }

    private string? HitIdAt(int clientX)
    {
        if (_hits.Count == 0) return null;
        var regions = new (int X, int Width)[_hits.Count];
        for (int i = 0; i < _hits.Count; i++)
            regions[i] = (_hits[i].X, _hits[i].Width);
        int iHit = TaskbarLayout.HitTest(regions, clientX);
        return iHit >= 0 ? _hits[iHit].Id : null;
    }

    private void RepositionTipIfStillHovering()
    {
        if (_shownTipId == null) return;
        int i = IndexOfHit(_shownTipId);
        if (i < 0)
        {
            HideTip();
            return;
        }
        ShowTip(_hits[i]);
    }

    private int IndexOfHit(string id)
    {
        for (int i = 0; i < _hits.Count; i++)
            if (_hits[i].Id == id) return i;
        return -1;
    }

    private void ShowTip(TaskbarPillRenderer.HitChip chip)
    {
        if (_hwnd == IntPtr.Zero || string.IsNullOrEmpty(chip.Tip)) return;
        EnsureTipWindow();
        if (_tipHwnd == IntPtr.Zero) return;

        using var bmp = TaskbarTipRenderer.Render(
            chip.Tip, TaskbarTipRenderer.SkinFor(IsLightTheme()), Scale,
            out int fullW, out int fullH, out int shadowPx, out int bubbleW, out int bubbleH);
        // PlaceTooltip 按气泡本体摆，窗口再外扩阴影边距
        if (!TryChipScreenRect(chip, out var chipScreen)) return;
        var work = WorkAreaNearPill();
        int gap = (int)Math.Round(TipGapDip * Scale);
        var (tx, ty) = TaskbarLayout.PlaceTooltip(_taskbarRect, chipScreen, bubbleW, bubbleH, gap, work);

        UploadTip(bmp, tx - shadowPx, ty - shadowPx, fullW, fullH);
        Native.ShowWindow(_tipHwnd, Native.SW_SHOWNOACTIVATE);
        Native.SetWindowPos(_tipHwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        _tipVisible = true;
        _shownTipId = chip.Id;
    }

    private void EnsureTipDib(int w, int h)
    {
        if (_tipDib != IntPtr.Zero && _tipDibW == w && _tipDibH == h) return;
        ReleaseTipDib();

        var bmi = new Native.BITMAPINFO
        {
            bmiHeader = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            },
        };
        _tipMemDc = Native.CreateCompatibleDC(IntPtr.Zero);
        _tipDib = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out _tipDibBits, IntPtr.Zero, 0);
        _tipOldDib = Native.SelectObject(_tipMemDc, _tipDib);
        _tipDibW = w;
        _tipDibH = h;
    }

    private void ReleaseTipDib()
    {
        if (_tipMemDc != IntPtr.Zero)
        {
            if (_tipOldDib != IntPtr.Zero) Native.SelectObject(_tipMemDc, _tipOldDib);
            Native.DeleteDC(_tipMemDc);
            _tipMemDc = IntPtr.Zero;
        }
        if (_tipDib != IntPtr.Zero)
        {
            Native.DeleteObject(_tipDib);
            _tipDib = IntPtr.Zero;
        }
        _tipOldDib = IntPtr.Zero;
        _tipDibBits = IntPtr.Zero;
        _tipDibW = _tipDibH = 0;
    }

    private void UploadTip(Gdi.Bitmap bmp, int x, int y, int w, int h)
    {
        if (_tipHwnd == IntPtr.Zero) return;
        EnsureTipDib(w, h);
        if (_tipDib == IntPtr.Zero) return;

        var data = bmp.LockBits(new Gdi.Rectangle(0, 0, w, h), GdiImaging.ImageLockMode.ReadOnly, GdiImaging.PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[w * 4];
            for (int rowY = 0; rowY < h; rowY++)
            {
                Marshal.Copy(data.Scan0 + rowY * data.Stride, row, 0, row.Length);
                Marshal.Copy(row, 0, _tipDibBits + rowY * w * 4, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        var dst = new Native.POINT { X = x, Y = y };
        var size = new Native.SIZE { cx = w, cy = h };
        var src = new Native.POINT { X = 0, Y = 0 };
        var blend = new Native.BLENDFUNCTION
        {
            BlendOp = Native.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = Native.AC_SRC_ALPHA,
        };
        if (!Native.UpdateLayeredWindow(_tipHwnd, IntPtr.Zero, ref dst, ref size, _tipMemDc, ref src, 0, ref blend, Native.ULW_ALPHA))
            Log.Warn($"任务栏贴片：提示 UpdateLayeredWindow 失败，err={Marshal.GetLastWin32Error()}");
    }

    private bool TryChipScreenRect(TaskbarPillRenderer.HitChip chip, out PixelRect rect)
    {
        rect = default;
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var wr)) return false;
        rect = new PixelRect(wr.Left + chip.X, wr.Top, chip.Width, _pillH);
        return true;
    }

    private PixelRect WorkAreaNearPill()
    {
        if (_hwnd == IntPtr.Zero) return default;
        var mon = Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);
        if (mon == IntPtr.Zero) return default;
        var info = new Native.MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFOEX>() };
        if (!Native.GetMonitorInfoW(mon, ref info)) return default;
        var r = info.rcWork;
        return new PixelRect(r.Left, r.Top, r.Width, r.Height);
    }

    private void HideTip()
    {
        if (_tipHwnd != IntPtr.Zero && Native.IsWindow(_tipHwnd))
            Native.ShowWindow(_tipHwnd, Native.SW_HIDE);
        _tipVisible = false;
        _shownTipId = null;
    }

    private void OnLButtonUp() => EndDrag();

    private void OnDoubleClick()
    {
        if (_dragging) EndDrag();
        TrayService.ShowMainWindow();
    }

    private void OnRightClick()
    {
        HideTip();
        var p = Native.GetCursorPos();
        AppPopupMenu.Show(_hwnd, p.X, p.Y);
    }

    // ---------- 环境 ----------

    /// <summary>任务栏所在显示器的缩放。贴片是它的子窗口，按任务栏的 DPI 而不是自己的。</summary>
    private float Scale
    {
        get
        {
            uint dpi = TaskbarDpi();
            return dpi == 0 ? 1f : dpi / 96f;
        }
    }

    private uint TaskbarDpi() =>
        _taskbar != IntPtr.Zero ? Native.GetDpiForWindow(_taskbar) : 0;

    /// <summary>应用主题跟随系统，原生窗口读注册表比拿 WinUI 的 ActualTheme 更直接。</summary>
    private static bool IsLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") as int? != 0;
        }
        catch
        {
            return true;
        }
    }
}
