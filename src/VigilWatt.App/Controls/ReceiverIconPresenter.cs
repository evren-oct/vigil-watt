using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VigilWatt.App.Services;
using Windows.UI;

namespace VigilWatt.App.Controls;

/// <summary>
/// 功 能： 设备列表行的接收器体系图标：按目录给的 key 现画，每个行容器各持一个实例。
/// 2026/11/11 11:58:00 20211
/// 与 <see cref="DeviceIconPresenter"/> 同因：画好的元素放进行模型会被行复用带进第二个容器（runtime-pitfalls.md §22）。
/// </summary>
public sealed class ReceiverIconPresenter : ContentControl
{
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(ReceiverIconPresenter),
        new PropertyMetadata(null, OnIconChanged));

    public static readonly DependencyProperty IconColorProperty = DependencyProperty.Register(
        nameof(IconColor), typeof(Color), typeof(ReceiverIconPresenter),
        new PropertyMetadata(Microsoft.UI.Colors.Gray, OnIconChanged));

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public ReceiverIconPresenter()
    {
        // 先画一个占位：没有内容时 ContentControl 高度为 0，绑定到位那一刻会跳一下
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
        Rebuild();
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ReceiverIconPresenter)d).Rebuild();

    private void Rebuild() => Content = ReceiverIconFactory.Scaled(IconKey, IconColor, ReceiverIconFactory.BadgeSize);
}
