using System.Diagnostics;
using System.Linq;
using VigilWatt.App.Controls;
using VigilWatt.App.Services;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Utils;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    /// <summary>每次进页面都重读设置。搜索索引改为按需重建：折叠区展开后视觉树会变，缓存会过期。</summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SyncFromSettings();

    /// <summary>只建一次：下拉框档位。缓存页复用同一实例，再建一遍会叠加出重复条目。</summary>
    private void BuildOptions()
    {
        // 档位按统一范围生成，保证下拉框和取值收敛永远同源
        foreach (var n in WidgetMaxDevicesOptions.Choices)
            WidgetMaxCombo.Items.Add(new ComboBoxItem { Content = $"{n} 个", Tag = n });
        foreach (var n in TaskbarBatteryOptions.Choices)
            TaskbarMaxCombo.Items.Add(new ComboBoxItem { Content = $"{n} 个", Tag = n });
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
            TaskbarDeviceSummaryText.Text = "暂无已识别的设备";
            return;
        }

        // 配置里记的名字可能指向已经拔掉的设备：只报台数，不逐个列名字，免得摘要里一堆查无此机
        int selected = App.Services.Settings.TaskbarBatterySelectedDevices?.Count ?? 0;
        TaskbarDeviceSummaryText.Text = selected == 0
            ? $"{names.Count} 台全部显示"
            : $"已选 {selected} 台 · 其余不进贴片";
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
                ? $"未勾选任何设备，{names.Count} 台全部显示"
                : $"已选 {on} / {names.Count} 台";
        }

        foreach (var chip in chips)
        {
            chip.Checked += (_, _) => RefreshCount();
            chip.Unchecked += (_, _) => RefreshCount();
        }
        RefreshCount();

        var selectAll = new HyperlinkButton { Content = "全选", Padding = new Thickness(8, 4, 8, 4) };
        var selectNone = new HyperlinkButton { Content = "清空", Padding = new Thickness(8, 4, 8, 4) };
        selectAll.Click += (_, _) => { foreach (var c in chips) c.IsChecked = true; };
        selectNone.Click += (_, _) => { foreach (var c in chips) c.IsChecked = false; };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        actions.Children.Add(selectAll);
        actions.Children.Add(selectNone);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            Text = "勾选后只有这些设备会出现在贴片里；一个都不勾 = 全部显示。",
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
            Title = "选择任务栏显示设备",
            Content = body,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
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
            $"设置保存失败，无法写入：\r\n{AppPaths.RootDir}\\settings.json\r\n\r\n" +
            $"错误：{AppSettings.LastSaveError}\r\n\r\n" +
            "常见原因：目录不可写、磁盘空间不足、或被安全软件拦截。\r\n本次修改不会保留。",
            "VigilWatt 保存失败",
            0x00000010 /* MB_ICONERROR */);
        return false;
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

    /// <summary>行内摘要：开了几个、都是谁。品牌多了只列前三个。</summary>
    private void UpdatePluginSummary()
    {
        var plugins = OrderedPlugins().ToList();
        var enabled = plugins
            .Where(p => App.Services.Settings.PluginEnabled.TryGetValue(p.Name, out var on) && on)
            .ToList();

        PluginSummaryText.Text = enabled.Count == 0
            ? $"{plugins.Count} 个品牌，当前都没开启"
            : $"已开启 {enabled.Count} 个 · {string.Join("、", enabled.Take(3).Select(p => p.DisplayName))}"
              + (enabled.Count > 3 ? " 等" : string.Empty);

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
                ? $"已开启 {on} / {plugins.Count} 个品牌"
                : $"匹配 {visibleCount} 个 · 已开启 {on} / {plugins.Count}";
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
                    Text = "没有匹配的品牌",
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
                OnContent = "开",
                OffContent = "关",
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

        searchBox.PlaceholderText = "搜索品牌（中文或英文）";
        searchBox.TextChanged += (_, _) => Relayout(searchBox.Text.Trim());

        var selectAll = new HyperlinkButton { Content = "全开", Padding = new Thickness(8, 4, 8, 4) };
        var selectNone = new HyperlinkButton { Content = "全关", Padding = new Thickness(8, 4, 8, 4) };
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
            Text = "开启某个品牌即授权它发送工作所需的帧；会写帧的品牌默认关闭。「全开 / 全关」只作用于搜索出来的品牌。",
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
            Title = "2.4G 设备插件",
            Content = body,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
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

    /// <summary>索引每次搜索都重建：折叠区一展开就往树里加元素，缓存下来的引用会指向已摘下的节点。</summary>
    private void RebuildSearchIndex()
    {
        _searchEntries.Clear();
        AddSearchEntries(SettingsContent);
    }

    /// <summary>日志区默认收起，里面的行遍历不到——搜索前先摊开。品牌和设备已挪进弹窗，索引靠行的无障碍名兜住。</summary>
    private void ExpandForSearch()
    {
        if (!LogExpander.IsExpanded) LogExpander.IsExpanded = true;
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
            // 日志区收进了 Expander，中间没有「标题行 + 卡片」的 section StackPanel，分类取 Header 文字
            if (parent is Expander expander)
            {
                var headerText = (expander.Header as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
                if (!string.IsNullOrWhiteSpace(headerText)) return headerText;
            }

            if (parent is not StackPanel section || section.Children.FirstOrDefault() is not StackPanel header) continue;
            var title = header.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(title)) return title;
        }

        return string.Empty;
    }

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SettingsSearchBox.Text.Trim();
        // 先摊开折叠区再建索引：收起态的品牌行不在树里，搜「北通」会一条都命中不了
        if (query.Length > 0) ExpandForSearch();
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
        entry.TitleBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
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
