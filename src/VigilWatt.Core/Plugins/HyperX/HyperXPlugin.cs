using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.HyperX;

/// <summary>
/// 功 能： HyperX Cloud Flight 2 耳机电量插件——零写入被动监听 FF90 口的状态推送帧。
/// 2026/10/20 22:20:00 Yang
/// 帧结构与"充电阶段字段被证伪"的过程见 docs/hardware/hyperx-cf2.md；编解码在 Cf2Codec。
/// </summary>
public sealed class HyperXPlugin : BatteryPluginBase
{
    public override string Name => "HyperX";             // AppSettings.PluginEnabled 的 key
    public override string DisplayName => "HyperX";
    public override string DisplayNameEn => "HyperX";
    public override DeviceKind Kind => DeviceKind.Headset;

    // 纯被动零写入 → 可参与快速发现节拍
    public override bool SupportsFastDiscovery => true;
    protected override bool SupportsPassiveListen => true;

    // 静置时状态流只剩心跳，沉默兜底放宽到 3 分钟
    protected override OfflinePolicy Offline => new()
    {
        Silence = TimeSpan.FromSeconds(180),
        CountPollFailures = false,      // 纯被动，不发帧，不做失败记账
    };

    private const ushort VendorId = 0x03F0;               // HP（≠初代 Kingston 0x0951）
    private const ushort ReceiverPid = 0x0AC1;            // CF2 dongle
    private const ushort StatusUsagePage = 0xFF90;        // 状态流接口

    /// <summary>只认状态流接口：少滤会碰到音频与按键接口。</summary>
    protected override bool IsCandidate(HidDeviceInfo info) =>
        info.VendorId == VendorId
        && info.ProductId == ReceiverPid
        && info.UsagePage == StatusUsagePage;

    // ------------------------------------------------------------------ 基类钩子

    /// <summary>见到任何报文即认领（心跳常驻在跑）；电量由后续推送帧补上。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(3);
        bool sawAny = false;
        while (DateTimeOffset.Now < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(200));
            if (input == null) continue;
            sawAny = true;

            var parsed = Cf2Codec.Parse(input);
            if (parsed is null) continue;              // 心跳帧：接口在位，等推送
            var (pct, charging, offline, _) = parsed.Value;
            if (offline) return ("cf2-ff90", null, ChargingState.Unknown);
            if (pct.HasValue) return ("cf2-ff90", pct, charging);
        }
        return sawAny ? ("cf2-ff90", null, ChargingState.Unknown) : null;
    }

    /// <summary>纯被动协议无查询可发；读数一律由后台监听线程经 StatusPushed 推送。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev) => null;

    /// <summary>被动帧解析（零写入）。电量帧不携带充电状态，须沿用设备当前值。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        var parsed = Cf2Codec.Parse(input);
        if (parsed is null)
            return dev.Status.BatteryPercent is null      // 还没读到电量：用心跳把在线状态翻正
                ? (dev.Status.BatteryPercent, dev.Status.Charging, Offline: false)
                : null;

        var (pct, charging, offline, carriesLevel) = parsed.Value;
        if (carriesLevel)
        {
            charging = pct >= 100 && dev.Status.Charging == ChargingState.Charging
                ? ChargingState.Full
                : dev.Status.Charging;
        }
        return (pct, charging, offline);
    }
}