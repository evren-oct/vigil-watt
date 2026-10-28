using VigilWatt.App.Services;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace VigilWatt.App;

/// <summary>
/// 功 能： 桌面小组件：无边框悬浮卡片，纵向显示各设备电量，可拖动，不进 Alt-Tab/任务栏。
/// 2026/10/06 15:53:36 xliu
/// 完整论证见 docs/dev/runtime-pitfalls.md §14。
/// </summary>
public sealed partial class WidgetWindow : Window
{
    private bool _dragging;
    private int _dragOffsetX, _dragOffsetY;

    // 与 XAML 的 Root.Padding / ChipStack.Spacing 保持一致
    private const double PadH = 16, PadV = 14;
    private const double RowGap = 16;           // 行与行间距（= ChipStack.Spacing）
    private const double IconSize = 22;         // 设备类型图标方块边长
    private const double IconGap = 10;          // 图标与名称间距
    private const double RowInnerGap = 9;       // 名称行与电量条间距
    private const double BarHeight = 6;
    private const double LineHeight = IconSize; // 名称行行高由图标方块决定，行高确定就不需要运行时 Measure
    private const double RowHeight = IconSize + RowInnerGap + BarHeight;
    private const double NameMaxWidth = 200;    // 名称列上限（超出省略号），正常设备名都能放下
    private const double ChargeMarkWidth = 14;  // 充电标识 ⚡ 列宽度（13 号字近似，与列表页同款字符）
    private const double TagFontSize = 10;      // 来源徽标字号（与设备列表页一致）
    private const double TagPadH = 5;           // 来源徽标左右内边距

    /// <summary>n 行内容（含行间距）的总高度。</summary>
    private static double RowsHeight(int rows) => rows <= 0 ? 0 : rows * RowHeight + (rows - 1) * RowGap;

    /// <summary>
    /// Win10 圆角半径（DIP）。比 Win11 大一圈是刻意的：
    /// Win10 的圆角完全由 XAML 绘制（抗锯齿、干净），大一点的弧线观感更接近现代磁贴。
    /// </summary>
    private const double Win10CornerRadiusDip = 16;

    /// <summary>Win10 兼容外壳是否生效（决定是否走透明窗口 + XAML 自绘圆角）。</summary>
    private readonly bool _win10Chrome;

    public WidgetWindow()
    {
        InitializeComponent();

        Title = "VigilWatt 小组件";
        AppWindow.Title = Title;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            // 层级先按"普通窗口"起步，随后 ApplyDisplayMode 按设置覆盖（置顶 / 桌面）
            presenter.IsAlwaysOnTop = false;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        // WS_EX_TOOLWINDOW：不出现在 Alt-Tab 和任务栏
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var exStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE,
            new IntPtr(exStyle.ToInt64() | Native.WS_EX_TOOLWINDOW));

        // Win10 走兼容外壳（透明窗口 + 恢复投影 + 大一号圆角）；Win11 保持原样
        _win10Chrome = WidgetCompat.NeedsTransparentChrome(Environment.OSVersion.Version.Build);
        if (_win10Chrome)
        {
            // XAML 里写死的 12 是 Win11 档；Win10 靠 XAML 自绘轮廓，半径大一圈弧线更立得住
            Root.CornerRadius = new CornerRadius(Win10CornerRadiusDip);
            ApplyWin10Chrome();
        }

        var s = App.Services.Settings;
        AppWindow.Move(new PointInt32((int)s.WidgetX, (int)s.WidgetY));
        // 层级：置顶 / 普通 / 桌面（窗口句柄已就绪，构造期就能应用）
        ApplyDisplayMode(s.WidgetDisplayMode);

        App.Services.Manager.DeviceUpdated += OnDeviceUpdated;
        App.Services.Manager.DevicesChanged += OnDevicesChanged;
        // 硬编码色值按主题分套，切主题要重画一遍
        Root.ActualThemeChanged += OnThemeChanged;
        Closed += OnClosed;

        // 透明背板要在窗口真正接上内容岛之后再补一次：构造期那一次不保证生效（贴片同理）
        Root.Loaded += (_, _) =>
        {
            if (_win10Chrome) ApplyWin10Chrome();
            RefreshContent();
        };
        RefreshContent();
    }

    // 小组件可反复开/关（设置页每次保存都 Close 后置 null）。订阅必须用命名方法并在 Closed 里解绑，
    // 否则旧实例被 Manager 强引用、回调叠加（见 docs/reviews/BACKLOG.md CR-09）。
    private void OnDeviceUpdated(DeviceStatus _) => RequestRefresh();
    private void OnDevicesChanged() => RequestRefresh();
    private void OnThemeChanged(FrameworkElement sender, object args) => RefreshContent();

    private int _refreshQueued;

    /// <summary>
    /// 合并刷新请求（CR-29）：一轮轮询会对 N 台设备各发一次 DeviceUpdated、外加一次 DevicesChanged，
    /// 逐个排队就是 N+1 次"清空 + 全量重建"。这里用 Interlocked 只保留一次待执行的刷新
    /// （与 TrayService.RequestUpdate 同一套路）。
    /// </summary>
    private void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);   // 先复位再刷新：刷新期间来的请求不会被吞掉
            RefreshContent();
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        App.Services.Manager.DeviceUpdated -= OnDeviceUpdated;
        App.Services.Manager.DevicesChanged -= OnDevicesChanged;
        Root.ActualThemeChanged -= OnThemeChanged;
    }

    private double Scale => Root.XamlRoot?.RasterizationScale is > 0 ? Root.XamlRoot.RasterizationScale : 1.0;

    /// <summary>
    /// Win10 专用外壳：窗口表面全透明，圆角与卡片交给 XAML 画。
    /// ⚠ **别**在这里恢复 DWM 非客户区渲染——它会把整窗填成白底，与透明互斥，见 runtime-pitfalls §17.8。
    /// </summary>
    private void ApplyWin10Chrome()
    {
        try
        {
            TransparentWindowSurface.Apply(this);
        }
        catch (Exception ex)
        {
            // 外壳失败不能影响功能：窗口仍可用，只是没有透明
            Log.Warn($"小组件 Win10 外壳应用失败: {ex}");
        }
    }

    private void RefreshContent()
    {
        // 本方法经 DispatcherQueue.TryEnqueue 调用，回调里未捕获的异常会绕过
        // Application.UnhandledException 直接 fail-fast（combase E_FAIL、进程静默消失），必须整体兜底
        try
        {
            RefreshContentCore();
        }
        catch (Exception ex)
        {
            Log.Warn($"小组件刷新失败: {ex}");
        }
    }

    private void RefreshContentCore()
    {
        ChipStack.Children.Clear();
        var skin = SkinFor(Root.ActualTheme);

        // 低电量优先：充电中/已满的排末尾，未知电量殿后，同级按名称保持稳定
        var devices = App.Services.Manager.Devices
            .Where(d => d.IsOnline)
            // Kind 要在这里补好：小组件按 Kind 直绘图标，插件声明 Unknown 的（雷柏/迈从）否则会画成「未识别」
            .Select(d => DevicePresentation.WithIconKind(
                DevicePresentation.Apply(d, App.Services.Settings), App.Services.Settings))
            .OrderBy(d => DeviceStatusFormatter.IsChargingOrFull(d.Charging))
            .ThenBy(d => d.BatteryPercent ?? 999)
            .ThenBy(d => d.DisplayName)
            .ToList();

        if (devices.Count == 0)
        {
            ChipStack.Children.Add(new TextBlock
            {
                Text = "暂无在线设备",
                FontSize = 13,
                Height = LineHeight,
                Width = 100,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(skin.Text) { Opacity = 0.55 },
            });
            ResizeContent(PadH * 2 + 100 + 6, PadV * 2 + LineHeight + 2);
            return;
        }

        // 所有行共用同一行宽（取最宽者），名称左对齐、百分比右对齐
        double nameW = 0, rowW = 0;
        foreach (var d in devices)
        {
            double w = Math.Min(EstimateNameWidth(d.DisplayName), NameMaxWidth);
            if (w > nameW) nameW = w;
        }
        foreach (var d in devices)
        {
            // 充电行比放电行多一列 ⚡（列宽 + 该列两侧间距）；放电行的 ⚡ 列放空串，只占列间距。
            // 来源徽标（蓝牙/2.4G/本机）所有行恒定存在。
            double chargeW = DeviceStatusFormatter.IsCharging(d.Charging) ? ChargeMarkWidth : 0;
            double w = IconSize + IconGap + nameW + 10 + SourceTagWidth(d.Source) + 10 + chargeW + 10 + PercentWidth(d);
            if (w > rowW) rowW = w;
        }

        foreach (var d in devices)
        {
            ChipStack.Children.Add(BuildRow(d, rowW, skin));
        }

        // 一屏最多显示 MaxVisibleDevices 条（设置项，1~5，默认 3），多出来的靠滚轮查看。
        // 高度按 min(实际条数, 上限) 算：窗口尺寸因此**封顶**，设备增减不会让卡片一跳一跳。
        // 上限的 DIP 高度向上取整，避免滚动容器把最后一行切掉半个像素。
        var maxVisible = WidgetMaxDevicesOptions.Clamp(App.Services.Settings.WidgetMaxDevices);
        var visibleRows = Math.Min(devices.Count, maxVisible);
        double visibleRowsH = Math.Ceiling(RowsHeight(visibleRows));

        // 总宽补上 Root Border 的 1 DIP 边框线（两侧共 2）+ DPI 舍入余量，
        // 少算几个 DIP 最右侧的百分比列就会被窗口边缘裁掉
        ResizeContent(PadH * 2 + rowW + 6, PadV * 2 + visibleRowsH + 8);
    }

    private void ResizeContent(double widthDip, double heightDip)
    {
        var scale = Scale;
        // 防御性下限：算出 NaN/负数时保底，避免 AppWindow.Resize 收到非法值
        AppWindow.Resize(new SizeInt32(
            (int)Math.Ceiling(Math.Max(40, widthDip) * scale),
            (int)Math.Ceiling(Math.Max(24, heightDip) * scale)));
        ClampToWorkArea();
    }

    /// <summary>
    /// 让窗口本身是圆角，只对 Win11 有效：Border 的 CornerRadius 只作用于卡片那一层，
    /// 窗口矩形背板会在四角把它糊住，所以窗口本身也要圆角。
    /// **Win10 不能走这里**（没有圆角 API，SetWindowRgn 对 DComp 无效），改由 XAML 自绘。
    /// </summary>
    private void ApplyRoundedCorner()
    {
        if (_win10Chrome) return;   // Win10 的圆角由 XAML 画，不碰窗口

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            int pref = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch (Exception ex)
        {
            Log.Warn($"小组件圆角设置失败: {ex}");
        }
    }

    /// <summary>确保窗口完整落在最近显示器的工作区内。</summary>
    private void ClampToWorkArea()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            int x = Math.Clamp(pos.X, area.WorkArea.X, Math.Max(area.WorkArea.X, area.WorkArea.X + area.WorkArea.Width - size.Width));
            int y = Math.Clamp(pos.Y, area.WorkArea.Y, Math.Max(area.WorkArea.Y, area.WorkArea.Y + area.WorkArea.Height - size.Height));
            if (x != pos.X || y != pos.Y)
                AppWindow.Move(new PointInt32(x, y));
        }
        catch
        {
            // 显示器信息拿不到就算了
        }
    }

    /// <summary>13 号字下的近似文本宽度：CJK≈15px、ASCII≈8.5px（偏宽松，多余宽度变成名称与百分比之间的空隙）。</summary>
    private static double EstimateNameWidth(string text)
    {
        double w = 0;
        foreach (var ch in text) w += ch > 0x2E80 ? 15 : 8.5;
        return w;
    }

    /// <summary>16 号加粗下百分比文本的近似宽度（偏宽松：估小了会被窗口右缘裁掉）。</summary>
    private static double PercentWidth(DeviceStatus d) =>
        d.BatteryPercent.HasValue ? $"{d.BatteryPercent}%".Length * 11 : 22;

    /// <summary>来源徽标近似宽度（10 号字 + 左右内边距）：CJK≈10px、ASCII≈6px，偏宽松留余量。</summary>
    private static double SourceTagWidth(DeviceSource source)
    {
        double w = TagPadH * 2;
        foreach (var ch in DeviceSourceInfo.ShortLabel(source)) w += ch > 0x2E80 ? 10 : 6;
        return w;
    }

    private static UIElement BuildRow(DeviceStatus d, double width, Skin skin)
    {
        var percent = d.BatteryPercent;
        var charging = DeviceStatusFormatter.IsCharging(d.Charging);
        // 进度条与百分比数字共用这一个状态色，两者永远同色（>50% 绿 / ≤50% 琥珀 / ≤20% 红 / 充电蓝）
        var stateColor = UiPalette.BatteryLevel(percent, charging);

        var row = new StackPanel { Spacing = RowInnerGap, Width = width };

        // 图标 Auto 列、名称 * 列自适应收缩（超长省略号）、来源徽标 Auto 列、充电标识 Auto 列、百分比 Auto 列，互不挤占。
        // 徽标与充电列在所有行恒定存在（不充电时放空串），五列结构一致，名称列宽与百分比右缘因此对得齐
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        line.Children.Add(DeviceIconFactory.Boxed(d.Kind, skin.IconFill, skin.IconStroke, IconSize));

        var name = new TextBlock
        {
            Text = d.DisplayName,
            FontSize = 13,
            Height = LineHeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(skin.Text),
        };
        Grid.SetColumn(name, 1);
        line.Children.Add(name);

        // 来源徽标：与设备列表页同款彩色胶囊（蓝牙蓝 / 2.4G 紫 / 本机青）
        var sourceTag = BuildSourceTag(d.Source);
        Grid.SetColumn(sourceTag, 2);
        line.Children.Add(sourceTag);

        // 充电标识：与设备列表页同款 ⚡（列表页由 Visibility 绑定 IsCharging 控制显隐；
        // 这里用空串占位，既保持五列结构对齐，又让 ⚡ 用与百分比/进度条相同的充电蓝色）
        var chargeMark = new TextBlock
        {
            Text = charging ? "⚡" : "",
            FontSize = 13,
            Height = LineHeight,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(stateColor),
        };
        Grid.SetColumn(chargeMark, 3);
        line.Children.Add(chargeMark);

        var percentText = new TextBlock
        {
            Text = percent.HasValue ? $"{percent}%" : "--",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(stateColor),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(percentText, 4);
        line.Children.Add(percentText);
        row.Children.Add(line);

        // 电量条用自绘（Grid 轨道 + 定宽填充），ProgressBar 模板在这个高度下不渲染填充。
        // 轨道色不用 Resources 查找：DispatcherQueue 回调里的异常不走 UnhandledException，会直接 fail-fast
        var track = new Grid
        {
            Height = BarHeight,
            CornerRadius = new CornerRadius(BarHeight / 2),
            Background = new SolidColorBrush(skin.Track),
        };
        track.Children.Add(new Border
        {
            Width = width * Math.Clamp(percent ?? 0, 0, 100) / 100.0,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(BarHeight / 2),
            Background = new SolidColorBrush(stateColor),
        });
        row.Children.Add(track);
        return row;
    }

    /// <summary>
    /// 来源徽标：彩色胶囊（文字=来源色，底色=同色压淡），与设备列表页 <c>DevicesPage</c> 的徽标同款。
    /// 颜色走 <see cref="UiPalette.SourceTag"/> 固定值、不走 ThemeResource——
    /// 本方法在 DispatcherQueue 回调路径上跑，查 Resources 抛异常会直接 fail-fast（同 <see cref="Skin"/> 的理由）。
    /// </summary>
    private static FrameworkElement BuildSourceTag(DeviceSource source)
    {
        var color = UiPalette.SourceTag(source);
        return new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(TagPadH, 1.5, TagPadH, 1.5),
            Background = new SolidColorBrush(UiPalette.Tint(color)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = DeviceSourceInfo.ShortLabel(source),
                FontSize = TagFontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
            },
        };
    }

    /// <summary>
    /// 小组件配色。全部硬编码、按主题分两套：ResizeContent 路径由 DispatcherQueue 回调驱动，
    /// 那里查 Resources 抛异常会直接 fail-fast，所以状态色一律不走 ThemeResource。
    /// </summary>
    private readonly record struct Skin(
        Color Track,
        Color IconFill,
        Color IconStroke,
        Color Text);

    private static Skin SkinFor(ElementTheme theme) => theme == ElementTheme.Dark
        ? new Skin(
            Track: Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
            IconFill: Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
            IconStroke: Color.FromArgb(0xFF, 0xD2, 0xD2, 0xD2),
            Text: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF))
        : new Skin(
            Track: Color.FromArgb(0x16, 0x00, 0x00, 0x00),
            IconFill: Color.FromArgb(0x0F, 0x00, 0x00, 0x00),
            IconStroke: Color.FromArgb(0xFF, 0x5F, 0x5E, 0x5A),
            Text: Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F));

    /// <summary>右键菜单：与托盘同一条 Win32 原生菜单路径（不依赖窗口激活，点别处即关，见 runtime-pitfalls §18）。</summary>
    private void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        var pos = Native.GetCursorPos();
        AppPopupMenu.Show(WinRT.Interop.WindowNative.GetWindowHandle(this), pos.X, pos.Y);
    }

    // ---------- 显示模式 ----------

    /// <summary>
    /// 设置保存后的即时生效入口：重新应用层级 + 重算窗口高度（最多显示条数可能变了）。
    /// 由设置页在已存在小组件时调用。
    /// </summary>
    public void RefreshFromSettings()
    {
        var s = App.Services.Settings;
        ApplyDisplayMode(s.WidgetDisplayMode);
        RefreshContent();
    }

    /// <summary>
    /// 应用窗口层级。可重复调用，每次先复位再设，避免从"置顶"切到"桌面"时残留 TOPMOST 标记。
    /// Topmost/Normal 走 `OverlappedPresenter.IsAlwaysOnTop`；Desktop 同样不置顶，再用
    /// `SetWindowPos(HWND_BOTTOM)` 压到 Z 序最底。
    ///
    /// ⚠ Desktop 档**不能**用 `IsAlwaysOnTop` 的反面做：它只认识"置顶/非置顶"，表达不了"沉到最底"。
    /// </summary>
    public void ApplyDisplayMode(string? mode)
    {
        var resolved = WidgetDisplayMode.Normalize(mode);
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) return;

            if (AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.IsAlwaysOnTop = resolved == WidgetDisplayMode.Topmost;

            if (resolved == WidgetDisplayMode.Desktop)
            {
                Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            else
            {
                // 从桌面档切回来时要显式脱离 bottom：HWND_TOP + NOACTIVATE 把它抬回普通层，
                // 既不抢焦点，也不会在 Topmost 档下和 IsAlwaysOnTop 打架。
                Native.SetWindowPos(hwnd, Native.HWND_TOP, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"小组件显示模式应用失败({resolved}): {ex.Message}");
        }
    }

    // ---------- 拖动 ----------

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 仅左键拖动；右键走 Root_RightTapped（Win32 原生菜单）
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        Root.CapturePointer(e.Pointer);
        var pos = Native.GetCursorPos();
        _dragOffsetX = pos.X - AppWindow.Position.X;
        _dragOffsetY = pos.Y - AppWindow.Position.Y;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var pos = Native.GetCursorPos();
        AppWindow.Move(new PointInt32(pos.X - _dragOffsetX, pos.Y - _dragOffsetY));
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Root.ReleasePointerCapture(e.Pointer);

        ClampToWorkArea();
        // 拖到另一块显示器时 DPI 会变，圆角半径的物理像素值要跟着重算
        ApplyRoundedCorner();
        var s = App.Services.Settings;
        s.WidgetX = AppWindow.Position.X;
        s.WidgetY = AppWindow.Position.Y;
        s.Save();
    }
}
