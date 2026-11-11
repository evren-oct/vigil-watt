using VigilWatt.App.Services;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace VigilWatt.App.Views;

/// <summary>
/// 功 能： 单台设备详情页：Hero（身份 + 电量）+ 性能 / 连接 / 身份三张卡。
/// 2026/11/11 22:30:00 20211
/// 只订阅一台设备的更新（按 Id 过滤），固定布局直接改命名元素，不建行模型（同 StatsPage）。
/// </summary>
public sealed partial class DeviceDetailPage : Page
{
    private string _deviceId = "";
    private bool _hooked;
    private bool _refreshing;

    /// <summary>指标行的内容指纹：值没变就不重建元素，免得每分钟白闪一次。</summary>
    private string _metricsKey = "";

    public DeviceDetailPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        // 主题切换后显式前景色不会自己跟着换，重刷一次（轮询也会兜住，但别等那一分钟）
        ActualThemeChanged += (_, _) => Apply();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string id) _deviceId = id;
        // 页面是缓存复用的：上一台设备的指标单元格必须先清掉，否则新设备没有指标时旧数字会留在屏幕上
        ResetMetrics();
        GoneText.Visibility = Visibility.Collapsed;
        Apply();
    }

    // 页面被缓存复用、构造函数只跑一次：订阅必须跟着 Loaded/Unloaded 走（runtime-pitfalls.md §19）
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_hooked) return;
        _hooked = true;
        App.Services.Manager.DeviceUpdated += OnDeviceUpdated;
        App.Services.Manager.DevicesChanged += OnDevicesChanged;
        Apply();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked) return;
        _hooked = false;
        App.Services.Manager.DeviceUpdated -= OnDeviceUpdated;
        App.Services.Manager.DevicesChanged -= OnDevicesChanged;
    }

    private void OnDeviceUpdated(DeviceStatus status)
    {
        if (status.Id != _deviceId) return;
        DispatcherQueue.TryEnqueue(Apply);
    }

    private void OnDevicesChanged() => DispatcherQueue.TryEnqueue(Apply);

    /// <summary>把当前快照写进界面。找不到设备（被删除/忽略）就退回列表。</summary>
    private void Apply()
    {
        try
        {
            ApplyCore();
        }
        catch (Exception ex)
        {
            // 详情页刷新失败不能带走进程：异常从 DispatcherQueue 回调里逸出会直接 fail-fast（§11）
            Log.Error("设备详情页刷新失败", ex);
        }
    }

    private void ApplyCore()
    {
        // 页面已卸载或还没拿到设备 ID 时什么都不做：迟到的事件回调不该改已离开的页面
        if (!_hooked || _deviceId.Length == 0) return;

        var raw = App.Services.Manager.Find(_deviceId);
        if (raw is null)
        {
            GoneText.Visibility = Visibility.Visible;
            if (Frame.CanGoBack) Frame.GoBack();
            return;
        }

        var settings = App.Services.Settings;
        // 图标覆盖与自定义名都要生效；WithIconKind 只改 clone，不会污染数据源
        var d = DevicePresentation.WithIconKind(DevicePresentation.Apply(raw, settings), settings);
        var percent = d.BatteryPercent;
        bool charging = DeviceStatusFormatter.IsCharging(d.Charging);
        bool online = d.IsOnline;
        var color = online ? UiPalette.BatteryLevel(percent, charging) : UiPalette.Offline;

        ApplyHero(d, charging, online, color);
        ApplyBattery(percent, charging, online, color);
        ApplyMetrics(d);
        ApplyConnection(d, online);
        ApplyIdentity(raw, d);
    }

    private void ApplyHero(DeviceStatus d, bool charging, bool online, Color color)
    {
        NameText.Text = d.DisplayName;
        // 品牌没有独立字段（协议串里才有），这里只承诺"类型 + 协议"，不猜品牌
        MetaText.Text = string.IsNullOrWhiteSpace(d.Protocol)
            ? DeviceKindInfo.Label(d.Kind)
            : $"{DeviceKindInfo.Label(d.Kind)} · {d.Protocol}";

        HeroIconHost.Content = DeviceIconFactory.Hero(d.Kind, color, 34);
        Plate.Background = UiPalette.Brush(UiPalette.Tint(color, 0x1F));

        OnlineText.Text = online ? "在线" : "离线";

        var tone = online ? UiPalette.ConnectionTag(d.Connection, d.Source) : UiPalette.Offline;
        ConnText.Text = DeviceConnectionInfo.Badge(d.Connection, d.Source, online);
        ConnText.Foreground = UiPalette.Brush(tone);
        ConnSuffix.Foreground = UiPalette.Brush(tone);
        ConnPill.Background = UiPalette.Brush(UiPalette.Tint(tone));
        // 只有非设备直报的来源才挂标记，与列表 / 指标同一套口径
        ConnSuffix.Visibility = d.Connection.Source == TelemetrySource.Inferred
            ? Visibility.Visible
            : Visibility.Collapsed;

        var family = DeviceConnectionInfo.ReceiverLabel(d.Receiver);
        var iconKey = DeviceConnectionInfo.ReceiverIconKey(d.Receiver);
        RecText.Text = family ?? "";
        RecText.Foreground = UiPalette.Brush(tone);
        RecIcon.IconKey = iconKey;
        RecIcon.IconColor = tone;
        RecPill.Background = UiPalette.Brush(UiPalette.Tint(tone, 0x1A));
        RecPill.Visibility = family is null ? Visibility.Collapsed : Visibility.Visible;

        ChargeMark.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
        StateText.Text = StateLine(d, charging, online);
    }

    /// <summary>第三行：状态 · 剩余 · 时刻。离线时不写"更新于"，改成"最后已知"。</summary>
    private string StateLine(DeviceStatus d, bool charging, bool online)
    {
        if (!online) return $"离线 · 最后已知 {d.LastUpdated:MM-dd HH:mm}";

        string charge = DeviceStatusFormatter.ChargeSuffix(d.Charging).Trim();
        if (charge.Length == 0)
            charge = d.Charging == ChargingState.Discharging ? "放电中" : "充电状态未知";

        var parts = new List<string> { charge };
        var remaining = App.Services.Manager.GetRemainingTimeEstimate(d);
        if (remaining.IsAvailable)
        {
            parts.Add($"约{RemainingTimeEstimator.FormatWithTotalHours(remaining.Remaining!.Value)}剩余");
        }
        else if (remaining.Reason is not (RemainingTimeUnavailableReason.Charging or RemainingTimeUnavailableReason.Full))
        {
            // 充/满两种原因上面那个状态词已经说过了，别重复；其余原因要写出来，否则这一行看着像"没算"
            parts.Add(remaining.Explain());
        }
        parts.Add($"{d.LastUpdated:HH:mm:ss} 更新");
        return string.Join(" · ", parts);
    }

    private void ApplyBattery(int? percent, bool charging, bool online, Color color)
    {
        Ring.Percent = percent ?? 0;
        Ring.IsKnown = percent.HasValue;
        Ring.RingColor = color;

        // 数字只在异常态上色：正常用主文字色，别让彩色数字跟环抢注意力
        Color? figure = !online ? UiPalette.Offline
            : charging ? UiPalette.Charging
            : percent is <= 20 ? UiPalette.Low
            : null;
        PctText.Foreground = figure is { } value ? UiPalette.Brush(value) : Primary;
        PctNumber.Text = percent?.ToString() ?? "—";
    }

    private void ApplyMetrics(DeviceStatus d)
    {
        var metrics = PointerTelemetryFormatter.DetailMetrics(d, DateTimeOffset.Now);

        // 一项都没有（能力未声明 / 离线且从没读到）：整卡收起。
        // 必须走显式分支——空列表的指纹是空串，正好等于复位后的 _metricsKey，走"没变化"会留着上一台设备的数字。
        if (metrics.Count == 0)
        {
            ResetMetrics();
            return;
        }

        string key = string.Join("|", metrics.Select(m => $"{m.Kind}:{m.Value}:{m.Note}"));
        if (key == _metricsKey) return;
        _metricsKey = key;
        BuildMetrics(metrics);
    }

    /// <summary>清空指标区并收起卡片：换设备、或这台设备没有任何可展示的指标时调用。</summary>
    private void ResetMetrics()
    {
        _metricsKey = "";
        MetricsHost.Children.Clear();
        MetricsHost.ColumnDefinitions.Clear();
        MetricsCard.Visibility = Visibility.Collapsed;
    }

    /// <summary>指标行按实际项数建列：1~3 项都成立，缺项不留空位（能力未声明就不出现）。</summary>
    private void BuildMetrics(IReadOnlyList<DetailMetric> metrics)
    {
        MetricsHost.Children.Clear();
        MetricsHost.ColumnDefinitions.Clear();

        MetricsCard.Visibility = Visibility.Visible;

        Brush rule = UiPalette.Brush(Color.FromArgb(0x1A, 0, 0, 0));
        if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke) && stroke is Brush strokeBrush)
            rule = strokeBrush;

        for (int i = 0; i < metrics.Count; i++)
            MetricsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < metrics.Count; i++)
        {
            var metric = metrics[i];
            var labelColor = metric.IsKnown ? UiPalette.Metric(metric.Kind) : UiPalette.Offline;

            var label = new TextBlock
            {
                Text = metric.Label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiPalette.Brush(labelColor),
            };
            var value = new TextBlock
            {
                Text = metric.Value,
                Foreground = metric.IsKnown ? Primary : Tertiary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            // 有值用 18 号（与页面标题同档）；未知降到 14 号，免得"未获取"比真值还显眼
            if (metric.IsKnown)
            {
                value.Style = (Style)Application.Current.Resources["AppMetricValueStyle"];
            }
            else
            {
                value.FontSize = 14;
                value.FontWeight = FontWeights.Normal;
            }
            var note = new TextBlock
            {
                Text = metric.Note,
                FontSize = 11,
                Foreground = Tertiary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var stack = new StackPanel { Spacing = 1, Children = { label, value, note } };
            var cell = new Border
            {
                // 分隔线画在第 2 项起的左侧：第一项左边不留一条悬空竖线
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0),
                BorderBrush = rule,
                Padding = new Thickness(i == 0 ? 0 : 16, 0, 0, 0),
                Child = stack,
            };
            Grid.SetColumn(cell, i);
            MetricsHost.Children.Add(cell);
        }
    }

    private void ApplyConnection(DeviceStatus d, bool online)
    {
        var tone = online ? UiPalette.ConnectionTag(d.Connection, d.Source) : UiPalette.Offline;
        ConnFactText.Text = DeviceConnectionInfo.Badge(d.Connection, d.Source, online);
        ConnFactText.Foreground = UiPalette.Brush(tone);
        ConnFactPill.Background = UiPalette.Brush(UiPalette.Tint(tone));

        var family = DeviceConnectionInfo.ReceiverLabel(d.Receiver);
        SetFact(ReceiverFamilyText, family ?? "未知", family is not null);

        bool hasReceiver = d.Receiver is not null && d.Usb is { IsValid: true };
        SetFact(ReceiverIdentityText, hasReceiver ? FormatVidPid(d.Usb!.Value) : "未获取", hasReceiver);

        SetFact(LinkStateText, online ? $"在线 · {d.LastUpdated:HH:mm:ss}" : $"离线 · 最后应答 {d.LastUpdated:MM-dd HH:mm}", online);
        SetFact(LinkBasisText, LinkBasis(d), d.Connection.HasValue || d.Source != DeviceSource.Hid24G);
    }

    /// <summary>链路判定的依据：推断要说清楚是"谁推断的"，别让用户以为设备自报了 2.4G。</summary>
    private static string LinkBasis(DeviceStatus d) => d.Connection.Source switch
    {
        TelemetrySource.DeviceReported => "设备上报",
        TelemetrySource.Inferred => "由接收器响应推断",
        TelemetrySource.VendorSdk => "厂商提供",
        TelemetrySource.Estimated => "系统估算",
        _ => d.Source is DeviceSource.Ble or DeviceSource.BluetoothClassic ? "接入方式即链路" : "未声明",
    };

    private void ApplyIdentity(DeviceStatus raw, DeviceStatus d)
    {
        SetFact(ModelText, string.IsNullOrWhiteSpace(raw.Name) ? "未提供" : raw.Name, !string.IsNullOrWhiteSpace(raw.Name));
        SetFact(KindText, DeviceKindInfo.Label(d.Kind), d.Kind != DeviceKind.Unknown);
        SetFact(ProtocolText, string.IsNullOrWhiteSpace(d.Protocol) ? "未声明" : d.Protocol, !string.IsNullOrWhiteSpace(d.Protocol));

        bool hasReported = d.Reported is { IsValid: true };
        SetFact(BodyIdentityText, hasReported ? FormatVidPid(d.Reported!.Value.VendorId, d.Reported!.Value.ProductId) : "未知", hasReported);

        SetFact(DeviceIdText, d.Id, true);

        IdentityNote.Text = d.Receiver is null
            ? "未登记接收器：这台设备的身份来自设备自身或接入方式，不会用接收器型号替代。"
            : "接收器是传输通道，不是设备本体。本体身份需设备自报，未返回时显示「未知」——应用不会用接收器型号替代。";
    }

    /// <summary>未知值统一走三级灰：值对了颜色，就不需要额外的感叹号。</summary>
    private static void SetFact(TextBlock target, string text, bool known)
    {
        target.Text = text;
        target.Foreground = known ? Primary : Tertiary;
    }

    private static string FormatVidPid(UsbIdentity usb) => FormatVidPid(usb.VendorId, usb.ProductId);

    private static string FormatVidPid(ushort vendorId, ushort productId) => $"{vendorId:X4}:{productId:X4}";

    private static Brush Primary =>
        Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush ?? UiPalette.Brush(UiPalette.Offline);

    private static Brush Tertiary =>
        Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush ?? UiPalette.Brush(UiPalette.Offline);

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        _refreshing = true;
        SetRefreshing(true);
        var startedAt = DateTimeOffset.Now;
        try
        {
            await App.Services.Manager.RequestPollAsync();
        }
        catch (Exception ex)
        {
            Log.Error("详情页手动刷新失败", ex);
        }
        finally
        {
            // 与列表页同样的"至少可见一小会儿"：本地扫描几十毫秒就结束，按钮闪一下反而像没反应
            var elapsed = DateTimeOffset.Now - startedAt;
            var minVisible = TimeSpan.FromMilliseconds(450);
            if (elapsed < minVisible) await Task.Delay(minVisible - elapsed);

            Apply();
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
        RefreshText.Text = on ? "刷新中…" : "刷新";
    }
}
