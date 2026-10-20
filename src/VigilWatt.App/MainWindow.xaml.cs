using System.Runtime.InteropServices;
using VigilWatt.App.Services;
using VigilWatt.App.Views;
using VigilWatt.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VigilWatt.App;
/// <summary>
/// 功 能： 主窗口，在设备、统计、历史、设置和关于之间切换。
/// 2026/10/06 15:53:36 xliu
/// </summary>

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public MainWindow()
    {
        InitializeComponent();

        Title = "VigilWatt";
        AppWindow.Title = "VigilWatt";
        ApplyWindowIcon();
        // AppWindow.Resize 用物理像素，按 DPI 换算成 800x600 DIP（runtime-pitfalls §6）
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpiScale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(800 * dpiScale), (int)(600 * dpiScale)));

            // 标题栏融入内容：Win11 / Win10 都做，与背景材质无关。
            // 旧版塞在 MicaController.IsSupported() 分支里，于是 Win10 上系统照常画标准标题栏，
            // 而内容区的 AppTitleBar 又画一遍应用名 —— 窗口里出现两个标题（"Win10 不好看"的头号原因）。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ApplyBackdrop();
        SyncTitleBarButtonColors();
        Activated += (_, _) => App.OnMainWindowActivated();
        Root.Loaded += (_, _) => SyncTitleBarButtonColors();
        Root.ActualThemeChanged += (_, _) => SyncTitleBarButtonColors();

        AppWindow.Closing += (s, e) =>
        {
            e.Cancel = true;
            if (AppVisibilityPolicy.CanHideMainWindow(App.Services.Settings))
                AppWindow.Hide();
            else
                TrayService.ExitApplication();
        };

        // 初次进入设备页
        ContentFrame.Navigate(typeof(DevicesPage));
        PrewarmSettingsPage();
    }

    /// <summary>
    /// 预热设置页：XBF 解析与控件构造是一次性开销，先在窗口可见前付掉，首次点设置就不用等。
    /// </summary>
    private void PrewarmSettingsPage()
    {
        try
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            ContentFrame.Navigate(typeof(DevicesPage));
            ContentFrame.BackStack.Clear();
        }
        catch (Exception ex)
        {
            // 预热失败不该拖垮启动；退回设备页即可，设置页之后照常按需构造
            Log.Warn($"设置页预热失败，回退设备页: {ex.Message}");
            try { ContentFrame.Navigate(typeof(DevicesPage)); } catch { /* 已尽力 */ }
        }
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
        ContentFrame.Navigate(tag switch
        {
            "devices" => typeof(DevicesPage),
            "stats" => typeof(StatsPage),
            "history" => typeof(HistoryPage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(DevicesPage),
        });
        // 各页都已 NavigationCacheMode.Required 常驻，BackStack 只剩无界累积的簿记（CR-65）
        ContentFrame.BackStack.Clear();
    }
}
