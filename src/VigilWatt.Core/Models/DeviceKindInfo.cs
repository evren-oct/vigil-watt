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
        DeviceKind.Mouse => "鼠标",
        DeviceKind.Keyboard => "键盘",
        DeviceKind.Headset => "耳机",
        DeviceKind.Speaker => "音箱",
        DeviceKind.Gamepad => "手柄",
        DeviceKind.Computer => "电脑",
        DeviceKind.Phone => "手机",
        DeviceKind.Tablet => "平板",
        DeviceKind.Watch => "手表",
        _ => "未识别",
    };
}
