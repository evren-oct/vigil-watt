using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VigilWatt.App.Services;
using VigilWatt.Core.Models;
using Windows.UI;

namespace VigilWatt.App.Controls;

/// <summary>
/// 功 能： 设备列表行的类型图标：按 Kind 现画，每个行容器各持一个实例。
/// 2026/10/25 12:05:00 20211
/// 不能把画好的元素放进行模型：行复用会把同一个元素交给第二个 ContentPresenter（runtime-pitfalls.md §22）。
/// </summary>
public sealed class DeviceIconPresenter : ContentControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(DeviceKind), typeof(DeviceIconPresenter),
        new PropertyMetadata(DeviceKind.Unknown, OnIconChanged));

    public static readonly DependencyProperty IconColorProperty = DependencyProperty.Register(
        nameof(IconColor), typeof(Color), typeof(DeviceIconPresenter),
        new PropertyMetadata(Microsoft.UI.Colors.Gray, OnIconChanged));

    public DeviceKind Kind
    {
        get => (DeviceKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public DeviceIconPresenter()
    {
        // 先画一个占位：没有内容时 ContentControl 高度为 0，绑定到位那一刻会跳一下
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
        Rebuild();
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DeviceIconPresenter)d).Rebuild();

    private void Rebuild() => Content = DeviceIconFactory.Scaled(Kind, IconColor, 28);
}
