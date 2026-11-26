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
            ShowMessage("采集进行中，先停止再刷新设备。", InfoBarSeverity.Warning);
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
            ShowMessage($"枚举设备失败：{ex.Message}", InfoBarSeverity.Error);
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
            ? "只列出 VigilWatt 插件正在认领的设备，用来排查「读不到 / 读数不对」。采集期间插件仍在轮询同一接口。"
            : "列出系统里全部 HID 设备，未适配的排在前面。采集方式固定为 USB/HID，程序不会自动改用蓝牙或厂商软件。";

        if (groups.Count == 0)
        {
            DeviceHint.Text = "没有枚举到 HID 接口。确认设备已插好，或换个 USB 口再刷新。";
            return;
        }

        DeviceHint.Text = CurrentTask == CaptureTaskKind.SupportedDeviceDiagnosis && filtered.Count == 0
            ? "没有插件正在认领任何接口：可能插件都关着，或设备没插。改选「未知设备调查」可以看到全部设备。"
            : $"共 {groups.Count} 台设备、{groups.Sum(g => g.Interfaces.Count)} 个接口，当前显示 {filtered.Count} 台。"
              + "选中一台后，到第 2 步挑接口。";
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
                    + (ReferenceEquals(device, recommended) ? "（建议先试）" : ""),
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
            InterfaceHint.Text = "这台设备没有可打开的接口（全部被系统保护）。换一台设备试试。";
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
            return "这个接口打不开（系统保护 collection），采不到报文。改选同一台设备的厂商定义接口。";
        if (!device.IsVendorDefined)
        {
            var kind = device.UsagePage switch
            {
                0x000C => "消费类（媒体键）",
                0x0001 => "键鼠",
                0x000D => "数字化仪",
                _ => "标准 HID",
            };
            return $"这个接口是标准 HID 用途页（{kind}），只能读到标准输入报文，不会出现厂商电量协议。"
                   + "如果这台设备在系统里没有 0xFF00 段的厂商接口，就说明它不通过 HID 暴露状态数据。";
        }
        if (device.InputReportLength > 0)
            return "这个接口有输入通道，被动监听就能采到报文。下一步执行预检。";
        if (device.FeatureReportLength > 0)
            return "这个接口只有 Feature 通道：要勾选「读取 Feature report」并选一个 report ID，否则不会有数据。"
                   + "这类通道常见是配置/握手用途，勾了也可能一直返回同一帧——电量这类会变的量通常在输入通道上。";
        return "这个接口既没有输入也没有 Feature 通道，采不到东西。换同一台设备的其他接口。";
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
                    ? "勾选后本会话的句柄会申请写权限，采集页出现「手动发帧」。只有明确知道要发什么时才用——发错帧可能让设备进 bootloader。"
                    : "该接口没有输出通道（caps 里 out=0），无法发帧。";
            ShowMessage(device.AccessDenied
                ? "预检完成：该接口被系统保护，只能看到元信息。"
                : "预检完成。确认无误后可以开始采集。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Warn($"采集预检失败: {ex.Message}");
            ShowMessage($"预检失败：{ex.Message}", InfoBarSeverity.Error);
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
        if (_capabilities.Count == 0) return "预检后这里显示「几项可用、几项要处理」。";
        var counts = _capabilities
            .GroupBy(c => c.Status)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key.Label()} {g.Count()}");
        var head = $"预检 {_capabilities.Count} 项 · {string.Join(" · ", counts)}";
        if (showAll) return head;
        return _capabilityRowsAttention.Count == 0
            ? head + "。全部可用。"
            : head + $"。下面只列需要你注意的 {_capabilityRowsAttention.Count} 项。";
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
            ShowMessage("已经有一个采集会话，先停止它。", InfoBarSeverity.Warning);
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
            if (BatteryStatus != null) BatteryStatus.Text = "尚未记录电量参照。";
            SetRunningState(true);
            RefreshRecords();
            ShowMessage(featureIds.Count > 0
                ? $"开始采集（含 Feature 读取 {string.Join("、", featureIds.Select(i => $"0x{i:X2}"))}）。"
                : "开始采集（仅被动监听输入报告）。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Log.Error("创建采集会话失败", ex);
            ShowMessage($"无法开始采集：{ex.Message}", InfoBarSeverity.Error);
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
                if (PauseLabel != null) PauseLabel.Text = "继续";
                ShowMessage("已暂停：暂停期间不读取设备，这段时间没有记录不代表没有数据。", InfoBarSeverity.Warning);
            }
            else if (_active.State == CaptureSessionState.Paused)
            {
                _active.Resume();
                if (PauseLabel != null) PauseLabel.Text = "暂停";
                ShowMessage("已继续采集。", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"暂停/继续失败: {ex.Message}");
            ShowMessage($"操作失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>在记录列表里插一条分割行：后面这批报文属于哪一步，一眼能对上。</summary>
    private void AddStepHeader(string text) => _recordRows.Add(CaptureRecordRow.StepHeader(text));

    /// <summary>进入下一步（或全部走完）时插分割行——步骤窗口从上一条标记算起，边界就在点「完成」这一刻。</summary>
    private void AddStepHeaderForCurrent()
    {
        if (_guide.Current is { } next)
            AddStepHeader($"第 {_guide.CurrentIndex + 1} 步 · {next.Title}");
        else
            AddStepHeader("步骤已完成 · 以下是清单外的记录");
    }

    private void Mark_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || MarkBox == null) return;
        var note = MarkBox.Text.Trim();
        _active.Mark(note);
        MarkBox.Text = "";
        AddStepHeader(string.IsNullOrEmpty(note) ? "自定义标记" : $"自定义标记 · {note}");
        // 标记的语义是「这一段到此结束」，所以文案要说清它划的是分界而不是起点
        ShowMessage("已标记：这一段到此结束，之后的报文归下一段。", InfoBarSeverity.Informational);
    }

    /// <summary>用户从官方软件/设备屏幕读到的电量，作为核对参照。空或 0 = 不使用（不是输入错误）。</summary>
    private void Battery_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || BatteryBox == null || BatteryStatus == null) return;

        var text = BatteryBox.Text.Trim();
        if (text.Length == 0 || text == "0")
        {
            BatteryStatus.Text = "未记录：留空或 0 视为不使用电量核对。";
            ShowMessage("没有记录电量（留空或 0 表示不使用）。", InfoBarSeverity.Informational);
            return;
        }

        if (!int.TryParse(text, out var percent) || percent is < 1 or > 100)
        {
            BatteryStatus.Text = "输入无效，没有记录。请填 1–100 的整数，或留空/填 0 表示不使用。";
            ShowMessage("电量要填 1–100 的整数。", InfoBarSeverity.Warning);
            return;
        }

        if (!_active.MarkBattery(percent))
        {
            BatteryStatus.Text = "当前不在采集状态，没有记录。";
            ShowMessage("当前不在采集状态，记不了电量。", InfoBarSeverity.Warning);
            return;
        }

        // 输入框**不清空**：留着用户才能确认自己填的是多少
        BatteryStatus.Text = $"已记录 {percent}%（{DateTime.Now:HH:mm:ss}）。改完数值再点一次可再记一条。";
        ShowMessage($"已记录电量参照 {percent}%，报告里会拿它和报文比对。", InfoBarSeverity.Success);
    }

    private void GuideNext_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || _guide.Current is not { } step) return;
        CompleteStep(step);
        _guide.Advance();
        AddStepHeaderForCurrent();
        UpdateGuideUi();
        ShowMessage(_guide.IsComplete
            ? "全部步骤已完成，可以停止并生成报告。"
            : $"已记录「{step.Title}」，接下来：{_guide.Current!.Title}", InfoBarSeverity.Success);
    }

    private void GuideRedo_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || _guide.Current is not { } step) return;
        RedoStep(step);
        UpdateGuideUi();
        ShowMessage($"重新记录「{step.Title}」。", InfoBarSeverity.Informational);
    }

    private void GuideBack_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null || !_guide.Back()) return;
        UpdateGuideUi();
        ShowMessage($"回到「{_guide.Current!.Title}」，可以重做。", InfoBarSeverity.Informational);
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
                ? "开始采集后，这里会一步步告诉你怎么操作设备"
                : "采集已停止";
            GuideHint.Text = "每一步做一件事，做完点「完成这一步」；程序会自动给这一步打标记，后面才能对照出差异。";
        }
        else if (_guide.IsComplete)
        {
            GuideTitle.Text = "全部步骤已完成";
            GuideHint.Text = "可以点「停止并生成报告」。如果还想试别的操作，用下面的自定义标记。";
        }
        else
        {
            GuideTitle.Text = $"第 {_guide.CurrentIndex + 1} / {_guide.Steps.Count} 步 · {_guide.Current!.Title}";
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
        var count = start is { } from && done is { } to ? $" · {Math.Max(0, to - from)} 条" : "";

        if (index < _guide.CurrentIndex) return "已完成" + count;
        if (index == _guide.CurrentIndex) return "进行中" + count;
        return "未开始";
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
            ? $"已完成 {done} 步 · 本步已采到 {live} 条报文"
            : $"共完成 {done} 步";
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
            CounterText.Text = $"已停止 · 记录 {bundle.Analysis.TotalRecords} 条 · 分组 {bundle.Analysis.Groups.Count} 个";
            ResultText.Text = BuildResultText(bundle, session) + BuildEmptyHint(bundle);
            SetResultWarning(bundle);
            OpenReportButton.IsEnabled = File.Exists(ReportPath(session));
            OpenFolderButton.IsEnabled = Directory.Exists(session.Directory);
            SaveAsButton.IsEnabled = File.Exists(session.PackagePath);
            CopySummaryButton.IsEnabled = true;
            var hasStatic = bundle.Analysis.StaticGroups.Count > 0;
            ShowMessage(hasStatic
                    ? "采集已停止，但整场没有任何变化——这条通道可能不是状态通道，别据此下结论。"
                    : "采集已停止，报告与诊断包已生成。",
                hasStatic ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            ResultCard?.StartBringIntoView();
            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            Log.Error("停止采集失败", ex);
            ShowMessage($"停止采集时出错：{ex.Message}", InfoBarSeverity.Error);
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
            Title = "放弃这次采集？",
            Content = $"已采到的 {count} 条原始记录会连同会话目录一起删除，不会生成报告和诊断包。\n\n"
                      + "这个操作不能撤销。想留下证据请用「停止并生成报告」。",
            PrimaryButtonText = "放弃并删除",
            CloseButtonText = "继续采集",
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
                CounterText.Text = $"已放弃 · 删除 {bundle.Analysis.TotalRecords} 条记录";
                ResultText.Text = "已放弃本次采集：记录与会话目录已删除，没有生成报告。";
                ShowMessage("已放弃本次采集，记录已删除。", InfoBarSeverity.Informational);
            }
            else
            {
                // 删不掉就如实说，别让用户以为记录没了
                CounterText.Text = $"已放弃 · {bundle.Analysis.TotalRecords} 条记录未能删除";
                ResultText.Text = $"已放弃本次采集，但记录没能删掉（文件被占用）：\n{session.Directory}\n可以手动删除这个目录。";
                ShowMessage("已放弃，但记录没能删除，位置写在结果里。", InfoBarSeverity.Warning);
            }
            ResultCard?.StartBringIntoView();
            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            Log.Error("放弃采集失败", ex);
            ShowMessage($"放弃采集时出错：{ex.Message}", InfoBarSeverity.Error);
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
            picker.FileTypeChoices.Add("ZIP 诊断包", [".zip"]);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Services.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            File.Copy(source, file.Path, overwrite: true);
            ShowMessage($"已另存到 {file.Path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            // 非打包 WinUI 下选择器可能不可用；退回到「复制路径 + 打开文件夹」，绝不静默失败。
            Log.Warn($"另存诊断包失败: {ex.Message}");
            CopyToClipboard(source);
            ShowMessage($"系统保存对话框不可用，已把路径复制到剪贴板：{source}", InfoBarSeverity.Warning);
        }
    }

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastSummary)) return;
        CopyToClipboard(_lastSummary);
        ShowMessage("调查摘要已复制，可直接贴到 Issue 或对话里。", InfoBarSeverity.Success);
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not CaptureHistoryRow row) return;
        if (!row.HasReport)
        {
            // 会话没收尾（应用在采集途中被关掉）：至少把原始记录所在目录打开，别让人以为记录丢了
            OpenWithShell(row.Directory);
            ShowMessage("这条会话没有收尾（应用在采集途中被关闭），没有生成报告；已打开原始记录目录。", InfoBarSeverity.Warning);
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
                Subtitle = $"{s.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {s.RecordCount} 条记录 · {s.ErrorCount} 条错误"
                           + (s.HasReport ? "" : " · 未生成报告（会话没收尾）"),
                Directory = s.Directory,
                ReportPath = CaptureStore.PathOf(s.Directory, "report.html"),
                HasReport = s.HasReport,
            }).ToList();
            HistoryHint.Text = sessions.Count == 0
                ? "还没有保存过采集。"
                : $"共 {sessions.Count} 条，单击打开报告。位置：{store.Root}";
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
                        CaptureRecordStatus.Sent => "发出",
                        _ => "失败",
                    },
                    Accent = ChannelBrush(record),
                });
            }
            _renderedCount = _active.RecordCount;
            while (_recordRows.Count > MaxRenderedRecords) _recordRows.RemoveAt(0);
        }

        CounterText.Text = _active.State switch
        {
            CaptureSessionState.Running => $"采集中 · 记录 {_active.RecordCount} 条 · 错误 {_active.ErrorCount} 条"
                                           + GuideStateSuffix(),
            CaptureSessionState.Paused => $"已暂停 · 记录 {_active.RecordCount} 条（暂停期间不读取设备）",
            CaptureSessionState.Failed => $"采集已异常结束 · 记录 {_active.RecordCount} 条，已有证据保留",
            _ => $"已停止 · 记录 {_active.RecordCount} 条",
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
            ? "首字节是 report ID（" + string.Join("、", device.OutputReportIds.Select(i => $"0x{i:X2}")) + "）"
            : "该接口不使用 report ID，首字节就是数据";
        SendHint.Text = $"需要 {device.OutputReportLength} 字节，{ids}。\n"
                        + "帧从哪来：USBPcap + Wireshark 抓官方软件的一次操作，抄 out 方向的 payload；"
                        + "或厂商文档 / 开源项目里的请求帧。\n"
                        + "发错帧可能让设备进 bootloader —— 不确定就别发。";
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { } session || SendBox == null || SendButton == null) return;
        if (!CaptureHex.TryParse(SendBox.Text, out var bytes, out var error))
        {
            ShowMessage($"帧格式不对：{error}", InfoBarSeverity.Error);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认发送这一帧？",
            Content = $"将向设备发送 {bytes.Length} 字节：\n\n{CaptureHex.Format(bytes)}\n\n"
                      + "发错帧可能让设备进入 bootloader 或失去响应。确定要继续吗？",
            PrimaryButtonText = "发送",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SendButton.IsEnabled = false;
        try
        {
            if (session.TrySend(bytes, out var sendError))
            {
                ShowMessage($"已发送 {bytes.Length} 字节，记录已写进本次采集。", InfoBarSeverity.Success);
                SendBox.Text = "";
            }
            else
            {
                ShowMessage($"没有发出去：{sendError}", InfoBarSeverity.Error);
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
            WaitHint.Text = "已暂停：期间不读取设备，恢复后继续等";
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Offline);
            return;
        }

        var count = _active.RecordCount;
        if (count > 0)
        {
            WaitHint.Text = _firstRecordMono is { } first
                ? $"已收到 {count} 条 · 首帧在 {first / 1000.0:0.0} 秒"
                : $"已收到 {count} 条";
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Good);
            return;
        }

        // 设备差异太大（雷柏醒着 3 秒一帧、睡着静默；迈从要先开会话），所以给分档和动作，不给固定数字
        var seconds = _active.ElapsedMs / 1000.0;
        if (seconds < 5)
        {
            WaitHint.Text = "正在等第一帧 —— 多数设备 1~5 秒内会推一帧";
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Offline);
        }
        else if (seconds < 15)
        {
            WaitHint.Text = $"已等 {seconds:0} 秒还没收到：动一下设备（按键/移动），或确认接口选对";
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Mid);
        }
        else
        {
            WaitHint.Text = $"已 {seconds:0} 秒无报文：这条通道可能不自发推送，换「有输入通道」的接口重采";
            WaitHint.Foreground = UiPalette.Brush(UiPalette.Low);
        }
    }

    private string GuideStateSuffix()
        => _guide.IsComplete ? " · 步骤已完成" : $" · 步骤 {_guide.CurrentIndex + 1}/{_guide.Steps.Count}";

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
        if (!running && PauseLabel != null) PauseLabel.Text = "暂停";
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
        if (CapabilitySummary != null) CapabilitySummary.Text = "预检后这里显示「几项可用、几项要处理」。";
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
                                + (string.IsNullOrEmpty(claimed) ? "未适配" : $"已适配（{claimed}）");
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
        subtitle += $" · {group.Interfaces.Count} 个接口";
        if (group.Interfaces.All(i => i.AccessDenied)) subtitle += " · 全部打不开（系统保护）";
        else if (recommended == null) subtitle += " · 没有厂商定义接口（只有标准键鼠/消费类）";
        else subtitle += $" · 建议先试 {CaptureDeviceCatalog.ShortInterface(recommended.InterfaceKey)}";
        return new CaptureDeviceRow
        {
            Title = group.Title,
            Subtitle = subtitle,
            Status = group.IsAdapted ? $"已适配（{group.ClaimedBy}）" : "未适配",
            Group = group,
        };
    }

    private static CaptureCapabilityRow BuildCapabilityRow(CapabilityItem cap)
    {
        var detail = cap.Status == CapabilityStatus.Available
            ? cap.Evidence ?? ""
            : $"原因：{cap.Reason}　影响：{cap.Impact}　后续：{cap.NextStep}";
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
        var text = $"记录 {analysis.ReceivedRecords} 条（失败 {analysis.FailedRecords}）· "
                   + $"分组 {analysis.Groups.Count} 个 · 差异 {analysis.Changes.Count} 处 · "
                   + $"错误 {analysis.TotalErrors} 条 · 耗时 {(bundle.Session.DurationMs ?? 0) / 1000.0:0.0} 秒";
        if (bundle.Session.RecordsTruncated) text += " · 已达记录上限，采集被截断";
        // 结果卡也要写清设备：截图/贴摘要时得知道这是哪台的结论
        if (bundle.Device is { } device)
        {
            var name = string.IsNullOrWhiteSpace(device.Product) ? device.VidPid : device.Product;
            text += $"\n设备：{name}（{device.VidPid} · {CaptureDeviceCatalog.ShortInterface(device.InterfaceKey)}）";
        }
        return text + $"\n位置：{session.Directory}";
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

        ResultWarn.Text = $"注意：{bundle.Analysis.StaticGroupText} 整场完全一致，没有任何变化。"
                          + "这通常说明它不是状态通道（配置/握手通道常见），别据此判断设备行为。";
        ResultWarn.Foreground = UiPalette.Brush(UiPalette.Low);
        ResultWarn.Visibility = Visibility.Visible;
    }

    private string BuildEmptyHint(CaptureBundle bundle)
    {
        if (bundle.Analysis.ReceivedRecords > 0) return "";
        var recommended = _selectedGroup?.Recommended;
        var hint = "\n没有采到报文：这不代表设备没有相关协议。";
        if (_selected is { InputReportLength: 0 } && _selected.AccessDenied == false)
            hint += "\n这个接口本身没有输入通道，换同一台设备的其他接口试试。";
        else if (recommended != null && !ReferenceEquals(recommended, _selected))
            hint += $"\n建议改选 {CaptureDeviceCatalog.ShortInterface(recommended.InterfaceKey)}"
                    + "（同设备里有输入通道的那个接口）。";
        else
            hint += "\n设备可能不自发推送（需要先开会话），而本功能不写设备；也可能该通道要用 Feature 读取。";
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
