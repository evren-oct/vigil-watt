using VigilWatt.Core.Storage;

namespace VigilWatt.App.ViewModels;

/// <summary>
/// 功 能： 历史管理页的设备行（一次性绑定，每次刷新重建）。
/// 2026/10/06 16:58:42 xliu
/// 勾选状态与 CheckBox 双向绑定：列表虚拟化会复用行容器，只靠事件写回会让复用的行带错状态。
/// </summary>
public sealed class HistoryDeviceRow
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required int Count { get; init; }
    public required DateTimeOffset First { get; init; }
    public required DateTimeOffset Last { get; init; }

    /// <summary>勾选状态：与 CheckBox 双向绑定，页面只读。</summary>
    public bool IsSelected { get; set; }

    public string CountText => $"{Count:N0} 条";

    public string RangeText => $"{First.LocalDateTime:MM-dd HH:mm} ~ {Last.LocalDateTime:MM-dd HH:mm}";

    public static HistoryDeviceRow From(DeviceHistoryStat stat) => new()
    {
        DeviceId = stat.DeviceId,
        Name = stat.Name,
        Count = stat.Count,
        First = stat.First,
        Last = stat.Last,
    };
}
