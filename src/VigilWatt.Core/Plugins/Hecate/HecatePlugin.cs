using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Hecate;

/// <summary>
/// 功 能： HECATE 2.4G 鼠标插件：被动听状态帧 + 主动查询（写 0x04 查询帧，见 HecateCodec.CreateQuery）。
/// 2026/10/21 00:00:00 xliu
/// 主动查询使电量不依赖 HECATE Connect（Connect 关闭也能读），离线判定同时变为真实
/// （查询无应答 = 设备真的不在，不再被接收器会话心跳污染，见 issue #11 现象 1 残余形态）。
/// 会向设备写帧：品牌开关 PluginEnabled["Hecate"] 默认关闭（会写帧的插件按贡献指南
/// 默认关闭），用户在设置页手动开启后主动查询与被动监听同时生效。帧布局见 docs/hardware/hecate.md §1。
/// </summary>
public sealed class HecatePlugin : BatteryPluginBase
{
    public const ushort HecateVid = 0x35BB;
    public const ushort G5mProPid = 0xD00B;
    public const ushort VendorUsagePage = 0xFF1C;
    public const ushort VendorUsage = 0x0092;

    public override string Name => "Hecate";
    public override string DisplayName => "漫步者电竞";
    public override string DisplayNameEn => "HECATE";
    public override DeviceKind Kind => DeviceKind.Mouse;
    // 会写帧的插件不进 15 秒快速发现档（避免探测写入被抬到 4 倍频），走 ≤60 秒常规节拍。
    public override bool SupportsFastDiscovery => false;
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);
    protected override bool WritesEnabled => true;
    protected override bool ProbeWrites => true;
    protected override bool SupportsPassiveListen => true;

    /// <summary>只有心跳、没有电量帧时设备仍在；标离线会从列表里消失。</summary>
    protected override bool NullPercentMeansOffline => false;

    /// <summary>探针听到了报文但没有电量帧：设备无应答（关机/休眠/不在 2.4G）。
    /// 主动查询模式下下一轮轮询会自动重试，不再需要用户打开 Connect 唤醒。</summary>
    public const string DormantProtocol = "2.4G（未收到应答：设备可能关机或休眠，恢复后自动更新）";

    /// <summary>主动查询模式离线判定（GANSS 同款）：Silence = Zero——查询帧就是存活探针，
    /// 不再用沉默判定（Connect 关闭时查询周期 60s ≈ 沉默阈值会造成在线/离线横跳；
    /// Connect 开启时心跳又会污染沉默判定）。离线 = 连续 3 次查询无应答（MaxFailures=3），
    /// 约 3 个主轮询周期（≈3 分钟），行为确定、零横跳。深度休眠的鼠标也会在约 3 分钟后
    /// 显示离线，动一下鼠标即恢复——查询无应答与真实离线不可区分，固有限制取中庸。</summary>
    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == HecateVid && info.ProductId == G5mProPid
           && info.UsagePage == VendorUsagePage && info.Usage == VendorUsage
           && info.InputReportLength >= HecateCodec.ReportLength
           && info.OutputReportLength >= HecateCodec.ReportLength;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        // 主动查询先写一帧：Connect 关闭时被动流是静默的，查询让状态帧立刻回来，认领从
        // "6 秒等不到→休眠态" 变成 "亚秒级拿到真实电量"。
        if (WritesEnabled && ProbeWrites)
            handle.WriteOutput(HecateCodec.CreateQuery(), TimeSpan.FromMilliseconds(500));

        var heard = await ListenAsync(handle, TimeSpan.FromSeconds(6)).ConfigureAwait(false);
        if (heard.Percent.HasValue) return ("2.4G", heard.Percent, heard.Charging);
        if (!heard.AnyInput) return null;
        return (DormantProtocol, null, ChargingState.Unknown);
    }

    /// <summary>主动查询：连问 3 次（间隔 300ms）排除瞬态，仍无应答计一次轮询失败
    /// （鼠标关机/深度休眠/不在 2.4G）。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var result = QueryAsync(dev.Handle).GetAwaiter().GetResult();
            if (result.HasValue)
            {
                if (attempt > 1) Log.Info($"HECATE 第 {attempt} 次查询成功");
                ReportPollAnswered(dev);
                return result;
            }
            if (attempt < 3) Thread.Sleep(300);
        }
        Log.Warn("HECATE 连续 3 次查询无应答（鼠标关机/深度休眠/不在 2.4G）");
        ReportPollFailure(dev);
        return null;
    }

    private async Task<(int? Percent, ChargingState Charging)?> QueryAsync(HidDeviceHandle handle)
    {
        if (!WritesEnabled || !IsCandidate(handle.Info)) return null;
        if (!handle.WriteOutput(HecateCodec.CreateQuery(), TimeSpan.FromMilliseconds(500)))
            return null;
        var heard = await ListenAsync(handle, TimeSpan.FromMilliseconds(750)).ConfigureAwait(false);
        if (heard.Percent.HasValue) return (heard.Percent, heard.Charging);
        Log.Warn("HECATE 电量查询未收到有效响应（鼠标可能关机/休眠，动一下鼠标再试）");
        return null;
    }

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        var status = HecateCodec.Parse(input);
        if (status.Kind == HecateFrameKind.NotStatus)
        {
            if (Log.IsEnabled(LogLevel.Debug) && HecateCodec.IsHeartbeat(input))
                Log.Debug($"HECATE 心跳: {Log.Hex(input)}");
            return null;
        }

        if (dev.Status.Protocol != "2.4G")
            dev.Status.Protocol = "2.4G";

        Log.InfoOnChange($"hecate-status:{dev.Status.Id}",
            $"{status.ChargeFlag}|{status.Percent}|{status.Charging}",
            $"HECATE 状态帧: 电量={status.Percent}% 充电标志=0x{status.ChargeFlag:X2} " +
            $"充电状态={status.Charging} 原始={Log.Hex(input)}");

        if (status.Kind == HecateFrameKind.StatusInvalidPercent) return null;
        return (status.Percent, status.Charging, Offline: false);
    }

    private readonly record struct ProbeListen(bool AnyInput, int? Percent, ChargingState Charging);

    private static async Task<ProbeListen> ListenAsync(HidDeviceHandle handle, TimeSpan window)
    {
        bool anyInput = false;
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(500) ? remaining : TimeSpan.FromMilliseconds(500);
            if (slice <= TimeSpan.Zero) break;
            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            anyInput = true;
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"HECATE 收到报文: {Log.Hex(input)}");

            var status = HecateCodec.Parse(input);
            if (status.Kind == HecateFrameKind.Status)
                return new ProbeListen(true, status.Percent, status.Charging);
        }
        return new ProbeListen(anyInput, null, ChargingState.Unknown);
    }

    protected override string BuildDisplayName(HidDeviceInfo info)
        => string.IsNullOrWhiteSpace(info.Product) ? "HECATE G5M Pro" : info.Product;
}
