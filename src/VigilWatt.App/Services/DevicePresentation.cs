using VigilWatt.Core.Models;
using VigilWatt.Core.Settings;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 将用户自定义值应用到设备展示快照。
/// 2026/10/18 12:19:45 20211
/// </summary>
public static class DevicePresentation
{
    public static DeviceStatus Apply(DeviceStatus source, AppSettings settings)
        => DeviceCustomizationResolver.Apply(source, settings.DeviceCustomizations);

    /// <summary>
    /// 插件没声明类型（雷柏、迈从这类复合 HID）时按显示名猜图标，写回 <see cref="DeviceStatus.Kind"/>。
    /// 贴片是 GDI 直绘、拿不到设备列表那套行模型，不提前算好就只能画出「未识别」。
    /// ⚠️ 入参必须是 <see cref="Apply"/> 的产物（已是 Clone），否则会改到数据源共享的实例。
    /// </summary>
    public static DeviceStatus WithIconKind(DeviceStatus applied, AppSettings settings)
    {
        // 用户显式选过图标（含选「通用」）就不猜，否则会覆盖掉他的选择
        if (applied.Kind != DeviceKind.Unknown || HasIconOverride(applied.Id, settings)) return applied;
        applied.Kind = DeviceKindGuesser.Guess(applied.DisplayName);
        return applied;
    }

    public static bool HasIconOverride(string id, AppSettings settings) =>
        settings.DeviceCustomizations?.TryGetValue(id, out var custom) == true && custom.IconKind.HasValue;

    public static string DisplayName(string id, string fallback, AppSettings settings) =>
        DeviceCustomizationResolver.DisplayName(id, fallback, settings.DeviceCustomizations);
}
