using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Hecate;

/// <summary>
/// 功 能： HECATE 2.4G 鼠标插件：被动听状态帧 + 主动查询（写 0x04 查询帧）。
/// 2026/10/21 00:00:00 xliu
/// 会写帧故开关默认关闭，开启后两条路径同时生效；协议与离线判定见 docs/hardware/hecate.md §3。
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
    protected override bool ProbeWrites => true;
    protected override bool SupportsPassiveListen => true;

    /// <summary>只有心跳、没有电量帧时设备仍在；标离线会从列表里消失。</summary>
    protected override bool NullPercentMeansOffline => false;

    /// <summary>探针听到了报文但没有电量帧：设备无应答（关机/休眠/不在 2.4G）。
    /// 主动查询模式下下一轮轮询会自动重试，不再需要用户打开 Connect 唤醒。</summary>
    public const string DormantProtocol = "2.4G（未收到应答：设备可能关机或休眠，恢复后自动更新）";

    /// <summary>离线判定（GANSS 同款）：Silence = Zero，查询帧即存活探针——沉默判定既会被
    /// Connect 心跳污染、60s 查询周期≈60s 阈值又会横跳；连续 3 次无应答才离线（hecate.md §3）。</summary>
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

    // 查询时序与每轮次数见 HecateQueryBudget（CR-100：原来关机时每轮空等 ≈2.9 秒，堵住整轮 pass）。

    /// <summary>主动查询：按 <see cref="HecateQueryBudget.Attempts"/> 决定一轮问几次；仍无应答计一次轮询失败
    /// （鼠标关机/深度休眠/不在 2.4G）。半开探活失败由基类跳过记账（不升退避档位）。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        bool canWrite = CanWrite(dev);
        // 排查用：谁在什么时候触发本轮查询、熔断与失败计数的当时状态（默认 Info 不写）。
        if (Log.IsEnabled(LogLevel.Debug))
            Log.Debug($"HECATE 轮询上下文: 可写={canWrite} 半开探活={dev.HalfOpenProbe} 放行核实={dev.BypassActive} " +
                      $"停写={dev.WritesDisabled} 熔断档={dev.BreakerTrips} 连续失败={dev.ConsecutiveFailures}");
        if (!canWrite) return null;

        int attempts = HecateQueryBudget.Attempts(dev.HalfOpenProbe, dev.BypassActive, dev.ConsecutiveFailures);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            var result = QueryAsync(dev.Handle).GetAwaiter().GetResult();
            if (result.HasValue)
            {
                if (attempt > 1) Log.Info($"HECATE 第 {attempt} 次查询成功");
                elapsed.Stop();
                if (Log.IsEnabled(LogLevel.Debug))
                    Log.Debug($"HECATE 本轮查询耗时 {elapsed.ElapsedMilliseconds}ms（{attempt} 次，有应答）");
                ReportPollAnswered(dev);
                return result;
            }
            if (attempt < attempts) Thread.Sleep(HecateQueryBudget.RetryGap);
        }
        elapsed.Stop();

        // CR-100 的自查口径：日志级别开到"详细"时把本轮真实耗时打出来（默认 Info 不写，避免刷日志）。
        if (Log.IsEnabled(LogLevel.Debug))
            Log.Debug($"HECATE 本轮查询耗时 {elapsed.ElapsedMilliseconds}ms（{attempts} 次，无应答）");

        Log.Warn(dev.HalfOpenProbe
            ? "HECATE 半开探活无应答（设备仍在休眠/关机），保持离线"
            : $"HECATE 查询无应答（{attempts} 次，鼠标关机/深度休眠/不在 2.4G）");
        ReportPollFailure(dev);
        return null;
    }

    private async Task<(int? Percent, ChargingState Charging)?> QueryAsync(HidDeviceHandle handle)
    {
        if (!WritesEnabled || !IsCandidate(handle.Info)) return null;
        if (!handle.WriteOutput(HecateCodec.CreateQuery(), HecateQueryBudget.WriteTimeout))
            return null;
        var heard = await ListenAsync(handle, HecateQueryBudget.ListenWindow).ConfigureAwait(false);
        if (heard.Percent.HasValue) return (heard.Percent, heard.Charging);
        // 无应答的日志由 PollDevice 统一记一条（含本轮问了几次），这里不再逐次刷 WARN。
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
