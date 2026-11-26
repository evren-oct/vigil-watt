using VigilWatt.App.ViewModels;
using VigilWatt.App.Services;
using VigilWatt.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace VigilWatt.App.Views;

/// <summary>
/// 功 能： 历史数据管理页：概览 + 按设备删除。
/// 2026/10/06 16:58:42 xliu
/// 删除不可逆，每次都过二次确认对话框； 删除后必须 VACUUM 收缩文件（SQLite 的 DELETE 只把页标空闲、不缩小文件， 不收缩用户会以为删除没生效）。
/// </summary>
public sealed partial class HistoryPage : Page
{
    private List<HistoryDeviceRow> _rows = new();
    private string _filter = "";

    public HistoryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        // 页面被缓存复用、构造函数只跑一次，重查必须挂到 Loaded 上（本页不订阅设备事件）
        Loaded += (_, _) => _ = ReloadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    /// <summary>查询放到线程池：UI 线程同步跑 SQLite 全表统计会卡住界面（S5 / CR-06）。</summary>
    private async Task ReloadAsync()
    {
        if (DeviceList == null || SubtitleText == null) return;

        try
        {
            var history = App.Services.History;
            var (stats, devices) = await Task.Run(() => (history.GetStats(), history.GetDeviceStats()));

            SubtitleText.Text = stats.TotalCount == 0
                ? "暂无历史数据"
                : $"共 {stats.TotalCount:N0} 条记录 · 覆盖 {stats.DeviceCount} 台设备 · {DateTime.Now:HH:mm:ss} 更新";

            var fileSize = ByteSize.Format(stats.FileBytes);
            int split = fileSize.LastIndexOf(' ');
            KpiFileSizeNumber.Text = split > 0 ? fileSize[..split] : fileSize;
            KpiFileSizeUnit.Text = split > 0 ? " " + fileSize[(split + 1)..] : "";
            KpiTotal.Text = $"{stats.TotalCount:N0}";
            KpiDevices.Text = $"{stats.DeviceCount}";
            if (stats.First is { } first)
            {
                KpiEarliest.Text = first.LocalDateTime.ToString("MM-dd");
                KpiEarliestSub.Text = first.LocalDateTime.ToString("HH:mm");
            }
            else
            {
                KpiEarliest.Text = "—";
                KpiEarliestSub.Text = "—";
            }

            _rows = devices.Select(d => HistoryDeviceRow.From(d with
            {
                Name = DevicePresentation.DisplayName(d.DeviceId, d.Name, App.Services.Settings),
            })).ToList();
            ApplyFilter();

            UpdateSelectionSummary();
        }
        catch (Exception ex)
        {
            Log.Error("历史管理页刷新失败", ex);
        }
    }

    private void DeviceSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = DeviceSearchBox.Text.Trim();
        ApplyFilter();
    }

    /// <summary>过滤只换 ItemsSource；勾选状态留在行模型里，切回全部时不会丢。</summary>
    private void ApplyFilter()
    {
        if (DeviceList == null) return;

        var rows = string.IsNullOrEmpty(_filter)
            ? _rows
            : _rows.Where(r => r.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        DeviceList.ItemsSource = rows;

        bool hasAny = _rows.Count > 0;
        bool hasMatch = rows.Count > 0;
        DeviceList.Visibility = hasMatch ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        NoMatchState.Visibility = hasAny && !hasMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSelectionSummary()
    {
        if (SelectionText == null || DeleteSelectedButton == null) return;

        var selected = _rows.Where(r => r.IsSelected).ToList();
        SelectionText.Text = selected.Count == 0
            ? "未选中设备"
            : $"已选 {selected.Count} 台 · 将删除 {selected.Sum(r => r.Count):N0} 条记录";
        DeleteSelectedButton.IsEnabled = selected.Count > 0;
    }

    // 读 CheckBox 自身的 IsChecked 而不是绑定值：事件回调与绑定写回的先后顺序没有保证
    private void Row_CheckChanged(object sender, RoutedEventArgs e)
    {
        // 用 DataContext 而不是 Tag：列表虚拟化后容器会复用，Tag 与 IsChecked 的更新顺序不可依赖
        if (sender is not CheckBox box || box.DataContext is not HistoryDeviceRow row) return;
        row.IsSelected = box.IsChecked == true;
        UpdateSelectionSummary();
    }

    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && HoverFill is { } brush) border.Background = brush;
    }

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border) border.ClearValue(Border.BackgroundProperty);
    }

    private static Brush? HoverFill =>
        Application.Current.Resources.TryGetValue("AppHoverFillBrush", out var value)
            ? value as Brush
            : null;

    private async void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string deviceId) return;
        var row = _rows.FirstOrDefault(r => r.DeviceId == deviceId);
        if (row == null) return;
        await DeleteAsync(new[] { row });
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;
        await DeleteAsync(selected);
    }

    private async Task DeleteAsync(IReadOnlyList<HistoryDeviceRow> rows)
    {
        int total = rows.Sum(r => r.Count);
        string detail = rows.Count == 1
            ? $"设备：{rows[0].Name}\n时间范围：{rows[0].RangeText}\n记录数：{rows[0].CountText}"
            : string.Join("\n", rows.Select(r => $"· {r.Name}（{r.CountText}）"));

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = $"删除 {total:N0} 条历史记录？",
            Content = $"{detail}\n\n删除后无法恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var history = App.Services.History;
            long before = history.FileBytes();
            // 删除 + VACUUM 都是全表级操作，放线程池（VACUUM 尤其重，同步跑会明显卡 UI）
            var (deleted, after) = await Task.Run(() =>
            {
                int d = history.DeleteDevices(rows.Select(r => r.DeviceId).ToList());
                history.Compact();   // DELETE 不还空间，必须 VACUUM 才能把文件缩回去
                return (d, history.FileBytes());
            });

            ResultBar.Severity = InfoBarSeverity.Success;
            ResultBar.Title = "已删除";
            ResultBar.Message =
                $"删除 {deleted:N0} 条记录，释放 {ByteSize.Format(before - after)}，库文件现有 {ByteSize.Format(after)}";
            ResultBar.IsOpen = true;
            Log.Info($"历史管理：删除 {deleted} 条，库文件 {before} → {after} 字节");
        }
        catch (Exception ex)
        {
            Log.Error("删除历史失败", ex);
            ResultBar.Severity = InfoBarSeverity.Error;
            ResultBar.Title = "删除失败";
            ResultBar.Message = ex.Message;
            ResultBar.IsOpen = true;
        }

        await ReloadAsync();
    }
}
