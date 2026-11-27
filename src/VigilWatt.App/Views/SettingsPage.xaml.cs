using System.Diagnostics;
using System.Linq;
using VigilWatt.App.Controls;
using VigilWatt.App.Localization;
using VigilWatt.App.Services;
using VigilWatt.Core.Localization;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Utils;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
    /// <summary>屏幕下拉框每项对应的友好名（身份 → 名字）：保存时要把名字一并记下，屏没插时才显示得出来。</summary>
    private readonly Dictionary<string, string> _screenLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SearchEntry> _searchEntries = new();
    /// <summary>弹窗闸：同一时刻只允许一个 ContentDialog，否则 ShowAsync 抛异常、async void 里逃出去会崩进程。</summary>
    private bool _dialogOpen;
    private bool _syncingLanguage;
    private DispatcherQueueTimer? _highlightTimer;
    private TextBlock? _highlightedTitle;
    private Brush? _previousTitleBrush;
    private Windows.UI.Text.FontWeight _previousTitleWeight;
    private SolidColorBrush? _titleHighlightBrush;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = Strings.Current;
        // 懒缓存：首次进来构造一次、之后复用；启动期不再预热（预热一份 231 元素 + 19 个下拉框要 ~27MB 提交）
        NavigationCacheMode = NavigationCacheMode.Required;
        BuildOptions();
        Loaded += OnPageLoaded;
        Unloaded += (_, _) => _highlightTimer?.Stop();
    }

    /// <summary>每次进页面都重读设置。搜索索引改为按需重建：折叠区展开后视觉树会变，缓存会过期。</summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SyncFromSettings();

    /// <summary>只建一次：下拉框档位。缓存页复用同一实例，再建一遍会叠加出重复条目。</summary>
    private void BuildOptions()
    {
        // 档位按统一范围生成，保证下拉框和取值收敛永远同源
        foreach (var n in WidgetMaxDevicesOptions.Choices)
            WidgetMaxCombo.Items.Add(new ComboBoxItem { Content = Strings.Current.Format("S_aedfa7a813", n), Tag = n });
        foreach (var n in TaskbarBatteryOptions.Choices)
            TaskbarMaxCombo.Items.Add(new ComboBoxItem { Content = Strings.Current.Format("S_aedfa7a813", n), Tag = n });
        foreach (var (value, label) in LogLevelOptions.Choices)
            LogLevelCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
    }

    /// <summary>
    /// 品牌开关与设备勾选都改成弹窗里的临时控件，页面不再持有它们，所以插件行只在弹窗打开时构建。
    /// 按 Name 排序而不是反射顺序：GetTypes() 的次序随编译/重构漂移，条目会莫名其妙换位置。
    /// 同一个 Name 只出一行（AGENTS.md §9）：一个品牌的多套协议可以是多个插件类，但开关只有一个。
    /// </summary>
    private static IEnumerable<IBatteryPlugin> OrderedPlugins() =>
        App.Services.Manager.Plugins
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>每次进入页面都重读：设置可能被别处改过（例如插件开关在保存时落盘）。</summary>
    private void SyncFromSettings()
    {
        var s = App.Services.Settings;
        _syncingLanguage = true;
        try { SelectByTag(LanguageCombo, string.IsNullOrEmpty(s.Language) ? "auto" : s.Language); }
        finally { _syncingLanguage = false; }
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
        var taskbarMode = TaskbarBatteryMode.Normalize(s.TaskbarBatteryMode);
        SelectByTag(TaskbarModeCombo, taskbarMode);
        TaskbarModeHintText.Text = TaskbarBatteryMode.Describe(taskbarMode);
        SelectByTag(TaskbarMaxCombo, TaskbarBatteryOptions.Clamp(s.TaskbarBatteryMaxDevices));
        UpdateTaskbarDeviceSummary();
        // 屏幕随时可能插拔：每次进页面重新枚举，选中项按稳定身份还原
        SyncTaskbarScreenCombo(s.TaskbarBatteryScreen, s.TaskbarBatteryScreenLabel);
        TaskbarLockToggle.IsOn = s.TaskbarBatteryLockPosition;

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

        UpdatePluginSummary();
        UpdateGroupSummaries();
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

    /// <summary>
    /// 当前可选的设备展示名。名字按 OrdinalIgnoreCase 去重——同一只设备无线/有线两个 Id 会
    /// 在展示层合成一行（见 DeviceManager.DedupeForDisplay），这里再按名字去一次重。
    /// 口径必须与贴片一致：贴片用 DevicePresentation.Apply 之后的展示名（用户可自定义），否则勾了也匹配不上。
    /// </summary>
    private static List<string> CollectTaskbarDeviceNames() =>
        App.Services.Manager.Devices
            .Select(d => DevicePresentation.Apply(d, App.Services.Settings).DisplayName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>把"一个都不勾 = 显示全部"这条负向规则写成当前状态，省得用户去反推。</summary>
    private void UpdateTaskbarDeviceSummary()
    {
        var names = CollectTaskbarDeviceNames();
        TaskbarDeviceMoreButton.IsEnabled = names.Count > 0;
        // 设备胶囊在弹窗里，搜索索引抓不到；把设备名挂到行上，搜设备名仍能定位到这一行
        AutomationProperties.SetName(TaskbarDeviceRowRoot, string.Join(" ", names));

        if (names.Count == 0)
        {
            TaskbarDeviceSummaryText.Text = Strings.Current.Get("S_ec8aab679a");
            return;
        }

        // 配置里记的名字可能指向已经拔掉的设备：只报台数，不逐个列名字，免得摘要里一堆查无此机
        int selected = App.Services.Settings.TaskbarBatterySelectedDevices?.Count ?? 0;
        TaskbarDeviceSummaryText.Text = selected == 0
            ? Strings.Current.Format("S_0161d1b30a", names.Count)
            : Strings.Current.Format("S_47a40bc22e", selected);
    }

    /// <summary>
    /// 设备勾选弹窗。确定即落盘并刷新贴片：勾选结果在设置页里看不见摸不着，不立刻应用用户没法确认效果。
    /// 设置页其他项仍走底部「保存设置」，两条路最终都进 <see cref="TrySaveSettings"/>。
    /// </summary>
    private async void TaskbarDeviceMore_Click(object sender, RoutedEventArgs e)
    {
        // 与插件弹窗共用同一个闸：两个弹窗同时开着同样会抛异常
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            await ShowTaskbarDeviceDialogAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("打开选择显示设备弹窗失败: " + ex.Message);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async Task ShowTaskbarDeviceDialogAsync()
    {
        var names = CollectTaskbarDeviceNames();
        if (names.Count == 0) return;

        var selected = new HashSet<string>(
            App.Services.Settings.TaskbarBatterySelectedDevices ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        var chips = new List<ToggleButton>();
        var panel = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        foreach (var name in names)
        {
            var chip = new ToggleButton
            {
                // 名字放 Tag：Content 换成 TextBlock 后没法再从 Content 取回原字符串（保存要用）
                Tag = name,
                Content = new TextBlock { Text = name, FontSize = 12 },
                IsChecked = selected.Contains(name),
                MinWidth = 0,
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(14),
            };
            chips.Add(chip);
            panel.Children.Add(chip);
        }

        var countText = new TextBlock { Style = (Style)Resources["RowDescStyle"] };
        void RefreshCount()
        {
            int on = chips.Count(c => c.IsChecked == true);
            countText.Text = on == 0
                ? Strings.Current.Format("S_27bafa2fdb", names.Count)
                : Strings.Current.Format("S_a34a8a5c27", on, names.Count);
        }

        foreach (var chip in chips)
        {
            chip.Checked += (_, _) => RefreshCount();
            chip.Unchecked += (_, _) => RefreshCount();
        }
        RefreshCount();

        var selectAll = new HyperlinkButton { Content = Strings.Current.Get("S_3e44b2a933"), Padding = new Thickness(8, 4, 8, 4) };
        var selectNone = new HyperlinkButton { Content = Strings.Current.Get("S_84fcd70d42"), Padding = new Thickness(8, 4, 8, 4) };
        selectAll.Click += (_, _) => { foreach (var c in chips) c.IsChecked = true; };
        selectNone.Click += (_, _) => { foreach (var c in chips) c.IsChecked = false; };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        actions.Children.Add(selectAll);
        actions.Children.Add(selectNone);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            Text = Strings.Current.Get("S_7bc446baa9"),
            Style = (Style)Resources["RowDescStyle"],
        });
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        });
        body.Children.Add(actions);
        body.Children.Add(countText);

        var dialog = new ContentDialog
        {
            Title = Strings.Current.Get("S_e8d9a738c8"),
            Content = body,
            PrimaryButtonText = Strings.Current.Get("S_f526c89937"),
            CloseButtonText = Strings.Current.Get("S_4d0b4688c7"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        App.Services.Settings.TaskbarBatterySelectedDevices = chips
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag)
            .ToList();
        if (!TrySaveSettings()) return;

        App.Services.TaskbarBattery?.RefreshFromSettings();
        UpdateTaskbarDeviceSummary();
    }

    /// <summary>落盘并把失败原因说出来。弹窗的"确定即生效"和底部「保存设置」共用同一条路径。</summary>
    private bool TrySaveSettings()
    {
        if (App.Services.Settings.Save()) return true;

        Native.MessageBoxW(
            IntPtr.Zero,
            Strings.Current.Format("S_74d7a70b04", AppPaths.RootDir) +
            Strings.Current.Format("S_f7daeb6fbe", AppSettings.LastSaveError) +
            Strings.Current.Get("S_3cf26bb1a4"),
            Strings.Current.Get("S_0d7a3e0984"),
            0x00000010 /* MB_ICONERROR */);
        return false;
    }

    /// <summary>语言切换即时生效：XAML 绑定由 Strings 刷新，托盘/贴片/小组件由 LanguageChanged 重绘。</summary>
    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLanguage || LanguageCombo.SelectedItem is not ComboBoxItem item
            || item.Tag is not string tag) return;
        var settings = App.Services.Settings;
        settings.Language = tag == "auto" ? AppLanguage.Auto : AppLanguage.Normalize(tag);
        settings.Save();
        Strings.Current.SetLanguage(tag);
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

    /// <summary>锁定开关即时生效（不等保存）：拖动本来就是即时行为，等保存才锁等于没锁。
    /// 程序化赋值 IsOn 也会触发本事件，靠 Manager.SetLockPosition 的"值没变就返回"兜住。</summary>
    private void TaskbarLock_Toggled(object sender, RoutedEventArgs e)
    {
        App.Services.TaskbarBattery?.SetLockPosition(TaskbarLockToggle.IsOn);
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
            TaskbarScreenCombo.Items.Add(new ComboBoxItem { Content = Strings.Current.Get("S_cd1ffa09b6"), Tag = MonitorSelection.PrimaryToken });

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
        var offline = new ComboBoxItem { Content = Strings.Current.Format("S_916f2a70cc", name), Tag = wanted };
        TaskbarScreenCombo.Items.Add(offline);
        _screenLabels[wanted] = name;
        TaskbarScreenCombo.SelectedItem = offline;
    }

    /// <summary>配置的屏没插时的名字：先按设备路径查（未激活的屏也能查到），再退回 GDI 序号文案。</summary>
    private static string NameOfMissingScreen(string id)
    {
        string? name = MonitorCatalog.FriendlyNameOf(id);
        if (!string.IsNullOrEmpty(name)) return name;
        return id.StartsWith(@"\\.\", StringComparison.Ordinal) ? MonitorSelection.FallbackLabel(id) : Strings.Current.Get("S_5270720394");
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

    /// <summary>行内摘要：开了几个、都是谁。品牌多了只列前三个。</summary>
    private void UpdatePluginSummary()
    {
        var plugins = OrderedPlugins().ToList();
        var enabled = plugins
            .Where(p => App.Services.Settings.PluginEnabled.TryGetValue(p.Name, out var on) && on)
            .ToList();

        PluginSummaryText.Text = enabled.Count == 0
            ? Strings.Current.Format("S_ec6d86dffa", plugins.Count)
            : Strings.Current.Format("S_1a6de83725", enabled.Count, string.Join("、", enabled.Take(3).Select(p => p.DisplayName)))
              + (enabled.Count > 3 ? Strings.Current.Get("S_93ac7be2bc") : string.Empty);

        // 品牌行已挪进弹窗，搜索索引抓不到它们；把中英两名挂到行上，搜「北通」仍能定位到这一行
        AutomationProperties.SetName(PluginRowRoot,
            string.Join(" ", plugins.SelectMany(p => new[] { p.DisplayName, p.DisplayNameEn })));
    }

    private async void PluginManage_Click(object sender, RoutedEventArgs e)
    {
        // 连点会抛「Only a single ContentDialog can be open at any time」，async void 里逃出去直接崩进程
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            await ShowPluginDialogAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("打开 2.4G 插件弹窗失败: " + ex.Message);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    /// <summary>品牌名匹配：中英两名与配置键都算，用户按哪种写法搜都能命中。</summary>
    private static bool MatchesPlugin(IBatteryPlugin plugin, string query) =>
        query.Length == 0
        || plugin.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || plugin.DisplayNameEn.Contains(query, StringComparison.OrdinalIgnoreCase)
        || plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 品牌开关弹窗。二十多个品牌平铺在设置页要滚三屏，改成双列弹窗，并给搜索与批量开关。
    /// 「全开 / 全关」只作用于搜索出来的品牌：搜「罗技」再全开就只开罗技，不用一个个点。
    /// 确定即落盘并让插件开关生效——开关改的是扫描行为，不立刻应用用户看不到任何变化。
    /// </summary>
    private async Task ShowPluginDialogAsync()
    {
        var plugins = OrderedPlugins().ToList();
        var toggles = new Dictionary<string, ToggleSwitch>(StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<string, Grid>(StringComparer.OrdinalIgnoreCase);
        var searchBox = new TextBox();
        var countText = new TextBlock { Style = (Style)Resources["RowDescStyle"] };
        var grid = new Grid { ColumnSpacing = 16 };
        int visibleCount = plugins.Count;

        void UpdateCount()
        {
            // 初始化阶段字典还没填满，按 Values 数，别按 plugins 逐个查
            int on = toggles.Values.Count(t => t.IsOn);
            countText.Text = visibleCount == plugins.Count
                ? Strings.Current.Format("S_febca6c10e", on, plugins.Count)
                : Strings.Current.Format("S_b9454b9f7e", visibleCount, on, plugins.Count);
        }

        // 行元素只建一次，筛选时重排这批对象而不是重建控件——重建会让开关状态和焦点全部丢掉
        void Relayout(string query)
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var visible = plugins.Where(p => MatchesPlugin(p, query)).ToList();
            visibleCount = visible.Count;
            for (int r = 0; r < Math.Max(1, (visible.Count + 1) / 2); r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            if (visible.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = Strings.Current.Get("S_54d63cc0ba"),
                    Style = (Style)Resources["RowDescStyle"],
                    Margin = new Thickness(0, 12, 0, 12),
                };
                Grid.SetColumnSpan(empty, 2);
                grid.Children.Add(empty);
            }
            else
            {
                for (int i = 0; i < visible.Count; i++)
                {
                    var row = rows[visible[i].Name];
                    Grid.SetColumn(row, i % 2);
                    Grid.SetRow(row, i / 2);
                    grid.Children.Add(row);
                }
            }

            UpdateCount();
        }

        foreach (var plugin in plugins)
        {
            var toggle = new ToggleSwitch
            {
                OnContent = Strings.Current.Get("S_b4217093bc"),
                OffContent = Strings.Current.Get("S_a3332f1c94"),
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
            };
            toggle.Toggled += (_, _) => UpdateCount();

            // 中英两名都进无障碍名：用户按哪种写法搜都命中（"北通 BETOP" / "迈从 MCHOSE"）。
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = plugin.DisplayName, Style = (Style)Resources["RowTitleStyle"] });
            text.Children.Add(new TextBlock { Text = plugin.DisplayNameEn, Style = (Style)Resources["RowDescStyle"] });

            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 8, 0, 8) };
            AutomationProperties.SetName(row, $"{plugin.DisplayName} {plugin.DisplayNameEn}");
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 0);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(text);
            row.Children.Add(toggle);

            toggles[plugin.Name] = toggle;
            rows[plugin.Name] = row;
            // 先进字典再设值：Toggled 回调里要按名字查自己
            toggle.IsOn = App.Services.Settings.PluginEnabled.TryGetValue(plugin.Name, out var on) && on;
        }

        searchBox.PlaceholderText = Strings.Current.Get("S_f9a070a5c6");
        searchBox.TextChanged += (_, _) => Relayout(searchBox.Text.Trim());

        var selectAll = new HyperlinkButton { Content = Strings.Current.Get("S_d5179c4192"), Padding = new Thickness(8, 4, 8, 4) };
        var selectNone = new HyperlinkButton { Content = Strings.Current.Get("S_91b9bc43b2"), Padding = new Thickness(8, 4, 8, 4) };
        selectAll.Click += (_, _) => SetVisible(true);
        selectNone.Click += (_, _) => SetVisible(false);

        // 只动搜索出来的那些：没筛选时可见即全部，行为与"全开所有"等价
        void SetVisible(bool isOn)
        {
            foreach (var plugin in plugins.Where(p => MatchesPlugin(p, searchBox.Text.Trim())))
                toggles[plugin.Name].IsOn = isOn;
            UpdateCount();
        }

        var bulk = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        bulk.Children.Add(selectAll);
        bulk.Children.Add(selectNone);

        var toolbar = new Grid { ColumnSpacing = 8 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(searchBox, 0);
        Grid.SetColumn(bulk, 1);
        toolbar.Children.Add(searchBox);
        toolbar.Children.Add(bulk);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            Text = Strings.Current.Get("S_b62adfca1c"),
            Style = (Style)Resources["RowDescStyle"],
        });
        body.Children.Add(toolbar);
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = grid,
        });
        body.Children.Add(countText);

        Relayout(string.Empty);

        var dialog = new ContentDialog
        {
            Title = Strings.Current.Get("S_8b66d101bf"),
            Content = body,
            PrimaryButtonText = Strings.Current.Get("S_f526c89937"),
            CloseButtonText = Strings.Current.Get("S_4d0b4688c7"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        foreach (var (name, toggle) in toggles)
            App.Services.Settings.PluginEnabled[name] = toggle.IsOn;
        if (!TrySaveSettings()) return;

        App.Services.Manager.ApplySettings();
        UpdatePluginSummary();
    }

    /// <summary>收起时显示当前值摘要：分组头右侧那一行是用户判断"要不要展开"的唯一线索。</summary>
    private void UpdateGroupSummaries()
    {
        ReminderSummary.Text =
            Strings.Current.Format("S_ac30a084bc", NumberText(ThresholdBox.Value), (RemainingTimeEstimateToggle.IsOn ? Strings.Current.Get("S_b4217093bc") : Strings.Current.Get("S_a3332f1c94")));
        DataSummary.Text =
            Strings.Current.Format("S_21f1bad1d9", NumberText(SampleBox.Value), NumberText(RetentionBox.Value));
        WidgetSummary.Text =
            Strings.Current.Format("S_9ab1c1113e", (WidgetToggle.IsOn ? Strings.Current.Get("S_b4217093bc") : Strings.Current.Get("S_a3332f1c94")), SelectedInt(WidgetMaxCombo, WidgetMaxDevicesOptions.Default));
        TaskbarSummary.Text =
            Strings.Current.Format("S_775e6421e2", (TaskbarToggle.IsOn ? Strings.Current.Get("S_b4217093bc") : Strings.Current.Get("S_a3332f1c94")), SelectedInt(TaskbarMaxCombo, TaskbarBatteryOptions.Default));
        LogSummary.Text =
            Strings.Current.Format("S_5b95ff9028", SelectedLabel(LogLevelCombo, Strings.Current.Get("S_2da40f4073")), NumberText(LogRetentionBox.Value));
    }

    private static string NumberText(double value) =>
        double.IsNaN(value) ? "0" : ((int)Math.Round(value)).ToString();

    private static int SelectedInt(ComboBox combo, int fallback) =>
        combo.SelectedItem is ComboBoxItem { Tag: int value } ? value : fallback;

    private static string SelectedLabel(ComboBox combo, string fallback) =>
        combo.SelectedItem is ComboBoxItem { Content: string text } ? text : fallback;

    private void Group_Expanding(Expander sender, ExpanderExpandingEventArgs args) => SetSummaryOpacity(sender, 0);

    private void Group_Collapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        UpdateGroupSummaries();
        SetSummaryOpacity(sender, 1);
    }

    private static void SetSummaryOpacity(object sender, double opacity)
    {
        if (sender is Expander { Header: Grid header } &&
            header.Children.Count > 1 &&
            header.Children[1] is TextBlock summary)
        {
            summary.Opacity = opacity;
        }
    }

    private void SettingsRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid row && HoverFill is { } brush) row.Background = brush;
    }

    private void SettingsRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid row) row.ClearValue(Panel.BackgroundProperty);
    }

    private static Brush? HoverFill =>
        Application.Current.Resources.TryGetValue("AppHoverFillBrush", out var value)
            ? value as Brush
            : null;

    /// <summary>索引每次搜索都重建：折叠区一展开就往树里加元素，缓存下来的引用会指向已摘下的节点。</summary>
    private void RebuildSearchIndex()
    {
        _searchEntries.Clear();
        AddSearchEntries(SettingsContent);
    }

    /// <summary>折叠区默认收起，里面的行遍历不到——搜索前先把所有分组摊开。品牌和设备已挪进弹窗，索引靠行的无障碍名兜住。</summary>
    private void ExpandForSearch()
    {
        foreach (var expander in Descendants<Expander>(SettingsContent))
        {
            if (!expander.IsExpanded) expander.IsExpanded = true;
        }
        // 展开是异步布局：不强制量一次，折叠区里的行还不在视觉树里，搜索会漏。
        SettingsContent.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
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
            if (parent is Expander expander)
            {
                var headerText = FirstText(expander.Header);
                if (!string.IsNullOrWhiteSpace(headerText)) return headerText;
            }

            if (parent is StackPanel section &&
                section.Children.FirstOrDefault() is TextBlock sectionTitle)
            {
                if (!string.IsNullOrWhiteSpace(sectionTitle.Text)) return sectionTitle.Text;
            }
        }

        return string.Empty;
    }

    private static string? FirstText(object? node)
    {
        if (node is TextBlock text) return text.Text;
        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                var found = FirstText(child);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        if (node is ContentControl { Content: { } content }) return FirstText(content);
        return null;
    }

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SettingsSearchBox.Text.Trim();
        // 先摊开折叠区再建索引：收起态的品牌行不在树里，搜「北通」会一条都命中不了
        if (query.Length > 0)
        {
            ExpandForSearch();
            SettingsScrollViewer.ChangeView(null, 0, null, true);
        }
        RebuildSearchIndex();
        SearchResultsPanel.Children.Clear();
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

        // y 是相对内容根的坐标，本身不随滚动变化；再叠加 VerticalOffset 会多滚一个当前偏移量
        // （停在顶部时 offset=0 恰好正确，所以这个错只在滚过之后才暴露）
        var y = entry.Target.TransformToVisual(SettingsContent).TransformPoint(new Point(0, 0)).Y;
        SettingsScrollViewer.ChangeView(null, Math.Max(0, y - 12), null, false);

        ClearHighlight();
        var accent = Application.Current.Resources.TryGetValue("SystemAccentColor", out var value) && value is Color color
            ? color
            : Color.FromArgb(255, 0, 120, 215);
        _highlightedTitle = entry.TitleBlock;
        _previousTitleBrush = entry.TitleBlock.Foreground;
        _previousTitleWeight = entry.TitleBlock.FontWeight;
        _titleHighlightBrush = new SolidColorBrush(accent);
        entry.TitleBlock.Foreground = _titleHighlightBrush;
        entry.TitleBlock.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        _highlightTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _highlightTimer.Stop();
        _highlightTimer.Interval = TimeSpan.FromMilliseconds(950);
        _highlightTimer.Tick -= HighlightTimer_Tick;
        _highlightTimer.Tick += HighlightTimer_Tick;
        _highlightTimer.Start();
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
        s.TaskbarBatteryMode = TaskbarBatteryMode.Normalize(
            (TaskbarModeCombo.SelectedItem as ComboBoxItem)?.Tag as string);
        s.TaskbarBatteryMaxDevices = TaskbarMaxCombo.SelectedItem is ComboBoxItem taskbarMaxItem
                                     && taskbarMaxItem.Tag is int taskbarMaxValue
            ? TaskbarBatteryOptions.Clamp(taskbarMaxValue)
            : TaskbarBatteryOptions.Default;
        // 贴片关着时上面的 Toggled 没有落盘通道（Manager 为 null），保存时补一次，免得开关显示与实际相反
        s.TaskbarBatteryLockPosition = TaskbarLockToggle.IsOn;
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
        // 插件开关与任务栏设备勾选只从各自的弹窗改，确定时已经落过盘，这里不重复写：
        // 页面上的控件里根本没有它们的值，硬写会把弹窗里刚改的结果覆盖掉。
        // 保存失败必须让用户看见：以前这里把返回值丢掉，界面毫无反馈，用户以为"点了没反应"。
        // 写不进去时（目录不可写 / 磁盘满 / 安全软件拦截）继续往下走只会让内存与磁盘不一致，直接停。
        if (!TrySaveSettings()) return;

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
            ? s.LogLevel == LogLevelNames.Default ? Strings.Current.Get("S_cdfab96f75") : Strings.Current.Get("S_fbfadede29")
            : Strings.Current.Get("S_2e9061c024");
        UpdateGroupSummaries();
        ShowSavedFeedback(feedback);

        if (autostartError != null)
        {
            Native.MessageBoxW(
                IntPtr.Zero,
                Strings.Current.Get("S_4291e16ff4") +
                Strings.Current.Format("S_3faff71558", autostartError) +
                Strings.Current.Get("S_8f4e16bdc5") +
                Strings.Current.Get("S_96314d25cc") +
                Strings.Current.Get("S_35ce4a8ffa"),
                Strings.Current.Get("S_2b6b27b20a"),
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
            Title = Strings.Current.Get("S_18a42772ea"),
            Content = Strings.Current.Get("S_cc33b14ab3"),
            PrimaryButtonText = Strings.Current.Get("S_84fcd70d42"),
            CloseButtonText = Strings.Current.Get("S_4d0b4688c7"),
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
            ShowSavedFeedback(Strings.Current.Get("S_6cfd6e9283"));
        }
        catch (Exception ex)
        {
            Log.Warn("清空日志失败: " + ex.Message);
            ShowSavedFeedback(Strings.Current.Get("S_59cfa9a896") + ex.Message);
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
