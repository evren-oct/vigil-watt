using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace VigilWatt.App.Controls;

/// <summary>
/// 功 能： 按行排布、放不下就换行，用于设备胶囊这类宽度随内容走的元素。
/// 2026/10/28 21:58:12 20211
/// WinUI 3 没有内置 WrapPanel（VariableSizedWrapGrid 要求固定格宽，胶囊宽度不齐）。
/// </summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WrapPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        // 竖向 StackPanel 给的高度是 Infinity、宽度是确定值；宽度也拿不到时退化成单行
        double limit = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        double x = 0, y = 0, rowHeight = 0, widest = 0;

        foreach (var child in Children)
        {
            // 传 Infinity 让它按内容撑开：胶囊宽度必须由文字决定，不能均分
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > limit)
            {
                widest = Math.Max(widest, x - HorizontalSpacing);
                x = 0;
                y += rowHeight + VerticalSpacing;
                rowHeight = 0;
            }

            x += size.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        widest = Math.Max(widest, Math.Max(0, x - HorizontalSpacing));
        return new Size(double.IsInfinity(limit) ? widest : limit, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double limit = finalSize.Width;
        double x = 0, y = 0, rowHeight = 0;

        foreach (var child in Children)
        {
            // 单个元素比整行还宽时夹到行宽，否则会画出边界（设备名可能很长）
            double width = Math.Min(child.DesiredSize.Width, limit);
            if (x > 0 && x + width > limit)
            {
                x = 0;
                y += rowHeight + VerticalSpacing;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, width, child.DesiredSize.Height));
            x += width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        return finalSize;
    }
}
