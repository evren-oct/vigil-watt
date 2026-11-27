using System.Collections.ObjectModel;
using System.Diagnostics;
using VigilWatt.App.Services;
using VigilWatt.App.ViewModels;
using VigilWatt.Core.Capture;
using VigilWatt.Core.Capture.Hid;
using VigilWatt.Core.Utils;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Views;
/// <summary>
/// 功 能： 设备诊断页：选任务/设备/接口 → 预检 → 只读采集 → 出报告，全程本地、不自动切换采集方式。
/// 2026/10/24 12:40:43 20211
/// 采集在 Core 里跑，本页只做编排与展示；离开页面不终止会话，退出应用时统一释放（见 DisposeActive）。
/// </summary>
public sealed partial class CapturePage : Page
{
    private static CaptureSession? _active;
    private CaptureSession? _lastSession;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ObservableCollection<CaptureRecordRow> _recordRows = [];
    private int _renderedCount;
    private IReadOnlyList<CaptureDeviceGroup> _allGroups = [];
    private CaptureDeviceGroup? _selectedGroup;
    private CaptureDeviceInfo? _selected;
    private IReadOnlyList<CapabilityItem> _capabilities = [];
    private IReadOnlyList<CaptureCapabilityRow> _capabilityRowsAll = [];
    private IReadOnlyList<CaptureCapabilityRow> _capabilityRowsAttention = [];
    private string _lastSummary = "";
    private readonly CaptureGuide _guide = CaptureGuide.Default();
    private readonly List<GuideStepProgress> _steps = [];
    // 步骤条里每段的「N 条」文本。整棵树每 500ms 重建会闪，只改这些 TextBlock 的 Text。
    private readonly List<TextBlock> _stepStateBlocks = [];
    // 当前步骤窗口的起点（= 上一条标记时的记录总数）。第一步从 0 算。
    private int _windowStartCount;
    // 首帧的相对时间：界面要回答「等了多久才出数据」
    private long? _firstRecordMono;
    // 协议通道 → 颜色。按首次出现顺序从调色板取，一个会话内固定。
    private readonly Dictionary<string, SolidColorBrush> _channelColors = new(StringComparer.Ordinal);
    private int _nextChannelColor;
    private bool _runningNarrow;
    private bool _idleNarrow;
    private bool? _idleStacked;

    public CapturePage()
    {
        InitializeComponent();
        DataContext = Strings.Current;
        NavigationCacheMode = NavigationCacheMode.Required;
        // 醒目但不刺眼：琥珀底 + 黑字，深浅主题都读得清（UiPalette.Mid 是项目自己的色板）
        if (WindowHint != null) WindowHint.Background = UiPalette.Brush(UiPalette.Mid);
        if (WindowHintText != null) WindowHintText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black);
        // 放弃是不可逆的，给它一个警示色，别和「停止并生成报告」长得一样
        if (AbandonButton != null) AbandonButton.Foreground = UiPalette.Brush(UiPalette.Low);
        // 页面被缓存复用、构造函数只跑一次，刷新必须挂到 Loaded（dev/runtime-pitfalls.md §19）
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _timer.Tick += (_, _) => RefreshRecords();
        // 记录列表绑 ObservableCollection 增量追加：每拍重建 ItemsSource 会让整表闪一下
        RecordList.ItemsSource = _recordRows;
    }

    private const int MaxRenderedRecords = 100;

    private CaptureTaskKind CurrentTask
        => TaskSupported?.IsChecked == true
            ? CaptureTaskKind.SupportedDeviceDiagnosis
            : CaptureTaskKind.UnknownDeviceInvestigation;

    /// <summary>应用退出时调用，保证句柄与文件流被释放（R4）。</summary>
    public static void DisposeActive()
    {
        try
        {
            _active?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"退出时释放采集会话失败: {ex.Message}");
        }
        _active = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (SubtitleText == null) return;
        _timer.Start();
        _ = RefreshDevicesAsync();
        _ = RefreshHistoryAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private void Task_Checked(object sender, RoutedEventArgs e)
    {
        // InitializeComponent 期间 XAML 里的 IsChecked 也会触发，此时其它控件还没建好
        if (DeviceList == null || TaskHint == null) return;
        if (_allGroups.Count > 0) ApplyTaskFilter(_allGroups);
    }

    private async Task RefreshDevicesAsync()
    {
        if (DeviceList == null || DeviceHint == null) return;
        if (_active is { State: CaptureSessionState.Running or CaptureSessionState.Paused })
        {
            ShowMessage(Strings.Current.Get("S_0d23ee2a1d"), InfoBarSeverity.Warning);
            return;
        }

        try
        {
            _allGroups = await Task.Run(() =>
            {
                var devices = HidCaptureEnumerator.Enumerate();
                // 认领表由 DeviceManager 持有：被插件认领 = 这台设备已经适配
                return CaptureDeviceCatalog.Group(devices, App.Services.Manager.GetClaimOwner);
            });
            ApplyTaskFilter(_allGroups);
        }
        catch (Exception ex)
        {
            Log.Warn($"采集页枚举设备失败: {ex.Message}");
            ShowMessage(Strings.Current.Format("S_e59c839cf9", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void ApplyTaskFilter(IReadOnlyList<CaptureDeviceGroup> groups)
    {
        if (DeviceList == null || DeviceHint == null || TaskHint == null) return;

        var filtered = CaptureDeviceCatalog.ForTask(groups, CurrentTask);
        DeviceList.ItemsSource = filtered.Select(ToDeviceRow).ToList();
        DeviceList.SelectedItem = null;
        ClearSelection();

        TaskHint.Text = CurrentTask == CaptureTaskKind.SupportedDeviceDiagnosis
            ? Strings.Current.Get("S_cac82fa320")
            : Strings.Current.Get("S_eebe07616e");

        if (groups.Count == 0)
        {
            DeviceHint.Text = Strings.Current.Get("S_ae468ceee3");
            return;
        }

        DeviceHint.Text = CurrentTask == CaptureTaskKind.SupportedDeviceDiagnosis && filtered.Count == 0
            ? Strings.Current.Get("S_380b91a527")
            : Strings.Current.Format("S_9fa307a442", groups.Count, groups.Sum(g => g.Interfaces.Count), filtered.Count)
              + Strings.Current.Get("S_aa4531ff39");
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceList == null || ProbeButton == null || InterfaceList == null || InterfaceHint == null) return;
        if (DeviceList.SelectedItem is not CaptureDeviceRow row)
        {
            ClearSelection();
            return;
        }

        _selectedGroup = row.Group;
        var recommended = row.Group.Recommended;
        var interfaces = row.Group.Interfaces.Select(device => new CaptureInterfaceRow
        {
            Title = CaptureDeviceCatalog.ShortInterface(device.InterfaceKey)
                    + (ReferenceEquals(device, recommended) ? Strings.Current.Get("S_ed44097716") : ""),
            Subtitle = CaptureDeviceCatalog.DescribeInterface(device),
            IsRecommended = ReferenceEquals(device, recommended),
            Info = device,
        }).ToList();

        InterfaceList.ItemsSource = interfaces;
        InterfaceList.IsEnabled = true;
        var pick = interfaces.FirstOrDefault(i => i.IsRecommended) ?? interfaces.FirstOrDefault();
        InterfaceList.SelectedItem = pick;
        if (pick == null)
        {
            _selected = null;
            ProbeButton.IsEnabled = false;
            StartButton.IsEnabled = false;
            InterfaceHint.Text = Strings.Current.Get("S_88e482df25");
        }
    }

    private async void InterfaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InterfaceList == null || ProbeButton == null || StartButton == null || InterfaceHint == null) return;
        if (CapabilityList == null || FeatureCheck == null || FeatureIdBox == null) return;
        if (InterfaceList.SelectedItem is not CaptureInterfaceRow row)
        {
            _selected = null;
            ProbeButton.IsEnabled = false;
            StartButton.IsEnabled = false;
            return;
        }

        _selected = row.Info;
        _capabilities = [];
        CapabilityList.ItemsSource = null;
        ProbeButton.IsEnabled = true;
        StartButton.IsEnabled = false;
        FeatureCheck.IsEnabled = false;
        FeatureCheck.IsChecked = false;
        FeatureIdBox.ItemsSource = null;
        InterfaceHint.Text = DescribeInterfaceHint(row.Info);
        // 预检只读枚举时已有的 caps，不碰设备，所以选中就直接跑，不需要用户再点一下
        await RunProbeAsync();
    }

    private static string DescribeInterfaceHint(CaptureDeviceInfo device)
    {
        if (device.AccessDenied)
            return Strings.Current.Get("S_c50c4cd4b8");
        if (!device.IsVendorDefined)
        {
            var kind = device.UsagePage switch
            {
                0x000C => Strings.Current.Get("S_d8f7010598"),
                0x0001 => Strings.Current.Get("S_f0ec6f2199"),
                0x000D => Strings.Current.Get("S_b0b94daeed"),
                _ => Strings.Current.Get("S_c1cd80bc57"),
            };
            return Strings.Current.Format("S_7502174118", kind)
                   + Strings.Current.Get("S_da7d89ff47");
        }
        if (device.InputReportLength > 0)
            return Strings.Current.Get("S_cde87ad479");
        if (device.FeatureReportLength > 0)
            return Strings.Current.Get("S_62a303b84a")
                   + Strings.Current.Get("S_d231babe56");
        return Strings.Current.Get("S_131b0ae4a0");
    }

    private async void Probe_Click(object sender, RoutedEventArgs e) => await RunProbeAsync();

    /// <summary>预检只读枚举时已有的 caps（不碰设备），所以选中接口就自动跑；按钮留着给拔插后重读。</summary>
    private async Task RunProbeAsync()
    {
        if (_selected is not { } device) return;
        if (CapabilityList == null || StartButton == null || FeatureCheck == null || FeatureIdBox == null) return;
        try
        {
            var capabilities = await Task.Run(() => HidCapabilityProbe.Probe(device));
            // 快速切换接口时，晚到的旧结果不能盖掉当前选中的那条
            if (!ReferenceEquals(device, _selected)) return;

            _capabilities = capabilities;
            _capabilityRowsAll = _capabilities.Select(BuildCapabilityRow).ToList();
            _capabilityRowsAttention = _capabilities
                .Where(c => c.Status != CapabilityStatus.Available)
                .Select(BuildCapabilityRow)
                .ToList();
            // 全都可用时不给空列表，直接展开全部
            if (ShowAllCapsCheck != null) ShowAllCapsCheck.IsChecked = _capabilityRowsAttention.Count == 0;
            if (ShowAllCapsCheck != null) ShowAllCapsCheck.IsEnabled = true;
            ApplyCapabilityFilter();
            StartButton.IsEnabled = !device.AccessDenied && (device.InputReportLength > 0 || device.FeatureReportLength > 0);
            var hasFeature = device.FeatureReportIds.Count > 0 && !device.AccessDenied;
            FeatureCheck.IsEnabled = hasFeature;
            FeatureIdBox.ItemsSource = hasFeature
                ? device.FeatureReportIds.Select(id => $"0x{id:X2}").ToList()
                : null;
            if (hasFeature) FeatureIdBox.SelectedIndex = 0;
            // 只有该接口真有输出通道才谈得上发帧
            var canSend = !device.AccessDenied && device.OutputReportLength > 0;
            if (AllowSendCheck != null)
            {
                AllowSendCheck.IsEnabled = canSend;
                if (!canSend) AllowSendCheck.IsChecked = false;
            }
            if (AllowSendHint != null)
                AllowSendHint.Text = canSend
                    ? Strings.Current.Get("S_6291cde9c3")
                    : Strings.Current.Get("S_268cbd4d10");
            ShowMessage(device.AccessDenied
                ? Strings.Current.Get("S_3c9df4da2a")
                : Strings.Current.Get("S_fcf4c5490a"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Warn($"采集预检失败: {ex.Message}");
            ShowMessage(Strings.Current.Format("S_fb60b3b35a", ex.Message), InfoBarSeverity.Error);
        }
    }

    /// <summary>默认只列「需要你处理」的项：16 项全铺开，用户每次都得扒拉半天。</summary>
    private void ApplyCapabilityFilter()
    {
        if (CapabilityList == null || CapabilitySummary == null) return;
        var showAll = ShowAllCapsCheck?.IsChecked == true;
        CapabilityList.ItemsSource = showAll ? _capabilityRowsAll : _capabilityRowsAttention;
        CapabilitySummary.Text = BuildCapabilitySummary(showAll);
    }

    private string BuildCapabilitySummary(bool showAll)
    {
        if (_capabilities.Count == 0) return Strings.Current.Get("S_ee21f07e93");
        var counts = _capabilities
            .GroupBy(c => c.Status)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key.Label()} {g.Count()}");
        var head = Strings.Current.Format("S_34f12a4864", _capabilities.Count, string.Join(" · ", counts));
        if (showAll) return head;
        return _capabilityRowsAttention.Count == 0
            ? head + Strings.Current.Get("S_9fb87cafd1")
            : head + Strings.Current.Format("S_7ab8bced60", _capabilityRowsAttention.Count);
    }

    private void ShowAllCaps_Changed(object sender, RoutedEventArgs e)
    {
        if (CapabilityList == null || _capabilities.Count == 0) return;
        ApplyCapabilityFilter();
    }

    /// <summary>窗口偏窄时提醒最大化——两列布局在 800px 下能看，但明显更好用的是最大化之后。</summary>
    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WindowHint == null) return;
        WindowHint.Visibility = e.NewSize.Width < 640 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>空闲态两栏：窄窗口退回上下堆叠（设备卡固定高度），宽窗口左侧设备列表占满剩余高度。</summary>
    private void IdlePanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IdlePanel == null || DevicePickCard == null || DetailScroll == null) return;
        bool stacked = e.NewSize.Width < 520;
        if (stacked == _idleStacked) return;
        _idleStacked = stacked;

        IdlePanel.ColumnDefinitions.Clear();
        IdlePanel.RowDefinitions.Clear();
        if (stacked)
        {
            IdlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            IdlePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(320) });
            IdlePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(DevicePickCard, 0);
            Grid.SetRow(DevicePickCard, 0);
            Grid.SetColumn(DetailScroll, 0);
            Grid.SetRow(DetailScroll, 1);
            DetailScroll.Margin = new Thickness(0, 20, 0, 0);
        }
        else
        {
            IdlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star), MinWidth = 250 });
            IdlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star) });
            IdlePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(DevicePickCard, 0);
            Grid.SetRow(DevicePickCard, 0);
            Grid.SetColumn(DetailScroll, 1);
            Grid.SetRow(DetailScroll, 0);
            DetailScroll.Margin = new Thickness(0);
        }
    }

    private void FeatureCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (FeatureIdBox == null) return;
        FeatureIdBox.IsEnabled = FeatureCheck.IsChecked == true;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _capabilities.Count == 0) return;
        if (_active != null)
        {
            ShowMessage(Strings.Current.Get("S_42d4c37781"), InfoBarSeverity.Warning);
            return;
        }

        var device = _selected;
        var featureIds = ReadFeatureIds(device);
        var allowSend = AllowSendCheck?.IsChecked == true;
        var options = new CaptureOptions
        {
            Task = CurrentTask,
            FeatureReportIds = featureIds,
            AllowSend = allowSend,
            AppVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "",
            OsVersion = CaptureEnvironment.OsVersion,
            Backend = CaptureEnvironment.Backend,
        };

        try
        {
            var session = CaptureSession.Create(device, _capabilities, options,
                () => new UsbHidCaptureSource(device, featureIds, options.ReadTimeout, options.FeatureInterval,
                    allowSend));
            _active = session;
            _lastSession = null;
            _lastSummary = "";
            session.Start();
            ResetGuide();
            UpdateDeviceBanner();
            ResetRecordRows();
            _firstRecordMono = null;
            _channelColors.Clear();
            _nextChannelColor = 0;
            AddStepHeaderForCurrent();
            if (BatteryBox != null) BatteryBox.Text = "";
            if (BatteryStatus != null) BatteryStatus.Text = Strings.Current.Get("S_5de955fd0f");
            SetRunningState(true);
            RefreshRecords();
            ShowMessage(featureIds.Count > 0
                ? Strings.Current.Format("S_aa37a7d30b", string.Join("、", featureIds.Select(i => $"0x{i:X2}")))
                : Strings.Current.Get("S_8a905715f3"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error("创建采集会话失败", ex);
            ShowMessage(Strings.Current.Format("S_830a200b88", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || PauseButton == null) return;
        try
        {
            if (_active.State == CaptureSessionState.Running)
            {
                await _active.PauseAsync();
                if (PauseLabel != null) PauseLabel.Text = Strings.Current.Get("S_1fc1afc5c5");
                ShowMessage(Strings.Current.Get("S_af2dbe3482"), InfoBarSeverity.Warning);
            }
            else if (_active.State == CaptureSessionState.Paused)
            {
                _active.Resume();
                if (PauseLabel != null) PauseLabel.Text = Strings.Current.Get("S_130448bce6");
                ShowMessage(Strings.Current.Get("S_d2e4ea5d1e"), InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"暂停/继续失败: {ex.Message}");
            ShowMessage(Strings.Current.Format("S_b2a510be7d", ex.Message), InfoBarSeverity.Error);
        }
    }

    /// <summary>在记录列表里插一条分割行：后面这批报文属于哪一步，一眼能对上。</summary>
    private void AddStepHeader(string text) => _recordRows.Add(CaptureRecordRow.StepHeader(text));

    /// <summary>进入下一步（或全部走完）时插分割行——步骤窗口从上一条标记算起，边界就在点「完成」这一刻。</summary>
    private void AddStepHeaderForCurrent()
    {
        if (_guide.Current is { } next)
            AddStepHeader(Strings.Current.Format("S_48367fdef0", _guide.CurrentIndex + 1, next.Title));
        else
            AddStepHeader(Strings.Current.Get("S_d252747e6f"));
    }

    private void Mark_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || MarkBox == null) return;
        var note = MarkBox.Text.Trim();
        _active.Mark(note);
        MarkBox.Text = "";
        AddStepHeader(string.IsNullOrEmpty(note) ? Strings.Current.Get("S_99245369f0") : Strings.Current.Format("S_a124918159", note));
        // 标记的语义是「这一段到此结束」，所以文案要说清它划的是分界而不是起点
        ShowMessage(Strings.Current.Get("S_e56392bc58"), InfoBarSeverity.Informational);
    }

    /// <summary>用户从官方软件/设备屏幕读到的电量，作为核对参照。空或 0 = 不使用（不是输入错误）。</summary>
    private void Battery_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || BatteryBox == null || BatteryStatus == null) return;

        var text = BatteryBox.Text.Trim();
        if (text.Length == 0 || text == "0")
        {
            BatteryStatus.Text = Strings.Current.Get("S_3d922fc9be");
            ShowMessage(Strings.Current.Get("S_7f8a9f85ea"), InfoBarSeverity.Informational);
            return;
        }

        if (!int.TryParse(text, out var percent) || percent is < 1 or > 100)
        {
            BatteryStatus.Text = Strings.Current.Get("S_cd4d2f3c04");
            ShowMessage(Strings.Current.Get("S_6ccb1c30aa"), InfoBarSeverity.Warning);
            return;
        }

        if (!_active.MarkBattery(percent))
        {
            BatteryStatus.Text = Strings.Current.Get("S_e5ce4b13d6");
            ShowMessage(Strings.Current.Get("S_80c04ed7d9"), InfoBarSeverity.Warning);
            return;
        }

        // 输入框**不清空**：留着用户才能确认自己填的是多少
        BatteryStatus.Text = Strings.Current.Format("S_98db3985b8", percent, DateTime.Now);
        ShowMessage(Strings.Current.Format("S_c40bdad215", percent), InfoBarSeverity.Success);
    }

    private void GuideNext_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || _guide.Current is not { } step) return;
        CompleteStep(step);
        _guide.Advance();
        AddStepHeaderForCurrent();
        UpdateGuideUi();
        ShowMessage(_guide.IsComplete
            ? Strings.Current.Get("S_813f22f55f")
            : Strings.Current.Format("S_68aff9a8b4", step.Title, _guide.Current!.Title), InfoBarSeverity.Success);
    }

    private void GuideRedo_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || _guide.Current is not { } step) return;
        RedoStep(step);
        UpdateGuideUi();
        ShowMessage(Strings.Current.Format("S_d847b10d64", step.Title), InfoBarSeverity.Informational);
    }

    private void GuideBack_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || !_guide.Back()) return;
        UpdateGuideUi();
        ShowMessage(Strings.Current.Format("S_d2605e08ab", _guide.Current!.Title), InfoBarSeverity.Informational);
    }

    /// <summary>标记只表示「这一步做完了」，所以本步窗口是上一条标记到这一刻，点完成时才封口。</summary>
    private void CompleteStep(CaptureGuideStep step)
    {
        _active!.Mark(step.Title, step.Id);
        var count = _active.RecordCount;
        var entry = _steps.LastOrDefault(s => s.StepId == step.Id && s.CountAtDone == null);
        if (entry == null)
        {
            entry = new GuideStepProgress { StepId = step.Id, Title = step.Title, CountAtStart = _windowStartCount };
            _steps.Add(entry);
        }
        entry.CountAtDone = count;
        _windowStartCount = count;
    }

    /// <summary>重做：从这一刻重新量本步窗口。不额外打标记——标记只表示「做完」，重做还没做完。</summary>
    private void RedoStep(CaptureGuideStep step)
    {
        var count = _active?.RecordCount ?? _windowStartCount;
        _windowStartCount = count;
        _steps.RemoveAll(s => s.StepId == step.Id && s.CountAtDone == null);
        _steps.Add(new GuideStepProgress { StepId = step.Id, Title = step.Title, CountAtStart = count });
    }

    /// <summary>停止时把当前这一步的窗口封口（条目还没建就补一个），否则它的条数会永远显示不出来。</summary>
    private void CloseOpenStep()
    {
        var count = _active?.RecordCount ?? 0;
        var open = _steps.LastOrDefault(s => s.CountAtDone == null);
        if (open == null)
        {
            if (_guide.Current is not { } step) return;
            open = new GuideStepProgress { StepId = step.Id, Title = step.Title, CountAtStart = _windowStartCount };
            _steps.Add(open);
        }
        open.CountAtDone = count;
    }

    private void ResetGuide()
    {
        _guide.Reset();
        _steps.Clear();
        _windowStartCount = 0;
        UpdateGuideUi();
    }

    private void UpdateGuideUi()
    {
        if (GuideTitle == null || GuideHint == null || GuideNextButton == null
            || GuideRedoButton == null || GuideBackButton == null) return;

        var running = _active is { State: CaptureSessionState.Running or CaptureSessionState.Paused };
        GuideNextButton.IsEnabled = running && !_guide.IsComplete;
        GuideRedoButton.IsEnabled = running && !_guide.IsComplete;
        GuideBackButton.IsEnabled = running && _guide.CurrentIndex > 0;

        if (!running)
        {
            GuideTitle.Text = _active == null
                ? Strings.Current.Get("S_4985a7f09a")
                : Strings.Current.Get("S_f7614b1d2f");
            GuideHint.Text = Strings.Current.Get("S_ea0ff2f22b");
        }
        else if (_guide.IsComplete)
        {
            GuideTitle.Text = Strings.Current.Get("S_02ee09d132");
            GuideHint.Text = Strings.Current.Get("S_fdd7cf7060");
        }
        else
        {
            GuideTitle.Text = Strings.Current.Format("S_e34430bc71", _guide.CurrentIndex + 1, _guide.Steps.Count, _guide.Current!.Title);
            GuideHint.Text = _guide.Current.Hint;
        }

        if (GuideDeviceWarning != null)
        {
            // 采的如果是键鼠本身，关机那一步会把自己的光标弄没，只能靠快捷键
            GuideDeviceWarning.Visibility = running && _selectedGroup is { IsInputPeripheral: true }
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        BuildStepBar();
        UpdateGuideProgress();
    }

    /// <summary>顶部常驻步骤条：不管页面滚到哪，当前第几步都看得见。</summary>
    private void BuildStepBar()
    {
        if (StepBar == null) return;
        StepBar.Children.Clear();
        StepBar.ColumnDefinitions.Clear();
        _stepStateBlocks.Clear();
        for (var i = 0; i < _guide.Steps.Count; i++)
            StepBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < _guide.Steps.Count; index++)
        {
            var step = _guide.Steps[index];
            var isDone = index < _guide.CurrentIndex;
            var isCurrent = index == _guide.CurrentIndex;

            var title = new TextBlock
            {
                Text = $"{(isDone ? "✓ " : "")}{index + 1} {step.Title}",
                FontWeight = isCurrent ? FontWeights.Medium : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
            };
            var state = new TextBlock
            {
                Text = StepStateText(index, step),
                FontSize = 11,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
            };
            _stepStateBlocks.Add(state);
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(title);
            panel.Children.Add(state);

            var chip = new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(8),
                Background = ResourceBrush("LayerFillColorDefaultBrush"),
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
                BorderBrush = isCurrent
                    ? ResourceBrush("AccentFillColorDefaultBrush")
                    : ResourceBrush("ControlStrokeColorDefaultBrush"),
                Child = panel,
            };
            Grid.SetColumn(chip, index);
            StepBar.Children.Add(chip);
        }
    }

    private string StepStateText(int index, CaptureGuideStep step)
    {
        var entry = _steps.LastOrDefault(s => s.StepId == step.Id);
        // 当前步骤还没点过「完成」时没有条目，窗口起点用 _windowStartCount，这样数字从一开就跟着涨
        var start = entry?.CountAtStart ?? (index == _guide.CurrentIndex ? _windowStartCount : (int?)null);
        var done = entry?.CountAtDone ?? _active?.RecordCount;
        var count = start is { } from && done is { } to ? Strings.Current.Format("S_60162d6a9a", Math.Max(0, to - from)) : "";

        if (index < _guide.CurrentIndex) return Strings.Current.Get("S_e99b48a29b") + count;
        if (index == _guide.CurrentIndex) return Strings.Current.Get("S_6f1972e48e") + count;
        return Strings.Current.Get("S_062e5e670f");
    }

    private static Brush? ResourceBrush(string key)
    {
        try
        {
            return Application.Current.Resources[key] as Brush;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>空闲态「接口 / 预检」两栏：窄窗口上下堆叠，接口在上。</summary>
    private void InterfaceProbeGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (InterfaceProbeGrid == null || InterfaceColumn == null || ProbeColumn == null) return;
        var narrow = e.NewSize.Width < 900;
        if (narrow == _idleNarrow && InterfaceProbeGrid.ColumnDefinitions.Count > 0) return;
        _idleNarrow = narrow;

        InterfaceProbeGrid.ColumnDefinitions.Clear();
        InterfaceProbeGrid.RowDefinitions.Clear();
        InterfaceProbeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (narrow)
        {
            InterfaceProbeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            InterfaceProbeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(InterfaceColumn, 0);
            Grid.SetRow(InterfaceColumn, 0);
            Grid.SetColumn(ProbeColumn, 0);
            Grid.SetRow(ProbeColumn, 1);
        }
        else
        {
            InterfaceProbeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            InterfaceProbeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            Grid.SetColumn(InterfaceColumn, 0);
            Grid.SetRow(InterfaceColumn, 0);
            Grid.SetColumn(ProbeColumn, 1);
            Grid.SetRow(ProbeColumn, 0);
        }
    }

    /// <summary>窗口窄的时候退回单列：引导在上、记录在下，两列并排会被挤扁。</summary>
    private void RunningPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (RunningContent == null || GuideColumn == null || RecordCard == null) return;
        var narrow = e.NewSize.Width < 900;
        if (narrow == _runningNarrow && RunningContent.ColumnDefinitions.Count > 0) return;
        _runningNarrow = narrow;

        RunningContent.ColumnDefinitions.Clear();
        RunningContent.RowDefinitions.Clear();
        if (narrow)
        {
            RunningContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RunningContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RunningContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(GuideColumn, 0);
            Grid.SetRow(GuideColumn, 0);
            Grid.SetColumn(RecordCard, 0);
            Grid.SetRow(RecordCard, 1);
        }
        else
        {
            RunningContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RunningContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RunningContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(GuideColumn, 0);
            Grid.SetRow(GuideColumn, 0);
            Grid.SetColumn(RecordCard, 1);
            Grid.SetRow(RecordCard, 0);
        }
    }

    private void UpdateGuideProgress()
    {
        if (GuideProgress == null) return;
        var done = CompletedStepCount();
        var open = _steps.LastOrDefault(s => s.CountAtDone == null);
        var start = open?.CountAtStart ?? _windowStartCount;
        var live = _active != null ? Math.Max(0, _active.RecordCount - start) : 0;
        GuideProgress.Text = _active is { State: CaptureSessionState.Running or CaptureSessionState.Paused }
            ? Strings.Current.Format("S_054598c095", done, live)
            : Strings.Current.Format("S_4917bcf0d6", done);
        // 步骤条上的每段条数跟着一起涨，否则只有底部这行在动
        for (var i = 0; i < _stepStateBlocks.Count && i < _guide.Steps.Count; i++)
            _stepStateBlocks[i].Text = StepStateText(i, _guide.Steps[i]);
    }

    /// <summary>已完成步数 = 引导的当前下标：重做不虚增，点「上一步」如实回退，也不会超过步骤总数。</summary>
    private int CompletedStepCount() => _guide.CurrentIndex;

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || StopButton == null) return;
        StopButton.IsEnabled = false;
        try
        {
            var session = _active;
            var bundle = await session.StopAsync();
            _lastSummary = IssueSummaryBuilder.Build(bundle, session.PackagePath);
            // 最后一步的窗口在会话结束时才封口，否则它会永远显示「记录中」
            CloseOpenStep();
            _lastSession = session;
            _active = null;
            SetRunningState(false);
            CounterText.Text = Strings.Current.Format("S_08c09bb5e4", bundle.Analysis.TotalRecords, bundle.Analysis.Groups.Count);
            ResultText.Text = BuildResultText(bundle, session) + BuildEmptyHint(bundle);
            SetResultWarning(bundle);
            OpenReportButton.IsEnabled = File.Exists(ReportPath(session));
            OpenFolderButton.IsEnabled = Directory.Exists(session.Directory);
            SaveAsButton.IsEnabled = File.Exists(session.PackagePath);
            CopySummaryButton.IsEnabled = true;
            var hasStatic = bundle.Analysis.StaticGroups.Count > 0;
            ShowMessage(hasStatic
                    ? Strings.Current.Get("S_0ddf9babdb")
                    : Strings.Current.Get("S_65bd0c20f1"),
                hasStatic ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            ResultCard?.StartBringIntoView();
            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            Log.Error("停止采集失败", ex);
            ShowMessage(Strings.Current.Format("S_05a3b0e45d", ex.Message), InfoBarSeverity.Error);
            StopButton.IsEnabled = true;
        }
    }

    private CaptureSession? CurrentSession => _lastSession ?? _active;

    /// <summary>放弃：停采集、删会话目录、不出报告。确认框默认落在「继续采集」上，防手滑。</summary>
    private async void Abandon_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || AbandonButton == null) return;
        var session = _active;
        var count = session.RecordCount;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Current.Get("S_1a8b7de528"),
            Content = Strings.Current.Format("S_e73c57dd5f", count)
                      + Strings.Current.Get("S_6f34fa28fc"),
            PrimaryButtonText = Strings.Current.Get("S_980d83641f"),
            CloseButtonText = Strings.Current.Get("S_d388395729"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        StopButton.IsEnabled = false;
        AbandonButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        try
        {
            var bundle = await session.AbortAsync();
            _active = null;
            _lastSession = null;
            _lastSummary = "";
            SetRunningState(false);
            ResetGuide();
            OpenReportButton.IsEnabled = File.Exists(ReportPath(session));
            OpenFolderButton.IsEnabled = Directory.Exists(session.Directory);
            SaveAsButton.IsEnabled = File.Exists(session.PackagePath);
            CopySummaryButton.IsEnabled = false;
            if (ResultWarn != null)
            {
                // 放弃时不出报告，上一次会话的静态通道提示不能留着误导
                ResultWarn.Text = "";
                ResultWarn.Visibility = Visibility.Collapsed;
            }

            if (session.RecordsDeleted)
            {
                CounterText.Text = Strings.Current.Format("S_0340e01c89", bundle.Analysis.TotalRecords);
                ResultText.Text = Strings.Current.Get("S_79d60a2ba0");
                ShowMessage(Strings.Current.Get("S_3a46080ca1"), InfoBarSeverity.Informational);
            }
            else
            {
                // 删不掉就如实说，别让用户以为记录没了
                CounterText.Text = Strings.Current.Format("S_bb2107d305", bundle.Analysis.TotalRecords);
                ResultText.Text = Strings.Current.Format("S_f384754e66", session.Directory);
                ShowMessage(Strings.Current.Get("S_075fa94b99"), InfoBarSeverity.Warning);
            }
            ResultCard?.StartBringIntoView();
            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            Log.Error("放弃采集失败", ex);
            ShowMessage(Strings.Current.Format("S_8a99fa77b9", ex.Message), InfoBarSeverity.Error);
            StopButton.IsEnabled = true;
            AbandonButton.IsEnabled = true;
            PauseButton.IsEnabled = true;
        }
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentSession is not { } session) return;
        OpenWithShell(ReportPath(session));
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentSession is not { } session) return;
        OpenWithShell(session.Directory);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentSession?.PackagePath is not { } source || !File.Exists(source)) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = "vigilwatt-capture",
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeChoices.Add(Strings.Current.Get("S_2479eda92b"), [".zip"]);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Services.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            File.Copy(source, file.Path, overwrite: true);
            ShowMessage(Strings.Current.Format("S_6a051bf65c", file.Path), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            // 非打包 WinUI 下选择器可能不可用；退回到「复制路径 + 打开文件夹」，绝不静默失败。
            Log.Warn($"另存诊断包失败: {ex.Message}");
            CopyToClipboard(source);
            ShowMessage(Strings.Current.Format("S_26b841314b", source), InfoBarSeverity.Warning);
        }
    }

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastSummary)) return;
        CopyToClipboard(_lastSummary);
        ShowMessage(Strings.Current.Get("S_7125dc174d"), InfoBarSeverity.Success);
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CaptureHistoryRow row) return;
        if (!row.HasReport)
        {
            // 会话没收尾（应用在采集途中被关掉）：至少把原始记录所在目录打开，别让人以为记录丢了
            OpenWithShell(row.Directory);
            ShowMessage(Strings.Current.Get("S_5d2fdf1b0b"), InfoBarSeverity.Warning);
            await Task.CompletedTask;
            return;
        }
        OpenWithShell(row.ReportPath);
        await Task.CompletedTask;
    }

    private async Task RefreshHistoryAsync()
    {
        if (HistoryList == null || HistoryHint == null) return;
        try
        {
            var store = new CaptureStore();
            var sessions = await Task.Run(store.ListSessions);
            HistoryList.ItemsSource = sessions.Select(s => new CaptureHistoryRow
            {
                Title = s.DisplayName,
                Subtitle = Strings.Current.Format("S_9de1c6a91e", s.StartedUtc.ToLocalTime(), s.RecordCount, s.ErrorCount)
                           + (s.HasReport ? "" : Strings.Current.Get("S_e661dfeb53")),
                Directory = s.Directory,
                ReportPath = CaptureStore.PathOf(s.Directory, "report.html"),
                HasReport = s.HasReport,
            }).ToList();
            HistoryHint.Text = sessions.Count == 0
                ? Strings.Current.Get("S_f3816a36e6")
                : Strings.Current.Format("S_7b7cd5cdeb", sessions.Count, store.Root);
        }
        catch (Exception ex)
        {
            Log.Warn($"读取采集历史失败: {ex.Message}");
        }
    }

    private void RefreshRecords()
    {
        if (RecordList == null || CounterText == null) return;
        if (_active == null)
        {
            // 停止/放弃后把这行清掉，否则会留着上一次的「已收到 N 条」
            UpdateWaitHint();
            return;
        }

        // 只追加新记录、从头部裁掉超出上限的，避免每拍重建整张表造成闪烁
        var slice = _active.Slice(_renderedCount, MaxRenderedRecords);
        if (slice.Count > 0)
        {
            _firstRecordMono ??= slice[0].TimestampMono;
            foreach (var record in slice)
            {
                _recordRows.Add(new CaptureRecordRow
                {
                    Time = record.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"),
                    Kind = (record.Status == CaptureRecordStatus.Sent ? "→ " : "")
                           + record.RecordType.Replace("hid_", "")
                           + (record.ReportId is { } id ? $" 0x{id:X2}" : ""),
                    Hex = record.DataHex,
                    Note = record.Status switch
                    {
                        CaptureRecordStatus.Received => $"{record.Length}B",
                        CaptureRecordStatus.Sent => Strings.Current.Get("S_3d391baa4f"),
                        _ => Strings.Current.Get("S_3e3c8068bb"),
                    },
                    Accent = ChannelBrush(record),
                });
            }
            _renderedCount = _active.RecordCount;
            while (_recordRows.Count > MaxRenderedRecords) _recordRows.RemoveAt(0);
        }

        CounterText.Text = _active.State switch
        {
            CaptureSessionState.Running => Strings.Current.Format("S_46a179caea", _active.RecordCount, _active.ErrorCount)
                                           + GuideStateSuffix(),
            CaptureSessionState.Paused => Strings.Current.Format("S_7efc52c500", _active.RecordCount),
            CaptureSessionState.Failed => Strings.Current.Format("S_9429da1a0f", _active.RecordCount),
            _ => Strings.Current.Format("S_7acaf5733c", _active.RecordCount),
        };
        UpdateGuideProgress();
        UpdateWaitHint();
    }

    /// <summary>同一「类型 + report ID」始终同色；新通道按出现顺序取调色板的下一个颜色。</summary>
    private Brush ChannelBrush(CaptureRecord record)
    {
        var key = $"{record.RecordType}|{record.ReportId?.ToString() ?? "-"}";
        if (_channelColors.TryGetValue(key, out var brush)) return brush;
        brush = new SolidColorBrush(UiPalette.SeriesColors[_nextChannelColor++ % UiPalette.SeriesColors.Length]);
        _channelColors[key] = brush;
        return brush;
    }

    /// <summary>发帧卡里的说明：长度/report ID 规则 + 帧从哪来 + 风险。用户问过「这种是怎么做的」，所以写全。</summary>
    private void UpdateSendHint()
    {
        if (SendHint == null || _selected is not { } device) return;
        var ids = device.OutputReportIds.Count > 0
            ? Strings.Current.Format("S_19e63d7d3a", string.Join("、", device.OutputReportIds.Select(i => $"0x{i:X2}")))
            : Strings.Current.Get("S_54981e1871");
        SendHint.Text = Strings.Current.Format("S_5a2b54e36a", device.OutputReportLength, ids)
                        + Strings.Current.Get("S_34643010e4")
                        + Strings.Current.Get("S_2e1b54f66d")
                        + Strings.Current.Get("S_a3c83f9b47");
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { } session || SendBox == null || SendButton == null) return;
        if (!CaptureHex.TryParse(SendBox.Text, out var bytes, out var error))
        {
            ShowMessage(Strings.Current.Format("S_8bd7b4e1fc", error), InfoBarSeverity.Error);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Current.Get("S_3a8142c71f"),
            Content = Strings.Current.Format("S_52f8f1cd34", bytes.Length, CaptureHex.Format(bytes))
                      + Strings.Current.Get("S_4cd3698d30"),
            PrimaryButtonText = Strings.Current.Get("S_1214d633a4"),
            CloseButtonText = Strings.Current.Get("S_4d0b4688c7"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SendButton.IsEnabled = false;
        try
        {
            if (session.TrySend(bytes, out var sendError))
            {
                ShowMessage(Strings.Current.Format("S_9329f06a0f", bytes.Length), InfoBarSeverity.Success);
                SendBox.Text = "";
            }
            else
            {
                ShowMessage(Strings.Current.Format("S_b6e4f1c1dd", sendError), InfoBarSeverity.Error);
            }
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    /// <summary>记录卡顶部那行：在等什么、等了多久、下一步该做什么——免得进来一两秒没报文就退掉。</summary>
    private void UpdateWaitHint()
    {
        if (WaitHint == null) return;
        if (_active == null)
        {
            WaitHint.Text = "";
            return;
        }

        if (_active.State == CaptureSessionState.Paused)
        {
            WaitHint.Text = Strings.Current.Get("S_fe8f51cbbc");
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Offline);
            return;
        }

        var count = _active.RecordCount;
        if (count > 0)
        {
            WaitHint.Text = _firstRecordMono is { } first
                ? Strings.Current.Format("S_2f1368aa8d", count, first / 1000.0)
                : Strings.Current.Format("S_de96530a94", count);
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Good);
            return;
        }

        // 设备差异太大（雷柏醒着 3 秒一帧、睡着静默；迈从要先开会话），所以给分档和动作，不给固定数字
        var seconds = _active.ElapsedMs / 1000.0;
        if (seconds < 5)
        {
            WaitHint.Text = Strings.Current.Get("S_3ff8c35b3e");
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Offline);
        }
        else if (seconds < 15)
        {
            WaitHint.Text = Strings.Current.Format("S_b25ec8e46f", seconds);
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Mid);
        }
        else
        {
            WaitHint.Text = Strings.Current.Format("S_8db3e34776", seconds);
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Low);
        }
    }

    private string GuideStateSuffix()
        => _guide.IsComplete ? Strings.Current.Get("S_7741f9cc14") : Strings.Current.Format("S_669814cbab", _guide.CurrentIndex + 1, _guide.Steps.Count);

    private void ResetRecordRows()
    {
        _recordRows.Clear();
        _renderedCount = 0;
    }

    private void SetRunningState(bool running)
    {
        if (IdlePanel != null) IdlePanel.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        if (RunningPanel != null) RunningPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        StartButton.IsEnabled = !running;
        PauseButton.IsEnabled = running;
        StopButton.IsEnabled = running;
        if (AbandonButton != null) AbandonButton.IsEnabled = running;
        if (SendCard != null)
        {
            SendCard.Visibility = running && _active?.CanSend == true ? Visibility.Visible : Visibility.Collapsed;
            if (SendCard.Visibility == Visibility.Visible) UpdateSendHint();
        }
        MarkBox.IsEnabled = running;
        MarkButton.IsEnabled = running;
        BatteryBox.IsEnabled = running;
        BatteryButton.IsEnabled = running;
        RefreshButton.IsEnabled = !running;
        ProbeButton.IsEnabled = !running && _selected != null;
        DeviceList.IsEnabled = !running;
        InterfaceList.IsEnabled = !running && _selectedGroup != null;
        TaskUnknown.IsEnabled = !running;
        TaskSupported.IsEnabled = !running;
        if (!running && PauseLabel != null) PauseLabel.Text = Strings.Current.Get("S_130448bce6");
        UpdateGuideUi();
    }

    private void ClearSelection()
    {
        _selectedGroup = null;
        _selected = null;
        _capabilities = [];
        if (InterfaceList != null)
        {
            InterfaceList.ItemsSource = null;
            InterfaceList.IsEnabled = false;
        }
        if (CapabilityList != null) CapabilityList.ItemsSource = null;
        _capabilityRowsAll = [];
        _capabilityRowsAttention = [];
        if (CapabilitySummary != null) CapabilitySummary.Text = Strings.Current.Get("S_ee21f07e93");
        if (ShowAllCapsCheck != null)
        {
            ShowAllCapsCheck.IsEnabled = false;
            ShowAllCapsCheck.IsChecked = false;
        }
        if (ProbeButton != null) ProbeButton.IsEnabled = false;
        if (StartButton != null) StartButton.IsEnabled = false;
        if (FeatureCheck != null)
        {
            FeatureCheck.IsEnabled = false;
            FeatureCheck.IsChecked = false;
        }
        if (FeatureIdBox != null) FeatureIdBox.ItemsSource = null;
    }

    private IReadOnlyList<byte> ReadFeatureIds(CaptureDeviceInfo device)
    {
        if (FeatureCheck.IsChecked != true || device.FeatureReportIds.Count == 0) return [];
        var index = FeatureIdBox.SelectedIndex;
        if (index < 0 || index >= device.FeatureReportIds.Count) return [];
        return [device.FeatureReportIds[index]];
    }

    /// <summary>运行态常驻横幅：选择卡收起后，这里是唯一还能确认「采的是哪台、哪个接口」的地方。</summary>
    private void UpdateDeviceBanner()
    {
        if (DeviceBanner == null || DeviceBannerName == null || DeviceBannerMeta == null) return;
        if (_selected is not { } device) return;

        // 很多 dongle 没有产品名，这时退回 VID/PID，不能留空白
        DeviceBannerName.Text = string.IsNullOrWhiteSpace(device.Product) ? device.VidPid : device.Product;
        var claimed = _selectedGroup?.ClaimedBy;
        DeviceBannerMeta.Text = $"{device.VidPid} · {CaptureDeviceCatalog.ShortInterface(device.InterfaceKey)} · "
                                + (string.IsNullOrEmpty(claimed) ? Strings.Current.Get("S_e3d46eadfc") : Strings.Current.Format("S_7656b7e221", claimed));
        // 完整路径太长，只放悬停提示
        ToolTipService.SetToolTip(DeviceBanner,
            $"{device.InterfacePath}\nusage 0x{device.UsagePage:X4} / 0x{device.Usage:X4}");
    }

    private static CaptureDeviceRow ToDeviceRow(CaptureDeviceGroup group)
    {
        var recommended = group.Recommended;
        var subtitle = string.IsNullOrWhiteSpace(group.Manufacturer)
            ? group.VidPid
            : $"{group.Manufacturer} · {group.VidPid}";
        subtitle += Strings.Current.Format("S_6dacf0b9f8", group.Interfaces.Count);
        if (group.Interfaces.All(i => i.AccessDenied)) subtitle += Strings.Current.Get("S_69c48e1bff");
        else if (recommended == null) subtitle += Strings.Current.Get("S_d5722df606");
        else subtitle += Strings.Current.Format("S_7d0074d29a", CaptureDeviceCatalog.ShortInterface(recommended.InterfaceKey));
        return new CaptureDeviceRow
        {
            Title = group.Title,
            Subtitle = subtitle,
            Status = group.IsAdapted ? Strings.Current.Format("S_3ded5f5415", group.ClaimedBy) : Strings.Current.Get("S_e3d46eadfc"),
            Group = group,
        };
    }

    private static CaptureCapabilityRow BuildCapabilityRow(CapabilityItem cap)
    {
        var detail = cap.Status == CapabilityStatus.Available
            ? cap.Evidence ?? ""
            : Strings.Current.Format("S_f1462717e0", cap.Reason, cap.Impact, cap.NextStep);
        return new CaptureCapabilityRow
        {
            Title = cap.Title,
            Status = cap.Status.Label(),
            Detail = string.IsNullOrWhiteSpace(detail) ? "—" : detail,
        };
    }

    private static string BuildResultText(CaptureBundle bundle, CaptureSession session)
    {
        var analysis = bundle.Analysis;
        var text = Strings.Current.Format("S_909e63d7f6", analysis.ReceivedRecords, analysis.FailedRecords)
                   + Strings.Current.Format("S_a6a9112c31", analysis.Groups.Count, analysis.Changes.Count)
                   + Strings.Current.Format("S_55c1b91e9d", analysis.TotalErrors, (bundle.Session.DurationMs ?? 0) / 1000.0);
        if (bundle.Session.RecordsTruncated) text += Strings.Current.Get("S_0abff1d67c");
        // 结果卡也要写清设备：截图/贴摘要时得知道这是哪台的结论
        if (bundle.Device is { } device)
        {
            var name = string.IsNullOrWhiteSpace(device.Product) ? device.VidPid : device.Product;
            text += Strings.Current.Format("S_81ef4351b2", name, device.VidPid, CaptureDeviceCatalog.ShortInterface(device.InterfaceKey));
        }
        return text + Strings.Current.Format("S_9e5439c2eb", session.Directory);
    }

    /// <summary>零报文时给出下一步，而不是让用户以为设备没协议。</summary>
    /// <summary>整场没变的分组直接写在结果卡上：用户会看这里，报告里的结论他不一定翻。</summary>
    private void SetResultWarning(CaptureBundle bundle)
    {
        if (ResultWarn == null) return;
        if (bundle.Analysis.StaticGroups.Count == 0)
        {
            ResultWarn.Text = "";
            ResultWarn.Visibility = Visibility.Collapsed;
            return;
        }

        ResultWarn.Text = Strings.Current.Format("S_020484ddab", bundle.Analysis.StaticGroupText)
                          + Strings.Current.Get("S_f73b3cac81");
        ResultWarn.Foreground = UiPalette.Brush(UiPalette.Low);
        ResultWarn.Visibility = Visibility.Visible;
    }

    private string BuildEmptyHint(CaptureBundle bundle)
    {
        if (bundle.Analysis.ReceivedRecords > 0) return "";
        var recommended = _selectedGroup?.Recommended;
        var hint = Strings.Current.Get("S_f072b46239");
        if (_selected is { InputReportLength: 0 } && _selected.AccessDenied == false)
            hint += Strings.Current.Get("S_f8d8acd288");
        else if (recommended != null && !ReferenceEquals(recommended, _selected))
            hint += Strings.Current.Format("S_e7ce6cb264", CaptureDeviceCatalog.ShortInterface(recommended.InterfaceKey))
                    + Strings.Current.Get("S_79b7d43de7");
        else
            hint += Strings.Current.Get("S_01ee19eea6");
        return hint;
    }

    private static string ReportPath(CaptureSession session) => CaptureStore.PathOf(session.Directory, "report.html");

    private void ShowMessage(string text, InfoBarSeverity severity)
    {
        if (MessageBar == null) return;
        MessageBar.Message = text;
        MessageBar.Severity = severity;
        MessageBar.IsOpen = true;
    }

    private static void OpenWithShell(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"打开 {path} 失败: {ex.Message}");
        }
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            Log.Warn($"写剪贴板失败: {ex.Message}");
        }
    }

    /// <summary>一步的采集窗口：从上一条标记（或会话开始）到点「完成这一步」为止。</summary>
    private sealed class GuideStepProgress
    {
        public required string StepId { get; init; }

        public required string Title { get; init; }

        /// <summary>本步开始时的记录总数。</summary>
        public required int CountAtStart { get; init; }

        /// <summary>该步骤结束时的记录总数；null = 还在进行中。</summary>
        public int? CountAtDone { get; set; }
    }
}
