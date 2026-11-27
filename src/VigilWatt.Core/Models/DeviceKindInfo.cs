using VigilWatt.Core.Localization;
namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 设备类型的中文展示名（详情页身份卡、图标选择器共用）。
/// 2026/11/11 22:12:00 20211
/// 类型只认插件声明的 <see cref="DeviceKind"/>，不按显示名猜（见 docs/project/architecture.md §6）。
/// </summary>
public static class DeviceKindInfo
{
    public static string Label(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => UiText.Get("S_dec974e26a", "鼠标"),
        DeviceKind.Keyboard => UiText.Get("S_e2766fb28a", "键盘"),
        DeviceKind.Headset => UiText.Get("S_d5ca969dc3", "耳机"),
        DeviceKind.Speaker => UiText.Get("S_049c7adb04", "音箱"),
        DeviceKind.Gamepad => UiText.Get("S_c1f19102a6", "手柄"),
        DeviceKind.Computer => UiText.Get("S_ec87a4b867", "电脑"),
        DeviceKind.Phone => UiText.Get("S_9c01ad0911", "手机"),
        DeviceKind.Tablet => UiText.Get("S_ca99b45978", "平板"),
        DeviceKind.Watch => UiText.Get("S_68075dc4ab", "手表"),
        _ => UiText.Get("S_face982a4a", "未识别"),
    };
}
