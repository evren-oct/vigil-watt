namespace VigilWatt.Core.Plugins.Ganss;

/// <summary>
/// 功 能： 0x05AC:024F 这一族设备的身份归一（按型号给一个稳定 Id，不随 USB 路径变）。
/// 2026/10/26
///
/// 为什么需要（1.3.0 实测暴露的问题）
/// ----------------------------------
/// 设备 Id 默认是 <c>插件名:路径哈希</c>，而路径里的 <c>7&amp;xxxx</c> 段会随
/// "换 USB 口 / 换接收器 / 重新插拔"变化，于是同一只设备每次都变成一个新 Id：
/// - 设备列表里同一个物理设备冒出多行；
/// - 历史断成几段，"剩余时间估算"重新从头算。
///
/// 本机实测（<c>scripts-reverse/probe_pair_identity.py</c>，2026-10-26）：
/// 狼蛛 F108 Pro 的 6 条接口**序列号全为空**（设备根本不报 SN），
/// 所以"按 SN 配对"这条路对它走不通，只能归到型号粒度。
///
/// 判据只用**路径里的接口编号**（<c>mi_03</c>）
/// --------------------------------------
/// 不是不想用产品名（<c>F108Pro Dongle</c>），而是黑名单键与上报 Id 必须同源：
/// 黑名单判定走 <c>IdFor(路径)</c>，上报走 <c>BuildDeviceId(设备信息)</c>。
/// 两者若一个看路径一个看产品名，就可能出现"删不掉的设备"——
/// 按 A 键写黑名单、按 B 键判黑名单。共用同一判据就没有这个坑。
///
/// 取舍（同 <c>betop.md</c> §1.8.2 的鲲鹏20）
/// --------------------------------------
/// 归一只到型号粒度：同一型号的**第二台**设备会被并进同一个 Id。
/// 这是明知的取舍——对 F108 这种不报 SN 的设备，型号粒度已经是最细的可用粒度；
/// 真有两台同型号的用户需要靠"设备自定义名"（<c>DeviceCustomizations</c>）区分显示。
/// 换来的是：换口不新增条目、历史连续、估算不重置。
/// </summary>
internal static class GanssDeviceIdentity
{
    /// <summary>狼蛛 F108 Pro 的归一 Id 段（按型号给，不含路径）。</summary>
    internal const string AulaF108Model = "aula-f108pro";

    /// <summary>GANSS GS3104T-Pro 的归一 Id 段。</summary>
    internal const string GanssGs3104tModel = "ganss-gs3104t-pro";

    /// <summary>
    /// 按路径归一：命中本族被认领的接口（<c>mi_03</c>）就返回固定 Id，
    /// 否则返回 null（调用方回退路径哈希）。
    /// </summary>
    /// <param name="devicePath">HID 接口路径。</param>
    /// <param name="pluginNameLower">插件名（小写），用于拼 Id 前缀。</param>
    internal static string? IdForPath(string devicePath, string pluginNameLower)
        => GanssCodec.IsCapturedInterfaceIndex(devicePath)
            ? $"{pluginNameLower}:{AulaF108Model}"
            : null;

    /// <summary>
    /// 按产品名归一（只用于展示或将来需要更细粒度时）。
    /// 0x05AC:024F 在 HID 层同指纹，区分品牌与型号只剩产品名这一条路。
    /// </summary>
    internal static string? ModelIdByProduct(string product, string pluginNameLower)
    {
        if (GanssCodec.LooksLikeAulaF108(product)) return $"{pluginNameLower}:{AulaF108Model}";
        if (GanssCodec.LooksLikeGanss(product)) return $"{pluginNameLower}:{GanssGs3104tModel}";
        return null;
    }
}
