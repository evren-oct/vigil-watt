using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Flydigi;

/// <summary>
/// 功 能： 飞智 APEX5 / APEX6 2.4G 接收器电量插件（NewXInput 心跳）。
/// 2026/10/22 21:50:00 xliu
/// 2026/11/17 meish 扩展 APEX6（0x2502）。字节表、证据链见 docs/hardware/brands/flydigi.md。
/// </summary>
public sealed class FlydigiPlugin : BatteryPluginBase
{
    private const string ProtocolName = "flydigi-newxinput";

    public override string Name => "Flydigi";
    public override string DisplayName => "飞智";
    public override string DisplayNameEn => "FLYDIGI";
    public override DeviceKind Kind => DeviceKind.Gamepad;

    /// <summary>接收器常驻、手柄可能休眠或关机：查询失败 3 次判离线并熔断，退避期满自动半开重试。</summary>
    protected override OfflinePolicy Offline { get; } = new() { Silence = TimeSpan.Zero, CountPollFailures = true };

    /// <summary>开机就在充电时探测不到电量（档位被 0x11 顶替），此时设备仍算在线，界面显示 —。</summary>
    protected override bool NullPercentMeansOffline => false;

    /// <summary>必须小于 60 秒轮询周期，否则冷却相位让重试间隔翻倍（见基类注释）。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(30);

    /// <summary>认领的都是 2.4G 接收器接口（有线本体不走这条通道），链路可推定 2.4G；否则徽标先显示「未知」。</summary>
    protected override TelemetryValue<ConnectionKind> ConnectionOfProbe(HidDeviceInfo info)
        => TelemetryValue<ConnectionKind>.Inferred(ConnectionKind.Wireless24G, DateTimeOffset.Now);

    protected override bool IsCandidate(HidDeviceInfo info)
        => FlydigiCodec.IsSupportedInterface(info.VendorId, info.ProductId, info.UsagePage, info.Usage,
            info.InputReportLength, info.OutputReportLength);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var read = Query(handle, TimeSpan.FromSeconds(2));
        if (!read.HasValue) return null;
        return (ProtocolName, read.Value.Percent, MapCharging(read.Value));
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = Query(dev.Handle, TimeSpan.FromSeconds(1.5));
        if (!read.HasValue) return null;   // 基类按 CountPollFailures 记账，3 轮转离线

        // APEX5 充电时设备用 0x11 顶替了档位（Percent=null），真实电量无从得知：
        // 沿用上次采信值，只把状态改成充电中。APEX6 充电时仍带档位（Percent 有效），照实返回。
        if (read.Value.IsCharging && read.Value.Percent is null)
        {
            int last = dev.Filter.LastAcceptedPercent;
            return (last >= 0 ? last : null, ChargingState.Charging);
        }

        return (read.Value.Percent, MapCharging(read.Value));
    }

    private static ChargingState MapCharging(FlydigiCodec.Status status)
    {
        if (status.IsCharging) return ChargingState.Charging;
        // APEX6 已充满时 Percent 恒 100：走通用收尾规则把它归为 Full（未充电且 100% ⇒ 已充满）
        return ChargingStateMap.FromChargingFlag(false, status.Percent ?? 0);
    }

    /// <summary>发一条心跳并等应答；窗口内收到可解析帧即返回。</summary>
    private static FlydigiCodec.Status? Query(HidDeviceHandle handle, TimeSpan window)
    {
        var plan = ResolvePlan(handle);
        if (plan == null) return null;
        var (reportId, length) = plan.Value;

        var frame = FlydigiCodec.CreateHeartbeat(reportId, length);
        // 这台 dongle 的 WriteFile 路径可用（贡献者实测），不像 ATK 那台必须走 HidD_SetOutputReport
        if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500), quiet: true))
        {
            Log.Debug($"飞智 心跳写失败（report_id=0x{reportId:X2} len={length}）: {handle.Info.DevicePath}");
            return null;
        }

        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (FlydigiCodec.TryParse(input, out var status)) return status;
        }
        return null;
    }

    private readonly record struct QueryPlan(byte ReportId, int Length);

    private static readonly ConcurrentDictionary<string, QueryPlan> PlanByPath = new();

    /// <summary>report ID 与写长度取自该接口自己的 caps，硬编码必然写失败（北通/ATK 同款结论）。</summary>
    private static QueryPlan? ResolvePlan(HidDeviceHandle handle)
    {
        var path = handle.Info.DevicePath;
        if (PlanByPath.TryGetValue(path, out var cached)) return cached;

        byte reportId = handle.GetOutputReportId() ?? FlydigiCodec.FallbackWriteReportId;
        int length = handle.Info.OutputReportLength >= FlydigiCodec.HidReportLength
            ? handle.Info.OutputReportLength
            : FlydigiCodec.HidReportLength;

        var plan = new QueryPlan(reportId, length);
        Log.Debug($"飞智 接口参数: report_id=0x{reportId:X2} 写长度={length}");
        PlanByPath[path] = plan;
        return plan;
    }
}
