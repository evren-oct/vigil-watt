using System.Runtime.InteropServices;
using VigilWatt.App.Services;
using VigilWatt.App.Views;
using VigilWatt.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace VigilWatt.App;
/// <summary>
/// 功 能： 主窗口，在设备、统计、历史、设置和关于之间切换。
/// 2026/10/06 15:53:36 xliu
/// </summary>

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private bool _firstNavigation = true;
    private bool _initialSizeApplied;

    public MainWindow()
    {
        InitializeComponent();

        Title = "VigilWatt";
        AppWindow.Title = "VigilWatt";
        ApplyWindowIcon();
        VersionHeader.Content = $"v{GetType().Assembly.GetName().Version?.ToString(3) ?? "—"}";
        // 构造期 GetDpiForWindow 可能还是 96；先给一个兜底尺寸，Loaded 后再按真实 DPI 定尺
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpiScale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(900 * dpiScale), (int)(640 * dpiScale)));

            // 标题栏融入内容：Win11 / Win10 都做，与背景材质无关。
            // 旧版塞在 MicaController.IsSupported() 分支里，于是 Win10 上系统照常画标准标题栏，
            // 而内容区的 AppTitleBar 又画一遍应用名 —— 窗口里出现两个标题（"Win10 不好看"的头号原因）。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ApplyBackdrop();
        SyncTitleBarButtonColors();
        Activated += (_, _) => App.OnMainWindowActivated();
        Root.Loaded += (_, _) =>
        {
            SyncTitleBarButtonColors();
            ApplyInitialSize();
        };
        Root.ActualThemeChanged += (_, _) => SyncTitleBarButtonColors();

        // 关闭按钮 = 隐藏到托盘
        AppWindow.Closing += (s, e) =>
        {
            e.Cancel = true;
            HideToTray();
        };

        // 初次进入设备页
        ContentFrame.Navigate(typeof(DevicesPage));
    }

    /// <summary>窗口真正显示后再取一次 DPI：构造期常拿到 96，直接按物理像素算会在 150% 屏上缩成 600 DIP。</summary>
    private void ApplyInitialSize()
    {
        if (_initialSizeApplied) return;
        _initialSizeApplied = true;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpiScale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(900 * dpiScale), (int)(640 * dpiScale)));
    }

    /// <summary>隐藏到托盘并安排一次工作集修剪：用户 2026-11-25 明确，隐藏后要的就是任务管理器那个数字。</summary>
    public void HideToTray()
    {
        AppWindow.Hide();
        WorkingSetTrim.Queue();
    }

    /// <summary>
        /// 按用户设置 + 本机能力应用窗口背景材质（设置页改完也会调这里即时生效）。
        /// 三级回退：Mica（Win11）→ Acrylic（Win10 1809+）→ 纯色分层底。
        /// </summary>
    public void ApplyBackdrop()
    {
        try
        {
            var resolved = WindowBackdrop.Resolve(
                App.Services.Settings.WindowBackdrop,
                WindowBackdrop.IsMicaSupported,
                WindowBackdrop.IsAcrylicSupported);

            var backdrop = WindowBackdrop.Create(resolved);
            SystemBackdrop = backdrop;

            // 关键：有材质时 Root 必须透明，铺任何不透明背景都会把材质整个盖住（包括 Mica）。
            // 纯色模式下才铺一层分层色，让白色卡片能浮起来。
            Root.Background = backdrop == null
                ? (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"]
                : null;
        }
        catch (Exception ex)
        {
            // 背景不该让窗口起不来；出问题就退回纯色，窗口照常可用
            Log.Warn($"应用窗口背景失败，回退纯色: {ex.Message}");
            SystemBackdrop = null;
            try
            {
                Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
            }
            catch { /* 连资源都拿不到就保持现状 */ }
        }
    }

        /// <summary>
        /// ExtendsContentIntoTitleBar 之后，右上角系统按钮由 DWM 绘制、颜色不自动跟随主题
        /// （Win10 深色下会变成"黑按钮配深底"，几乎看不见），这里按当前主题显式刷一遍前景色。
        /// </summary>
    private void SyncTitleBarButtonColors()
    {
        try
        {
            var bar = AppWindow.TitleBar;
            var dark = Root.ActualTheme == ElementTheme.Dark;
            var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            bar.ButtonForegroundColor = fg;
            bar.ButtonHoverForegroundColor = fg;
            bar.ButtonPressedForegroundColor = fg;
            bar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
        }
        catch (Exception ex)
        {
            Log.Warn($"同步标题栏按钮配色失败: {ex.Message}");
        }
    }

        /// <summary>
        /// 运行时再设一次窗口图标：非打包 WinUI 3 有时不继承 exe 的 Win32 图标资源，
        /// 任务栏 / Alt-Tab 会显示成空白方块。
        /// ⚠ 本方法在窗口构造期调用，异常必须自己吞掉（构造期抛出会让应用起不来）。
        /// </summary>
    private void ApplyWindowIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "VigilWatt.ico");
            if (File.Exists(path))
            {
                AppWindow.SetIcon(path);
            }
            else
            {
                Log.Warn($"窗口图标文件不存在，跳过 SetIcon: {path}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"设置窗口图标失败: {ex.Message}");
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;
        var page = tag switch
        {
            "devices" => typeof(DevicesPage),
            "stats" => typeof(StatsPage),
            "history" => typeof(HistoryPage),
            "capture" => typeof(CapturePage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(DevicesPage),
        };
        // 首屏不播转场：缓存页复用后切页才淡入，避免启动时闪一下。
        var transition = _firstNavigation ? null : new EntranceNavigationTransitionInfo();
        _firstNavigation = false;
        ContentFrame.Navigate(page, null, transition);
        // 各页都已 NavigationCacheMode.Required 常驻，BackStack 只剩无界累积的簿记（CR-65）
        ContentFrame.BackStack.Clear();
    }
}
