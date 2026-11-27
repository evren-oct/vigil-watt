using VigilWatt.Core.Localization;
namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 设备状态的"人话"格式化与状态判定：设备列表页、桌面小组件、托盘菜单三处共用同一套口径， 避免"充电标志怎么算 / 没读数显示什么"各写一份（CR-24）。
/// 2026/10/10 17:44:33 xliu
/// 纯逻辑（零 WinRT、零 UI），因此可直接被测试项目链入（R8）。
/// </summary>
public static class DeviceStatusFormatter
{
    /// <summary>充电中（**不含"已充满"**）：列表页/小组件/托盘的着色与 ⚡ 标识都以它为准。</summary>
    public static bool IsCharging(ChargingState state) => state == ChargingState.Charging;

    /// <summary>在充电口上（充电中或已充满）：小组件排序用它把这类设备排到末尾。</summary>
    public static bool IsChargingOrFull(ChargingState state) =>
        state is ChargingState.Charging or ChargingState.Full;

    /// <summary>电量文本；无读数用 <paramref name="unknown"/>（设备列表页用 "--"、托盘菜单用 "?"）。</summary>
    public static string PercentText(int? percent, string unknown = "--") =>
        percent is { } p ? $"{p}%" : unknown;

    /// <summary>充电状态后缀（设备列表页用）：" ⚡充电中" / " 已充满" / 空串。</summary>
    public static string ChargeSuffix(ChargingState state) => state switch
    {
        ChargingState.Charging => UiText.Get("S_cd8588eec1", " ⚡充电中"),
        ChargingState.Full => UiText.Get("S_8d458b16f5", " 已充满"),
        _ => "",
    };
}
