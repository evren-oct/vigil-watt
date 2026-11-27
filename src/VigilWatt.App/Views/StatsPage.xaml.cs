using VigilWatt.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using VigilWatt.Core.Models;
using VigilWatt.Core.Storage;
using VigilWatt.Core.Utils;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Views;
/// <summary>
/// 功 能： 历史统计页。
/// 2026/10/06 15:53:36 xliu
/// </summary>

public sealed partial class StatsPage : Page
{
    /// <summary>历史页设备条目：Id 用于查库，Name 既画曲线标注也做下拉框文案（只显示名称，不附历史时间等附加信息）。</summary>
    private sealed record DeviceEntry(string Id, string Name);

    /// <summary>手动刷新进行中：挡住重复点击，也让"刷新中"状态不被后台事件提前清掉。</summary>
    private bool _refreshing;

    /// <summary>渲染代次：查询移到线程池后会有并发，用它丢弃"过期的旧结果"，避免旧曲线盖掉新曲线。</summary>
    private int _renderSeq;

    /// <summary>事件是否已订阅：Loaded 可能重复触发，不挡住就会叠加回调。</summary>
    private bool _hooked;

    /// <summary>上次画曲线用的数据指纹；相同时跳过重建，避免 DevicesChanged 把悬停气泡冲掉。</summary>
    private string? _chartKey;

    public StatsPage()
    {
        InitializeComponent();
        DataContext = Strings.Current;
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    // 页面被缓存复用、构造函数只跑一次：订阅与刷新必须跟着 Loaded/Unloaded 走（同 DevicesPage）
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked)
        {
            _hooked = true;
            App.Services.Manager.DevicesChanged += OnDevicesChanged;
        }
        _ = ReloadDeviceListAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked) return;
        _hooked = false;
        App.Services.Manager.DevicesChanged -= OnDevicesChanged;
    }

    private void OnDevicesChanged() => DispatcherQueue.TryEnqueue(() => _ = ReloadDeviceListAsync());

    /// <summary>当前选中的时间范围（小时）。分段控件互斥，默认 24 小时。</summary>
    private int GetSelectedHours()
    {
        if (Range168?.IsChecked == true) return 168;
        if (Range720?.IsChecked == true) return 720;
        return 24;
    }

    private string GetRangeText() => GetSelectedHours() switch
    {
        168 => Strings.Current.Get("S_2a0a052cea"),
        720 => Strings.Current.Get("S_279e4c240d"),
        _ => Strings.Current.Get("S_eb9f889d6a"),
    };

    /// <summary>时间范围切换：三个分段互斥，点谁谁亮。</summary>
    private void Range_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;
        foreach (var button in new[] { Range24, Range168, Range720 })
            button.IsChecked = ReferenceEquals(button, clicked);

        _ = ReloadDeviceListAsync();
    }

    /// <summary>查询放到线程池：UI 线程同步跑 SQLite 查询会卡住界面（S5 / CR-06）。</summary>
    private async Task ReloadDeviceListAsync()
    {
        if (DeviceCombo == null || ChartHost == null) return;

        int seq = ++_renderSeq;
        int hours = GetSelectedHours();

            // 设备列表按历史库走：所选时间范围内有过采样的设备都在列（含已下线/换过 Id 的历史设备），
            // 不跟随当前在线设备——否则昨天用旧协议采的曲线永远选不到。
        var historyEntries = await Task.Run(() => App.Services.History
            .DistinctDevices(DateTimeOffset.Now.AddHours(-hours))
            .ToList());
        var entries = historyEntries
            .Select(d => new DeviceEntry(d.DeviceId,
                DevicePresentation.DisplayName(d.DeviceId, d.Name, App.Services.Settings)))
            .ToList();
        if (seq != _renderSeq) return;   // 期间又发起了新刷新，丢弃这次过期结果

        // 列表内容没变就不重建 ItemsSource——重建会强制关闭打开中的下拉框并把选择弹回，
        // 每 60 秒一次的轮询周期正好会打断用户切换（表现为"切换没反应"）
        var selectedId = (DeviceCombo.SelectedItem as DeviceEntry)?.Id;
        bool sameList = DeviceCombo.ItemsSource is IReadOnlyList<DeviceEntry> cur &&
                        cur.Count == entries.Count &&
                        cur.Zip(entries, (a, b) => a.Id == b.Id && a.Name == b.Name).All(x => x);
        if (!sameList)
        {
            DeviceCombo.ItemsSource = entries;
            DeviceCombo.SelectedItem = entries.FirstOrDefault(e => e.Id == selectedId) ?? entries.FirstOrDefault();
        }

        await RenderChartAsync();
    }

    private void Device_SelectionChanged(object sender, SelectionChangedEventArgs e) => _ = RenderChartAsync();

    private async Task RenderChartAsync()
    {
        // XAML 解析期事件可能先于字段赋值触发，统一判空
        if (DeviceCombo == null || ChartHost == null) return;

        int seq = ++_renderSeq;
        var from = DateTimeOffset.Now.AddHours(-GetSelectedHours());
        var to = DateTimeOffset.Now;
        var device = DeviceCombo.SelectedItem as DeviceEntry;
        var samples = device is null
            ? new List<HistorySample>()
            : await Task.Run(() => App.Services.History.Query(device.Id, from, to));
        if (seq != _renderSeq) return;   // 期间又发起了新刷新，丢弃这次过期结果

        UpdateKpis(device, samples);

        // 指纹不含 DateTime.Now：时间窗右端每秒都变，却不该整图重造；新采样靠条数/末点变化触发。
        string key = device is null
            ? $"none|{GetSelectedHours()}"
            : $"{device.Id}|{device.Name}|{GetSelectedHours()}|{samples.Count}|" +
              (samples.Count == 0
                  ? "0"
                  : $"{samples[^1].Timestamp.ToUnixTimeMilliseconds()}:{samples[^1].Percent}");
        if (key == _chartKey && ChartHost.Children.Count > 0) return;
        _chartKey = key;

        // 断档阈值 = 3×采样间隔（最少 15 分钟）：在线时应按间隔落点，更大间隔几乎肯定是离线/关机
        var gapBreak = TimeSpan.FromMinutes(Math.Max(15, App.Services.Settings.SampleIntervalMinutes * 3));
        var chart = new BatteryChartControl();
        if (device is null)
        {
            // 没有设备：仍画空坐标系，把所选时间窗显式交给图表（切档位能看出轴在变）
            chart.SetData(Array.Empty<BatteryChartControl.Series>(), from, to, gapBreak);
        }
        else
        {
            chart.SetData(new[]
            {
                new BatteryChartControl.Series
                {
                    Name = device.Name,
                    Color = UiPalette.SeriesColors[0],
                    Points = samples.Select(s => (s.Timestamp, s.Percent)).ToList(),
                },
            }, from, to, gapBreak);
        }

        ChartHost.Children.Clear();
        ChartHost.Children.Add(chart);
    }

    /// <summary>刷新顶部副标题与 4 张指标卡。当前值优先取"实时在线"读数，取不到才回退到最后一条历史采样。</summary>
    private void UpdateKpis(DeviceEntry? device, List<HistorySample> samples)
    {
        if (device is null)
        {
            SubtitleText.Text = Strings.Current.Get("S_1086c184e5");
            ResetKpis();
            return;
        }

        if (samples.Count == 0)
        {
            SubtitleText.Text = Strings.Current.Format("S_e51f2854a3", device.Name, GetRangeText());
            ResetKpis();
            return;
        }

        var last = samples[^1];
        var max = samples.MaxBy(s => s.Percent)!;
        var min = samples.MinBy(s => s.Percent)!;
        double avg = samples.Average(s => s.Percent);

        // 历史库是每 N 分钟一条；设备此刻在线时用实时值更准
        var live = App.Services.Manager.Devices.FirstOrDefault(d => d.Id == device.Id && d.IsOnline);
        int current = live?.BatteryPercent ?? last.Percent;
        bool charging = (live?.Charging ?? ChargingState.Unknown) is ChargingState.Charging or ChargingState.Full;

        SubtitleText.Text = Strings.Current.Format("S_b22e321271", device.Name, GetRangeText(), samples.Count);

        KpiCurrentNumber.Text = current.ToString();
        KpiCurrentNumber.Foreground = new SolidColorBrush(UiPalette.BatteryLevel(current, charging));
        KpiCurrentSub.Text = Strings.Current.Format("S_938fbc04d9", (live is not null ? Strings.Current.Get("S_bd1ab15a74") : Strings.Current.Get("S_cd36ed4932")), last.Timestamp.LocalDateTime);

        KpiMaxNumber.Text = max.Percent.ToString();
        KpiMaxSub.Text = $"{max.Timestamp.LocalDateTime:MM-dd HH:mm}";

        KpiMinNumber.Text = min.Percent.ToString();
        KpiMinSub.Text = $"{min.Timestamp.LocalDateTime:MM-dd HH:mm}";

        KpiAvgNumber.Text = Math.Round(avg).ToString("0");
        KpiAvgSub.Text = Strings.Current.Format("S_3ab0df28e0", max.Percent - min.Percent);
    }

    private void ResetKpis()
    {
        foreach (var (value, sub) in new[]
                 {
                     (KpiCurrentNumber, KpiCurrentSub),
                     (KpiMaxNumber, KpiMaxSub),
                     (KpiMinNumber, KpiMinSub),
                     (KpiAvgNumber, KpiAvgSub),
                 })
        {
            value.Text = "—";
            sub.Text = "—";
        }
        KpiCurrentNumber.Foreground = Primary;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;

        _refreshing = true;
        SetRefreshing(true);
        var startedAt = DateTimeOffset.Now;
        try
        {
            // 等整轮跑完（含 BLE 异步补读）。若自动轮询正在进行，这里会复用那一轮，不会白跑也不会被丢弃。
            await App.Services.Manager.RequestPollAsync();
        }
        catch (Exception ex)
        {
            Log.Error("历史统计手动刷新失败", ex);
        }
        finally
        {
            // 保证"刷新中"至少可见一小会儿：否则本地扫描几十毫秒就结束，按钮闪一下反而像没反应
            var elapsed = DateTimeOffset.Now - startedAt;
            var minVisible = TimeSpan.FromMilliseconds(450);
            if (elapsed < minVisible)
                await Task.Delay(minVisible - elapsed);

            await ReloadDeviceListAsync();
            SetRefreshing(false);
            _refreshing = false;
        }
    }

    private void SetRefreshing(bool on)
    {
        RefreshButton.IsEnabled = !on;
        RefreshRing.IsActive = on;
        RefreshRing.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RefreshIcon.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        RefreshText.Text = on ? Strings.Current.Get("S_ea0ab1492c") : Strings.Current.Get("S_38108eaa1d");
    }

    private static Brush Primary =>
        Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
        ?? UiPalette.Brush(UiPalette.Offline);
}
