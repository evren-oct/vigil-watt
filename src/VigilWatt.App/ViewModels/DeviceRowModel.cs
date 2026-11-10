using System.ComponentModel;
using System.Runtime.CompilerServices;
using VigilWatt.App.Services;
using VigilWatt.Core.Models;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VigilWatt.App.ViewModels;

/// <summary>
/// 功 能： 设备列表行（可变 + 属性通知，原地更新；Brush 在 UI 线程的刷新路径里创建）。
/// 2026/10/06 15:53:36 xliu
/// 不重建对象：替换 ObservableCollection 元素会让 ListView 回收容器，整行闪一下（docs/dev/runtime-pitfalls.md §25）。
/// </summary>
public sealed class DeviceRowModel : INotifyPropertyChanged
{
    /// <summary>不可能被算出来的颜色，只用来表示"这一项还没赋过值"。透明色不会被任何状态色命中。</summary>
    private static readonly Color Unset = Color.FromArgb(0, 0, 0, 0);

    // 指标标签色只可能在"三色 + 灰"之间切换，按索引存一份就不必每次比 Brush 引用
    private readonly Color[] _labelColors = [Unset, Unset, Unset];
    private readonly Brush[] _labelBrushes = new Brush[3];

    // 数值色：已获取 = 主题前景色，未获取 = 灰（与标签同步）。Unset 是透明色，天然当"未算过"的哨兵
    private readonly Color[] _valueColors = new Color[3];
    private readonly Brush[] _valueBrushes = new Brush[3];

    private string _name = "";
    private string _percentText = "--%";
    private string _sourceText = "";
    private string _subText = "";
    private string _dpiLabel = "";
    private string _dpiValue = "";
    private string _levelSeparator = "";
    private string _levelLabel = "";
    private string _levelValue = "";
    private string _rateSeparator = "";
    private string _rateLabel = "";
    private string _rateValue = "";
    private DeviceKind _iconKind;
    private Color _iconColor;
    private Color _sourceColor = Unset;
    private Color _progressColor = Unset;
    // null 是合法值（正常态用主题前景色），所以初始必须给 Unset——否则首帧被判"没变化"，
    // 刷子会停在构造函数里那个灰色，百分比一直是灰的（2026-11-07 回归，见 runtime-pitfalls §25）
    private Color? _percentColor = Unset;
    private double _progress;
    private bool _hasPointer;
    private bool _isCharging;
    private bool _isOnline = true;
    private Brush _sourceBrush = UiPalette.Brush(UiPalette.Offline);
    private Brush _sourceBackgroundBrush = UiPalette.Brush(UiPalette.Offline);
    private Brush _progressBrush = UiPalette.Brush(UiPalette.Offline);
    private Brush _percentBrush = UiPalette.Brush(UiPalette.Offline);

    public DeviceRowModel()
    {
        for (int i = 0; i < _labelBrushes.Length; i++)
        {
            _labelBrushes[i] = UiPalette.Brush(UiPalette.Offline);
            _valueBrushes[i] = UiPalette.Brush(UiPalette.Offline);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; private set; } = "";

    /// <summary>行首图标只存值，由模板里的 <see cref="Controls.DeviceIconPresenter"/> 现画：
    /// 存画好的元素会让复用行把它带进第二个容器（docs/dev/runtime-pitfalls.md §22）。</summary>
    public DeviceKind IconKind { get => _iconKind; private set => Set(ref _iconKind, value); }

    public Color IconColor { get => _iconColor; private set => Set(ref _iconColor, value); }

    public string Name { get => _name; private set => Set(ref _name, value); }

    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }

    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public string SourceText { get => _sourceText; private set => Set(ref _sourceText, value); }

    public string SubText { get => _subText; private set => Set(ref _subText, value); }

    /// <summary>指针遥测整行是否有内容；false 时整行折叠（键盘/耳机/本机电量不该多出一条空行）。</summary>
    public bool HasPointer { get => _hasPointer; private set => Set(ref _hasPointer, value); }

    public string DpiLabel { get => _dpiLabel; private set => Set(ref _dpiLabel, value); }
    public string DpiValue { get => _dpiValue; private set => Set(ref _dpiValue, value); }
    public string LevelSeparator { get => _levelSeparator; private set => Set(ref _levelSeparator, value); }
    public string LevelLabel { get => _levelLabel; private set => Set(ref _levelLabel, value); }
    public string LevelValue { get => _levelValue; private set => Set(ref _levelValue, value); }
    public string RateSeparator { get => _rateSeparator; private set => Set(ref _rateSeparator, value); }
    public string RateLabel { get => _rateLabel; private set => Set(ref _rateLabel, value); }
    public string RateValue { get => _rateValue; private set => Set(ref _rateValue, value); }

    /// <summary>指标标签色：已获取用类别色，未获取统一灰。数值一律用主题前景色，不参与上色。</summary>
    public Brush DpiLabelBrush => _labelBrushes[0];
    public Brush LevelLabelBrush => _labelBrushes[1];
    public Brush RateLabelBrush => _labelBrushes[2];

    /// <summary>指标数值色：已获取 = 主题前景色，未获取 = 灰（"未获取"不该比真值还显眼）。</summary>
    public Brush DpiValueBrush => _valueBrushes[0];
    public Brush LevelValueBrush => _valueBrushes[1];
    public Brush RateValueBrush => _valueBrushes[2];

    public bool IsCharging { get => _isCharging; private set => Set(ref _isCharging, value); }

    /// <summary>只有离线设备能删：在线设备删掉等于藏起来，插件下一轮又会报回来。</summary>
    public bool IsOnline { get => _isOnline; private set => Set(ref _isOnline, value); }

    /// <summary>连接徽标文字色（有线钢蓝 / 2.4G 紫 / 蓝牙蓝 / 本机青 / 未知灰）。</summary>
    public Brush SourceBrush { get => _sourceBrush; private set => Set(ref _sourceBrush, value); }

    /// <summary>连接徽标胶囊底色（同色压淡）。</summary>
    public Brush SourceBackgroundBrush { get => _sourceBackgroundBrush; private set => Set(ref _sourceBackgroundBrush, value); }

    /// <summary>电量条着色：充电蓝 / 低电红 / 中等橙 / 正常绿 / 离线灰。</summary>
    public Brush ProgressBrush { get => _progressBrush; private set => Set(ref _progressBrush, value); }

    /// <summary>百分比文字：仅异常状态着色（充电蓝 / 低电红 / 离线灰），正常态用主题前景色。
    /// 注意 x:Bind 绑 null 会压掉样式默认前景导致文字不可见，此属性必须非空。</summary>
    public Brush PercentBrush { get => _percentBrush; private set => Set(ref _percentBrush, value); }

    /// <summary>
    /// 把一台设备的当前状态写进本行。只对真正变了的字段发通知——轮询每拍都会调它，
    /// 值没变就不该产生任何布局开销（时间戳变化除外，它本来就该刷新）。
    /// </summary>
    public void Update(
        DeviceStatus source, AppSettings settings, RemainingTimeEstimateResult remainingTime, DateTimeOffset now)
    {
        // 插件没声明类型的按名字猜图标；贴片走同一个方法，两处图标不会再分叉
        var d = DevicePresentation.WithIconKind(DevicePresentation.Apply(source, settings), settings);
        var percent = d.BatteryPercent;
        bool charging = DeviceStatusFormatter.IsCharging(d.Charging);

        string state = DeviceStatusFormatter.ChargeSuffix(d.Charging);
        if (!d.IsOnline) state = " · 离线";

        Id = d.Id;
        Name = d.DisplayName;
        IconKind = d.Kind;
        IconColor = PrimaryColor;
        // 无读数用 "----"：与 "100%" 同宽，右对齐时不会和相邻行差半个字
        PercentText = DeviceStatusFormatter.PercentText(percent, "----");
        Progress = percent ?? 0;
        SourceText = DeviceConnectionInfo.Label(d.Connection, d.Source);
        SubText = $"{d.Protocol}{state}{(remainingTime.IsAvailable ? $" · 约{RemainingTimeEstimator.Format(remainingTime.Remaining!.Value)}剩余" : $" · {remainingTime.Explain()}")} · 更新于 {d.LastUpdated:HH:mm:ss}";
        IsCharging = charging;
        IsOnline = d.IsOnline;

        SetSourceColor(UiPalette.ConnectionTag(d.Connection, d.Source));
        SetProgressColor(d.IsOnline ? UiPalette.BatteryLevel(percent, charging) : UiPalette.Offline);
        // null = 用主题前景色（正常态），非空才换 UiPalette 的固定色
        SetPercentColor(!d.IsOnline ? UiPalette.Offline
            : charging ? UiPalette.Charging
            : percent is <= 20 ? UiPalette.Low
            : null);

        ApplyMetrics(PointerTelemetryFormatter.Metrics(d, now));
    }

    /// <summary>三个指标各自可能缺项：缺的那段写成空串，XAML 里就没有多余的分隔点。</summary>
    private void ApplyMetrics(IReadOnlyList<PointerMetric> metrics)
    {
        bool hasDpi = TryMetric(metrics, PointerMetricKind.Dpi, out var dpi);
        bool hasLevel = TryMetric(metrics, PointerMetricKind.Level, out var level);
        bool hasRate = TryMetric(metrics, PointerMetricKind.Rate, out var rate);

        HasPointer = metrics.Count > 0;
        DpiLabel = hasDpi ? dpi.Label : "";
        DpiValue = hasDpi ? dpi.Value : "";
        LevelSeparator = hasLevel && hasDpi ? " · " : "";
        LevelLabel = hasLevel ? level.Label : "";
        LevelValue = hasLevel ? level.Value : "";
        RateSeparator = hasRate && (hasDpi || hasLevel) ? " · " : "";
        RateLabel = hasRate ? rate.Label : "";
        RateValue = hasRate ? rate.Value : "";

        SetLabelBrush(0, dpi.IsKnown);
        SetLabelBrush(1, level.IsKnown);
        SetLabelBrush(2, rate.IsKnown);
        SetValueBrush(0, dpi.IsKnown);
        SetValueBrush(1, level.IsKnown);
        SetValueBrush(2, rate.IsKnown);
    }

    /// <summary>按类别取指标：三项顺序固定但可能缺项（未声明能力的字段不出现）。</summary>
    private static bool TryMetric(
        IReadOnlyList<PointerMetric> metrics, PointerMetricKind kind, out PointerMetric metric)
    {
        for (int i = 0; i < metrics.Count; i++)
        {
            if (metrics[i].Kind != kind) continue;
            metric = metrics[i];
            return true;
        }
        metric = default;
        return false;
    }

    private void SetSourceColor(Color color)
    {
        if (_sourceColor == color) return;
        _sourceColor = color;
        SourceBrush = UiPalette.Brush(color);
        SourceBackgroundBrush = UiPalette.Brush(UiPalette.Tint(color));
    }

    private void SetProgressColor(Color color)
    {
        if (_progressColor == color) return;
        _progressColor = color;
        ProgressBrush = UiPalette.Brush(color);
    }

    private void SetPercentColor(Color? color)
    {
        if (Nullable.Equals(_percentColor, color)) return;
        _percentColor = color;
        PercentBrush = color is { } value ? UiPalette.Brush(value) : PrimaryBrush;
    }

    private void SetLabelBrush(int index, bool known)
    {
        var color = known ? UiPalette.Metric((PointerMetricKind)index) : UiPalette.Offline;
        if (_labelColors[index] == color) return;
        _labelColors[index] = color;
        _labelBrushes[index] = UiPalette.Brush(color);
        Raise(index switch
        {
            0 => nameof(DpiLabelBrush),
            1 => nameof(LevelLabelBrush),
            _ => nameof(RateLabelBrush),
        });
    }

    /// <summary>比 Color 不比 Brush：<see cref="UiPalette.Brush"/> 每次都 new，比引用永远不等。
    /// 每次重算主题前景色，系统切主题后下一拍就能跟上。</summary>
    private void SetValueBrush(int index, bool known)
    {
        var color = known ? PrimaryColor : UiPalette.Offline;
        if (_valueColors[index] == color) return;
        _valueColors[index] = color;
        _valueBrushes[index] = UiPalette.Brush(color);
        Raise(index switch
        {
            0 => nameof(DpiValueBrush),
            1 => nameof(LevelValueBrush),
            _ => nameof(RateValueBrush),
        });
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static Brush PrimaryBrush =>
        Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush ?? UiPalette.Brush(UiPalette.Offline);

    private static Color PrimaryColor =>
        (Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush)?.Color
        ?? Color.FromArgb(255, 80, 80, 80);
}
