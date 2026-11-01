using System.Runtime.InteropServices;

namespace VigilWatt.App.Services;
/// <summary>
/// 功 能： Win32 互操作声明。
/// 2026/10/06 15:53:36 xliu
/// </summary>

internal static class Native
{
    public delegate IntPtr WndProcDele(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const int GWL_EXSTYLE = -20;
    public const int GWLP_WNDPROC = -4;
    public const int WS_EX_TOOLWINDOW = 0x80;

    /// <summary>点击不激活窗口：任务栏贴片必须带它，否则点一下就把焦点从用户正在用的程序抢走。</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // ---- 无边框窗口的标准样式位（GWL_STYLE）----
    // 光调 AppWindow 的 SetBorderAndTitleBar(false,false) 只关掉标题栏；这几位留着，
    // Win11 仍会给窗口画系统描边 + 圆角外轮廓（贴片没底色，这一圈直接暴露"这里有个窗口"），
    // Win10 上非客户区还会把客户区挤窄——而 AppWindow.Resize 设的是**外框**，最右一格因此被裁。
    public const int GWL_STYLE = -16;

    /// <summary>WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU。</summary>
    private const long BorderlessStyleMask = 0x00CF0000;

    // ---- 托盘 ----
    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;

    public const uint NIF_MESSAGE = 0x1;
    public const uint NIF_ICON = 0x2;
    public const uint NIF_TIP = 0x4;
    public const uint NIF_INFO = 0x10;

    public const uint NIIF_WARNING = 0x2;
    public const uint NIIF_INFO = 0x1;

    public const uint WM_LBUTTONUP = 0x0202;
    public const uint NIN_BALLOONUSERCLICK = 0x0405;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_NULL = 0x0000;

    // ---- 设备变更（插拔广播，发给所有顶层窗口，不需要 RegisterDeviceNotification）----
    public const uint WM_DEVICECHANGE = 0x0219;
    public const uint DBT_DEVNODES_CHANGED = 0x0007;

    // ---- 菜单 ----
    public const uint MF_STRING = 0x0;
    public const uint MF_SEPARATOR = 0x800;
    public const uint MF_CHECKED = 0x8;
    public const uint MF_GRAYED = 0x1;
    public const uint TPM_RETURNCMD = 0x100;
    public const uint TPM_RIGHTBUTTON = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    /// <summary>启动失败时的最后一道可见反馈：此刻 XAML 可能还没就绪，只能用原生弹窗。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassW(ref WNDCLASSW wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CallWindowProcW(IntPtr prevProc, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) => GetWindowLongPtr64(hWnd, nIndex);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        SetWindowLongPtr64(hWnd, nIndex, value);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    private static extern bool GetCursorPosRaw(out POINT point);

    public static POINT GetCursorPos()
    {
        POINT p = default;
        GetCursorPosRaw(out p);
        return p;
    }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessageW(string message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? name);

    // ---- 任务栏定位（任务栏电量贴片用）----
    // 贴片窗口靠"找任务栏窗口 → 读它的矩形 → 把自己摆进去"来定位，不是 deskband（Win11 已移除该机制）。
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    /// <summary>任务栏主窗口类名（主显示器那块；任务栏被拖到副屏时它也跟过去）。</summary>
    public const string TaskbarClassName = "Shell_TrayWnd";

    /// <summary>副屏任务栏窗口类名（Win10/Win11「在所有显示器上显示任务栏」时启用）。</summary>
    public const string SecondaryTaskbarClassName = "Shell_SecondaryTrayWnd";

    /// <summary>任务栏内的通知区域（图标 + 时钟）类名，贴片就贴在它左边。</summary>
    public const string TrayNotifyClassName = "TrayNotifyWnd";

    // ---- 窗口圆角（仅 Win11）----
    // ⚠ Win10 **不要**用 SetWindowRgn 裁圆角：WinUI 3 的内容走 DirectComposition，SetWindowRgn
    // 只裁传统 GDI 区域、对 DComp 内容完全无效（实测"设了区域仍是直角"）。
    // Win10 的圆角靠"透明窗口 + XAML 自绘"，见 WidgetWindow.ApplyWin10Chrome。
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    /// <summary>圆角偏好：不圆。贴片没有底色，圆角只会留下一圈空弧线（Win11 专用）。</summary>
    public const int DWMWCP_DONOTROUND = 1;

    /// <summary>Win11 的窗口描边颜色属性；取值 <see cref="DWMWA_COLOR_NONE"/> 表示不画描边。</summary>
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // 注：曾用 `DWMWA_NCRENDERING_POLICY = DWMNCRP_ENABLED` 给 Win10 的无边框小组件找回 DWM 投影，
    // 但透明窗口要求 `DwmExtendFrameIntoClientArea(-1)`（整窗算边框），两者叠加会把整窗填成不透明底。
    // 该调用已删；投影与透明在 Win10 上互斥的实测见 docs/dev/runtime-pitfalls.md §17.8。

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    // SetWindowPos 的 Z 序插入位：钉桌面档靠 HWND_BOTTOM，切回普通/置顶档靠 HWND_TOP。
    // HWND_TOPMOST 留给 IsAlwaysOnTop 管，别混用。
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);

    /// <summary>任务栏贴片专用：把自己抬到任务栏之上（两者都是 topmost，后设的在上）。</summary>
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const int SW_HIDE = 0;

    /// <summary>显示但不激活——贴片不能被点击/显示动作抢走焦点。</summary>
    public const int SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    public const uint SWP_NOSIZE = 0x1;
    public const uint SWP_NOMOVE = 0x2;
    public const uint SWP_NOZORDER = 0x4;
    public const uint SWP_NOACTIVATE = 0x10;
    public const uint SWP_FRAMECHANGED = 0x20;

    /// <summary>
    /// 收敛无边框窗口外观：清掉 <see cref="BorderlessStyleMask"/> 里的边框样式位（改完必须补
    /// <c>SWP_FRAMECHANGED</c>，否则样式改动不一定立刻反映到外观），并关掉 DWM 圆角与外描边
    /// （这两项 Win10 上没有，失败即忽略——调用点不判返回值，别把窗口初始化搞挂）。
    /// </summary>
    public static void ApplyBorderlessChrome(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style & ~BorderlessStyleMask));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        int noRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));
        int noBorder = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
    }

    // ---- 清 Win32 那层窗口底色（现仅小组件用，封装在 TransparentWindowSurface）----
    // 窗口类背景刷是 COLOR_WINDOW，浅色主题下就是纯白。去掉它两步走——先让 DWM 改用窗口表面的
    // 逐像素 alpha 合成，再让客户区被擦成 alpha=0（GDI 填色不写 alpha 字节，正好留下 0）。
    // 注意这只是**两层**背景里的一层，另一层（内容岛）见 docs/dev/runtime-pitfalls.md §17。
    public const uint WM_PAINT = 0x000F;
    public const uint WM_ERASEBKGND = 0x0014;

    /// <summary>窗口销毁消息：子类化后用它把"按 HWND 存的旧窗口过程"记录摘掉。</summary>
    public const uint WM_NCDESTROY = 0x0082;

    /// <summary>stock object 里的黑色画刷。GDI 填色不写 alpha 字节，正好留下 0。</summary>
    public const int BLACK_BRUSH = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    /// <summary>把整个窗口都算作"边框"（margins 全 -1）：DWM 从此按窗口表面的 alpha 合成，而不是当成不透明。</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int index);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    public static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    // ---- 任务栏贴片「真嵌入」：SetParent 进任务栏 + 分层子窗口 ----
    // 原理与三道锁（SetParent / WS_EX_LAYERED / UpdateLayeredWindow）见 docs/dev/runtime-pitfalls.md §16。
    // **不能用 WinUI 3 窗口去 reparent**：内容岛的 DWM 合成链会断（灰屏），所以内容改走 ARGB 位图。
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const int WS_EX_LAYERED = 0x80000;

    /// <summary>窗口类样式：要收 WM_LBUTTONDBLCLK 就必须带它，否则双击只来两次单击。</summary>
    public const uint CS_DBLCLKS = 0x0008;

    public const int IDC_ARROW = 32512;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_MOUSEHOVER = 0x02A1;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_MOUSEACTIVATE = 0x0021;

    public const int MA_ACTIVATE = 1;
    public const int MA_NOACTIVATE = 2;
    public const int MA_ACTIVATEANDEAT = 3;
    public const int MA_NOACTIVATEANDEAT = 4;

    /// <summary>UpdateLayeredWindow 的逐像素 alpha 模式（配合 AC_SRC_ALPHA 使用）。</summary>
    public const uint ULW_ALPHA = 0x00000002;

    // ---- 贴片悬停（自绘提示窗 + TrackMouseEvent；勿挂进任务栏，见 runtime-pitfalls.md §16.10）----
    public const uint TME_HOVER = 0x00000001;
    public const uint TME_LEAVE = 0x00000002;
    public const uint SPI_GETMOUSEHOVERTIME = 0x0066;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfoW(uint action, uint uiParam, ref uint pvParam, uint fWinIni);

    /// <summary>系统拖动阈值（以按下点为中心的矩形），横向取 CX、竖向取 CY；默认都是 4px。</summary>
    public const int SM_CXDRAG = 68;
    public const int SM_CYDRAG = 69;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX info);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    // ---- 显示器身份与友好名（QueryDisplayConfig）----
    // MONITORINFOEX 只给 \\.\DISPLAYn：那是**当前显示配置里的序号**，换一次显示模式就可能指向另一块屏。
    // EnumDisplayDevices 对内置屏常返回 "Generic PnP Monitor"。真实名称与跨模式稳定的身份只有 QDC 有，
    // 实测对照见 docs/dev/monitor-identity.md。
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>模式数组只是"给 QDC 一块能写的缓冲"：内部联合体按 48 字节占位，总长与原生一致（64）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public ulong union0, union1, union2, union3, union4, union5;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME packet);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME packet);

    // ---- 按设备路径查显示器名字（含未激活的屏）----
    // QDC 对未激活的屏返回空名字，但 EnumDisplayDevices 的子设备仍带 EDID 名，
    // 且带 EDD_GET_DEVICE_INTERFACE_NAME 时 DeviceID 与 QDC 的 monitorDevicePath 是同一条。
    public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICEW
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevicesW(string? device, uint devNum, ref DISPLAY_DEVICEW info, uint flags);

    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    /// <summary>把窗口挂到别的窗口下（跨进程到任务栏）。**不会自动改 WS_CHILD/WS_POPUP**，调用方自己同步。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hwndChild, IntPtr hwndNewParent);

    /// <summary>读父窗口。拔插显示器时资源管理器会拆任务栏，贴片可能不再挂在 Shell_TrayWnd 下。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint flags);

    /// <summary>屏幕坐标 → 客户区坐标。贴片是任务栏的子窗口，坐标必须换算到父窗口客户区。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    public const int VK_LBUTTON = 0x01;

    /// <summary>贴片拖动靠它判断左键还按着没有：捕获对非前台窗口无效，只能轮询（runtime-pitfalls.md §16.5）。</summary>
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hwnd);

    /// <summary>贴片清扫残留窗口时要排除别的进程（Debug 版与安装版同时跑不能互相杀）。</summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    /// <summary>窗口所在显示器的 DPI。贴片按任务栏的 DPI 换算尺寸，不用自己的（子窗口的 DPI 跟父窗口）。</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorName);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);
}
