using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>连接方式：<c>[1]</c> 低 4 位。</summary>
internal enum RapooLinkMode
{
    /// <summary>非 0/2 的未知值，按 2.4G 保守处理。</summary>
    Unknown = -1,
    /// <summary>2.4G 无线（<c>[1]</c> 低 4 位 = 0）——此时 <c>[18].bit7</c> 才是有效的 RF 链路位。</summary>
    Wireless2G = 0,
    /// <summary>有线（<c>[1]</c> 低 4 位 = 2）——没有 RF 链路，<c>[18].bit7</c> 恒 0 属正常。</summary>
    Wired = 2,
}

/// <summary>0x07 状态帧的解析结果类型。</summary>
internal enum RapooFrameKind
{
    /// <summary>不是 0x07 状态帧（其余字段无意义）。</summary>
    NotStatus,
    Status,
    /// <summary>状态帧但电量越界（0 / &gt;100，实测为休眠瞬态）：不能用来更新读数，但链路位仍然可信。</summary>
    StatusInvalidPercent,
}

/// <summary>0x07 状态帧的解析结果。Kind 决定其余字段是否有意义。</summary>
internal readonly record struct RapooStatus(
    RapooFrameKind Kind, int Percent, ChargingState Charging,
    byte ChargeFlag, byte RfByte, string Link, RapooLinkMode LinkMode)
{
    /// <summary>
    /// 链路是否断开。**只在 2.4G 模式下有意义**：有线时不存在 RF 链路，bit7 恒 0 属正常
    /// （实测有线帧 rf=0x00），不能当断链。详见 docs/hardware/rapoo.md §4.1。
    /// </summary>
    public bool LinkDown => LinkMode != RapooLinkMode.Wired && RapooCodec.IsLinkDown(RfByte);
}

/// <summary>
/// 功 能： 雷柏 2.4G 状态帧（report 0x07，19 字节）的**纯帧解析**，不碰句柄、不写日志，便于单测。
/// 2026/10/08 22:25:16 xliu
/// 帧布局见 docs/hardware/rapoo.md §1；有线模式（bit7 恒 0、连接方式判据）见同文 §4.1。
/// </summary>
internal static class RapooCodec
{
    public const byte StatusReportId = 0x07;
    public const int StatusReportLength = 19;

    /// <summary><c>[18].bit7</c> = "RF 已连接"（协议定义）。bit7=0 ⟺ dongle 与鼠标的 2.4G 链路断开。</summary>
    public static bool IsLinkDown(byte rfByte) => (rfByte & 0x80) == 0;

    /// <summary>由 <c>[1]</c> 低 4 位还原连接方式。</summary>
    public static RapooLinkMode LinkModeOf(int linkRaw) => linkRaw switch
    {
        (int)RapooLinkMode.Wireless2G => RapooLinkMode.Wireless2G,
        (int)RapooLinkMode.Wired => RapooLinkMode.Wired,
        _ => RapooLinkMode.Unknown,
    };

    /// <summary>
    /// 解析一帧。<see cref="RapooStatus.Kind"/> 为 <see cref="RapooFrameKind.NotStatus"/> 时说明
    /// 这不是 0x07 状态帧（其余字段无意义）。
    /// </summary>
    public static RapooStatus Parse(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + StatusReportLength) continue;
            if (input[off] != StatusReportId) continue;

            int linkRaw = input[off + 1] & 0x0F;
            byte chargeFlag = input[off + 7];
            byte pct = input[off + 8];
            byte rfByte = input[off + 18];
            var mode = LinkModeOf(linkRaw);
            string link = mode switch
            {
                RapooLinkMode.Wireless2G => "2.4G",
                RapooLinkMode.Wired => "有线",
                _ => $"link{linkRaw}",
            };

            if (pct is 0 or > 100)
                return new RapooStatus(RapooFrameKind.StatusInvalidPercent, pct,
                    ChargingState.Unknown, chargeFlag, rfByte, link, mode);

            // [7] 充电标志实测：0x01=放电、0x02=充电；0x00 是切换瞬间的过渡值。
            // 满电优先判「充满」：满电后放充电底座上 [7] 会反复横跳（0/1/2），
            // 让 0x02 优先就会在 Charging/Full 之间横跳，图标连闪。见 docs/hardware/rapoo.md §5.2。
            var charging = pct >= 100 ? ChargingState.Full
                : chargeFlag == 0x02 ? ChargingState.Charging
                : ChargingState.Discharging;
            return new RapooStatus(RapooFrameKind.Status, pct, charging, chargeFlag, rfByte, link, mode);
        }
        return new RapooStatus(RapooFrameKind.NotStatus, 0, ChargingState.Unknown, 0, 0, "",
            RapooLinkMode.Unknown);
    }

    /// <summary>
    /// 充电状态去抖后的结果：判定值 + 要写回的状态。
    /// </summary>
    /// <param name="Charging">去抖后的充电状态。</param>
    /// <param name="LastFlag">要写回的「上一帧 [7] 标志」。</param>
    /// <param name="Stable">要写回的「是否已连续两帧充电中」。</param>
    public readonly record struct ChargeDecision(
        ChargingState Charging, byte LastFlag, bool Stable);

    /// <summary>
    /// 充电状态去抖：**连续两帧都是「充电中」才判充电**；满电一律判「充满」。
    /// 设备在状态切换瞬间会密集连发过渡帧，单帧直判会让充电图标连闪。
    /// 详见 docs/hardware/rapoo.md §5.2。
    /// </summary>
    public static ChargeDecision ResolveCharging(
        byte chargeFlag, byte lastFlag, bool wasStable, int percent)
    {
        if (percent >= 100)
            return new ChargeDecision(ChargingState.Full, chargeFlag, false);

        bool isCharge = chargeFlag == 0x02;
        bool stable = isCharge && lastFlag == 0x02;
        return new ChargeDecision(
            stable ? ChargingState.Charging : ChargingState.Discharging,
            chargeFlag, stable);
    }

    /// <summary>
    /// 生成显示名。雷柏的电量帧挂在键盘集合上，靠"是不是鼠标"改名字；
    /// 名字里带 "Mouse" 是有意的——UI 靠名字猜图标。详见 docs/hardware/rapoo.md §4.2、§5.3。
    /// </summary>
    public static string DisplayName(string product, bool siblingIsMouse, string vidPid = "")
    {
        var p = product ?? "";
        if (p.Contains("Mouse", StringComparison.OrdinalIgnoreCase))
            return $"雷柏 {p}";
        if (siblingIsMouse)
        {
            // "Rapoo Gaming Device" 这类设备级产品串不带键鼠信息，认到鼠标就补上 Mouse
            if (p.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
                || p.Contains("Gaming Device", StringComparison.OrdinalIgnoreCase))
                return "雷柏 Rapoo Mouse";
        }
        if (string.IsNullOrEmpty(p))
            return string.IsNullOrEmpty(vidPid) ? "雷柏" : $"雷柏 ({vidPid})";
        return $"雷柏 {p}";
    }
}
