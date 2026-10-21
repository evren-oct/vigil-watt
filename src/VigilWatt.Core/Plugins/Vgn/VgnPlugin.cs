using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Vgn;

/// <summary>
/// 功 能： VGN（蜻蜓 / Dragonfly）2.4G 鼠标的电池插件。
/// 2026/10/19 新增
///
/// 通道：厂商接口 <c>usagePage 0xFFC1 / usage 0x0001</c>，in/out = 64 字节，
/// 请求走 report ID <c>0xB3</c>（官方驱动 <c>kcMs2</c> 的 reportId=179）。
/// 电量不是一个独立命令，而是随"设备配置帧"（cmd <c>0x06</c>）一起回来，
/// 位置见 <see cref="VgnCodec"/>。
///
/// 同一只鼠标插线后会换 PID（<c>0x1A23</c> → <c>0x1026</c>）并少掉那个 33 字节接口，
/// 两个 PID 都要认，否则充电期间设备会从列表里消失。
/// </summary>
public sealed class VgnPlugin : BatteryPluginBase
{
    /// <summary>插件开关的键（插件开关即写入授权）。</summary>
    private const string SettingsKey = "Vgn";

    public override string Name => SettingsKey;
    public override string DisplayName => "VGN";
    public override string DisplayNameEn => "VGN";
    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>该协议没有可用的自发推送（只见过命令应答，没见过状态广播），只能主动问。</summary>
    protected override bool SupportsPassiveListen => false;

    /// <summary>
    /// 离线策略：**不熔断**，理由与 <c>PmoPlugin</c> 相同——
    /// 基类的熔断链是"连续失败 → 停写 → 被动监听的空闲 tick 里半开"，
    /// 本插件没有被动监听，半开永远不触发，一旦睡过一次退避就会逐次加码（30s→300s）。
    /// 鼠标空闲一两分钟就睡，这条路径会把恢复拖到几分钟，所以改由 <see cref="PollDevice"/> 自己数：
    /// 够了只 <c>RequestOffline</c>（标离线、不停写），下一轮照常发帧。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
        // 连续 10 次（约 10 分钟）没应答才标离线；鼠标睡一会儿不该在界面上闪成"离线"
        MaxFailures = 10,
    };

    /// <summary>探测会写 1 帧。冷却 20 秒，睡着时不会被 15 秒发现节拍反复戳。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(20);

    /// <summary>参与 15 秒快速发现：插上 dongle 后不必等满 60 秒才出现。</summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>
    /// 只认"VID + 厂商 usage + 长度"三件套，两代协议各一套：
    /// VGN 自家号段的 kc2（<c>0x391D</c> + <c>0xFFC1/0x0001</c> + 64/64），
    /// 与 Compx 号段的 y2（<c>0x3554</c> + <c>0xFF02/0x0002</c> + 17/17，蜻蜓 F2 大师+）。
    /// 同一 VID 下还有别的接口（kc2 的 0x008C 只读接口、y2 的 FF04 feature 接口），长度校验能把它们排除掉。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => VgnCodec.IsMatch(info.VendorId, info.ProductId, info.UsagePage, info.Usage,
               info.InputReportLength, info.OutputReportLength)
           || VgnY2Codec.IsMatch(info.VendorId, info.ProductId, info.UsagePage, info.Usage,
               info.InputReportLength, info.OutputReportLength);

    /// <summary>这条接口属于 y2 协议（蜻蜓 F2 大师+ / Compx dongle）。</summary>
    private static bool IsY2(HidDeviceInfo info)
        => VgnY2Codec.IsMatch(info.VendorId, info.ProductId, info.UsagePage, info.Usage,
            info.InputReportLength, info.OutputReportLength);

    /// <summary>系统给的名字是 <c>Geek Yanxuan | Dragonfly F1 V5 Turbo+</c>，去掉厂商前缀。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        var product = info.Product;
        if (string.IsNullOrWhiteSpace(product)) return "VGN 鼠标";

        const string separator = "|";
        int idx = product.IndexOf(separator, StringComparison.Ordinal);
        return idx >= 0 && idx + 1 < product.Length
            ? product[(idx + 1)..].Trim()
            : product.Trim();
    }

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (!IsWritesEnabled(SettingsKey))
        {
            Log.Once(LogLevel.Info, $"{handle.Info.DevicePath}|vgn-no-permit",
                $"VGN: 未开启写入许可，跳过主动查询: {handle.Info.DevicePath}");
            return null;
        }
        Log.ResetOnce($"{handle.Info.DevicePath}|vgn-no-permit");

        var reading = IsY2(handle.Info) ? QueryY2(handle) : Query(handle);
        if (reading == null)
        {
            Log.Debug($"VGN 探测无应答: {handle.Info.DevicePath}");
            return null;
        }

        Log.Info($"VGN 探测成功: 电量={reading.Value.Percent} 充电={reading.Value.Charging}");
        return (IsY2(handle.Info) ? "vgn-y2" : "vgn-kc2", reading.Value.Percent, reading.Value.Charging);
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var reading = IsY2(dev.Handle.Info) ? QueryY2(dev.Handle) : Query(dev.Handle);
        if (reading == null)
        {
            // 失败自己数（基类那条路会熔断停写，见 Offline 注释）：够了只标离线，不停写
            if (++dev.ConsecutiveFailures >= Offline.MaxFailures) RequestOffline(dev);
            return null;
        }

        dev.ConsecutiveFailures = 0;
        return (reading.Value.Percent, reading.Value.Charging);
    }

    /// <summary>
    /// 依次尝试可用的 output report ID，第一个能写成功的就用它读电量。
    ///
    /// 为什么要试：同一套协议在不同固件上用的 ID 不一样（本机 0xB3 / 0xB5 都能写，
    /// 官方驱动固定用 0xB3），而已知的 0x008C 接口只有 0xB2 且不应答。
    /// 写一个该接口没声明的 ID 会直接被驱动拒（ERROR_INVALID_PARAMETER 87），
    /// 所以"写成功"本身就是这道接口能不能用的判据。
    /// </summary>
    private static (int Percent, ChargingState Charging)? Query(HidDeviceHandle handle)
    {
        foreach (byte reportId in ReportIdCandidates(handle))
        {
            if (!handle.WriteOutput(VgnCodec.BuildConfigRequest(reportId),
                    TimeSpan.FromMilliseconds(300), quiet: true))
                continue;

            var reading = ReadBattery(handle);
            if (reading != null) return reading;
        }
        return null;
    }

    /// <summary>
    /// y2 协议（蜻蜓 F2 大师+）：report ID 固定 <c>0x08</c>，发一条 <c>BatteryLevel</c>（cmd 0x04）等应答。
    /// 与 kc2 不同，这里不需要试 report ID——官方驱动写死 8，写别的 ID 会被驱动直接拒。
    /// </summary>
    private static (int Percent, ChargingState Charging)? QueryY2(HidDeviceHandle handle)
    {
        if (!handle.WriteOutput(VgnY2Codec.BuildCommand(VgnY2Codec.CmdBatteryLevel),
                TimeSpan.FromMilliseconds(500), quiet: true))
        {
            Log.Debug($"VGN y2 写帧失败（接口不收 17 字节 0x08 帧）: {handle.Info.DevicePath}");
            return null;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1200);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            if (VgnY2Codec.TryParseBattery(input, out var percent, out var charging, out int voltageMv))
            {
                Log.Debug($"VGN y2 电量应答: {percent}% {charging} {voltageMv}mV 原始={Log.Hex(input)}");
                return (percent, charging);
            }

            // 收到了却解不出来：最常见的两种是"报错回显"和"长度/首字节不对"，必须留证才排得动
            Log.Debug($"VGN y2 忽略非电量帧（{input.Length} 字节）: {Log.Hex(input)}");
        }
        Log.Debug($"VGN y2 窗口内无任何应答: {handle.Info.DevicePath}");
        return null;
    }

    /// <summary>候选 report ID：驱动报的优先（个别固件会变），再补两个真机验证过的常量。</summary>
    private static IEnumerable<byte> ReportIdCandidates(HidDeviceHandle handle)
    {
        var seen = new HashSet<byte>();
        byte? hinted = null;
        try { hinted = handle.GetOutputReportId(); } catch { /* caps 拿不到就走常量 */ }

        if (hinted is { } id && id != 0 && seen.Add(id)) yield return id;
        if (seen.Add(VgnCodec.ReportId)) yield return VgnCodec.ReportId;
        if (seen.Add(VgnCodec.ReportIdAlt)) yield return VgnCodec.ReportIdAlt;
    }

    /// <summary>在 600ms 窗口里收帧并解析，非电量帧（错误回显、状态广播）直接跳过。</summary>
    private static (int Percent, ChargingState Charging)? ReadBattery(HidDeviceHandle handle)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(600);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(200));
            if (input == null) continue;

            if (VgnCodec.TryParseBattery(input, out var percent, out var charging))
            {
                Log.Debug($"VGN 电量应答: {percent}% {charging} 原始={Log.Hex(input)}");
                return (percent, charging);
            }
        }
        return null;
    }
}
