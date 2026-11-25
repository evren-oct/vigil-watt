using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.GameSir;

/// <summary>
/// 功 能： 盖世小鸡 (GameSir) G7 Pro CE 手柄状态流的纯帧解析——不碰句柄，便于单测。
/// 2026/10/19 19:50:00 zc165
/// 字段偏移取自官方 Connect 1.16.5 的解析逻辑，依据见 docs/hardware/brands/gamesir.md §1。
/// </summary>
internal static class GameSirCodec
{
    /// <summary>手柄状态流的 report ID（官方 <c>Values.GamepadKeyStateReportId</c> = 18 = 0x12）。</summary>
    public const byte StateReportId = 0x12;

    /// <summary>整帧偏移：充电状态（官方 <c>e[35]</c>）。</summary>
    public const int ChargeStateOffset = 35;

    /// <summary>整帧偏移：电量百分比（官方 <c>e[36]</c>）。</summary>
    public const int BatteryOffset = 36;

    /// <summary>充电状态取值：官方 UI 用 <c>1 == chargeState</c> 判"充电中"。</summary>
    public const byte ChargeStateCharging = 1;

    /// <summary>帧最小长度：至少要能读到电量字节。</summary>
    public const int MinLength = BatteryOffset + 1;

    /// <summary>解析结果。</summary>
    /// <param name="Percent">电量百分比 0~100。</param>
    /// <param name="Charging">是否充电中。</param>
    public readonly record struct State(int Percent, bool Charging);

    /// <summary>
    /// 解析手柄状态帧（report 0x12）：<c>[35]</c> = 充电状态、<c>[36]</c> = 电量百分比。
    ///
    /// **刻意不做 bit7 拆包**：官方对这条状态流就是直接把 <c>[36]</c> 当电量、
    /// 把 <c>[35]==1</c> 当充电中；"电量字节 bit7 = 充电中"是官方另一条
    /// <c>getDeviceInfo</c>（SAPJOYUNI_DEVICEINFO）路径的编码，两者不可混用。
    /// 套用那条规则会把 100% 之类的高位值误读成"充电中 0~27%"。
    /// </summary>
    public static bool TryParseState(byte[] input, out State state)
    {
        state = default;
        if (input.Length < MinLength) return false;
        if (input[0] != StateReportId) return false;

        byte percent = input[BatteryOffset];

        // 0 = 未就绪/占位（刚上电、刚连上），不是"电量 0%"，拒绝它避免误报低电量。
        if (percent is <= 0 or > 100) return false;

        state = new State(percent, input[ChargeStateOffset] == ChargeStateCharging);
        return true;
    }

    /// <summary>充电标志只有"是否充电"一档：收尾规则（未充电且 100% ⇒ 已充满）见 <see cref="ChargingStateMap"/>。</summary>
    public static ChargingState MapCharging(int percent, bool charging)
        => ChargingStateMap.FromChargingFlag(charging, percent);

    /// <summary>配置通道的 report ID（官方命令构造器里数组首元素 = 15 = 0x0F）。</summary>
    public const byte ConfigReportId = 0x0F;

    /// <summary>心跳命令字节（官方 <c>bm(e=0) = [15, 242, e]</c>）。</summary>
    public const byte HeartbeatCommand = 0xF2;

    /// <summary>
    /// 构造一帧心跳，并补零到接口声明的输出长度（实测 64）。
    ///
    /// **这帧是状态流的开关**：真机实测（2026-10-19）——什么都不发时 `0x12` 为 0 帧；
    /// 发一帧心跳后立刻涌出约 250 帧/秒的状态流，且只维持约 2 秒（随后又静音）。
    /// 官方客户端正是每秒发这条心跳来维持流，并用 60 秒轮询读一次电量；
    /// 本插件沿用"60 秒发一帧心跳、读 2 秒"的突发模式，写入量与官方同量级。
    /// </summary>
    public static byte[] BuildHeartbeat(int length = 64)
    {
        if (length < 3) throw new ArgumentOutOfRangeException(nameof(length), "长度至少 3");
        var frame = new byte[length];
        frame[0] = ConfigReportId;
        frame[1] = HeartbeatCommand;
        frame[2] = 0x00;
        return frame;
    }

    /// <summary>官方产品名清理：<c>"GameSir G7 Pro"</c> → <c>"G7 Pro"</c>（避免 UI 里出现两遍品牌名）。</summary>
    public static string CleanProductName(string? product)
    {
        if (string.IsNullOrWhiteSpace(product)) return "";
        var s = product.Trim();
        foreach (var vendor in new[] { "GameSir", "GAMESIR", "Gamesir", "盖世小鸡" })
        {
            if (!s.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)) continue;
            s = s[vendor.Length..].TrimStart(' ', '-', '_');
            break;
        }
        return s.Trim();
    }
}
