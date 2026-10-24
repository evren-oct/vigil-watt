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
using System.Text;

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

    public DevicesPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
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
        var devices = App.Services.Manager.Devices;
        DeviceList.ItemsSource = devices.Select(d => DeviceRowModel.From(
            d, App.Services.Settings, App.Services.Manager.GetRemainingTimeEstimate(d))).ToList();
        var online = devices.Count(d => d.IsOnline);
        // 带上时间戳：即使设备数没变，用户也能一眼看出这次刷新确实跑过了
        SummaryText.Text = $"{online} 台在线 / 共 {devices.Count} 台 · {DateTime.Now:HH:mm:ss} 更新";

        bool empty = devices.Count == 0;
        DeviceList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        // 永久移除的设备不在列表里，恢复入口只有这一行，所以它必须跟着名单实时变
        int blocked = App.Services.Manager.BlockedDevices.Count;
        BlockedBar.Visibility = blocked > 0 ? Visibility.Visible : Visibility.Collapsed;
        BlockedText.Text = $"已屏蔽 {blocked} 台设备 · 管理";
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
            PlaceholderText = "输入设备名称",
        };
        var nameError = new TextBlock
        {
            Text = "名称不能为空。",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28)),
            Visibility = Visibility.Collapsed,
        };
        var nameHint = new TextBlock
        {
            Text = "仅支持中文、英文、数字、空格及常见中英文标点。",
            FontSize = 12,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
        };
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
        var iconChoices = new List<(RadioButton Button, Border Card)>();
        var iconGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (int i = 0; i < 4; i++)
            iconGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 2; i++)
            iconGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var options = new (DeviceKind? Value, DeviceKind Preview, string Label)[]
        {
            (null, automaticKind, "自动"), (DeviceKind.Unknown, DeviceKind.Unknown, "通用"),
            (DeviceKind.Mouse, DeviceKind.Mouse, "鼠标"), (DeviceKind.Keyboard, DeviceKind.Keyboard, "键盘"),
            (DeviceKind.Headset, DeviceKind.Headset, "耳机"), (DeviceKind.Speaker, DeviceKind.Speaker, "音箱"),
            (DeviceKind.Gamepad, DeviceKind.Gamepad, "手柄"), (DeviceKind.Computer, DeviceKind.Computer, "电脑"),
        };
        var primaryBrush = Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush;
        var iconColor = primaryBrush?.Color ?? Color.FromArgb(255, 80, 80, 80);
        Brush GetBrush(string key, Brush fallback) =>
            Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : fallback;
        var cardBrush = GetBrush("CardBackgroundFillColorDefaultBrush",
            new SolidColorBrush(Color.FromArgb(255, 248, 248, 248)));
        var borderBrush = GetBrush("CardStrokeColorDefaultBrush",
            new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)));
        var accentBrush = GetBrush("AccentFillColorDefaultBrush",
            new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)));
        var accentColor = (accentBrush as SolidColorBrush)?.Color ?? Color.FromArgb(255, 0, 120, 212);
        var accentTint = new SolidColorBrush(Color.FromArgb(28, accentColor.R, accentColor.G, accentColor.B));

        for (int i = 0; i < options.Length; i++)
        {
            var option = options[i];
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 9, 4, 8),
                Background = cardBrush,
                BorderBrush = borderBrush,
                Child = new StackPanel
                {
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new Grid
                        {
                            Width = 28,
                            Height = 28,
                            Children = { DeviceIconFactory.Glyph(option.Preview, iconColor) },
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
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                MinWidth = 0,
            };
            radio.Checked += (_, _) =>
            {
                selectedKind = option.Value;
                UpdateIconChoiceCards();
            };
            iconChoices.Add((radio, card));
            Grid.SetColumn(radio, i % 4);
            Grid.SetRow(radio, i / 4);
            iconGrid.Children.Add(radio);
        }

        void UpdateIconChoiceCards()
        {
            foreach (var (radio, card) in iconChoices)
            {
                bool selected = radio.IsChecked == true;
                card.BorderBrush = selected ? accentBrush : borderBrush;
                card.Background = selected ? accentTint : cardBrush;
            }
        }
        UpdateIconChoiceCards();

        var body = new StackPanel { Spacing = 10, MinWidth = 400 };
        body.Children.Add(new TextBlock { Text = "设备名称", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        body.Children.Add(nameBox);
        body.Children.Add(nameHint);
        body.Children.Add(nameError);
        body.Children.Add(new TextBlock
        {
            Text = "图标",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0),
        });
        body.Children.Add(iconGrid);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "设备外观",
            Content = body,
            PrimaryButtonText = "保存",
            SecondaryButtonText = "恢复默认",
            CloseButtonText = "取消",
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
            var custom = new DeviceCustomization();
            custom.Name = name;
            custom.IconKind = selectedKind;
            if (custom.Name == null && custom.IconKind == null) settings.DeviceCustomizations.Remove(id);
            else settings.DeviceCustomizations[id] = custom;
        }

        settings.Save();
        App.Services.Manager.RefreshPresentation();
        Refresh();
    }

    private void DeviceRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement row || row.DataContext is not DeviceRowModel model) return;
        var device = App.Services.Manager.Find(model.Id);
        if (device == null || device.Source == DeviceSource.System) return;

        var menu = new MenuFlyout();
        var customize = new MenuFlyoutItem
        {
            Text = "自定义名称和图标",
            Tag = model.Id,
        };
        customize.Click += CustomizeDevice_Click;
        menu.Items.Add(customize);
        menu.Items.Add(new MenuFlyoutSeparator());

        // 在线设备不给删：删了插件下一轮又报回来，等于什么都没做
        var delete = new MenuFlyoutItem
        {
            Text = model.IsOnline ? "删除设备（离线后可删）" : "删除设备…",
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
            ? "该设备没有历史记录。"
            : $"将同时删除 {summary.Count:N0} 条历史记录（{summary.First:MM-dd} ~ {summary.Last:MM-dd}），无法恢复。";

        var permanent = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "永久移除此设备（加入黑名单，以后不再扫描；需要恢复时到本页底部「已屏蔽设备」解除）",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320,
            },
            Margin = new Thickness(0, 8, 0, 0),
        };
        var secondary = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
        var critical = Application.Current.Resources["SystemFillColorCriticalBrush"] as Brush;
        var body = new StackPanel { Spacing = 6, MinWidth = 380 };
        body.Children.Add(new TextBlock
        {
            Text = $"来源：{DeviceSourceInfo.ShortLabel(device.Source)} · 最后更新 {device.LastUpdated:MM-dd HH:mm}",
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
            Text = "设备重新连接后会自动回到列表。",
            FontSize = 12,
            Foreground = secondary,
        });
        body.Children.Add(permanent);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"删除「{device.DisplayName}」？",
            Content = body,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,   // 回车不误删
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var result = App.Services.Manager.RemoveOfflineDevice(id, permanent.IsChecked == true);
        if (result != DeviceRemoveResult.Removed)
        {
            await ShowMessageAsync("无法删除", result switch
            {
                DeviceRemoveResult.Online => "设备已经上线，未删除。",
                DeviceRemoveResult.NotFound => "设备已不在列表中。",
                DeviceRemoveResult.SystemDevice => "本机电量不能删除。",
                _ => "设置写入失败，未删除。",
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
            await ShowMessageAsync("历史未删净", $"设备已从列表移除，但历史删除失败：{ex.Message}");
        }

        Refresh();
    }

    private async void ManageBlocked_Click(object sender, RoutedEventArgs e)
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
            Title = "已屏蔽的设备",
            Content = root,
            CloseButtonText = "关闭",
        };

        void Rebuild()
        {
            panel.Children.Clear();
            var items = App.Services.Manager.BlockedDevices;
            if (items.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = "没有已屏蔽的设备。" });
                return;
            }

            var tertiary = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush;
            foreach (var (id, info) in items)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new StackPanel { Spacing = 2 };
                text.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(info.Name) ? id : info.Name,
                    FontSize = 14,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                text.Children.Add(new TextBlock
                {
                    Text = $"屏蔽于 {info.BlockedAt:yyyy-MM-dd HH:mm}",
                    FontSize = 11,
                    Foreground = tertiary,
                });
                row.Children.Add(text);

                var unblock = new HyperlinkButton
                {
                    Content = "解除",
                    Tag = id,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                unblock.Click += (s, _) =>
                {
                    if (s is not HyperlinkButton button || button.Tag is not string target) return;
                    if (!App.Services.Manager.UnblockDevice(target))
                    {
                        status.Text = "解除失败：设置写入失败，未解除屏蔽。";
                        status.Visibility = Visibility.Visible;
                        return;
                    }
                    status.Visibility = Visibility.Collapsed;
                    Rebuild();
                    Refresh();
                };
                Grid.SetColumn(unblock, 1);
                row.Children.Add(unblock);

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
            CloseButtonText = "知道了",
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
        RefreshText.Text = on ? "刷新中…" : "刷新";
    }
}
