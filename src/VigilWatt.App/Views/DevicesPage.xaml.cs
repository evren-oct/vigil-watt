using VigilWatt.App.ViewModels;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using VigilWatt.Core.Settings;
using Microsoft.UI.Xaml.Navigation;
using VigilWatt.App.Services;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Collections.ObjectModel;
using System.Text;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Views;
/// <summary>
/// 功 能： 设备电量列表页。
/// 2026/10/06 15:53:36 xliu
/// </summary>

public sealed partial class DevicesPage : Page
{
    /// <summary>手动刷新进行中：挡住重复点击，也让"刷新中"状态不被后台事件提前清掉。</summary>
    private bool _refreshing;

    /// <summary>事件是否已订阅：Loaded 可能重复触发，不挡住就会叠加回调。</summary>
    private bool _hooked;

    /// <summary>列表数据源。只在构造时赋给 ItemsSource，之后按 Id 增量同步——
    /// 整块换 ItemsSource 会让 ListView 丢弃所有行容器重建，每分钟轮询都闪一下。</summary>
    private readonly ObservableCollection<DeviceRowModel> _rows = new();

    public DevicesPage()
    {
        InitializeComponent();
        DataContext = Strings.Current;
        NavigationCacheMode = NavigationCacheMode.Required;
        DeviceList.ItemsSource = _rows;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    // 页面被缓存复用、构造函数只跑一次：订阅与刷新必须跟着 Loaded/Unloaded 走，
    // 否则第一次切走就解绑，切回来不再走构造函数，页面从此收不到设备事件也不再刷新
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked)
        {
            _hooked = true;
            App.Services.Manager.DevicesChanged += OnDevicesChanged;
            App.Services.Manager.DeviceUpdated += OnDeviceUpdated;
        }
        Refresh();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked) return;
        _hooked = false;
        App.Services.Manager.DevicesChanged -= OnDevicesChanged;
        App.Services.Manager.DeviceUpdated -= OnDeviceUpdated;
    }

    private void OnDeviceUpdated(DeviceStatus _) => OnDevicesChanged();

    private void OnDevicesChanged() => RequestRefresh();

    private int _refreshQueued;

    /// <summary>
    /// 合并刷新请求（CR-29）：一轮轮询会对 N 台设备各发一次 DeviceUpdated、外加一次 DevicesChanged，
    /// 逐个排队就是 N+1 次全量重建 ItemsSource。用 Interlocked 只保留一次待执行的刷新。
    /// </summary>
    private void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);   // 先复位再刷新：刷新期间来的请求不会被吞掉
            Refresh();
        });
    }

    private void Refresh()
    {
        try
        {
            RefreshCore();
        }
        catch (Exception ex)
        {
            // 列表刷新失败不能带走进程：异常从 ObservableCollection 的通知回调里逸出时，
            // App.UnhandledException 的 e.Handled 拦不住，进程会直接 fail-fast（runtime-pitfalls.md §22）
            Log.Error("设备列表刷新失败", ex);
        }
    }

    private void RefreshCore()
    {
        var devices = App.Services.Manager.Devices;
        var settings = App.Services.Settings;

        // 按 Id 取旧行原地 Update：集合元素不替换，ListView 就不会回收容器（替换会让整行闪一下）
        var existing = new Dictionary<string, DeviceRowModel>(_rows.Count);
        foreach (var row in _rows) existing[row.Id] = row;

        var fresh = new List<DeviceRowModel>(devices.Count);
        foreach (var d in devices)
        {
            if (!existing.Remove(d.Id, out var row)) row = new DeviceRowModel();
            row.Update(d, settings);
            fresh.Add(row);
        }

        // existing 里剩下的是这一轮没再出现的设备，先摘掉，再对齐顺序
        foreach (var gone in existing.Values) _rows.Remove(gone);
        SyncRows(fresh);

        var online = devices.Count(d => d.IsOnline);
        // 带上时间戳：即使设备数没变，用户也能一眼看出这次刷新确实跑过了
        SummaryText.Text = Strings.Current.Format("S_63ec387d75", online, devices.Count, DateTime.Now);

        // 不在列表里的设备只能从这里恢复，所以两个入口都要跟着名单实时变
        int blocked = App.Services.Manager.BlockedDevices.Count;
        BlockedBar.Visibility = blocked > 0 ? Visibility.Visible : Visibility.Collapsed;
        BlockedText.Text = Strings.Current.Format("S_0959df313f", blocked);

        int ignored = App.Services.Manager.IgnoredDevices.Count;
        IgnoredBar.Visibility = ignored > 0 ? Visibility.Visible : Visibility.Collapsed;
        IgnoredText.Text = Strings.Current.Format("S_355437cca7", ignored);

        HiddenBars.Visibility = blocked > 0 || ignored > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool empty = devices.Count == 0;
        DeviceList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        // 全被忽略时还说"未发现设备"会把人引向"是不是没扫到"，得指向真正的恢复入口
        EmptyHint.Text = ignored > 0
            ? Strings.Current.Format("S_9040773dbb", ignored)
            : Strings.Current.Get("S_13f817fd67");
    }

    /// <summary>把 _rows 对齐到 fresh 的顺序：同实例同位置一律不动，只处理换位与新增（换位才会触发重排动画）。</summary>
    private void SyncRows(List<DeviceRowModel> fresh)
    {
        for (int i = 0; i < fresh.Count; i++)
        {
            if (i < _rows.Count && ReferenceEquals(_rows[i], fresh[i])) continue;
            int at = IndexOfRow(fresh[i]);
            if (at >= 0) _rows.Move(at, i);
            else _rows.Insert(i, fresh[i]);
        }
    }

    private int IndexOfRow(DeviceRowModel row)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (ReferenceEquals(_rows[i], row)) return i;
        }
        return -1;
    }

    private async void CustomizeDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string id) return;
        var device = App.Services.Manager.Find(id);
        if (device == null || device.Source == DeviceSource.System) return;

        var settings = App.Services.Settings;
        settings.DeviceCustomizations.TryGetValue(id, out var current);
        var nameBox = new TextBox
        {
            Text = current?.Name ?? device.DisplayName,
            MaxLength = 20,
            PlaceholderText = Strings.Current.Get("S_5e9b40fad7"),
        };
        var nameError = new TextBlock
        {
            Text = Strings.Current.Get("S_8e86429ab7"),
            FontSize = 12,
            Foreground = Application.Current.Resources["AppDangerBrush"] as Brush,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var nameHint = new TextBlock
        {
            Text = Strings.Current.Get("S_d152c13691"),
            FontSize = 11,
            Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush,
            Margin = new Thickness(0, 6, 0, 0),
        };
        nameBox.Margin = new Thickness(0, 8, 0, 0);
        bool filteringName = false;
        nameBox.TextChanged += (_, _) =>
        {
            nameError.Visibility = Visibility.Collapsed;
            if (filteringName) return;
            var filtered = FilterDeviceNameInput(nameBox.Text, nameBox.SelectionStart, nameBox.SelectionLength);
            if (filtered.Text == nameBox.Text) return;
            filteringName = true;
            try
            {
                nameBox.Text = filtered.Text;
                nameBox.SelectionStart = filtered.SelectionStart;
                nameBox.SelectionLength = filtered.SelectionLength;
            }
            finally
            {
                filteringName = false;
            }
        };

        var automaticKind = device.Kind == DeviceKind.Unknown
            ? DeviceKindGuesser.Guess(device.DisplayName)
            : device.Kind;
        DeviceKind? selectedKind = current?.IconKind;
        const int iconColumns = 4;
        var iconGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (int i = 0; i < iconColumns; i++)
            iconGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var options = new (DeviceKind? Value, DeviceKind Preview, string Label)[]
        {
            (null, automaticKind, Strings.Current.Get("S_4afad87755")), (DeviceKind.Unknown, DeviceKind.Unknown, Strings.Current.Get("S_1a0fdce8f8")),
            (DeviceKind.Mouse, DeviceKind.Mouse, Strings.Current.Get("S_dec974e26a")), (DeviceKind.Keyboard, DeviceKind.Keyboard, Strings.Current.Get("S_e2766fb28a")),
            (DeviceKind.Headset, DeviceKind.Headset, Strings.Current.Get("S_d5ca969dc3")), (DeviceKind.Speaker, DeviceKind.Speaker, Strings.Current.Get("S_049c7adb04")),
            (DeviceKind.Gamepad, DeviceKind.Gamepad, Strings.Current.Get("S_c1f19102a6")), (DeviceKind.Computer, DeviceKind.Computer, Strings.Current.Get("S_ec87a4b867")),
            (DeviceKind.Phone, DeviceKind.Phone, Strings.Current.Get("S_9c01ad0911")), (DeviceKind.Tablet, DeviceKind.Tablet, Strings.Current.Get("S_ca99b45978")),
            (DeviceKind.Watch, DeviceKind.Watch, Strings.Current.Get("S_68075dc4ab")),
        };
        // 行数由选项数推出来：以后再加图标只要往 options 里追加，不用动布局
        for (int i = 0; i < (options.Length + iconColumns - 1) / iconColumns; i++)
            iconGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Brush GetBrush(string key, Brush fallback) =>
            Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : fallback;
        var primaryBrush = GetBrush("TextFillColorPrimaryBrush", new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)));
        var iconColor = (primaryBrush as SolidColorBrush)?.Color ?? Color.FromArgb(255, 80, 80, 80);
        var tileFill = GetBrush("SubtleFillColorSecondaryBrush", new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)));
        var tileHover = GetBrush("SubtleFillColorTertiaryBrush", new SolidColorBrush(Color.FromArgb(36, 0, 0, 0)));
        var accentBrush = GetBrush("AccentFillColorDefaultBrush",
            new SolidColorBrush(Color.FromArgb(255, 0, 113, 227)));
        var accentColor = (accentBrush as SolidColorBrush)?.Color ?? Color.FromArgb(255, 0, 113, 227);
        var accentTint = new SolidColorBrush(Color.FromArgb(20, accentColor.R, accentColor.G, accentColor.B));
        var transparentBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        var iconChoiceStyle = (Style)Resources["IconChoiceStyle"];
        var repainters = new List<Action>();

        for (int i = 0; i < options.Length; i++)
        {
            var option = options[i];
            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(6, 12, 6, 10),
                MinHeight = 86,
                Background = tileFill,
                BorderBrush = transparentBrush,
                Child = new StackPanel
                {
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new Grid
                        {
                            Width = 28,
                            Height = 28,
                            Children = { DeviceIconFactory.Scaled(option.Preview, iconColor, 28) },
                        },
                        new TextBlock
                        {
                            Text = option.Label,
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                    },
                },
            };
            var radio = new RadioButton
            {
                GroupName = "DeviceIconChoice",
                Tag = option.Value,
                IsChecked = selectedKind == option.Value,
                Content = card,
                Style = iconChoiceStyle,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            bool pointerOver = false;
            void Paint()
            {
                bool selected = radio.IsChecked == true;
                card.BorderBrush = selected ? accentBrush : transparentBrush;
                card.Background = selected ? accentTint : pointerOver ? tileHover : tileFill;
            }
            card.PointerEntered += (_, _) => { pointerOver = true; Paint(); };
            card.PointerExited += (_, _) => { pointerOver = false; Paint(); };
            radio.Checked += (_, _) =>
            {
                selectedKind = option.Value;
                foreach (var repaint in repainters) repaint();
            };
            repainters.Add(Paint);
            Paint();

            Grid.SetColumn(radio, i % iconColumns);
            Grid.SetRow(radio, i / iconColumns);
            iconGrid.Children.Add(radio);
        }

        TextBlock SectionLabel(string text, double topMargin) => new()
        {
            Text = text,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = GetBrush("TextFillColorSecondaryBrush", primaryBrush),
            Margin = new Thickness(0, topMargin, 0, 0),
        };

        var body = new StackPanel { MinWidth = 500 };
        body.Children.Add(SectionLabel(Strings.Current.Get("S_1be7ae4fc2"), 0));
        body.Children.Add(nameBox);
        body.Children.Add(nameHint);
        body.Children.Add(nameError);
        body.Children.Add(SectionLabel(Strings.Current.Get("S_1f24c1e5aa"), 20));
        body.Children.Add(new TextBlock
        {
            Text = Strings.Current.Get("S_03ec7b47a1"),
            FontSize = 11,
            Foreground = GetBrush("TextFillColorTertiaryBrush", primaryBrush),
            Margin = new Thickness(0, 4, 0, 10),
        });
        body.Children.Add(iconGrid);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Current.Get("S_7ed1e14c30"),
            Content = body,
            PrimaryButtonText = Strings.Current.Get("S_fadf24dbc5"),
            SecondaryButtonText = Strings.Current.Get("S_a191935bc6"),
            CloseButtonText = Strings.Current.Get("S_4d0b4688c7"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var filtered = FilterDeviceNameInput(nameBox.Text, nameBox.SelectionStart, nameBox.SelectionLength);
            var normalized = filtered.Text.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                nameError.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }
            nameBox.Text = normalized;
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;

        if (result == ContentDialogResult.Secondary)
        {
            settings.DeviceCustomizations.Remove(id);
        }
        else
        {
            var name = FilterDeviceNameInput(nameBox.Text, 0, 0).Text.Trim();
            // 复用现有项：新建会把详情页选过的配色（ImageKey）一起清掉
            settings.DeviceCustomizations.TryGetValue(id, out var custom);
            custom ??= new DeviceCustomization();
            custom.Name = name;
            custom.IconKind = selectedKind;
            // 首次改名时记下协议原名：离线墓碑的名字取自历史库（已是改名后的值），没有原名就配不上产品图
            if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(custom.OriginalName))
                custom.OriginalName = App.Services.Manager.Find(id)?.Name;
            if (custom.Name == null && custom.IconKind == null) settings.DeviceCustomizations.Remove(id);
            else settings.DeviceCustomizations[id] = custom;
        }

        settings.Save();
        App.Services.Manager.RefreshPresentation();
        Refresh();
    }

    /// <summary>单击整卡进详情：行内不设第二个热点，避免"点这里跳、点那里没反应"。</summary>
    private void DeviceRow_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DeviceRowModel model) return;
        Frame.Navigate(typeof(DeviceDetailPage), model.Id);
    }

    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string id) return;
        Frame.Navigate(typeof(DeviceDetailPage), id);
    }

    /// <summary>悬停换底色：卡片不再描边，ListViewItem 的悬停底被卡片盖住。</summary>
    private void DeviceRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && HoverFill is { } brush) border.Background = brush;
    }

    private void DeviceRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // 清掉本地值才会回落到样式里的卡片底，主题切换也能跟着变
        if (sender is Border border) border.ClearValue(Border.BackgroundProperty);
    }

    private static Brush? HoverFill =>
        Application.Current.Resources.TryGetValue("AppHoverFillBrush", out var value)
            ? value as Brush
            : null;

    private void DeviceRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement row || row.DataContext is not DeviceRowModel model) return;
        var device = App.Services.Manager.Find(model.Id);
        if (device == null || device.Source == DeviceSource.System) return;

        var menu = new MenuFlyout();
        // 详情放第一项：它是唯一无损的动作，其余三项都会改状态
        var detail = new MenuFlyoutItem
        {
            Text = Strings.Current.Get("S_faea8c1db9"),
            Tag = model.Id,
        };
        detail.Click += OpenDetail_Click;
        menu.Items.Add(detail);

        var customize = new MenuFlyoutItem
        {
            Text = Strings.Current.Get("S_c3e7dc0e04"),
            Tag = model.Id,
        };
        customize.Click += CustomizeDevice_Click;
        menu.Items.Add(customize);

        // 忽略对在线设备也开放：直连电源的设备永远在线，"删除"那条路永远走不到（issue #52）
        var ignore = new MenuFlyoutItem
        {
            Text = Strings.Current.Get("S_9ea78b8a22"),
            Tag = model.Id,
        };
        ignore.Click += IgnoreDevice_Click;
        menu.Items.Add(ignore);

        menu.Items.Add(new MenuFlyoutSeparator());

        // 在线设备不给删：删了插件下一轮又报回来，等于什么都没做
        var delete = new MenuFlyoutItem
        {
            Text = model.IsOnline ? Strings.Current.Get("S_96030ae365") : Strings.Current.Get("S_74df625ee0"),
            Tag = model.Id,
            IsEnabled = !model.IsOnline,
        };
        delete.Click += DeleteDevice_Click;
        menu.Items.Add(delete);

        menu.ShowAt(row, new FlyoutShowOptions { Position = e.GetPosition(row) });
        e.Handled = true;
    }

    private async void DeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string id) return;
        var device = App.Services.Manager.Find(id);
        if (device == null || device.Source == DeviceSource.System || device.IsOnline) return;

        // 条数要写进确认框：只说"无法恢复"用户没概念，写出多少条才知道代价
        var summary = await Task.Run(() => App.Services.History.GetDeviceSummary(id));
        string historyLine = summary.Count == 0
            ? Strings.Current.Get("S_520cb0e751")
            : Strings.Current.Format("S_0ec2fb5757", summary.Count, summary.First, summary.Last);

        var permanent = new CheckBox
        {
            Content = new TextBlock
            {
                Text = Strings.Current.Get("S_4a13900d1f"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320,
            },
            Margin = new Thickness(0, 8, 0, 0),
        };
        var secondary = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
        var critical = Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush;
        // 连接信息统一走 DeviceConnectionInfo：离线设备只说"最后已知"或"未知"，不再拿来源冒充链路
        var body = new StackPanel { Spacing = 6, MinWidth = 380 };
        // 体系名是链路之外的第二个维度（Bolt 走 2.4G），有目录记录才补，不猜品牌
        var receiver = DeviceConnectionInfo.ReceiverLabel(device.Receiver);
        body.Children.Add(new TextBlock
        {
            Text = Strings.Current.Format("S_f196a03a79", DeviceConnectionInfo.Describe(device.Connection, device.Source, device.IsOnline))
                   + (receiver is null ? "" : $"（{receiver}）")
                   + Strings.Current.Format("S_d4c0b4bb13", device.LastUpdated),
            FontSize = 12,
            Foreground = secondary,
        });
        // 危险动作靠文字点出来：WinUI 没有内置的"危险按钮"样式，App 级引用 generic.xaml 的 DefaultButtonStyle
        // 会让 App.xaml 解析失败（启动即崩），所以按钮保持系统默认，代价写在这一行里
        body.Children.Add(new TextBlock
        {
            Text = historyLine,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = summary.Count > 0 ? critical : secondary,
        });
        body.Children.Add(new TextBlock
        {
            Text = Strings.Current.Get("S_00273f9014"),
            FontSize = 12,
            Foreground = secondary,
        });
        body.Children.Add(permanent);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Current.Format("S_33d2cb8f16", device.DisplayName),
            Content = body,
            PrimaryButtonText = Strings.Current.Get("S_3755f56f2f"),
            CloseButtonText = Strings.Current.Get("S_4d0b4688c7"),
            DefaultButton = ContentDialogButton.Close,   // 回车不误删
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var result = App.Services.Manager.RemoveOfflineDevice(id, permanent.IsChecked == true);
        if (result != DeviceRemoveResult.Removed)
        {
            await ShowMessageAsync(Strings.Current.Get("S_d42a1d1d29"), result switch
            {
                DeviceRemoveResult.Online => Strings.Current.Get("S_c560b9f6d7"),
                DeviceRemoveResult.NotFound => Strings.Current.Get("S_ac7002789c"),
                DeviceRemoveResult.SystemDevice => Strings.Current.Get("S_f6c40b9a26"),
                _ => Strings.Current.Get("S_12ff0d9be8"),
            });
            Refresh();
            return;
        }

        // 删除 + VACUUM 都是全表级操作，放线程池；失败要让人看见，不能静默（R5）
        try
        {
            int deleted = await Task.Run(() =>
            {
                int n = App.Services.History.DeleteDevices(new[] { id });
                App.Services.History.Compact();   // DELETE 只标空闲页，不 VACUUM 文件不会缩小
                return n;
            });
            Log.Info($"删除设备 {device.DisplayName}：清理历史 {deleted} 条");
        }
        catch (Exception ex)
        {
            Log.Error("删除设备历史失败", ex);
            await ShowMessageAsync(Strings.Current.Get("S_7e0dc51c77"), Strings.Current.Format("S_ed2da4669c", ex.Message));
        }

        Refresh();
    }

    private void IgnoreDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string id) return;
        // 可逆操作不弹确认框：底部「已忽略 N 台设备」就是恢复入口
        if (!App.Services.Manager.IgnoreDevice(id))
        {
            _ = ShowMessageAsync(Strings.Current.Get("S_857f377392"), Strings.Current.Get("S_c21b59a67a"));
            return;
        }
        Refresh();
    }

    private async void ManageBlocked_Click(object sender, RoutedEventArgs e)
        => await ShowManagedDialogAsync(
            Strings.Current.Get("S_132d44038e"),
            Strings.Current.Get("S_b4a4a85346"),
            () => App.Services.Manager.BlockedDevices
                .Select(kv => (kv.Id, kv.Info.Name, kv.Info.BlockedAt, Strings.Current.Get("S_fdaa7681f5"))).ToList(),
            Strings.Current.Get("S_ec7ae06b09"),
            App.Services.Manager.UnblockDevice,
            Strings.Current.Get("S_a864d705c9"));

    private async void ManageIgnored_Click(object sender, RoutedEventArgs e)
        => await ShowManagedDialogAsync(
            Strings.Current.Get("S_9e1fd62c3c"),
            Strings.Current.Get("S_db0c3faaca"),
            () => App.Services.Manager.IgnoredDevices
                .Select(kv => (kv.Id, kv.Info.Name, kv.Info.IgnoredAt, Strings.Current.Get("S_11e1a93972"))).ToList(),
            Strings.Current.Get("S_79748ca1c6"),
            App.Services.Manager.UnignoreDevice,
            Strings.Current.Get("S_1e785c875d"));

    /// <summary>「已忽略 / 已屏蔽」共用一套列表：两份名单只差标题、时间前缀和动作，抄两份早晚会改漏一边。</summary>
    private async Task ShowManagedDialogAsync(
        string title,
        string emptyText,
        Func<IReadOnlyList<(string Id, string Name, DateTimeOffset At, string AtLabel)>> provider,
        string actionLabel,
        Func<string, bool> action,
        string failureText)
    {
        var panel = new StackPanel { Spacing = 10 };
        // 失败提示只能写在框里：ContentDialog 同一 XamlRoot 同时只允许开一个，这里再弹一个会直接抛
        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush,
        };
        var root = new StackPanel { Spacing = 8, MinWidth = 380, Children = { panel, status } };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = root,
            CloseButtonText = Strings.Current.Get("S_6c14bd7f6f"),
        };

        void Rebuild()
        {
            panel.Children.Clear();
            var items = provider();
            if (items.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = emptyText });
                return;
            }

            var tertiary = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush;
            foreach (var (id, name, at, atLabel) in items)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new StackPanel { Spacing = 2 };
                text.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(name) ? id : name,
                    FontSize = 14,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                text.Children.Add(new TextBlock
                {
                    Text = $"{atLabel} {at:yyyy-MM-dd HH:mm}",
                    FontSize = 11,
                    Foreground = tertiary,
                });
                row.Children.Add(text);

                var button = new HyperlinkButton
                {
                    Content = actionLabel,
                    Tag = id,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                button.Click += (s, _) =>
                {
                    if (s is not HyperlinkButton clicked || clicked.Tag is not string target) return;
                    if (!action(target))
                    {
                        status.Text = failureText;
                        status.Visibility = Visibility.Visible;
                        return;
                    }
                    status.Visibility = Visibility.Collapsed;
                    Rebuild();
                    Refresh();
                };
                Grid.SetColumn(button, 1);
                row.Children.Add(button);

                panel.Children.Add(row);
            }
        }

        Rebuild();
        await dialog.ShowAsync();
        Refresh();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = Strings.Current.Get("S_cb63c62e50"),
        };
        await dialog.ShowAsync();
    }

    private static (string Text, int SelectionStart, int SelectionLength) FilterDeviceNameInput(
        string input, int selectionStart, int selectionLength)
    {
        const string symbols = " -_.,!?@#$%&+()[]{}'\"/:;=~`\\|<>*^，。！？、：；（）【】《》〈〉「」『』‘’“”…—–·￥";
        var output = new StringBuilder(input.Length);
        int filteredStart = 0;
        int filteredEnd = 0;
        int inputOffset = 0;
        int selectionEnd = selectionStart + selectionLength;
        foreach (var rune in input.EnumerateRunes())
        {
            int value = rune.Value;
            bool allowed = value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                or >= 0xFF10 and <= 0xFF19 or >= 0xFF21 and <= 0xFF3A or >= 0xFF41 and <= 0xFF5A
                or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF
                or >= 0x20000 and <= 0x323AF
                || value == 0x3000 || symbols.Contains(rune.ToString(), StringComparison.Ordinal);
            if (allowed)
            {
                output.Append(rune.ToString());
                if (inputOffset < selectionStart) filteredStart += rune.Utf16SequenceLength;
                if (inputOffset < selectionEnd) filteredEnd += rune.Utf16SequenceLength;
            }
            inputOffset += rune.Utf16SequenceLength;
        }
        return (output.ToString(), filteredStart, Math.Max(0, filteredEnd - filteredStart));
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
            Log.Error("手动刷新失败", ex);
        }
        finally
        {
            // 保证"刷新中"至少可见一小会儿：否则本地扫描几十毫秒就结束，按钮闪一下反而像没反应
            var elapsed = DateTimeOffset.Now - startedAt;
            var minVisible = TimeSpan.FromMilliseconds(450);
            if (elapsed < minVisible)
                await Task.Delay(minVisible - elapsed);

            Refresh();
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
