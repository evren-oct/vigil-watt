using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>
/// 功 能： 已实现的雷柏协议族注册表。顺序即"未收录设备"的探测顺序；目录命中的设备只试它那一个族。
/// 2026/11/01 22:35:00 xliu
/// 族清单与被动可行性见 docs/hardware/rapoo-catalog.md §2；加一族只需在 CreateAll 里加一行。
/// </summary>
internal static class RapooFamilies
{
    public static RapooFamily[] CreateAll() =>
    [
        new StatusReport7Family(),
        new Telink8273Family(),
        new GcomeApFamily(),
        new TelinkKeyboardFamily(),
        new Ch585mFamily(),
    ];
}

/// <summary>
/// 功 能： V2 世代鼠标 user-data —— `VT_nrf54L`(report 7) / `LM20RT`(report 7) / `VT_CH584`(report 8)，纯被动零写入。
/// 2026/11/01 22:20:00 xliu
/// 三个官方 driver 的字段偏移逐字节相同，只差 report ID，所以由一个族统一吃下；帧布局见 docs/hardware/rapoo.md §1。
/// </summary>
internal sealed class StatusReport7Family : RapooFamily
{
    /// <summary>
    /// 按设备路径隔离的充电去抖状态。**必须按设备隔离**：同一插件实例持有多台设备，
    /// 共用一份会让两台的帧互相串味（R3）。
    /// </summary>
    private readonly ConcurrentDictionary<string, (byte Flag, bool Stable)> _chargeState =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>`VT_CH584`（VT1 SE / VT7 SE）把同一套 user-data 放在 report 0x08 上。</summary>
    private const byte Ch584ReportId = 0x08;

    public override string[] Protocols => ["status-report7", "lm20rt", "vt-ch584"];

    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>
    /// 候选接口：vendor 通道（雷柏把 0xFF00 放在 UsagePage 或 Usage 字段，两边都查），
    /// 且输入长度**正好 19 字节**（状态报表 0x07 的长度）。
    /// 收敛到 <c>==19</c> 而非"≥19 就试试"：本机 dongle 的 32B/33B 两条 vendor 通道从不发 0x07，
    /// 放行它们只会每次首扫各白等一整个探测窗口。
    /// </summary>
    public override bool MatchesShape(RapooInterfaceShape shape)
        => (shape.UsagePage == 0xFF00 || shape.Usage == 0xFF00)
           && shape.InputLength == RapooCodec.StatusReportLength;

    /// <summary>探测 = 被动听一个窗口，听不到 = 鼠标睡着，返回 null 进冷却，下一轮再试。不发任何字节。</summary>
    public override Task<(int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle, TimeSpan window)
        => ListenAsync(handle, window, input =>
        {
            var status = RapooCodec.Parse(input, RapooCodec.StatusReportId, Ch584ReportId);
            return status.Kind == RapooFrameKind.Status ? (status.Percent, status.Charging) : null;
        });

    public override RapooPassiveResult? ParsePassive(RapooDeviceRef dev, byte[] input)
    {
        var status = RapooCodec.Parse(input, RapooCodec.StatusReportId, Ch584ReportId);
        if (status.Kind == RapooFrameKind.NotStatus) return null;

        // CR-28：心跳约 3 秒一条，逐帧 Info + Hex 会冲到约 2.8 万行/天、把 2MB 滚动历史冲掉。
        // 改成只在链路/充电/电量变化时记一条。**key 里带 RF 字节**：否则断链帧（bit7 由 1 变 0）
        // 不会触发记录，只在日志里看到"标记离线"却找不到断链那一帧，排查时抓瞎。
        var linkNote = status.LinkMode == RapooLinkMode.Wired
            ? "有线(无RF链路,bit7不适用)"
            : $"bit7连接={!status.LinkDown}";
        Log.InfoOnChange($"rapoo-status:{dev.Id}",
            $"{status.Link}|{status.ChargeFlag}|{status.Percent}|{status.RfByte}",
            $"雷柏 状态帧: 连接={status.Link} 充电标志={status.ChargeFlag} 电量={status.Percent}% " +
            $"RF=0x{status.RfByte:X2}({linkNote} RSSI={status.RfByte & 0x7F})");

        // 链路位与电量无关：电量越界的休眠瞬态帧也要能标记/撤销离线候选。
        var hint = status.LinkDown ? RapooLinkHint.Down : RapooLinkHint.Up;

        if (status.Kind == RapooFrameKind.StatusInvalidPercent)
        {
            if (Log.IsEnabled(LogLevel.Debug))
                Log.Debug($"雷柏 电量越界/归零（疑似休眠瞬态），丢弃: {Log.Hex(input)}");
            return new RapooPassiveResult(null, hint);
        }

        return new RapooPassiveResult(
            new RapooReading(status.Percent, ResolveCharging(dev.DevicePath, status)), hint);
    }

    /// <summary>充电状态去抖，规则见 <see cref="RapooCodec.ResolveCharging"/>；状态按设备路径存于 <see cref="_chargeState"/>。</summary>
    private ChargingState ResolveCharging(string devicePath, RapooStatus status)
    {
        var (flag, stable) = _chargeState.GetOrAdd(devicePath, (0xFF, false));
        var d = RapooCodec.ResolveCharging(status.ChargeFlag, flag, stable, status.Percent);
        _chargeState[devicePath] = (d.LastFlag, d.Stable);
        return d.Charging;
    }

    public override void Prune(IReadOnlyList<HidDeviceInfo> present)
    {
        var alive = present.Select(d => d.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _chargeState.Keys.Where(k => !alive.Contains(k)).ToList())
            _chargeState.TryRemove(key, out _);
    }

    public override void Reset() => _chargeState.Clear();
}

/// <summary>
/// 功 能： Telink 8273 鼠标（MT 系列）—— report `0xBB` 被动上报，零写入。
/// 2026/11/01 22:35:00 xliu
/// 帧布局见 docs/hardware/rapoo-catalog.md §2；本机无 MT 系列设备，未真机验证。
/// </summary>
internal sealed class Telink8273Family : RapooFamily
{
    public override string[] Protocols => ["telink-8273"];

    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>与键盘族共用 `0xFF00:0x0E`，只能靠目录分流；长度按"report ID + 6 字节载荷"给下限。</summary>
    public override bool MatchesShape(RapooInterfaceShape shape)
        => (shape.UsagePage == 0xFF00 || shape.Usage == 0xFF00)
           && (shape.UsagePage == 0x000E || shape.Usage == 0x000E)
           && shape.InputLength >= 7;

    public override Task<(int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle, TimeSpan window)
        => ListenAsync(handle, window, RapooTelink8273Codec.Parse);

    public override RapooPassiveResult? ParsePassive(RapooDeviceRef dev, byte[] input)
        => RapooTelink8273Codec.Parse(input) is { } r
            ? new RapooPassiveResult(new RapooReading(r.Percent, r.Charging), RapooLinkHint.None)
            : null;
}

/// <summary>
/// 功 能： GCOME_AP 键盘（Ace 68 Turbo / VT68）—— `FA FB 07` 电量帧，零写入。
/// 2026/11/01 22:35:00 xliu
/// 帧布局见 docs/hardware/rapoo-catalog.md §2；本机无键盘，未真机验证。
/// </summary>
internal sealed class GcomeApFamily : RapooFamily
{
    public override string[] Protocols => ["gcome-ap"];

    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>这两个键盘的配置集合是 `0x0001:0x00`（不是厂商页），与雷柏其它族完全不同。</summary>
    public override bool MatchesShape(RapooInterfaceShape shape)
        => shape.UsagePage == 0x0001 && shape.Usage == 0x0000 && shape.InputLength >= 6;

    public override Task<(int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle, TimeSpan window)
        => ListenAsync(handle, window, RapooGcomeApCodec.Parse);

    public override RapooPassiveResult? ParsePassive(RapooDeviceRef dev, byte[] input)
        => RapooGcomeApCodec.Parse(input) is { } r
            ? new RapooPassiveResult(new RapooReading(r.Percent, r.Charging), RapooLinkHint.None)
            : null;
}

/// <summary>
/// 功 能： TelinkKeyboard（A83 Air、V700diy 系列）—— 载荷首字节 `0x01` 的用户数据帧，零写入。
/// 2026/11/01 22:35:00 xliu
/// 布局见 docs/hardware/rapoo-catalog.md §2；V700diy 子变体靠 PID 分流，本机无键盘，未真机验证。
/// </summary>
internal sealed class TelinkKeyboardFamily : RapooFamily
{
    public override string[] Protocols => ["telink-keyboard"];

    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>与 8273 鼠标族共用 `0xFF00:0x0E`；帧长按"marker + 10 字节载荷"给下限，靠目录分流。</summary>
    public override bool MatchesShape(RapooInterfaceShape shape)
        => (shape.UsagePage == 0xFF00 || shape.Usage == 0xFF00)
           && (shape.UsagePage == 0x000E || shape.Usage == 0x000E)
           && shape.InputLength >= 10;

    public override Task<(int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle, TimeSpan window)
        => ListenAsync(handle, window, input => RapooKeyboardCodec.ParseTelink(input, handle.Info.ProductId));

    public override RapooPassiveResult? ParsePassive(RapooDeviceRef dev, byte[] input)
        => RapooKeyboardCodec.ParseTelink(input, dev.ProductId) is { } r
            ? new RapooPassiveResult(new RapooReading(r.Percent, r.Charging), RapooLinkHint.None)
            : null;
}

/// <summary>
/// 功 能： CH585M 键盘（ESK900 系列）—— 载荷首字节 `0x01` 的用户数据帧（布局与 Telink 不同），零写入。
/// 2026/11/01 22:35:00 xliu
/// 布局见 docs/hardware/rapoo-catalog.md §2；本机无键盘，未真机验证。
/// </summary>
internal sealed class Ch585mFamily : RapooFamily
{
    public override string[] Protocols => ["ch585m"];

    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>
    /// 与 `status-report7` 共用 `0xFF00:0x02`，所以必须排除 19 字节那条（鼠标状态帧）：
    /// 否则鼠标睡着时会白等本族一整个探测窗口。
    /// </summary>
    public override bool MatchesShape(RapooInterfaceShape shape)
        => (shape.UsagePage == 0xFF00 || shape.Usage == 0xFF00)
           && (shape.UsagePage == 0x0002 || shape.Usage == 0x0002)
           && shape.InputLength >= 10
           && shape.InputLength != RapooCodec.StatusReportLength;

    public override Task<(int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle, TimeSpan window)
        => ListenAsync(handle, window, RapooKeyboardCodec.ParseCh585m);

    public override RapooPassiveResult? ParsePassive(RapooDeviceRef dev, byte[] input)
        => RapooKeyboardCodec.ParseCh585m(input) is { } r
            ? new RapooPassiveResult(new RapooReading(r.Percent, r.Charging), RapooLinkHint.None)
            : null;
}
