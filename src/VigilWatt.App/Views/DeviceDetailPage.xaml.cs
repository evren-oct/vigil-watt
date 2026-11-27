using VigilWatt.App.Services;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Utils;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

using VigilWatt.App.Localization;
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
    private bool _pointerOverPlate;

    /// <summary>指标行的内容指纹：值没变就不重建元素，免得每分钟白闪一次。</summary>
    private string _metricsKey = "";

    public DeviceDetailPage()
    {
        InitializeComponent();
        DataContext = Strings.Current;
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
        _pointerOverPlate = false;
        AdjustImageButton.Visibility = Visibility.Collapsed;
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

        ApplyHero(raw, d, charging, online, color);
        ApplyBattery(percent, charging, online, color);
        ApplyRemaining(d);
        ApplyMetrics(d);
        ApplyConnection(d, online);
        ApplyIdentity(raw, d);
    }

    private void ApplyHero(DeviceStatus raw, DeviceStatus d, bool charging, bool online, Color color)
    {
        NameText.Text = d.DisplayName;
        // 品牌没有独立字段（协议串里才有），这里只承诺"类型 + 协议"，不猜品牌
        MetaText.Text = string.IsNullOrWhiteSpace(d.Protocol)
            ? DeviceKindInfo.Label(d.Kind)
            : $"{DeviceKindInfo.Label(d.Kind)} · {d.Protocol}";

        ApplyHeroArt(raw, d, color);

        OnlineText.Text = online ? Strings.Current.Get("S_0373ff9231") : Strings.Current.Get("Tray_Offline");

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
        RecText.Foreground = Tertiary;
        RecIcon.IconKey = iconKey;
        RecIcon.IconColor = TertiaryColor;
        RecPill.Background = NeutralPill;
        RecPill.Visibility = family is null ? Visibility.Collapsed : Visibility.Visible;

        ChargeMark.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
        StateText.Text = StateLine(d, charging, online);
    }

    /// <summary>有本地产品图就用图，否则回退几何图标；两者共用同一块牌子，切换设备不会跳高度。</summary>
    private void ApplyHeroArt(DeviceStatus raw, DeviceStatus d, Color color)
    {
        var settings = App.Services.Settings;
        var imageKey = DeviceCustomizationResolver.ImageKey(raw.Id, settings.DeviceCustomizations);
        if (App.Services.DeviceImages.TryResolve(raw, imageKey, out var asset))
        {
            try
            {
                HeroImage.Source = App.Services.DeviceImages.GetImage(asset, 224);
                HeroImage.Visibility = Visibility.Visible;
                HeroIconHost.Visibility = Visibility.Collapsed;
                Plate.Padding = new Thickness(6);
                Plate.Background = ThemeBrush("CardBackgroundFillColorDefaultBrush",
                    UiPalette.Tint(UiPalette.Offline, 0x10));
                Plate.BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush",
                    UiPalette.Tint(UiPalette.Offline, 0x30));
                Plate.BorderThickness = new Thickness(1);
                AdjustImageButton.IsEnabled = true;
                AdjustImageButton.Visibility = _pointerOverPlate ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            catch (Exception ex)
            {
                // 文件被删/解码失败不能把整页刷新带走，回退几何图标即可
                Log.Warn($"设备图片加载失败 {asset.ImagePath}: {ex.Message}");
            }
        }

        HeroImage.Source = null;
        HeroImage.Visibility = Visibility.Collapsed;
        HeroIconHost.Visibility = Visibility.Visible;
        HeroIconHost.Content = DeviceIconFactory.Hero(d.Kind, color, 80);
        Plate.Padding = new Thickness(0);
        Plate.Background = UiPalette.Brush(UiPalette.Tint(color, 0x1F));
        Plate.BorderThickness = new Thickness(0);
        AdjustImageButton.IsEnabled = false;
        AdjustImageButton.Visibility = Visibility.Collapsed;
    }

    private void Plate_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverPlate = true;
        if (HeroImage.Visibility == Visibility.Visible) AdjustImageButton.Visibility = Visibility.Visible;
    }

    private void Plate_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverPlate = false;
        AdjustImageButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>详情页 Hero 的调整入口：选配色后按设备 ID 写入自定义项，再重画当前页。</summary>
    private async void AdjustImage_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceId.Length == 0) return;
        var device = App.Services.Manager.Find(_deviceId);
        if (device is null) return;
        var options = App.Services.DeviceImages.GetOptions(device);
        if (options.Count == 0) return;

        var settings = App.Services.Settings;
        settings.DeviceCustomizations.TryGetValue(_deviceId, out var current);
        var combo = new ComboBox
        {
            MinWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        combo.Items.Add(new ComboBoxItem { Content = Strings.Current.Get("S_c439ab067f"), Tag = null });
        foreach (var option in options)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Image
            {
                Source = App.Services.DeviceImages.GetImage(option, 64),
                Width = 48,
                Height = 30,
                Stretch = Stretch.Uniform,
            });
            row.Children.Add(new TextBlock
            {
                Text = option.Label ?? option.ColorKey,
                VerticalAlignment = VerticalAlignment.Center,
            });
            combo.Items.Add(new ComboBoxItem { Content = row, Tag = option.ColorKey });
        }
        combo.SelectedIndex = 0;
        if (current?.ImageKey is { } selectedKey)
        {
            for (int i = 1; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem { Tag: string optionKey } &&
                    string.Equals(optionKey, selectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = Strings.Current.Get("S_35412651e4"),
            FontSize = 12,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
        });
        body.Children.Add(combo);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Current.Get("S_9ae39426bc"),
            Content = body,
            PrimaryButtonText = Strings.Current.Get("S_fadf24dbc5"),
            CloseButtonText = Strings.Current.Get("S_4d0b4688c7"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var imageKey = combo.SelectedItem is ComboBoxItem { Tag: string key } ? key : null;
        if (current is null) current = new DeviceCustomization();
        current.ImageKey = imageKey;
        if (string.IsNullOrWhiteSpace(current.Name) && current.IconKind is null &&
            string.IsNullOrWhiteSpace(current.ImageKey))
            settings.DeviceCustomizations.Remove(_deviceId);
        else
            settings.DeviceCustomizations[_deviceId] = current;

        settings.Save();
        Apply();
    }

    /// <summary>第三行：状态 · 时刻。剩余时间单独放环下；不可用原因仍写在这里，避免信息丢失。</summary>
    private string StateLine(DeviceStatus d, bool charging, bool online)
    {
        if (!online) return Strings.Current.Format("S_b98799630f", d.LastUpdated);

        string charge = DeviceStatusFormatter.ChargeSuffix(d.Charging).Trim();
        if (charge.Length == 0)
            charge = d.Charging == ChargingState.Discharging ? Strings.Current.Get("S_b549c1221e") : Strings.Current.Get("S_f80c70054c");

        var parts = new List<string> { charge };
        var remaining = App.Services.Manager.GetRemainingTimeEstimate(d);
        if (!remaining.IsAvailable &&
            remaining.Reason is not (RemainingTimeUnavailableReason.Charging or RemainingTimeUnavailableReason.Full))
        {
            // 充/满两种原因上面那个状态词已经说过了，别重复；其余原因要写出来，否则这一行看着像"没算"
            parts.Add(remaining.Explain());
        }
        parts.Add(Strings.Current.Format("S_1e0514eb69", d.LastUpdated));
        return string.Join(" · ", parts);
    }

    private void ApplyBattery(int? percent, bool charging, bool online, Color color)
    {
        Ring.Percent = percent ?? 0;
        Ring.IsKnown = percent.HasValue;
        Ring.RingColor = color;

        // 环和数字同一把尺子：数字也按电量状态取色
        PctText.Foreground = UiPalette.Brush(color);
        PctNumber.Text = percent?.ToString() ?? "—";
        PctUnit.Text = percent.HasValue ? "%" : "";
    }

    /// <summary>环下的剩余时间：只在估算可用时显示；不可用原因已经写在状态行。</summary>
    private void ApplyRemaining(DeviceStatus d)
    {
        var remaining = App.Services.Manager.GetRemainingTimeEstimate(d);
        if (remaining.IsAvailable)
        {
            RemainingText.Text = Strings.Current.Format("S_c2a8061806", RemainingTimeEstimator.Format(remaining.Remaining!.Value));
            RemainingText.Visibility = Visibility.Visible;
        }
        else
        {
            RemainingText.Text = "";
            RemainingText.Visibility = Visibility.Collapsed;
        }
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
        MetricsSection.Visibility = Visibility.Collapsed;
    }

    /// <summary>指标行按实际项数建列：1~3 项都成立，缺项不留空位（能力未声明就不出现）。</summary>
    private void BuildMetrics(IReadOnlyList<DetailMetric> metrics)
    {
        MetricsHost.Children.Clear();
        MetricsHost.ColumnDefinitions.Clear();

        MetricsSection.Visibility = Visibility.Visible;

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
                FontWeight = FontWeights.Medium,
                Foreground = UiPalette.Brush(labelColor),
            };
            var value = new TextBlock
            {
                Text = metric.Value,
                Foreground = metric.IsKnown ? Primary : Tertiary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            // 有值用 22 号；未知降到 15 号，免得"未获取"比真值还显眼
            if (metric.IsKnown)
            {
                value.Style = (Style)Application.Current.Resources["AppMetricValueStyle"];
            }
            else
            {
                value.FontSize = 15;
                value.FontWeight = FontWeights.Normal;
            }
            var note = new TextBlock
            {
                Text = metric.Note,
                FontSize = 11,
                Foreground = Tertiary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var stack = new StackPanel { Spacing = 4, Children = { label, value, note } };
            Grid.SetColumn(stack, i);
            MetricsHost.Children.Add(stack);
        }
    }

    private void ApplyConnection(DeviceStatus d, bool online)
    {
        var tone = online ? UiPalette.ConnectionTag(d.Connection, d.Source) : UiPalette.Offline;
        ConnFactText.Text = DeviceConnectionInfo.Badge(d.Connection, d.Source, online);
        ConnFactText.Foreground = UiPalette.Brush(tone);
        ConnFactPill.Background = UiPalette.Brush(UiPalette.Tint(tone));

        var family = DeviceConnectionInfo.ReceiverLabel(d.Receiver);
        SetFact(ReceiverFamilyText, family ?? Strings.Current.Get("S_d9c32a4c3d"), family is not null);

        bool hasReceiver = d.Receiver is not null && d.Usb is { IsValid: true };
        SetFact(ReceiverIdentityText, hasReceiver ? FormatVidPid(d.Usb!.Value) : Strings.Current.Get("S_8fcd5a384d"), hasReceiver);

        SetFact(LinkStateText, online ? Strings.Current.Format("S_84249f546d", d.LastUpdated) : Strings.Current.Format("S_4299e0fd82", d.LastUpdated), online);
        SetFact(LinkBasisText, LinkBasis(d), d.Connection.HasValue || d.Source != DeviceSource.Hid24G);
    }

    /// <summary>链路判定的依据：推断要说清楚是"谁推断的"，别让用户以为设备自报了 2.4G。</summary>
    private static string LinkBasis(DeviceStatus d) => d.Connection.Source switch
    {
        TelemetrySource.DeviceReported => Strings.Current.Get("S_32df337af2"),
        TelemetrySource.Inferred => Strings.Current.Get("S_44f84d7a3f"),
        TelemetrySource.VendorSdk => Strings.Current.Get("S_6c34f09098"),
        TelemetrySource.Estimated => Strings.Current.Get("S_6f39593eff"),
        _ => d.Source is DeviceSource.Ble or DeviceSource.BluetoothClassic ? Strings.Current.Get("S_cb761f5c5e") : Strings.Current.Get("S_6cad9198f2"),
    };

    private void ApplyIdentity(DeviceStatus raw, DeviceStatus d)
    {
        SetFact(ModelText, string.IsNullOrWhiteSpace(raw.Name) ? Strings.Current.Get("S_071fb244ea") : raw.Name, !string.IsNullOrWhiteSpace(raw.Name));
        SetFact(KindText, DeviceKindInfo.Label(d.Kind), d.Kind != DeviceKind.Unknown);
        SetFact(ProtocolText, string.IsNullOrWhiteSpace(d.Protocol) ? Strings.Current.Get("S_6cad9198f2") : d.Protocol, !string.IsNullOrWhiteSpace(d.Protocol));

        bool hasReported = d.Reported is { IsValid: true };
        SetFact(BodyIdentityText, hasReported ? FormatVidPid(d.Reported!.Value.VendorId, d.Reported!.Value.ProductId) : Strings.Current.Get("S_d9c32a4c3d"), hasReported);

        SetFact(DeviceIdText, d.Id, true);

        IdentityNote.Text = d.Receiver is null
            ? Strings.Current.Get("S_648435f237")
            : Strings.Current.Get("S_f2db247d31");
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

    private static Color TertiaryColor =>
        (Application.Current.Resources["TextFillColorTertiaryBrush"] as SolidColorBrush)?.Color
        ?? Color.FromArgb(255, 128, 128, 128);

    private static Brush NeutralPill =>
        Application.Current.Resources.TryGetValue("AppNeutralPillFillBrush", out var value) && value is Brush brush
            ? brush
            : UiPalette.Brush(UiPalette.Tint(UiPalette.Offline, 0x1A));

    private static Brush ThemeBrush(string key, Color fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : UiPalette.Brush(fallback);

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
        RefreshText.Text = on ? Strings.Current.Get("S_ea0ab1492c") : Strings.Current.Get("S_38108eaa1d");
    }
}
