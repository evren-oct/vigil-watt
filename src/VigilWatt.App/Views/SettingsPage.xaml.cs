using System.Diagnostics;
using System.Linq;
using VigilWatt.App.Services;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Utils;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.UI;

namespace VigilWatt.App.Views;
/// <summary>
/// 功 能： 设置页。
/// 2026/10/06 15:53:36 xliu
/// </summary>

public sealed partial class SettingsPage : Page
{
    private readonly Dictionary<string, ToggleSwitch> _pluginToggles = new();

    /// <summary>屏幕下拉框每项对应的友好名（身份 → 名字）：保存时要把名字一并记下，屏没插时才显示得出来。</summary>
    private readonly Dictionary<string, string> _screenLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SearchEntry> _searchEntries = new();
    private bool _searchIndexBuilt;
    private DispatcherQueueTimer? _highlightTimer;
    private TextBlock? _highlightedTitle;
    private Brush? _previousTitleBrush;
    private Windows.UI.Text.FontWeight _previousTitleWeight;
    private SolidColorBrush? _titleHighlightBrush;

    public SettingsPage()
    {
        InitializeComponent();
        // 页面被缓存复用：构造函数只跑一次，所以"建一次"的活留这里，"每次进来都要重读"的活放 Loaded
        NavigationCacheMode = NavigationCacheMode.Required;
        BuildOptions();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        SyncFromSettings();
        // 搜索索引要递归遍历整棵视觉树：放到布局完成后的低优先级队列里建，别占页面首次显示的时间
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, EnsureSearchIndex);
    }

    /// <summary>只建一次：下拉框档位与插件行。缓存页复用同一实例，再建一遍会叠加出重复条目。</summary>
    private void BuildOptions()
    {
        // 档位按统一范围生成，保证下拉框和取值收敛永远同源
        foreach (var n in WidgetMaxDevicesOptions.Choices)
            WidgetMaxCombo.Items.Add(new ComboBoxItem { Content = $"{n} 个", Tag = n });
        foreach (var n in TaskbarBatteryOptions.Choices)
            TaskbarMaxCombo.Items.Add(new ComboBoxItem { Content = $"{n} 个", Tag = n });
        foreach (var (value, label) in LogLevelOptions.Choices)
            LogLevelCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });

        // 插件开关动态生成：新插件（IBatteryPlugin 实现）自动出现在设置页。
        // 按 Name 排序而不是反射顺序：GetTypes() 的次序随编译/重构漂移，设置页条目会莫名其妙换位置。
        // 同一个 Name 只出一行（AGENTS.md §9）：一个品牌的多套协议可以是多个插件类，但开关只有一个。
        bool first = true;
        foreach (var plugin in App.Services.Manager.Plugins
                     .OrderBy(p => p.Name, StringComparer.Ordinal)
                     .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            AddPluginRow(plugin, first);
            first = false;
        }
    }

    /// <summary>每次进入页面都重读：设置可能被别处改过（例如插件开关在保存时落盘）。</summary>
    private void SyncFromSettings()
    {
        var s = App.Services.Settings;
        ThresholdBox.Value = s.LowBatteryThreshold;
        RemainingTimeEstimateToggle.IsOn = s.RemainingTimeEstimateEnabled;
        SampleBox.Value = s.SampleIntervalMinutes;
        RetentionBox.Value = s.HistoryRetentionDays;
        StartMinimizedToggle.IsOn = s.StartMinimized;
        AutostartToggle.IsOn = AutostartService.IsEnabled();
        WidgetToggle.IsOn = s.WidgetEnabled;
        SystemBatteryToggle.IsOn = s.MonitorSystemBattery;

        // 小组件显示方式：与"窗口背景"同一套路，下拉框存偏好、副标题写实际行为
        var mode = WidgetDisplayMode.Normalize(s.WidgetDisplayMode);
        SelectByTag(WidgetModeCombo, mode);
        WidgetModeHintText.Text = WidgetDisplayMode.Describe(mode);
        SelectByTag(WidgetMaxCombo, WidgetMaxDevicesOptions.Clamp(s.WidgetMaxDevices));

        // 任务栏贴片：与小组件同一套路（下拉框存偏好、副标题写行为），取值收敛也只有一处定义
        TaskbarToggle.IsOn = s.TaskbarBatteryEnabled;
        TaskbarLockToggle.IsOn = s.TaskbarBatteryPositionLocked;
        var taskbarMode = TaskbarBatteryMode.Normalize(s.TaskbarBatteryMode);
        SelectByTag(TaskbarModeCombo, taskbarMode);
        TaskbarModeHintText.Text = TaskbarBatteryMode.Describe(taskbarMode);
        SelectByTag(TaskbarMaxCombo, TaskbarBatteryOptions.Clamp(s.TaskbarBatteryMaxDevices));
        // 屏幕随时可能插拔：每次进页面重新枚举，选中项按稳定身份还原
        SyncTaskbarScreenCombo(s.TaskbarBatteryScreen, s.TaskbarBatteryScreenLabel);

        // 窗口背景：下拉框选中"用户偏好"，副标题说"这台机器上实际会生效什么"——
        // 两者不是一回事（Win10 上选 Mica 会静默降级成 Acrylic），分开显示才不会让人困惑。
        var pref = WindowBackdrop.Normalize(s.WindowBackdrop);
        SelectByTag(BackdropCombo, pref);
        UpdateBackdropHint(pref);

        // 日志级别：副标题说明当前档位含义。注意与"窗口背景"不同，这里是重启生效——副标题已写明。
        var level = LogLevelNames.Normalize(s.LogLevel);
        SelectByTag(LogLevelCombo, level);
        LogLevelHintText.Text = LogLevelOptions.Describe(level);
        LogRetentionBox.Value = s.LogRetentionDays;

        foreach (var (name, toggle) in _pluginToggles)
            toggle.IsOn = s.PluginEnabled.TryGetValue(name, out var on) && on;
    }

    /// <summary>按 Tag 选中下拉框条目；值对不上任何一条（配置被手改过）时保持未选中。</summary>
    private static void SelectByTag(ComboBox combo, object? tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (Equals(item.Tag, tag))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void Backdrop_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BackdropCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        UpdateBackdropHint(tag);
    }

    private void WidgetMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (WidgetModeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        WidgetModeHintText.Text = WidgetDisplayMode.Describe(tag);
    }

    private void LogLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LogLevelCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        LogLevelHintText.Text = LogLevelOptions.Describe(tag);
    }

    private void TaskbarMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TaskbarModeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        TaskbarModeHintText.Text = TaskbarBatteryMode.Describe(tag);
    }

    private void TaskbarReset_Click(object sender, RoutedEventArgs e)
    {
        App.Services.TaskbarBattery?.ResetPosition();
    }

    /// <summary>重建屏幕下拉框并选中配置项。<paramref name="configuredId"/> 是稳定身份，不是 \\.\DISPLAYn。</summary>
    private void SyncTaskbarScreenCombo(string? configuredId, string? configuredLabel)
    {
        TaskbarScreenCombo.Items.Clear();
        _screenLabels.Clear();
        var screens = MonitorCatalog.AllScreens();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var screen in screens)
        {
            // 名称 + 分辨率：两块同型号显示器只靠名字分不开
            string baseLabel = MonitorSelection.Describe(screen);
            string label = baseLabel;
            for (int n = 2; !used.Add(label); n++) label = $"{baseLabel}（{n}）";
            TaskbarScreenCombo.Items.Add(new ComboBoxItem { Content = label, Tag = screen.Id });
            _screenLabels[screen.Id] = screen.Label;
        }

        // 只有一块屏时"跟随主显示器"和它本身同义，不占位
        if (screens.Count > 1)
            TaskbarScreenCombo.Items.Add(new ComboBoxItem { Content = "跟随主显示器", Tag = MonitorSelection.PrimaryToken });

        string wanted = string.IsNullOrEmpty(configuredId) ? MonitorSelection.PrimaryToken : configuredId;
        if (wanted.Equals(MonitorSelection.PrimaryToken, StringComparison.OrdinalIgnoreCase) && screens.Count == 1)
            wanted = screens[0].Id;

        foreach (ComboBoxItem item in TaskbarScreenCombo.Items)
        {
            if (item.Tag is string tag && tag.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                TaskbarScreenCombo.SelectedItem = item;
                return;
            }
        }

        // 配置的屏当前不在：仍列出它并标"未连接"，名字用上次记下的——保存时也不会被静默改回主屏
        string name = configuredLabel is { Length: > 0 } ? configuredLabel : NameOfMissingScreen(wanted);
        var offline = new ComboBoxItem { Content = $"{name}（未连接）", Tag = wanted };
        TaskbarScreenCombo.Items.Add(offline);
        _screenLabels[wanted] = name;
        TaskbarScreenCombo.SelectedItem = offline;
    }

    /// <summary>配置的屏没插时的名字：先按设备路径查（未激活的屏也能查到），再退回 GDI 序号文案。</summary>
    private static string NameOfMissingScreen(string id)
    {
        string? name = MonitorCatalog.FriendlyNameOf(id);
        if (!string.IsNullOrEmpty(name)) return name;
        return id.StartsWith(@"\\.\", StringComparison.Ordinal) ? MonitorSelection.FallbackLabel(id) : "未知显示器";
    }

    private void TaskbarScreenRefresh_Click(object sender, RoutedEventArgs e)
    {
        // 保持当前选择：还活着就用它的新名字，不在了就沿用配置里记下的名字
        string? tag = (TaskbarScreenCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var hit = tag == null ? null : MonitorSelection.Match(MonitorCatalog.AllScreens(), tag);
        SyncTaskbarScreenCombo(tag, hit?.Label ?? App.Services.Settings.TaskbarBatteryScreenLabel);
    }

    /// <summary>副标题按"选中的偏好在本机上实际会生效成什么"来写。</summary>
    private void UpdateBackdropHint(string preference)
    {
        var mica = WindowBackdrop.IsMicaSupported;
        var resolved = WindowBackdrop.Resolve(preference, mica, WindowBackdrop.IsAcrylicSupported);
        BackdropHintText.Text = WindowBackdrop.Describe(resolved, mica);
    }

    /// <summary>
    /// 按"标签左、开关右"的统一样式动态生成一行插件开关。复用 Page.Resources 里的
    /// RowTitleStyle/RowDescStyle/RowDividerStyle，让动态行与 XAML 手写行视觉完全一致。
    /// </summary>
    private void AddPluginRow(IBatteryPlugin plugin, bool first)
    {
        if (!first)
            PluginPanel.Children.Add(new Border { Style = (Style)Resources["RowDividerStyle"] });

        var toggle = new ToggleSwitch
        {
            OnContent = "开",
            OffContent = "关",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = plugin.DisplayName, Style = (Style)Resources["RowTitleStyle"] });
        text.Children.Add(new TextBlock { Text = plugin.DisplayNameEn, Style = (Style)Resources["RowDescStyle"] });

        var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 12, 0, 12) };
        // 中英两名都进无障碍名：用户按哪种写法搜都命中（"北通 BETOP" / "迈从 MCHOSE"）。
        AutomationProperties.SetName(row, $"{plugin.DisplayName} {plugin.DisplayNameEn}");
        AutomationProperties.SetHelpText(row, "2.4G 设备插件");
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(text);
        row.Children.Add(toggle);

        PluginPanel.Children.Add(row);
        _pluginToggles[plugin.Name] = toggle;
    }

    /// <summary>搜索索引延迟到真正需要时才建（OnPageLoaded 会先在空闲时预热一次，这里兜底）。</summary>
    private void EnsureSearchIndex()
    {
        if (_searchIndexBuilt) return;
        _searchIndexBuilt = true;
        _searchEntries.Clear();
        AddSearchEntries(SettingsContent);
    }

    private void AddSearchEntries(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Grid row && GetRowText(row) is { } text)
            {
                var aliases = AutomationProperties.GetName(row);
                var help = AutomationProperties.GetHelpText(row);
                if (string.IsNullOrWhiteSpace(help)) help = text.Description;
                var category = FindCategory(row);
                _searchEntries.Add(new SearchEntry(row, text.Title, help, category, aliases, text.TitleBlock));
            }

            AddSearchEntries(child);
        }
    }

    private static (string Title, string Description, TextBlock TitleBlock)? GetRowText(Grid row)
    {
        var textPanel = row.Children.OfType<StackPanel>().FirstOrDefault();
        if (textPanel is null) return null;
        var blocks = new List<TextBlock>();
        CollectTextBlocks(textPanel, blocks);
        return blocks.Count == 0 ? null : (blocks[0].Text, blocks.Count > 1 ? blocks[1].Text : string.Empty, blocks[0]);
    }

    private static void CollectTextBlocks(DependencyObject parent, List<TextBlock> blocks)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock text) blocks.Add(text);
            else CollectTextBlocks(child, blocks);
        }
    }

    private static string FindCategory(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not StackPanel section || section.Children.FirstOrDefault() is not StackPanel header) continue;
            var title = header.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(title)) return title;
        }

        return string.Empty;
    }

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        EnsureSearchIndex();
        SearchResultsPanel.Children.Clear();
        var query = SettingsSearchBox.Text.Trim();
        SearchEmptyText.Visibility = Visibility.Collapsed;
        if (query.Length == 0)
        {
            SearchResultsBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var matches = _searchEntries
            .Where(entry => entry.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            SearchResultsBorder.Visibility = Visibility.Collapsed;
            SearchEmptyText.Visibility = Visibility.Visible;
            return;
        }

        foreach (var entry in matches)
        {
            var content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
            content.Children.Add(new TextBlock { Text = entry.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", new[] { entry.Category, entry.Description }.Where(part => !string.IsNullOrWhiteSpace(part))),
                FontSize = 12,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
            });
            var button = new Button
            {
                Content = content,
                Tag = entry,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 12, 8),
            };
            button.Click += SearchResult_Click;
            SearchResultsPanel.Children.Add(button);
        }

        SearchResultsBorder.Visibility = Visibility.Visible;
    }

    private void SearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SearchEntry entry }) return;
        SearchResultsBorder.Visibility = Visibility.Collapsed;
        SearchEmptyText.Visibility = Visibility.Collapsed;
        SettingsContent.UpdateLayout();
        var y = entry.Target.TransformToVisual(SettingsContent).TransformPoint(new Point(0, 0)).Y;
        var topMargin = Math.Min(96, SettingsScrollViewer.ViewportHeight * 0.25);
        SettingsScrollViewer.ChangeView(null, Math.Max(0, SettingsScrollViewer.VerticalOffset + y - topMargin), null, false);

        ClearHighlight();
        var accent = Application.Current.Resources.TryGetValue("SystemAccentColor", out var value) && value is Color color
            ? color
            : Color.FromArgb(255, 0, 120, 215);
        _highlightedTitle = entry.TitleBlock;
        _previousTitleBrush = entry.TitleBlock.Foreground;
        _previousTitleWeight = entry.TitleBlock.FontWeight;
        _titleHighlightBrush = new SolidColorBrush(accent);
        entry.TitleBlock.Foreground = _titleHighlightBrush;
        entry.TitleBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _highlightTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _highlightTimer.Stop();
        _highlightTimer.Interval = TimeSpan.FromMilliseconds(950);
        _highlightTimer.Tick -= HighlightTimer_Tick;
        _highlightTimer.Tick += HighlightTimer_Tick;
        _highlightTimer.Start();
    }

    private void BackToSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchResultsBorder.Visibility = Visibility.Collapsed;
        SearchEmptyText.Visibility = Visibility.Collapsed;
        SettingsScrollViewer.ChangeView(null, 0, null, false);
        SettingsSearchBox.Focus(FocusState.Programmatic);
        SettingsSearchBox.SelectionStart = SettingsSearchBox.Text.Length;
    }

    private void SettingsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateBackToSearchVisibility();
    }

    private void SettingsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBackToSearchVisibility();
    }

    private void UpdateBackToSearchVisibility()
    {
        BackToSearchButton.Visibility = SettingsScrollViewer.ViewportHeight > 0
            && SettingsScrollViewer.VerticalOffset >= SettingsScrollViewer.ViewportHeight
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HighlightTimer_Tick(object? sender, object e)
    {
        _highlightTimer?.Stop();
        if (_highlightedTitle is null || _titleHighlightBrush is null) return;

        var brush = _titleHighlightBrush;
        var fade = new Storyboard();
        var animation = new ColorAnimation
        {
            To = GetBrushColor(_previousTitleBrush) ?? GetPrimaryTextColor(),
            Duration = new Duration(TimeSpan.FromMilliseconds(450)),
        };
        Storyboard.SetTarget(animation, brush);
        Storyboard.SetTargetProperty(animation, "Color");
        fade.Children.Add(animation);
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(_titleHighlightBrush, brush)) ClearHighlight();
        };
        fade.Begin();
    }

    private void ClearHighlight()
    {
        _highlightTimer?.Stop();
        if (_highlightedTitle is not null)
        {
            _highlightedTitle.Foreground = _previousTitleBrush;
            _highlightedTitle.FontWeight = _previousTitleWeight;
        }
        _highlightedTitle = null;
        _previousTitleBrush = null;
        _titleHighlightBrush = null;
    }

    private static Color? GetBrushColor(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : null;

    private static Color GetPrimaryTextColor() =>
        Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var brush)
        && brush is SolidColorBrush solid
            ? solid.Color
            : Color.FromArgb(255, 32, 32, 32);

    private sealed record SearchEntry(Grid Target, string Title, string Description, string Category, string Aliases, TextBlock TitleBlock)
    {
        public string SearchText => $"{Title} {Description} {Category} {Aliases}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Services.Settings;
        s.LowBatteryThreshold = (int)ThresholdBox.Value;
        s.RemainingTimeEstimateEnabled = RemainingTimeEstimateToggle.IsOn;
        s.SampleIntervalMinutes = (int)SampleBox.Value;
        s.HistoryRetentionDays = (int)RetentionBox.Value;
        s.StartMinimized = StartMinimizedToggle.IsOn;
        s.Autostart = AutostartToggle.IsOn;
        s.WidgetEnabled = WidgetToggle.IsOn;
        s.MonitorSystemBattery = SystemBatteryToggle.IsOn;
        s.WidgetDisplayMode = WidgetModeCombo.SelectedItem is ComboBoxItem modeItem && modeItem.Tag is string modeTag
            ? WidgetDisplayMode.Normalize(modeTag)
            : WidgetDisplayMode.Normal;
        s.WidgetMaxDevices = WidgetMaxCombo.SelectedItem is ComboBoxItem maxItem && maxItem.Tag is int maxValue
            ? WidgetMaxDevicesOptions.Clamp(maxValue)
            : WidgetMaxDevicesOptions.Default;
        s.TaskbarBatteryEnabled = TaskbarToggle.IsOn;
        s.TaskbarBatteryPositionLocked = TaskbarLockToggle.IsOn;
        s.TaskbarBatteryMode = TaskbarBatteryMode.Normalize(
            (TaskbarModeCombo.SelectedItem as ComboBoxItem)?.Tag as string);
        s.TaskbarBatteryMaxDevices = TaskbarMaxCombo.SelectedItem is ComboBoxItem taskbarMaxItem
                                     && taskbarMaxItem.Tag is int taskbarMaxValue
            ? TaskbarBatteryOptions.Clamp(taskbarMaxValue)
            : TaskbarBatteryOptions.Default;
        // 身份与名字一起存：屏没插时设置页还要显示它叫什么叫
        if (TaskbarScreenCombo.SelectedItem is ComboBoxItem screenItem && screenItem.Tag is string screenTag)
        {
            s.TaskbarBatteryScreen = screenTag;
            if (_screenLabels.TryGetValue(screenTag, out var screenLabel)) s.TaskbarBatteryScreenLabel = screenLabel;
        }
        else
        {
            s.TaskbarBatteryScreen = MonitorSelection.PrimaryToken;
        }
        s.WindowBackdrop = (BackdropCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? WindowBackdrop.Auto;
        s.LogLevel = LogLevelNames.Normalize((LogLevelCombo.SelectedItem as ComboBoxItem)?.Tag as string);
        s.LogRetentionDays = (int)LogRetentionBox.Value;
        foreach (var (name, toggle) in _pluginToggles)
        {
            s.PluginEnabled[name] = toggle.IsOn;
        }
        // 保存失败必须让用户看见：以前这里把返回值丢掉，界面毫无反馈，用户以为"点了没反应"。
        // 写不进去时（目录不可写 / 磁盘满 / 安全软件拦截）继续往下走只会让内存与磁盘不一致，直接停。
        if (!s.Save())
        {
            Native.MessageBoxW(
                IntPtr.Zero,
                $"设置保存失败，无法写入：\r\n{AppPaths.RootDir}\\settings.json\r\n\r\n" +
                $"错误：{AppSettings.LastSaveError}\r\n\r\n" +
                "常见原因：目录不可写、磁盘空间不足、或被安全软件拦截。\r\n本次修改不会保留。",
                "VigilWatt 保存失败",
                0x00000010 /* MB_ICONERROR */);
            return;
        }

        // 自启写失败不拦其他设置（注册表被拒 ≠ 设置无效），但必须说出来：否则用户以为设上了，
        // 下次开机才发现没起来。异常在这里就地收口，不能再逸出到熔断器（dev/runtime-pitfalls.md §20）。
        var autostartError = AutostartService.TrySetEnabled(AutostartToggle.IsOn, out var autostartReason)
            ? null
            : autostartReason;
        App.Services.Manager.ApplySettings();

        // 小组件开关即时生效
        if (s.WidgetEnabled && App.Services.Widget == null)
        {
            App.Services.Widget = new WidgetWindow();
            App.Services.Widget.Activate();
        }
        else if (!s.WidgetEnabled && App.Services.Widget != null)
        {
            App.Services.Widget.Close();
            App.Services.Widget = null;
        }
        else if (s.WidgetEnabled)
        {
            // 已开着的小组件：先把配置落到位再重画，让"显示方式/最多显示设备数"当场生效，
            // 不必关掉再开。RefreshFromSettings 内部会重新应用层级并重算窗口高度。
            App.Services.Widget?.RefreshFromSettings();
        }

        // 任务栏贴片开关即时生效（与小组件同一条路）
        if (s.TaskbarBatteryEnabled && App.Services.TaskbarBattery == null)
        {
            App.Services.TaskbarBattery = new TaskbarPillManager();
        }
        else if (!s.TaskbarBatteryEnabled && App.Services.TaskbarBattery != null)
        {
            App.Services.TaskbarBattery.Dispose();
            App.Services.TaskbarBattery = null;
        }
        else if (s.TaskbarBatteryEnabled)
        {
            App.Services.TaskbarBattery?.RefreshFromSettings();
        }

        // 窗口背景即时生效：改完就想看到效果，不该逼用户重启应用
        App.Services.MainWindow?.ApplyBackdrop();

        // 历史清理立即执行一次
        App.Services.Manager.CleanupHistoryNow();

        // 日志级别不热更新（重启生效），保存后给一句提示，避免用户以为"点了没反应"
        var feedback = autostartError == null
            ? s.LogLevel == LogLevelNames.Default ? "已保存" : "已保存（日志级别重启后生效）"
            : "已保存（开机自启未生效）";
        ShowSavedFeedback(feedback);

        if (autostartError != null)
        {
            Native.MessageBoxW(
                IntPtr.Zero,
                "其他设置已保存，但开机自启没能写入注册表，本次不会生效。\r\n\r\n" +
                $"错误：{autostartError}\r\n\r\n" +
                "常见原因：进程跑在受限权限下（沙箱 / 安全软件 / 非当前用户配置），" +
                "此时 HKEY_CURRENT_USER 的 Run 键是只读的。\r\n" +
                "可以从资源管理器直接双击 VigilWatt.App.exe 再试一次。",
                "VigilWatt 开机自启未生效",
                0x00000030 /* MB_ICONWARNING */);
        }
    }

    private void OpenLogDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(AppPaths.RootDir, "logs");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("打开日志目录失败: " + ex.Message);
        }
    }

    private async void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清空日志？",
            Content = "将删除 logs 目录下的全部按日日志（app-日期.log）与 log-error.txt，且不可恢复。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            // 先释放日志句柄，否则删不掉；之后的日志会重新开一个新文件
            Log.Shutdown();
            var dir = Path.Combine(AppPaths.RootDir, "logs");
            if (Directory.Exists(dir))
            {
                // app*.log 同时覆盖按日日志 app-日期.log 与旧版 app.log / app.N.log
                foreach (var file in Directory.EnumerateFiles(dir, "app*.log"))
                    File.Delete(file);
                var err = Path.Combine(dir, "log-error.txt");
                if (File.Exists(err)) File.Delete(err);
            }
            Log.Info("日志已由设置页清空");
            ShowSavedFeedback("日志已清空");
        }
        catch (Exception ex)
        {
            Log.Warn("清空日志失败: " + ex.Message);
            ShowSavedFeedback("清空日志失败：" + ex.Message);
        }
    }

    /// <summary>底部保存栏的即时反馈：显示一句话，2 秒后自动隐去。</summary>
    private void ShowSavedFeedback(string text)
    {
        SavedIcon.Visibility = Visibility.Visible;
        SavedText.Text = text;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) =>
        {
            SavedText.Text = "";
            SavedIcon.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }
}
